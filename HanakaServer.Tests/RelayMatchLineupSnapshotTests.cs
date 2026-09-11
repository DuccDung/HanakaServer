using HanakaServer.Data;
using HanakaServer.Models;
using HanakaServer.Options;
using HanakaServer.Services.Relay;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace HanakaServer.Tests;

public sealed class RelayMatchLineupSnapshotTests
{
    [RelaySqlFact]
    public async Task Sql_snapshot_supports_referee_projection_reader_and_repeated_scoring()
    {
        // Use the shipped SQL migration: EnsureCreated alone could hide a CLR/SQL type mismatch.
        await using var sandbox = await RelaySqlSandbox.CreateAsync();
        await sandbox.SeedTeamsAsync();
        var clock = new RelayTestClock();
        var match = new TournamentGroupMatch
        {
            MatchId = 100,
            TournamentId = 1,
            Team1RegistrationId = 10,
            Team2RegistrationId = 20
        };

        await using (var db = sandbox.CreateDb())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var snapshots = new RelayMatchLineupSnapshotService(db, clock);
            Assert.True(await snapshots.EnsureForMatchAsync(match));
            Assert.Equal(2, await db.SaveChangesAsync());
            await transaction.CommitAsync();
        }

        await using (var db = sandbox.CreateDb())
        {
            // Match the projection and composite dictionary key used by ListMyMatches.
            var matchIds = new[] { match.MatchId };
            var names = (await db.RelayMatchLineupSnapshots.AsNoTracking()
                .Where(x => matchIds.Contains(x.MatchId))
                .Select(x => new { x.MatchId, x.Side, x.TeamName })
                .ToListAsync()).ToDictionary(x => (x.MatchId, x.Side), x => x.TeamName);
            Assert.Equal(2, names.Count);
            Assert.Equal("Đội 10", names[(100L, 1)]);
            Assert.Equal("Đội 20", names[(100L, 2)]);

            var secondSide = await db.RelayMatchLineupSnapshots.AsNoTracking()
                .SingleAsync(x => x.MatchId == match.MatchId && x.Side == 2);
            Assert.Equal(20L, secondSide.RegistrationId);
            Assert.Equal(clock.Now, secondSide.CapturedAtUtc);

            await using var transaction = await db.Database.BeginTransactionAsync();
            Assert.True(await new RelayMatchLineupSnapshotService(db, clock).EnsureForMatchAsync(match));
            Assert.Equal(0, await db.SaveChangesAsync());
            await transaction.CommitAsync();
            Assert.Equal(2, await db.RelayMatchLineupSnapshots.CountAsync());
        }

        await sandbox.SqlAsync("""
            UPDATE dbo.RelayTeams SET TeamName = N'Đội 10 mới', Version = Version + 1 WHERE RegistrationId = 10;
            UPDATE dbo.RelayTeamMembers SET DisplayName = N'VĐV mới' WHERE RegistrationId = 10 AND Position = 1;
            """);
        await using var readerDb = sandbox.CreateDb();
        var reader = new RelayTeamReader(readerDb,
            Microsoft.Extensions.Options.Options.Create(new RelayOptions { AdminPreviewEnabled = true }));
        var teams = await reader.ReadForMatchesAsync(1, [new RelayMatchTeamReference(100, 10, 20)]);
        Assert.Equal("Đội 10", teams[(100L, 1)].TeamName);
        Assert.Equal("VĐV 1", teams[(100L, 1)].Lineup.Members[0].DisplayName);
        Assert.Equal("Đội 20", teams[(100L, 2)].TeamName);
    }

    [Fact]
    public async Task Played_match_keeps_captured_roster_while_unstarted_match_reads_latest_roster()
    {
        await using var db = new PickleballDbContext(new DbContextOptionsBuilder<PickleballDbContext>()
            .UseInMemoryDatabase($"relay-match-lineup-{Guid.NewGuid():N}").Options);
        db.RelayTournamentSettings.Add(new RelayTournamentSettings
        {
            TournamentId = 1,
            TeamSize = 4,
            TargetScore = 40,
            LegDurationSeconds = 600,
            IsEnabled = true,
            Version = 1
        });
        db.TournamentRegistrations.AddRange(
            new TournamentRegistration { RegistrationId = 10, TournamentId = 1, RegCode = "R10", Player1Name = "Đội A" },
            new TournamentRegistration { RegistrationId = 20, TournamentId = 1, RegCode = "R20", Player1Name = "Đội B" });
        db.RelayTeams.AddRange(CreateTeam(10, "Đội A"), CreateTeam(20, "Đội B"));
        var played = new TournamentGroupMatch
        {
            MatchId = 100,
            TournamentId = 1,
            Team1RegistrationId = 10,
            Team2RegistrationId = 20
        };
        var upcoming = new TournamentGroupMatch
        {
            MatchId = 200,
            TournamentId = 1,
            Team1RegistrationId = 10,
            Team2RegistrationId = 20
        };
        db.TournamentGroupMatches.AddRange(played, upcoming);
        await db.SaveChangesAsync();

        var snapshots = new RelayMatchLineupSnapshotService(db, TimeProvider.System);
        Assert.True(await snapshots.EnsureForMatchAsync(played));
        await db.SaveChangesAsync();

        var team = await db.RelayTeams.Include(x => x.Members).SingleAsync(x => x.RegistrationId == 10);
        team.TeamName = "Đội A mới";
        team.Members.Single(x => x.Position == 1).DisplayName = "VĐV mới";
        team.Version++;
        await db.SaveChangesAsync();

        var reader = new RelayTeamReader(db,
            Microsoft.Extensions.Options.Options.Create(new RelayOptions { AdminPreviewEnabled = true }));
        var result = await reader.ReadForMatchesAsync(1,
        [
            new RelayMatchTeamReference(played.MatchId, 10, 20),
            new RelayMatchTeamReference(upcoming.MatchId, 10, 20)
        ]);

        Assert.Equal("Đội A", result[(played.MatchId, 1)].TeamName);
        Assert.Equal("Đội 10.1", result[(played.MatchId, 1)].Lineup.Members[0].DisplayName);
        Assert.Equal("Đội A mới", result[(upcoming.MatchId, 1)].TeamName);
        Assert.Equal("VĐV mới", result[(upcoming.MatchId, 1)].Lineup.Members[0].DisplayName);
    }

    private static RelayTeam CreateTeam(long registrationId, string name)
    {
        var team = new RelayTeam
        {
            RegistrationId = registrationId,
            TournamentId = 1,
            TeamName = name,
            Version = 1
        };
        foreach (var position in Enumerable.Range(1, 4))
        {
            team.Members.Add(new RelayTeamMember
            {
                RegistrationId = registrationId,
                Position = position,
                DisplayName = $"Đội {registrationId}.{position}"
            });
        }
        return team;
    }
}
