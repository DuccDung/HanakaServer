using HanakaServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Claims;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public class NotificationsController : ControllerBase
    {
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
                        title = x.TournamentTitle
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
    }
}