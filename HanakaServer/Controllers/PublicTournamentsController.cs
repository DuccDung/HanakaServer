using HanakaServer.Data;
using HanakaServer.Dtos;
using HanakaServer.Helpers;
using HanakaServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/public/tournaments")]
    [AllowAnonymous]
    public class PublicTournamentsController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly IConfiguration _config;

        public PublicTournamentsController(PickleballDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        private string? ToAbsoluteUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            return _config["PublicBaseUrl"] + url;
        }

        // GET: /api/public/tournaments/{id}
        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetDetail(long id)
        {
            var t = await _db.Tournaments.AsNoTracking()
                .Where(x => x.TournamentId == id && !x.Remove && x.Status != "DRAFT")
                .Select(x => new PublicTournamentDetailDto
                {
                    TournamentId = x.TournamentId,
                    ExternalId = x.ExternalId,

                    Status = x.Status,
                    Title = x.Title,

                    BannerUrl = x.BannerUrl,

                    StartTimeRaw = x.StartTimeRaw,
                    StartTime = x.StartTime,

                    RegisterDeadlineRaw = x.RegisterDeadlineRaw,
                    RegisterDeadline = x.RegisterDeadline,

                    FormatText = x.FormatText,
                    PlayoffType = x.PlayoffType,
                    GameType = x.GameType,
                    GenderCategory = x.GenderCategory,

                    SingleLimit = x.SingleLimit,
                    DoubleLimit = x.DoubleLimit,

                    LocationText = x.LocationText,
                    AreaText = x.AreaText,

                    ExpectedTeams = x.ExpectedTeams,
                    MatchesCount = x.MatchesCount,

                    StatusText = x.StatusText,
                    StateText = x.StateText,

                    Organizer = x.Organizer,
                    CreatorName = x.CreatorName,

                    RegisteredCount = null,
                    PairedCount = null,

                    Content = x.Content,
                    CreatedAt = x.CreatedAt
                })
                .FirstOrDefaultAsync();

            if (t == null) return NotFound(new { message = "Tournament not found." });

            t.BannerUrl = ToAbsoluteUrl(t.BannerUrl);

            var matchCount = await _db.TournamentGroupMatches
                .AsNoTracking()
                .CountAsync(m => m.TournamentId == id);

            t.MatchesCount = matchCount;

            var regQ = _db.TournamentRegistrations
                .AsNoTracking()
                .Where(r => r.TournamentId == id);

            var waitingCount = await regQ.CountAsync(r => r.WaitingPair);
            var successCount = await regQ.CountAsync(r => r.Success);
            var tournamentType = TournamentTypeHelper.Resolve(t.GameType, t.GenderCategory);
            var (registeredCount, pairedCount) = ComputePublicRegistrationCounts(
                t.GameType,
                t.GenderCategory,
                successCount,
                waitingCount);

            t.GenderCategory = tournamentType.GenderCategory;
            t.TournamentTypeCode = tournamentType.TournamentTypeCode;
            t.TournamentTypeLabel = tournamentType.TournamentTypeLabel;
            t.RegisteredCount = registeredCount;
            t.PairedCount = pairedCount;

            return Ok(t);
        }

        // GET: /api/public/tournaments/{tournamentId}/registrations
        [HttpGet("{tournamentId:long}/registrations")]
        public async Task<IActionResult> PublicRegistrations(long tournamentId, [FromQuery] string tab = "ALL")
        {
            tab = (tab ?? "ALL").Trim().ToUpperInvariant();

            var tournament = await _db.Tournaments
                .AsNoTracking()
                .Where(t => t.TournamentId == tournamentId && !t.Remove && t.Status != "DRAFT")
                .Select(t => new { t.TournamentId, t.ExpectedTeams, t.GameType, t.GenderCategory, t.Title, t.Status })
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found." });

            var baseQ = _db.TournamentRegistrations
                .AsNoTracking()
                .Where(x => x.TournamentId == tournamentId);

            var successCount = await baseQ.CountAsync(x => x.Success);
            var waitingCount = await baseQ.CountAsync(x => x.WaitingPair);
            var capacityLeft = Math.Max(0, tournament.ExpectedTeams - successCount);

            var q = baseQ;
            if (tab == "SUCCESS") q = q.Where(x => x.Success);
            else if (tab == "WAITING") q = q.Where(x => x.WaitingPair);

            var rows = await (
                from r in q.OrderBy(x => x.RegIndex)

                join u1x in _db.Users on r.Player1UserId equals (long?)u1x.UserId into u1g
                from u1 in u1g.DefaultIfEmpty()

                join u2x in _db.Users on r.Player2UserId equals (long?)u2x.UserId into u2g
                from u2 in u2g.DefaultIfEmpty()

                // Get latest rating from UserRatingHistories for Player1
                let u1LatestRating = _db.UserRatingHistories
                    .Where(rh => rh.UserId == u1.UserId)
                    .OrderByDescending(rh => rh.RatedAt)
                    .ThenByDescending(rh => rh.RatingHistoryId)
                    .Select(rh => new { rh.RatingSingle, rh.RatingDouble })
                    .FirstOrDefault()

                // Get latest rating from UserRatingHistories for Player2
                let u2LatestRating = _db.UserRatingHistories
                    .Where(rh => rh.UserId == u2.UserId)
                    .OrderByDescending(rh => rh.RatedAt)
                    .ThenByDescending(rh => rh.RatingHistoryId)
                    .Select(rh => new { rh.RatingSingle, rh.RatingDouble })
                    .FirstOrDefault()

                select new
                {
                    r.RegistrationId,
                    r.RegIndex,
                    r.RegCode,
                    r.RegTime,
                    r.Points,
                    r.WaitingPair,
                    r.Success,

                    r.Player1UserId,
                    r.Player1Name,
                    r.Player1Avatar,
                    r.Player1Level,

                    r.Player2UserId,
                    r.Player2Name,
                    r.Player2Avatar,
                    r.Player2Level,

                    U1Avatar = (string?)u1.AvatarUrl,
                    U2Avatar = (string?)u2.AvatarUrl,

                    U1RatingSingle = (decimal?)(u1LatestRating != null ? u1LatestRating.RatingSingle : null),
                    U1RatingDouble = (decimal?)(u1LatestRating != null ? u1LatestRating.RatingDouble : null),
                    U2RatingSingle = (decimal?)(u2LatestRating != null ? u2LatestRating.RatingSingle : null),
                    U2RatingDouble = (decimal?)(u2LatestRating != null ? u2LatestRating.RatingDouble : null),

                    U1Verified = (bool?)(u1 != null ? u1.Verified : null),
                    U2Verified = (bool?)(u2 != null ? u2.Verified : null),
                }
            ).ToListAsync();

            var tournamentType = TournamentTypeHelper.Resolve(tournament.GameType, tournament.GenderCategory);
            bool isDouble = tournamentType.IsDoubleLike;

            PublicRegistrationItemDto MapItem(dynamic x)
            {
                decimal p1Level = x.Player1Level;
                decimal p2Level = x.Player2Level;

                // Only override with User rating if Player1Level is 0 or null (legacy data)
                // For new registrations, Player1Level already contains the correct snapshot rating
                if (x.Player1UserId != null && (p1Level == 0 || p1Level == null))
                {
                    var picked = (isDouble ? x.U1RatingDouble : x.U1RatingSingle);
                    if (picked != null) p1Level = (decimal)picked;
                }

                if (x.Player2UserId != null && (!x.WaitingPair && !string.IsNullOrWhiteSpace(x.Player2Name)))
                {
                    var picked = (isDouble ? x.U2RatingDouble : x.U2RatingSingle);
                    if (picked != null) p2Level = (decimal)picked;
                }

                var player2PickedLevel = !x.WaitingPair && !string.IsNullOrWhiteSpace(x.Player2Name)
                    ? p2Level
                    : (decimal?)null;

                var p1 = new PublicPlayerDto
                {
                    UserId = x.Player1UserId,
                    IsGuest = x.Player1UserId == null,
                    Verified = x.Player1UserId != null ? (x.U1Verified ?? false) : false,
                    Name = x.Player1Name ?? "",
                    Avatar = ToAbsoluteUrl(x.Player1UserId != null ? x.U1Avatar : x.Player1Avatar),
                    Level = p1Level
                };

                PublicPlayerDto? p2 = null;
                if (!x.WaitingPair && !string.IsNullOrWhiteSpace(x.Player2Name))
                {
                    p2 = new PublicPlayerDto
                    {
                        UserId = x.Player2UserId,
                        IsGuest = x.Player2UserId == null,
                        Verified = x.Player2UserId != null ? (x.U2Verified ?? false) : false,
                        Name = x.Player2Name ?? "",
                        Avatar = ToAbsoluteUrl(x.Player2UserId != null ? x.U2Avatar : x.Player2Avatar),
                        Level = p2Level
                    };
                }

                return new PublicRegistrationItemDto
                {
                    RegistrationId = x.RegistrationId,
                    RegIndex = x.RegIndex,
                    RegCode = x.RegCode,
                    RegTime = x.RegTime,
                    Points = CalcPoints(isDouble ? "DOUBLE" : "SINGLE", p1Level, player2PickedLevel),
                    WaitingPair = x.WaitingPair,
                    Success = x.Success,
                    Player1 = p1,
                    Player2 = p2
                };
            }

            var mapped = rows.Select(r => MapItem(r)).ToList();

            var successItems = mapped.Where(m => m.Success).ToArray();
            var waitingItems = mapped.Where(m => m.WaitingPair).ToArray();

            return Ok(new PublicTournamentRegistrationsResponseDto
            {
                Tournament = new
                {
                    tournament.TournamentId,
                    tournament.Title,
                    tournament.Status,
                    tournament.GameType,
                    GenderCategory = tournamentType.GenderCategory,
                    TournamentTypeCode = tournamentType.TournamentTypeCode,
                    TournamentTypeLabel = tournamentType.TournamentTypeLabel,
                    tournament.ExpectedTeams
                },
                Counts = new PublicRegistrationCountsDto
                {
                    Success = successCount,
                    Waiting = waitingCount,
                    CapacityLeft = capacityLeft
                },
                SuccessItems = successItems,
                WaitingItems = waitingItems
            });
        }

        private static decimal CalcPoints(string? gameType, decimal player1Level, decimal? player2Level)
        {
            var normalized = (gameType ?? "DOUBLE").Trim().ToUpperInvariant();
            return normalized == "SINGLE"
                ? player1Level
                : player1Level + (player2Level ?? 0m);
        }

        private static string? TrimToNull(string? s)
        {
            if (s == null) return null;
            var t = s.Trim();
            return string.IsNullOrWhiteSpace(t) ? null : t;
        }

        public class TournamentMobileListItemDto
        {
            public long TournamentId { get; set; }
            public string Title { get; set; } = null!;
            public string Status { get; set; } = null!;
            public string? StatusText { get; set; }
            public string? StateText { get; set; }
            public DateTime? StartTime { get; set; }
            public DateTime? RegisterDeadline { get; set; }
            public string GameType { get; set; } = "DOUBLE";
            public string GenderCategory { get; set; } = "OPEN";
            public string TournamentTypeCode { get; set; } = "DOUBLE_OPEN";
            public string TournamentTypeLabel { get; set; } = "";
            public decimal SingleLimit { get; set; }
            public decimal DoubleLimit { get; set; }
            public int ExpectedTeams { get; set; }
            public int RegisteredCount { get; set; }
            public int PairedCount { get; set; }
            public int MatchesCount { get; set; }
            public string? LocationText { get; set; }
            public string? AreaText { get; set; }
            public string? Organizer { get; set; }
            public string? CreatorName { get; set; }
            public string? FormatText { get; set; }
            public string? PlayoffType { get; set; }
            public string? BannerUrl { get; set; }
            public string? Content { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        private TournamentMobileListItemDto MapToDto(Tournament t)
        {
            var tournamentType = TournamentTypeHelper.Resolve(t.GameType, t.GenderCategory);

            return new TournamentMobileListItemDto
            {
                TournamentId = t.TournamentId,
                Title = t.Title,
                Status = t.Status,
                StatusText = t.StatusText,
                StateText = t.StateText,
                StartTime = t.StartTime,
                RegisterDeadline = t.RegisterDeadline,
                GameType = t.GameType ?? "DOUBLE",
                GenderCategory = tournamentType.GenderCategory,
                TournamentTypeCode = tournamentType.TournamentTypeCode,
                TournamentTypeLabel = tournamentType.TournamentTypeLabel,
                SingleLimit = t.SingleLimit,
                DoubleLimit = t.DoubleLimit,
                ExpectedTeams = t.ExpectedTeams,
                RegisteredCount = t.RegisteredCount,
                PairedCount = t.PairedCount,
                MatchesCount = t.MatchesCount,
                LocationText = t.LocationText,
                AreaText = t.AreaText,
                Organizer = t.Organizer,
                CreatorName = t.CreatorName,
                FormatText = t.FormatText,
                PlayoffType = t.PlayoffType,
                BannerUrl = ToAbsoluteUrl(t.BannerUrl),
                Content = t.Content,
                CreatedAt = t.CreatedAt
            };
        }

        private static (int registeredCount, int pairedCount) ComputePublicRegistrationCounts(
            string? gameType,
            string? genderCategory,
            int successCount,
            int waitingCount)
        {
            var isDoubleLike = TournamentTypeHelper.IsDoubleLike(gameType, genderCategory);

            if (isDoubleLike)
            {
                var pairedCount = successCount * 2;
                var registeredCount = waitingCount + pairedCount;
                return (registeredCount, pairedCount);
            }

            return (successCount + waitingCount, successCount);
        }

        // GET: /api/public/tournaments?page=1&pageSize=10&status=OPEN&query=abc
        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? status = null,
            [FromQuery] string? query = null)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 10;
            if (pageSize > 50) pageSize = 50;

            status = TrimToNull(status)?.ToUpperInvariant();
            query = TrimToNull(query);

            var q = _db.Tournaments.AsNoTracking().AsQueryable();

            // Ẩn toàn bộ giải draft và giải đã xóa mềm ở public API
            q = q.Where(t => !t.Remove && t.Status != "DRAFT");

            if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
            {
                q = q.Where(x => x.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                q = q.Where(x =>
                    x.Title.Contains(query) ||
                    (x.LocationText != null && x.LocationText.Contains(query)) ||
                    (x.AreaText != null && x.AreaText.Contains(query)) ||
                    (x.Organizer != null && x.Organizer.Contains(query)) ||
                    (x.CreatorName != null && x.CreatorName.Contains(query))
                );
            }

            var total = await q.CountAsync();

            var raw = await q
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.TournamentId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var tournamentIds = raw.Select(x => x.TournamentId).ToList();

            var registrationStats = await _db.TournamentRegistrations
                .AsNoTracking()
                .Where(x => tournamentIds.Contains(x.TournamentId))
                .GroupBy(x => x.TournamentId)
                .Select(g => new
                {
                    TournamentId = g.Key,
                    SuccessCount = g.Count(x => x.Success),
                    WaitingCount = g.Count(x => x.WaitingPair)
                })
                .ToListAsync();

            var matchStats = await _db.TournamentGroupMatches
                .AsNoTracking()
                .Where(x => tournamentIds.Contains(x.TournamentId))
                .GroupBy(x => x.TournamentId)
                .Select(g => new
                {
                    TournamentId = g.Key,
                    MatchesCount = g.Count()
                })
                .ToListAsync();

            var registrationStatsMap = registrationStats.ToDictionary(x => x.TournamentId, x => x);
            var matchStatsMap = matchStats.ToDictionary(x => x.TournamentId, x => x.MatchesCount);

            var items = raw.Select(t =>
            {
                var dto = MapToDto(t);

                registrationStatsMap.TryGetValue(t.TournamentId, out var regStat);
                matchStatsMap.TryGetValue(t.TournamentId, out var liveMatchesCount);

                var (registeredCount, pairedCount) = ComputePublicRegistrationCounts(
                    t.GameType,
                    t.GenderCategory,
                    regStat?.SuccessCount ?? 0,
                    regStat?.WaitingCount ?? 0);

                dto.RegisteredCount = registeredCount;
                dto.PairedCount = pairedCount;
                dto.MatchesCount = liveMatchesCount;

                return dto;
            }).ToList();

            var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
            var hasNextPage = page < totalPages;

            return Ok(new
            {
                page,
                pageSize,
                total,
                totalPages,
                hasNextPage,
                items
            });
        }
    }
}
