using HanakaServer.Data;
using HanakaServer.Dtos.Brackets;
using HanakaServer.Models;
using HanakaServer.Services.Brackets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HanakaServer.Tests;

public sealed class BracketTemplateLibraryManagementTests
{
    [Fact]
    public async Task List_propagates_caller_cancellation()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ListAsync(null, null, null, null, 1, 20, cancellation.Token));
    }

    [Fact]
    public async Task Relay_template_is_created_and_filtered_by_participant_mode()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var created = await service.CreateAsync(new CreateBracketTemplateRequest
        {
            TemplateCode = "RELAY_ONLY",
            TemplateName = "Sơ đồ tiếp sức",
            FormatType = BracketTemplateFormatTypes.SingleElimination,
            ParticipantMode = BracketTemplateParticipantModes.RelayTeam,
            MinimumTeams = 4,
            SeedCapacity = 8
        }, null, CancellationToken.None);

        Assert.True(created.Success, created.Message);
        Assert.Equal(BracketTemplateParticipantModes.RelayTeam, created.Data!.ParticipantMode);
        var relayPage = await service.ListAsync(null, null, null,
            BracketTemplateParticipantModes.RelayTeam, 1, 20, CancellationToken.None);
        var standardPage = await service.ListAsync(null, null, null,
            BracketTemplateParticipantModes.Standard, 1, 20, CancellationToken.None);
        Assert.Single(relayPage.Items);
        Assert.Empty(standardPage.Items);
    }

    [Fact]
    public async Task Participant_mode_cannot_change_after_publish()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var detail = await CreateTemplateAsync(service, "MODE_LOCKED");
        var template = await db.BracketTemplates.SingleAsync();
        var version = await db.BracketTemplateVersions.SingleAsync();
        template.Status = BracketTemplateStatuses.Published;
        template.CurrentPublishedVersionId = version.BracketTemplateVersionId;
        version.Status = BracketTemplateStatuses.Published;
        await db.SaveChangesAsync();
        detail = (await service.GetAsync(template.BracketTemplateId, CancellationToken.None))!;

        var result = await service.UpdateSettingsAsync(template.BracketTemplateId,
            new UpdateBracketTemplateSettingsRequest
            {
                TemplateName = detail.TemplateName,
                ParticipantMode = BracketTemplateParticipantModes.RelayTeam,
                MinimumTeams = detail.MinimumTeams!.Value,
                SeedCapacity = detail.SeedCapacity!.Value,
                RowVersion = detail.RowVersion
            }, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("PARTICIPANT_MODE_LOCKED", result.ErrorCode);
    }

    [Fact]
    public async Task Published_capacity_changes_are_rejected_but_renaming_preserves_version()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var detail = await CreateTemplateAsync(service, "MANUAL_RANGE");
        var version = await db.BracketTemplateVersions.SingleAsync();
        var template = await db.BracketTemplates.SingleAsync();
        version.Status = BracketTemplateStatuses.Published;
        template.Status = BracketTemplateStatuses.Published;
        template.CurrentPublishedVersionId = version.BracketTemplateVersionId;
        await db.SaveChangesAsync();
        detail = (await service.GetAsync(template.BracketTemplateId, CancellationToken.None))!;

        var result = await service.UpdateSettingsAsync(template.BracketTemplateId,
            new UpdateBracketTemplateSettingsRequest
            {
                TemplateName = "Giải 20 đội",
                MinimumTeams = 8,
                SeedCapacity = 20,
                RowVersion = detail.RowVersion
            }, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("VERSION_IMMUTABLE", result.ErrorCode);
        Assert.Equal(detail.TemplateName, template.TemplateName);
        var graph = await service.GetGraphAsync(version.BracketTemplateVersionId, CancellationToken.None);
        Assert.NotNull(graph);
        Assert.Equal(detail.MinimumTeams, graph.MinimumTeams);
        Assert.Equal(detail.SeedCapacity, graph.SeedCapacity);
        var before = System.Text.Json.JsonSerializer.Serialize(graph);
        result = await service.UpdateSettingsAsync(template.BracketTemplateId, new()
        {
            TemplateName = "Tên hiển thị mới", MinimumTeams = graph.MinimumTeams,
            SeedCapacity = graph.SeedCapacity, RowVersion = detail.RowVersion
        }, null, default);
        Assert.True(result.Success, result.Message);
        Assert.Equal("Tên hiển thị mới", result.Data!.TemplateName);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(await service.GetGraphAsync(version.BracketTemplateVersionId, default)));
    }

    [Fact]
    public async Task Settings_update_rejects_minimum_above_maximum()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var detail = await CreateTemplateAsync(service, "INVALID_RANGE");

        var result = await service.UpdateSettingsAsync(detail.BracketTemplateId,
            new UpdateBracketTemplateSettingsRequest
            {
                TemplateName = detail.TemplateName,
                MinimumTeams = 21,
                SeedCapacity = 20,
                RowVersion = detail.RowVersion
            }, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("TEAM_RANGE_INVALID", result.ErrorCode);
    }

    [Fact]
    public async Task Unused_published_template_can_be_deleted()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var detail = await CreateTemplateAsync(service, "DELETE_UNUSED");
        var version = await db.BracketTemplateVersions.SingleAsync();
        var template = await db.BracketTemplates.SingleAsync();
        version.Status = BracketTemplateStatuses.Published;
        template.Status = BracketTemplateStatuses.Published;
        template.CurrentPublishedVersionId = version.BracketTemplateVersionId;
        await db.SaveChangesAsync();
        detail = (await service.GetAsync(template.BracketTemplateId, CancellationToken.None))!;

        var result = await service.DeleteAsync(template.BracketTemplateId,
            new DeleteBracketTemplateRequest
            {
                RowVersion = detail.RowVersion,
                Confirmation = "XOA"
            }, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Empty(await db.BracketTemplates.ToListAsync());
        Assert.Empty(await db.BracketTemplateVersions.ToListAsync());
    }

    [Fact]
    public async Task Used_template_cannot_be_deleted()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var detail = await CreateTemplateAsync(service, "DELETE_USED");
        var version = await db.BracketTemplateVersions.SingleAsync();
        db.Tournaments.Add(new Tournament
        {
            TournamentId = 99,
            Status = "ACTIVE",
            Title = "Tournament using template",
            GenderCategory = "OPEN",
            RegistrationFeeCurrency = "VND",
            CreatedAt = DateTime.UtcNow
        });
        db.TournamentBracketApplications.Add(new TournamentBracketApplication
        {
            TournamentBracketApplicationId = 500,
            TournamentId = 99,
            BracketTemplateId = detail.BracketTemplateId,
            BracketTemplateVersionId = version.BracketTemplateVersionId,
            Status = BracketApplicationStatuses.Reverted,
            IsActive = false,
            SeedingMethod = BracketSeedingMethods.RegistrationOrder,
            SeedCapacity = 2,
            PreviewHash = "TEST",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await service.DeleteAsync(detail.BracketTemplateId,
            new DeleteBracketTemplateRequest
            {
                RowVersion = detail.RowVersion,
                Confirmation = "XOA"
            }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("TEMPLATE_IN_USE", result.ErrorCode);
        Assert.True(await db.BracketTemplates.AnyAsync());
    }

    private static PickleballDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<PickleballDbContext>()
            .UseInMemoryDatabase($"template-library-{Guid.NewGuid():N}")
            .Options;
        return new PickleballDbContext(options);
    }

    private static BracketTemplateService CreateService(PickleballDbContext db) =>
        new(db, new BracketTemplateValidationService(), NullLogger<BracketTemplateService>.Instance);

    private static async Task<BracketTemplateDetailDto> CreateTemplateAsync(
        BracketTemplateService service,
        string code)
    {
        var result = await service.CreateAsync(new CreateBracketTemplateRequest
        {
            TemplateCode = code,
            TemplateName = code,
            FormatType = BracketTemplateFormatTypes.Custom
        }, null, CancellationToken.None);
        Assert.True(result.Success, result.Message);
        return result.Data!;
    }
}
