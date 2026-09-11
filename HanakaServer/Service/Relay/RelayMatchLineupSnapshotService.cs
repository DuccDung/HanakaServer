using System.Text.Json;
using HanakaServer.Data;
using HanakaServer.Dtos.Relay;
using HanakaServer.Models;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Services.Relay;

public sealed class RelayMatchLineupSnapshotService(PickleballDbContext db, TimeProvider timeProvider)
{
    public async Task<bool> EnsureForMatchAsync(TournamentGroupMatch match, CancellationToken ct = default)
    {
        if (!match.Team1RegistrationId.HasValue || !match.Team2RegistrationId.HasValue)
            return false;

        var settings = await db.RelayTournamentSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TournamentId == match.TournamentId, ct);
        if (settings == null)
            return false;

        if (db.Database.IsRelational())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT MatchId FROM dbo.TournamentGroupMatches WITH (UPDLOCK, HOLDLOCK) WHERE MatchId = {match.MatchId}", ct);
        }

        var existing = await db.RelayMatchLineupSnapshots
            .Where(x => x.MatchId == match.MatchId)
            .ToDictionaryAsync(x => x.Side, ct);
        var registrations = new[] { match.Team1RegistrationId.Value, match.Team2RegistrationId.Value };

        foreach (var side in new[] { 1, 2 })
        {
            if (existing.TryGetValue(side, out var stored)
                && stored.RegistrationId != registrations[side - 1])
            {
                throw new RelayRuleException("MATCH_TEAMS_CHANGED",
                    "Đội của trận đấu đã thay đổi sau khi trận bắt đầu chấm điểm.");
            }
        }

        if (existing.Count == 2)
            return true;

        var teams = await db.RelayTeams.AsNoTracking().Include(x => x.Members)
            .Where(x => registrations.Contains(x.RegistrationId))
            .ToDictionaryAsync(x => x.RegistrationId, ct);
        if (teams.Count != 2 || teams.Values.Any(x => x.TournamentId != match.TournamentId))
            throw new RelayRuleException("LINEUP_NOT_READY", "Hai đội tiếp sức của trận đấu không hợp lệ.");

        var capturedAt = timeProvider.GetUtcNow();
        for (var side = 1; side <= 2; side++)
        {
            if (existing.ContainsKey(side))
                continue;

            var registrationId = registrations[side - 1];
            var team = teams[registrationId];
            var members = team.Members.OrderBy(x => x.Position)
                .Select(x => new RelayMemberInput(x.Position, x.UserId, x.DisplayName, x.AvatarUrl)).ToArray();
            RelayLineupService.ValidateMembers(members, settings.TeamSize, true);
            var lineup = new RelayBracketTeamDto(
                settings.TeamSize,
                team.Version,
                capturedAt,
                members.Select(x => new RelayBracketMemberDto(x.Position, x.UserId, x.DisplayName, x.AvatarUrl)).ToArray());

            db.RelayMatchLineupSnapshots.Add(new RelayMatchLineupSnapshot
            {
                MatchId = match.MatchId,
                Side = side,
                RegistrationId = registrationId,
                TeamName = team.TeamName,
                LineupVersion = team.Version,
                CapturedAtUtc = capturedAt,
                LineupJson = JsonSerializer.Serialize(lineup)
            });
        }

        return true;
    }

    public async Task CapturePlayedMatchesForTeamAsync(long tournamentId, long registrationId,
        CancellationToken ct = default)
    {
        var matches = await db.TournamentGroupMatches
            .Where(x => x.TournamentId == tournamentId
                && (x.Team1RegistrationId == registrationId || x.Team2RegistrationId == registrationId)
                && (x.IsCompleted || x.ScoreTeam1 != 0 || x.ScoreTeam2 != 0
                    || db.TournamentMatchScoreHistories.Any(h => h.MatchId == x.MatchId)))
            .Select(x => new TournamentGroupMatch
            {
                MatchId = x.MatchId,
                TournamentId = x.TournamentId,
                Team1RegistrationId = x.Team1RegistrationId,
                Team2RegistrationId = x.Team2RegistrationId
            })
            .ToListAsync(ct);

        foreach (var match in matches)
            await EnsureForMatchAsync(match, ct);
    }
}
