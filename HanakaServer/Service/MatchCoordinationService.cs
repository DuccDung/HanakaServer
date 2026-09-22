using System.Data;
using HanakaServer.Data;
using HanakaServer.Helpers;
using HanakaServer.Models;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Services;

public sealed class CoordinationException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public sealed class CoordinateMatchRequest
{
    public long ExpectedVersion { get; set; }
    public string? CourtText { get; set; }
    public bool Preparing { get; set; }
}

public sealed class MatchCoordinationService(PickleballDbContext db, IConfiguration config,
    PublicRealtimeHub realtime, ILogger<MatchCoordinationService> logger)
{
    public bool Enabled => config.GetValue("Coordination:Enabled", true);

    public Task<bool> CanCoordinateAsync(long userId, long tournamentId, CancellationToken ct = default) => !Enabled
        ? Task.FromResult(false)
        : db.TournamentCoordinators.AsNoTracking().AnyAsync(a => a.UserId == userId && a.TournamentId == tournamentId
            && db.Users.Any(u => u.UserId == userId && u.IsActive)
            && db.Tournaments.Any(t => t.TournamentId == tournamentId && !t.Remove)
            && db.UserRoles.Any(r => r.UserId == userId && r.Role.RoleCode == RoleCodes.Coordinator), ct);

    public static object Snapshot(TournamentGroupMatch m) => new
    {
        m.MatchId, m.TournamentId, m.TournamentRoundGroupId, m.MatchStatus, m.StateVersion,
        m.CourtText, m.AddressText, m.ScoreTeam1, m.ScoreTeam2, m.IsCompleted,
        m.WinnerRegistrationId, m.UpdatedAt, m.VideoUrl,
        m.Team1RegistrationId, m.Team2RegistrationId, m.CompletionReason
    };

    public async Task<object> UpdateAsync(long userId, long matchId, CoordinateMatchRequest request, CancellationToken ct)
    {
        if (!Enabled) throw new CoordinationException(404, "COORDINATION_DISABLED", "Điều phối đang tạm tắt.");
        var court = request.CourtText?.Trim();
        if (request.ExpectedVersion < 1 || court?.Length > 100 || (request.Preparing && string.IsNullOrWhiteSpace(court)))
            throw new CoordinationException(400, "INVALID_COORDINATION", "Chọn sân trước khi gọi chuẩn bị; tên sân tối đa 100 ký tự.");

        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        // Same parent-row lock order as the scoring endpoints.
        if (db.Database.IsRelational())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT MatchId FROM dbo.TournamentGroupMatches WITH (UPDLOCK, HOLDLOCK) WHERE MatchId = {matchId}", ct);
        var match = await db.TournamentGroupMatches.SingleOrDefaultAsync(x => x.MatchId == matchId, ct)
            ?? throw new CoordinationException(404, "MATCH_NOT_FOUND", "Không tìm thấy trận đấu.");
        if (!await CanCoordinateAsync(userId, match.TournamentId, ct))
            throw new CoordinationException(403, "COORDINATION_FORBIDDEN", "Bạn không được phân công điều phối giải này.");
        if (match.StateVersion != request.ExpectedVersion)
            throw new CoordinationException(409, "MATCH_CHANGED", "Trận đã được cập nhật. Vui lòng tải lại trước khi lưu.");
        if (match.IsCompleted || !MatchStatuses.CanCoordinate(match.MatchStatus))
            throw new CoordinationException(409, "MATCH_STARTED", "Chỉ được điều phối trước khi trọng tài bắt đầu chấm.");
        if (request.Preparing && (!match.Team1RegistrationId.HasValue || !match.Team2RegistrationId.HasValue
            || match.CompletionReason == MatchCompletionReasons.Bye))
            throw new CoordinationException(400, "MATCH_NOT_READY", "Trận phải có đủ hai đội để gọi chuẩn bị.");
        var status = request.Preparing ? MatchStatuses.Preparing : MatchStatuses.NotStarted;
        court = string.IsNullOrWhiteSpace(court) ? null : court;
        if (match.CourtText != court || match.MatchStatus != status)
        {
            db.MatchCoordinationHistories.Add(new()
            {
                MatchId = matchId, ActorUserId = userId, PreviousCourt = match.CourtText, Court = court,
                PreviousStatus = match.MatchStatus, Status = status, CreatedAt = DateTime.UtcNow
            });
            match.CourtText = court;
            match.MatchStatus = status;
            await db.SaveChangesAsync(ct);
        }
        if (tx != null) await tx.CommitAsync(ct);
        var snapshot = Snapshot(match);
        // A disconnected client must not suppress notification of an already committed change.
        try { await realtime.BroadcastMatchCoordinationUpdatedAsync(match.TournamentId, match.MatchId, snapshot); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not broadcast coordination for match {MatchId}", matchId); }
        return snapshot;
    }
}
