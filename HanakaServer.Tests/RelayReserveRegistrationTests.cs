using System.Security.Claims;
using System.Text.Json;
using HanakaServer.Controllers;
using HanakaServer.Data;
using HanakaServer.Dtos;
using HanakaServer.Dtos.Relay;
using HanakaServer.Dtos.Brackets;
using HanakaServer.Models;
using HanakaServer.Options;
using HanakaServer.Services;
using HanakaServer.Services.Relay;
using HanakaServer.Services.Brackets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HanakaServer.Tests;

public sealed class RelayReserveRegistrationTests
{
    [Fact]
    public async Task Bracket_preview_uses_only_complete_main_rosters_even_with_four_reserves()
    {
        await using var db = NewDb();
        var (t, _) = await Seed(db, 4);
        var controller = new AdminRegistrationsController(db, null!, null!);
        for (var i = 0; i < 4; i++)
        {
            var request = GuestRequest();
            request.RelayTeamName += i;
            request.RelayReserveMembers = Enumerable.Range(1, 4).Select(p => new RelayRegistrationMemberForm
                { Position = p, DisplayName = "Dự bị " + p }).ToList();
            Assert.IsType<OkObjectResult>(await controller.Create(t.TournamentId, request));
        }
        t.RegistrationLockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var templates = new BracketTemplateService(db, new BracketTemplateValidationService(), NullLogger<BracketTemplateService>.Instance);
        var created = await templates.CreateAsync(new CreateBracketTemplateRequest
        {
            TemplateCode = "RESERVE-4", TemplateName = "Kiểm thử dự bị", ParticipantMode = BracketTemplateParticipantModes.RelayTeam,
            FormatType = BracketTemplateFormatTypes.SingleElimination, MinimumTeams = 4, SeedCapacity = 4,
            DefaultSeedingMethod = BracketSeedingMethods.RegistrationOrder
        }, null, default);
        Assert.True(created.Success, created.Message);
        var version = created.Data!.Versions.Single();
        var graph = BracketTemplateService.GenerateSingleElimination(4, false, [0]);
        graph.RowVersion = version.RowVersion;
        Assert.True((await templates.SaveGraphAsync(version.BracketTemplateVersionId, graph, default)).Success);
        Assert.True((await templates.PublishAsync(version.BracketTemplateVersionId, null, default)).Success);
        var applications = new TournamentBracketApplicationService(db, templates, new BracketTemplateValidationService(),
            NullLogger<TournamentBracketApplicationService>.Instance,
            Microsoft.Extensions.Options.Options.Create(new RelayOptions { AdminPreviewEnabled = true }));
        var previewRequest = new TournamentBracketPreviewRequest
        {
            BracketTemplateVersionId = version.BracketTemplateVersionId, SeedingMethod = BracketSeedingMethods.RegistrationOrder
        };
        var preview = await applications.PreviewAsync(t.TournamentId, previewRequest, default);
        Assert.True(preview.Success, preview.Message);
        Assert.Equal(4, preview.Data!.Seeds.Count);
        Assert.All(preview.Data.Seeds, seed =>
        {
            Assert.Equal(4, seed.Relay!.Members.Count);
            Assert.DoesNotContain(seed.Relay.Members, m => m.DisplayName.StartsWith("Dự bị"));
        });
        db.RelayTeamMembers.Remove(await db.RelayTeamMembers.FirstAsync());
        await db.SaveChangesAsync();
        var incomplete = await applications.PreviewAsync(t.TournamentId, previewRequest, default);
        Assert.True(incomplete.Success, incomplete.Message);
        Assert.Equal(3, incomplete.Data!.EligibleRegistrationCount);
        Assert.Equal(1, incomplete.Data.ExcludedRegistrationCount);
    }

    [Theory]
    [InlineData(4, 0)] [InlineData(4, 1)] [InlineData(4, 4)]
    [InlineData(6, 0)] [InlineData(6, 4)] [InlineData(8, 0)] [InlineData(8, 4)]
    public async Task User_registration_keeps_main_roster_capacity_and_public_contract(int size, int count)
    {
        await using var db = NewDb();
        var (tournament, users) = await Seed(db, size);
        var request = UserRequest(users, size, count);
        if (count == 0) request.ReserveMembers = null; // Existing app payload.
        Assert.IsType<OkObjectResult>(await UserController(db, users[0].UserId).RegisterRelayTeam(tournament.TournamentId, request, default));
        db.ChangeTracker.Clear();
        var team = await db.RelayTeams.Include(x => x.Members).Include(x => x.ReserveMembers).SingleAsync();
        Assert.Equal(size, team.Members.Count);
        Assert.Equal(count, team.ReserveMembers.Count);
        Assert.Single(await db.TournamentRegistrations.ToListAsync());
        Assert.Equal(users[0].UserId, (await db.TournamentRegistrations.SingleAsync()).Player1UserId);

        var publicResult = Json(Assert.IsType<OkObjectResult>(await new PublicTournamentsController(db, Config())
            .PublicRegistrations(tournament.TournamentId)));
        var item = publicResult.GetProperty("SuccessItems")[0];
        Assert.Equal(size, item.GetProperty("Members").GetArrayLength());
        Assert.Equal(count, item.GetProperty("ReserveMembers").GetArrayLength());
        Assert.True(item.GetProperty("IsReady").GetBoolean());
        Assert.Equal(15, publicResult.GetProperty("Counts").GetProperty("CapacityLeft").GetInt32());

        var viewer = count > 0 ? users[size].UserId : users[0].UserId;
        var state = Json(Assert.IsType<OkObjectResult>(await UserController(db, viewer)
            .GetMyTournamentRegistrationState(tournament.TournamentId, default)));
        Assert.False(state.GetProperty("canRegister").GetBoolean());
        Assert.Equal(count, state.GetProperty("existingRegistration").GetProperty("ReserveMembers").GetArrayLength());
        if (count > 0)
        {
            var search = Json(Assert.IsType<OkObjectResult>(await UserController(db, users[^1].UserId)
                .SearchRelayMember(tournament.TournamentId, viewer.ToString(), 20, default)));
            var found = search.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("UserId").GetInt64() == viewer);
            Assert.False(found.GetProperty("CanSelect").GetBoolean());
        }
    }

    [Theory]
    [InlineData("too-many")] [InlineData("main-duplicate")] [InlineData("reserve-duplicate")]
    [InlineData("position-duplicate")] [InlineData("position-invalid")] [InlineData("inactive")]
    [InlineData("missing-user")] [InlineData("missing-main")]
    public async Task Invalid_reserves_or_incomplete_main_roster_do_not_create_registration(string kind)
    {
        await using var db = NewDb();
        var (t, users) = await Seed(db, 4);
        var request = UserRequest(users, 4, 4);
        switch (kind)
        {
            case "too-many": request.ReserveMembers!.Add(new() { Position = 5, UserId = users[8].UserId }); break;
            case "main-duplicate": request.ReserveMembers![0].UserId = users[0].UserId; break;
            case "reserve-duplicate": request.ReserveMembers![1].UserId = request.ReserveMembers[0].UserId; break;
            case "position-duplicate": request.ReserveMembers![1].Position = 1; break;
            case "position-invalid": request.ReserveMembers![0].Position = 0; break;
            case "inactive": users[4].IsActive = false; await db.SaveChangesAsync(); break;
            case "missing-user": request.ReserveMembers![0].UserId = 99999; break;
            case "missing-main": request.Members.RemoveAt(0); break;
        }
        var result = Assert.IsAssignableFrom<ObjectResult>(await UserController(db, users[0].UserId)
            .RegisterRelayTeam(t.TournamentId, request, default));
        Assert.Equal(400, result.StatusCode);
        Assert.Empty(await db.TournamentRegistrations.ToListAsync());
        Assert.Empty(await db.RelayTeamReserveMembers.ToListAsync());
    }

    [Fact]
    public async Task Cross_team_reserve_and_main_assignments_are_rejected_in_both_directions()
    {
        await using var db = NewDb();
        var (t, users) = await Seed(db, 4);
        Assert.IsType<OkObjectResult>(await UserController(db, users[0].UserId)
            .RegisterRelayTeam(t.TournamentId, UserRequest(users, 4, 1), default));
        var second = UserRequest(users.Skip(5).ToArray(), 4, 0);
        second.Members[0].UserId = users[4].UserId;
        var result = Assert.IsAssignableFrom<ObjectResult>(await UserController(db, users[5].UserId)
            .RegisterRelayTeam(t.TournamentId, second, default));
        Assert.Equal(409, result.StatusCode);

        var admin = new AdminRegistrationsController(db, null!, null!);
        foreach (var taken in new[] { users[0].UserId, users[4].UserId })
        {
            var create = GuestRequest();
            create.RelayReserveMembers = [new() { Position = 1, UserId = taken }];
            var rejected = Assert.IsAssignableFrom<ObjectResult>(await admin.Create(t.TournamentId, create));
            Assert.Equal(400, rejected.StatusCode);
        }
        Assert.Single(await db.RelayTeams.ToListAsync());
    }

    [Fact]
    public async Task Admin_edits_preserve_omitted_reserves_replace_explicitly_and_delete_with_registration()
    {
        await using var db = NewDb();
        var (t, users) = await Seed(db, 4);
        await CheckAdminLifecycle(db, t, users);
    }

    private static async Task CheckAdminLifecycle(PickleballDbContext db, Tournament t, User[] users)
    {
        var controller = new AdminRegistrationsController(db, null!, null!);
        var request = GuestRequest();
        request.RelayReserveMembers = [new() { Position = 1, UserId = users[0].UserId }, new() { Position = 4, DisplayName = "Khách dự bị" }];
        var created = Json(Assert.IsType<OkObjectResult>(await controller.Create(t.TournamentId, request)));
        var id = created.GetProperty("RegistrationId").GetInt64();
        Assert.Equal(2, created.GetProperty("RelayReserveMembers").GetArrayLength());
        Assert.Equal(4, created.GetProperty("RelayMemberCount").GetInt32());
        var update = new UpdateRegistrationPlayersForm { RelayTeamName = "Đội sửa", RelayExpectedVersion = 1, RelayMembers = request.RelayMembers };
        Assert.IsType<OkObjectResult>(await controller.UpdatePlayers(id, update));
        Assert.Equal(2, await db.RelayTeamReserveMembers.CountAsync());

        // Old clients must not promote a reserve into the main roster without removing its reserve assignment.
        update.RelayExpectedVersion = 2;
        update.RelayMembers[0].UserId = users[0].UserId;
        Assert.IsType<BadRequestObjectResult>(await controller.UpdatePlayers(id, update));
        update.RelayMembers[0].UserId = null;
        update.RelayReserveMembersIncluded = true;
        Assert.IsType<OkObjectResult>(await controller.UpdatePlayers(id, update));
        Assert.Empty(await db.RelayTeamReserveMembers.ToListAsync());

        update.RelayExpectedVersion = 3;
        update.RelayReserveMembers = [new() { Position = 2, UserId = users[1].UserId }];
        Assert.IsType<OkObjectResult>(await controller.UpdatePlayers(id, update));
        Assert.Equal(users[1].UserId, (await db.RelayTeamReserveMembers.SingleAsync()).UserId);
        update.RelayExpectedVersion = 3;
        Assert.IsType<ConflictObjectResult>(await controller.UpdatePlayers(id, update));
        Assert.IsType<OkObjectResult>(await controller.Delete(id, default));
        Assert.Empty(await db.RelayTeamReserveMembers.ToListAsync());
        Assert.Empty(await db.RelayTeams.ToListAsync());
    }

    [Fact]
    public async Task Reserves_do_not_complete_a_short_roster_or_enter_match_snapshots()
    {
        await using var db = NewDb();
        var (t, users) = await Seed(db, 4);
        Assert.IsType<OkObjectResult>(await UserController(db, users[0].UserId)
            .RegisterRelayTeam(t.TournamentId, UserRequest(users, 4, 4), default));
        var team = await db.RelayTeams.Include(x => x.Members).SingleAsync();
        var opponent = Json(Assert.IsType<OkObjectResult>(await new AdminRegistrationsController(db, null!, null!)
            .Create(t.TournamentId, GuestRequest()))).GetProperty("RegistrationId").GetInt64();
        var match = new TournamentGroupMatch { TournamentId = t.TournamentId, Team1RegistrationId = team.RegistrationId, Team2RegistrationId = opponent };
        db.TournamentGroupMatches.Add(match);
        await db.SaveChangesAsync();
        var service = new RelayMatchLineupSnapshotService(db, TimeProvider.System);
        await service.EnsureForMatchAsync(match);
        await db.SaveChangesAsync();
        var before = await db.RelayMatchLineupSnapshots.Where(x => x.Side == 1).Select(x => x.LineupJson).SingleAsync();
        Assert.DoesNotContain("Dự bị", before);
        var reserves = await db.RelayTeamReserveMembers.ToListAsync();
        db.RelayTeamReserveMembers.RemoveRange(reserves);
        await db.SaveChangesAsync();
        await service.EnsureForMatchAsync(match);
        Assert.Equal(before, await db.RelayMatchLineupSnapshots.Where(x => x.Side == 1).Select(x => x.LineupJson).SingleAsync());

        db.RelayTeamMembers.Remove(team.Members.First());
        db.RelayTeamReserveMembers.AddRange(Enumerable.Range(1, 4).Select(p => new RelayTeamReserveMember
            { RegistrationId = team.RegistrationId, Position = p, DisplayName = "Dự bị " + p }));
        await db.SaveChangesAsync();
        var read = new RelayTeamReader(db, Microsoft.Extensions.Options.Options.Create(new RelayOptions { AdminPreviewEnabled = true }));
        Assert.Empty(await read.ReadReadyAsync(t.TournamentId, [team.RegistrationId], default));
        var list = Json(Assert.IsType<OkObjectResult>(await new PublicTournamentsController(db, Config()).PublicRegistrations(t.TournamentId)));
        Assert.False(list.GetProperty("SuccessItems")[0].GetProperty("IsReady").GetBoolean());
    }

    [RelaySqlFact]
    public async Task Actual_sql_migration_is_repeatable_and_supports_all_registration_paths()
    {
        await using var sandbox = await RelaySqlSandbox.CreateFullSchemaAsync();
        await sandbox.MigrateReservesAsync();
        await using var db = sandbox.CreateDb();
        var (t, users) = await Seed(db, 4);
        await CheckAdminLifecycle(db, t, users);
        Assert.IsType<OkObjectResult>(await UserController(db, users[0].UserId)
            .RegisterRelayTeam(t.TournamentId, UserRequest(users, 4, 4), default));
        db.ChangeTracker.Clear();
        var id = await db.RelayTeams.Select(x => x.RegistrationId).SingleAsync();
        Assert.Equal(4, await db.RelayTeamReserveMembers.CountAsync());
        await sandbox.MigrateReservesAsync();
        Assert.Equal(4, await db.RelayTeamReserveMembers.CountAsync());
        await Assert.ThrowsAsync<SqlException>(() => sandbox.SqlAsync($"INSERT dbo.RelayTeamReserveMembers (RegistrationId, Position, DisplayName) VALUES ({id},5,N'Không hợp lệ')"));
        var result = Json(Assert.IsType<OkObjectResult>(await new PublicTournamentsController(db, Config()).PublicRegistrations(t.TournamentId)));
        Assert.Equal(4, result.GetProperty("SuccessItems")[0].GetProperty("ReserveMembers").GetArrayLength());
        Assert.Equal(4, result.GetProperty("SuccessItems")[0].GetProperty("Members").GetArrayLength());
    }

    private static JsonElement Json(OkObjectResult result) => JsonSerializer.SerializeToElement(result.Value);
    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Relay:AdminPreviewEnabled"] = "true" }).Build();
    private static PickleballDbContext NewDb() => new(new DbContextOptionsBuilder<PickleballDbContext>()
        .UseInMemoryDatabase("relay-reserves-" + Guid.NewGuid()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static TournamentRegistrationUserController UserController(PickleballDbContext db, long userId) =>
        new(db, Config(), null!, new RealtimeHub(), new RelayLegacyWriteGuard(db,
            Microsoft.Extensions.Options.Options.Create(new RelayOptions { AdminPreviewEnabled = true })))
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("uid", userId.ToString())], "test")) } } };
    private static CreateRelayUserRegistrationRequest UserRequest(User[] users, int size, int count) => new()
    {
        TeamName = "Đội kiểm thử", Members = users.Skip(1).Take(size - 1).Select((u, i) => new RelayUserRegistrationMemberRequest
            { Position = i + 2, UserId = u.UserId }).ToList(),
        ReserveMembers = users.Skip(size).Take(count).Select((u, i) => new RelayUserReserveMemberRequest
            { Position = i + 1, UserId = u.UserId }).ToList()
    };
    private static CreateRegistrationForm GuestRequest() => new()
    {
        RelayTeamName = "Đội khách", RelayMembers = Enumerable.Range(1, 4).Select(p => new RelayRegistrationMemberForm
            { Position = p, DisplayName = "Khách " + p }).ToList()
    };
    private static async Task<(Tournament, User[])> Seed(PickleballDbContext db, int size)
    {
        var t = new Tournament { Title = "Giải kiểm thử dự bị", Status = "OPEN", GameType = "DOUBLE", GenderCategory = "OPEN",
            ExpectedTeams = 16, RegisterDeadline = DateTime.Now.AddDays(2), RegistrationFeeCurrency = "VND", CreatedAt = DateTime.UtcNow };
        var users = Enumerable.Range(1, 16).Select(p => new User { FullName = "VĐV " + p, Phone = $"090009{p:0000}",
            IsActive = true, RatingDouble = 3.5m, CreatedAt = DateTime.UtcNow }).ToArray();
        db.Tournaments.Add(t); db.Users.AddRange(users); await db.SaveChangesAsync();
        db.RelayTournamentSettings.Add(new() { TournamentId = t.TournamentId, TeamSize = size, Version = 1 });
        await db.SaveChangesAsync();
        return (t, users);
    }
}
