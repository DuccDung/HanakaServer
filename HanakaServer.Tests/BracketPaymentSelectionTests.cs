using HanakaServer.Controllers;
using HanakaServer.Data;
using HanakaServer.Dtos.Brackets;
using HanakaServer.Models;
using HanakaServer.Options;
using HanakaServer.Services;
using HanakaServer.Services.Brackets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace HanakaServer.Tests;

public sealed partial class BracketTemplateWorkflowIntegrationTests
{
    [Theory]
    [InlineData("SINGLE", false)]
    [InlineData("SINGLE", true)]
    [InlineData("DOUBLE", false)]
    [InlineData("DOUBLE", true)]
    [InlineData("RELAY", false)]
    [InlineData("RELAY", true)]
    public async Task Payment_selection_is_consistent_from_list_and_templates_to_applied_seeds(string mode, bool excludeUnpaid)
    {
        await using var db = CreateDb();
        var (tournamentId, versionId, service) = await CreatePaymentSelectionFixtureAsync(db, mode);
        var list = await service.GetEligibleRegistrationsAsync(tournamentId, default);
        Assert.True(list.Success, list.Message);
        Assert.Equal(100, list.Data!.RegistrationFeeAmount);
        Assert.Equal(4, list.Data.Items.Count);
        Assert.Equal(2, list.Data.Items.Count(x => !x.Paid));

        var expectedCount = excludeUnpaid ? 2 : 4;
        var templates = await service.GetApplicableTemplatesAsync(tournamentId, default, excludeUnpaid);
        Assert.Equal(expectedCount, Assert.Single(templates).EligibleTeamCount);
        Assert.True(templates[0].IsApplicable);
        var request = PaymentApplyRequest(versionId, excludeUnpaid);
        var preview = await service.PreviewAsync(tournamentId, request, default);
        Assert.True(preview.Success, preview.Message);
        Assert.Equal(expectedCount, preview.Data!.EligibleRegistrationCount);
        Assert.Equal(excludeUnpaid ? 2 : 0, preview.Data.ExcludedUnpaidRegistrationCount);
        Assert.Equal(excludeUnpaid, preview.Data.ExcludeUnpaidTeams);
        var expectedIds = list.Data.Items.Where(x => !excludeUnpaid || x.Paid).Select(x => x.RegistrationId).Order().ToArray();
        Assert.Equal(expectedIds, preview.Data.Seeds.Where(x => x.RegistrationId.HasValue).Select(x => x.RegistrationId).Order());

        request.PreviewHash = preview.Data.PreviewHash;
        var applied = await service.ApplyAsync(tournamentId, request, TestRefereeUserId, default);
        Assert.True(applied.Success, applied.Message);
        Assert.Equal(expectedIds, applied.Data!.Seeds.Where(x => x.RegistrationId.HasValue).Select(x => x.RegistrationId).Order());
        Assert.Equal(4, await db.TournamentRegistrations.CountAsync(x => x.TournamentId == tournamentId));
        Assert.Equal(2, await db.TournamentRegistrations.CountAsync(x => x.TournamentId == tournamentId && !x.Paid));
        var snapshotIds = applied.Data.Seeds.Select(x => x.RegistrationId).ToArray();
        foreach (var registration in await db.TournamentRegistrations.ToListAsync()) registration.Paid = true;
        await db.SaveChangesAsync();
        var recovered = await service.GetActiveApplicationAsync(tournamentId, default);
        Assert.Equal(snapshotIds, recovered!.Seeds.Select(x => x.RegistrationId));
    }

    [Fact]
    public async Task Registration_API_keeps_the_data_array_and_exposes_the_tournament_fee()
    {
        await using var db = CreateDb();
        var (tournamentId, _, service) = await CreatePaymentSelectionFixtureAsync(db);
        var controller = new AdminTournamentBracketApplicationsController(service,
            new TournamentBracketPropagationService(db, new TournamentStandingsService(db), NullLogger<TournamentBracketPropagationService>.Instance));
        var response = Assert.IsType<OkObjectResult>(await controller.EligibleRegistrations(tournamentId, default));
        var json = JsonSerializer.SerializeToElement(response.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(4, json.GetProperty("data").GetArrayLength());
        Assert.Equal(100, json.GetProperty("registrationFeeAmount").GetDecimal());
        Assert.Equal(2, json.GetProperty("data").EnumerateArray().Count(x => !x.GetProperty("paid").GetBoolean()));
    }

    [Fact]
    public async Task Free_tournaments_keep_unpaid_teams_even_when_payment_filter_is_requested()
    {
        await using var db = CreateDb();
        var (tournamentId, versionId, service) = await CreatePaymentSelectionFixtureAsync(db);
        (await db.Tournaments.FindAsync(tournamentId))!.RegistrationFeeAmount = 0;
        foreach (var registration in await db.TournamentRegistrations.ToListAsync()) registration.Paid = false;
        await db.SaveChangesAsync();
        var request = PaymentApplyRequest(versionId, true);
        var preview = await service.PreviewAsync(tournamentId, request, default);
        Assert.True(preview.Success, preview.Message);
        Assert.Equal(4, preview.Data!.EligibleRegistrationCount);
        Assert.Equal(0, preview.Data.ExcludedUnpaidRegistrationCount);
        Assert.Equal(4, Assert.Single(await service.GetApplicableTemplatesAsync(tournamentId, default, true)).EligibleTeamCount);
        request.PreviewHash = preview.Data.PreviewHash;
        Assert.True((await service.ApplyAsync(tournamentId, request, null, default)).Success);
    }

    [Fact]
    public async Task Changing_payment_filter_invalidates_preview_even_when_every_team_has_paid()
    {
        await using var db = CreateDb();
        var (tournamentId, versionId, service) = await CreatePaymentSelectionFixtureAsync(db);
        foreach (var registration in await db.TournamentRegistrations.ToListAsync()) registration.Paid = true;
        await db.SaveChangesAsync();
        var request = PaymentApplyRequest(versionId);
        var preview = await service.PreviewAsync(tournamentId, request, default);
        Assert.True(preview.Success, preview.Message);
        request.PreviewHash = preview.Data!.PreviewHash;
        request.ExcludeUnpaidTeams = true;
        var changedPreview = await service.PreviewAsync(tournamentId, request, default);
        Assert.True(changedPreview.Success, changedPreview.Message);
        Assert.NotEqual(request.PreviewHash, changedPreview.Data!.PreviewHash);
        Assert.Equal("PREVIEW_CHANGED", (await service.ApplyAsync(tournamentId, request, null, default)).ErrorCode);
        Assert.Empty(await db.TournamentBracketApplications.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Payment_updates_between_preview_and_apply_require_a_new_preview(bool excludeUnpaid)
    {
        await using var db = CreateDb();
        var (tournamentId, versionId, service) = await CreatePaymentSelectionFixtureAsync(db);
        var request = PaymentApplyRequest(versionId, excludeUnpaid);
        var preview = await service.PreviewAsync(tournamentId, request, default);
        Assert.True(preview.Success, preview.Message);
        request.PreviewHash = preview.Data!.PreviewHash;
        (await db.TournamentRegistrations.FirstAsync(x => !x.Paid)).Paid = true;
        await db.SaveChangesAsync();
        Assert.Equal("PREVIEW_CHANGED", (await service.ApplyAsync(tournamentId, request, null, default)).ErrorCode);
        Assert.Empty(await db.TournamentGroupMatches.ToListAsync());
    }

    [Fact]
    public async Task Default_selection_does_not_silently_drop_unpaid_teams_to_fit_template_capacity()
    {
        await using var db = CreateDb();
        var (tournamentId, versionId, service) = await CreatePaymentSelectionFixtureAsync(db);
        db.TournamentRegistrations.Add(new() { TournamentId = tournamentId, RegCode = "EXTRA", RegIndex = 5,
            Player1Name = "An", Player2Name = "Bình", Success = true, Paid = false });
        await db.SaveChangesAsync();
        var template = Assert.Single(await service.GetApplicableTemplatesAsync(tournamentId, default));
        Assert.Equal(5, template.EligibleTeamCount);
        Assert.False(template.IsApplicable);
        Assert.False((await service.PreviewAsync(tournamentId, PaymentApplyRequest(versionId), default)).Success);
        Assert.True((await service.PreviewAsync(tournamentId, PaymentApplyRequest(versionId, true), default)).Success);
    }

    [Fact]
    public async Task Payment_option_preserves_roster_guards_and_rejects_an_empty_paid_selection()
    {
        await using var db = CreateDb();
        var (tournamentId, versionId, service) = await CreatePaymentSelectionFixtureAsync(db, "RELAY");
        var registrations = await db.TournamentRegistrations.OrderBy(x => x.RegIndex).ToListAsync();
        registrations[0].WaitingPair = true;
        registrations[1].Success = false;
        db.RelayTeamMembers.Remove(await db.RelayTeamMembers.FirstAsync(x => x.RegistrationId == registrations[2].RegistrationId));
        await db.SaveChangesAsync();
        var list = await service.GetEligibleRegistrationsAsync(tournamentId, default);
        Assert.Equal(registrations[3].RegistrationId, Assert.Single(list.Data!.Items).RegistrationId);
        Assert.False(Assert.Single(await service.GetApplicableTemplatesAsync(tournamentId, default, true)).IsApplicable);
        Assert.False((await service.PreviewAsync(tournamentId, PaymentApplyRequest(versionId, true), default)).Success);
        Assert.Empty(await db.TournamentBracketApplications.ToListAsync());
    }

    private static ApplyTournamentBracketRequest PaymentApplyRequest(long versionId, bool excludeUnpaid = false) => new()
    {
        BracketTemplateVersionId = versionId,
        ExcludeUnpaidTeams = excludeUnpaid,
        SeedingMethod = BracketSeedingMethods.RegistrationOrder,
        StartAt = TestMatchStartAt,
        RefereeUserId = TestRefereeUserId,
        AddressText = TestMatchAddress
    };

    private static async Task<(long TournamentId, long VersionId, TournamentBracketApplicationService Service)>
        CreatePaymentSelectionFixtureAsync(PickleballDbContext db, string mode = "DOUBLE")
    {
        var templates = CreateTemplateService(db);
        var (_, versionId, rowVersion) = await CreateDraftAsync(templates, "PAYMENT-4",
            BracketTemplateFormatTypes.SingleElimination, 2, 4, true,
            mode == "RELAY" ? BracketTemplateParticipantModes.RelayTeam : BracketTemplateParticipantModes.Standard);
        var graph = BracketTemplateService.GenerateSingleElimination(4, false, [0]);
        graph.RowVersion = rowVersion;
        Assert.True((await templates.SaveGraphAsync(versionId, graph, default)).Success);
        Assert.True((await templates.PublishAsync(versionId, null, default)).Success);
        var tournamentId = await SeedTournamentAsync(db, 4);
        var tournament = (await db.Tournaments.FindAsync(tournamentId))!;
        tournament.RegistrationFeeAmount = 100;
        tournament.GameType = mode == "SINGLE" ? "SINGLE" : "DOUBLE";
        if (mode == "RELAY") db.RelayTournamentSettings.Add(new() { TournamentId = tournamentId, TeamSize = 4 });
        foreach (var registration in tournament.TournamentRegistrations)
        {
            registration.Paid = registration.RegIndex <= 2;
            if (mode == "SINGLE" || mode == "RELAY") registration.Player2Name = null;
            if (mode != "RELAY") continue;
            var team = new RelayTeam { RegistrationId = registration.RegistrationId, TournamentId = tournamentId, TeamName = $"Đội {registration.RegIndex}" };
            foreach (var position in Enumerable.Range(1, 4)) team.Members.Add(new()
            {
                RegistrationId = registration.RegistrationId, Position = position, DisplayName = $"VĐV {registration.RegIndex}.{position}"
            });
            db.RelayTeams.Add(team);
        }
        await db.SaveChangesAsync();
        var service = new TournamentBracketApplicationService(db, templates, new BracketTemplateValidationService(),
            NullLogger<TournamentBracketApplicationService>.Instance,
            Microsoft.Extensions.Options.Options.Create(new RelayOptions { AdminPreviewEnabled = mode == "RELAY" }));
        return (tournamentId, versionId, service);
    }
}
