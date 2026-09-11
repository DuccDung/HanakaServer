using System.Text.Json;
using HanakaServer.Data;
using HanakaServer.Dtos.Brackets;
using HanakaServer.Dtos.Relay;
using HanakaServer.Models;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Services.Relay;

// Extends the existing library/application workflow; template CRUD, validation and graph stay unchanged.
internal sealed class RelayBracketAdapter(PickleballDbContext db, bool enabled)
{
    public Task<RelayTournamentSettings?> GetSettingsAsync(long tournamentId, CancellationToken ct) => enabled
        ? db.RelayTournamentSettings.AsNoTracking().SingleOrDefaultAsync(x => x.TournamentId == tournamentId, ct)
        : Task.FromResult<RelayTournamentSettings?>(null);

    public IQueryable<TournamentRegistration> FilterEligible(IQueryable<TournamentRegistration> query, RelayTournamentSettings settings) =>
        query.Where(registration => db.RelayTeams.Any(team => team.RegistrationId == registration.RegistrationId
            && team.TournamentId == settings.TournamentId
            && team.TeamName.Trim() != "" && team.Members.Count == settings.TeamSize
            && team.Members.All(member => member.Position >= 1 && member.Position <= settings.TeamSize && member.DisplayName.Trim() != "")));

    public async Task EnrichAsync(long tournamentId, IReadOnlyList<TournamentBracketSeedDto> seeds, CancellationToken ct)
    {
        if (!enabled || !seeds.Any()) return;
        var ids = seeds.Where(x => x.RegistrationId.HasValue && !x.IsBye && !x.IsVirtualTeam)
            .Select(x => x.RegistrationId!.Value).ToArray();
        if (ids.Length == 0) return;
        var teams = await db.RelayTeams.AsNoTracking().Include(x => x.Members)
            .Where(x => x.TournamentId == tournamentId && ids.Contains(x.RegistrationId))
            .ToDictionaryAsync(x => x.RegistrationId, ct);
        foreach (var seed in seeds)
        {
            if (!teams.TryGetValue(seed.RegistrationId ?? 0, out var team)) continue;
            seed.TeamName = team.TeamName;
            seed.Relay = new(team.Members.Count, team.Version, team.LineupLockedAtUtc,
                team.Members.OrderBy(x => x.Position)
                    .Select(x => new RelayBracketMemberDto(x.Position, x.UserId, x.DisplayName, x.AvatarUrl)).ToArray());
        }
    }

    public void AddSnapshots(long applicationId, IEnumerable<TournamentBracketSeedDto> seeds)
    {
        foreach (var seed in seeds.Where(x => x.Relay != null))
            db.RelayBracketSeedSnapshots.Add(new()
            {
                TournamentBracketApplicationId = applicationId, SeedNumber = seed.SeedNumber,
                TeamName = seed.TeamName, LineupJson = JsonSerializer.Serialize(seed.Relay)
            });
    }

    public async Task RestoreSnapshotsAsync(long applicationId, IEnumerable<TournamentBracketSeedDto> seeds, CancellationToken ct)
    {
        if (!enabled) return;
        var snapshots = await db.RelayBracketSeedSnapshots.AsNoTracking()
            .Where(x => x.TournamentBracketApplicationId == applicationId).ToDictionaryAsync(x => x.SeedNumber, ct);
        foreach (var seed in seeds)
        {
            if (!snapshots.TryGetValue(seed.SeedNumber, out var snapshot)) continue;
            seed.TeamName = snapshot.TeamName;
            seed.Relay = JsonSerializer.Deserialize<RelayBracketTeamDto>(snapshot.LineupJson);
        }
    }

    public static string HashInput(TournamentBracketSeedDto seed) => seed.Relay == null ? ""
        : JsonSerializer.Serialize(new { seed.SeedNumber, seed.RegistrationId, seed.TeamName, seed.Relay });
}
