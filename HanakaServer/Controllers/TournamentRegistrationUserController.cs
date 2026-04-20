using System.Data;
using System.Security.Claims;
using System.Text.Json;
using HanakaServer.Data;
using HanakaServer.Models;
using HanakaServer.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers;

[ApiController]
[Route("api/tournament-registrations")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class TournamentRegistrationUserController : ControllerBase
{
    private const int PairRequestExpiryHours = 48;

    private readonly PickleballDbContext _db;
    private readonly IConfiguration _config;
    private readonly RealtimeHub _realtimeHub;

    public TournamentRegistrationUserController(
        PickleballDbContext db,
        IConfiguration config,
        RealtimeHub realtimeHub)
    {
        _db = db;
        _config = config;
        _realtimeHub = realtimeHub;
    }

    [HttpGet("tournaments/{tournamentId:long}/me")]
    public async Task<IActionResult> GetMyTournamentRegistrationState(long tournamentId, CancellationToken ct)
    {
        var userId = GetUserIdFromToken();
        await ExpirePendingPairRequestsAsync(tournamentId, ct);

        var tournament = await _db.Tournaments
            .AsNoTracking()
            .Where(x => x.TournamentId == tournamentId && x.Status != "DRAFT")
            .Select(x => new
            {
                x.TournamentId,
                x.Title,
                x.Status,
                x.StatusText,
                x.GameType,
                x.ExpectedTeams,
                x.SingleLimit,
                x.DoubleLimit,
                x.RegisterDeadline,
                x.StartTime
            })
            .FirstOrDefaultAsync(ct);

        if (tournament == null)
            return NotFound(new { message = "Tournament not found." });

        var me = await LoadUserSnapshotAsync(userId, ct);
        if (me == null)
            return NotFound(new { message = "User not found." });

        var registrationRow = await _db.TournamentRegistrations
            .AsNoTracking()
            .Where(x =>
                x.TournamentId == tournamentId &&
                (x.Player1UserId == userId || x.Player2UserId == userId))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.RegistrationId,
                x.RegIndex,
                x.RegCode,
                x.RegTime,
                x.WaitingPair,
                x.Success,
                x.Points,
                x.Player1UserId,
                x.Player1Name,
                x.Player1Avatar,
                x.Player1Level,
                x.Player2UserId,
                x.Player2Name,
                x.Player2Avatar,
                x.Player2Level
            })
            .FirstOrDefaultAsync(ct);

        var registration = registrationRow == null
            ? null
            : new
            {
                registrationRow.RegistrationId,
                registrationRow.RegIndex,
                registrationRow.RegCode,
                registrationRow.RegTime,
                registrationRow.WaitingPair,
                registrationRow.Success,
                registrationRow.Points,
                registrationRow.Player1UserId,
                registrationRow.Player1Name,
                Player1Avatar = ToAbsoluteUrl(registrationRow.Player1Avatar),
                registrationRow.Player1Level,
                registrationRow.Player2UserId,
                registrationRow.Player2Name,
                Player2Avatar = ToAbsoluteUrl(registrationRow.Player2Avatar),
                registrationRow.Player2Level
            };

        var pendingSent = await LoadPairRequestsAsync(tournamentId, userId, sent: true, ct);
        var pendingReceived = await LoadPairRequestsAsync(tournamentId, userId, sent: false, ct);

        var successCount = await _db.TournamentRegistrations
            .AsNoTracking()
            .CountAsync(x => x.TournamentId == tournamentId && x.Success, ct);

        var gameType = NormalizeGameType(tournament.GameType);
        var capacityLeft = Math.Max(0, tournament.ExpectedTeams - successCount);
        var canRegisterResult = ValidateTournamentRegistrationWindow(
            tournament.Status,
            tournament.RegisterDeadline);

        var hasPending = pendingSent.Count > 0 || pendingReceived.Count > 0;
        var canRegister =
            registration == null &&
            !hasPending &&
            canRegisterResult.CanRegister &&
            (IsDoubleLike(gameType) || capacityLeft > 0);
        var reason = registration != null
            ? "Bạn đã có đăng ký trong giải này."
            : hasPending
                ? "Bạn đang có lời mời ghép đôi chờ xử lý."
                : !canRegisterResult.CanRegister
                    ? canRegisterResult.Reason
                    : capacityLeft <= 0 && !IsDoubleLike(gameType)
                        ? "Giải đã đủ số đội đăng ký."
                        : "";

        return Ok(new
        {
            tournament = new
            {
                tournament.TournamentId,
                tournament.Title,
                tournament.Status,
                tournament.StatusText,
                GameType = gameType,
                tournament.ExpectedTeams,
                tournament.SingleLimit,
                tournament.DoubleLimit,
                tournament.RegisterDeadline,
                tournament.StartTime,
                CapacityLeft = capacityLeft
            },
            me,
            existingRegistration = registration,
            pendingSent,
            pendingReceived,
            canRegister,
            reason
        });
    }

    [HttpGet("tournaments/{tournamentId:long}/partner-search")]
    public async Task<IActionResult> SearchPartner(long tournamentId, [FromQuery] string? query, [FromQuery] int pageSize = 10, CancellationToken ct = default)
    {
        var userId = GetUserIdFromToken();
        query = (query ?? string.Empty).Trim();
        pageSize = Math.Clamp(pageSize, 1, 20);

        if (query.Length < 2)
            return Ok(new { items = Array.Empty<object>() });

        var tournamentExists = await _db.Tournaments
            .AsNoTracking()
            .AnyAsync(x => x.TournamentId == tournamentId && x.Status != "DRAFT", ct);

        if (!tournamentExists)
            return NotFound(new { message = "Tournament not found." });

        var q = _db.Users
            .AsNoTracking()
            .Where(x => x.IsActive && x.UserId != userId)
            .Where(x =>
                x.FullName.Contains(query) ||
                (x.Phone != null && x.Phone.Contains(query)) ||
                (x.Email != null && x.Email.Contains(query)) ||
                x.UserId.ToString().Contains(query));

        var rows = await q
            .OrderByDescending(x => x.Verified)
            .ThenBy(x => x.FullName)
            .Take(pageSize)
            .Select(x => new
            {
                x.UserId,
                x.FullName,
                x.AvatarUrl,
                x.City,
                x.Verified,
                LatestRating = _db.UserRatingHistories
                    .Where(r => r.UserId == x.UserId)
                    .OrderByDescending(r => r.RatedAt)
                    .ThenByDescending(r => r.RatingHistoryId)
                    .Select(r => new { r.RatingSingle, r.RatingDouble })
                    .FirstOrDefault(),
                IsRegistered = _db.TournamentRegistrations.Any(r =>
                    r.TournamentId == tournamentId &&
                    (r.Player1UserId == x.UserId || r.Player2UserId == x.UserId)),
                IsBlocked = _db.UserBlocks.Any(b =>
                    b.IsActive &&
                    ((b.BlockerUserId == userId && b.BlockedUserId == x.UserId) ||
                     (b.BlockerUserId == x.UserId && b.BlockedUserId == userId)))
            })
            .ToListAsync(ct);

        var items = rows.Select(x => new
        {
            x.UserId,
            x.FullName,
            AvatarUrl = ToAbsoluteUrl(x.AvatarUrl),
            x.City,
            x.Verified,
            RatingSingle = x.LatestRating?.RatingSingle ?? 0m,
            RatingDouble = x.LatestRating?.RatingDouble ?? 0m,
            x.IsRegistered,
            x.IsBlocked,
            CanInvite = !x.IsRegistered && !x.IsBlocked
        });

        return Ok(new { items });
    }

    [HttpPost("tournaments/{tournamentId:long}/single")]
    public async Task<IActionResult> RegisterSingle(long tournamentId, CancellationToken ct)
    {
        var userId = GetUserIdFromToken();

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        try
        {
            var tournament = await LoadTournamentForUpdateAsync(tournamentId, ct);
            if (tournament == null)
                return NotFound(new { message = "Tournament not found." });

            var gameType = NormalizeGameType(tournament.GameType);
            if (gameType != "SINGLE")
                return BadRequest(new { message = "Giải này không phải giải đơn." });

            var validation = await ValidateUserCanCreateRegistrationAsync(tournament, userId, null, requireCapacity: true, ct);
            if (!validation.Ok)
                return BadRequest(new { message = validation.Message });

            var player = await LoadUserSnapshotAsync(userId, ct);
            if (player == null)
                return NotFound(new { message = "User not found." });

            if (tournament.SingleLimit > 0 && player.RatingSingle > tournament.SingleLimit)
                return BadRequest(new { message = "Điểm trình đơn vượt giới hạn của giải." });

            var reg = await BuildRegistrationAsync(tournament, gameType, player, null, waitingPair: false, success: true, ct);
            _db.TournamentRegistrations.Add(reg);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Ok(new
            {
                ok = true,
                message = "Đăng ký giải đơn thành công.",
                registrationId = reg.RegistrationId
            });
        }
        catch (DbUpdateException ex)
        {
            await tx.RollbackAsync(ct);
            return StatusCode(500, new { message = "Không thể tạo đăng ký.", detail = ex.Message });
        }
    }

    [HttpPost("tournaments/{tournamentId:long}/waiting")]
    public async Task<IActionResult> RegisterWaitingPair(long tournamentId, CancellationToken ct)
    {
        var userId = GetUserIdFromToken();

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        try
        {
            var tournament = await LoadTournamentForUpdateAsync(tournamentId, ct);
            if (tournament == null)
                return NotFound(new { message = "Tournament not found." });

            var gameType = NormalizeGameType(tournament.GameType);
            if (!IsDoubleLike(gameType))
                return BadRequest(new { message = "Chờ ghép chỉ áp dụng cho giải đôi." });

            var validation = await ValidateUserCanCreateRegistrationAsync(tournament, userId, null, requireCapacity: false, ct);
            if (!validation.Ok)
                return BadRequest(new { message = validation.Message });

            var player = await LoadUserSnapshotAsync(userId, ct);
            if (player == null)
                return NotFound(new { message = "User not found." });

            if (tournament.DoubleLimit > 0 && player.RatingDouble > tournament.DoubleLimit)
                return BadRequest(new { message = "Điểm trình đôi vượt giới hạn của giải." });

            var reg = await BuildRegistrationAsync(tournament, gameType, player, null, waitingPair: true, success: false, ct);
            _db.TournamentRegistrations.Add(reg);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Ok(new
            {
                ok = true,
                message = "Đã đăng ký chờ ghép.",
                registrationId = reg.RegistrationId
            });
        }
        catch (DbUpdateException ex)
        {
            await tx.RollbackAsync(ct);
            return StatusCode(500, new { message = "Không thể tạo đăng ký chờ ghép.", detail = ex.Message });
        }
    }

    [HttpPost("tournaments/{tournamentId:long}/pair-requests")]
    public async Task<IActionResult> CreatePairRequest(long tournamentId, [FromBody] CreatePairRequestDto? request, CancellationToken ct)
    {
        var requestedByUserId = GetUserIdFromToken();
        var requestedToUserId = request?.RequestedToUserId ?? 0;

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        try
        {
            var tournament = await LoadTournamentForUpdateAsync(tournamentId, ct);
            if (tournament == null)
                return NotFound(new { message = "Tournament not found." });

            var gameType = NormalizeGameType(tournament.GameType);
            if (!IsDoubleLike(gameType))
                return BadRequest(new { message = "Ghép cặp chỉ áp dụng cho giải đôi." });

            if (requestedByUserId == requestedToUserId)
                return BadRequest(new { message = "Bạn không thể tự ghép cặp với chính mình." });

            var partner = await LoadUserSnapshotAsync(requestedToUserId, ct);
            if (partner == null)
                return NotFound(new { message = "Người được mời không tồn tại." });

            var validation = await ValidateUserCanCreateRegistrationAsync(tournament, requestedByUserId, requestedToUserId, requireCapacity: true, ct);
            if (!validation.Ok)
                return BadRequest(new { message = validation.Message });

            if (await IsBlockedBetweenAsync(requestedByUserId, requestedToUserId, ct))
                return BadRequest(new { message = "Không thể gửi lời mời vì hai tài khoản đang chặn nhau." });

            var requester = await LoadUserSnapshotAsync(requestedByUserId, ct);
            if (requester == null)
                return NotFound(new { message = "User not found." });

            var totalPoints = requester.RatingDouble + partner.RatingDouble;
            if (tournament.DoubleLimit > 0 && totalPoints > tournament.DoubleLimit)
                return BadRequest(new { message = "Tổng điểm trình đôi vượt giới hạn của giải." });

            var hasPendingBetween = await _db.TournamentPairRequests.AnyAsync(x =>
                x.TournamentId == tournamentId &&
                x.Status == "PENDING" &&
                ((x.RequestedByUserId == requestedByUserId && x.RequestedToUserId == requestedToUserId) ||
                 (x.RequestedByUserId == requestedToUserId && x.RequestedToUserId == requestedByUserId)), ct);

            if (hasPendingBetween)
                return BadRequest(new { message = "Hai người đang có lời mời ghép đôi chờ xử lý." });

            var now = DateTime.UtcNow;
            var pairRequest = new TournamentPairRequest
            {
                TournamentId = tournamentId,
                RequestedByUserId = requestedByUserId,
                RequestedToUserId = requestedToUserId,
                Status = "PENDING",
                RequestedAt = now,
                ExpiresAt = now.AddHours(PairRequestExpiryHours)
            };

            _db.TournamentPairRequests.Add(pairRequest);
            await _db.SaveChangesAsync(ct);

            var notification = BuildNotification(
                requestedToUserId,
                "PAIR_REQUEST",
                "Lời mời ghép đôi",
                $"{requester.FullName} mời bạn ghép cặp tại giải {tournament.Title}.",
                "PAIR_REQUEST",
                pairRequest.PairRequestId,
                new
                {
                    pairRequest.PairRequestId,
                    tournament.TournamentId,
                    pairRequest.ExpiresAt,
                    tournament.Title,
                    requestedBy = ToUserBrief(requester),
                    requestedTo = ToUserBrief(partner)
                });

            _db.UserNotifications.Add(notification);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await SendTournamentNotificationAsync(requestedToUserId, notification, tournamentId, pairRequest.PairRequestId, ct);

            return Ok(new
            {
                ok = true,
                message = "Đã gửi lời mời ghép đôi.",
                pairRequestId = pairRequest.PairRequestId
            });
        }
        catch (DbUpdateException ex)
        {
            await tx.RollbackAsync(ct);
            return StatusCode(500, new { message = "Không thể gửi lời mời ghép đôi.", detail = ex.Message });
        }
    }

    [HttpPost("pair-requests/{pairRequestId:long}/accept")]
    public async Task<IActionResult> AcceptPairRequest(long pairRequestId, CancellationToken ct)
    {
        var userId = GetUserIdFromToken();

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        try
        {
            var pairRequest = await _db.TournamentPairRequests
                .FirstOrDefaultAsync(x =>
                    x.PairRequestId == pairRequestId &&
                    x.RequestedToUserId == userId &&
                    x.Status == "PENDING", ct);

            if (pairRequest == null)
                return NotFound(new { message = "Lời mời không tồn tại hoặc đã được xử lý." });

            var now = DateTime.UtcNow;
            if (pairRequest.ExpiresAt.HasValue && pairRequest.ExpiresAt.Value <= now)
            {
                pairRequest.Status = "EXPIRED";
                pairRequest.RespondedAt = now;
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return BadRequest(new { message = "Lời mời đã hết hạn." });
            }

            var tournament = await LoadTournamentForUpdateAsync(pairRequest.TournamentId, ct);
            if (tournament == null)
                return NotFound(new { message = "Tournament not found." });

            var gameType = NormalizeGameType(tournament.GameType);
            if (!IsDoubleLike(gameType))
                return BadRequest(new { message = "Giải này không phải giải đôi." });

            var validation = await ValidateUserCanCreateRegistrationAsync(
                tournament,
                pairRequest.RequestedByUserId,
                pairRequest.RequestedToUserId,
                requireCapacity: true,
                ct);

            if (!validation.Ok)
                return BadRequest(new { message = validation.Message });

            if (await IsBlockedBetweenAsync(pairRequest.RequestedByUserId, pairRequest.RequestedToUserId, ct))
                return BadRequest(new { message = "Không thể chấp nhận vì hai tài khoản đang chặn nhau." });

            var requester = await LoadUserSnapshotAsync(pairRequest.RequestedByUserId, ct);
            var partner = await LoadUserSnapshotAsync(pairRequest.RequestedToUserId, ct);
            if (requester == null || partner == null)
                return NotFound(new { message = "Không tìm thấy người chơi." });

            var totalPoints = requester.RatingDouble + partner.RatingDouble;
            if (tournament.DoubleLimit > 0 && totalPoints > tournament.DoubleLimit)
                return BadRequest(new { message = "Tổng điểm trình đôi vượt giới hạn của giải." });

            var reg = await BuildRegistrationAsync(tournament, gameType, requester, partner, waitingPair: false, success: true, ct);
            _db.TournamentRegistrations.Add(reg);
            await _db.SaveChangesAsync(ct);

            pairRequest.Status = "ACCEPTED";
            pairRequest.RespondedAt = now;
            pairRequest.RegistrationId = reg.RegistrationId;

            var notification = BuildNotification(
                pairRequest.RequestedByUserId,
                "PAIR_ACCEPTED",
                "Ghép cặp thành công",
                $"{partner.FullName} đã đồng ý ghép cặp tại giải {tournament.Title}.",
                "PAIR_REQUEST",
                pairRequest.PairRequestId,
                new
                {
                    pairRequest.PairRequestId,
                    tournament.TournamentId,
                    tournament.Title,
                    registrationId = reg.RegistrationId,
                    acceptedBy = ToUserBrief(partner)
                });

            _db.UserNotifications.Add(notification);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            await SendTournamentNotificationAsync(pairRequest.RequestedByUserId, notification, tournament.TournamentId, pairRequest.PairRequestId, ct);

            return Ok(new
            {
                ok = true,
                message = "Đã chấp nhận lời mời và tạo đăng ký chính thức.",
                registrationId = reg.RegistrationId
            });
        }
        catch (DbUpdateException ex)
        {
            await tx.RollbackAsync(ct);
            return StatusCode(500, new { message = "Không thể chấp nhận lời mời.", detail = ex.Message });
        }
    }

    [HttpPost("pair-requests/{pairRequestId:long}/reject")]
    public async Task<IActionResult> RejectPairRequest(long pairRequestId, [FromBody] PairRequestResponseDto? request, CancellationToken ct)
    {
        var userId = GetUserIdFromToken();
        return await RespondPairRequestAsync(pairRequestId, userId, "REJECTED", request?.ResponseNote, ct);
    }

    [HttpPost("pair-requests/{pairRequestId:long}/cancel")]
    public async Task<IActionResult> CancelPairRequest(long pairRequestId, CancellationToken ct)
    {
        var userId = GetUserIdFromToken();

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var pairRequest = await _db.TournamentPairRequests
            .FirstOrDefaultAsync(x =>
                x.PairRequestId == pairRequestId &&
                x.RequestedByUserId == userId &&
                x.Status == "PENDING", ct);

        if (pairRequest == null)
            return NotFound(new { message = "Lời mời không tồn tại hoặc đã được xử lý." });

        pairRequest.Status = "CANCELED";
        pairRequest.RespondedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Ok(new { ok = true, message = "Đã hủy lời mời ghép đôi." });
    }

    private async Task<IActionResult> RespondPairRequestAsync(long pairRequestId, long userId, string status, string? note, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var pairRequest = await _db.TournamentPairRequests
            .Include(x => x.Tournament)
            .Include(x => x.RequestedToUser)
            .FirstOrDefaultAsync(x =>
                x.PairRequestId == pairRequestId &&
                x.RequestedToUserId == userId &&
                x.Status == "PENDING", ct);

        if (pairRequest == null)
            return NotFound(new { message = "Lời mời không tồn tại hoặc đã được xử lý." });

        pairRequest.Status = status;
        pairRequest.RespondedAt = DateTime.UtcNow;
        pairRequest.ResponseNote = Clean(note, 500);

        var notificationType = status == "REJECTED" ? "PAIR_REJECTED" : status;
        var notification = BuildNotification(
            pairRequest.RequestedByUserId,
            notificationType,
            "Lời mời ghép đôi đã bị từ chối",
            $"{pairRequest.RequestedToUser.FullName} đã từ chối ghép cặp tại giải {pairRequest.Tournament.Title}.",
            "PAIR_REQUEST",
            pairRequest.PairRequestId,
            new
            {
                pairRequest.PairRequestId,
                pairRequest.TournamentId,
                pairRequest.Tournament.Title,
                responseNote = pairRequest.ResponseNote,
                requestedTo = ToUserBrief(new UserRegistrationSnapshot
                {
                    UserId = pairRequest.RequestedToUser.UserId,
                    FullName = pairRequest.RequestedToUser.FullName,
                    AvatarUrl = pairRequest.RequestedToUser.AvatarUrl,
                    AvatarUrlAbsolute = ToAbsoluteUrl(pairRequest.RequestedToUser.AvatarUrl),
                    Verified = pairRequest.RequestedToUser.Verified,
                    RatingSingle = pairRequest.RequestedToUser.RatingSingle ?? 0m,
                    RatingDouble = pairRequest.RequestedToUser.RatingDouble ?? 0m
                })
            });

        _db.UserNotifications.Add(notification);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await SendTournamentNotificationAsync(pairRequest.RequestedByUserId, notification, pairRequest.TournamentId, pairRequest.PairRequestId, ct);

        return Ok(new { ok = true, message = "Đã từ chối lời mời ghép đôi." });
    }

    private async Task<Tournament?> LoadTournamentForUpdateAsync(long tournamentId, CancellationToken ct)
    {
        return await _db.Tournaments
            .FirstOrDefaultAsync(x => x.TournamentId == tournamentId && x.Status != "DRAFT", ct);
    }

    private async Task<(bool Ok, string Message)> ValidateUserCanCreateRegistrationAsync(
        Tournament tournament,
        long userId,
        long? partnerUserId,
        bool requireCapacity,
        CancellationToken ct)
    {
        var successCount = await _db.TournamentRegistrations
            .Where(x => x.TournamentId == tournament.TournamentId && x.Success)
            .CountAsync(ct);

        var tournamentValidation = ValidateTournamentRegistrationWindow(
            tournament.Status,
            tournament.RegisterDeadline);

        if (!tournamentValidation.CanRegister)
            return (false, tournamentValidation.Reason);

        if (requireCapacity && tournament.ExpectedTeams - successCount <= 0)
            return (false, "Giải đã đủ số đội đăng ký.");

        var userIds = partnerUserId.HasValue
            ? new[] { userId, partnerUserId.Value }
            : new[] { userId };

        var hasRegistration = await _db.TournamentRegistrations.AnyAsync(x =>
            x.TournamentId == tournament.TournamentId &&
            ((x.Player1UserId.HasValue && userIds.Contains(x.Player1UserId.Value)) ||
             (x.Player2UserId.HasValue && userIds.Contains(x.Player2UserId.Value))), ct);

        if (hasRegistration)
            return (false, "Vận động viên đã có đăng ký trong giải này.");

        return (true, "");
    }

    private (bool CanRegister, string Reason) ValidateTournamentRegistrationWindow(string? status, DateTime? registerDeadline)
    {
        var normalizedStatus = (status ?? string.Empty).Trim().ToUpperInvariant();
        if (normalizedStatus != "OPEN")
            return (false, "Giải chưa mở đăng ký hoặc đã đóng đăng ký.");

        if (registerDeadline.HasValue && registerDeadline.Value < DateTime.Now)
            return (false, "Giải đã hết hạn đăng ký.");

        return (true, "");
    }

    private async Task<TournamentRegistration> BuildRegistrationAsync(
        Tournament tournament,
        string gameType,
        UserRegistrationSnapshot player1,
        UserRegistrationSnapshot? player2,
        bool waitingPair,
        bool success,
        CancellationToken ct)
    {
        var maxIndex = await _db.TournamentRegistrations
            .Where(x => x.TournamentId == tournament.TournamentId)
            .MaxAsync(x => (int?)x.RegIndex, ct) ?? 0;

        var nextIndex = maxIndex + 1;
        var now = DateTime.UtcNow;
        var isDouble = IsDoubleLike(gameType);
        var player1Level = isDouble ? player1.RatingDouble : player1.RatingSingle;
        var player2Level = player2 == null ? 0m : player2.RatingDouble;

        return new TournamentRegistration
        {
            TournamentId = tournament.TournamentId,
            RegIndex = nextIndex,
            RegCode = $"{tournament.TournamentId}-{nextIndex:0000}",
            RegTime = now,
            RegTimeRaw = now.ToString("o"),
            Player1UserId = player1.UserId,
            Player1Name = player1.FullName,
            Player1Avatar = player1.AvatarUrl,
            Player1Level = player1Level,
            Player1Verified = player1.Verified,
            Player2UserId = player2?.UserId,
            Player2Name = waitingPair ? null : player2?.FullName,
            Player2Avatar = waitingPair ? null : player2?.AvatarUrl,
            Player2Level = waitingPair ? 0m : player2Level,
            Player2Verified = !waitingPair && (player2?.Verified ?? false),
            Points = isDouble ? player1Level + (waitingPair ? 0m : player2Level) : player1Level,
            Paid = false,
            WaitingPair = waitingPair,
            Success = success,
            CreatedAt = now
        };
    }

    private async Task<UserRegistrationSnapshot?> LoadUserSnapshotAsync(long userId, CancellationToken ct)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive)
            .Select(x => new
            {
                x.UserId,
                x.FullName,
                x.AvatarUrl,
                x.Verified,
                x.City,
                x.RatingSingle,
                x.RatingDouble,
                LatestRating = _db.UserRatingHistories
                    .Where(r => r.UserId == x.UserId)
                    .OrderByDescending(r => r.RatedAt)
                    .ThenByDescending(r => r.RatingHistoryId)
                    .Select(r => new { r.RatingSingle, r.RatingDouble })
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);

        if (user == null)
            return null;

        return new UserRegistrationSnapshot
        {
            UserId = user.UserId,
            FullName = user.FullName,
            AvatarUrl = user.AvatarUrl,
            AvatarUrlAbsolute = ToAbsoluteUrl(user.AvatarUrl),
            Verified = user.Verified,
            City = user.City,
            RatingSingle = user.LatestRating?.RatingSingle ?? user.RatingSingle ?? 0m,
            RatingDouble = user.LatestRating?.RatingDouble ?? user.RatingDouble ?? 0m
        };
    }

    private async Task<List<object>> LoadPairRequestsAsync(long tournamentId, long userId, bool sent, CancellationToken ct)
    {
        if (sent)
        {
            var rows = await _db.TournamentPairRequests
                .AsNoTracking()
                .Where(x =>
                    x.TournamentId == tournamentId &&
                    x.Status == "PENDING" &&
                    x.RequestedByUserId == userId)
                .OrderByDescending(x => x.RequestedAt)
                .Select(x => new
                {
                    x.PairRequestId,
                    x.TournamentId,
                    x.RequestedAt,
                    x.ExpiresAt,
                    x.Status,
                    x.RequestedToUser.UserId,
                    x.RequestedToUser.FullName,
                    x.RequestedToUser.AvatarUrl,
                    x.RequestedToUser.Verified
                })
                .ToListAsync(ct);

            return rows.Select(x => (object)new
            {
                x.PairRequestId,
                x.TournamentId,
                x.RequestedAt,
                x.ExpiresAt,
                x.Status,
                user = new
                {
                    x.UserId,
                    x.FullName,
                    AvatarUrl = ToAbsoluteUrl(x.AvatarUrl),
                    x.Verified
                }
            }).ToList();
        }

        var receivedRows = await _db.TournamentPairRequests
            .AsNoTracking()
            .Where(x =>
                x.TournamentId == tournamentId &&
                x.Status == "PENDING" &&
                x.RequestedToUserId == userId)
            .OrderByDescending(x => x.RequestedAt)
            .Select(x => new
            {
                x.PairRequestId,
                x.TournamentId,
                x.RequestedAt,
                x.ExpiresAt,
                x.Status,
                x.RequestedByUser.UserId,
                x.RequestedByUser.FullName,
                x.RequestedByUser.AvatarUrl,
                x.RequestedByUser.Verified
            })
            .ToListAsync(ct);

        return receivedRows.Select(x => (object)new
        {
            x.PairRequestId,
            x.TournamentId,
            x.RequestedAt,
            x.ExpiresAt,
            x.Status,
            user = new
            {
                x.UserId,
                x.FullName,
                AvatarUrl = ToAbsoluteUrl(x.AvatarUrl),
                x.Verified
            }
        }).ToList();
    }

    private async Task<bool> IsBlockedBetweenAsync(long userA, long userB, CancellationToken ct)
    {
        return await _db.UserBlocks.AnyAsync(x =>
            x.IsActive &&
            ((x.BlockerUserId == userA && x.BlockedUserId == userB) ||
             (x.BlockerUserId == userB && x.BlockedUserId == userA)), ct);
    }

    private async Task ExpirePendingPairRequestsAsync(long tournamentId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var expired = await _db.TournamentPairRequests
            .Where(x =>
                x.TournamentId == tournamentId &&
                x.Status == "PENDING" &&
                x.ExpiresAt.HasValue &&
                x.ExpiresAt.Value <= now)
            .ToListAsync(ct);

        if (expired.Count == 0)
            return;

        foreach (var item in expired)
        {
            item.Status = "EXPIRED";
            item.RespondedAt = now;
        }

        await _db.SaveChangesAsync(ct);
    }

    private static UserNotification BuildNotification(
        long userId,
        string notificationType,
        string title,
        string body,
        string refType,
        long refId,
        object payload)
    {
        return new UserNotification
        {
            UserId = userId,
            NotificationType = notificationType,
            Title = title,
            Body = body,
            RefType = refType,
            RefId = refId,
            PayloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            CreatedAt = DateTime.UtcNow
        };
    }

    private async Task SendTournamentNotificationAsync(long userId, UserNotification notification, long tournamentId, long pairRequestId, CancellationToken ct)
    {
        JsonElement? details = null;

        if (!string.IsNullOrWhiteSpace(notification.PayloadJson))
        {
            try
            {
                details = JsonSerializer.Deserialize<JsonElement>(notification.PayloadJson);
            }
            catch
            {
                details = null;
            }
        }

        await _realtimeHub.SendTournamentNotificationToUserAsync(userId.ToString(), new
        {
            notification.NotificationId,
            notification.NotificationType,
            notification.Title,
            notification.Body,
            notification.RefType,
            notification.RefId,
            notification.CreatedAt,
            TournamentId = tournamentId,
            PairRequestId = pairRequestId,
            Details = details
        });
    }

    private long GetUserIdFromToken()
    {
        var uid = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(uid) || !long.TryParse(uid, out var userId))
            throw new UnauthorizedAccessException("Invalid token: missing uid.");

        return userId;
    }

    private string? ToAbsoluteUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        url = url.Trim();

        if (Uri.TryCreate(url, UriKind.Absolute, out _))
            return url;

        if (Request?.Host.HasValue == true)
        {
            var relativePath = url.StartsWith("/") ? url : "/" + url;
            return $"{Request.Scheme}://{Request.Host}{Request.PathBase}{relativePath}";
        }

        var baseUrl = (_config["PublicBaseUrl"] ?? string.Empty).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            return url;

        return url.StartsWith("/") ? baseUrl + url : baseUrl + "/" + url;
    }

    private static string NormalizeGameType(string? gameType)
    {
        var normalized = (gameType ?? "DOUBLE").Trim().ToUpperInvariant();
        return normalized is "SINGLE" or "DOUBLE" or "MIXED" ? normalized : "DOUBLE";
    }

    private static bool IsDoubleLike(string? gameType)
    {
        var normalized = NormalizeGameType(gameType);
        return normalized is "DOUBLE" or "MIXED";
    }

    private static string? Clean(string? value, int maxLength)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static object ToUserBrief(UserRegistrationSnapshot user)
    {
        return new
        {
            user.UserId,
            user.FullName,
            AvatarUrl = user.AvatarUrlAbsolute,
            user.Verified,
            user.RatingSingle,
            user.RatingDouble
        };
    }

    public sealed class CreatePairRequestDto
    {
        public long RequestedToUserId { get; set; }
    }

    public sealed class PairRequestResponseDto
    {
        public string? ResponseNote { get; set; }
    }

    private sealed class UserRegistrationSnapshot
    {
        public long UserId { get; init; }
        public string FullName { get; init; } = "";
        public string? AvatarUrl { get; init; }
        public string? AvatarUrlAbsolute { get; init; }
        public bool Verified { get; init; }
        public string? City { get; init; }
        public decimal RatingSingle { get; init; }
        public decimal RatingDouble { get; init; }
    }
}
