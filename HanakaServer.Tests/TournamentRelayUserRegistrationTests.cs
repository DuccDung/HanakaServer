using System.Security.Claims;
using System.Text.Json;
using HanakaServer.Controllers;
using HanakaServer.Data;
using HanakaServer.Dtos.Relay;
using HanakaServer.Models;
using HanakaServer.Options;
using HanakaServer.Services;
using HanakaServer.Services.Payments;
using HanakaServer.Services.Relay;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HanakaServer.Tests;

public sealed class TournamentRelayUserRegistrationTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public async Task Captain_creates_a_ready_complete_team_without_member_approval(int teamSize)
    {
        await using var db = NewDb();
        var tournament = await SeedAsync(db, teamSize, userCount: teamSize);
        var controller = NewController(db, captainUserId: 1);

        var before = Assert.IsType<OkObjectResult>(
            await controller.GetMyTournamentRegistrationState(tournament.TournamentId, default));
        using (var beforeJson = JsonDocument.Parse(JsonSerializer.Serialize(before.Value)))
        {
            Assert.True(beforeJson.RootElement.GetProperty("isRelay").GetBoolean());
            Assert.True(beforeJson.RootElement.GetProperty("canRegister").GetBoolean());
            Assert.Equal(teamSize, beforeJson.RootElement.GetProperty("relay").GetProperty("TeamSize").GetInt32());
        }

        var result = await controller.RegisterRelayTeam(
            tournament.TournamentId,
            Request(teamSize),
            default);

        var ok = Assert.IsType<OkObjectResult>(result);
        using (var responseJson = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value)))
        {
            Assert.True(responseJson.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(teamSize, responseJson.RootElement.GetProperty("teamSize").GetInt32());
        }

        var registration = await db.TournamentRegistrations.SingleAsync();
        var team = await db.RelayTeams.Include(x => x.Members).SingleAsync();
        Assert.True(registration.Success);
        Assert.False(registration.WaitingPair);
        Assert.False(registration.Paid);
        Assert.Equal(0m, registration.Points);
        Assert.Equal(1, registration.Player1UserId);
        Assert.Equal(2, registration.Player2UserId);
        Assert.Equal(1, team.CaptainUserId);
        Assert.Null(team.LineupLockedAtUtc);
        Assert.Equal(1, team.Version);
        Assert.Equal(teamSize, team.Members.Count);
        Assert.Equal(Enumerable.Range(1, teamSize), team.Members.OrderBy(x => x.Position).Select(x => x.Position));
        Assert.Equal(teamSize - 1, await db.UserNotifications.CountAsync(x => x.NotificationType == "RELAY_TEAM_ADDED"));
        Assert.Empty(await db.TournamentPairRequests.ToListAsync());

        var after = Assert.IsType<OkObjectResult>(
            await controller.GetMyTournamentRegistrationState(tournament.TournamentId, default));
        using var afterJson = JsonDocument.Parse(JsonSerializer.Serialize(after.Value));
        Assert.False(afterJson.RootElement.GetProperty("canRegister").GetBoolean());
        var existing = afterJson.RootElement.GetProperty("existingRegistration");
        Assert.Equal("Đội user", existing.GetProperty("TeamName").GetString());
        Assert.True(existing.GetProperty("IsCaptain").GetBoolean());
        Assert.True(existing.GetProperty("LineupLocked").GetBoolean());
        Assert.Equal(teamSize, existing.GetProperty("Members").GetArrayLength());

        var memberController = NewController(db, captainUserId: teamSize);
        var memberState = Assert.IsType<OkObjectResult>(
            await memberController.GetMyTournamentRegistrationState(tournament.TournamentId, default));
        using var memberJson = JsonDocument.Parse(JsonSerializer.Serialize(memberState.Value));
        Assert.False(memberJson.RootElement.GetProperty("canRegister").GetBoolean());
        Assert.False(memberJson.RootElement.GetProperty("existingRegistration").GetProperty("IsCaptain").GetBoolean());
    }

    [Fact]
    public async Task Relay_member_search_supports_name_phone_and_user_id()
    {
        await using var db = NewDb();
        var tournament = await SeedAsync(db, teamSize: 4, userCount: 4);
        var target = await db.Users.SingleAsync(x => x.UserId == 2);
        target.FullName = "Nguyễn Minh Thành";
        target.Phone = "0912345678";
        target.RatingDouble = 4.25m;
        await db.SaveChangesAsync();
        var controller = NewController(db, captainUserId: 1);

        foreach (var query in new[] { "Minh Thành", "345678", "2" })
        {
            var result = Assert.IsType<OkObjectResult>(
                await controller.SearchRelayMember(tournament.TournamentId, query, 10, default));
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
            var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(2, item.GetProperty("UserId").GetInt64());
            Assert.Equal("******5678", item.GetProperty("PhoneMasked").GetString());
            Assert.True(item.GetProperty("CanSelect").GetBoolean());
        }
    }

    [Fact]
    public async Task Public_registration_list_returns_full_relay_roster_with_current_profile_and_rating()
    {
        await using var db = NewDb();
        var tournament = await SeedAsync(db, teamSize: 6, userCount: 6);
        var target = await db.Users.SingleAsync(x => x.UserId == 5);
        target.FullName = "Relay player snapshot";
        target.AvatarUrl = "https://images.test/relay-5-old.jpg";
        target.RatingDouble = 3.25m;
        await db.SaveChangesAsync();
        Assert.IsType<OkObjectResult>(await NewController(db, captainUserId: 1).RegisterRelayTeam(
            tournament.TournamentId,
            Request(6),
            default));

        target.FullName = "Relay player current profile";
        target.AvatarUrl = "https://images.test/relay-5.jpg";
        db.UserRatingHistories.Add(new UserRatingHistory
        {
            RatingHistoryId = 5001,
            UserId = target.UserId,
            RatingDouble = 4.75m,
            RatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Relay:AdminPreviewEnabled"] = "true",
            ["PublicBaseUrl"] = "https://cdn.test"
        }).Build();
        var publicController = new PublicTournamentsController(db, config);
        var result = Assert.IsType<OkObjectResult>(
            await publicController.PublicRegistrations(tournament.TournamentId));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));

        var tournamentJson = json.RootElement.GetProperty("Tournament");
        Assert.True(tournamentJson.GetProperty("IsRelay").GetBoolean());
        Assert.Equal("RELAY_TEAM", tournamentJson.GetProperty("TournamentTypeCode").GetString());
        Assert.Equal(6, tournamentJson.GetProperty("TeamSize").GetInt32());
        Assert.Equal("https://zalo.test/relay", tournamentJson.GetProperty("ZaloLink").GetString());

        var registration = Assert.Single(json.RootElement.GetProperty("SuccessItems").EnumerateArray());
        Assert.True(registration.GetProperty("IsRelay").GetBoolean());
        Assert.Equal("Đội user", registration.GetProperty("TeamName").GetString());
        Assert.Equal(0m, registration.GetProperty("Points").GetDecimal());
        Assert.Equal(6, registration.GetProperty("Members").GetArrayLength());
        var member = registration.GetProperty("Members").EnumerateArray()
            .Single(x => x.GetProperty("UserId").GetInt64() == 5);
        Assert.Equal(5, member.GetProperty("Position").GetInt32());
        Assert.Equal(3, member.GetProperty("PairNumber").GetInt32());
        Assert.Equal("Relay player current profile", member.GetProperty("Name").GetString());
        Assert.Equal("https://images.test/relay-5.jpg", member.GetProperty("Avatar").GetString());
        Assert.Equal(4.75m, member.GetProperty("Level").GetDecimal());
        var memberWithoutHistory = registration.GetProperty("Members").EnumerateArray()
            .Single(x => x.GetProperty("UserId").GetInt64() == 4);
        Assert.Equal(3.4m, memberWithoutHistory.GetProperty("Level").GetDecimal());
    }

    [Fact]
    public async Task Relay_registration_rejects_member_already_used_by_another_team()
    {
        await using var db = NewDb();
        var tournament = await SeedAsync(db, teamSize: 4, userCount: 7);
        var firstController = NewController(db, captainUserId: 1);
        Assert.IsType<OkObjectResult>(await firstController.RegisterRelayTeam(
            tournament.TournamentId,
            Request(4),
            default));

        var secondController = NewController(db, captainUserId: 5);
        var conflict = Assert.IsType<ConflictObjectResult>(await secondController.RegisterRelayTeam(
            tournament.TournamentId,
            new CreateRelayUserRegistrationRequest
            {
                TeamName = "Đội trùng",
                Members =
                [
                    new() { Position = 2, UserId = 2 },
                    new() { Position = 3, UserId = 6 },
                    new() { Position = 4, UserId = 7 }
                ]
            },
            default));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(conflict.Value));
        Assert.Equal("ATHLETE_ALREADY_REGISTERED", json.RootElement.GetProperty("code").GetString());
        Assert.Single(await db.TournamentRegistrations.ToListAsync());
        Assert.Single(await db.RelayTeams.ToListAsync());
    }

    [Fact]
    public async Task Relay_registration_rejects_inactive_or_incomplete_roster_without_writes()
    {
        await using var db = NewDb();
        var tournament = await SeedAsync(db, teamSize: 4, userCount: 4);
        var inactive = await db.Users.SingleAsync(x => x.UserId == 4);
        inactive.IsActive = false;
        await db.SaveChangesAsync();
        var controller = NewController(db, captainUserId: 1);

        var inactiveResult = await controller.RegisterRelayTeam(tournament.TournamentId, Request(4), default);
        Assert.IsType<BadRequestObjectResult>(inactiveResult);

        var incompleteResult = await controller.RegisterRelayTeam(
            tournament.TournamentId,
            new CreateRelayUserRegistrationRequest
            {
                TeamName = "Thiếu người",
                Members = [new() { Position = 2, UserId = 2 }]
            },
            default);
        Assert.IsType<BadRequestObjectResult>(incompleteResult);
        Assert.Empty(await db.TournamentRegistrations.ToListAsync());
        Assert.Empty(await db.RelayTeams.ToListAsync());
    }

    [Fact]
    public async Task Any_authenticated_user_can_pay_for_relay_registration()
    {
        await using var db = NewDb();
        var tournament = await SeedAsync(db, teamSize: 4, userCount: 5);
        Assert.IsType<OkObjectResult>(await NewController(db, captainUserId: 1).RegisterRelayTeam(
            tournament.TournamentId,
            Request(4),
            default));
        var registrationId = await db.TournamentRegistrations.Select(x => x.RegistrationId).SingleAsync();
        var paymentService = NewPaymentService(db);

        var outsiderResult = await paymentService.CreateOrReuseCheckoutAsync(
            userId: 5,
            registrationId,
            default);

        Assert.True(outsiderResult.Success, outsiderResult.Message);
        Assert.NotNull(outsiderResult.Payment);
        Assert.Equal(5, await db.TournamentRegistrationPayments.Select(x => x.UserId).SingleAsync());

        var memberResult = await paymentService.CreateOrReuseCheckoutAsync(
            userId: 4,
            registrationId,
            default);

        Assert.True(memberResult.Success, memberResult.Message);
        Assert.True(memberResult.Payment?.ReusedExistingPayment);
        Assert.Equal(
            outsiderResult.Payment?.TransactionCode,
            memberResult.Payment?.TransactionCode);
    }

    [Fact]
    public async Task Relay_state_and_search_block_accounts_present_only_in_legacy_registration_fields()
    {
        await using var db = NewDb();
        var tournament = await SeedAsync(db, teamSize: 4, userCount: 4);
        db.TournamentRegistrations.Add(new TournamentRegistration
        {
            TournamentId = tournament.TournamentId,
            RegIndex = 1,
            RegCode = $"{tournament.TournamentId}-0001",
            Player1UserId = 1,
            Player1Name = "VĐV 1",
            Player2UserId = 2,
            Player2Name = "VĐV 2",
            Success = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = NewController(db, captainUserId: 1);
        var state = Assert.IsType<OkObjectResult>(
            await controller.GetMyTournamentRegistrationState(tournament.TournamentId, default));
        using (var stateJson = JsonDocument.Parse(JsonSerializer.Serialize(state.Value)))
        {
            Assert.False(stateJson.RootElement.GetProperty("canRegister").GetBoolean());
            Assert.Contains("đã có đăng ký", stateJson.RootElement.GetProperty("reason").GetString());
        }

        var search = Assert.IsType<OkObjectResult>(
            await controller.SearchRelayMember(tournament.TournamentId, "2", 10, default));
        using var searchJson = JsonDocument.Parse(JsonSerializer.Serialize(search.Value));
        var item = Assert.Single(searchJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.True(item.GetProperty("IsRegistered").GetBoolean());
        Assert.False(item.GetProperty("CanSelect").GetBoolean());
    }

    [RelaySqlFact]
    public async Task Relay_user_registration_creates_a_ready_team_against_real_sql_constraints()
    {
        await using var sandbox = await RelaySqlSandbox.CreateFullSchemaAsync();
        await using var db = sandbox.CreateDb();
        var tournament = new Tournament
        {
            Title = "Giải user SQL",
            Status = "OPEN",
            GameType = "DOUBLE",
            GenderCategory = "OPEN",
            ExpectedTeams = 8,
            RegisterDeadline = DateTime.Now.AddDays(2),
            RegistrationFeeAmount = 400_000m,
            RegistrationFeeCurrency = "VND",
            CreatedAt = DateTime.UtcNow
        };
        db.Tournaments.Add(tournament);
        var users = Enumerable.Range(1, 4).Select(position => new User
        {
            FullName = $"SQL VĐV {position}",
            Phone = $"091000{position:0000}",
            RatingDouble = 3m + position / 10m,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        }).ToList();
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        db.RelayTournamentSettings.Add(new RelayTournamentSettings
        {
            TournamentId = tournament.TournamentId,
            TeamSize = 4,
            TargetScore = 40,
            LegDurationSeconds = 600,
            Version = 1
        });
        await db.SaveChangesAsync();

        var result = await NewController(db, users[0].UserId).RegisterRelayTeam(
            tournament.TournamentId,
            new CreateRelayUserRegistrationRequest
            {
                TeamName = "Đội SQL user",
                Members = users.Skip(1).Select((user, index) => new RelayUserRegistrationMemberRequest
                {
                    Position = index + 2,
                    UserId = user.UserId
                }).ToList()
            },
            default);

        Assert.IsType<OkObjectResult>(result);
        var team = await db.RelayTeams.Include(x => x.Members).SingleAsync();
        Assert.Equal(4, team.Members.Count);
        Assert.Null(team.LineupLockedAtUtc);
        Assert.Equal(1, team.Version);
        Assert.True(await db.TournamentRegistrations.Select(x => x.Success).SingleAsync());

        var publicConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Relay:AdminPreviewEnabled"] = "true"
        }).Build();
        var publicResult = Assert.IsType<OkObjectResult>(
            await new PublicTournamentsController(db, publicConfig).PublicRegistrations(tournament.TournamentId));
        using var publicJson = JsonDocument.Parse(JsonSerializer.Serialize(publicResult.Value));
        var publicRegistration = Assert.Single(publicJson.RootElement.GetProperty("SuccessItems").EnumerateArray());
        Assert.True(publicRegistration.GetProperty("IsRelay").GetBoolean());
        Assert.Equal(4, publicRegistration.GetProperty("Members").GetArrayLength());
    }

    private static CreateRelayUserRegistrationRequest Request(int teamSize) => new()
    {
        TeamName = "Đội user",
        Members = Enumerable.Range(2, teamSize - 1)
            .Select(position => new RelayUserRegistrationMemberRequest
            {
                Position = position,
                UserId = position
            })
            .ToList()
    };

    private static async Task<Tournament> SeedAsync(PickleballDbContext db, int teamSize, int userCount)
    {
        var tournament = new Tournament
        {
            Title = "Giải tiếp sức user",
            Status = "OPEN",
            GameType = "DOUBLE",
            GenderCategory = "OPEN",
            ExpectedTeams = 16,
            RegisterDeadline = DateTime.Now.AddDays(3),
            RegistrationFeeAmount = 500_000m,
            RegistrationFeeCurrency = "VND",
            ZaloLink = "https://zalo.test/relay",
            CreatedAt = DateTime.UtcNow
        };
        db.Tournaments.Add(tournament);
        await db.SaveChangesAsync();
        db.RelayTournamentSettings.Add(new RelayTournamentSettings
        {
            TournamentId = tournament.TournamentId,
            TeamSize = teamSize,
            TargetScore = 40,
            LegDurationSeconds = 600,
            Version = 1
        });
        db.Users.AddRange(Enumerable.Range(1, userCount).Select(id => new User
        {
            UserId = id,
            FullName = $"VĐV {id}",
            Phone = $"090000{id:0000}",
            RatingDouble = 3m + id / 10m,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        }));
        await db.SaveChangesAsync();
        return tournament;
    }

    private static PickleballDbContext NewDb() => new(new DbContextOptionsBuilder<PickleballDbContext>()
        .UseInMemoryDatabase($"relay-user-registration-{Guid.NewGuid():N}")
        .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private static TournamentRegistrationUserController NewController(PickleballDbContext db, long captainUserId)
    {
        var relayOptions = Microsoft.Extensions.Options.Options.Create(
            new RelayOptions { AdminPreviewEnabled = true });
        return new TournamentRegistrationUserController(
            db,
            new ConfigurationBuilder().Build(),
            null!,
            new RealtimeHub(),
            new RelayLegacyWriteGuard(db, relayOptions))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("uid", captainUserId.ToString())],
                        "test"))
                }
            }
        };
    }

    private static TournamentRegistrationPaymentService NewPaymentService(PickleballDbContext db)
    {
        var sepayOptions = Microsoft.Extensions.Options.Options.Create(new SepayOptions
        {
            ApiToken = string.Empty,
            ReceiverBankShortName = "MBBank",
            ReceiverBankName = "MBBank",
            ReceiverAccountNumber = "0123456789",
            ReceiverAccountName = "HANAKA TEST"
        });
        var relayOptions = Microsoft.Extensions.Options.Options.Create(
            new RelayOptions { AdminPreviewEnabled = true });
        var settingsProvider = new SepaySettingsProvider(
            db,
            sepayOptions,
            NullLogger<SepaySettingsProvider>.Instance);

        return new TournamentRegistrationPaymentService(
            db,
            new SepayGatewayClient(new HttpClient(), NullLogger<SepayGatewayClient>.Instance),
            settingsProvider,
            new PublicRealtimeHub(NullLogger<PublicRealtimeHub>.Instance),
            NullLogger<TournamentRegistrationPaymentService>.Instance,
            relayOptions);
    }
}
