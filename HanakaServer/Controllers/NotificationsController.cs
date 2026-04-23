using HanakaServer.Data;
using Microsoft.AspNetCore.Authorization;
using HanakaServer.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public class NotificationsController : ControllerBase
    {
        private const int DefaultInboxPageSize = 20;
        private const int MaxInboxPageSize = 50;

        private readonly PickleballDbContext _db;
        private readonly IConfiguration _config;

        public NotificationsController(PickleballDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
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
            if (string.IsNullOrWhiteSpace(url)) return null;
            return _config["PublicBaseUrl"] + url;
        }

        private static TimeZoneInfo GetVietnamTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            }
            catch
            {
                return TimeZoneInfo.Local;
            }
        }

        private static string ToVietnamDateTimeText(DateTime? dt)
        {
            if (!dt.HasValue) return "";

            var vnTz = GetVietnamTimeZone();

            // giả định StartAt đang lưu UTC
            var utc = dt.Value.Kind == DateTimeKind.Utc
                ? dt.Value
                : DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc);

            var vnTime = TimeZoneInfo.ConvertTimeFromUtc(utc, vnTz);

            return vnTime.ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture);
        }

        private static DateTime GetVietnamNowUtcComparable()
        {
            // dùng utc để so sánh nếu StartAt lưu UTC
            return DateTime.UtcNow;
        }

        private static string? ReadJsonString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
                return null;

            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }

        private static long? ReadJsonLong(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
                return null;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
                return number;

            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number))
                return number;

            return null;
        }

        private static bool? ReadJsonBool(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
                return null;

            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var result))
                return result;

            return null;
        }

        private static object? ReadUserBrief(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
                return null;

            return new
            {
                userId = ReadJsonLong(value, "userId") ?? 0,
                fullName = ReadJsonString(value, "fullName") ?? "",
                avatarUrl = ReadJsonString(value, "avatarUrl"),
                verified = ReadJsonBool(value, "verified") ?? false
            };
        }

        [HttpGet("inbox")]
        public async Task<IActionResult> GetInboxNotifications([FromQuery] int page = 1, [FromQuery] int pageSize = DefaultInboxPageSize, CancellationToken ct = default)
        {
            try
            {
                var userId = GetUserIdFromToken();
                var normalizedPage = page < 1 ? 1 : page;
                var normalizedPageSize = pageSize < 1
                    ? DefaultInboxPageSize
                    : Math.Min(pageSize, MaxInboxPageSize);
                var skip = (normalizedPage - 1) * normalizedPageSize;
                var inboxQuery = _db.UserNotifications
                    .AsNoTracking()
                    .Where(x => x.UserId == userId && x.NotificationType != "PAIR_REQUEST");

                var unreadTotal = await _db.UserNotifications
                    .AsNoTracking()
                    .CountAsync(x => x.UserId == userId && !x.IsRead, ct);

                var unreadNonPairTotal = await _db.UserNotifications
                    .AsNoTracking()
                    .CountAsync(x =>
                        x.UserId == userId &&
                        !x.IsRead &&
                        x.NotificationType != "PAIR_REQUEST", ct);

                var total = await inboxQuery.CountAsync(ct);

                var rows = await inboxQuery
                    .OrderByDescending(x => x.CreatedAt)
                    .Skip(skip)
                    .Take(normalizedPageSize)
                    .Select(x => new
                    {
                        x.NotificationId,
                        x.NotificationType,
                        x.Title,
                        x.Body,
                        x.RefType,
                        x.RefId,
                        x.IsRead,
                        x.ReadAt,
                        x.CreatedAt,
                        x.PayloadJson
                    })
                    .ToListAsync(ct);

                var items = rows.Select(x =>
                {
                    long pairRequestId = 0;
                    long tournamentId = 0;
                    long registrationId = 0;
                    string? tournamentTitle = null;
                    string? responseNote = null;
                    object? acceptedBy = null;
                    object? requestedBy = null;
                    object? requestedTo = null;

                    if (!string.IsNullOrWhiteSpace(x.PayloadJson))
                    {
                        try
                        {
                            using var document = JsonDocument.Parse(x.PayloadJson);
                            var root = document.RootElement;

                            pairRequestId = ReadJsonLong(root, "pairRequestId") ?? (x.RefType == "PAIR_REQUEST" ? x.RefId ?? 0 : 0);
                            tournamentId = ReadJsonLong(root, "tournamentId") ?? 0;
                            registrationId = ReadJsonLong(root, "registrationId") ?? 0;
                            tournamentTitle = ReadJsonString(root, "tournamentTitle") ?? ReadJsonString(root, "title");
                            responseNote = ReadJsonString(root, "responseNote");
                            acceptedBy = ReadUserBrief(root, "acceptedBy");
                            requestedBy = ReadUserBrief(root, "requestedBy");
                            requestedTo = ReadUserBrief(root, "requestedTo");
                        }
                        catch
                        {
                        }
                    }

                    return new
                    {
                        id = x.NotificationId,
                        notificationId = x.NotificationId,
                        type = x.NotificationType,
                        notificationType = x.NotificationType,
                        title = x.Title,
                        message = x.Body,
                        x.IsRead,
                        x.ReadAt,
                        x.CreatedAt,
                        x.RefType,
                        x.RefId,
                        pairRequestId,
                        tournamentId,
                        tournamentTitle,
                        registrationId,
                        responseNote,
                        acceptedBy,
                        requestedBy,
                        requestedTo
                    };
                }).ToList();

                return Ok(new
                {
                    page = normalizedPage,
                    pageSize = normalizedPageSize,
                    unreadTotal,
                    unreadNonPairTotal,
                    total,
                    hasMore = skip + items.Count < total,
                    items
                });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
            {
                return new EmptyResult();
            }
        }

        [HttpPost("inbox/read-all")]
        public async Task<IActionResult> MarkInboxNotificationsRead(CancellationToken ct)
        {
            try
            {
                var userId = GetUserIdFromToken();
                var now = DateTime.UtcNow;

                var notifications = await _db.UserNotifications
                    .Where(x => x.UserId == userId && !x.IsRead && x.NotificationType != "PAIR_REQUEST")
                    .ToListAsync(ct);

                if (notifications.Count == 0)
                {
                    return Ok(new { ok = true, count = 0 });
                }

                foreach (var notification in notifications)
                {
                    notification.IsRead = true;
                    notification.ReadAt = now;
                }

                await _db.SaveChangesAsync(ct);

                return Ok(new
                {
                    ok = true,
                    count = notifications.Count
                });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
            {
                return new EmptyResult();
            }
        }

        [HttpPost("inbox/{notificationId:long}/read")]
        public async Task<IActionResult> MarkInboxNotificationRead(long notificationId, CancellationToken ct)
        {
            try
            {
                var userId = GetUserIdFromToken();
                var notification = await _db.UserNotifications
                    .FirstOrDefaultAsync(x =>
                        x.NotificationId == notificationId &&
                        x.UserId == userId &&
                        x.NotificationType != "PAIR_REQUEST", ct);

                if (notification == null)
                {
                    return NotFound(new { message = "Không tìm thấy thông báo." });
                }

                if (notification.IsRead)
                {
                    return Ok(new
                    {
                        ok = true,
                        alreadyRead = true,
                        notificationId = notification.NotificationId
                    });
                }

                notification.IsRead = true;
                notification.ReadAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                return Ok(new
                {
                    ok = true,
                    notificationId = notification.NotificationId
                });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
            {
                return new EmptyResult();
            }
        }

        [HttpGet("upcoming-matches")]
        public async Task<IActionResult> GetUpcomingMatchNotifications()
        {
            var userId = GetUserIdFromToken();
            var nowUtc = GetVietnamNowUtcComparable();

            var rows = await (
                from m in _db.TournamentGroupMatches.AsNoTracking()
                join t in _db.Tournaments.AsNoTracking() on m.TournamentId equals t.TournamentId
                join team1 in _db.TournamentRegistrations.AsNoTracking() on m.Team1RegistrationId equals team1.RegistrationId
                join team2 in _db.TournamentRegistrations.AsNoTracking() on m.Team2RegistrationId equals team2.RegistrationId
                where !m.IsCompleted
                      && !t.Remove
                      && m.StartAt.HasValue
                      && m.StartAt.Value >= nowUtc
                      && (
                            team1.Player1UserId == userId ||
                            team1.Player2UserId == userId ||
                            team2.Player1UserId == userId ||
                            team2.Player2UserId == userId
                         )
                orderby m.StartAt ascending
                select new
                {
                    m.MatchId,
                    m.TournamentId,
                    TournamentTitle = t.Title,
                    TournamentGameType = t.GameType,
                    TournamentGenderCategory = t.GenderCategory,
                    m.StartAt,
                    m.AddressText,
                    m.CourtText,

                    Team1 = new
                    {
                        team1.RegistrationId,
                        team1.Player1UserId,
                        team1.Player1Name,
                        team1.Player1Avatar,
                        team1.Player2UserId,
                        team1.Player2Name,
                        team1.Player2Avatar
                    },
                    Team2 = new
                    {
                        team2.RegistrationId,
                        team2.Player1UserId,
                        team2.Player1Name,
                        team2.Player1Avatar,
                        team2.Player2UserId,
                        team2.Player2Name,
                        team2.Player2Avatar
                    }
                }
            ).ToListAsync();

            var items = rows.Select(x =>
            {
                var tournamentType = TournamentTypeHelper.Resolve(x.TournamentGameType, x.TournamentGenderCategory);
                var isMyTeam1 =
                    x.Team1.Player1UserId == userId ||
                    x.Team1.Player2UserId == userId;

                var myTeam = isMyTeam1 ? x.Team1 : x.Team2;
                var opponentTeam = isMyTeam1 ? x.Team2 : x.Team1;

                return new
                {
                    id = x.MatchId,
                    type = "TOURNAMENT_MATCH",
                    title = "Hanaka Sport - Thông báo",
                    message =
                        $"Bạn chuẩn bị thi đấu giải {x.TournamentTitle} lúc {ToVietnamDateTimeText(x.StartAt)} tại {x.AddressText ?? "Chưa cập nhật địa chỉ"}" +
                        $"{(string.IsNullOrWhiteSpace(x.CourtText) ? "" : $" - sân {x.CourtText}")}.",

                    tournament = new
                    {
                        tournamentId = x.TournamentId,
                        title = x.TournamentTitle,
                        gameType = x.TournamentGameType,
                        genderCategory = tournamentType.GenderCategory,
                        tournamentTypeCode = tournamentType.TournamentTypeCode,
                        tournamentTypeLabel = tournamentType.TournamentTypeLabel
                    },

                    match = new
                    {
                        matchId = x.MatchId,
                        startAt = x.StartAt,
                        startAtText = ToVietnamDateTimeText(x.StartAt),
                        addressText = x.AddressText,
                        courtText = x.CourtText
                    },

                    myTeam = new
                    {
                        registrationId = myTeam.RegistrationId,
                        player1 = new
                        {
                            userId = myTeam.Player1UserId,
                            name = myTeam.Player1Name,
                            avatarUrl = ToAbsoluteUrl(myTeam.Player1Avatar)
                        },
                        player2 = string.IsNullOrWhiteSpace(myTeam.Player2Name) ? null : new
                        {
                            userId = myTeam.Player2UserId,
                            name = myTeam.Player2Name,
                            avatarUrl = ToAbsoluteUrl(myTeam.Player2Avatar)
                        }
                    },

                    opponentTeam = new
                    {
                        registrationId = opponentTeam.RegistrationId,
                        player1 = new
                        {
                            userId = opponentTeam.Player1UserId,
                            name = opponentTeam.Player1Name,
                            avatarUrl = ToAbsoluteUrl(opponentTeam.Player1Avatar)
                        },
                        player2 = string.IsNullOrWhiteSpace(opponentTeam.Player2Name) ? null : new
                        {
                            userId = opponentTeam.Player2UserId,
                            name = opponentTeam.Player2Name,
                            avatarUrl = ToAbsoluteUrl(opponentTeam.Player2Avatar)
                        }
                    }
                };
            }).ToList();

            return Ok(new
            {
                total = items.Count,
                items
            });
        }

        [HttpGet("pair-requests")]
        public async Task<IActionResult> GetPairRequestNotifications(CancellationToken ct)
        {
            try
            {
                var userId = GetUserIdFromToken();
                var now = DateTime.UtcNow;

                var expired = await _db.TournamentPairRequests
                    .Where(x =>
                        x.RequestedToUserId == userId &&
                        x.Status == "PENDING" &&
                        x.ExpiresAt.HasValue &&
                        x.ExpiresAt.Value <= now)
                    .ToListAsync(ct);

                if (expired.Count > 0)
                {
                    foreach (var item in expired)
                    {
                        item.Status = "EXPIRED";
                        item.RespondedAt = now;
                    }

                    await _db.SaveChangesAsync(ct);
                }

                var pendingRows = await _db.TournamentPairRequests
                    .AsNoTracking()
                    .Where(x => x.RequestedToUserId == userId && x.Status == "PENDING")
                    .OrderByDescending(x => x.RequestedAt)
                    .Select(x => new
                    {
                        x.PairRequestId,
                        x.TournamentId,
                        TournamentTitle = x.Tournament.Title,
                        TournamentGameType = x.Tournament.GameType,
                        TournamentGenderCategory = x.Tournament.GenderCategory,
                        x.RequestedAt,
                        x.ExpiresAt,
                        x.Status,
                        RequestedByUserId = x.RequestedByUser.UserId,
                        RequestedByName = x.RequestedByUser.FullName,
                        RequestedByAvatar = x.RequestedByUser.AvatarUrl,
                        RequestedByVerified = x.RequestedByUser.Verified
                    })
                    .ToListAsync(ct);

                var pendingItems = pendingRows.Select(x =>
                {
                    var tournamentType = TournamentTypeHelper.Resolve(x.TournamentGameType, x.TournamentGenderCategory);

                    return new
                    {
                        type = "PAIR_REQUEST",
                        x.PairRequestId,
                        x.TournamentId,
                        tournamentTitle = x.TournamentTitle,
                        tournamentGameType = x.TournamentGameType,
                        tournamentGenderCategory = tournamentType.GenderCategory,
                        tournamentTypeCode = tournamentType.TournamentTypeCode,
                        tournamentTypeLabel = tournamentType.TournamentTypeLabel,
                        x.RequestedAt,
                        x.ExpiresAt,
                        x.Status,
                        title = "Lời mời ghép đôi",
                        message = $"{x.RequestedByName} mời bạn ghép cặp tại giải {x.TournamentTitle}.",
                        requestedBy = new
                        {
                            userId = x.RequestedByUserId,
                            fullName = x.RequestedByName,
                            avatarUrl = ToAbsoluteUrl(x.RequestedByAvatar),
                            verified = x.RequestedByVerified
                        }
                    };
                }).ToList();

                return Ok(new
                {
                    total = pendingItems.Count,
                    items = pendingItems
                });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
            {
                return new EmptyResult();
            }
        }
    }
}
