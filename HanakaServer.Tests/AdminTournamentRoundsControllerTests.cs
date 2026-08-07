using System.Text.Json;
using HanakaServer.Controllers;
using HanakaServer.Data;
using HanakaServer.Helpers;
using HanakaServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HanakaServer.Tests;

public sealed class AdminTournamentRoundsControllerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("xoa")]
    [InlineData(" XOA ")]
    public async Task Delete_requires_exact_XOA_confirmation(string? confirmation)
    {
        await using var db = CreateDb();
        await SeedRoundWithDependentMatchAsync(db);
        var controller = new AdminTournamentRoundsController(db);

        var result = await controller.Delete(16, 101, new DeleteRoundMapDto
        {
            Confirmation = confirmation
        });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.True(await db.TournamentRoundMaps.AnyAsync(x => x.TournamentRoundMapId == 101));
        Assert.True(await db.TournamentGroupMatches.AnyAsync(x => x.MatchId == 301));
    }

    [Fact]
    public async Task DeleteSummary_returns_the_data_that_will_be_affected()
    {
        await using var db = CreateDb();
        await SeedRoundWithDependentMatchAsync(db);
        var controller = new AdminTournamentRoundsController(db);

        var result = Assert.IsType<OkObjectResult>(await controller.DeleteSummary(16, 101));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        var summary = json.RootElement;

        Assert.Equal(1, summary.GetProperty("GroupCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("MatchCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("CompletedMatchCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("ScheduledMatchCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("ScoreHistoryCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("NotificationCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("DependentMatchCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("DependentSlotCount").GetInt32());
    }

    [Fact]
    public async Task Delete_removes_round_contents_and_preserves_external_match_result()
    {
        await using var db = CreateDb();
        await SeedRoundWithDependentMatchAsync(db);
        var controller = new AdminTournamentRoundsController(db);

        var result = Assert.IsType<OkObjectResult>(await controller.Delete(16, 101,
            new DeleteRoundMapDto { Confirmation = "XOA" }));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        Assert.Equal(1, json.RootElement.GetProperty("deletedMatchCount").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("detachedDependentSlotCount").GetInt32());

        db.ChangeTracker.Clear();
        Assert.False(await db.TournamentRoundMaps.AnyAsync(x => x.TournamentRoundMapId == 101));
        Assert.False(await db.TournamentRoundGroups.AnyAsync(x => x.TournamentRoundGroupId == 201));
        Assert.False(await db.TournamentGroupMatches.AnyAsync(x => x.MatchId == 301));
        Assert.False(await db.TournamentMatchScoreHistories.AnyAsync(x => x.MatchId == 301));
        Assert.False(await db.UserNotifications.AnyAsync(x => x.RefType == "MATCH" && x.RefId == 301));

        var externalMatch = await db.TournamentGroupMatches.SingleAsync(x => x.MatchId == 302);
        Assert.Equal(MatchSourceTypes.Registration, externalMatch.Team1SourceType);
        Assert.Null(externalMatch.Team1SourceMatchId);
        Assert.Null(externalMatch.Team1SourceGroupId);
        Assert.Equal(9001, externalMatch.Team1RegistrationId);
        Assert.True(externalMatch.IsCompleted);
        Assert.Equal(11, externalMatch.ScoreTeam1);
        Assert.Equal(7, externalMatch.ScoreTeam2);
        Assert.Equal(9001, externalMatch.WinnerRegistrationId);
    }

    private static PickleballDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<PickleballDbContext>()
            .UseInMemoryDatabase($"round-delete-{Guid.NewGuid():N}")
            .Options;
        return new PickleballDbContext(options);
    }

    private static async Task SeedRoundWithDependentMatchAsync(PickleballDbContext db)
    {
        var now = DateTime.UtcNow;
        db.Tournaments.Add(new Tournament
        {
            TournamentId = 16,
            Status = "ACTIVE",
            Title = "Delete round test",
            GenderCategory = "OPEN",
            RegistrationFeeCurrency = "VND",
            CreatedAt = now
        });
        db.TournamentRoundMaps.AddRange(
            new TournamentRoundMap
            {
                TournamentRoundMapId = 101,
                TournamentId = 16,
                RoundKey = "R1",
                RoundLabel = "Vòng 1",
                SortOrder = 1,
                CreatedAt = now
            },
            new TournamentRoundMap
            {
                TournamentRoundMapId = 102,
                TournamentId = 16,
                RoundKey = "R2",
                RoundLabel = "Vòng 2",
                SortOrder = 2,
                CreatedAt = now
            });
        db.TournamentRoundGroups.AddRange(
            new TournamentRoundGroup
            {
                TournamentRoundGroupId = 201,
                TournamentRoundMapId = 101,
                GroupName = "Nhánh R1",
                SortOrder = 1,
                CreatedAt = now
            },
            new TournamentRoundGroup
            {
                TournamentRoundGroupId = 202,
                TournamentRoundMapId = 102,
                GroupName = "Nhánh R2",
                SortOrder = 1,
                CreatedAt = now
            });
        db.TournamentGroupMatches.AddRange(
            new TournamentGroupMatch
            {
                MatchId = 301,
                TournamentRoundGroupId = 201,
                TournamentId = 16,
                Team1SourceType = MatchSourceTypes.Registration,
                Team2SourceType = MatchSourceTypes.Registration,
                Team1RegistrationId = 9001,
                Team2RegistrationId = 9002,
                StartAt = now,
                ScoreTeam1 = 11,
                ScoreTeam2 = 5,
                IsCompleted = true,
                WinnerRegistrationId = 9001,
                CreatedAt = now
            },
            new TournamentGroupMatch
            {
                MatchId = 302,
                TournamentRoundGroupId = 202,
                TournamentId = 16,
                Team1SourceType = MatchSourceTypes.WinnerMatch,
                Team1SourceMatchId = 301,
                Team1RegistrationId = 9001,
                Team2SourceType = MatchSourceTypes.Registration,
                Team2RegistrationId = 9003,
                ScoreTeam1 = 11,
                ScoreTeam2 = 7,
                IsCompleted = true,
                WinnerRegistrationId = 9001,
                CreatedAt = now
            });
        db.TournamentMatchScoreHistories.Add(new TournamentMatchScoreHistory
        {
            ScoreHistoryId = 401,
            MatchId = 301,
            RefereeUserId = 8001,
            ScoreTeam1 = 11,
            ScoreTeam2 = 5,
            IsCompleted = true,
            WinnerRegistrationId = 9001,
            CreatedAt = now
        });
        db.UserNotifications.Add(new UserNotification
        {
            NotificationId = 501,
            UserId = 7001,
            NotificationType = "MATCH_RESULT",
            Title = "Kết quả trận",
            Body = "Trận đã kết thúc",
            RefType = "MATCH",
            RefId = 301,
            CreatedAt = now
        });
        await db.SaveChangesAsync();
    }
}
