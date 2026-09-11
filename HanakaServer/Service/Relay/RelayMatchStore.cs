using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HanakaServer.Data;
using HanakaServer.Models;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Services.Relay;

public sealed record RelayCommandRequest(Guid RequestId, long ExpectedVersion, string Operation, int? Side = null);
public sealed record RelayCommandResult(RelayMatchSnapshot Snapshot, bool Replayed);

// Transactional persistence foundation, intentionally not exposed through controllers until
// legacy write guards, realtime, propagation and public contracts have been integrated.
public sealed class RelayMatchStore(PickleballDbContext db, TimeProvider timeProvider)
{
    public async Task<RelayCommandResult> ExecuteAsync(long matchId, long actorUserId, bool actorIsAdmin,
        RelayCommandRequest request, CancellationToken ct = default)
    {
        ValidateRequest(request);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        // Serialize relay commands for a match across processes; do not use an in-memory mutex.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT MatchId FROM dbo.TournamentGroupMatches WITH (UPDLOCK, HOLDLOCK) WHERE MatchId = {matchId}", ct);
        var match = await db.TournamentGroupMatches.Where(x => x.MatchId == matchId)
            .Select(x => new
            {
                x.TournamentId, x.Team1RegistrationId, x.Team2RegistrationId, x.RefereeUserId,
                x.ScoreTeam1, x.ScoreTeam2, x.IsCompleted, x.WinnerRegistrationId, x.CompletionReason
            }).SingleOrDefaultAsync(ct)
            ?? throw new RelayRuleException("MATCH_NOT_FOUND", "Không tìm thấy trận đấu.");
        if (actorUserId <= 0 || (!actorIsAdmin && match.RefereeUserId != actorUserId))
            throw new RelayRuleException("FORBIDDEN", "Bạn không được điều khiển trận đấu này.");

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { actorUserId, request }))));
        var previous = await db.RelayMatchCommands.AsNoTracking()
            .SingleOrDefaultAsync(x => x.MatchId == matchId && x.RequestId == request.RequestId, ct);
        if (previous != null)
        {
            if (previous.RequestHash != hash)
                throw new RelayRuleException("REQUEST_ID_REUSED", "Mã thao tác đã được dùng cho nội dung khác.");
            var replay = JsonSerializer.Deserialize<RelayMatchSnapshot>(previous.ResultJson)!;
            await tx.CommitAsync(ct);
            return new(replay, true);
        }

        if (!match.Team1RegistrationId.HasValue || !match.Team2RegistrationId.HasValue || match.CompletionReason == "BYE")
            throw new RelayRuleException("MATCH_TEAMS_INVALID", "Trận chưa đủ hai đội thật hoặc là trận miễn đấu.");
        var stored = await db.RelayMatchStates.Include(x => x.Legs).SingleOrDefaultAsync(x => x.MatchId == matchId, ct);
        if ((stored?.Version ?? 0) != request.ExpectedVersion)
            throw new RelayRuleException("VERSION_CONFLICT", "Trận đã thay đổi; tải lại trạng thái mới.");
        if (stored != null && (stored.Team1RegistrationId != match.Team1RegistrationId
                              || stored.Team2RegistrationId != match.Team2RegistrationId || stored.TournamentId != match.TournamentId))
            throw new RelayRuleException("MATCH_TEAMS_CHANGED", "Đội trong trận không khớp dữ liệu tiếp sức đã lưu.");

        RelayMatchSnapshot snapshot;
        if (stored == null)
        {
            if (request.Operation != "START" || match.IsCompleted || match.ScoreTeam1 != 0 || match.ScoreTeam2 != 0 || match.WinnerRegistrationId.HasValue)
                throw new RelayRuleException("MATCH_NOT_READY", "Không thể khởi tạo tiếp sức cho trận đã có kết quả/điểm.");
            var settings = await db.RelayTournamentSettings.AsNoTracking().SingleOrDefaultAsync(x => x.TournamentId == match.TournamentId, ct);
            if (settings?.IsEnabled != true)
                throw new RelayRuleException("RELAY_DISABLED", "Giải chưa được bật thể thức tiếp sức.");
            var ids = new[] { match.Team1RegistrationId.Value, match.Team2RegistrationId.Value };
            var teams = await db.RelayTeams.Include(x => x.Members).Where(x => ids.Contains(x.RegistrationId)).ToListAsync(ct);
            if (teams.Count != 2 || teams.Any(x => x.TournamentId != match.TournamentId)
                || await db.TournamentRegistrations.AnyAsync(x => ids.Contains(x.RegistrationId) && x.IsVirtualTeam, ct))
                throw new RelayRuleException("LINEUP_NOT_READY", "Hai đội phải thuộc giải, là đội thật và có đội hình đầy đủ.");
            foreach (var team in teams)
                RelayLineupService.ValidateMembers(team.Members.Select(x => new RelayMemberInput(x.Position, x.UserId, x.DisplayName)).ToList(), settings.TeamSize, true);
            await new RelayMatchLineupSnapshotService(db, timeProvider).EnsureForMatchAsync(new TournamentGroupMatch
            {
                MatchId = matchId,
                TournamentId = match.TournamentId,
                Team1RegistrationId = ids[0],
                Team2RegistrationId = ids[1]
            }, ct);
            var rules = new RelayRules(settings.TeamSize, settings.TargetScore, settings.LegDurationSeconds);
            snapshot = RelayMatchEngine.Create(matchId, ids[0], ids[1], rules);
            stored = new RelayMatchState
            {
                MatchId = matchId, TournamentId = match.TournamentId, Team1RegistrationId = ids[0], Team2RegistrationId = ids[1],
                PairCount = rules.PairCount, TargetScore = rules.TargetScore, LegDurationSeconds = rules.LegDurationSeconds, DeadlinePolicy = null
            };
            db.RelayMatchStates.Add(stored);
        }
        else
        {
            if (match.IsCompleted != (stored.Status == RelayStatuses.Completed))
                throw new RelayRuleException("MATCH_STATE_MISMATCH", "Kết quả trận chính không khớp trạng thái tiếp sức.");
            snapshot = new(matchId, stored.Team1RegistrationId, stored.Team2RegistrationId,
                new(stored.PairCount * 2, stored.TargetScore, stored.LegDurationSeconds),
                stored.Status, match.ScoreTeam1, match.ScoreTeam2, match.WinnerRegistrationId, stored.Version, stored.UpdatedAtUtc,
                stored.Legs.OrderBy(x => x.LegNumber).Select(x => new RelayLegSnapshot(x.LegNumber, x.PairNumber,
                    x.StartedAtUtc, x.EndsAtUtc, x.FinishedAtUtc, x.StartScoreTeam1, x.StartScoreTeam2, x.EndScoreTeam1, x.EndScoreTeam2)).ToArray());
        }

        var now = timeProvider.GetUtcNow();
        var next = request.Operation switch
        {
            "START" => RelayMatchEngine.Start(snapshot, now),
            "AWARD_POINT" => RelayMatchEngine.AwardPoint(snapshot, request.Side!.Value, now),
            "FINISH_LEG" => RelayMatchEngine.FinishLeg(snapshot, now),
            "NEXT_LEG" => RelayMatchEngine.StartNextLeg(snapshot, now),
            _ => throw new RelayRuleException("OPERATION_INVALID", "Thao tác không hợp lệ.")
        };
        stored.Status = next.Status;
        stored.Version = next.Version;
        stored.CurrentLegNumber = next.LegNumber;
        stored.UpdatedAtUtc = next.UpdatedAtUtc;
        foreach (var leg in next.Legs)
        {
            var entity = stored.Legs.SingleOrDefault(x => x.LegNumber == leg.LegNumber);
            if (entity == null)
            {
                entity = new RelayLeg { MatchId = matchId, LegNumber = leg.LegNumber };
                stored.Legs.Add(entity);
            }
            entity.PairNumber = leg.PairNumber;
            entity.StartedAtUtc = leg.StartedAtUtc;
            entity.EndsAtUtc = leg.EndsAtUtc;
            entity.FinishedAtUtc = leg.FinishedAtUtc;
            entity.StartScoreTeam1 = leg.StartScoreTeam1;
            entity.StartScoreTeam2 = leg.StartScoreTeam2;
            entity.EndScoreTeam1 = leg.EndScoreTeam1;
            entity.EndScoreTeam2 = leg.EndScoreTeam2;
        }
        await db.TournamentGroupMatches.Where(x => x.MatchId == matchId).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.ScoreTeam1, next.ScoreTeam1).SetProperty(x => x.ScoreTeam2, next.ScoreTeam2)
            .SetProperty(x => x.IsCompleted, next.Status == RelayStatuses.Completed)
            .SetProperty(x => x.WinnerRegistrationId, next.WinnerRegistrationId)
            .SetProperty(x => x.CompletionReason, next.Status == RelayStatuses.Completed ? "NORMAL" : null)
            .SetProperty(x => x.UpdatedAt, now.UtcDateTime), ct);
        if (next.ScoreTeam1 != match.ScoreTeam1 || next.ScoreTeam2 != match.ScoreTeam2)
            db.TournamentMatchScoreHistories.Add(new TournamentMatchScoreHistory
            {
                MatchId = matchId, RefereeUserId = actorUserId, ScoreTeam1 = next.ScoreTeam1, ScoreTeam2 = next.ScoreTeam2,
                IsCompleted = next.Status == RelayStatuses.Completed, WinnerRegistrationId = next.WinnerRegistrationId,
                CreatedAt = now.UtcDateTime, Note = $"RELAY:{request.Operation}:LEG:{next.LegNumber}"
            });
        db.RelayMatchCommands.Add(new RelayMatchCommand
        {
            MatchId = matchId, RequestId = request.RequestId, ActorUserId = actorUserId, RequestHash = hash,
            Operation = request.Operation, ResultVersion = next.Version, CreatedAtUtc = now, ResultJson = JsonSerializer.Serialize(next)
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(next, false);
    }

    private static void ValidateRequest(RelayCommandRequest request)
    {
        if (request.RequestId == Guid.Empty || request.ExpectedVersion < 0
            || request.Operation is not ("START" or "AWARD_POINT" or "FINISH_LEG" or "NEXT_LEG")
            || (request.Operation == "AWARD_POINT" && !request.Side.HasValue)
            || (request.Side.HasValue && request.Side is not (1 or 2))
            || (request.Side.HasValue && request.Operation is "START" or "FINISH_LEG" or "NEXT_LEG"))
            throw new RelayRuleException("COMMAND_INVALID", "Mã yêu cầu, phiên bản hoặc nội dung thao tác không hợp lệ.");
    }
}
