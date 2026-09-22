using System.Text.Json;
using HanakaServer.Controllers;
using HanakaServer.Data;
using HanakaServer.Dtos;
using HanakaServer.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HanakaServer.Tests;

public sealed class RelayAppRegistrationSummaryTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(4, false)]
    [InlineData(4, true)]
    [InlineData(6, false)]
    [InlineData(6, true)]
    [InlineData(8, false)]
    [InlineData(8, true)]
    public async Task Default_view_represents_each_team_as_one_verified_player(int size, bool paid)
    {
        await using var db = NewDb();
        await Seed(db, size, paid);
        var result = await Read(new PublicTournamentsController(db, Config()));
        var item = Assert.Single(result.SuccessItems);
        var metadata = JsonSerializer.SerializeToElement(result.Tournament, WebJson);

        // The released app reads tournamentTypeCode before gameType when choosing its single-player layout.
        Assert.Equal("SINGLE", metadata.GetProperty("tournamentTypeCode").GetString());
        Assert.Equal("DOUBLE", metadata.GetProperty("gameType").GetString());
        Assert.True(metadata.GetProperty("isRelay").GetBoolean());
        Assert.Equal("Đồng đội tiếp sức", metadata.GetProperty("tournamentTypeLabel").GetString());
        Assert.Equal("Hanaka A", item.Player1.Name);
        Assert.Equal(size * 3m, item.Player1.Level);
        Assert.Equal(item.Player1.Level, item.Points);
        Assert.True(item.Player1.Verified);
        Assert.False(item.Player1.IsGuest);
        Assert.Null(item.Player1.UserId);
        Assert.Null(item.Player1.Avatar);
        Assert.Null(item.Player2);
        Assert.True(item.IsReady);
        Assert.Equal(size, item.Members.Length);
        Assert.Single(item.ReserveMembers);
        Assert.Equal(99m, item.ReserveMembers[0].Level);
        Assert.Equal(501, item.RegistrationId);
        Assert.Equal("37-0001", item.RegCode);
        Assert.Equal(paid, item.Paid);
        Assert.Equal(paid ? 500_000m : null, item.PaymentAmount);
        Assert.Equal((await db.TournamentRegistrations.SingleAsync()).PaidAt, item.PaidAt);
        Assert.True(item.Success);
        Assert.False(item.WaitingPair);
        Assert.Equal(1, result.Counts.Success);
        Assert.Equal(paid ? 1 : 0, result.Counts.Paid);
        Assert.Equal(15, result.Counts.CapacityLeft);

        // Compatibility fields must never turn the captain into a verified account or rewrite stored registrations.
        db.ChangeTracker.Clear();
        Assert.All(await db.Users.ToListAsync(), user => Assert.False(user.Verified));
        var stored = await db.TournamentRegistrations.SingleAsync();
        Assert.Equal("Snapshot 1", stored.Player1Name);
        Assert.Equal(1, stored.Player1UserId);
        Assert.Equal(2, stored.Player2UserId);
        Assert.Equal(0m, stored.Points);
        Assert.Equal("DOUBLE", (await db.Tournaments.SingleAsync()).GameType);
    }

    [Fact]
    public async Task Rating_uses_latest_history_then_account_and_zero_without_counting_reserves()
    {
        await using var db = NewDb();
        await Seed(db, 6);
        var ratedAt = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc);
        db.UserRatingHistories.AddRange(
            new UserRatingHistory { RatingHistoryId = 1, UserId = 1, RatingDouble = 8m, RatedAt = ratedAt.AddDays(-1) },
            new UserRatingHistory { RatingHistoryId = 2, UserId = 1, RatingDouble = 5m, RatedAt = ratedAt },
            new UserRatingHistory { RatingHistoryId = 3, UserId = 1, RatingDouble = 4.25m, RatedAt = ratedAt });
        (await db.Users.SingleAsync(x => x.UserId == 2)).RatingDouble = null;
        (await db.RelayTeamMembers.SingleAsync(x => x.Position == 3)).UserId = null;
        await db.SaveChangesAsync();

        var item = Assert.Single((await Read(new PublicTournamentsController(db, Config()))).SuccessItems);
        Assert.Equal(13.25m, item.Player1.Level); // 4.25 + 0 + 0 + 3 + 3 + 3; reserve is 99.
        Assert.False(item.Members[0].Verified);
        Assert.Equal(4.25m, item.Members[0].Level);
    }

    [Fact]
    public async Task Missing_team_name_and_empty_roster_have_safe_fallbacks()
    {
        await using var db = NewDb();
        await Seed(db, 4);
        (await db.RelayTeams.SingleAsync()).TeamName = "  ";
        db.RelayTeamMembers.RemoveRange(db.RelayTeamMembers);
        await db.SaveChangesAsync();
        var item = Assert.Single((await Read(new PublicTournamentsController(db, Config()))).SuccessItems);
        Assert.Equal("Đội 37-0001", item.Player1.Name);
        Assert.Equal(0m, item.Player1.Level);
        Assert.Null(item.Player1.Avatar);
        Assert.True(item.Player1.Verified);
        Assert.False(item.IsReady);
        Assert.Empty(item.Members);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cache_keeps_full_and_app_views_separate_in_both_request_orders(bool fullFirst)
    {
        await using var db = NewDb();
        await Seed(db, 6);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = new PublicTournamentsController(db, Config(), cache: cache);
        var first = await Read(controller, fullFirst ? "full" : null);
        var before = JsonSerializer.Serialize(first, WebJson);
        var second = await Read(controller, fullFirst ? null : "full");
        Assert.Equal(before, JsonSerializer.Serialize(first, WebJson));
        var full = fullFirst ? first : second;
        var summary = fullFirst ? second : first;
        Assert.NotSame(full, summary);
        Assert.NotSame(full.SuccessItems[0], summary.SuccessItems[0]);
        Assert.Equal("RELAY_TEAM", JsonSerializer.SerializeToElement(full.Tournament, WebJson)
            .GetProperty("tournamentTypeCode").GetString());
        Assert.Equal("Current 1", full.SuccessItems[0].Player1.Name);
        Assert.Equal(1, full.SuccessItems[0].Player1.UserId);
        Assert.NotNull(full.SuccessItems[0].Player2);
        Assert.Equal(0m, full.SuccessItems[0].Points);
        Assert.Equal("Hanaka A", summary.SuccessItems[0].Player1.Name);
        Assert.Same(full, await Read(controller, " FULL "));
        Assert.Same(summary, await Read(controller));
        Assert.Equal(JsonSerializer.Serialize(full.Counts), JsonSerializer.Serialize(summary.Counts));
        Assert.Equal(JsonSerializer.Serialize(full.SuccessItems[0].Members), JsonSerializer.Serialize(summary.SuccessItems[0].Members));
    }

    [Fact]
    public async Task Rollback_switch_uses_full_view_even_after_app_summary_was_cached()
    {
        await using var db = NewDb();
        await Seed(db, 4);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var config = Config();
        var controller = new PublicTournamentsController(db, config, cache: cache);
        var summary = await Read(controller);
        config["PublicRegistrations:LegacyRelaySummaryEnabled"] = "false";
        var restored = await Read(controller);
        Assert.Equal("RELAY_TEAM", JsonSerializer.SerializeToElement(restored.Tournament, WebJson)
            .GetProperty("tournamentTypeCode").GetString());
        Assert.NotNull(restored.SuccessItems[0].Player2);
        Assert.Equal("Hanaka A", summary.SuccessItems[0].Player1.Name);
        config["PublicRegistrations:LegacyRelaySummaryEnabled"] = "true";
        Assert.Same(summary, await Read(controller));
    }

    [Theory]
    [InlineData("SINGLE", "SINGLE_OPEN")]
    [InlineData("DOUBLE", "DOUBLE_OPEN")]
    [InlineData("MIXED", "DOUBLE_MIXED")]
    public async Task Standard_tournaments_return_the_same_contract_in_both_views(string gameType, string code)
    {
        await using var db = NewDb();
        await Seed(db, 4);
        db.RelayTeamReserveMembers.RemoveRange(db.RelayTeamReserveMembers);
        db.RelayTeamMembers.RemoveRange(db.RelayTeamMembers);
        db.RelayTeams.RemoveRange(db.RelayTeams);
        db.RelayTournamentSettings.RemoveRange(db.RelayTournamentSettings);
        (await db.Tournaments.SingleAsync()).GameType = gameType;
        await db.SaveChangesAsync();
        var controller = new PublicTournamentsController(db, Config());
        var standard = await Read(controller);
        Assert.Equal(JsonSerializer.Serialize(await Read(controller, "full"), WebJson), JsonSerializer.Serialize(standard, WebJson));
        Assert.Equal(code, JsonSerializer.SerializeToElement(standard.Tournament, WebJson)
            .GetProperty("tournamentTypeCode").GetString());
        Assert.False(standard.SuccessItems[0].IsRelay);
        Assert.False(standard.SuccessItems[0].Player1.Verified);
    }

    [Fact]
    public async Task Disabled_relay_feature_does_not_activate_the_compatibility_projection()
    {
        await using var db = NewDb();
        await Seed(db, 4);
        var config = Config();
        config["Relay:AdminPreviewEnabled"] = "false";
        var controller = new PublicTournamentsController(db, config);
        var result = await Read(controller);
        Assert.False(result.SuccessItems[0].IsRelay);
        Assert.NotNull(result.SuccessItems[0].Player2);
        Assert.Equal(JsonSerializer.Serialize(await Read(controller, "full"), WebJson), JsonSerializer.Serialize(result, WebJson));
    }

    [Fact]
    public async Task Tabs_and_cache_do_not_drop_or_reclassify_waiting_registrations()
    {
        await using var db = NewDb();
        await Seed(db, 4);
        db.TournamentRegistrations.Add(new TournamentRegistration
        {
            RegistrationId = 502, TournamentId = 37, RegIndex = 2, RegCode = "37-0002",
            Player1Name = "Pending", WaitingPair = true, Success = false
        });
        await db.SaveChangesAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = new PublicTournamentsController(db, Config(), cache: cache);
        foreach (var view in new string?[] { null, "full" })
        {
            var all = await Read(controller, view);
            var success = await Read(controller, view, "SUCCESS");
            var waiting = await Read(controller, view, "WAITING");
            Assert.Single(all.SuccessItems);
            Assert.Single(all.WaitingItems);
            Assert.Empty(success.WaitingItems);
            Assert.Empty(waiting.SuccessItems);
            Assert.Equal(502, Assert.Single(waiting.WaitingItems).RegistrationId);
            Assert.True(waiting.WaitingItems[0].WaitingPair);
            Assert.False(waiting.WaitingItems[0].Success);
            Assert.Equal(1, waiting.Counts.Success);
            Assert.Equal(1, waiting.Counts.Waiting);
        }
    }

    [Fact]
    public async Task Http_endpoint_binds_view_and_returns_the_camel_case_contract_without_authentication()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["Relay:AdminPreviewEnabled"] = "true";
        var database = "relay-app-http-" + Guid.NewGuid();
        builder.Services.AddDbContext<PickleballDbContext>(options => options.UseInMemoryDatabase(database));
        builder.Services.AddMemoryCache();
        builder.Services.AddControllers().AddApplicationPart(typeof(PublicTournamentsController).Assembly);
        await using var app = builder.Build();
        app.MapControllers();
        await using (var scope = app.Services.CreateAsyncScope())
            await Seed(scope.ServiceProvider.GetRequiredService<PickleballDbContext>(), 6);
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            foreach (var view in new[] { "", "&view=full", "" })
            {
                using var response = await client.GetAsync("/api/public/tournaments/37/registrations?tab=ALL" + view);
                response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var body = json.RootElement;
                Assert.Equal(view == "" ? "SINGLE" : "RELAY_TEAM", body.GetProperty("tournament").GetProperty("tournamentTypeCode").GetString());
                var item = body.GetProperty("successItems")[0];
                Assert.Equal(501, item.GetProperty("registrationId").GetInt64());
                Assert.Equal(6, item.GetProperty("members").GetArrayLength());
                if (view == "")
                {
                    Assert.Equal("Hanaka A", item.GetProperty("player1").GetProperty("name").GetString());
                    Assert.Equal(18m, item.GetProperty("player1").GetProperty("level").GetDecimal());
                    Assert.True(item.GetProperty("player1").GetProperty("verified").GetBoolean());
                    Assert.Equal(JsonValueKind.Null, item.GetProperty("player2").ValueKind);
                }
            }
        }
        finally { await app.StopAsync(); }
    }

    private static async Task<PublicTournamentRegistrationsResponseDto> Read(PublicTournamentsController controller,
        string? view = null, string tab = "ALL") => Assert.IsType<PublicTournamentRegistrationsResponseDto>(
            Assert.IsType<OkObjectResult>(await controller.PublicRegistrations(37, tab, view: view)).Value);

    private static IConfigurationRoot Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Relay:AdminPreviewEnabled"] = "true", ["PublicRegistrations:CacheSeconds"] = "30"
    }).Build();

    private static PickleballDbContext NewDb() => new(new DbContextOptionsBuilder<PickleballDbContext>()
        .UseInMemoryDatabase("relay-app-summary-" + Guid.NewGuid()).Options);

    private static async Task Seed(PickleballDbContext db, int size, bool paid = false)
    {
        db.Tournaments.Add(new Tournament
        {
            TournamentId = 37, Title = "Relay summary", Status = "OPEN", GameType = "DOUBLE", GenderCategory = "OPEN",
            ExpectedTeams = 16, RegistrationFeeAmount = 500_000m, RegistrationFeeCurrency = "VND"
        });
        db.RelayTournamentSettings.Add(new RelayTournamentSettings { TournamentId = 37, TeamSize = size });
        db.Users.AddRange(Enumerable.Range(1, size + 1).Select(id => new User
        {
            UserId = id, FullName = $"Current {id}", IsActive = true,
            AvatarUrl = $"https://images.test/{id}.jpg", RatingDouble = id > size ? 99m : 3m, RatingSingle = 1m
        }));
        db.TournamentRegistrations.Add(new TournamentRegistration
        {
            RegistrationId = 501, TournamentId = 37, RegIndex = 1, RegCode = "37-0001",
            RegTime = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc),
            Player1UserId = 1, Player1Name = "Snapshot 1", Player2UserId = 2, Player2Name = "Snapshot 2",
            Success = true, Paid = paid, PaymentAmount = paid ? 500_000m : null,
            PaidAt = paid ? new DateTime(2026, 9, 12, 1, 0, 0, DateTimeKind.Utc) : null
        });
        db.RelayTeams.Add(new RelayTeam
        {
            RegistrationId = 501, TournamentId = 37, TeamName = "Hanaka A", CaptainUserId = 1,
            Members = Enumerable.Range(1, size).Select(position => new RelayTeamMember
            {
                RegistrationId = 501, Position = position, UserId = position, DisplayName = $"Snapshot {position}"
            }).ToList(),
            ReserveMembers = [new RelayTeamReserveMember
            {
                RegistrationId = 501, Position = 1, UserId = size + 1, DisplayName = "Reserve"
            }]
        });
        await db.SaveChangesAsync();
    }
}
