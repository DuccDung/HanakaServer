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
    public async Task Settings_update_keeps_manual_team_range_for_published_version()
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

        Assert.True(result.Success, result.Message);
        Assert.Equal("Giải 20 đội", result.Data!.TemplateName);
        Assert.Equal(8, result.Data.MinimumTeams);
        Assert.Equal(20, result.Data.SeedCapacity);
        var graph = await service.GetGraphAsync(version.BracketTemplateVersionId, CancellationToken.None);
        Assert.NotNull(graph);
        Assert.Equal(8, graph.MinimumTeams);
        Assert.Equal(20, graph.SeedCapacity);
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
