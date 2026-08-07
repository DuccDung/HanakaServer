using HanakaServer.Data;
using HanakaServer.Dtos.Brackets;
using HanakaServer.Helpers;
using HanakaServer.Models;
using HanakaServer.Services.Brackets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HanakaServer.Tests;

public sealed class BracketTemplateWorkflowIntegrationTests
{
    private const long TestRefereeUserId = 90_001;
    private const string TestMatchAddress = "Nhà thi đấu Hanaka";
    private static readonly DateTime TestMatchStartAt = new(2026, 8, 10, 8, 30, 0);

    [Fact]
    public async Task Manual_crud_round_group_match_and_slot_preserves_draft_graph()
    {
        await using var db = CreateDb();
        var service = CreateTemplateService(db);
        var (_, versionId, rowVersion) = await CreateDraftAsync(
            service, "CRUD-MANUAL", BracketTemplateFormatTypes.Custom, 2, 4, true);

        var round = await service.AddRoundAsync(versionId, new BracketTemplateRoundMutationRequest
        {
            RowVersion = rowVersion,
            RoundKey = "R1",
            RoundLabel = "Vòng mở màn",
            RoundType = BracketRoundTypes.Knockout,
            SortOrder = 0
        }, CancellationToken.None);
        Assert.True(round.Success);

        var renamedRound = await service.UpdateRoundAsync(versionId, "R1",
            new BracketTemplateRoundMutationRequest
            {
                RowVersion = round.Data!.RowVersion,
                RoundKey = "ROUND-A",
                RoundLabel = "Vòng A",
                RoundType = BracketRoundTypes.Knockout,
                SortOrder = 0
            }, CancellationToken.None);
        Assert.True(renamedRound.Success);

        var group = await service.AddGroupAsync(versionId, "ROUND-A",
            new BracketTemplateGroupMutationRequest
            {
                RowVersion = renamedRound.Data!.RowVersion,
                GroupKey = "G1",
                GroupName = "Nhánh 1",
                GroupType = BracketGroupTypes.KnockoutBranch,
                SortOrder = 0
            }, CancellationToken.None);
        Assert.True(group.Success);

        var renamedGroup = await service.UpdateGroupAsync(versionId, "G1",
            new BracketTemplateGroupMutationRequest
            {
                RowVersion = group.Data!.RowVersion,
                GroupKey = "BRANCH-A",
                GroupName = "Nhánh A",
                GroupType = BracketGroupTypes.KnockoutBranch,
                SortOrder = 0
            }, CancellationToken.None);
        Assert.True(renamedGroup.Success);

        var match = await service.AddMatchAsync(versionId, "BRANCH-A",
            new BracketTemplateMatchMutationRequest
            {
                RowVersion = renamedGroup.Data!.RowVersion,
                MatchKey = "M1",
                MatchLabel = "Trận mở màn",
                SortOrder = 0,
                Slots =
                [
                    SeedSlot(1, 1),
                    SeedSlot(2, 2)
                ]
            }, CancellationToken.None);
        Assert.True(match.Success);

        var renamedMatch = await service.UpdateMatchAsync(versionId, "M1",
            new BracketTemplateMatchMutationRequest
            {
                RowVersion = match.Data!.RowVersion,
                MatchKey = "OPENING",
                MatchLabel = "Trận khai mạc",
                SortOrder = 0,
                Slots =
                [
                    SeedSlot(1, 1),
                    SeedSlot(2, 2)
                ]
            }, CancellationToken.None);
        Assert.True(renamedMatch.Success);

        var slot = await service.UpdateSlotAsync(versionId, "OPENING", 2,
            new BracketTemplateSlotMutationRequest
            {
                RowVersion = renamedMatch.Data!.RowVersion,
                SourceType = BracketTemplateSourceTypes.Seed,
                SeedNumber = 3
            }, CancellationToken.None);
        Assert.True(slot.Success);
        Assert.Equal(3, slot.Data!.Rounds.Single().Groups.Single().Matches.Single()
            .Slots.Single(x => x.SlotNumber == 2).SeedNumber);

        var deletedMatch = await service.DeleteMatchAsync(versionId, "OPENING",
            new BracketTemplateDeleteRequest { RowVersion = slot.Data.RowVersion },
            CancellationToken.None);
        Assert.True(deletedMatch.Success);

        var deletedGroup = await service.DeleteGroupAsync(versionId, "BRANCH-A",
            new BracketTemplateDeleteRequest { RowVersion = deletedMatch.Data!.RowVersion },
            CancellationToken.None);
        Assert.True(deletedGroup.Success);

        var deletedRound = await service.DeleteRoundAsync(versionId, "ROUND-A",
            new BracketTemplateDeleteRequest { RowVersion = deletedGroup.Data!.RowVersion },
            CancellationToken.None);
        Assert.True(deletedRound.Success);
        Assert.Empty(deletedRound.Data!.Rounds);

        var reloaded = await service.GetGraphAsync(versionId, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Empty(reloaded.Rounds);
    }

    [Fact]
    public async Task Eight_seed_knockout_can_be_saved_and_published()
    {
        await using var db = CreateDb();
        var service = CreateTemplateService(db);
        var (_, versionId, rowVersion) = await CreateDraftAsync(
            service, "KO-8", BracketTemplateFormatTypes.SingleElimination, 4, 8, true);
        var graph = BracketTemplateService.GenerateSingleElimination(8, false, [0]);
        graph.RowVersion = rowVersion;
        graph.Rounds[0].Groups[0].GroupColor = "#16876C";

        var saved = await service.SaveGraphAsync(versionId, graph, CancellationToken.None);
        Assert.True(saved.Success);
        Assert.True(saved.Data!.Validation!.IsValid);
        Assert.Equal("#16876C", saved.Data.Rounds[0].Groups[0].GroupColor);
        Assert.Equal(3, saved.Data.Rounds.Count);
        Assert.Equal(7, saved.Data.Rounds.Sum(x => x.Groups.Sum(g => g.Matches.Count)));

        var published = await service.PublishAsync(versionId, null, CancellationToken.None);
        Assert.True(published.Success);
        Assert.Equal(BracketTemplateStatuses.Published, published.Data!.Status);

        var template = await db.BracketTemplates.AsNoTracking().SingleAsync();
        var persistedVersion = await db.BracketTemplateVersions.AsNoTracking().SingleAsync();
        Assert.False(string.IsNullOrWhiteSpace(persistedVersion.ConfigurationHash));
        Assert.Equal(versionId, template.CurrentPublishedVersionId);
        Assert.Equal(BracketTemplateStatuses.Published, template.Status);

        var reloaded = await service.GetGraphAsync(versionId, CancellationToken.None);
        Assert.Equal("#16876C", reloaded!.Rounds[0].Groups[0].GroupColor);
    }

    [Fact]
    public async Task Publish_materializes_valid_json_draft_when_normalized_graph_is_missing()
    {
        await using var db = CreateDb();
        var service = CreateTemplateService(db);
        var (_, versionId, rowVersion) = await CreateDraftAsync(
            service, "KO-8-RECOVERY", BracketTemplateFormatTypes.SingleElimination, 4, 8, true);
        var graph = BracketTemplateService.GenerateSingleElimination(8, false, [0]);
        graph.RowVersion = rowVersion;

        var saved = await service.SaveGraphAsync(versionId, graph, CancellationToken.None);
        Assert.True(saved.Success);

        db.BracketTemplateMatchSlots.RemoveRange(await db.BracketTemplateMatchSlots.ToListAsync());
        db.BracketTemplateMatches.RemoveRange(await db.BracketTemplateMatches.ToListAsync());
        db.BracketTemplateGroups.RemoveRange(await db.BracketTemplateGroups.ToListAsync());
        db.BracketTemplateRounds.RemoveRange(await db.BracketTemplateRounds.ToListAsync());
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Empty(await db.BracketTemplateRounds.AsNoTracking().ToListAsync());
        Assert.False(string.IsNullOrWhiteSpace(
            (await db.BracketTemplateVersions.AsNoTracking().SingleAsync()).DraftGraphJson));

        var published = await CreateTemplateService(db).PublishAsync(versionId, null, CancellationToken.None);

        Assert.True(published.Success);
        Assert.Equal(3, await db.BracketTemplateRounds.CountAsync());
        Assert.Equal(3, await db.BracketTemplateGroups.CountAsync());
        Assert.Equal(7, await db.BracketTemplateMatches.CountAsync());
        Assert.Equal(14, await db.BracketTemplateMatchSlots.CountAsync());
        var reloaded = await CreateTemplateService(db).GetGraphAsync(versionId, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal(3, reloaded.Rounds.Count);
        Assert.Equal(7, reloaded.Rounds.Sum(x => x.Groups.Sum(g => g.Matches.Count)));
    }

    [Fact]
    public async Task Empty_initial_team_positions_can_be_saved()
    {
        await using var db = CreateDb();
        var service = CreateTemplateService(db);
        var (_, versionId, rowVersion) = await CreateDraftAsync(
            service, "EMPTY-INITIAL-POSITIONS", BracketTemplateFormatTypes.Custom, 2, 2, true);
        var graph = BracketTemplateService.GenerateSingleElimination(4, false, [0]);
        graph.RowVersion = rowVersion;
        graph.Rounds[0].Groups[0].Matches[0].Slots[0].SeedNumber = null;

        var saved = await service.SaveGraphAsync(versionId, graph, CancellationToken.None);

        Assert.True(saved.Success, saved.Message);
        Assert.True(saved.Data!.Validation!.IsValid);
        Assert.Null(saved.Data.Rounds[0].Groups[0].Matches[0].Slots[0].SeedNumber);
    }

    [Fact]
    public async Task Apply_copies_exact_runtime_and_keeps_third_place_label()
    {
        await using var db = CreateDb();
        var templateService = CreateTemplateService(db);
        var (_, versionId, rowVersion) = await CreateDraftAsync(
            templateService, "KO-8-THIRD", BracketTemplateFormatTypes.SingleElimination, 4, 8, true);
        var graph = BracketTemplateService.GenerateSingleElimination(8, true, [0]);
        graph.RowVersion = rowVersion;
        Assert.True((await templateService.SaveGraphAsync(versionId, graph, CancellationToken.None)).Success);
        Assert.True((await templateService.PublishAsync(versionId, null, CancellationToken.None)).Success);

        var tournamentId = await SeedTournamentAsync(db, 8);
        var applicationService = CreateApplicationService(db, templateService);
        var previewRequest = new TournamentBracketPreviewRequest
        {
            BracketTemplateVersionId = versionId,
            SeedingMethod = BracketSeedingMethods.RegistrationOrder
        };
        var preview = await applicationService.PreviewAsync(
            tournamentId, previewRequest, CancellationToken.None);
        Assert.True(preview.Success);
        Assert.True(preview.Data!.Validation.IsValid);

        var applied = await applicationService.ApplyAsync(tournamentId,
            new ApplyTournamentBracketRequest
            {
                BracketTemplateVersionId = versionId,
                SeedingMethod = BracketSeedingMethods.RegistrationOrder,
                PreviewHash = preview.Data.PreviewHash,
                StartAt = TestMatchStartAt,
                RefereeUserId = TestRefereeUserId,
                AddressText = TestMatchAddress
            }, null, CancellationToken.None);
        Assert.True(applied.Success, applied.Message);
        Assert.Equal(preview.Data.RoundCount, applied.Data!.GeneratedRoundCount);
        Assert.Equal(preview.Data.GroupCount, applied.Data.GeneratedGroupCount);
        Assert.Equal(preview.Data.MatchCount, applied.Data.GeneratedMatchCount);

        var generatedMatches = await db.TournamentGroupMatches.AsNoTracking().ToListAsync();
        Assert.All(generatedMatches, match =>
        {
            Assert.Equal(TestMatchStartAt, match.StartAt);
            Assert.Equal(TestRefereeUserId, match.RefereeUserId);
            Assert.Equal(TestMatchAddress, match.AddressText);
        });

        var thirdPlace = await db.TournamentGroupMatches.AsNoTracking()
            .SingleAsync(x => x.TemplateMatchKey == "THIRD-M01");
        Assert.Equal("Tranh hạng ba", thirdPlace.TemplateMatchLabel);
        Assert.True(thirdPlace.TemplateIsTerminal);
        Assert.Equal("THIRD_PLACE", thirdPlace.TemplateTerminalType);
        Assert.Equal(MatchSourceTypes.LoserMatch, thirdPlace.Team1SourceType);
        Assert.Equal(MatchSourceTypes.LoserMatch, thirdPlace.Team2SourceType);
        Assert.True(thirdPlace.Team1SourceMatchId.HasValue);
        Assert.True(thirdPlace.Team2SourceMatchId.HasValue);
    }

    [Fact]
    public async Task Random_preview_applies_after_javascript_number_round_trip()
    {
        await using var db = CreateDb();
        var templateService = CreateTemplateService(db);
        var (_, versionId, rowVersion) = await CreateDraftAsync(
            templateService, "RANDOM-JS-SAFE", BracketTemplateFormatTypes.SingleElimination, 8, 8, false);
        var graph = BracketTemplateService.GenerateSingleElimination(8, false, [0]);
        graph.RowVersion = rowVersion;
        Assert.True((await templateService.SaveGraphAsync(versionId, graph, CancellationToken.None)).Success);
        Assert.True((await templateService.PublishAsync(versionId, null, CancellationToken.None)).Success);

        var tournamentId = await SeedTournamentAsync(db, 8);
        var applicationService = CreateApplicationService(db, templateService);
        var preview = await applicationService.PreviewAsync(tournamentId,
            new TournamentBracketPreviewRequest
            {
                BracketTemplateVersionId = versionId,
                SeedingMethod = BracketSeedingMethods.Random
            }, CancellationToken.None);
        Assert.True(preview.Success, preview.Message);
        Assert.True(preview.Data!.RandomSeed.HasValue);

        var browserRoundTrippedSeed = (long)(double)preview.Data.RandomSeed.Value;
        Assert.Equal(preview.Data.RandomSeed.Value, browserRoundTrippedSeed);

        var applied = await applicationService.ApplyAsync(tournamentId,
            new ApplyTournamentBracketRequest
            {
                BracketTemplateVersionId = versionId,
                SeedingMethod = BracketSeedingMethods.Random,
                RandomSeed = browserRoundTrippedSeed,
                PreviewHash = preview.Data.PreviewHash,
                StartAt = TestMatchStartAt,
                RefereeUserId = TestRefereeUserId,
                AddressText = TestMatchAddress
            }, null, CancellationToken.None);

        Assert.True(applied.Success, applied.Message);
        Assert.Equal(BracketSeedingMethods.Random, applied.Data!.SeedingMethod);
        Assert.Equal(browserRoundTrippedSeed, applied.Data.RandomSeed);
    }

    [Fact]
    public async Task Group_stage_sources_are_copied_into_knockout_runtime()
    {
        await using var db = CreateDb();
        var templateService = CreateTemplateService(db);
        var (_, versionId, rowVersion) = await CreateDraftAsync(
            templateService, "GROUP-KO", BracketTemplateFormatTypes.GroupKnockout, 8, 8, false);
        var graph = BracketTemplateService.GenerateGroupKnockout(2, 4, false, [0]);
        graph.RowVersion = rowVersion;
        Assert.True((await templateService.SaveGraphAsync(versionId, graph, CancellationToken.None)).Success);
        Assert.True((await templateService.PublishAsync(versionId, null, CancellationToken.None)).Success);

        var tournamentId = await SeedTournamentAsync(db, 8);
        var applicationService = CreateApplicationService(db, templateService);
        var preview = await applicationService.PreviewAsync(tournamentId,
            new TournamentBracketPreviewRequest
            {
                BracketTemplateVersionId = versionId,
                SeedingMethod = BracketSeedingMethods.RegistrationOrder
            }, CancellationToken.None);
        Assert.True(preview.Success);

        var applied = await applicationService.ApplyAsync(tournamentId,
            new ApplyTournamentBracketRequest
            {
                BracketTemplateVersionId = versionId,
                SeedingMethod = BracketSeedingMethods.RegistrationOrder,
                PreviewHash = preview.Data!.PreviewHash,
                StartAt = TestMatchStartAt,
                RefereeUserId = TestRefereeUserId,
                AddressText = TestMatchAddress
            }, null, CancellationToken.None);
        Assert.True(applied.Success, applied.Message);

        var firstKnockout = await db.TournamentGroupMatches.AsNoTracking()
            .Where(x => x.Team1SourceType == MatchSourceTypes.GroupRank
                        || x.Team2SourceType == MatchSourceTypes.GroupRank)
            .OrderBy(x => x.MatchId)
            .FirstAsync();
        Assert.Equal(MatchSourceTypes.GroupRank, firstKnockout.Team1SourceType);
        Assert.Equal(MatchSourceTypes.GroupRank, firstKnockout.Team2SourceType);
        Assert.True(firstKnockout.Team1SourceGroupId.HasValue);
        Assert.True(firstKnockout.Team2SourceGroupId.HasValue);
        Assert.True(firstKnockout.Team1SourceRank.HasValue);
        Assert.True(firstKnockout.Team2SourceRank.HasValue);
    }

    [Fact]
    public async Task Registration_change_invalidates_previous_preview()
    {
        await using var db = CreateDb();
        var templateService = CreateTemplateService(db);
        var (_, versionId, rowVersion) = await CreateDraftAsync(
            templateService, "PREVIEW-STALE", BracketTemplateFormatTypes.SingleElimination, 4, 8, true);
        var graph = BracketTemplateService.GenerateSingleElimination(8, false, [0]);
        graph.RowVersion = rowVersion;
        Assert.True((await templateService.SaveGraphAsync(versionId, graph, CancellationToken.None)).Success);
        Assert.True((await templateService.PublishAsync(versionId, null, CancellationToken.None)).Success);

        var tournamentId = await SeedTournamentAsync(db, 8);
        var applicationService = CreateApplicationService(db, templateService);
        var preview = await applicationService.PreviewAsync(tournamentId,
            new TournamentBracketPreviewRequest
            {
                BracketTemplateVersionId = versionId,
                SeedingMethod = BracketSeedingMethods.RegistrationOrder
            }, CancellationToken.None);
        Assert.True(preview.Success);

        var changedRegistration = await db.TournamentRegistrations
            .OrderBy(x => x.RegistrationId)
            .FirstAsync();
        changedRegistration.Player1Name = "Tên đội đã thay đổi";
        await db.SaveChangesAsync();

        var applied = await applicationService.ApplyAsync(tournamentId,
            new ApplyTournamentBracketRequest
            {
                BracketTemplateVersionId = versionId,
                SeedingMethod = BracketSeedingMethods.RegistrationOrder,
                PreviewHash = preview.Data!.PreviewHash
            }, null, CancellationToken.None);

        Assert.False(applied.Success);
        Assert.Equal("PREVIEW_CHANGED", applied.ErrorCode);
        Assert.Empty(await db.TournamentBracketApplications.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Reset_and_reseed_keep_previous_application_and_seed_snapshot()
    {
        await using var db = CreateDb();
        var templateService = CreateTemplateService(db);
        var (_, versionId, rowVersion) = await CreateDraftAsync(
            templateService, "RESET-RESEED", BracketTemplateFormatTypes.SingleElimination, 4, 8, true);
        var graph = BracketTemplateService.GenerateSingleElimination(8, false, [0]);
        graph.RowVersion = rowVersion;
        Assert.True((await templateService.SaveGraphAsync(versionId, graph, CancellationToken.None)).Success);
        Assert.True((await templateService.PublishAsync(versionId, null, CancellationToken.None)).Success);

        var tournamentId = await SeedTournamentAsync(db, 8);
        var applicationService = CreateApplicationService(db, templateService);
        var firstPreview = await applicationService.PreviewAsync(tournamentId,
            new TournamentBracketPreviewRequest
            {
                BracketTemplateVersionId = versionId,
                SeedingMethod = BracketSeedingMethods.RegistrationOrder
            }, CancellationToken.None);
        var firstApply = await applicationService.ApplyAsync(tournamentId,
            new ApplyTournamentBracketRequest
            {
                BracketTemplateVersionId = versionId,
                SeedingMethod = BracketSeedingMethods.RegistrationOrder,
                PreviewHash = firstPreview.Data!.PreviewHash,
                StartAt = TestMatchStartAt,
                RefereeUserId = TestRefereeUserId,
                AddressText = TestMatchAddress
            }, null, CancellationToken.None);
        Assert.True(firstApply.Success, firstApply.Message);
        var firstApplicationId = firstApply.Data!.TournamentBracketApplicationId;

        var reset = await applicationService.ResetAsync(tournamentId,
            new ResetTournamentBracketRequest { Reason = "Sắp xếp lại seed" },
            null, CancellationToken.None);
        Assert.True(reset.Success, reset.Message);
        Assert.Empty(await db.TournamentRoundMaps.AsNoTracking().ToListAsync());
        Assert.Empty(await db.TournamentRoundGroups.AsNoTracking().ToListAsync());
        Assert.Empty(await db.TournamentGroupMatches.AsNoTracking().ToListAsync());

        var oldApplication = await db.TournamentBracketApplications.AsNoTracking()
            .SingleAsync(x => x.TournamentBracketApplicationId == firstApplicationId);
        Assert.False(oldApplication.IsActive);
        Assert.Equal(BracketApplicationStatuses.Reverted, oldApplication.Status);
        Assert.Equal(8, await db.TournamentBracketSeedAssignments.AsNoTracking()
            .CountAsync(x => x.TournamentBracketApplicationId == firstApplicationId));

        var secondPreview = await applicationService.PreviewAsync(tournamentId,
            new TournamentBracketPreviewRequest
            {
                BracketTemplateVersionId = versionId,
                SeedingMethod = BracketSeedingMethods.Ranking
            }, CancellationToken.None);
        var secondApply = await applicationService.ApplyAsync(tournamentId,
            new ApplyTournamentBracketRequest
            {
                BracketTemplateVersionId = versionId,
                SeedingMethod = BracketSeedingMethods.Ranking,
                PreviewHash = secondPreview.Data!.PreviewHash,
                StartAt = TestMatchStartAt,
                RefereeUserId = TestRefereeUserId,
                AddressText = TestMatchAddress
            }, null, CancellationToken.None);
        Assert.True(secondApply.Success, secondApply.Message);
        Assert.NotEqual(firstApplicationId, secondApply.Data!.TournamentBracketApplicationId);

        var history = await applicationService.GetApplicationHistoryAsync(
            tournamentId, CancellationToken.None);
        Assert.Equal(2, history.Count);
        Assert.Single(history, x => x.IsActive);
        Assert.Single(history, x => x.Status == BracketApplicationStatuses.Reverted);
    }

    private static PickleballDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<PickleballDbContext>()
            .UseInMemoryDatabase($"bracket-workflow-{Guid.NewGuid():N}")
            .Options;
        return new PickleballDbContext(options);
    }

    private static BracketTemplateService CreateTemplateService(PickleballDbContext db) =>
        new(db, new BracketTemplateValidationService(), NullLogger<BracketTemplateService>.Instance);

    private static TournamentBracketApplicationService CreateApplicationService(
        PickleballDbContext db,
        BracketTemplateService templateService) =>
        new(
            db,
            templateService,
            new BracketTemplateValidationService(),
            NullLogger<TournamentBracketApplicationService>.Instance);

    private static async Task<(long TemplateId, long VersionId, string RowVersion)> CreateDraftAsync(
        BracketTemplateService service,
        string code,
        string formatType,
        int minimumTeams,
        int seedCapacity,
        bool allowBye)
    {
        var created = await service.CreateAsync(new CreateBracketTemplateRequest
        {
            TemplateCode = code,
            TemplateName = code,
            FormatType = formatType,
            MinimumTeams = minimumTeams,
            SeedCapacity = seedCapacity,
            AllowBye = allowBye,
            DefaultSeedingMethod = BracketSeedingMethods.RegistrationOrder
        }, null, CancellationToken.None);
        Assert.True(created.Success, created.Message);
        var version = created.Data!.Versions.Single();
        return (created.Data.BracketTemplateId, version.BracketTemplateVersionId, version.RowVersion);
    }

    private static async Task<long> SeedTournamentAsync(PickleballDbContext db, int teamCount)
    {
        db.Roles.Add(new Role
        {
            RoleCode = "REFEREE",
            RoleName = "Trọng tài"
        });
        db.Users.Add(new User
        {
            UserId = TestRefereeUserId,
            FullName = "Trọng tài kiểm thử",
            IsActive = true,
            Verified = true,
            CreatedAt = new DateTime(2026, 8, 3)
        });
        db.Referees.Add(new Referee
        {
            ExternalId = TestRefereeUserId.ToString(),
            FullName = "Trọng tài kiểm thử",
            Verified = true,
            RefereeType = "MAIN",
            CreatedAt = new DateTime(2026, 8, 3)
        });

        var tournament = new Tournament
        {
            Status = "DRAFT",
            Title = "Integration tournament",
            GenderCategory = "OPEN",
            GameType = "DOUBLE",
            RegistrationFeeAmount = 0,
            RegistrationFeeCurrency = "VND",
            ExpectedTeams = teamCount,
            CreatedAt = new DateTime(2026, 8, 3),
            RegistrationLockedAt = new DateTime(2026, 8, 3)
        };
        for (var index = 1; index <= teamCount; index++)
        {
            tournament.TournamentRegistrations.Add(new TournamentRegistration
            {
                RegIndex = index,
                RegCode = $"REG-{index:00}",
                RegTime = new DateTime(2026, 8, 3).AddMinutes(index),
                Player1Name = $"P{index}A",
                Player2Name = $"P{index}B",
                Player1Level = index,
                Player2Level = index,
                Points = index * 10,
                Success = true,
                Paid = true,
                WaitingPair = false,
                CreatedAt = new DateTime(2026, 8, 3).AddMinutes(index)
            });
        }

        db.Tournaments.Add(tournament);
        await db.SaveChangesAsync();
        return tournament.TournamentId;
    }

    private static BracketTemplateSlotInput SeedSlot(byte slotNumber, int seedNumber) =>
        new()
        {
            SlotNumber = slotNumber,
            SourceType = BracketTemplateSourceTypes.Seed,
            SeedNumber = seedNumber
        };
}
