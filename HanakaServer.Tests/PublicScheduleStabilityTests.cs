using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HanakaServer.Controllers;
using HanakaServer.Models;
using HanakaServer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace HanakaServer.Tests;

public sealed class PublicScheduleStabilityTests
{
    [RelaySqlFact]
    public async Task Real_web_login_razor_csrf_account_switch_revocation_expiry_and_restart()
    {
        await using var host = await CoordinatorStabilityHost.StartAsync(2, 3);
        var fixture = host.Tournaments[0]; var id = fixture.Matches[0];
        var permissionsPath = $"/api/coordination/tournaments/{fixture.Id}/permissions";
        var path = $"/api/coordination/matches/{id}";
        var (first, oldToken) = await host.LoginWebCoordinator(); using var web = first;
        var page = await web.GetStringAsync($"/PickleballWeb/Tournament/{fixture.Id}/Schedule");
        Assert.Contains("tournament-schedule.js", page); Assert.Contains("tournament-schedule.css", page);
        Assert.Contains("data-detail-kind=\"tournament-schedule-page\"", page);
        using (var script = await web.GetAsync("/pickleball-web/js/tournament-schedule.js")) script.EnsureSuccessStatusCode();
        var session = await web.GetFromJsonAsync<JsonElement>("/api/web-auth/me");
        Assert.True(session.GetProperty("isAuthenticated").GetBoolean());
        using (var missingCsrf = await web.PutAsJsonAsync(path, new { expectedVersion = 1, courtText = "Sân sai", preparing = true }))
            Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        using (var switched = await web.PostAsJsonAsync("/api/web-auth/login", new { identifier = "coordinator1@example.test", password = CoordinatorStabilityHost.Password })) switched.EnsureSuccessStatusCode();
        using (var staleCsrf = await CoordinatorStabilityHost.Write(web, HttpMethod.Put, path, new { expectedVersion = 1, courtText = "Sân sai", preparing = true }, oldToken))
            Assert.Equal(HttpStatusCode.BadRequest, staleCsrf.StatusCode);
        var permissions = await web.GetFromJsonAsync<JsonElement>(permissionsPath);
        var token = permissions.GetProperty("requestToken").GetString()!;
        Assert.Equal(host.Coordinators[1], permissions.GetProperty("userId").GetInt64());
        using (var saved = await CoordinatorStabilityHost.Write(web, HttpMethod.Put, path, new { expectedVersion = 1, courtText = "Sân web", preparing = true }, token)) saved.EnsureSuccessStatusCode();
        // Saving an unchanged form is a successful no-op, with no duplicate audit/version.
        using (var unchanged = await CoordinatorStabilityHost.Write(web, HttpMethod.Put, path, new { expectedVersion = 2, courtText = "Sân web", preparing = true }, token))
        {
            unchanged.EnsureSuccessStatusCode();
            Assert.Equal(2, (await unchanged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("stateVersion").GetInt64());
        }
        await host.RestartAsync();
        Assert.True((await web.GetFromJsonAsync<JsonElement>(permissionsPath)).GetProperty("canCoordinate").GetBoolean());
        using var socket = new ClientWebSocket(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await CoordinatorStabilityTests.Subscribe(socket, host.Url, fixture.Id, timeout.Token);
        using (var saved = await CoordinatorStabilityHost.Write(web, HttpMethod.Put, path, new { expectedVersion = 2, courtText = "Sân sau restart", preparing = false }, token)) saved.EnsureSuccessStatusCode();
        var received = await CoordinatorStabilityTests.Event(socket, "tournament.match.coordination.updated", timeout.Token);
        Assert.Equal(3, received.GetProperty("payload").GetProperty("stateVersion").GetInt64()); socket.Abort();
        host.App.Configuration["Coordination:Enabled"] = "false";
        Assert.False((await web.GetFromJsonAsync<JsonElement>(permissionsPath)).GetProperty("canCoordinate").GetBoolean());
        using (var disabled = await CoordinatorStabilityHost.Write(web, HttpMethod.Put, path, new { expectedVersion = 3, courtText = "Sân cấm", preparing = true }, token))
            Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode);
        host.App.Configuration["Coordination:Enabled"] = "true";
        await using (var db = host.Db()) await new AdminTournamentCoordinatorsController(db).Revoke(fixture.Id, host.Coordinators[1], default);
        Assert.False((await web.GetFromJsonAsync<JsonElement>(permissionsPath)).GetProperty("canCoordinate").GetBoolean());
        Assert.True((await web.GetFromJsonAsync<JsonElement>($"/api/coordination/tournaments/{host.Tournaments[1].Id}/permissions")).GetProperty("canCoordinate").GetBoolean());
        using (var revoked = await CoordinatorStabilityHost.Write(web, HttpMethod.Put, path, new { expectedVersion = 3, courtText = "Sân cấm", preparing = true }, token))
            Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        await using (var db = host.Db()) { var user = await db.Users.SingleAsync(u => u.UserId == host.Coordinators[1]); user.IsActive = false; await db.SaveChangesAsync(); }
        Assert.False((await web.GetFromJsonAsync<JsonElement>($"/api/coordination/tournaments/{host.Tournaments[1].Id}/permissions")).GetProperty("canCoordinate").GetBoolean());
        using var expired = host.Client();
        var expiredJwt = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("coordination-test", "coordination-test",
            [new Claim("uid", host.Coordinators[0].ToString())], DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(-1),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(CoordinatorStabilityHost.JwtKey)), SecurityAlgorithms.HmacSha256)));
        expired.DefaultRequestHeaders.Add("Cookie", WebAuthCookieService.AccessTokenCookieName + "=" + expiredJwt);
        using (var result = await expired.GetAsync(permissionsPath)) Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        using (var publicSchedule = await expired.GetAsync($"/api/tournaments/{fixture.Id}/rounds-with-matches")) publicSchedule.EnsureSuccessStatusCode();
        await using var verify = host.Db();
        Assert.Equal(2, await verify.MatchCoordinationHistories.CountAsync());
        Assert.All(await verify.MatchCoordinationHistories.ToListAsync(), row => Assert.Equal(host.Coordinators[1], row.ActorUserId));
        Assert.Empty(await verify.TournamentMatchScoreHistories.ToListAsync());
    }

    [RelaySqlFact]
    public async Task One_hundred_races_each_between_web_operators_scoring_and_revocation_preserve_sql_and_audit()
    {
        await using var host = await CoordinatorStabilityHost.StartAsync(300);
        var fixture = host.Tournaments[0];
        var (a, tokenA) = await host.LoginWebCoordinator(); using var first = a;
        var (b, tokenB) = await host.LoginWebCoordinator(1); using var second = b;
        var referees = await Task.WhenAll(Enumerable.Range(0, 10).Select(host.LoginReferee));
        try
        {
            for (var i = 0; i < 300; i++)
            {
                var id = fixture.Matches[i]; var path = $"/api/coordination/matches/{id}";
                await using var gate = host.Db(); await using var tx = await gate.Database.BeginTransactionAsync();
                await gate.Database.ExecuteSqlInterpolatedAsync($"SELECT MatchId FROM dbo.TournamentGroupMatches WITH (UPDLOCK,HOLDLOCK) WHERE MatchId={id}");
                var dispatch = CoordinatorStabilityHost.Write(first, HttpMethod.Put, path, new { expectedVersion = 1, courtText = "Sân A", preparing = true }, tokenA);
                Task<HttpResponseMessage>? other = i < 100
                    ? CoordinatorStabilityHost.Write(second, HttpMethod.Put, path, new { expectedVersion = 1, courtText = "Sân B", preparing = true }, tokenB)
                    : i < 200 ? referees[i % 10].PutAsJsonAsync($"/api/referee/matches/{id}/score", new { scoreTeam1 = 0, scoreTeam2 = 0, isCompleted = false }) : null;
                await Task.Delay(30);
                if (i >= 200) { await using var revoke = host.Db(); await new AdminTournamentCoordinatorsController(revoke).Revoke(fixture.Id, host.Coordinators[0], default); }
                await tx.CommitAsync();
                using var result = await dispatch; using var concurrent = other == null ? null : await other;
                await using var verify = host.Db(); var match = await verify.TournamentGroupMatches.SingleAsync(m => m.MatchId == id);
                var audits = await verify.MatchCoordinationHistories.Where(h => h.MatchId == id).ToListAsync();
                if (i < 100)
                {
                    Assert.Equal(1, new[] { result, concurrent! }.Count(r => r.IsSuccessStatusCode));
                    Assert.Equal(1, new[] { result, concurrent! }.Count(r => r.StatusCode == HttpStatusCode.Conflict));
                    Assert.Single(audits); Assert.Equal(2, match.StateVersion); Assert.Equal(MatchStatuses.Preparing, match.MatchStatus);
                    Assert.Equal(result.IsSuccessStatusCode ? "Sân A" : "Sân B", match.CourtText);
                }
                else if (i < 200)
                {
                    concurrent!.EnsureSuccessStatusCode(); Assert.Contains(result.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
                    Assert.Equal(MatchStatuses.InProgress, match.MatchStatus);
                    Assert.Equal(result.IsSuccessStatusCode ? 1 : 0, audits.Count);
                    Assert.Equal(result.IsSuccessStatusCode ? 3 : 2, match.StateVersion);
                    Assert.Equal(1, await verify.TournamentMatchScoreHistories.CountAsync(h => h.MatchId == id));
                }
                else
                {
                    Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode); Assert.Empty(audits);
                    Assert.Equal(1, match.StateVersion); Assert.Equal(MatchStatuses.NotStarted, match.MatchStatus);
                    await new AdminTournamentCoordinatorsController(verify).Assign(fixture.Id, host.Coordinators[0], default);
                }
                Assert.Equal(0, match.ScoreTeam1); Assert.Equal(0, match.ScoreTeam2);
            }
        }
        finally { foreach (var referee in referees) referee.Dispose(); }
    }
}
