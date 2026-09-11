using System.Text.Json;
using System.Security.Claims;
using HanakaServer.Controllers;
using HanakaServer.Dtos.Brackets;
using HanakaServer.Models;
using HanakaServer.Options;
using HanakaServer.Services.Brackets;
using HanakaServer.Services;
using HanakaServer.Services.Relay;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HanakaServer.Tests;

public sealed partial class BracketTemplateWorkflowIntegrationTests
{
    [RelaySqlFact]
    public async Task Relay_library_SQL_requires_complete_teams_and_preserves_application_snapshots()
    {
        await using var sandbox = await RelaySqlSandbox.CreateFullSchemaAsync();
        await using var db = sandbox.CreateDb();
        var referee = new User { FullName = "Trọng tài", IsActive = true, Verified = true };
        db.Users.Add(referee);
        db.Roles.Add(new Role { RoleCode = "REFEREE", RoleName = "Trọng tài" });
        var tournament = new Tournament
        {
            Title = "Tiếp sức 6", Status = "DRAFT", GameType = "DOUBLE", GenderCategory = "OPEN",
            ExpectedTeams = 4, RegistrationFeeAmount = 100, RegistrationLockedAt = DateTime.UtcNow
        };
        for (var i = 1; i <= 7; i++)
            tournament.TournamentRegistrations.Add(new()
            {
                RegCode = $"RELAY-{i}", RegIndex = i, Player1Name = $"VĐV {i}.1", Player2Name = null,
                Success = true, Paid = i != 7, RegTime = DateTime.Today.AddMinutes(i)
            });
        db.Tournaments.Add(tournament);
        await db.SaveChangesAsync();
        db.Referees.Add(new Referee { FullName = referee.FullName!, ExternalId = referee.UserId.ToString(), Verified = true, RefereeType = "MAIN" });
        await db.SaveChangesAsync();
        await new RelayAdminService(db).SaveSettingsAsync(tournament.TournamentId, new() { TeamSize = 6 }, default);
        var lineup = new RelayLineupService(db, new RelayMatchLineupSnapshotService(db, new RelayTestClock()));
        foreach (var registration in tournament.TournamentRegistrations.Where(x => x.RegIndex != 6))
        {
            var count = registration.RegIndex == 5 ? 2 : 6;
            await lineup.SaveDraftAsync(registration.RegistrationId, tournament.TournamentId, $"Đội {registration.RegIndex}", null,
                Enumerable.Range(1, count).Select(x => new RelayMemberInput(x, null, $"VĐV {registration.RegIndex}.{x}")).ToArray(), 0, default);
        }
        var templates = CreateTemplateService(db);
        var (_, versionId, rowVersion) = await CreateDraftAsync(templates, "RELAY-SQL-4", BracketTemplateFormatTypes.SingleElimination, 4, 4, false,
            BracketTemplateParticipantModes.RelayTeam);
        var graph = BracketTemplateService.GenerateSingleElimination(4, false, [0]);
        graph.RowVersion = rowVersion;
        Assert.True((await templates.SaveGraphAsync(versionId, graph, default)).Success);
        Assert.True((await templates.PublishAsync(versionId, null, default)).Success);
        var templateBefore = JsonSerializer.Serialize(await templates.GetGraphAsync(versionId, default));
        var service = new TournamentBracketApplicationService(db, templates, new BracketTemplateValidationService(),
            NullLogger<TournamentBracketApplicationService>.Instance, Microsoft.Extensions.Options.Options.Create(new RelayOptions { AdminPreviewEnabled = true }));
        var eligible = await service.GetEligibleRegistrationsAsync(tournament.TournamentId, default);
        Assert.True(eligible.Success, eligible.Message);
        Assert.Equal(5, eligible.Data!.Items.Count); // Only incomplete/missing rosters are excluded by default.
        Assert.Single(eligible.Data.Items, x => !x.Paid);
        Assert.Equal(100, eligible.Data.RegistrationFeeAmount);
        Assert.All(eligible.Data.Items, x => { Assert.Equal(6, x.Relay!.Members.Count); Assert.Null(x.Player2Name); });
        var applicable = await service.GetApplicableTemplatesAsync(tournament.TournamentId, default);
        Assert.Equal(5, Assert.Single(applicable).EligibleTeamCount);
        Assert.False(Assert.Single(applicable).IsApplicable);
        var paidTemplates = await service.GetApplicableTemplatesAsync(tournament.TournamentId, default, excludeUnpaidTeams: true);
        Assert.Equal(4, Assert.Single(paidTemplates).EligibleTeamCount);
        Assert.True(Assert.Single(paidTemplates).IsApplicable);
        var request = new ApplyTournamentBracketRequest
        {
            BracketTemplateVersionId = versionId, SeedingMethod = BracketSeedingMethods.RegistrationOrder,
            ExcludeUnpaidTeams = true,
            StartAt = TestMatchStartAt, RefereeUserId = referee.UserId, AddressText = TestMatchAddress
        };
        var preview = await service.PreviewAsync(tournament.TournamentId, request, default);
        Assert.True(preview.Success, preview.Message);
        request.PreviewHash = preview.Data!.PreviewHash;
        Assert.Equal(1, preview.Data.ExcludedUnpaidRegistrationCount);
        var firstId = eligible.Data.Items.First().RegistrationId!.Value;
        // A source display update invalidates stale previews without changing any athlete name.
        await db.RelayTeams.Where(x => x.RegistrationId == firstId).ExecuteUpdateAsync(x => x.SetProperty(t => t.TeamName, "Đội A mới"));
        var stale = await service.ApplyAsync(tournament.TournamentId, request, referee.UserId, default);
        Assert.Equal("PREVIEW_CHANGED", stale.ErrorCode);
        preview = await service.PreviewAsync(tournament.TournamentId, request, default);
        request.PreviewHash = preview.Data!.PreviewHash;
        var applied = await service.ApplyAsync(tournament.TournamentId, request, referee.UserId, default);
        Assert.True(applied.Success, applied.Message);
        Assert.Equal(3, applied.Data!.GeneratedMatchCount);
        Assert.Equal(4, await db.RelayBracketSeedSnapshots.CountAsync());
        var snapshotBefore = JsonSerializer.Serialize(applied.Data.Seeds);
        await db.RelayTeams.Where(x => x.RegistrationId == firstId).ExecuteUpdateAsync(x => x.SetProperty(t => t.TeamName, "Tên hiện tại khác lịch sử"));
        await sandbox.MigrateBracketAsync(); // Repeatable with real snapshots present.
        var recovered = await service.GetActiveApplicationAsync(tournament.TournamentId, default);
        Assert.Equal(snapshotBefore, JsonSerializer.Serialize(recovered!.Seeds));
        Assert.Equal(templateBefore, JsonSerializer.Serialize(await templates.GetGraphAsync(versionId, default)));
        Assert.Equal("VĐV 1.1", await db.TournamentRegistrations.Where(x => x.RegistrationId == firstId).Select(x => x.Player1Name).SingleAsync());
        Assert.False(await db.RelayTournamentSettings.Select(x => x.IsEnabled).SingleAsync());

        var relayOptions = Microsoft.Extensions.Options.Options.Create(new RelayOptions { AdminPreviewEnabled = true });
        var publicController = new TournamentClientController(db, new TournamentStandingsService(db), new RelayTeamReader(db, relayOptions))
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
        var publicResponse = Assert.IsType<TournamentRoundsWithMatchesResponseDto>(
            Assert.IsType<OkObjectResult>(await publicController.GetRoundsWithMatches(tournament.TournamentId)).Value);
        var opening = publicResponse.Rounds.SelectMany(x => x.Groups).SelectMany(x => x.Matches)
            .First(x => x.Team1RegistrationId == firstId || x.Team2RegistrationId == firstId);
        var publicTeam = opening.Team1RegistrationId == firstId ? opening.Team1! : opening.Team2!;
        Assert.Equal("Tên hiện tại khác lịch sử", publicTeam.DisplayName);
        Assert.Equal("VĐV 1.1", publicTeam.Player1.Name);
        Assert.Equal(6, publicTeam.Relay!.Members.Count);
        var detail = Assert.IsType<TournamentMatchDetailResponseDto>(
            Assert.IsType<OkObjectResult>(await publicController.GetMatchDetail(opening.MatchId)).Value);
        Assert.Equal(opening.Team1Text, detail.Match.Team1Text);
        Assert.Equal(opening.Team2Text, detail.Match.Team2Text);
        var tbd = publicResponse.Rounds.SelectMany(x => x.Groups).SelectMany(x => x.Matches).First(x => !x.Team1RegistrationId.HasValue);
        Assert.Null(tbd.Team1!.Relay);

        var guard = new RelayLegacyWriteGuard(db, relayOptions);
        var registrationAdmin = new AdminRegistrationsController(db, null!, null!, guard);
        Assert.IsType<ConflictObjectResult>(await registrationAdmin.UpdatePlayers(firstId, new() { Player1Name = "Thay người trái phép" }));
        Assert.IsType<ConflictObjectResult>(await registrationAdmin.SyncLevels(firstId));
        Assert.IsType<ConflictObjectResult>(await registrationAdmin.Pair(firstId, new() { WithWaitingRegistrationId = eligible.Data.Items.Skip(1).First().RegistrationId!.Value }));
        var deleteBlocked = Assert.IsAssignableFrom<ObjectResult>(await registrationAdmin.Delete(firstId, default));
        Assert.Equal(StatusCodes.Status409Conflict, deleteBlocked.StatusCode);
        var admin = new AdminTournamentGroupMatchesController(db, null!, null!, null!,
            NullLogger<AdminTournamentGroupMatchesController>.Instance, guard);
        var scorer = new RefereeMatchesApiController(db, null!, null!, null!, NullLogger<RefereeMatchesApiController>.Instance, guard)
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("UserId", referee.UserId.ToString())], "test"))
            } }
        };
        await db.TournamentGroupMatches.Where(x => x.MatchId == opening.MatchId)
            .ExecuteUpdateAsync(x => x.SetProperty(m => m.StartAt, DateTime.Today.AddHours(9)));
        db.ChangeTracker.Clear();

        Assert.IsType<OkObjectResult>(await admin.SetScore(opening.TournamentRoundGroupId, opening.MatchId,
            new() { ScoreTeam1 = 5, ScoreTeam2 = 3, IsCompleted = false }));

        var targetReached = Assert.IsType<OkObjectResult>(await scorer.SetScore(opening.MatchId,
            new() { ScoreTeam1 = 40, ScoreTeam2 = 38, IsCompleted = false }));
        using (var targetJson = JsonDocument.Parse(JsonSerializer.Serialize(targetReached.Value)))
        {
            Assert.False(targetJson.RootElement.GetProperty("IsCompleted").GetBoolean());
            Assert.Equal(40, targetJson.RootElement.GetProperty("ScoreTeam1").GetInt32());
        }

        var openMatch = await db.TournamentGroupMatches.AsNoTracking()
            .SingleAsync(x => x.MatchId == opening.MatchId);
        Assert.False(openMatch.IsCompleted);
        Assert.Null(openMatch.WinnerRegistrationId);
        Assert.Single(await db.TournamentMatchScoreHistories.ToListAsync());
        Assert.Empty(await db.RelayMatchStates.ToListAsync());
        Assert.Empty(await db.RelayLegs.ToListAsync());
        Assert.Empty(await db.RelayMatchCommands.ToListAsync());

        Assert.IsType<BadRequestObjectResult>(await scorer.SetScore(opening.MatchId,
            new() { ScoreTeam1 = 40, ScoreTeam2 = 40, IsCompleted = true }));

        var completed = Assert.IsType<OkObjectResult>(await scorer.SetScore(opening.MatchId,
            new() { ScoreTeam1 = 40, ScoreTeam2 = 38, IsCompleted = true }));
        using (var completedJson = JsonDocument.Parse(JsonSerializer.Serialize(completed.Value)))
        {
            Assert.True(completedJson.RootElement.GetProperty("IsCompleted").GetBoolean());
            Assert.Equal(opening.Team1RegistrationId, completedJson.RootElement.GetProperty("WinnerRegistrationId").GetInt64());
        }
        Assert.Equal(2, await db.TournamentMatchScoreHistories.CountAsync());
        Assert.Empty(await db.RelayMatchStates.ToListAsync());

        var reset = await service.ResetAsync(tournament.TournamentId, new() { Reason = "Không được xóa trận đã có lịch sử điểm" }, referee.UserId, default);
        Assert.Equal("TOURNAMENT_ALREADY_STARTED", reset.ErrorCode);

        // Old mobile registration endpoints cannot create a two-player registration for a relay tournament.
        await db.Tournaments.Where(x => x.TournamentId == tournament.TournamentId).ExecuteUpdateAsync(x => x
            .SetProperty(t => t.Status, "OPEN").SetProperty(t => t.RegistrationLockedAt, (DateTime?)null));
        await using var userDb = sandbox.CreateDb();
        var userController = new TournamentRegistrationUserController(userDb, null!, null!, null!, new(userDb, relayOptions))
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("uid", referee.UserId.ToString())], "test"))
            } }
        };
        var rejected = Assert.IsType<BadRequestObjectResult>(await userController.RegisterWaitingPair(tournament.TournamentId, default));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(rejected.Value));
        Assert.Contains("tiếp sức", json.RootElement.GetProperty("message").GetString());
        Assert.Equal(7, await db.TournamentRegistrations.CountAsync());
    }

    [RelaySqlFact]
    public async Task Legacy_library_queries_work_without_relay_schema_when_feature_is_off()
    {
        await using var sandbox = await RelaySqlSandbox.CreateFullSchemaAsync();
        await using var db = sandbox.CreateDb();
        var tournament = new Tournament { Title = "Giải đôi", Status = "DRAFT", GameType = "DOUBLE", GenderCategory = "OPEN" };
        tournament.TournamentRegistrations.Add(new() { RegCode = "OLD", Player1Name = "An", Player2Name = "Bình", Success = true });
        db.Tournaments.Add(tournament);
        await db.SaveChangesAsync();
        await sandbox.SqlAsync("""
            DROP TABLE dbo.RelayBracketSeedSnapshots;
            DROP TABLE dbo.RelayMatchCommands;
            DROP TABLE dbo.RelayLegs;
            DROP TABLE dbo.RelayMatchStates;
            DROP TABLE dbo.RelayTeamMembers;
            DROP TABLE dbo.RelayTeamReserveMembers;
            DROP TABLE dbo.RelayTeams;
            DROP TABLE dbo.RelayTournamentSettings;
            """);
        var service = CreateApplicationService(db, CreateTemplateService(db));
        var eligible = await service.GetEligibleRegistrationsAsync(tournament.TournamentId, default);
        var seed = Assert.Single(eligible.Data!.Items);
        Assert.Equal("An/Bình", seed.TeamName);
        Assert.Null(seed.Relay);
        Assert.DoesNotContain("Relay", JsonSerializer.Serialize(seed));
        Assert.Empty(await service.GetApplicableTemplatesAsync(tournament.TournamentId, default));
    }
}
