using HanakaServer.Data;
using HanakaServer.Dtos.Brackets;
using HanakaServer.Helpers;
using HanakaServer.Models;
using HanakaServer.Options;
using HanakaServer.Services;
using HanakaServer.Services.Brackets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HanakaServer.Tests;

public sealed partial class BracketTemplateWorkflowIntegrationTests
{
    [Fact]
    public async Task Incomplete_TP08_draft_saves_but_cannot_publish()
    {
        await using var db = CreateDb();
        var templates = CreateTemplateService(db);
        var (_, versionId, rowVersion) = await CreateDraftAsync(templates, "TP08-REGRESSION", "CUSTOM", 2, 8, true);
        var graph = BracketTemplateService.GenerateSingleElimination(16, false, [0]);
        graph.RowVersion = rowVersion;
        graph.MinimumTeams = 2;
        graph.SeedCapacity = 8;
        foreach (var slot in graph.Rounds[0].Groups.SelectMany(x => x.Matches).SelectMany(x => x.Slots)) slot.SeedNumber = null;
        var saved = await templates.SaveGraphAsync(versionId, graph, default);
        Assert.True(saved.Success, saved.Message);
        var result = await templates.PublishAsync(versionId, null, default);
        Assert.Equal("GRAPH_INVALID", result.ErrorCode);
        Assert.Equal(16, result.Issues!.Count(x => x.Code == "INITIAL_TEAM_POSITION_REQUIRED"));
        Assert.Contains(result.Issues!, x => x.Code == "INITIAL_POSITION_CAPACITY_MISMATCH");
        Assert.Null((await db.BracketTemplates.SingleAsync()).CurrentPublishedVersionId);
        Assert.Equal("DRAFT", (await db.BracketTemplateVersions.SingleAsync()).Status);
    }

    [Theory]
    [InlineData("SINGLE")]
    [InlineData("DOUBLE")]
    [InlineData("RELAY")]
    public async Task Legacy_TP08_is_rejected_by_list_preview_and_apply_without_runtime_writes(string mode)
    {
        await using var db = CreateDb();
        var (tournament, versionId, referee, service) = await ReadinessFixtureAsync(db, 16, mode);
        await BreakLegacyTemplateAsync(db, versionId);
        await AssertLegacyRejectedAsync(db, tournament, versionId, service);
    }

    [Theory]
    [InlineData(8, "SINGLE")]
    [InlineData(8, "DOUBLE")]
    [InlineData(8, "RELAY")]
    [InlineData(16, "RELAY")]
    public async Task Valid_eight_team_bracket_resolves_opening_teams_and_remains_idempotent(int capacity, string mode)
    {
        await using var db = CreateDb();
        var (tournament, versionId, referee, service) = await ReadinessFixtureAsync(db, capacity, mode);
        await AssertReadyApplicationAsync(db, tournament, versionId, referee, service, capacity);
    }

    [RelaySqlFact]
    public async Task SQL_rejects_legacy_TP08_and_accepts_corrected_new_version_with_real_constraints()
    {
        await using var sandbox = await RelaySqlSandbox.CreateFullSchemaAsync();
        await using var db = sandbox.CreateDb();
        await AddRuntimeSourceConstraintsAsync(sandbox);
        var (tournament, versionId, referee, service) = await ReadinessFixtureAsync(db, 16, "RELAY");
        await BreakLegacyTemplateAsync(db, versionId);
        await AssertLegacyRejectedAsync(db, tournament, versionId, service);
        var templates = CreateTemplateService(db);
        var templateId = (await db.BracketTemplateVersions.SingleAsync(x => x.BracketTemplateVersionId == versionId)).BracketTemplateId;
        var draft = await templates.CreateDraftVersionAsync(templateId, null, default);
        Assert.True(draft.Success, draft.Message);
        var generated = await templates.GenerateAsync(draft.Data!.BracketTemplateVersionId,
            new() { GeneratorType = "SINGLE_ELIMINATION", TeamCount = 8 }, default);
        Assert.True(generated.Success, generated.Message);
        Assert.True((await templates.PublishAsync(draft.Data.BracketTemplateVersionId, null, default)).Success);
        Assert.Equal(16, (await templates.GetGraphAsync(versionId, default))!.Rounds[0].Groups.SelectMany(x => x.Matches).Sum(x => x.Slots.Count));
        await AssertReadyApplicationAsync(db, tournament, draft.Data.BracketTemplateVersionId, referee, service, 8);
    }

    [RelaySqlFact]
    public async Task SQL_apply_failure_rolls_back_all_generated_rounds_matches_and_snapshots()
    {
        await using var sandbox = await RelaySqlSandbox.CreateFullSchemaAsync();
        await using var db = sandbox.CreateDb();
        var (tournament, versionId, referee, service) = await ReadinessFixtureAsync(db, 8, "RELAY");
        var request = ReadinessApplyRequest(versionId, referee);
        var preview = await service.PreviewAsync(tournament, request, default);
        Assert.True(preview.Success, preview.Message);
        request.PreviewHash = preview.Data!.PreviewHash;
        await sandbox.SqlAsync("CREATE TRIGGER dbo.TR_TestBracketFailure ON dbo.TournamentGroupMatches AFTER INSERT AS BEGIN THROW 51099, 'Simulated bracket write failure', 1; END");
        Assert.Equal("APPLY_FAILED", (await service.ApplyAsync(tournament, request, null, default)).ErrorCode);
        Assert.Empty(await db.TournamentGroupMatches.ToListAsync());
        Assert.Empty(await db.TournamentRoundMaps.ToListAsync());
        Assert.Empty(await db.TournamentRoundGroups.ToListAsync());
        Assert.Empty(await db.TournamentBracketSeedAssignments.ToListAsync());
        Assert.Empty(await db.RelayBracketSeedSnapshots.ToListAsync());
        Assert.False(await db.TournamentBracketApplications.AnyAsync(x => x.IsActive));
        Assert.Equal(8, await db.TournamentRegistrations.CountAsync());
        Assert.Equal(48, await db.RelayTeamMembers.CountAsync());
    }

    private static Task AddRuntimeSourceConstraintsAsync(RelaySqlSandbox sandbox) => sandbox.SqlAsync("""
        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_TGM_Team1SourceType')
            ALTER TABLE dbo.TournamentGroupMatches ADD CONSTRAINT CK_TGM_Team1SourceType CHECK (Team1SourceType IN ('REGISTRATION','WINNER_MATCH','LOSER_MATCH','GROUP_RANK','BYE'));
        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_TGM_Team2SourceType')
            ALTER TABLE dbo.TournamentGroupMatches ADD CONSTRAINT CK_TGM_Team2SourceType CHECK (Team2SourceType IN ('REGISTRATION','WINNER_MATCH','LOSER_MATCH','GROUP_RANK','BYE'));
        """);

    private static async Task BreakLegacyTemplateAsync(PickleballDbContext db, long versionId)
    {
        var version = await db.BracketTemplateVersions.SingleAsync(x => x.BracketTemplateVersionId == versionId);
        version.SeedCapacity = 8;
        version.MinimumTeams = 2;
        foreach (var slot in await db.BracketTemplateMatchSlots.Where(x => x.SourceType == "SEED").ToListAsync()) slot.SeedNumber = null;
        await db.SaveChangesAsync();
    }

    private static async Task AssertLegacyRejectedAsync(PickleballDbContext db, long tournament, long versionId, TournamentBracketApplicationService service)
    {
        var item = Assert.Single(await service.GetApplicableTemplatesAsync(tournament, default));
        Assert.False(item.IsApplicable);
        Assert.Contains("16", item.InapplicableReason);
        var request = new ApplyTournamentBracketRequest { BracketTemplateVersionId = versionId, PreviewHash = "FORGED" };
        var preview = await service.PreviewAsync(tournament, request, default);
        Assert.Equal("GRAPH_INVALID", preview.ErrorCode);
        Assert.Equal(16, preview.Issues!.Count(x => x.Code == "INITIAL_TEAM_POSITION_REQUIRED"));
        var applied = await service.ApplyAsync(tournament, request, null, default);
        Assert.Equal("GRAPH_INVALID", applied.ErrorCode);
        Assert.NotEmpty(applied.Issues!);
        Assert.Empty(await db.TournamentBracketApplications.ToListAsync());
        Assert.Empty(await db.TournamentRoundMaps.ToListAsync());
        Assert.Empty(await db.TournamentGroupMatches.ToListAsync());
    }

    private static async Task AssertReadyApplicationAsync(PickleballDbContext db, long tournament, long versionId,
        long referee, TournamentBracketApplicationService service, int capacity)
    {
        var request = ReadinessApplyRequest(versionId, referee);
        var preview = await service.PreviewAsync(tournament, request, default);
        Assert.True(preview.Success, preview.Message);
        Assert.Equal(capacity - 8, preview.Data!.ByeCount);
        Assert.Equal(capacity - 1, preview.Data.MatchCount);
        var opening = preview.Data.Rounds[0].Groups.SelectMany(x => x.Matches).SelectMany(x => x.Slots).ToArray();
        Assert.Equal(8, opening.Count(x => x.RegistrationId.HasValue));
        Assert.All(opening, x => Assert.True(x.IsBye || x.RegistrationId.HasValue));
        request.PreviewHash = preview.Data.PreviewHash;
        var applied = await service.ApplyAsync(tournament, request, null, default);
        Assert.True(applied.Success, applied.Message);
        var second = await service.ApplyAsync(tournament, request, null, default);
        Assert.True(second.Success, second.Message);
        Assert.Equal(applied.Data!.TournamentBracketApplicationId, second.Data!.TournamentBracketApplicationId);
        Assert.Equal(capacity - 1, await db.TournamentGroupMatches.CountAsync());
        Assert.Equal(capacity - 8, await db.TournamentGroupMatches.CountAsync(x => x.CompletionReason == "BYE"));
        var match = await db.TournamentGroupMatches.FirstAsync(x => !x.IsCompleted && x.Team1RegistrationId != null && x.Team2RegistrationId != null);
        match.ScoreTeam1 = 11;
        match.ScoreTeam2 = 5;
        match.IsCompleted = true;
        match.WinnerRegistrationId = match.Team1RegistrationId;
        await db.SaveChangesAsync();
        var propagation = new TournamentBracketPropagationService(db, new TournamentStandingsService(db), NullLogger<TournamentBracketPropagationService>.Instance);
        await propagation.PropagateFromMatchAsync(match.MatchId);
        Assert.True(await db.TournamentGroupMatches.AnyAsync(x =>
            (x.Team1SourceMatchId == match.MatchId && x.Team1RegistrationId == match.WinnerRegistrationId)
            || (x.Team2SourceMatchId == match.MatchId && x.Team2RegistrationId == match.WinnerRegistrationId)));
    }

    private static ApplyTournamentBracketRequest ReadinessApplyRequest(long versionId, long referee) => new()
    {
        BracketTemplateVersionId = versionId, SeedingMethod = "REGISTRATION_ORDER",
        StartAt = TestMatchStartAt, AddressText = TestMatchAddress, RefereeUserId = referee
    };

    private static async Task<(long Tournament, long Version, long Referee, TournamentBracketApplicationService Service)>
        ReadinessFixtureAsync(PickleballDbContext db, int capacity, string mode)
    {
        var templates = CreateTemplateService(db);
        var (_, versionId, rowVersion) = await CreateDraftAsync(templates, "READINESS", "CUSTOM", 2, capacity, true,
            mode == "RELAY" ? "RELAY_TEAM" : "STANDARD");
        var graph = BracketTemplateService.GenerateSingleElimination(capacity, false, [0]);
        graph.RowVersion = rowVersion;
        Assert.True((await templates.SaveGraphAsync(versionId, graph, default)).Success);
        Assert.True((await templates.PublishAsync(versionId, null, default)).Success);
        var referee = new User { FullName = "Trọng tài thử", IsActive = true, Verified = true };
        db.Users.Add(referee);
        db.Roles.Add(new() { RoleCode = "REFEREE", RoleName = "Trọng tài" });
        var tournament = new Tournament { Title = "Tái hiện giải 37", Status = "DRAFT", GameType = mode == "SINGLE" ? "SINGLE" : "DOUBLE",
            GenderCategory = "OPEN", ExpectedTeams = 8, RegistrationLockedAt = DateTime.UtcNow };
        for (var i = 1; i <= 8; i++) tournament.TournamentRegistrations.Add(new()
        {
            RegCode = $"TEST-{i}", RegIndex = i, Success = true, Paid = i == 1,
            Player1Name = $"VĐV {i}", Player2Name = mode == "DOUBLE" ? $"Bạn đánh {i}" : null, CreatedAt = DateTime.UtcNow.AddMinutes(i)
        });
        db.Tournaments.Add(tournament);
        await db.SaveChangesAsync();
        db.Referees.Add(new() { ExternalId = referee.UserId.ToString(), FullName = referee.FullName, Verified = true, RefereeType = "MAIN" });
        if (mode == "RELAY")
        {
            db.RelayTournamentSettings.Add(new() { TournamentId = tournament.TournamentId, TeamSize = 6 });
            foreach (var registration in tournament.TournamentRegistrations)
            {
                var team = new RelayTeam { RegistrationId = registration.RegistrationId, TournamentId = tournament.TournamentId, TeamName = $"Đội {registration.RegIndex}" };
                for (var position = 1; position <= 6; position++) team.Members.Add(new()
                {
                    RegistrationId = registration.RegistrationId, Position = position, DisplayName = $"Thành viên {position}"
                });
                db.RelayTeams.Add(team);
            }
        }
        await db.SaveChangesAsync();
        var service = new TournamentBracketApplicationService(db, templates, new BracketTemplateValidationService(),
            NullLogger<TournamentBracketApplicationService>.Instance, Microsoft.Extensions.Options.Options.Create(new RelayOptions { AdminPreviewEnabled = mode == "RELAY" }));
        return (tournament.TournamentId, versionId, referee.UserId, service);
    }
}
