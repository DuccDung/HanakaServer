using HanakaServer.Data;
using HanakaServer.Dtos.Relay;
using HanakaServer.Models;
using HanakaServer.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HanakaServer.Services.Relay;

public sealed class RelayScoringService(PickleballDbContext db, IOptions<RelayOptions> options)
{
    public Task<bool> IsRelayAsync(long tournamentId, CancellationToken ct = default) =>
        options.Value.AdminPreviewEnabled
            ? db.RelayTournamentSettings.AsNoTracking().AnyAsync(x => x.TournamentId == tournamentId, ct)
            : Task.FromResult(false);

    public async Task<Dictionary<long, RelayMatchScore>> ReadAsync(IEnumerable<long> matchIds, CancellationToken ct = default)
    {
        var ids = matchIds.Distinct().ToArray();
        if (!options.Value.AdminPreviewEnabled || ids.Length == 0) return [];
        return await db.RelayMatchScores.AsNoTracking().Where(x => ids.Contains(x.MatchId))
            .ToDictionaryAsync(x => x.MatchId, ct);
    }

    public static RelayScoreDto Map(RelayMatchScore? score, int total1, int total2) => score == null
        ? new(0, total1 != 0 || total2 != 0, [new(1, 0, 0), new(2, 0, 0), new(3, 0, 0)])
        : new(score.Version, false, [new(1, score.Part1Team1, score.Part1Team2),
            new(2, score.Part2Team1, score.Part2Team2), new(3, score.Part3Team1, score.Part3Team2)]);

    // Caller owns a transaction and locks the parent match before reading it. Score parts,
    // parent totals, lineup snapshots and history are committed together by that caller.
    public async Task<RelayScoreDto?> ApplyAsync(TournamentGroupMatch match, RelayScoreUpdate? update,
        bool completed, CancellationToken ct = default)
    {
        if (!await IsRelayAsync(match.TournamentId, ct))
        {
            if (update != null) throw new RelayRuleException("RELAY_NOT_CONFIGURED", "Trận này không thuộc giải tiếp sức.");
            return null;
        }
        if (update == null)
            throw new RelayRuleException("RELAY_PARTS_REQUIRED", "Trận tiếp sức cần chấm điểm theo 3 đội con. Vui lòng mở lại bảng chấm điểm tiếp sức.");
        if (update.ExpectedVersion < 0 || update.ExpectedVersion == long.MaxValue
            || update.ChangedPart is < 1 or > 3 || update.Parts == null || update.Parts.Count != 3
            || update.Parts.Any(x => x == null || x.ScoreTeam1 < 0 || x.ScoreTeam2 < 0)
            || !update.Parts.OrderBy(x => x.PartNumber).Select(x => x.PartNumber).SequenceEqual(new[] { 1, 2, 3 }))
            throw new RelayRuleException("RELAY_SCORES_INVALID", "Cần đủ 3 đội con với điểm nguyên không âm và phiên bản hợp lệ.");

        var total1 = update.Parts.Sum(x => (long)x.ScoreTeam1);
        var total2 = update.Parts.Sum(x => (long)x.ScoreTeam2);
        if (total1 > int.MaxValue || total2 > int.MaxValue)
            throw new RelayRuleException("RELAY_SCORES_INVALID", "Tổng điểm vượt giới hạn lưu trữ.");
        if (completed && total1 == total2)
            throw new RelayRuleException("RELAY_SCORE_TIED", "Tổng điểm hai đội đang hòa, chưa thể kết thúc trận.");

        var score = await db.RelayMatchScores.SingleOrDefaultAsync(x => x.MatchId == match.MatchId, ct);
        if ((score?.Version ?? 0) != update.ExpectedVersion)
            throw new RelayRuleException("VERSION_CONFLICT", "Điểm đã được cập nhật ở phiên khác. Vui lòng đồng bộ rồi chấm tiếp.");
        if (score == null && (match.ScoreTeam1 != 0 || match.ScoreTeam2 != 0))
        {
            if (!update.AllocateExisting || total1 != match.ScoreTeam1 || total2 != match.ScoreTeam2
                || completed != match.IsCompleted)
                throw new RelayRuleException("RELAY_ALLOCATION_REQUIRED",
                    $"Hãy phân bổ điểm 3 đội con khớp tỷ số đã lưu {match.ScoreTeam1}–{match.ScoreTeam2} trước khi chấm tiếp.");
        }
        var oldParts = Map(score, match.ScoreTeam1, match.ScoreTeam2).Parts;
        var changedParts = update.Parts.Where(p => oldParts[p.PartNumber - 1].ScoreTeam1 != p.ScoreTeam1
            || oldParts[p.PartNumber - 1].ScoreTeam2 != p.ScoreTeam2).ToArray();
        update.ChangedPart = changedParts.Length == 1 ? changedParts[0].PartNumber : null;
        if (score == null)
        {
            score = new RelayMatchScore { MatchId = match.MatchId };
            db.RelayMatchScores.Add(score);
        }
        var parts = update.Parts.OrderBy(x => x.PartNumber).ToArray();
        score.Part1Team1 = parts[0].ScoreTeam1; score.Part1Team2 = parts[0].ScoreTeam2;
        score.Part2Team1 = parts[1].ScoreTeam1; score.Part2Team2 = parts[1].ScoreTeam2;
        score.Part3Team1 = parts[2].ScoreTeam1; score.Part3Team2 = parts[2].ScoreTeam2;
        score.Version++;
        match.ScoreTeam1 = (int)total1;
        match.ScoreTeam2 = (int)total2;
        return Map(score, match.ScoreTeam1, match.ScoreTeam2);
    }
}
