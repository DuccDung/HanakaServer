using HanakaServer.Data;
using HanakaServer.Dtos;
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
            // 1) Lấy detail tournament
            var t = await _db.Tournaments.AsNoTracking()
                .Where(x => x.TournamentId == id)
                .Select(x => new PublicTournamentDetailDto
                {
                    TournamentId = x.TournamentId,
                    ExternalId = x.ExternalId,

                    Status = x.Status,
                    Title = x.Title,

                    BannerUrl = x.BannerUrl, // map absolute phía dưới

                    StartTimeRaw = x.StartTimeRaw,
                    StartTime = x.StartTime,

                    RegisterDeadlineRaw = x.RegisterDeadlineRaw,
                    RegisterDeadline = x.RegisterDeadline,

                    FormatText = x.FormatText,
                    PlayoffType = x.PlayoffType,
                    GameType = x.GameType,

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

                    // ✅ IMPORTANT: ở đây CHƯA set RegisteredCount/PairedCount
                    // vì sẽ đếm từ bảng TournamentRegistrations phía dưới
                    RegisteredCount = null,
                    PairedCount = null,

                    Content = x.Content,
                    CreatedAt = x.CreatedAt
                })
                .FirstOrDefaultAsync();

            if (t == null) return NotFound(new { message = "Tournament not found." });

            // 2) Convert BannerUrl to absolute
            t.BannerUrl = ToAbsoluteUrl(t.BannerUrl);

            // 3) Đếm RegisteredCount/PairedCount từ TournamentRegistrations
            var gt = (t.GameType ?? "").Trim().ToUpperInvariant();

            // chỉ áp dụng cho giải đôi / đôi hỗn hợp
            if (gt == "DOUBLE" || gt == "MIXED")
            {
                var regQ = _db.TournamentRegistrations
                    .AsNoTracking()
                    .Where(r => r.TournamentId == id);

                // waiting: 1 người
                var waitingCount = await regQ.CountAsync(r => r.WaitingPair);

                // success: 1 đội đủ cặp => 2 người
                // (theo logic admin: DOUBLE đủ cặp sẽ Success=true và WaitingPair=false)
                var successCount = await regQ.CountAsync(r => r.Success);

                t.PairedCount = successCount * 2;
                t.RegisteredCount = waitingCount + successCount * 2;
            }
            else
            {
                // giải đơn: null theo yêu cầu
                t.RegisteredCount = null;
                t.PairedCount = null;
            }

            return Ok(t);
        }
        // GET: /api/public/tournaments/{tournamentId}/registrations
        // query:
        //   tab = ALL | SUCCESS | WAITING   (optional)
        // NOTE: public endpoint, không auth
        [HttpGet("{tournamentId:long}/registrations")]
        public async Task<IActionResult> PublicRegistrations(long tournamentId, [FromQuery] string tab = "ALL")
        {
            tab = (tab ?? "ALL").Trim().ToUpperInvariant();

            var tournament = await _db.Tournaments
                .AsNoTracking()
                .Where(t => t.TournamentId == tournamentId)
                .Select(t => new { t.TournamentId, t.ExpectedTeams, t.GameType, t.Title, t.Status })
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found." });

            // Base query
            var baseQ = _db.TournamentRegistrations
                .AsNoTracking()
                .Where(x => x.TournamentId == tournamentId);

            var successCount = await baseQ.CountAsync(x => x.Success);
            var waitingCount = await baseQ.CountAsync(x => x.WaitingPair);
            var capacityLeft = Math.Max(0, tournament.ExpectedTeams - successCount);

            // filter tab nếu muốn
            var q = baseQ;
            if (tab == "SUCCESS") q = q.Where(x => x.Success);
            else if (tab == "WAITING") q = q.Where(x => x.WaitingPair);

            // Join users để lấy verify/level/avatar
            // !!! IMPORTANT: đổi IsVerified theo field verify thật của bạn trong bảng Users !!!
            // Ví dụ: x.IsVerified hoặc x.Verified hoặc x.IsKycVerified ...
            var rows = await (
                from r in q.OrderBy(x => x.RegIndex)

                join u1x in _db.Users on r.Player1UserId equals (long?)u1x.UserId into u1g
                from u1 in u1g.DefaultIfEmpty()

                join u2x in _db.Users on r.Player2UserId equals (long?)u2x.UserId into u2g
                from u2 in u2g.DefaultIfEmpty()

                select new
                {
                    r.RegistrationId,
                    r.RegIndex,
                    r.RegCode,
                    r.RegTime,
                    r.Points,
                    r.WaitingPair,
                    r.Success,

                    // player1 from reg
                    r.Player1UserId,
                    r.Player1Name,
                    r.Player1Avatar,
                    r.Player1Level,

                    // player2 from reg
                    r.Player2UserId,
                    r.Player2Name,
                    r.Player2Avatar,
                    r.Player2Level,

                    // user lookup (nullable)
                    U1Avatar = (string?)u1.AvatarUrl,
                    U2Avatar = (string?)u2.AvatarUrl,

                    // nếu bạn muốn lấy level theo rating (giống admin):
                    U1RatingSingle = (decimal?)(u1 != null ? (u1.RatingSingle ?? 0m) : null),
                    U1RatingDouble = (decimal?)(u1 != null ? (u1.RatingDouble ?? 0m) : null),
                    U2RatingSingle = (decimal?)(u2 != null ? (u2.RatingSingle ?? 0m) : null),
                    U2RatingDouble = (decimal?)(u2 != null ? (u2.RatingDouble ?? 0m) : null),

                    // ✅ VERIFY FLAG: đổi theo schema bảng Users của bạn
                    U1Verified = (bool?)(u1 != null ? u1.Verified : null),
                    U2Verified = (bool?)(u2 != null ? u2.Verified : null),
                }
            ).ToListAsync();

            string gt = (tournament.GameType ?? "DOUBLE").Trim().ToUpperInvariant();
            bool isDouble = (gt == "DOUBLE" || gt == "MIXED");

            // map rows -> dto
            PublicRegistrationItemDto MapItem(dynamic x)
            {
                // level pick giống admin (tuỳ bạn có muốn):
                decimal p1Level = x.Player1Level;
                decimal p2Level = x.Player2Level;

                if (x.Player1UserId != null)
                {
                    var picked = (isDouble ? x.U1RatingDouble : x.U1RatingSingle);
                    if (picked != null) p1Level = (decimal)picked;
                }
                if (x.Player2UserId != null)
                {
                    var picked = (isDouble ? x.U2RatingDouble : x.U2RatingSingle);
                    if (picked != null) p2Level = (decimal)picked;
                }

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
                // DOUBLE waiting => player2 null
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
                    Points = x.Points,
                    WaitingPair = x.WaitingPair,
                    Success = x.Success,
                    Player1 = p1,
                    Player2 = p2
                };
            }

            var mapped = rows.Select(r => MapItem(r)).ToList();

            // successItems/waitingItems
            var successItems = mapped.Where(m => m.Success).ToArray();
            var waitingItems = mapped.Where(m => m.WaitingPair).ToArray();

            // trả response giống structure admin + thêm list
            return Ok(new PublicTournamentRegistrationsResponseDto
            {
                Tournament = new
                {
                    tournament.TournamentId,
                    tournament.Title,
                    tournament.Status,
                    tournament.GameType,
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
    }
}