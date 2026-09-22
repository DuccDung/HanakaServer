using System.Security.Claims;
using System.Text.Json;
using HanakaServer.Controllers;
using HanakaServer.Data;
using HanakaServer.Dtos.Relay;
using HanakaServer.Helpers;
using HanakaServer.Models;
using HanakaServer.Models.Dto;
using HanakaServer.Options;
using HanakaServer.Services;
using HanakaServer.Services.Relay;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HanakaServer.Tests;

public sealed class RelayThreePartScoringTests
{
    private static readonly Microsoft.Extensions.Options.IOptions<RelayOptions> Enabled =
        Microsoft.Extensions.Options.Options.Create(new RelayOptions { AdminPreviewEnabled = true });

    [Fact]
    public async Task Each_save_persists_parts_totals_history_and_snapshots_and_completion_uses_total()
    {
        await using var db = Memory();
        var f = await Seed(db);
        var controller = Referee(db, f.RefereeId);
        await Score(controller, f.MatchId, Update(0, (21, 18), (0, 0), (0, 0)));
        Assert.Equal(1, (await db.TournamentMatchScoreHistories.SingleAsync()).RelayPartNumber);
        await Score(controller, f.MatchId, Update(1, (21, 18), (15, 21), (0, 0)));
        var open = await Score(controller, f.MatchId, Update(2, (21, 18), (15, 21), (21, 20)));
        Assert.Equal(57, open.GetProperty("scoreTeam1").GetInt32());
        Assert.Equal(59, open.GetProperty("scoreTeam2").GetInt32());
        Assert.False(open.GetProperty("isCompleted").GetBoolean());
        Assert.Equal(3, await db.TournamentMatchScoreHistories.CountAsync());
        Assert.Equal(2, await db.RelayMatchLineupSnapshots.CountAsync());
        var result = await Score(controller, f.MatchId, Update(3, (21, 18), (15, 21), (21, 20)), true);
        Assert.Equal(f.Team2, result.GetProperty("winnerRegistrationId").GetInt64());
        Assert.True(result.GetProperty("isCompleted").GetBoolean());
        var history = await db.TournamentMatchScoreHistories.OrderBy(x => x.ScoreHistoryId).LastAsync();
        Assert.Equal(3, JsonDocument.Parse(history.RelayPartsJson!).RootElement.GetArrayLength());
        var list = Json(Assert.IsType<OkObjectResult>(await controller.ListMyMatches()).Value!);
        Assert.Equal(4, list.GetProperty("items")[0].GetProperty("relayScores").GetProperty("version").GetInt64());
        Assert.Empty(await db.RelayLegs.ToListAsync());
        Assert.Empty(await db.RelayMatchStates.ToListAsync());
    }

    [Fact]
    public async Task Referee_can_exceed_21_edit_any_part_and_reduce_to_zero()
    {
        await using var db = Memory();
        var f = await Seed(db); var c = Referee(db, f.RefereeId);
        await Score(c, f.MatchId, Update(0, (30, 29), (0, 0), (9, 4)));
        var score = await Score(c, f.MatchId, Update(1, (0, 0), (25, 23), (9, 4)));
        Assert.Equal(34, score.GetProperty("scoreTeam1").GetInt32());
        Assert.Equal(27, score.GetProperty("scoreTeam2").GetInt32());
        Assert.False(score.GetProperty("isCompleted").GetBoolean());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("negative")]
    [InlineData("overflow")]
    [InlineData("tie")]
    public async Task Invalid_scores_cannot_write_parts_or_history(string kind)
    {
        await using var db = Memory();
        var f = await Seed(db); var update = Update(0, (5, 3), (0, 0), (0, 0));
        switch (kind)
        {
            case "missing": update.Parts.RemoveAt(2); break;
            case "duplicate": update.Parts[2] = new(2, 0, 0); break;
            case "negative": update.Parts[0] = new(1, -1, 0); break;
            case "overflow": update.Parts[0] = new(1, int.MaxValue, 3); update.Parts[1] = new(2, 1, 0); break;
            case "tie": update.Parts[1] = new(2, 0, 2); break;
        }
        Assert.IsType<BadRequestObjectResult>(await Referee(db, f.RefereeId).SetScore(f.MatchId,
            new() { Relay = update, IsCompleted = kind == "tie" }));
        Assert.Empty(await db.RelayMatchScores.ToListAsync());
        Assert.Empty(await db.TournamentMatchScoreHistories.ToListAsync());
    }

    [Fact]
    public async Task Assigned_referee_today_and_shared_endpoint_guards_are_preserved()
    {
        await using var db = Memory(); var f = await Seed(db);
        var request = new RefereeSetScoreDto { Relay = Update(0, (1, 0), (0, 0), (0, 0)), IsCompleted = false };
        Assert.IsType<NotFoundObjectResult>(await Referee(db, f.RefereeId + 100).SetScore(f.MatchId, request));
        var m = await db.TournamentGroupMatches.SingleAsync();
        m.StartAt = DateTime.Today.AddDays(1); await db.SaveChangesAsync();
        Assert.IsType<BadRequestObjectResult>(await Referee(db, f.RefereeId).SetScore(f.MatchId, request));
        m.StartAt = DateTime.Today; await db.SaveChangesAsync();
        Assert.IsType<BadRequestObjectResult>(await Referee(db, f.RefereeId).SetScore(f.MatchId, new() { ScoreTeam1 = 8, ScoreTeam2 = 3, IsCompleted = false }));
        Assert.Empty(await db.RelayMatchScores.ToListAsync());
    }

    [Fact]
    public async Task Old_total_requires_explicit_matching_allocation_and_stale_write_is_rejected()
    {
        await using var db = Memory(); var f = await Seed(db); var c = Referee(db, f.RefereeId);
        var m = await db.TournamentGroupMatches.SingleAsync(); m.ScoreTeam1 = 30; m.ScoreTeam2 = 25; await db.SaveChangesAsync();
        var list = Json(Assert.IsType<OkObjectResult>(await c.ListMyMatches()).Value!);
        Assert.True(list.GetProperty("items")[0].GetProperty("relayScores").GetProperty("requiresAllocation").GetBoolean());
        var request = Update(0, (21, 18), (9, 7), (0, 0));
        Assert.IsType<BadRequestObjectResult>(await c.SetScore(f.MatchId, new() { Relay = request, IsCompleted = false }));
        request.AllocateExisting = true;
        await Score(c, f.MatchId, request);
        Assert.IsType<ConflictObjectResult>(await c.SetScore(f.MatchId, new() { Relay = request, IsCompleted = false }));
        Assert.Single(await db.TournamentMatchScoreHistories.ToListAsync());
    }

    [Fact]
    public async Task Admin_uses_same_parts_and_creates_identifiable_history_without_a_user_row()
    {
        await using var db = Memory(); var f = await Seed(db);
        var admin = Admin(db);
        var response = Assert.IsType<OkObjectResult>(await admin.SetScore(f.GroupId, f.MatchId,
            new() { Relay = Update(0, (21, 18), (15, 21), (21, 20)), IsCompleted = true }));
        Assert.Equal(f.Team2, Json(response.Value!).GetProperty("winnerRegistrationId").GetInt64());
        var history = Assert.Single(await db.TournamentMatchScoreHistories.ToListAsync());
        Assert.Null(history.RefereeUserId); Assert.Equal("Admin thử nghiệm", history.ActorName);
        Assert.NotNull(history.RelayPartsJson);
        Assert.IsType<BadRequestObjectResult>(await admin.SetScore(f.GroupId, f.MatchId, new() { ScoreTeam1 = 500, ScoreTeam2 = 3 }));
    }

    [Fact]
    public async Task Standard_scoring_is_unchanged_and_does_not_create_relay_scores()
    {
        await using var db = Memory(); var f = await Seed(db, relay: false);
        Assert.IsType<OkObjectResult>(await Referee(db, f.RefereeId).SetScore(f.MatchId, new() { ScoreTeam1 = 11, ScoreTeam2 = 7, IsCompleted = true }));
        Assert.Empty(await db.RelayMatchScores.ToListAsync());
        Assert.Null((await db.TournamentMatchScoreHistories.SingleAsync()).RelayPartsJson);
    }

    [Fact]
    public async Task Bracket_source_changes_preserve_an_already_scored_relay_match()
    {
        await using var db = Memory(); var f = await Seed(db);
        await Score(Referee(db, f.RefereeId), f.MatchId, Update(0, (5, 3), (0, 0), (0, 0)));
        var target = await db.TournamentGroupMatches.SingleAsync();
        var originalTeam = target.Team1RegistrationId;
        var third = new TournamentRegistration { TournamentId = f.TournamentId, RegCode = "C", Player1Name = "C" };
        db.TournamentRegistrations.Add(third); await db.SaveChangesAsync();
        var source = new TournamentGroupMatch
        {
            TournamentId = f.TournamentId,
            TournamentRoundGroupId = f.GroupId,
            Team1RegistrationId = originalTeam,
            Team2RegistrationId = third.RegistrationId,
            WinnerRegistrationId = third.RegistrationId,
            IsCompleted = true
        };
        db.TournamentGroupMatches.Add(source); await db.SaveChangesAsync();
        target.Team1SourceType = "WINNER_MATCH"; target.Team1SourceMatchId = source.MatchId; await db.SaveChangesAsync();
        var propagation = new TournamentBracketPropagationService(db, new TournamentStandingsService(db), NullLogger<TournamentBracketPropagationService>.Instance);
        await propagation.PropagateFromMatchAsync(source.MatchId);
        await propagation.RecalculateMatchSlotsAsync(target.MatchId);
        Assert.Equal(originalTeam, target.Team1RegistrationId);
        Assert.Equal(5, target.ScoreTeam1);
        Assert.Equal(5, (await db.RelayMatchScores.SingleAsync()).Part1Team1);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public async Task Reopening_retracts_winner_and_loser_slots_independently_of_score(bool admin, bool relay, bool clearPoints)
    {
        await using var db = Memory();
        var f = await Seed(db, relay);
        var source = await db.TournamentGroupMatches.SingleAsync();
        var other = new TournamentRegistration { TournamentId = f.TournamentId, RegCode = "OTHER", Player1Name = "Other" };
        db.TournamentRegistrations.Add(other);
        await db.SaveChangesAsync();
        var winnerMatch = new TournamentGroupMatch
        {
            TournamentId = f.TournamentId, TournamentRoundGroupId = f.GroupId,
            Team1SourceMatchId = f.MatchId, Team1SourceType = MatchSourceTypes.WinnerMatch,
            Team2RegistrationId = other.RegistrationId
        };
        var loserMatch = new TournamentGroupMatch
        {
            TournamentId = f.TournamentId, TournamentRoundGroupId = f.GroupId,
            Team2SourceMatchId = f.MatchId, Team2SourceType = MatchSourceTypes.LoserMatch,
            Team1RegistrationId = other.RegistrationId
        };
        var unrelated = new TournamentGroupMatch
        {
            TournamentId = f.TournamentId, TournamentRoundGroupId = f.GroupId,
            Team1RegistrationId = source.Team1RegistrationId, Team2RegistrationId = other.RegistrationId,
            IsCompleted = true, WinnerRegistrationId = source.Team1RegistrationId, ScoreTeam1 = 11, ScoreTeam2 = 7
        };
        db.TournamentGroupMatches.AddRange(winnerMatch, loserMatch, unrelated);
        await db.SaveChangesAsync();

        async Task Save(bool completed, long version, int a, int b)
        {
            var parts = relay ? Update(version, (a, b), (0, 0), (0, 0)) : null;
            var response = admin
                ? await Admin(db).SetScore(f.GroupId, f.MatchId, new() { ScoreTeam1 = a, ScoreTeam2 = b, Relay = parts, IsCompleted = completed })
                : await Referee(db, f.RefereeId).SetScore(f.MatchId, new() { ScoreTeam1 = a, ScoreTeam2 = b, Relay = parts, IsCompleted = completed });
            Assert.IsType<OkObjectResult>(response);
        }

        await Save(true, 0, 21, 18);
        Assert.Equal(source.Team1RegistrationId, winnerMatch.Team1RegistrationId);
        Assert.Equal(source.Team2RegistrationId, loserMatch.Team2RegistrationId);
        await Save(false, 1, clearPoints ? 0 : 21, clearPoints ? 0 : 18);
        db.ChangeTracker.Clear();
        var matches = await db.TournamentGroupMatches.ToDictionaryAsync(x => x.MatchId);
        Assert.False(matches[f.MatchId].IsCompleted);
        Assert.Null(matches[f.MatchId].WinnerRegistrationId);
        Assert.Equal(clearPoints ? 0 : 21, matches[f.MatchId].ScoreTeam1);
        Assert.Equal(clearPoints ? 0 : 18, matches[f.MatchId].ScoreTeam2);
        Assert.Null(matches[winnerMatch.MatchId].Team1RegistrationId);
        Assert.Null(matches[loserMatch.MatchId].Team2RegistrationId);
        Assert.Equal(f.MatchId, matches[winnerMatch.MatchId].Team1SourceMatchId);
        Assert.Equal(f.MatchId, matches[loserMatch.MatchId].Team2SourceMatchId);
        Assert.Equal(other.RegistrationId, matches[winnerMatch.MatchId].Team2RegistrationId);
        Assert.Equal(other.RegistrationId, matches[loserMatch.MatchId].Team1RegistrationId);
        Assert.True(matches[unrelated.MatchId].IsCompleted);
        Assert.Equal(11, matches[unrelated.MatchId].ScoreTeam1);

        await Save(true, 2, 18, 21);
        Assert.Equal(source.Team2RegistrationId, matches[winnerMatch.MatchId].Team1RegistrationId);
        Assert.Equal(source.Team1RegistrationId, matches[loserMatch.MatchId].Team2RegistrationId);
    }

    [RelaySqlFact]
    public async Task Sql_reopening_atomically_retracts_dependent_relay_results_and_byes_and_allows_rescoring()
    {
        await using var sandbox = await RelaySqlSandbox.CreateFullSchemaAsync();
        Fixture f;
        long nextId, byeId;
        await using (var db = sandbox.CreateDb())
        {
            f = await Seed(db);
            var roundId = (await db.TournamentRoundGroups.SingleAsync()).TournamentRoundMapId;
            var nextGroup = new TournamentRoundGroup { TournamentRoundMapId = roundId, GroupName = "Next" };
            var byeGroup = new TournamentRoundGroup { TournamentRoundMapId = roundId, GroupName = "Bye" };
            var next = new TournamentGroupMatch
            {
                TournamentId = f.TournamentId, TournamentRoundGroup = nextGroup,
                Team1SourceType = MatchSourceTypes.WinnerMatch, Team1SourceMatchId = f.MatchId,
                Team2SourceType = MatchSourceTypes.LoserMatch, Team2SourceMatchId = f.MatchId,
                RefereeUserId = f.RefereeId, StartAt = DateTime.Today.AddHours(9)
            };
            db.TournamentGroupMatches.Add(next);
            await db.SaveChangesAsync();
            var bye = new TournamentGroupMatch
            {
                TournamentId = f.TournamentId, TournamentRoundGroup = byeGroup,
                Team1SourceType = MatchSourceTypes.WinnerMatch, Team1SourceMatchId = next.MatchId,
                Team2SourceType = MatchSourceTypes.Bye
            };
            db.TournamentGroupMatches.Add(bye);
            await db.SaveChangesAsync();
            nextId = next.MatchId; byeId = bye.MatchId;
            await Score(Referee(db, f.RefereeId), f.MatchId, Update(0, (21, 18), (0, 0), (0, 0)), true);
            await Score(Referee(db, f.RefereeId), nextId, Update(0, (17, 21), (0, 0), (0, 0)), true);
            Assert.True(bye.IsCompleted);
        }

        await sandbox.SqlAsync($"CREATE TRIGGER dbo.TestRejectRetraction ON dbo.TournamentGroupMatches AFTER UPDATE AS IF EXISTS (SELECT 1 FROM inserted WHERE MatchId = {nextId} AND Team1RegistrationId IS NULL) THROW 51998, 'Intentional retraction failure', 1;");
        await using (var db = sandbox.CreateDb())
            await Assert.ThrowsAsync<DbUpdateException>(() => Score(Referee(db, f.RefereeId), f.MatchId, Update(1, (21, 18), (0, 0), (0, 0))));
        await using (var db = sandbox.CreateDb())
        {
            Assert.True((await db.TournamentGroupMatches.SingleAsync(x => x.MatchId == f.MatchId)).IsCompleted);
            Assert.True((await db.TournamentGroupMatches.SingleAsync(x => x.MatchId == nextId)).IsCompleted);
            Assert.Equal(1, (await db.RelayMatchScores.SingleAsync(x => x.MatchId == f.MatchId)).Version);
            Assert.Equal(4, await db.RelayMatchLineupSnapshots.CountAsync());
            Assert.Equal(2, await db.TournamentMatchScoreHistories.CountAsync());
        }
        await sandbox.SqlAsync("DROP TRIGGER dbo.TestRejectRetraction;");
        await using (var db = sandbox.CreateDb())
            await Score(Referee(db, f.RefereeId), f.MatchId, Update(1, (21, 18), (0, 0), (0, 0)));
        await using (var db = sandbox.CreateDb())
        {
            var next = await db.TournamentGroupMatches.SingleAsync(x => x.MatchId == nextId);
            var bye = await db.TournamentGroupMatches.SingleAsync(x => x.MatchId == byeId);
            Assert.Null(next.Team1RegistrationId); Assert.Null(next.Team2RegistrationId);
            Assert.False(next.IsCompleted); Assert.Null(next.WinnerRegistrationId);
            Assert.Equal(0, next.ScoreTeam1); Assert.Equal(0, next.ScoreTeam2);
            Assert.False(bye.IsCompleted); Assert.Null(bye.Team1RegistrationId); Assert.Null(bye.CompletionReason);
            var points = await db.RelayMatchScores.SingleAsync(x => x.MatchId == nextId);
            Assert.Equal(2, points.Version);
            Assert.All(RelayScoringService.Map(points, 0, 0).Parts, p => { Assert.Equal(0, p.ScoreTeam1); Assert.Equal(0, p.ScoreTeam2); });
            Assert.False(await db.RelayMatchLineupSnapshots.AnyAsync(x => x.MatchId == nextId));
            Assert.Equal(3, await db.TournamentMatchScoreHistories.CountAsync());

            await Score(Referee(db, f.RefereeId), f.MatchId, Update(2, (18, 21), (0, 0), (0, 0)), true);
            Assert.Equal(f.Team2, next.Team1RegistrationId);
            Assert.NotNull(next.Team2RegistrationId);
            Assert.IsType<ConflictObjectResult>(await Referee(db, f.RefereeId).SetScore(nextId,
                new() { Relay = Update(1, (18, 21), (0, 0), (0, 0)), IsCompleted = false }));
            await Score(Referee(db, f.RefereeId), nextId, Update(2, (21, 17), (0, 0), (0, 0)), true);
            Assert.True(bye.IsCompleted);
            Assert.Equal(f.Team2, bye.Team1RegistrationId);
            Assert.Equal(f.Team2, (await db.RelayMatchLineupSnapshots.SingleAsync(x => x.MatchId == nextId && x.Side == 1)).RegistrationId);
        }
    }

    [RelaySqlFact]
    public async Task Sql_migration_roundtrip_and_concurrent_writes_keep_a_single_consistent_score()
    {
        await using var sandbox = await RelaySqlSandbox.CreateFullSchemaAsync();
        // Exercise actual migration against pre-change tables, including the non-null old referee FK.
        await sandbox.SqlAsync("""
            DROP TABLE dbo.RelayMatchScores;
            ALTER TABLE dbo.TournamentMatchScoreHistories DROP COLUMN RelayPartNumber, RelayPartsJson, ActorName;
            ALTER TABLE dbo.TournamentMatchScoreHistories DROP CONSTRAINT FK_TMSH_RefereeUser;
            DROP INDEX IX_TMSH_RefereeUserId ON dbo.TournamentMatchScoreHistories;
            ALTER TABLE dbo.TournamentMatchScoreHistories ALTER COLUMN RefereeUserId bigint NOT NULL;
            CREATE INDEX IX_TMSH_RefereeUserId ON dbo.TournamentMatchScoreHistories(RefereeUserId);
            ALTER TABLE dbo.TournamentMatchScoreHistories ADD CONSTRAINT FK_TMSH_RefereeUser FOREIGN KEY(RefereeUserId) REFERENCES dbo.Users(UserId);
            """);
        await sandbox.MigrateScoresAsync(); await sandbox.MigrateScoresAsync();
        Fixture f;
        await using (var db = sandbox.CreateDb()) f = await Seed(db);
        async Task<IActionResult> Attempt(int points)
        {
            await using var db = sandbox.CreateDb();
            return await Referee(db, f.RefereeId).SetScore(f.MatchId, new() { Relay = Update(0, (points, 0), (0, 0), (0, 0)), IsCompleted = false });
        }
        var results = await Task.WhenAll(Attempt(1), Attempt(2));
        Assert.Single(results.OfType<OkObjectResult>()); Assert.Single(results.OfType<ConflictObjectResult>());
        await using (var db = sandbox.CreateDb())
        {
            var row = await db.RelayMatchScores.SingleAsync();
            Assert.Equal(1, row.Version);
            Assert.Equal(row.Part1Team1, (await db.TournamentGroupMatches.SingleAsync()).ScoreTeam1);
            Assert.Single(await db.TournamentMatchScoreHistories.ToListAsync());
            Assert.Equal(2, await db.RelayMatchLineupSnapshots.CountAsync());
            Assert.IsType<OkObjectResult>(await Admin(db).SetScore(f.GroupId, f.MatchId, new() { Relay = Update(1, (21, 18), (15, 21), (21, 20)), IsCompleted = true }));
        }
        await using (var db = sandbox.CreateDb())
        {
            Assert.Equal(f.Team2, (await db.TournamentGroupMatches.SingleAsync()).WinnerRegistrationId);
            Assert.Equal(2, await db.TournamentMatchScoreHistories.CountAsync());
        }
    }

    [RelaySqlFact]
    public async Task Sql_failure_rolls_back_parts_totals_and_lineup_snapshot_with_history()
    {
        await using var sandbox = await RelaySqlSandbox.CreateFullSchemaAsync();
        Fixture f; await using (var db = sandbox.CreateDb()) f = await Seed(db);
        await sandbox.SqlAsync("CREATE TRIGGER dbo.TestRejectHistory ON dbo.TournamentMatchScoreHistories AFTER INSERT AS THROW 51999, 'Intentional history failure', 1;");
        await using (var db = sandbox.CreateDb())
            await Assert.ThrowsAsync<DbUpdateException>(() => Referee(db, f.RefereeId).SetScore(f.MatchId, new() { Relay = Update(0, (1, 0), (0, 0), (0, 0)), IsCompleted = false }));
        await using var verify = sandbox.CreateDb();
        Assert.Empty(await verify.RelayMatchScores.ToListAsync());
        Assert.Empty(await verify.RelayMatchLineupSnapshots.ToListAsync());
        Assert.Equal(0, (await verify.TournamentGroupMatches.SingleAsync()).ScoreTeam1);
    }

    private static PickleballDbContext Memory() => new(new DbContextOptionsBuilder<PickleballDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static RefereeMatchesApiController Referee(PickleballDbContext db, long userId) => new(db,
        new PublicRealtimeHub(NullLogger<PublicRealtimeHub>.Instance), null!,
        new TournamentBracketPropagationService(db, new TournamentStandingsService(db), NullLogger<TournamentBracketPropagationService>.Instance),
        NullLogger<RefereeMatchesApiController>.Instance, new(db, Enabled), new(db, TimeProvider.System), new(db, Enabled))
    { ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("UserId", userId.ToString()), new Claim(ClaimTypes.Role, "REFEREE")], "test")) } } };
    private static AdminTournamentGroupMatchesController Admin(PickleballDbContext db) => new(db, null!,
        new TournamentBracketPropagationService(db, new TournamentStandingsService(db), NullLogger<TournamentBracketPropagationService>.Instance),
        new TournamentStandingsService(db), NullLogger<AdminTournamentGroupMatchesController>.Instance, new(db, Enabled), new(db, TimeProvider.System), new(db, Enabled))
    { ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin"), new Claim(ClaimTypes.Name, "Admin thử nghiệm")], "test")) } } };
    private static RelayScoreUpdate Update(long version, (int A, int B) p1, (int A, int B) p2, (int A, int B) p3) =>
        new() { ExpectedVersion = version, Parts = [new(1, p1.A, p1.B), new(2, p2.A, p2.B), new(3, p3.A, p3.B)] };
    private static async Task<JsonElement> Score(RefereeMatchesApiController controller, long matchId, RelayScoreUpdate update, bool completed = false) =>
        Json(Assert.IsType<OkObjectResult>(await controller.SetScore(matchId, new() { Relay = update, IsCompleted = completed, ScoreTeam1 = 999, ScoreTeam2 = 999 })).Value!);
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private sealed record Fixture(long MatchId, long TournamentId, long GroupId, long RefereeId, long Team2);
    private static async Task<Fixture> Seed(PickleballDbContext db, bool relay = true)
    {
        var user = new User { FullName = "Trọng tài kiểm thử", IsActive = true };
        var tournament = new Tournament { Title = "Tiếp sức", Status = "OPEN", GameType = "DOUBLE", GenderCategory = "OPEN", ExpectedTeams = 2 };
        var a = new TournamentRegistration { Tournament = tournament, RegCode = "A", Player1Name = "A", Success = true };
        var b = new TournamentRegistration { Tournament = tournament, RegCode = "B", Player1Name = "B", Success = true };
        var group = new TournamentRoundGroup { GroupName = "A", TournamentRoundMap = new() { Tournament = tournament, RoundKey = "R1", RoundLabel = "Vòng 1" } };
        db.Users.Add(user); db.TournamentRegistrations.AddRange(a, b); db.TournamentRoundGroups.Add(group); await db.SaveChangesAsync();
        if (relay) db.RelayTournamentSettings.Add(new() { TournamentId = tournament.TournamentId, TeamSize = 6, TargetScore = 21 });
        foreach (var registration in relay ? new[] { a, b } : Array.Empty<TournamentRegistration>())
        {
            var team = new RelayTeam { RegistrationId = registration.RegistrationId, TournamentId = tournament.TournamentId, TeamName = registration.RegCode, Version = 1 };
            foreach (var p in Enumerable.Range(1, 6)) team.Members.Add(new() { Position = p, DisplayName = $"{registration.RegCode} {p}" });
            db.RelayTeams.Add(team);
        }
        var match = new TournamentGroupMatch { Tournament = tournament, TournamentRoundGroup = group, Team1Registration = a, Team2Registration = b, RefereeUser = user, StartAt = DateTime.Today.AddHours(8) };
        db.TournamentGroupMatches.Add(match); await db.SaveChangesAsync();
        return new(match.MatchId, tournament.TournamentId, group.TournamentRoundGroupId, user.UserId, b.RegistrationId);
    }
}
