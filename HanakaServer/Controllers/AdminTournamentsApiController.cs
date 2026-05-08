using HanakaServer.Data;
using HanakaServer.Dtos;
using HanakaServer.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HanakaServer.Controllers
{
    [Route("api/admin/tournaments")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminTournamentsApiController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;

        public AdminTournamentsApiController(PickleballDbContext db, IWebHostEnvironment env, IConfiguration config)
        {
            _db = db;
            _env = env;
            _config = config;
        }

        // ===== URL helpers: DB lưu relative, response trả absolute =====

        private string? ToAbsoluteUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            return _config["PublicBaseUrl"] + url;
        }

        private string? NormalizeToRelative(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            url = url.Trim();

            if (url.StartsWith("/")) return url;

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return uri.PathAndQuery;

            return url;
        }

        private static string? TrimToNull(string? s)
        {
            if (s == null) return null;
            var t = s.Trim();
            return string.IsNullOrWhiteSpace(t) ? null : t;
        }

        private static string Upper(string? s, string fallback)
        {
            return string.IsNullOrWhiteSpace(s) ? fallback : s.Trim().ToUpperInvariant();
        }

        private IQueryable<HanakaServer.Models.Tournament> ActiveTournamentsQuery(bool asNoTracking = false)
        {
            var query = asNoTracking
                ? _db.Tournaments.AsNoTracking()
                : _db.Tournaments.AsQueryable();

            return query.Where(x => !x.Remove);
        }

        private static (bool Ok, string? GameType, string? GenderCategory, string? Message) ResolveTournamentType(
            string? gameType,
            string? genderCategory)
        {
            var normalizedGameType = Upper(gameType, string.Empty);

            // Backward compatibility cho client cũ còn gửi MIXED ở GameType.
            if (normalizedGameType == "MIXED")
            {
                return (true, "DOUBLE", "MIXED", null);
            }

            if (normalizedGameType is not ("SINGLE" or "DOUBLE"))
            {
                return (false, null, null, "GameType không hợp lệ. Dùng SINGLE hoặc DOUBLE.");
            }

            var normalizedGenderCategory = Upper(genderCategory, "OPEN");
            if (normalizedGenderCategory is not ("OPEN" or "MEN" or "WOMEN" or "MIXED"))
            {
                return (false, null, null, "GenderCategory không hợp lệ. Dùng OPEN/MEN/WOMEN/MIXED.");
            }

            if (normalizedGameType == "SINGLE" && normalizedGenderCategory == "MIXED")
            {
                return (false, null, null, "Giải đơn không thể có GenderCategory = MIXED.");
            }

            return (true, normalizedGameType, normalizedGenderCategory, null);
        }

        private TournamentListItemDto MapToDto(HanakaServer.Models.Tournament t)
        {
            var tournamentType = TournamentTypeHelper.Resolve(t.GameType, t.GenderCategory);

            return new TournamentListItemDto
            {
                TournamentId = t.TournamentId,
                Title = t.Title,
                Status = t.Status,
                StartTime = t.StartTime,
                RegisterDeadline = t.RegisterDeadline,
                GameType = t.GameType ?? "DOUBLE",
                GenderCategory = tournamentType.GenderCategory,
                TournamentTypeCode = tournamentType.TournamentTypeCode,
                TournamentTypeLabel = tournamentType.TournamentTypeLabel,
                ExpectedTeams = t.ExpectedTeams,
                LocationText = t.LocationText,
                AreaText = t.AreaText,
                BannerUrl = ToAbsoluteUrl(t.BannerUrl),
                CreatedAt = t.CreatedAt,
                SingleLimit = t.SingleLimit,
                DoubleLimit = t.DoubleLimit,

                FormatText = t.FormatText,
                PlayoffType = t.PlayoffType,
                Organizer = t.Organizer,
                CreatorName = t.CreatorName,
                StatusText = t.StatusText,
                StateText = t.StateText,
                MatchesCount = t.MatchesCount,
                RegisteredCount = t.RegisteredCount,
                PairedCount = t.PairedCount,
                Content = t.Content,
                TournamentRule = t.TournamentRule,
                ZaloLink = t.ZaloLink
            };
        }
        // GET: /api/admin/tournaments?status=OPEN&page=1&pageSize=50
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 50;
            if (pageSize > 200) pageSize = 200;

            status = status?.Trim();

            var q = ActiveTournamentsQuery(asNoTracking: true);

            if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
                q = q.Where(t => t.Status == status);

            var total = await q.CountAsync();

            var raw = await q
                .OrderByDescending(t => t.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new HanakaServer.Models.Tournament
                {
                    TournamentId = t.TournamentId,
                    Title = t.Title,
                    Status = t.Status,
                    StartTime = t.StartTime,
                    RegisterDeadline = t.RegisterDeadline,
                    GameType = t.GameType,
                    GenderCategory = t.GenderCategory,
                    ExpectedTeams = t.ExpectedTeams,
                    LocationText = t.LocationText,
                    AreaText = t.AreaText,
                    BannerUrl = t.BannerUrl,
                    CreatedAt = t.CreatedAt,
                    SingleLimit = t.SingleLimit,
                    DoubleLimit = t.DoubleLimit,
                    ZaloLink = t.ZaloLink,

                    Organizer = t.Organizer,
                    CreatorName = t.CreatorName,
                    FormatText = t.FormatText,
                    PlayoffType = t.PlayoffType
                })
                .ToListAsync();

            var items = raw.Select(MapToDto);

            return Ok(new { page, pageSize, total, items });
        }

        // GET: /api/admin/tournaments/{id}
        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetDetail([FromRoute] long id)
        {
            var t = await ActiveTournamentsQuery(asNoTracking: true)
                .FirstOrDefaultAsync(x => x.TournamentId == id);
            if (t == null) return NotFound(new { message = "Tournament not found." });

            return Ok(MapToDto(t));
        }

        // POST: /api/admin/tournaments (multipart/form-data)
        [HttpPost]
        [RequestSizeLimit(10_000_000)]
        public async Task<IActionResult> Create([FromForm] CreateTournamentRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Title))
                return BadRequest(new { message = "Title is required." });

            if (string.IsNullOrWhiteSpace(req.GameType))
                return BadRequest(new { message = "GameType is required." });

            var tournamentType = ResolveTournamentType(req.GameType, req.GenderCategory);
            if (!tournamentType.Ok)
                return BadRequest(new { message = tournamentType.Message });

            var status = Upper(req.Status, "DRAFT");

            // ===== Upload banner (nếu có) =====
            string? bannerRelativeUrl = null;

            if (req.BannerFile != null && req.BannerFile.Length > 0)
            {
                var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
                var ext = Path.GetExtension(req.BannerFile.FileName).ToLowerInvariant();
                if (!allowed.Contains(ext))
                    return BadRequest(new { message = "Banner only accepts jpg, jpeg, png, webp." });

                var uploadsDir = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", "tournaments");
                Directory.CreateDirectory(uploadsDir);

                var fileName = $"{Guid.NewGuid():N}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}";
                var filePath = Path.Combine(uploadsDir, fileName);

                await using (var stream = System.IO.File.Create(filePath))
                {
                    await req.BannerFile.CopyToAsync(stream);
                }

                bannerRelativeUrl = $"/uploads/tournaments/{fileName}";
            }

            var t = new HanakaServer.Models.Tournament
            {
                Status = status,
                Title = req.Title.Trim(),
                Remove = false,
                BannerUrl = bannerRelativeUrl,
                StartTime = req.StartTime,
                RegisterDeadline = req.RegisterDeadline,
                GameType = tournamentType.GameType!,
                GenderCategory = tournamentType.GenderCategory!,
                ExpectedTeams = req.ExpectedTeams ?? 0,
                LocationText = TrimToNull(req.LocationText),
                AreaText = TrimToNull(req.AreaText),
                SingleLimit = req.SingleLimit ?? 0,
                DoubleLimit = req.DoubleLimit ?? 0,
                Content = req.Content,
                TournamentRule = req.TournamentRule,
                ZaloLink = TrimToNull(req.ZaloLink),
                CreatedAt = DateTime.UtcNow,


                //  NEW
                Organizer = TrimToNull(req.Organizer),
                CreatorName = TrimToNull(req.CreatorName),
                FormatText = TrimToNull(req.FormatText),
                PlayoffType = TrimToNull(req.PlayoffType),

                StatusText = TrimToNull(req.StatusText),
                StateText = TrimToNull(req.StateText)
            };

            _db.Tournaments.Add(t);
            await _db.SaveChangesAsync();

            //  trả full dto để UI prepend/update table
            return Ok(MapToDto(t));
        }

        // PUT: /api/admin/tournaments/{id} (multipart/form-data)
        [HttpPut("{id:long}")]
        [RequestSizeLimit(10_000_000)]
        public async Task<IActionResult> Update([FromRoute] long id, [FromForm] UpdateTournamentRequest req)
        {
            var t = await ActiveTournamentsQuery()
                .FirstOrDefaultAsync(x => x.TournamentId == id);
            if (t == null) return NotFound(new { message = "Tournament not found." });

            if (!string.IsNullOrWhiteSpace(req.Title))
                t.Title = req.Title.Trim();

            if (!string.IsNullOrWhiteSpace(req.Status))
                t.Status = req.Status.Trim().ToUpperInvariant();

            if (req.GameType != null || req.GenderCategory != null)
            {
                var tournamentType = ResolveTournamentType(
                    req.GameType ?? t.GameType,
                    req.GenderCategory ?? t.GenderCategory);

                if (!tournamentType.Ok)
                    return BadRequest(new { message = tournamentType.Message });

                t.GameType = tournamentType.GameType!;
                t.GenderCategory = tournamentType.GenderCategory!;
            }

            if (req.ExpectedTeams.HasValue)
                t.ExpectedTeams = req.ExpectedTeams.Value;

            if (req.StartTime.HasValue)
                t.StartTime = req.StartTime.Value;

            if (req.RegisterDeadline.HasValue)
                t.RegisterDeadline = req.RegisterDeadline.Value;

            if (req.LocationText != null)
                t.LocationText = TrimToNull(req.LocationText);

            if (req.AreaText != null)
                t.AreaText = TrimToNull(req.AreaText);

            if (req.SingleLimit.HasValue) t.SingleLimit = req.SingleLimit.Value;
            if (req.DoubleLimit.HasValue) t.DoubleLimit = req.DoubleLimit.Value;

            if (req.Content != null) t.Content = req.Content;
            if (req.ZaloLink != null) t.ZaloLink = TrimToNull(req.ZaloLink);

            // edit các field mở rộng (cho phép clear bằng string rỗng)
            if (req.Organizer != null) t.Organizer = TrimToNull(req.Organizer);
            if (req.CreatorName != null) t.CreatorName = TrimToNull(req.CreatorName);
            if (req.FormatText != null) t.FormatText = TrimToNull(req.FormatText);
            if (req.PlayoffType != null) t.PlayoffType = TrimToNull(req.PlayoffType);
            if (req.TournamentRule != null) t.TournamentRule = req.TournamentRule;
            // ===== nếu có upload banner mới =====
            if (req.BannerFile != null && req.BannerFile.Length > 0)
            {
                var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
                var ext = Path.GetExtension(req.BannerFile.FileName).ToLowerInvariant();
                if (!allowed.Contains(ext))
                    return BadRequest(new { message = "Banner only accepts jpg, jpeg, png, webp." });

                var uploadsDir = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", "tournaments");
                Directory.CreateDirectory(uploadsDir);

                var fileName = $"{id}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}";
                var filePath = Path.Combine(uploadsDir, fileName);

                await using (var stream = System.IO.File.Create(filePath))
                {
                    await req.BannerFile.CopyToAsync(stream);
                }

                t.BannerUrl = $"/uploads/tournaments/{fileName}";
            }
            else
            {
                t.BannerUrl = NormalizeToRelative(t.BannerUrl);
            }

            await _db.SaveChangesAsync();

            // trả full dto để UI update row
            return Ok(MapToDto(t));
        }

        // DELETE: /api/admin/tournaments/{id}
        [HttpDelete("{id:long}")]
        public async Task<IActionResult> SoftDelete([FromRoute] long id)
        {
            var t = await ActiveTournamentsQuery()
                .FirstOrDefaultAsync(x => x.TournamentId == id);

            if (t == null)
                return NotFound(new { message = "Tournament not found." });

            t.Remove = true;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                ok = true,
                tournamentId = id,
                message = "Đã xóa mềm giải đấu."
            });
        }
    }
}
