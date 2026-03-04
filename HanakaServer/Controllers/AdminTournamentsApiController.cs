using HanakaServer.Data;
using HanakaServer.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HanakaServer.Controllers
{
    [Route("api/admin/tournaments")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    [IgnoreAntiforgeryToken]
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
        private string GetBaseUrl()
        {
            // Ưu tiên appsettings nếu bạn bật:
            // var baseUrl = _config["AppSettings:PublicBaseUrl"]?.Trim();
            // if (!string.IsNullOrWhiteSpace(baseUrl)) return baseUrl.TrimEnd('/');

            var baseUrl = "http://192.168.0.101:5062";
            return baseUrl.TrimEnd('/');
        }

        private string? ToAbsoluteUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            // nếu đã absolute thì trả luôn
            if (Uri.TryCreate(url, UriKind.Absolute, out _)) return url;

            if (!url.StartsWith("/")) url = "/" + url;
            return GetBaseUrl() + url;
        }

        private string? NormalizeToRelative(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            url = url.Trim();

            // relative sẵn
            if (url.StartsWith("/")) return url;

            // absolute => lấy path
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return uri.PathAndQuery; // thường chỉ cần uri.AbsolutePath

            return url;
        }

        // GET: /api/admin/tournaments?status=OPEN&page=1&pageSize=20
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 50;
            if (pageSize > 200) pageSize = 200;

            status = status?.Trim();

            var q = _db.Tournaments.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
                q = q.Where(t => t.Status == status);

            var total = await q.CountAsync();

            // Query raw (lấy BannerUrl relative từ DB), rồi map sang absolute khi trả
            var raw = await q
                .OrderByDescending(t => t.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new
                {
                    t.TournamentId,
                    t.Title,
                    t.Status,
                    t.StartTime,
                    t.RegisterDeadline,
                    t.GameType,
                    t.ExpectedTeams,
                    t.LocationText,
                    t.AreaText,
                    t.BannerUrl, // relative
                    t.CreatedAt,
                    t.SingleLimit,
                    t.DoubleLimit
                })
                .ToListAsync();

            var items = raw.Select(t => new TournamentListItemDto
            {
                TournamentId = t.TournamentId,
                Title = t.Title,
                Status = t.Status,
                StartTime = t.StartTime,
                RegisterDeadline = t.RegisterDeadline,
                GameType = t.GameType,
                ExpectedTeams = t.ExpectedTeams,
                LocationText = t.LocationText,
                AreaText = t.AreaText,
                BannerUrl = ToAbsoluteUrl(t.BannerUrl), // trả absolute
                CreatedAt = t.CreatedAt,
                SingleLimit = t.SingleLimit,
                DoubleLimit = t.DoubleLimit
            });

            return Ok(new { page, pageSize, total, items });
        }

        // POST: /api/admin/tournaments (multipart/form-data)
        [HttpPost]
        [RequestSizeLimit(10_000_000)]
        public async Task<IActionResult> Create([FromForm] CreateTournamentRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Title))
                return BadRequest(new { message = "Title is required." });

            if (string.IsNullOrWhiteSpace(req.GameType))
                return BadRequest(new { message = "GameType is required (SINGLE/DOUBLE/MIXED)." });

            var status = string.IsNullOrWhiteSpace(req.Status) ? "DRAFT" : req.Status.Trim().ToUpperInvariant();

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

                // LƯU DB DẠNG RELATIVE
                bannerRelativeUrl = $"/uploads/tournaments/{fileName}";
            }

            var t = new HanakaServer.Models.Tournament
            {
                Status = status,
                Title = req.Title.Trim(),
                BannerUrl = bannerRelativeUrl, // relative
                StartTime = req.StartTime,
                RegisterDeadline = req.RegisterDeadline,
                GameType = req.GameType.Trim().ToUpperInvariant(),
                ExpectedTeams = req.ExpectedTeams ?? 0,
                LocationText = req.LocationText?.Trim(),
                AreaText = req.AreaText?.Trim(),
                SingleLimit = req.SingleLimit ?? 0,
                DoubleLimit = req.DoubleLimit ?? 0,
                Content = req.Content,
                CreatedAt = DateTime.UtcNow,

                FormatText = req.FormatText,
                PlayoffType = req.PlayoffType,
                StatusText = req.StatusText,
                StateText = req.StateText,
                Organizer = req.Organizer,
                CreatorName = req.CreatorName,
            };

            _db.Tournaments.Add(t);
            await _db.SaveChangesAsync();

            return Ok(new TournamentListItemDto
            {
                TournamentId = t.TournamentId,
                Title = t.Title,
                Status = t.Status,
                StartTime = t.StartTime,
                RegisterDeadline = t.RegisterDeadline,
                GameType = t.GameType,
                ExpectedTeams = t.ExpectedTeams,
                LocationText = t.LocationText,
                AreaText = t.AreaText,
                BannerUrl = ToAbsoluteUrl(t.BannerUrl), // trả absolute
                CreatedAt = t.CreatedAt,
                SingleLimit = t.SingleLimit,
                DoubleLimit = t.DoubleLimit,
            });
        }

        // GET: /api/admin/tournaments/{id}
        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetDetail([FromRoute] long id)
        {
            var t = await _db.Tournaments.AsNoTracking().FirstOrDefaultAsync(x => x.TournamentId == id);
            if (t == null) return NotFound(new { message = "Tournament not found." });

            return Ok(new TournamentListItemDto
            {
                TournamentId = t.TournamentId,
                Title = t.Title,
                Status = t.Status,
                StartTime = t.StartTime,
                RegisterDeadline = t.RegisterDeadline,
                GameType = t.GameType,
                ExpectedTeams = t.ExpectedTeams,
                LocationText = t.LocationText,
                AreaText = t.AreaText,
                BannerUrl = ToAbsoluteUrl(t.BannerUrl), // trả absolute
                CreatedAt = t.CreatedAt,
                SingleLimit = t.SingleLimit,
                DoubleLimit = t.DoubleLimit,
            });
        }

        // PUT: /api/admin/tournaments/{id} (multipart/form-data)
        [HttpPut("{id:long}")]
        [RequestSizeLimit(10_000_000)]
        public async Task<IActionResult> Update([FromRoute] long id, [FromForm] UpdateTournamentRequest req)
        {
            var t = await _db.Tournaments.FirstOrDefaultAsync(x => x.TournamentId == id);
            if (t == null) return NotFound(new { message = "Tournament not found." });

            if (!string.IsNullOrWhiteSpace(req.Title))
                t.Title = req.Title.Trim();

            if (!string.IsNullOrWhiteSpace(req.Status))
                t.Status = req.Status.Trim().ToUpperInvariant();

            if (!string.IsNullOrWhiteSpace(req.GameType))
                t.GameType = req.GameType.Trim().ToUpperInvariant();

            if (req.ExpectedTeams.HasValue)
                t.ExpectedTeams = req.ExpectedTeams.Value;

            if (req.StartTime.HasValue)
                t.StartTime = req.StartTime.Value;

            if (req.RegisterDeadline.HasValue)
                t.RegisterDeadline = req.RegisterDeadline.Value;

            if (req.LocationText != null)
                t.LocationText = string.IsNullOrWhiteSpace(req.LocationText) ? null : req.LocationText.Trim();

            if (req.AreaText != null)
                t.AreaText = string.IsNullOrWhiteSpace(req.AreaText) ? null : req.AreaText.Trim();

            if (req.SingleLimit.HasValue) t.SingleLimit = req.SingleLimit.Value;
            if (req.DoubleLimit.HasValue) t.DoubleLimit = req.DoubleLimit.Value;
            if (req.Content != null) t.Content = req.Content;

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

                // LƯU DB DẠNG RELATIVE
                t.BannerUrl = $"/uploads/tournaments/{fileName}";
            }
            else
            {
                // Nếu nơi nào đó từng lưu absolute trong DB hoặc bạn update từ nguồn khác:
                // đảm bảo normalize lại (an toàn, không bắt buộc)
                t.BannerUrl = NormalizeToRelative(t.BannerUrl);
            }

            await _db.SaveChangesAsync();

            return Ok(new TournamentListItemDto
            {
                TournamentId = t.TournamentId,
                Title = t.Title,
                Status = t.Status,
                StartTime = t.StartTime,
                RegisterDeadline = t.RegisterDeadline,
                GameType = t.GameType,
                ExpectedTeams = t.ExpectedTeams,
                LocationText = t.LocationText,
                AreaText = t.AreaText,
                BannerUrl = ToAbsoluteUrl(t.BannerUrl), // trả absolute
                CreatedAt = t.CreatedAt,
                SingleLimit = t.SingleLimit,
                DoubleLimit = t.DoubleLimit,
            });
        }
    }
}