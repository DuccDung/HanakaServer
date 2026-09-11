using HanakaServer.Controllers;
using HanakaServer.Data;
using HanakaServer.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HanakaServer.Tests;

public sealed class AdminTournamentRelayCreationTests
{
    [Fact]
    public async Task Create_relay_tournament_persists_selected_rules_and_returns_relay_metadata()
    {
        await using var db = NewDb();
        var controller = NewController(db, relayEnabled: true);

        var response = Assert.IsType<OkObjectResult>(await controller.Create(new CreateTournamentRequest
        {
            Title = "Giải tiếp sức 8 người",
            GameType = "DOUBLE",
            GenderCategory = "OPEN",
            Status = "DRAFT",
            IsRelay = true,
            RelayTeamSize = 8,
            RelayTargetScore = 55,
            RegistrationFeeAmount = 800_000
        }));

        var dto = Assert.IsType<TournamentListItemDto>(response.Value);
        Assert.True(dto.IsRelay);
        Assert.Equal(8, dto.RelayTeamSize);
        Assert.Equal(4, dto.RelayPairCount);
        Assert.Equal(55, dto.RelayTargetScore);
        Assert.False(dto.RelayIsEnabled);
        Assert.Equal(1, dto.RelayVersion);

        var settings = await db.RelayTournamentSettings.SingleAsync();
        Assert.Equal(dto.TournamentId, settings.TournamentId);
        Assert.Equal(600, settings.LegDurationSeconds);
        Assert.Null(settings.DeadlinePolicy);
        Assert.Equal(800_000, (await db.Tournaments.SingleAsync()).RegistrationFeeAmount);
    }

    [Theory]
    [InlineData(5, 40)]
    [InlineData(6, 0)]
    public async Task Create_relay_tournament_rejects_invalid_rules(int teamSize, int targetScore)
    {
        await using var db = NewDb();
        var controller = NewController(db, relayEnabled: true);

        var response = await controller.Create(new CreateTournamentRequest
        {
            Title = "Giải tiếp sức sai cấu hình",
            GameType = "DOUBLE",
            GenderCategory = "OPEN",
            IsRelay = true,
            RelayTeamSize = teamSize,
            RelayTargetScore = targetScore
        });

        Assert.IsType<BadRequestObjectResult>(response);
        Assert.Empty(await db.Tournaments.ToListAsync());
        Assert.Empty(await db.RelayTournamentSettings.ToListAsync());
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100)]
    [InlineData(142)]
    public async Task Create_tournament_rejects_limits_outside_database_range(double doubleLimit)
    {
        await using var db = NewDb();
        var controller = NewController(db, relayEnabled: true);

        var response = await controller.Create(new CreateTournamentRequest
        {
            Title = "Giải có giới hạn không hợp lệ",
            GameType = "DOUBLE",
            GenderCategory = "OPEN",
            DoubleLimit = (decimal)doubleLimit
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(response);
        Assert.Contains("0 đến 99.99", badRequest.Value?.ToString());
        Assert.Empty(await db.Tournaments.ToListAsync());
    }

    private static PickleballDbContext NewDb() => new(new DbContextOptionsBuilder<PickleballDbContext>()
        .UseInMemoryDatabase($"admin-relay-create-{Guid.NewGuid():N}")
        .Options);

    private static AdminTournamentsApiController NewController(PickleballDbContext db, bool relayEnabled)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Relay:AdminPreviewEnabled"] = relayEnabled.ToString()
        }).Build();
        return new AdminTournamentsApiController(db, null!, configuration);
    }
}
