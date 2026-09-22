using HanakaServer.Controllers;
using HanakaServer.Data;
using HanakaServer.Dtos;
using HanakaServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;
using Xunit;

namespace HanakaServer.Tests;

public sealed class AdminRelayRegistrationTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public async Task Create_relay_registration_persists_exact_configured_roster(int teamSize)
    {
        await using var db = NewDb();
        var tournament = await SeedRelayTournamentAsync(db, teamSize);
        var controller = NewController(db);

        var result = await controller.Create(tournament.TournamentId, new CreateRegistrationForm
        {
            RelayTeamName = $"Đội {teamSize}",
            RelayMembers = Enumerable.Range(1, teamSize)
                .Select(position => new RelayRegistrationMemberForm
                {
                    Position = position,
                    DisplayName = $"VĐV {position}"
                }).ToList()
        });

        Assert.IsType<OkObjectResult>(result);
        var registration = await db.TournamentRegistrations.SingleAsync();
        var team = await db.RelayTeams.Include(x => x.Members).SingleAsync();
        Assert.Equal(registration.RegistrationId, team.RegistrationId);
        Assert.Equal(teamSize, team.Members.Count);
        Assert.Equal(Enumerable.Range(1, teamSize), team.Members.OrderBy(x => x.Position).Select(x => x.Position));
        Assert.Equal("VĐV 1", registration.Player1Name);
        Assert.Equal("VĐV 2", registration.Player2Name);
        Assert.True(registration.Success);
        Assert.False(registration.WaitingPair);
        Assert.Equal(0m, registration.Points);
    }

    [Theory]
    [InlineData(4, 3)]
    [InlineData(6, 7)]
    [InlineData(8, 0)]
    public async Task Create_relay_registration_rejects_missing_or_excess_members(int teamSize, int submittedCount)
    {
        await using var db = NewDb();
        var tournament = await SeedRelayTournamentAsync(db, teamSize);
        var controller = NewController(db);

        var result = await controller.Create(tournament.TournamentId, new CreateRegistrationForm
        {
            RelayTeamName = "Đội sai số lượng",
            RelayMembers = Enumerable.Range(1, submittedCount)
                .Select(position => new RelayRegistrationMemberForm
                {
                    Position = position,
                    DisplayName = $"VĐV {position}"
                }).ToList()
        });

        var badRequest = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
        Assert.Empty(await db.TournamentRegistrations.ToListAsync());
        Assert.Empty(await db.RelayTeams.ToListAsync());
    }

    [Fact]
    public async Task Create_relay_registration_rejects_an_account_already_used_by_another_team()
    {
        await using var db = NewDb();
        var tournament = await SeedRelayTournamentAsync(db, 4);
        db.Users.Add(new User
        {
            UserId = 99,
            FullName = "VĐV tài khoản",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var controller = NewController(db);

        var first = await controller.Create(tournament.TournamentId, RequestWithAccount("Đội A", 99));
        Assert.IsType<OkObjectResult>(first);

        var second = await controller.Create(tournament.TournamentId, RequestWithAccount("Đội B", 99));
        var conflict = Assert.IsAssignableFrom<ObjectResult>(second);
        Assert.Equal(400, conflict.StatusCode);
        Assert.Single(await db.TournamentRegistrations.ToListAsync());
    }

    [Fact]
    public async Task Update_relay_registration_replaces_complete_unlocked_roster_and_checks_version()
    {
        await using var db = NewDb();
        var tournament = await SeedRelayTournamentAsync(db, 4);
        var controller = NewController(db);
        var created = Assert.IsType<OkObjectResult>(await controller.Create(
            tournament.TournamentId,
            GuestRequest("Đội cũ", 4)));
        Assert.NotNull(created.Value);
        var registrationId = await db.TournamentRegistrations.Select(x => x.RegistrationId).SingleAsync();

        var update = await controller.UpdatePlayers(registrationId, new UpdateRegistrationPlayersForm
        {
            RelayTeamName = "Đội mới",
            RelayExpectedVersion = 1,
            RelayMembers = Enumerable.Range(1, 4).Select(position => new RelayRegistrationMemberForm
            {
                Position = position,
                DisplayName = $"Thành viên mới {position}"
            }).ToList()
        });

        Assert.IsType<OkObjectResult>(update);
        var team = await db.RelayTeams.Include(x => x.Members).SingleAsync();
        Assert.Equal("Đội mới", team.TeamName);
        Assert.Equal(2, team.Version);
        Assert.Equal("Thành viên mới 4", team.Members.Single(x => x.Position == 4).DisplayName);

        var staleUpdate = await controller.UpdatePlayers(registrationId, new UpdateRegistrationPlayersForm
        {
            RelayTeamName = "Ghi đè",
            RelayExpectedVersion = 1,
            RelayMembers = GuestRequest("", 4).RelayMembers
        });
        var staleConflict = Assert.IsAssignableFrom<ObjectResult>(staleUpdate);
        Assert.Equal(409, staleConflict.StatusCode);
    }

    [Fact]
    public async Task List_relay_registration_treats_a_complete_roster_as_ready()
    {
        await using var db = NewDb();
        var tournament = await SeedRelayTournamentAsync(db, 6);
        var controller = NewController(db);
        Assert.IsType<OkObjectResult>(await controller.Create(tournament.TournamentId, GuestRequest("Đội Sáu", 6)));

        var ready = Assert.IsType<OkObjectResult>(await controller.List(tournament.TournamentId, "SUCCESS"));
        using var readyJson = JsonDocument.Parse(JsonSerializer.Serialize(ready.Value));
        Assert.True(readyJson.RootElement.GetProperty("tournament").GetProperty("IsRelay").GetBoolean());
        Assert.Equal(6, readyJson.RootElement.GetProperty("tournament").GetProperty("RelayTeamSize").GetInt32());
        var readyItem = Assert.Single(readyJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Đội Sáu", readyItem.GetProperty("RelayTeamName").GetString());
        Assert.Equal(6, readyItem.GetProperty("RelayMemberCount").GetInt32());
        Assert.Equal(3, readyItem.GetProperty("RelayPairCount").GetInt32());
        Assert.True(readyItem.GetProperty("RelayIsReady").GetBoolean());
    }

    [Fact]
    public async Task List_relay_registration_returns_member_snapshot_and_latest_double_rating()
    {
        await using var db = NewDb();
        var tournament = await SeedRelayTournamentAsync(db, 4);
        db.Users.Add(new User
        {
            UserId = 501,
            FullName = "Nguyễn Văn A",
            AvatarUrl = "/uploads/avatars/501.jpg",
            RatingDouble = 3.25m,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        db.UserRatingHistories.Add(new UserRatingHistory
        {
            RatingHistoryId = 9001,
            UserId = 501,
            RatingDouble = 4.75m,
            RatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = NewController(db);
        Assert.IsType<OkObjectResult>(await controller.Create(
            tournament.TournamentId,
            RequestWithAccount("Đội có tài khoản", 501)));

        var list = Assert.IsType<OkObjectResult>(await controller.List(tournament.TournamentId));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(list.Value));
        var member = json.RootElement.GetProperty("items")[0]
            .GetProperty("RelayMembers")
            .EnumerateArray()
            .Single(x => x.GetProperty("Position").GetInt32() == 1);

        Assert.Equal(501, member.GetProperty("UserId").GetInt64());
        Assert.Equal("Nguyễn Văn A", member.GetProperty("DisplayName").GetString());
        Assert.Equal("/uploads/avatars/501.jpg", member.GetProperty("AvatarUrl").GetString());
        Assert.Equal(4.75m, member.GetProperty("RatingDouble").GetDecimal());
    }

    [Fact]
    public async Task Legacy_locked_relay_registration_can_still_be_edited_and_deleted()
    {
        await using var db = NewDb();
        var tournament = await SeedRelayTournamentAsync(db, 4);
        var controller = NewController(db);
        Assert.IsType<OkObjectResult>(await controller.Create(tournament.TournamentId, GuestRequest("Đội khóa", 4)));
        var team = await db.RelayTeams.SingleAsync();
        team.LineupLockedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var edit = await controller.UpdatePlayers(team.RegistrationId, new UpdateRegistrationPlayersForm
        {
            RelayTeamName = "Đội đã sửa",
            RelayExpectedVersion = team.Version,
            RelayMembers = Enumerable.Range(1, 4).Select(position => new RelayRegistrationMemberForm
            {
                Position = position,
                DisplayName = $"VĐV mới {position}"
            }).ToList()
        });
        Assert.IsType<OkObjectResult>(edit);
        var delete = await controller.Delete(team.RegistrationId, default);
        Assert.IsType<OkObjectResult>(delete);
        Assert.Empty(await db.TournamentRegistrations.ToListAsync());
        Assert.Empty(await db.RelayTeams.ToListAsync());
    }

    [RelaySqlFact]
    public async Task Create_relay_registration_is_atomic_against_real_sql_constraints()
    {
        await using var sandbox = await RelaySqlSandbox.CreateFullSchemaAsync();
        await using var db = sandbox.CreateDb();
        var tournament = new Tournament
        {
            Title = "Giải SQL 8 người",
            Status = "DRAFT",
            GameType = "DOUBLE",
            GenderCategory = "OPEN",
            ExpectedTeams = 8,
            RegistrationFeeCurrency = "VND",
            CreatedAt = DateTime.UtcNow
        };
        db.Tournaments.Add(tournament);
        await db.SaveChangesAsync();
        db.RelayTournamentSettings.Add(new RelayTournamentSettings
        {
            TournamentId = tournament.TournamentId,
            TeamSize = 8,
            TargetScore = 50,
            LegDurationSeconds = 600,
            Version = 1
        });
        await db.SaveChangesAsync();

        var result = await NewController(db).Create(tournament.TournamentId, GuestRequest("Đội SQL", 8));

        Assert.IsType<OkObjectResult>(result);
        Assert.Single(await db.TournamentRegistrations.ToListAsync());
        Assert.Single(await db.RelayTeams.ToListAsync());
        Assert.Equal(8, await db.RelayTeamMembers.CountAsync());
    }

    [Fact]
    public async Task Admin_conflict_lists_all_players_teams_and_roles_without_mirrored_duplicates()
    {
        await using var db = NewDb();
        var tournament = await SeedRelayTournamentAsync(db, 4);
        db.Users.AddRange(Enumerable.Range(99, 3).Select(id => new User
        {
            UserId = id, FullName = $"Người chơi {id}", IsActive = true, CreatedAt = DateTime.UtcNow
        }));
        await db.SaveChangesAsync();
        var controller = NewController(db);
        var teamA = RequestWithAccount("Team A", 99);
        teamA.RelayCaptainUserId = 99;
        teamA.RelayReserveMembers = [new() { Position = 3, UserId = 100 }];
        Assert.IsType<OkObjectResult>(await controller.Create(tournament.TournamentId, teamA));
        var teamB = RequestWithAccount("Team B", 101);
        Assert.IsType<OkObjectResult>(await controller.Create(tournament.TournamentId, teamB));
        var names = await db.RelayTeams.ToDictionaryAsync(x => x.TeamName, x => x.RegistrationId);
        var codes = await db.TournamentRegistrations.ToDictionaryAsync(x => x.RegistrationId, x => x.RegCode);

        var incoming = RequestWithAccount("Team mới", 99);
        incoming.RelayReserveMembers = [new() { Position = 1, UserId = 100 }, new() { Position = 2, UserId = 101 }];
        var error = Assert.IsType<BadRequestObjectResult>(await controller.Create(tournament.TournamentId, incoming));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(error.Value));
        Assert.Equal("ATHLETE_ALREADY_REGISTERED", json.RootElement.GetProperty("code").GetString());
        var message = json.RootElement.GetProperty("message").GetString()!;
        var lines = message.Split('\n').Skip(1).ToArray();
        Assert.Equal(3, lines.Length); // User 99 is captain, main member and mirrored Player1, but appears once.
        Assert.Contains($"Người chơi 99 (User ID #99) — đội “Team A” (mã ĐK {codes[names["Team A"]]}); đội hình chính, vị trí 1.", lines[0]);
        Assert.Contains($"Người chơi 100 (User ID #100) — đội “Team A” (mã ĐK {codes[names["Team A"]]}); dự bị, vị trí 3.", lines[1]);
        Assert.Contains($"Người chơi 101 (User ID #101) — đội “Team B” (mã ĐK {codes[names["Team B"]]}); đội hình chính, vị trí 1.", lines[2]);
        Assert.Equal(2, await db.TournamentRegistrations.CountAsync());
        Assert.Equal(8, await db.RelayTeamMembers.CountAsync());
        Assert.Single(await db.RelayTeamReserveMembers.ToListAsync());

        var edit = new UpdateRegistrationPlayersForm
        {
            RelayTeamName = "Team B", RelayExpectedVersion = 1, RelayMembers = teamB.RelayMembers,
            RelayReserveMembersIncluded = true, RelayReserveMembers = [new() { Position = 1, UserId = 99 }]
        };
        var editError = Assert.IsType<BadRequestObjectResult>(await controller.UpdatePlayers(names["Team B"], edit));
        using var editJson = JsonDocument.Parse(JsonSerializer.Serialize(editError.Value));
        var editMessage = editJson.RootElement.GetProperty("message").GetString()!;
        Assert.Contains("User ID #99", editMessage);
        Assert.DoesNotContain("User ID #101", editMessage); // Exclude the team currently being edited.
        Assert.Equal(1, (await db.RelayTeams.SingleAsync(x => x.TeamName == "Team B")).Version);
        edit.RelayReserveMembers = [];
        Assert.IsType<OkObjectResult>(await controller.UpdatePlayers(names["Team B"], edit));

        var otherTournament = await SeedRelayTournamentAsync(db, 4);
        Assert.IsType<OkObjectResult>(await controller.Create(otherTournament.TournamentId, RequestWithAccount("Team giải khác", 99)));
    }

    [Theory]
    [InlineData("captain", "đội trưởng")]
    [InlineData("legacy1", "VĐV 1 trong đăng ký cũ")]
    [InlineData("legacy2", "VĐV 2 trong đăng ký cũ")]
    public async Task Admin_conflict_identifies_captain_only_and_legacy_registration_assignments(string source, string role)
    {
        await using var db = NewDb();
        var tournament = await SeedRelayTournamentAsync(db, 4);
        db.Users.Add(new User { UserId = 99, FullName = "Tên hiện tại", IsActive = true });
        db.TournamentRegistrations.Add(new TournamentRegistration
        {
            TournamentId = tournament.TournamentId, RegIndex = 1, RegCode = "OLD-001", Player1Name = "Tên snapshot",
            Player1UserId = source == "legacy1" ? 99 : null,
            Player2UserId = source == "legacy2" ? 99 : null, Player2Name = "Tên snapshot 2", Success = true
        });
        await db.SaveChangesAsync();
        if (source == "captain")
        {
            db.RelayTeams.Add(new RelayTeam
            {
                RegistrationId = await db.TournamentRegistrations.Select(x => x.RegistrationId).SingleAsync(),
                TournamentId = tournament.TournamentId, TeamName = "Team đội trưởng", CaptainUserId = 99
            });
            await db.SaveChangesAsync();
        }
        var result = Assert.IsType<BadRequestObjectResult>(await NewController(db)
            .Create(tournament.TournamentId, RequestWithAccount("Team mới", 99)));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        var message = json.RootElement.GetProperty("message").GetString()!;
        Assert.Contains("Tên hiện tại (User ID #99)", message);
        Assert.Contains("OLD-001", message);
        Assert.Contains(role, message);
        Assert.Single(await db.TournamentRegistrations.ToListAsync());
    }

    [RelaySqlFact]
    public async Task Admin_conflict_details_query_real_sql_without_creating_registration()
    {
        await using var sandbox = await RelaySqlSandbox.CreateFullSchemaAsync();
        await using var db = sandbox.CreateDb();
        var tournament = await SeedRelayTournamentAsync(db, 4);
        var users = new[]
        {
            new User { FullName = "VĐV chính SQL", Phone = "0900090001", IsActive = true, CreatedAt = DateTime.UtcNow },
            new User { FullName = "VĐV dự bị SQL", Phone = "0900090002", IsActive = true, CreatedAt = DateTime.UtcNow }
        };
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        var controller = NewController(db);
        var existing = RequestWithAccount("Đội SQL đã đăng ký", users[0].UserId);
        existing.RelayCaptainUserId = users[0].UserId;
        existing.RelayReserveMembers = [new() { Position = 2, UserId = users[1].UserId }];
        Assert.IsType<OkObjectResult>(await controller.Create(tournament.TournamentId, existing));
        db.ChangeTracker.Clear();

        var incoming = RequestWithAccount("Đội SQL mới", users[1].UserId);
        incoming.RelayReserveMembers = [new() { Position = 1, UserId = users[0].UserId }];
        var error = Assert.IsType<BadRequestObjectResult>(await controller.Create(tournament.TournamentId, incoming));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(error.Value));
        var lines = json.RootElement.GetProperty("message").GetString()!.Split('\n').Skip(1).ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Contains($"VĐV chính SQL (User ID #{users[0].UserId})", lines[0]);
        Assert.Contains("đội hình chính, vị trí 1", lines[0]);
        Assert.Contains($"VĐV dự bị SQL (User ID #{users[1].UserId})", lines[1]);
        Assert.Contains("dự bị, vị trí 2", lines[1]);
        Assert.All(lines, line => Assert.Contains("Đội SQL đã đăng ký", line));
        Assert.Single(await db.TournamentRegistrations.ToListAsync());
        Assert.Equal(4, await db.RelayTeamMembers.CountAsync());
        Assert.Single(await db.RelayTeamReserveMembers.ToListAsync());
    }

    private static CreateRegistrationForm GuestRequest(string teamName, int teamSize) => new()
    {
        RelayTeamName = teamName,
        RelayMembers = Enumerable.Range(1, teamSize).Select(position => new RelayRegistrationMemberForm
        {
            Position = position,
            DisplayName = $"Khách {position}"
        }).ToList()
    };

    private static CreateRegistrationForm RequestWithAccount(string teamName, long userId)
    {
        var request = GuestRequest(teamName, 4);
        request.RelayMembers[0].UserId = userId;
        request.RelayMembers[0].DisplayName = null;
        return request;
    }

    private static async Task<Tournament> SeedRelayTournamentAsync(PickleballDbContext db, int teamSize)
    {
        var tournament = new Tournament
        {
            Title = "Giải tiếp sức",
            Status = "DRAFT",
            GameType = "DOUBLE",
            GenderCategory = "OPEN",
            ExpectedTeams = 16,
            RegistrationFeeCurrency = "VND",
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
        await db.SaveChangesAsync();
        return tournament;
    }

    private static PickleballDbContext NewDb() => new(new DbContextOptionsBuilder<PickleballDbContext>()
        .UseInMemoryDatabase($"admin-relay-registration-{Guid.NewGuid():N}")
        .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private static AdminRegistrationsController NewController(PickleballDbContext db) =>
        new(db, null!, null!);
}
