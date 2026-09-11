using HanakaServer.Data;
using HanakaServer.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HanakaServer.Services.Relay;

public sealed class RelayLegacyWriteGuard(PickleballDbContext db, IOptions<RelayOptions> options)
{
    public bool Enabled => options.Value.AdminPreviewEnabled;

    // Caller owns the transaction. Use the same parent-first lock order as RelayMatchStore.
    public Task LockMatchAsync(long matchId, CancellationToken ct = default) => Enabled
        ? db.Database.ExecuteSqlInterpolatedAsync($"SELECT MatchId FROM dbo.TournamentGroupMatches WITH (UPDLOCK, HOLDLOCK) WHERE MatchId = {matchId}", ct)
        : Task.CompletedTask;

    public Task<bool> IsRelayTournamentAsync(long tournamentId, CancellationToken ct = default) => options.Value.AdminPreviewEnabled
        ? db.RelayTournamentSettings.AsNoTracking().AnyAsync(x => x.TournamentId == tournamentId, ct)
        : Task.FromResult(false);

    public Task<bool> HasMatchStateAsync(long matchId, CancellationToken ct = default) => options.Value.AdminPreviewEnabled
        ? db.RelayMatchStates.AsNoTracking().AnyAsync(x => x.MatchId == matchId, ct)
        : Task.FromResult(false);

    public Task<bool> HasTeamAsync(long registrationId, CancellationToken ct = default) => Enabled
        ? db.RelayTeams.AsNoTracking().AnyAsync(x => x.RegistrationId == registrationId, ct)
        : Task.FromResult(false);
}
