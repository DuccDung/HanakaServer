using HanakaServer.Data;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Services
{
    public interface ITournamentStandingsService
    {
        Task<IReadOnlyList<GroupStandingRow>> GetGroupStandingsAsync(long groupId, CancellationToken ct = default);
        Task<bool> IsGroupCompletedAsync(long groupId, CancellationToken ct = default);
    }

    public sealed class GroupStandingRow
    {
        public long RegistrationId { get; set; }
        public string TeamName { get; set; } = "";
        public int Rank { get; set; }
        public int Played { get; set; }
        public int Wins { get; set; }
        public int Points { get; set; }
        public int ScoreFor { get; set; }
        public int ScoreAgainst { get; set; }
        public int ScoreDiff { get; set; }
    }

    public sealed class TournamentStandingsService : ITournamentStandingsService
    {
        private readonly PickleballDbContext _db;

        public TournamentStandingsService(PickleballDbContext db)
        {
            _db = db;
        }

        public async Task<IReadOnlyList<GroupStandingRow>> GetGroupStandingsAsync(long groupId, CancellationToken ct = default)
        {
            var groupInfo = await (
                from g in _db.TournamentRoundGroups.AsNoTracking()
                where g.TournamentRoundGroupId == groupId
                join rm in _db.TournamentRoundMaps.AsNoTracking()
                    on g.TournamentRoundMapId equals rm.TournamentRoundMapId
                join t in _db.Tournaments.AsNoTracking()
                    on rm.TournamentId equals t.TournamentId
                select new
                {
                    g.TournamentRoundGroupId,
                    t.GameType
                })
                .FirstOrDefaultAsync(ct);

            if (groupInfo == null)
                return Array.Empty<GroupStandingRow>();

            var matches = await _db.TournamentGroupMatches.AsNoTracking()
                .Where(x => x.TournamentRoundGroupId == groupId
                    && x.IsCompleted
                    && x.WinnerRegistrationId.HasValue
                    && x.Team1RegistrationId.HasValue
                    && x.Team2RegistrationId.HasValue)
                .Select(x => new
                {
                    x.MatchId,
                    Team1RegistrationId = x.Team1RegistrationId!.Value,
                    Team2RegistrationId = x.Team2RegistrationId!.Value,
                    x.ScoreTeam1,
                    x.ScoreTeam2,
                    WinnerRegistrationId = x.WinnerRegistrationId!.Value
                })
                .ToListAsync(ct);

            var registrationIds = matches
                .SelectMany(x => new[] { x.Team1RegistrationId, x.Team2RegistrationId })
                .Distinct()
                .ToList();

            var registrations = await _db.TournamentRegistrations.AsNoTracking()
                .Where(x => registrationIds.Contains(x.RegistrationId))
                .Select(x => new
                {
                    x.RegistrationId,
                    x.RegIndex,
                    x.Player1Name,
                    x.Player2Name
                })
                .ToListAsync(ct);

            var regMap = registrations.ToDictionary(
                x => x.RegistrationId,
                x => new
                {
                    x.RegIndex,
                    TeamName = BuildTeamName(groupInfo.GameType, x.Player1Name, x.Player2Name)
                });

            var stats = new Dictionary<long, GroupStandingRow>();

            void EnsureTeam(long registrationId)
            {
                if (stats.ContainsKey(registrationId))
                    return;

                stats[registrationId] = new GroupStandingRow
                {
                    RegistrationId = registrationId,
                    TeamName = regMap.TryGetValue(registrationId, out var reg)
                        ? reg.TeamName
                        : $"Doi #{registrationId}"
                };
            }

            foreach (var match in matches)
            {
                EnsureTeam(match.Team1RegistrationId);
                EnsureTeam(match.Team2RegistrationId);

                var team1 = stats[match.Team1RegistrationId];
                var team2 = stats[match.Team2RegistrationId];

                team1.Played++;
                team2.Played++;

                team1.ScoreFor += match.ScoreTeam1;
                team1.ScoreAgainst += match.ScoreTeam2;
                team1.ScoreDiff = team1.ScoreFor - team1.ScoreAgainst;

                team2.ScoreFor += match.ScoreTeam2;
                team2.ScoreAgainst += match.ScoreTeam1;
                team2.ScoreDiff = team2.ScoreFor - team2.ScoreAgainst;

                if (match.WinnerRegistrationId == match.Team1RegistrationId)
                {
                    team1.Wins++;
                    team1.Points++;
                }
                else if (match.WinnerRegistrationId == match.Team2RegistrationId)
                {
                    team2.Wins++;
                    team2.Points++;
                }
            }

            var ordered = stats.Values
                .OrderByDescending(x => x.Wins)
                .ThenByDescending(x => x.Points)
                .ThenByDescending(x => x.ScoreDiff)
                .ThenByDescending(x => x.ScoreFor)
                .ThenBy(x => regMap.TryGetValue(x.RegistrationId, out var reg) ? reg.RegIndex : int.MaxValue)
                .ThenBy(x => x.RegistrationId)
                .ToList();

            for (var i = 0; i < ordered.Count; i++)
                ordered[i].Rank = i + 1;

            return ordered;
        }

        public async Task<bool> IsGroupCompletedAsync(long groupId, CancellationToken ct = default)
        {
            var matches = await _db.TournamentGroupMatches.AsNoTracking()
                .Where(x => x.TournamentRoundGroupId == groupId)
                .Select(x => new
                {
                    x.IsCompleted,
                    x.WinnerRegistrationId,
                    x.Team1RegistrationId,
                    x.Team2RegistrationId
                })
                .ToListAsync(ct);

            return matches.Count > 0
                && matches.All(x => x.IsCompleted
                    && x.WinnerRegistrationId.HasValue
                    && x.Team1RegistrationId.HasValue
                    && x.Team2RegistrationId.HasValue);
        }

        private static string BuildTeamName(string? gameType, string? player1Name, string? player2Name)
        {
            var gt = (gameType ?? "DOUBLE").Trim().ToUpperInvariant();
            var p1 = (player1Name ?? "").Trim();
            var p2 = (player2Name ?? "").Trim();

            if (gt == "SINGLE")
                return p1;

            if (string.IsNullOrWhiteSpace(p2))
                return p1;

            return $"{p1}/{p2}";
        }
    }
}
