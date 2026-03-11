using HanakaServer.Data;
using HanakaServer.Dtos;
using HanakaServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HanakaServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ClubsController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;

        public ClubsController(
            PickleballDbContext db,
            IWebHostEnvironment env,
            IConfiguration config)
        {
            _db = db;
            _env = env;
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

        private string? NormalizeToRelative(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            url = url.Trim();

            if (url.StartsWith("/")) return url;

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return uri.PathAndQuery;
            }

            return url;
        }

        private static string BuildAreaText(string? province, string? district, string? address)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(address))
                parts.Add(address.Trim());

            if (!string.IsNullOrWhiteSpace(district))
                parts.Add(district.Trim());

            if (!string.IsNullOrWhiteSpace(province))
                parts.Add(province.Trim());

            return string.Join(", ", parts);
        }

        private async Task<bool> IsClubOwner(long clubId, long userId)
        {
            return await _db.Clubs.AnyAsync(x => x.ClubId == clubId && x.OwId == userId);
        }

        // =========================================================
        // POST: api/clubs
        [Authorize(AuthenticationSchemes = "Bearer")]
        [HttpPost]
        public async Task<IActionResult> CreateClub([FromBody] CreateClubRequestDto req)
        {
            var userId = GetUserIdFromToken();

            if (req == null)
                return BadRequest(new { message = "Invalid request." });

            if (string.IsNullOrWhiteSpace(req.ClubName))
                return BadRequest(new { message = "ClubName is required." });

            var clubName = req.ClubName.Trim();
            if (clubName.Length < 2)
                return BadRequest(new { message = "ClubName must be at least 2 characters." });

            var owner = await _db.Users.FirstOrDefaultAsync(x => x.UserId == userId && x.IsActive);
            if (owner == null)
                return NotFound(new { message = "Owner user not found." });

            var areaText = BuildAreaText(req.Province, req.District, req.Address);

            var club = new Club
            {
                ClubName = clubName,
                AreaText = string.IsNullOrWhiteSpace(areaText) ? null : areaText,
                CoverUrl = NormalizeToRelative(req.CoverUrl),
                OwId = userId,
                IsActive = true,
                RatingAvg = 0,
                ReviewsCount = 0,
                MatchesPlayed = 0,
                MatchesWin = 0,
                MatchesDraw = 0,
                MatchesLoss = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = null
            };

            _db.Clubs.Add(club);
            await _db.SaveChangesAsync();

            var ownerMember = new ClubMember
            {
                ClubId = club.ClubId,
                UserId = userId,
                MemberRole = "OWNER",
                JoinedAt = DateTime.UtcNow,
                IsActive = true
            };

            _db.ClubMembers.Add(ownerMember);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "Tạo CLB thành công.",
                clubId = club.ClubId,
                clubName = club.ClubName,
                areaText = club.AreaText,
                coverUrl = ToAbsoluteUrl(club.CoverUrl),
                owner = new
                {
                    userId = owner.UserId,
                    fullName = owner.FullName
                },
                isActive = club.IsActive,
                ratingAvg = club.RatingAvg,
                reviewsCount = club.ReviewsCount,
                matchesPlayed = club.MatchesPlayed,
                matchesWin = club.MatchesWin,
                matchesDraw = club.MatchesDraw,
                matchesLoss = club.MatchesLoss,
                createdAt = club.CreatedAt,
                extra = new
                {
                    description = req.Description,
                    foundedDate = req.FoundedDate,
                    playTime = req.PlayTime,
                    avatarUrl = req.AvatarUrl
                }
            });
        }

        // =========================================================
        // POST: api/clubs/cover
        [Authorize(AuthenticationSchemes = "Bearer")]
        [HttpPost("cover")]
        [RequestSizeLimit(10_000_000)]
        public async Task<IActionResult> UploadClubCover([FromForm] IFormFile file)
        {
            var userId = GetUserIdFromToken();

            var owner = await _db.Users.FirstOrDefaultAsync(x => x.UserId == userId && x.IsActive);
            if (owner == null)
                return NotFound(new { message = "User not found." });

            if (file == null || file.Length == 0)
                return BadRequest(new { message = "File is required." });

            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowed.Contains(ext))
                return BadRequest(new { message = "Only jpg, jpeg, png, webp are allowed." });

            var uploadsDir = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", "clubs");
            Directory.CreateDirectory(uploadsDir);

            var fileName = $"club_cover_{userId}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);

            await using (var stream = System.IO.File.Create(filePath))
            {
                await file.CopyToAsync(stream);
            }

            var relativeUrl = $"/uploads/clubs/{fileName}";

            return Ok(new
            {
                coverUrl = ToAbsoluteUrl(relativeUrl),
                relativeUrl
            });
        }

        // =========================================================
        // GET: api/clubs?keyword=&page=1&pageSize=10
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> GetClubs(
            [FromQuery] string? keyword,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 100) pageSize = 100;

            var q = _db.Clubs
                .AsNoTracking()
                .Where(x => x.IsActive);

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var k = keyword.Trim();
                q = q.Where(x =>
                    x.ClubName.Contains(k) ||
                    (x.AreaText != null && x.AreaText.Contains(k)));
            }

            var total = await q.CountAsync();

            var items = await q
                .OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    clubId = x.ClubId,
                    clubName = x.ClubName,
                    areaText = x.AreaText,
                    coverUrl = x.CoverUrl,
                    ratingAvg = x.RatingAvg,
                    reviewsCount = x.ReviewsCount,
                    matchesPlayed = x.MatchesPlayed,
                    matchesWin = x.MatchesWin,
                    matchesDraw = x.MatchesDraw,
                    matchesLoss = x.MatchesLoss,
                    owId = x.OwId,
                    createdAt = x.CreatedAt,
                    membersCount = x.ClubMembers.Count(cm => cm.IsActive)
                })
                .ToListAsync();

            var mappedItems = items.Select(x => new
            {
                x.clubId,
                x.clubName,
                x.areaText,
                coverUrl = ToAbsoluteUrl(x.coverUrl),
                x.ratingAvg,
                x.reviewsCount,
                x.matchesPlayed,
                x.matchesWin,
                x.matchesDraw,
                x.matchesLoss,
                x.owId,
                x.createdAt,
                x.membersCount
            });

            return Ok(new
            {
                total,
                page,
                pageSize,
                items = mappedItems
            });
        }

        // =========================================================
        // GET: api/clubs/my
        [Authorize(AuthenticationSchemes = "Bearer")]
        [HttpGet("my")]
        public async Task<IActionResult> GetMyClubs()
        {
            var userId = GetUserIdFromToken();

            var items = await _db.Clubs
                .AsNoTracking()
                .Where(x => x.OwId == userId && x.IsActive)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new
                {
                    x.ClubId,
                    x.ClubName,
                    x.AreaText,
                    x.CoverUrl,
                    x.RatingAvg,
                    x.ReviewsCount,
                    x.MatchesPlayed,
                    x.MatchesWin,
                    x.MatchesDraw,
                    x.MatchesLoss,
                    x.CreatedAt,
                    membersCount = x.ClubMembers.Count(cm => cm.IsActive)
                })
                .ToListAsync();

            return Ok(items.Select(x => new
            {
                x.ClubId,
                x.ClubName,
                x.AreaText,
                coverUrl = ToAbsoluteUrl(x.CoverUrl),
                x.RatingAvg,
                x.ReviewsCount,
                x.MatchesPlayed,
                x.MatchesWin,
                x.MatchesDraw,
                x.MatchesLoss,
                x.CreatedAt,
                x.membersCount
            }));
        }

        // =========================================================
        // GET: api/clubs/{id}
        [AllowAnonymous]
        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetClubDetail(long id)
        {
            var club = await _db.Clubs
                .AsNoTracking()
                .Where(x => x.ClubId == id && x.IsActive)
                .Select(x => new
                {
                    x.ClubId,
                    x.ClubName,
                    x.AreaText,
                    x.CoverUrl,
                    x.RatingAvg,
                    x.ReviewsCount,
                    x.MatchesPlayed,
                    x.MatchesWin,
                    x.MatchesDraw,
                    x.MatchesLoss,
                    x.OwId,
                    x.CreatedAt,
                    membersCount = x.ClubMembers.Count(cm => cm.IsActive),
                    pendingMembersCount = x.ClubMembers.Count(cm => !cm.IsActive)
                })
                .FirstOrDefaultAsync();

            if (club == null)
                return NotFound(new { message = "Club not found." });

            var owner = await _db.Users
                .AsNoTracking()
                .Where(x => x.UserId == club.OwId)
                .Select(x => new
                {
                    x.UserId,
                    x.FullName,
                    x.AvatarUrl
                })
                .FirstOrDefaultAsync();

            return Ok(new
            {
                club.ClubId,
                club.ClubName,
                club.AreaText,
                coverUrl = ToAbsoluteUrl(club.CoverUrl),
                club.RatingAvg,
                club.ReviewsCount,
                club.MatchesPlayed,
                club.MatchesWin,
                club.MatchesDraw,
                club.MatchesLoss,
                club.CreatedAt,
                club.membersCount,
                club.pendingMembersCount,
                owner = owner == null ? null : new
                {
                    owner.UserId,
                    owner.FullName,
                    avatarUrl = ToAbsoluteUrl(owner.AvatarUrl)
                }
            });
        }

        // =========================================================
        // GET: api/clubs/{id}/overview
        [AllowAnonymous]
        [HttpGet("{id:long}/overview")]
        public async Task<IActionResult> GetClubOverview(long id)
        {
            var club = await _db.Clubs
                .AsNoTracking()
                .Where(x => x.ClubId == id && x.IsActive)
                .Select(x => new
                {
                    clubId = x.ClubId,
                    clubName = x.ClubName,
                    areaText = x.AreaText,
                    coverUrl = x.CoverUrl,
                    ratingAvg = x.RatingAvg,
                    reviewsCount = x.ReviewsCount,
                    matchesPlayed = x.MatchesPlayed,
                    matchesWin = x.MatchesWin,
                    matchesDraw = x.MatchesDraw,
                    matchesLoss = x.MatchesLoss,
                    createdAt = x.CreatedAt,
                    owId = x.OwId,
                    membersCount = x.ClubMembers.Count(cm => cm.IsActive),
                    pendingMembersCount = x.ClubMembers.Count(cm => !cm.IsActive)
                })
                .FirstOrDefaultAsync();

            if (club == null)
                return NotFound(new { message = "Club not found." });

            var owner = await _db.Users
                .AsNoTracking()
                .Where(x => x.UserId == club.owId)
                .Select(x => new
                {
                    userId = x.UserId,
                    fullName = x.FullName,
                    avatarUrl = x.AvatarUrl,
                    ratingSingle = x.RatingSingle,
                    ratingDouble = x.RatingDouble
                })
                .FirstOrDefaultAsync();

            return Ok(new
            {
                club.clubId,
                club.clubName,
                club.areaText,
                coverUrl = ToAbsoluteUrl(club.coverUrl),
                club.ratingAvg,
                club.reviewsCount,
                club.matchesPlayed,
                club.matchesWin,
                club.matchesDraw,
                club.matchesLoss,
                club.createdAt,
                club.membersCount,
                club.pendingMembersCount,

                // vì model chưa có description/foundedDate/playTime/address riêng
                // tạm dùng thông tin khả dụng hiện tại
                overview = new
                {
                    title = club.clubName,
                    introduction = $"CLB {club.clubName} được thành lập và đang hoạt động tại {club.areaText ?? "chưa cập nhật khu vực"}.",
                    foundedAt = club.createdAt,
                    addressText = club.areaText,
                    level = owner?.ratingDouble ?? owner?.ratingSingle ?? 0m
                },

                owner = owner == null ? null : new
                {
                    owner.userId,
                    owner.fullName,
                    avatarUrl = ToAbsoluteUrl(owner.avatarUrl),
                    owner.ratingSingle,
                    owner.ratingDouble
                }
            });
        }

        // =========================================================
        // GET: api/clubs/{id}/members?page=1&pageSize=20
        [AllowAnonymous]
        [HttpGet("{id:long}/members")]
        public async Task<IActionResult> GetClubMembers(
            long id,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var clubExists = await _db.Clubs.AnyAsync(x => x.ClubId == id && x.IsActive);
            if (!clubExists)
                return NotFound(new { message = "Club not found." });

            var q = _db.ClubMembers
                .AsNoTracking()
                .Where(x => x.ClubId == id && x.IsActive);

            var total = await q.CountAsync();

            var items = await q
                .OrderByDescending(x => x.MemberRole == "OWNER")
                .ThenBy(x => x.User.FullName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    userId = x.UserId,
                    fullName = x.User.FullName,
                    avatarUrl = x.User.AvatarUrl,
                    city = x.User.City,
                    gender = x.User.Gender,
                    verified = x.User.Verified,
                    ratingSingle = x.User.RatingSingle,
                    ratingDouble = x.User.RatingDouble,
                    memberRole = x.MemberRole,
                    joinedAt = x.JoinedAt
                })
                .ToListAsync();

            return Ok(new
            {
                clubId = id,
                total,
                page,
                pageSize,
                items = items.Select(x => new
                {
                    x.userId,
                    x.fullName,
                    avatarUrl = ToAbsoluteUrl(x.avatarUrl),
                    x.city,
                    x.gender,
                    x.verified,
                    x.ratingSingle,
                    x.ratingDouble,
                    x.memberRole,
                    x.joinedAt
                })
            });
        }

        // =========================================================
        // GET: api/clubs/{id}/pending-members?page=1&pageSize=20
        // Chỉ owner xem được
        [Authorize(AuthenticationSchemes = "Bearer")]
        [HttpGet("{id:long}/pending-members")]
        public async Task<IActionResult> GetPendingClubMembers(
            long id,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var userId = GetUserIdFromToken();

            var club = await _db.Clubs
                .AsNoTracking()
                .Where(x => x.ClubId == id && x.IsActive)
                .Select(x => new
                {
                    x.ClubId,
                    x.OwId
                })
                .FirstOrDefaultAsync();

            if (club == null)
                return NotFound(new { message = "Club not found." });

            if (club.OwId != userId)
                return Forbid();

            var q = _db.ClubMembers
                .AsNoTracking()
                .Where(x => x.ClubId == id && !x.IsActive);

            var total = await q.CountAsync();

            var items = await q
                .OrderByDescending(x => x.JoinedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    userId = x.UserId,
                    fullName = x.User.FullName,
                    avatarUrl = x.User.AvatarUrl,
                    city = x.User.City,
                    gender = x.User.Gender,
                    verified = x.User.Verified,
                    ratingSingle = x.User.RatingSingle,
                    ratingDouble = x.User.RatingDouble,
                    memberRole = x.MemberRole,
                    joinedAt = x.JoinedAt
                })
                .ToListAsync();

            return Ok(new
            {
                clubId = id,
                total,
                page,
                pageSize,
                items = items.Select(x => new
                {
                    x.userId,
                    x.fullName,
                    avatarUrl = ToAbsoluteUrl(x.avatarUrl),
                    x.city,
                    x.gender,
                    x.verified,
                    x.ratingSingle,
                    x.ratingDouble,
                    x.memberRole,
                    x.joinedAt
                })
            });
        }
    }
}