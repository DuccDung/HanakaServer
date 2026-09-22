using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HanakaServer.Controllers;
using HanakaServer.Helpers;
using HanakaServer.Models;
using HanakaServer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HanakaServer.Tests;

public sealed class CoordinatorStabilityTests
{
    [RelaySqlFact]
    public async Task Single_double_and_relay_share_preparation_and_zero_score_handoff_on_real_sql()
    {
        await using var host = await CoordinatorStabilityHost.StartAsync(2, 3, 4);
        var (first, token) = await host.LoginCoordinator(); using var coordinator = first;
        using var referee = await host.LoginReferee();
        for (var mode = 0; mode < 3; mode++)
        {
            var fixture = host.Tournaments[mode]; var matchId = fixture.Matches[0];
            await using (var setup = host.Db())
            {
                var tournament = await setup.Tournaments.SingleAsync(t => t.TournamentId == fixture.Id);
                tournament.GameType = mode == 0 ? "SINGLE" : "DOUBLE";
                if (mode == 2)
                {
                    setup.RelayTournamentSettings.Add(new() { TournamentId = fixture.Id, TeamSize = 6, TargetScore = 21 });
                    foreach (var registration in new[] { fixture.Team1, fixture.Team2 })
                    {
                        var team = new RelayTeam { TournamentId = fixture.Id, RegistrationId = registration, TeamName = $"Đội tiếp sức {registration}", Version = 1 };
                        foreach (var position in Enumerable.Range(1, 6)) team.Members.Add(new() { Position = position, DisplayName = $"Thành viên {position}" });
                        setup.RelayTeams.Add(team);
                    }
                }
                await setup.SaveChangesAsync();
            }
            using (var preparing = await CoordinatorStabilityHost.Write(coordinator, HttpMethod.Put, $"/api/coordinator-portal/matches/{matchId}",
                new { expectedVersion = 1, courtText = "Sân loại " + mode, preparing = true }, token)) preparing.EnsureSuccessStatusCode();
            object Payload(bool completed) => new { scoreTeam1 = completed ? 11 : 0, scoreTeam2 = completed ? 8 : 0, isCompleted = completed,
                relay = mode != 2 ? null : new { expectedVersion = completed ? 1 : 0, changedPart = 1, parts = new[] {
                    new { partNumber = 1, scoreTeam1 = completed ? 21 : 0, scoreTeam2 = completed ? 8 : 0 },
                    new { partNumber = 2, scoreTeam1 = 0, scoreTeam2 = 0 }, new { partNumber = 3, scoreTeam1 = 0, scoreTeam2 = 0 } } } };
            using (var zero = await referee.PutAsJsonAsync($"/api/referee/matches/{matchId}/score", Payload(false)))
            {
                zero.EnsureSuccessStatusCode(); var body = await zero.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("IN_PROGRESS", body.GetProperty("matchStatus").GetString()); Assert.Equal(0, body.GetProperty("scoreTeam1").GetInt32());
            }
            using (var locked = await CoordinatorStabilityHost.Write(coordinator, HttpMethod.Put, $"/api/coordinator-portal/matches/{matchId}",
                new { expectedVersion = 3, courtText = "Không được đổi", preparing = false }, token)) Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
            using (var completed = await referee.PutAsJsonAsync($"/api/referee/matches/{matchId}/score", Payload(true)))
            {
                completed.EnsureSuccessStatusCode(); var body = await completed.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("COMPLETED", body.GetProperty("matchStatus").GetString()); Assert.Equal(mode == 2 ? 21 : 11, body.GetProperty("scoreTeam1").GetInt32());
            }
        }
    }

    [RelaySqlFact]
    public async Task Expired_cookie_account_switch_old_csrf_and_feature_switch_keep_permissions_consistent()
    {
        await using var host = await CoordinatorStabilityHost.StartAsync(2);
        var fixture = host.Tournaments[0]; var matchId = fixture.Matches[0];
        var (first, oldToken) = await host.LoginCoordinator(); using var client = first;
        using (var logout = await CoordinatorStabilityHost.Write(client, HttpMethod.Post, "/api/coordinator-portal/logout", new { }, oldToken)) logout.EnsureSuccessStatusCode();
        var anonymousToken = await CoordinatorStabilityHost.Token(client, "/CoordinatorPortal/Login");
        using (var login = await CoordinatorStabilityHost.Write(client, HttpMethod.Post, "/api/coordinator-portal/login",
            new { account = "coordinator1@example.test", password = CoordinatorStabilityHost.Password }, anonymousToken)) login.EnsureSuccessStatusCode();
        var path = $"/api/coordinator-portal/matches/{matchId}";
        var payload = new { expectedVersion = 1, courtText = "Sân tài khoản mới", preparing = true };
        using (var crossAccount = await CoordinatorStabilityHost.Write(client, HttpMethod.Put, path, payload, oldToken))
            Assert.Equal(HttpStatusCode.BadRequest, crossAccount.StatusCode);
        var token = await CoordinatorStabilityHost.Token(client, "/CoordinatorPortal/Matches");
        using (var saved = await CoordinatorStabilityHost.Write(client, HttpMethod.Put, path, payload, token)) saved.EnsureSuccessStatusCode();
        await using (var db = host.Db()) Assert.Equal(host.Coordinators[1], (await db.MatchCoordinationHistories.SingleAsync()).ActorUserId);
        host.App.Configuration["Coordination:Enabled"] = "false";
        using (var session = await client.GetAsync("/api/coordinator-portal/session")) Assert.Equal(HttpStatusCode.Forbidden, session.StatusCode);
        using (var disabled = await CoordinatorStabilityHost.Write(client, HttpMethod.Put, path, payload, token)) Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode);
        using (var schedule = await client.GetAsync($"/api/tournaments/{fixture.Id}/rounds-with-matches")) schedule.EnsureSuccessStatusCode();
        using (var referee = await host.LoginReferee())
        using (var score = await referee.PutAsJsonAsync($"/api/referee/matches/{matchId}/score", new { scoreTeam1 = 0, scoreTeam2 = 0, isCompleted = false })) score.EnsureSuccessStatusCode();
        host.App.Configuration["Coordination:Enabled"] = "true";
        var options = host.App.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CoordinatorPortalAuthentication.Scheme);
        options.TimeProvider = new ExpiredClock();
        using var expired = await client.GetAsync("/api/coordinator-portal/session");
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Contains("application/json", expired.Content.Headers.ContentType!.MediaType!);
    }

    private sealed class ExpiredClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddHours(9);
    }

    [RelaySqlFact]
    public async Task Restart_preserves_committed_court_status_cookie_and_resubscribed_delivery()
    {
        await using var host = await CoordinatorStabilityHost.StartAsync(2);
        var fixture = host.Tournaments[0]; var id = fixture.Matches[0];
        var (first, token) = await host.LoginCoordinator(); using var client = first;
        using (var saved = await CoordinatorStabilityHost.Write(client, HttpMethod.Put, $"/api/coordinator-portal/matches/{id}",
            new { expectedVersion = 1, courtText = "Sân trước restart", preparing = true }, token)) saved.EnsureSuccessStatusCode();
        await host.RestartAsync();
        using (var session = await client.GetAsync("/api/coordinator-portal/session")) session.EnsureSuccessStatusCode();
        var schedule = await client.GetFromJsonAsync<JsonElement>($"/api/tournaments/{fixture.Id}/rounds-with-matches");
        var restored = CoordinatorStabilityHost.Matches(schedule).Single(m => m.GetProperty("matchId").GetInt64() == id);
        Assert.Equal("PREPARING", restored.GetProperty("matchStatus").GetString()); Assert.Equal("Sân trước restart", restored.GetProperty("courtText").GetString());
        using var socket = new ClientWebSocket(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Subscribe(socket, host.Url, fixture.Id, timeout.Token);
        using (var saved = await CoordinatorStabilityHost.Write(client, HttpMethod.Put, $"/api/coordinator-portal/matches/{id}",
            new { expectedVersion = 2, courtText = "Sân sau restart", preparing = false }, token)) saved.EnsureSuccessStatusCode();
        Assert.Equal("NOT_STARTED", (await Event(socket, "tournament.match.coordination.updated", timeout.Token)).GetProperty("payload").GetProperty("matchStatus").GetString());
        socket.Abort();
        await using var db = host.Db(); Assert.Equal(2, await db.MatchCoordinationHistories.CountAsync());
    }

    [RelaySqlFact]
    public async Task Migration_backfills_zero_score_history_completed_and_idle_matches_and_is_repeatable()
    {
        await using var host = await CoordinatorStabilityHost.StartAsync(6);
        var ids = host.Tournaments[0].Matches;
        await host.Sandbox.SqlAsync($"""
            UPDATE dbo.TournamentGroupMatches SET ScoreTeam1=11,ScoreTeam2=8,IsCompleted=1 WHERE MatchId={ids[1]};
            UPDATE dbo.TournamentGroupMatches SET ScoreTeam1=1 WHERE MatchId={ids[2]};
            UPDATE dbo.TournamentGroupMatches SET UpdatedAt='2026-09-22T08:00:00' WHERE MatchId IN ({ids[3]},{ids[4]});
            INSERT dbo.TournamentMatchScoreHistories(MatchId,RefereeUserId,ScoreTeam1,ScoreTeam2,IsCompleted,CreatedAt)
            VALUES({ids[3]},{host.Referees[3]},0,0,0,'2026-09-22T08:01:00'),({ids[4]},{host.Referees[4]},0,0,0,'2026-09-22T07:59:00');
            UPDATE dbo.TournamentGroupMatches SET IsCompleted=1,CompletionReason='BYE',Team2RegistrationId=NULL WHERE MatchId={ids[5]};
            DROP TABLE dbo.MatchCoordinationHistories;
            DROP TABLE dbo.TournamentCoordinators;
            DECLARE @constraintName sysname;
            SELECT @constraintName = dc.name FROM sys.default_constraints dc JOIN sys.columns c
            ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id
            WHERE dc.parent_object_id=OBJECT_ID('dbo.TournamentGroupMatches') AND c.name='MatchStatus';
            IF @constraintName IS NOT NULL EXEC('ALTER TABLE dbo.TournamentGroupMatches DROP CONSTRAINT ['+@constraintName+']');
            ALTER TABLE dbo.TournamentGroupMatches DROP COLUMN MatchStatus, StateVersion;
            """);
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "20260922_add_match_coordination.sql"));
        await host.Sandbox.SqlAsync(script); await host.Sandbox.SqlAsync(script);
        await using var db = host.Db();
        var rows = await db.TournamentGroupMatches.OrderBy(m => m.MatchId).ToListAsync();
        Assert.Equal(new[] { "NOT_STARTED", "COMPLETED", "IN_PROGRESS", "IN_PROGRESS", "NOT_STARTED", "COMPLETED" }, rows.Select(m => m.MatchStatus));
        Assert.All(rows, m => Assert.Equal(1, m.StateVersion));
        Assert.Equal(11, rows[1].ScoreTeam1); Assert.Equal(8, rows[1].ScoreTeam2);
        Assert.Equal(2, await db.TournamentMatchScoreHistories.CountAsync());
        Assert.Empty(await db.TournamentCoordinators.ToListAsync());
    }

    [RelaySqlFact]
    public async Task Schedule_sort_keeps_null_times_last_and_cancelled_reads_do_not_poison_following_requests()
    {
        await using var host = await CoordinatorStabilityHost.StartAsync(5);
        var fixture = host.Tournaments[0];
        await using (var db = host.Db())
        {
            var rows = await db.TournamentGroupMatches.OrderBy(m => m.MatchId).ToListAsync();
            rows[0].StartAt = null; rows[1].StartAt = DateTime.Today.AddHours(9);
            rows[2].StartAt = DateTime.Today.AddHours(7); rows[3].StartAt = rows[2].StartAt;
            await db.SaveChangesAsync();
        }
        using var client = host.Client(); var path = $"/api/tournaments/{fixture.Id}/rounds-with-matches";
        await using (var gate = host.Db())
        await using (var tx = await gate.Database.BeginTransactionAsync())
        {
            var id = fixture.Matches[0];
            await gate.Database.ExecuteSqlInterpolatedAsync($"SELECT MatchId FROM dbo.TournamentGroupMatches WITH (XLOCK,HOLDLOCK) WHERE MatchId={id}");
            using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync(path, cancelled.Token));
            await tx.CommitAsync();
        }
        var schedule = await client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(new[] { fixture.Matches[2], fixture.Matches[3], fixture.Matches[4], fixture.Matches[1], fixture.Matches[0] },
            CoordinatorStabilityHost.Matches(schedule).Select(m => m.GetProperty("matchId").GetInt64()));
    }

    [RelaySqlFact]
    public async Task Real_portal_sql_websocket_and_referee_complete_one_lifecycle()
    {
        await using var host = await CoordinatorStabilityHost.StartAsync(30);
        var fixture = host.Tournaments[0]; var matchId = fixture.Matches[0];
        var (client, token) = await host.LoginCoordinator(); using var coordinator = client;
        using var referee = await host.LoginReferee(); using var viewer = host.Client();
        using var socket = new ClientWebSocket();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await Subscribe(socket, host.Url, fixture.Id, timeout.Token);
        using var prepare = await CoordinatorStabilityHost.Write(coordinator, HttpMethod.Put, $"/api/coordinator-portal/matches/{matchId}",
            new { expectedVersion = 1, courtText = "Sân tích hợp", preparing = true }, token);
        prepare.EnsureSuccessStatusCode();
        var prepared = await Event(socket, "tournament.match.coordination.updated", timeout.Token);
        Assert.Equal("PREPARING", prepared.GetProperty("payload").GetProperty("matchStatus").GetString());
        var schedule = await viewer.GetFromJsonAsync<JsonElement>($"/api/tournaments/{fixture.Id}/rounds-with-matches");
        Assert.Equal("Sân tích hợp", CoordinatorStabilityHost.Matches(schedule).Single(m => m.GetProperty("matchId").GetInt64() == matchId).GetProperty("courtText").GetString());
        using var score = await referee.PutAsJsonAsync($"/api/referee/matches/{matchId}/score", new { scoreTeam1 = 0, scoreTeam2 = 0, isCompleted = false });
        score.EnsureSuccessStatusCode();
        var running = await Event(socket, "tournament.match.score.updated", timeout.Token);
        Assert.Equal("IN_PROGRESS", running.GetProperty("payload").GetProperty("matchStatus").GetString());
        using var illegal = await CoordinatorStabilityHost.Write(coordinator, HttpMethod.Put, $"/api/coordinator-portal/matches/{matchId}",
            new { expectedVersion = 3, courtText = "Sai sân", preparing = false }, token);
        Assert.Equal(HttpStatusCode.Conflict, illegal.StatusCode);
        using var completed = await referee.PutAsJsonAsync($"/api/referee/matches/{matchId}/score", new { scoreTeam1 = 11, scoreTeam2 = 8, isCompleted = true });
        completed.EnsureSuccessStatusCode();
        var ended = await Event(socket, "tournament.match.score.updated", timeout.Token);
        Assert.Equal("COMPLETED", ended.GetProperty("payload").GetProperty("matchStatus").GetString());
        // A disconnected viewer recovers from authoritative REST and resubscribes on the same host.
        socket.Abort();
        using var reconnected = new ClientWebSocket(); await Subscribe(reconnected, host.Url, fixture.Id, timeout.Token);
        schedule = await viewer.GetFromJsonAsync<JsonElement>($"/api/tournaments/{fixture.Id}/rounds-with-matches");
        var final = CoordinatorStabilityHost.Matches(schedule).Single(m => m.GetProperty("matchId").GetInt64() == matchId);
        Assert.Equal(11, final.GetProperty("scoreTeam1").GetInt32()); Assert.Equal("COMPLETED", final.GetProperty("matchStatus").GetString());
        using var reopen = await referee.PutAsJsonAsync($"/api/referee/matches/{matchId}/score", new { scoreTeam1 = 0, scoreTeam2 = 0, isCompleted = false });
        reopen.EnsureSuccessStatusCode();
        Assert.Equal("IN_PROGRESS", (await Event(reconnected, "tournament.match.score.updated", timeout.Token)).GetProperty("payload").GetProperty("matchStatus").GetString());
        reconnected.Abort();
        await using var db = host.Db(); Assert.Single(await db.MatchCoordinationHistories.ToListAsync());
        Assert.Equal(3, await db.TournamentMatchScoreHistories.CountAsync());
    }

    [RelaySqlFact]
    public async Task One_hundred_simultaneous_dispatch_and_score_requests_preserve_score_and_audit()
    {
        await using var host = await CoordinatorStabilityHost.StartAsync(100);
        var fixture = host.Tournaments[0];
        var (client, token) = await host.LoginCoordinator(); using var coordinator = client;
        var referees = await Task.WhenAll(Enumerable.Range(0, 10).Select(host.LoginReferee));
        try
        {
            for (var i = 0; i < 100; i++)
            {
                var id = fixture.Matches[i];
                // Both HTTP requests reach SQL while another transaction holds the parent lock.
                await using var gate = host.Db(); await using var tx = await gate.Database.BeginTransactionAsync();
                await gate.Database.ExecuteSqlInterpolatedAsync($"SELECT MatchId FROM dbo.TournamentGroupMatches WITH (UPDLOCK, HOLDLOCK) WHERE MatchId = {id}");
                var dispatch = CoordinatorStabilityHost.Write(coordinator, HttpMethod.Put, $"/api/coordinator-portal/matches/{id}",
                    new { expectedVersion = 1, courtText = "Sân cạnh tranh", preparing = true }, token);
                var score = referees[i % 10].PutAsJsonAsync($"/api/referee/matches/{id}/score", new { scoreTeam1 = 1, scoreTeam2 = 0, isCompleted = false });
                await Task.Delay(25); await tx.CommitAsync();
                using var d = await dispatch; using var s = await score;
                s.EnsureSuccessStatusCode(); Assert.Contains(d.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
                await using var verify = host.Db(); var match = await verify.TournamentGroupMatches.SingleAsync(m => m.MatchId == id);
                Assert.Equal(MatchStatuses.InProgress, match.MatchStatus); Assert.Equal(1, match.ScoreTeam1);
                Assert.Equal(d.IsSuccessStatusCode ? 1 : 0, await verify.MatchCoordinationHistories.CountAsync(h => h.MatchId == id));
                Assert.Equal(d.IsSuccessStatusCode ? 3 : 2, match.StateVersion);
            }
        }
        finally { foreach (var referee in referees) referee.Dispose(); }
    }

    [RelaySqlFact]
    public async Task Revocation_committing_before_waiting_dispatch_prevents_a_write_and_audit_failure_rolls_back()
    {
        await using var host = await CoordinatorStabilityHost.StartAsync(2);
        var fixture = host.Tournaments[0]; var id = fixture.Matches[0];
        var (client, token) = await host.LoginCoordinator(); using var coordinator = client;
        await using (var gate = host.Db())
        await using (var tx = await gate.Database.BeginTransactionAsync())
        {
            await gate.Database.ExecuteSqlInterpolatedAsync($"SELECT MatchId FROM dbo.TournamentGroupMatches WITH (UPDLOCK,HOLDLOCK) WHERE MatchId={id}");
            var waiting = CoordinatorStabilityHost.Write(coordinator, HttpMethod.Put, $"/api/coordinator-portal/matches/{id}",
                new { expectedVersion = 1, courtText = "Sân bị thu hồi", preparing = true }, token);
            await Task.Delay(50);
            await using var revoke = host.Db();
            await new AdminTournamentCoordinatorsController(revoke).Revoke(fixture.Id, host.Coordinators[0], default);
            await tx.CommitAsync();
            using var result = await waiting; Assert.Contains(result.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized });
        }
        await host.Sandbox.SqlAsync("CREATE TRIGGER dbo.RejectCoordinationAudit ON dbo.MatchCoordinationHistories AFTER INSERT AS THROW 51998, 'Intentional coordination audit failure', 1;");
        var (second, secondToken) = await host.LoginCoordinator(1); using var secondClient = second;
        using var failed = await CoordinatorStabilityHost.Write(secondClient, HttpMethod.Put, $"/api/coordinator-portal/matches/{id}",
            new { expectedVersion = 1, courtText = "Sân rollback", preparing = true }, secondToken);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        await using var verify = host.Db(); var match = await verify.TournamentGroupMatches.SingleAsync(m => m.MatchId == id);
        Assert.Equal(1, match.StateVersion); Assert.Equal(MatchStatuses.NotStarted, match.MatchStatus);
        Assert.Equal("Sân 1", match.CourtText); Assert.Empty(await verify.MatchCoordinationHistories.ToListAsync());
    }

    internal static async Task Subscribe(ClientWebSocket socket, string url, long tournamentId, CancellationToken ct)
    {
        await socket.ConnectAsync(new Uri(url.Replace("http://", "ws://") + "/ws-public"), ct);
        await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "tournament.subscribe", tournamentId })), WebSocketMessageType.Text, true, ct);
        await Event(socket, "tournament.subscribed", ct);
    }
    internal static async Task<JsonElement> Event(ClientWebSocket socket, string type, CancellationToken ct)
    {
        while (true)
        {
            var buffer = new byte[8192]; using var content = new MemoryStream();
            WebSocketReceiveResult part;
            do { part = await socket.ReceiveAsync(buffer, ct); if (part.MessageType == WebSocketMessageType.Close) throw new IOException("Socket closed before expected event."); content.Write(buffer, 0, part.Count); } while (!part.EndOfMessage);
            using var json = JsonDocument.Parse(content.ToArray());
            if (json.RootElement.GetProperty("type").GetString() == type) return json.RootElement.Clone();
        }
    }
}
