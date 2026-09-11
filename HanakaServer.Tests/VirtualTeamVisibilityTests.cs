using System.Security.Claims;
using HanakaServer.Controllers;
using HanakaServer.Data;
using HanakaServer.Models;
using HanakaServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HanakaServer.Tests;

public sealed class VirtualTeamVisibilityTests
{
    [Fact]
    public async Task Shared_bracket_api_masks_virtual_team_for_public_but_shows_it_to_admin()
    {
        await using var db = CreateDb();
        var (tournamentId, virtualTeamName) = await SeedMatchWithVirtualTeamAsync(db);

        var publicController = CreateController(db, isAdmin: false);
        var publicResult = Assert.IsType<OkObjectResult>(
            await publicController.GetRoundsWithMatches(tournamentId));
        var publicPayload = Assert.IsType<TournamentRoundsWithMatchesResponseDto>(publicResult.Value);
        var publicMatch = publicPayload.Rounds.Single().Groups.Single().Matches.Single();
        Assert.Equal("Chờ cập nhật", publicMatch.Team2Text);
        Assert.Equal("Chờ cập nhật", publicMatch.Team2!.DisplayName);
        Assert.Equal("", publicMatch.Team2.RegCode);
        Assert.DoesNotContain(virtualTeamName, new[] { publicMatch.Team2Text, publicMatch.Team2.Player1.Name });

        var adminController = CreateController(db, isAdmin: true);
        var adminResult = Assert.IsType<OkObjectResult>(
            await adminController.GetRoundsWithMatches(tournamentId));
        var adminPayload = Assert.IsType<TournamentRoundsWithMatchesResponseDto>(adminResult.Value);
        var adminMatch = adminPayload.Rounds.Single().Groups.Single().Matches.Single();
        Assert.Equal(virtualTeamName, adminMatch.Team2Text);
        Assert.Equal(virtualTeamName, adminMatch.Team2!.DisplayName);
        Assert.Equal("VIRTUAL-A1-S0002", adminMatch.Team2.RegCode);
    }

    private static TournamentClientController CreateController(PickleballDbContext db, bool isAdmin)
    {
        var identity = isAdmin
            ? new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "test")
            : new ClaimsIdentity();
        return new TournamentClientController(db, new TournamentStandingsService(db))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            }
        };
    }

    private static PickleballDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<PickleballDbContext>()
            .UseInMemoryDatabase($"virtual-team-visibility-{Guid.NewGuid():N}")
            .Options;
        return new PickleballDbContext(options);
    }

    private static async Task<(long TournamentId, string VirtualTeamName)> SeedMatchWithVirtualTeamAsync(
        PickleballDbContext db)
    {
        const string virtualTeamName = "Nguyễn Minh Anh";
        var tournament = new Tournament
        {
            Title = "Giải kiểm tra hiển thị",
            Status = "OPEN",
            GameType = "DOUBLE",
            GenderCategory = "OPEN",
            CreatedAt = DateTime.UtcNow
        };
        db.Tournaments.Add(tournament);
        await db.SaveChangesAsync();

        var realTeam = new TournamentRegistration
        {
            TournamentId = tournament.TournamentId,
            RegIndex = 1,
            RegCode = "REG-REAL-01",
            Player1Name = "Người chơi A",
            Player2Name = "Người chơi B",
            Success = true,
            Paid = true,
            CreatedAt = DateTime.UtcNow
        };
        var virtualTeam = new TournamentRegistration
        {
            TournamentId = tournament.TournamentId,
            RegIndex = 2,
            RegCode = "VIRTUAL-A1-S0002",
            Player1Name = virtualTeamName,
            IsVirtualTeam = true,
            Success = false,
            Paid = false,
            CreatedAt = DateTime.UtcNow
        };
        db.TournamentRegistrations.AddRange(realTeam, virtualTeam);
        await db.SaveChangesAsync();

        var round = new TournamentRoundMap
        {
            TournamentId = tournament.TournamentId,
            RoundKey = "R1",
            RoundLabel = "Vòng 1",
            SortOrder = 1,
            CreatedAt = DateTime.UtcNow
        };
        db.TournamentRoundMaps.Add(round);
        await db.SaveChangesAsync();

        var group = new TournamentRoundGroup
        {
            TournamentRoundMapId = round.TournamentRoundMapId,
            GroupName = "Nhánh A",
            SortOrder = 1,
            CreatedAt = DateTime.UtcNow
        };
        db.TournamentRoundGroups.Add(group);
        await db.SaveChangesAsync();

        db.TournamentGroupMatches.Add(new TournamentGroupMatch
        {
            TournamentId = tournament.TournamentId,
            TournamentRoundGroupId = group.TournamentRoundGroupId,
            Team1RegistrationId = realTeam.RegistrationId,
            Team2RegistrationId = virtualTeam.RegistrationId,
            Team1SourceType = "REGISTRATION",
            Team2SourceType = "REGISTRATION",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return (tournament.TournamentId, virtualTeamName);
    }
}
