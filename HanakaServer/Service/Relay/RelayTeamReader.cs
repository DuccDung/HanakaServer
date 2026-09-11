using HanakaServer.Data;
using HanakaServer.Dtos.Relay;
using HanakaServer.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace HanakaServer.Services.Relay;

public sealed record RelayTeamSummary(long RegistrationId, string TeamName, RelayBracketTeamDto Lineup);

public sealed class RelayTeamReader(PickleballDbContext db, IOptions<RelayOptions> options)
{
    public async Task<IReadOnlyDictionary<long, RelayTeamSummary>> ReadReadyAsync(long tournamentId,
        IEnumerable<long> registrationIds, CancellationToken ct = default)
    {
        if (!options.Value.AdminPreviewEnabled) return new Dictionary<long, RelayTeamSummary>();
        var ids = registrationIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<long, RelayTeamSummary>();
        var settings = await db.RelayTournamentSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TournamentId == tournamentId, ct);
        if (settings == null) return new Dictionary<long, RelayTeamSummary>();
        var teams = await db.RelayTeams.AsNoTracking().Include(x => x.Members)
            .Where(x => x.TournamentId == tournamentId && ids.Contains(x.RegistrationId)
                && db.TournamentRegistrations.Any(r => r.RegistrationId == x.RegistrationId && !r.IsVirtualTeam))
            .ToListAsync(ct);
        return teams.Where(x => IsComplete(x, settings.TeamSize)).ToDictionary(x => x.RegistrationId,
            x => new RelayTeamSummary(x.RegistrationId, x.TeamName,
                new(settings.TeamSize, x.Version, x.LineupLockedAtUtc,
                    x.Members.OrderBy(m => m.Position)
                        .Select(m => new RelayBracketMemberDto(m.Position, m.UserId, m.DisplayName, m.AvatarUrl)).ToArray())));
    }

    [Obsolete("Use ReadReadyAsync. Relay teams no longer have to be locked.")]
    public Task<IReadOnlyDictionary<long, RelayTeamSummary>> ReadLockedAsync(long tournamentId,
        IEnumerable<long> registrationIds, CancellationToken ct = default) =>
        ReadReadyAsync(tournamentId, registrationIds, ct);

    public async Task<IReadOnlyDictionary<(long MatchId, int Side), RelayTeamSummary>> ReadForMatchesAsync(
        long tournamentId, IEnumerable<RelayMatchTeamReference> matches, CancellationToken ct = default)
    {
        if (!options.Value.AdminPreviewEnabled)
            return new Dictionary<(long, int), RelayTeamSummary>();
        var rows = matches.ToArray();
        if (rows.Length == 0)
            return new Dictionary<(long, int), RelayTeamSummary>();

        var current = await ReadReadyAsync(tournamentId,
            rows.SelectMany(x => new[] { x.Team1RegistrationId, x.Team2RegistrationId })
                .Where(x => x.HasValue).Select(x => x!.Value), ct);
        var matchIds = rows.Select(x => x.MatchId).Distinct().ToArray();
        var stored = await db.RelayMatchLineupSnapshots.AsNoTracking()
            .Where(x => matchIds.Contains(x.MatchId))
            .ToListAsync(ct);
        var snapshots = stored.ToDictionary(x => (x.MatchId, x.Side), x =>
        {
            var lineup = JsonSerializer.Deserialize<RelayBracketTeamDto>(x.LineupJson)
                ?? throw new InvalidOperationException($"Snapshot đội hình trận {x.MatchId} không hợp lệ.");
            return new RelayTeamSummary(x.RegistrationId, x.TeamName, lineup);
        });

        var result = new Dictionary<(long, int), RelayTeamSummary>();
        foreach (var row in rows)
        {
            Add(row.MatchId, 1, row.Team1RegistrationId);
            Add(row.MatchId, 2, row.Team2RegistrationId);
        }
        return result;

        void Add(long matchId, int side, long? registrationId)
        {
            if (!registrationId.HasValue) return;
            if (snapshots.TryGetValue((matchId, side), out var snapshot))
                result[(matchId, side)] = snapshot;
            else if (current.TryGetValue(registrationId.Value, out var team))
                result[(matchId, side)] = team;
        }
    }

    private static bool IsComplete(HanakaServer.Models.RelayTeam team, int teamSize) =>
        team.Members.Count == teamSize
        && team.Members.OrderBy(x => x.Position).Select(x => x.Position)
            .SequenceEqual(Enumerable.Range(1, teamSize))
        && team.Members.All(x => !string.IsNullOrWhiteSpace(x.DisplayName));
}

public sealed record RelayMatchTeamReference(long MatchId, long? Team1RegistrationId, long? Team2RegistrationId);
