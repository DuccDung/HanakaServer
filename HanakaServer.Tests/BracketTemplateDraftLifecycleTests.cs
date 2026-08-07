using HanakaServer.Data;
using HanakaServer.Dtos.Brackets;
using HanakaServer.Models;
using HanakaServer.Services.Brackets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HanakaServer.Tests;

public sealed class BracketTemplateDraftLifecycleTests
{
    [Fact]
    public async Task Incomplete_graph_is_saved_and_can_be_loaded_again()
    {
        await using var db = CreateDb();
        var version = await SeedDraftVersionAsync(db);
        var service = CreateService(db);

        var result = await service.SaveGraphAsync(version.BracketTemplateVersionId, new SaveBracketTemplateGraphRequest
        {
            MinimumTeams = 2,
            SeedCapacity = 4,
            AllowBye = false,
            DefaultSeedingMethod = BracketSeedingMethods.RegistrationOrder,
            RowVersion = Convert.ToBase64String(version.RowVersion),
            Rounds = []
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.False(result.Data.Validation!.IsValid);
        Assert.Contains(result.Data.Validation.Issues, x => x.Code == "ROUND_REQUIRED");

        var persisted = await db.BracketTemplateVersions.AsNoTracking().SingleAsync();
        Assert.False(string.IsNullOrWhiteSpace(persisted.DraftGraphJson));

        var reloaded = await service.GetGraphAsync(version.BracketTemplateVersionId, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Empty(reloaded.Rounds);
        Assert.Equal(4, reloaded.SeedCapacity);
        Assert.False(reloaded.AllowBye);
        Assert.Equal(BracketSeedingMethods.RegistrationOrder, reloaded.DefaultSeedingMethod);
    }

    [Fact]
    public async Task Incomplete_draft_cannot_be_published()
    {
        await using var db = CreateDb();
        var version = await SeedDraftVersionAsync(db);
        var service = CreateService(db);
        await service.SaveGraphAsync(version.BracketTemplateVersionId, new SaveBracketTemplateGraphRequest
        {
            MinimumTeams = 2,
            SeedCapacity = 4,
            AllowBye = false,
            DefaultSeedingMethod = BracketSeedingMethods.RegistrationOrder,
            RowVersion = Convert.ToBase64String(version.RowVersion),
            Rounds = []
        }, CancellationToken.None);

        var publish = await service.PublishAsync(
            version.BracketTemplateVersionId,
            userId: 1,
            CancellationToken.None);

        Assert.False(publish.Success);
        Assert.Equal("GRAPH_INVALID", publish.ErrorCode);
        Assert.Equal(BracketTemplateStatuses.Draft,
            (await db.BracketTemplateVersions.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Next_code_uses_the_highest_existing_template_sequence()
    {
        await using var db = CreateDb();
        db.BracketTemplates.AddRange(
            NewTemplate("TP_01"),
            NewTemplate("TP_12"),
            NewTemplate("CUSTOM_CODE"));
        await db.SaveChangesAsync();

        var code = await CreateService(db).GetNextCodeAsync(CancellationToken.None);

        Assert.Equal("TP_13", code);
    }

    private static PickleballDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<PickleballDbContext>()
            .UseInMemoryDatabase($"bracket-draft-{Guid.NewGuid():N}")
            .Options;
        return new PickleballDbContext(options);
    }

    private static BracketTemplateService CreateService(PickleballDbContext db) =>
        new(db, new BracketTemplateValidationService(), NullLogger<BracketTemplateService>.Instance);

    private static BracketTemplate NewTemplate(string code) => new()
    {
        TemplateCode = code,
        TemplateName = code,
        FormatType = BracketTemplateFormatTypes.Custom,
        Status = BracketTemplateStatuses.Draft,
        CreatedAt = new DateTime(2026, 8, 4)
    };

    private static async Task<BracketTemplateVersion> SeedDraftVersionAsync(PickleballDbContext db)
    {
        var template = new BracketTemplate
        {
            BracketTemplateId = 1,
            TemplateCode = "MANUAL-TEST",
            TemplateName = "Manual test",
            FormatType = BracketTemplateFormatTypes.Custom,
            Status = BracketTemplateStatuses.Draft,
            CreatedAt = new DateTime(2026, 8, 3),
            RowVersion = [1]
        };
        var version = new BracketTemplateVersion
        {
            BracketTemplateVersionId = 1,
            BracketTemplateId = 1,
            BracketTemplate = template,
            VersionNumber = 1,
            Status = BracketTemplateStatuses.Draft,
            MinimumTeams = 2,
            SeedCapacity = 4,
            AllowBye = false,
            DefaultSeedingMethod = BracketSeedingMethods.RegistrationOrder,
            CreatedAt = new DateTime(2026, 8, 3),
            RowVersion = [2]
        };
        template.Versions.Add(version);
        db.BracketTemplates.Add(template);
        await db.SaveChangesAsync();
        return version;
    }
}
