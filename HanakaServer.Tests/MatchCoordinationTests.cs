using System.Security.Claims;
using System.Text.Json;
using HanakaServer.Controllers;
using HanakaServer.Data;
using HanakaServer.Helpers;
using HanakaServer.Models;
using HanakaServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using System.Net;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HanakaServer.Tests;

public sealed class MatchCoordinationTests
{
    [Fact]
    public async Task Http_uses_existing_uid_only_tokens_enforces_permissions_and_exposes_public_status()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("coordination-integration-tests-only-key-2026"));
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var database = "coordination-http-" + Guid.NewGuid();
        builder.Services.AddDbContext<PickleballDbContext>(options => options.UseInMemoryDatabase(database));
        builder.Services.AddSingleton<PublicRealtimeHub>();
        builder.Services.AddScoped<MatchCoordinationService>();
        builder.Services.AddScoped<ITournamentStandingsService, TournamentStandingsService>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
            options.TokenValidationParameters = new() { ValidateIssuer = false, ValidateAudience = false,
                IssuerSigningKey = key, ValidateLifetime = true, ClockSkew = TimeSpan.Zero });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(MatchCoordinationController).Assembly);
        await using var app = builder.Build();
        app.UseMiddleware<HanakaServer.Middleware.MatchConcurrencyMiddleware>();
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        Fixture f;
        await using (var scope = app.Services.CreateAsyncScope()) f = await Seed(scope.ServiceProvider.GetRequiredService<PickleballDbContext>());
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            string Token(long id, bool admin = false) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                claims: admin ? [new Claim("uid", id.ToString()), new Claim(ClaimTypes.Role, "Admin")] : [new Claim("uid", id.ToString())],
                expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));
            var path = $"/api/coordination/matches/{f.MatchId}";
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync(path, new { expectedVersion = 1, courtText = "Sân 1", preparing = true })).StatusCode);
            client.DefaultRequestHeaders.Authorization = new("Bearer", Token(f.UserId + 50));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(path, new { expectedVersion = 1, courtText = "Sân 1", preparing = true })).StatusCode);
            var operatorToken = Token(f.UserId);
            client.DefaultRequestHeaders.Authorization = new("Bearer", operatorToken);
            var response = await client.PutAsJsonAsync(path, new { expectedVersion = 1, courtText = "Sân 2", preparing = true, scoreTeam1 = 99, isCompleted = true });
            response.EnsureSuccessStatusCode();
            var snapshot = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(0, snapshot.GetProperty("scoreTeam1").GetInt32());
            Assert.False(snapshot.GetProperty("isCompleted").GetBoolean());
            Assert.Equal("PREPARING", snapshot.GetProperty("matchStatus").GetString());
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(path, new { expectedVersion = 1, courtText = "Sân 3", preparing = true })).StatusCode);
            client.DefaultRequestHeaders.Authorization = null;
            var schedule = await client.GetFromJsonAsync<JsonElement>($"/api/tournaments/{f.TournamentId}/rounds-with-matches");
            var match = schedule.GetProperty("rounds")[0].GetProperty("groups")[0].GetProperty("matches")[0];
            Assert.Equal("PREPARING", match.GetProperty("matchStatus").GetString());
            Assert.Equal(2, match.GetProperty("stateVersion").GetInt64());
            client.DefaultRequestHeaders.Authorization = new("Bearer", Token(f.UserId, true));
            (await client.DeleteAsync($"/api/admin/tournaments/{f.TournamentId}/coordinators/{f.UserId}")).EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Authorization = new("Bearer", operatorToken);
            var permissions = await client.GetFromJsonAsync<JsonElement>($"/api/coordination/tournaments/{f.TournamentId}/permissions");
            Assert.False(permissions.GetProperty("canCoordinate").GetBoolean());
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(path, new { expectedVersion = 2, courtText = "Sân 3", preparing = false })).StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    private static PickleballDbContext Memory() => new(new DbContextOptionsBuilder<PickleballDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static MatchCoordinationService Service(PickleballDbContext db, bool enabled = true) => new(db,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Coordination:Enabled"] = enabled.ToString() }).Build(),
        new PublicRealtimeHub(NullLogger<PublicRealtimeHub>.Instance), NullLogger<MatchCoordinationService>.Instance);

    [Fact]
    public async Task Preparing_court_changes_and_cancel_are_persisted_with_audit_and_no_score_changes()
    {
        await using var db = Memory();
        var f = await Seed(db);
        var service = Service(db);
        Assert.True(await service.CanCoordinateAsync(f.UserId, f.TournamentId));
        var snapshot = Json(await service.UpdateAsync(f.UserId, f.MatchId, new() { ExpectedVersion = 1, CourtText = " Sân 2 ", Preparing = true }, default));
        Assert.Equal("PREPARING", snapshot.GetProperty("matchStatus").GetString());
        Assert.Equal(2, snapshot.GetProperty("stateVersion").GetInt64());
        var match = await db.TournamentGroupMatches.SingleAsync();
        Assert.Equal("Sân 2", match.CourtText);
        Assert.Equal(0, match.ScoreTeam1);
        Assert.False(match.IsCompleted);
        Assert.Empty(await db.TournamentMatchScoreHistories.ToListAsync());
        await service.UpdateAsync(f.UserId, f.MatchId, new() { ExpectedVersion = 2, CourtText = "Sân 3", Preparing = false }, default);
        Assert.Equal(MatchStatuses.NotStarted, match.MatchStatus);
        Assert.Equal(2, await db.MatchCoordinationHistories.CountAsync());
        Assert.Equal("Sân 2", (await db.MatchCoordinationHistories.OrderBy(x => x.Id).LastAsync()).PreviousCourt);
    }

    [Fact]
    public async Task Live_assignment_active_account_and_feature_switch_are_required()
    {
        await using var db = Memory(); var f = await Seed(db); var service = Service(db);
        Assert.False(await service.CanCoordinateAsync(f.UserId, f.TournamentId + 1));
        Assert.False(await service.CanCoordinateAsync(f.UserId + 10, f.TournamentId));
        Assert.False(await Service(db, false).CanCoordinateAsync(f.UserId, f.TournamentId));
        var request = new CoordinateMatchRequest { ExpectedVersion = 1, CourtText = "Sân 1", Preparing = true };
        Assert.Equal(403, (await Assert.ThrowsAsync<CoordinationException>(() => service.UpdateAsync(f.UserId + 10, f.MatchId, request, default))).Status);
        await new AdminTournamentCoordinatorsController(db).Revoke(f.TournamentId, f.UserId, default);
        Assert.False(await service.CanCoordinateAsync(f.UserId, f.TournamentId));
        Assert.Equal(403, (await Assert.ThrowsAsync<CoordinationException>(() => service.UpdateAsync(f.UserId, f.MatchId, request, default))).Status);
        await new AdminTournamentCoordinatorsController(db).Assign(f.TournamentId, f.UserId, default);
        (await db.Users.SingleAsync()).IsActive = false; await db.SaveChangesAsync();
        Assert.False(await service.CanCoordinateAsync(f.UserId, f.TournamentId));
        Assert.Empty(await db.MatchCoordinationHistories.ToListAsync());
    }

    [Fact]
    public async Task Stale_requests_missing_teams_and_invalid_court_cannot_write()
    {
        await using var db = Memory(); var f = await Seed(db); var service = Service(db);
        foreach (var request in new[] {
            new CoordinateMatchRequest { ExpectedVersion = 1, Preparing = true },
            new CoordinateMatchRequest { ExpectedVersion = 1, CourtText = new string('a', 101) },
            new CoordinateMatchRequest { ExpectedVersion = 2, CourtText = "Sân 1", Preparing = true } })
            await Assert.ThrowsAsync<CoordinationException>(() => service.UpdateAsync(f.UserId, f.MatchId, request, default));
        var match = await db.TournamentGroupMatches.SingleAsync();
        match.Team2RegistrationId = null; match.Team2Registration = null; await db.SaveChangesAsync();
        Assert.Equal("MATCH_NOT_READY", (await Assert.ThrowsAsync<CoordinationException>(() => service.UpdateAsync(f.UserId, f.MatchId,
            new() { ExpectedVersion = match.StateVersion, CourtText = "Sân 1", Preparing = true }, default))).Code);
        Assert.Empty(await db.MatchCoordinationHistories.ToListAsync());
    }

    [Fact]
    public async Task Actual_referee_scoring_starts_at_zero_keeps_running_on_undo_and_completes_and_reopens()
    {
        await using var db = Memory(); var f = await Seed(db); var service = Service(db);
        await service.UpdateAsync(f.UserId, f.MatchId, new() { ExpectedVersion = 1, CourtText = "Sân 1", Preparing = true }, default);
        var referee = Referee(db, f.UserId);
        Assert.IsType<OkObjectResult>(await referee.SetScore(f.MatchId, new() { ScoreTeam1 = 0, ScoreTeam2 = 0, IsCompleted = false }));
        var match = await db.TournamentGroupMatches.SingleAsync();
        Assert.Equal(MatchStatuses.InProgress, match.MatchStatus);
        Assert.Equal("MATCH_STARTED", (await Assert.ThrowsAsync<CoordinationException>(() => service.UpdateAsync(f.UserId, f.MatchId,
            new() { ExpectedVersion = match.StateVersion, CourtText = "Sân 2", Preparing = true }, default))).Code);
        Assert.IsType<OkObjectResult>(await referee.SetScore(f.MatchId, new() { ScoreTeam1 = 1, ScoreTeam2 = 0, IsCompleted = false }));
        Assert.IsType<OkObjectResult>(await referee.SetScore(f.MatchId, new() { ScoreTeam1 = 0, ScoreTeam2 = 0, IsCompleted = false }));
        Assert.Equal(MatchStatuses.InProgress, match.MatchStatus);
        Assert.IsType<OkObjectResult>(await referee.SetScore(f.MatchId, new() { ScoreTeam1 = 11, ScoreTeam2 = 8, IsCompleted = true }));
        Assert.Equal(MatchStatuses.Completed, match.MatchStatus);
        Assert.IsType<OkObjectResult>(await referee.SetScore(f.MatchId, new() { ScoreTeam1 = 11, ScoreTeam2 = 8, IsCompleted = false }));
        Assert.Equal(MatchStatuses.InProgress, match.MatchStatus);
    }

    [Fact]
    public async Task Invalidated_participants_clear_preparation_and_bye_is_completed()
    {
        await using var db = Memory(); var f = await Seed(db);
        await Service(db).UpdateAsync(f.UserId, f.MatchId, new() { ExpectedVersion = 1, CourtText = "Sân 1", Preparing = true }, default);
        var m = await db.TournamentGroupMatches.SingleAsync();
        m.Team2RegistrationId = null; m.Team2Registration = null;
        await db.SaveChangesAsync(); Assert.Equal(MatchStatuses.NotStarted, m.MatchStatus);
        m.IsCompleted = true; m.CompletionReason = MatchCompletionReasons.Bye;
        await db.SaveChangesAsync(); Assert.Equal(MatchStatuses.Completed, m.MatchStatus);
        m.IsCompleted = false; m.CompletionReason = null;
        await db.SaveChangesAsync(); Assert.Equal(MatchStatuses.NotStarted, m.MatchStatus);
    }

    [RelaySqlFact]
    public async Task Sql_migration_is_repeatable_and_two_dispatchers_cannot_overwrite_each_other()
    {
        await using var sandbox = await RelaySqlSandbox.CreateFullSchemaAsync();
        var sql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sql", "20260922_add_match_coordination.sql"));
        Fixture f;
        await using (var seed = sandbox.CreateDb()) f = await Seed(seed);
        await sandbox.SqlAsync($"UPDATE dbo.TournamentGroupMatches SET ScoreTeam1 = 11, ScoreTeam2 = 8, IsCompleted = 1 WHERE MatchId = {f.MatchId}");
        // Exercise ALTER/backfill against the old shape, on this isolated test database only.
        await sandbox.SqlAsync("""
            DROP TABLE dbo.MatchCoordinationHistories;
            DROP TABLE dbo.TournamentCoordinators;
            DECLARE @constraintName sysname;
            SELECT @constraintName = dc.name FROM sys.default_constraints dc JOIN sys.columns c
            ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
            WHERE dc.parent_object_id = OBJECT_ID('dbo.TournamentGroupMatches') AND c.name = 'MatchStatus';
            IF @constraintName IS NOT NULL EXEC('ALTER TABLE dbo.TournamentGroupMatches DROP CONSTRAINT [' + @constraintName + ']');
            ALTER TABLE dbo.TournamentGroupMatches DROP COLUMN MatchStatus, StateVersion;
            """);
        await sandbox.SqlAsync(sql); await sandbox.SqlAsync(sql);
        await using (var check = sandbox.CreateDb())
        {
            var old = await check.TournamentGroupMatches.SingleAsync();
            Assert.Equal(MatchStatuses.Completed, old.MatchStatus); Assert.Equal(11, old.ScoreTeam1); Assert.Equal(8, old.ScoreTeam2);
            await new AdminTournamentCoordinatorsController(check).Assign(f.TournamentId, f.UserId, default);
        }
        await sandbox.SqlAsync($"UPDATE dbo.TournamentGroupMatches SET ScoreTeam1 = 0, ScoreTeam2 = 0, IsCompleted = 0, MatchStatus = 'NOT_STARTED' WHERE MatchId = {f.MatchId}");
        async Task<Exception?> Dispatch(string court)
        {
            await using var db = sandbox.CreateDb();
            return await Record.ExceptionAsync(() => Service(db).UpdateAsync(f.UserId, f.MatchId,
                new() { ExpectedVersion = 1, CourtText = court, Preparing = true }, default));
        }
        var results = await Task.WhenAll(Dispatch("Sân 1"), Dispatch("Sân 2"));
        Assert.Single(results, x => x == null);
        Assert.Equal("MATCH_CHANGED", Assert.IsType<CoordinationException>(results.Single(x => x != null)).Code);
        await using var verify = sandbox.CreateDb();
        Assert.Single(await verify.MatchCoordinationHistories.ToListAsync());
        Assert.Equal(2, (await verify.TournamentGroupMatches.SingleAsync()).StateVersion);
    }

    [RelaySqlFact]
    public async Task Sql_score_winning_the_race_prevents_preparation_and_rollback_does_not_change_status()
    {
        await using var sandbox = await RelaySqlSandbox.CreateFullSchemaAsync();
        Fixture f;
        await using (var seed = sandbox.CreateDb()) f = await Seed(seed);
        await using (var scoreDb = sandbox.CreateDb())
        {
            Assert.IsType<OkObjectResult>(await Referee(scoreDb, f.UserId).SetScore(f.MatchId, new() { ScoreTeam1 = 1, ScoreTeam2 = 0, IsCompleted = false }));
        }
        await using var stale = sandbox.CreateDb();
        Assert.Equal("MATCH_CHANGED", (await Assert.ThrowsAsync<CoordinationException>(() => Service(stale).UpdateAsync(f.UserId, f.MatchId,
            new() { ExpectedVersion = 1, CourtText = "Sân 2", Preparing = true }, default))).Code);
        await using (var rollback = sandbox.CreateDb())
        {
            await using var tx = await rollback.Database.BeginTransactionAsync();
            var m = await rollback.TournamentGroupMatches.SingleAsync(); m.IsCompleted = true;
            await rollback.SaveChangesAsync(); await tx.RollbackAsync();
        }
        await using var verify = sandbox.CreateDb();
        Assert.Equal(MatchStatuses.InProgress, (await verify.TournamentGroupMatches.SingleAsync()).MatchStatus);
        Assert.Empty(await verify.MatchCoordinationHistories.ToListAsync());
    }

    private static RefereeMatchesApiController Referee(PickleballDbContext db, long userId) => new(db,
        new PublicRealtimeHub(NullLogger<PublicRealtimeHub>.Instance), null!,
        new TournamentBracketPropagationService(db, new TournamentStandingsService(db), NullLogger<TournamentBracketPropagationService>.Instance),
        NullLogger<RefereeMatchesApiController>.Instance)
    { ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
        new Claim("UserId", userId.ToString()), new Claim(ClaimTypes.Role, "REFEREE")], "test")) } } };
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private sealed record Fixture(long MatchId, long TournamentId, long UserId);
    private static async Task<Fixture> Seed(PickleballDbContext db)
    {
        var user = new User { FullName = "Điều phối kiểm thử", IsActive = true };
        var tournament = new Tournament { Title = "Giải kiểm thử", Status = "OPEN", GameType = "DOUBLE", GenderCategory = "OPEN" };
        var a = new TournamentRegistration { Tournament = tournament, RegCode = "A", Player1Name = "Đội A", Success = true };
        var b = new TournamentRegistration { Tournament = tournament, RegCode = "B", Player1Name = "Đội B", Success = true };
        var group = new TournamentRoundGroup { GroupName = "A", TournamentRoundMap = new() { Tournament = tournament, RoundKey = "R1", RoundLabel = "Vòng 1" } };
        db.Users.Add(user); db.TournamentRegistrations.AddRange(a, b); db.TournamentRoundGroups.Add(group); await db.SaveChangesAsync();
        var match = new TournamentGroupMatch { Tournament = tournament, TournamentRoundGroup = group,
            Team1Registration = a, Team2Registration = b, RefereeUser = user, StartAt = DateTime.Today.AddHours(8) };
        db.TournamentGroupMatches.Add(match); await db.SaveChangesAsync();
        await new AdminTournamentCoordinatorsController(db).Assign(tournament.TournamentId, user.UserId, default);
        return new(match.MatchId, tournament.TournamentId, user.UserId);
    }
}
