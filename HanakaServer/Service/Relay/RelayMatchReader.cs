using System.Data;
using HanakaServer.Data;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Services.Relay;

public sealed record RelayMatchReadResult(long TournamentId, DateTimeOffset ServerNowUtc, RelayMatchSnapshot Snapshot);

public sealed class RelayMatchReader(PickleballDbContext db, TimeProvider timeProvider)
{
    public async Task<RelayMatchReadResult?> ReadAsync(long matchId, CancellationToken ct = default)
    {
        // Same lock order as commands (parent match, then relay state/legs). A reader
        // must not combine a pre-command score with a post-command leg/version.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var match = await db.TournamentGroupMatches.AsNoTracking().Where(x => x.MatchId == matchId)
            .Select(x => new { x.TournamentId, x.Team1RegistrationId, x.Team2RegistrationId,
                x.ScoreTeam1, x.ScoreTeam2, x.WinnerRegistrationId, x.IsCompleted }).SingleOrDefaultAsync(ct);
        if (match == null) return null;
        var state = await db.RelayMatchStates.AsNoTracking().Include(x => x.Legs).SingleOrDefaultAsync(x => x.MatchId == matchId, ct);
        if (state == null) return null;
        if (state.TournamentId != match.TournamentId || state.Team1RegistrationId != match.Team1RegistrationId
            || state.Team2RegistrationId != match.Team2RegistrationId || match.IsCompleted != (state.Status == RelayStatuses.Completed))
            throw new RelayRuleException("MATCH_STATE_MISMATCH", "Trận chính không khớp dữ liệu tiếp sức; cần kiểm tra trước khi tiếp tục.");
        var legs = state.Legs.OrderBy(x => x.LegNumber).Select(x => new RelayLegSnapshot(x.LegNumber, x.PairNumber,
            x.StartedAtUtc, x.EndsAtUtc, x.FinishedAtUtc, x.StartScoreTeam1, x.StartScoreTeam2, x.EndScoreTeam1, x.EndScoreTeam2)).ToArray();
        if (state.CurrentLegNumber != legs.Length || legs.Where((leg, index) => leg.LegNumber != index + 1).Any())
            throw new RelayRuleException("LEG_SEQUENCE_INVALID", "Lịch sử lượt tiếp sức không đầy đủ.");
        var snapshot = new RelayMatchSnapshot(matchId, state.Team1RegistrationId, state.Team2RegistrationId,
            new(state.PairCount * 2, state.TargetScore, state.LegDurationSeconds), state.Status,
            match.ScoreTeam1, match.ScoreTeam2, match.WinnerRegistrationId, state.Version, state.UpdatedAtUtc, legs);
        var now = timeProvider.GetUtcNow();
        await tx.CommitAsync(ct);
        return new(match.TournamentId, now, snapshot);
    }
}
