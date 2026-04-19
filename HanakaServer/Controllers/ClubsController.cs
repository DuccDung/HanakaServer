using HanakaServer.Data;
using HanakaServer.Dtos;
using HanakaServer.Models;
using HanakaServer.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

        private readonly RealtimeHub _realtimeHub;

        public ClubsController(
            PickleballDbContext db,
            IWebHostEnvironment env,
            IConfiguration config,
            RealtimeHub realtimeHub)
        {
            _db = db;
            _env = env;
            _config = config;
            _realtimeHub = realtimeHub;
        }

        private long GetUserIdFromToken()
        {
            var uid = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid) || !long.TryParse(uid, out var userId))
                throw new UnauthorizedAccessException("Invalid token: missing uid.");
            return userId;
        }

        private async Task<long?> TryGetUserIdFromToken()
        {
            var uid = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(uid) && long.TryParse(uid, out var parsedUserId))
                return parsedUserId;

            var authResult = await HttpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);

            if (!authResult.Succeeded || authResult.Principal == null)
                return null;

            var principalUid =
                authResult.Principal.FindFirstValue("uid") ??
                authResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(principalUid) || !long.TryParse(principalUid, out var userId))
                return null;

            return userId;
        }

        private string? ToAbsoluteUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            var trimmed = url.Trim();
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri.ToString();
            }

            if (!trimmed.StartsWith("/"))
            {
                trimmed = "/" + trimmed;
            }

            if (Request?.Host.HasValue == true)
            {
                var pathBase = Request.PathBase.HasValue ? Request.PathBase.Value : string.Empty;
                return $"{Request.Scheme}://{Request.Host}{pathBase}{trimmed}";
            }

            var baseUrl = _config["PublicBaseUrl"]?.TrimEnd('/');
            return string.IsNullOrWhiteSpace(baseUrl) ? trimmed : $"{baseUrl}{trimmed}";
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

        private static readonly string[] UnsafeChatTerms =
        {
            "fuck",
            "shit",
            "bitch",
            "asshole",
            "motherfucker",
            "kill",
            "rape",
            "sex",
            "nude",
            "xxx"
        };

        private static bool ContainsObjectionableChatContent(string? content)
        {
            if (string.IsNullOrWhiteSpace(content)) return false;

            return UnsafeChatTerms.Any(term =>
                content.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        private async Task<bool> IsClubOwner(long clubId, long userId)
        {
            return await _db.ClubMembers.AnyAsync(x =>
                x.ClubId == clubId &&
                x.UserId == userId &&
                x.IsActive &&
                x.MemberRole == "OWNER");
        }

        private async Task<(string MyClubStatus, string? MyMemberRole, bool CanManage)> GetMyClubRelation(long clubId, long? userId)
        {
            if (!userId.HasValue)
                return ("NONE", null, false);

            var member = await _db.ClubMembers
                .AsNoTracking()
                .Where(x => x.ClubId == clubId && x.UserId == userId.Value)
                .Select(x => new
                {
                    x.MemberRole,
                    x.IsActive
                })
                .FirstOrDefaultAsync();

            if (member == null)
                return ("NONE", null, false);

            if (!member.IsActive)
                return ("PENDING", member.MemberRole, false);

            if (member.MemberRole == "OWNER")
                return ("MANAGER", member.MemberRole, true);

            return ("MEMBER", member.MemberRole, false);
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
                AllowChallenge = false,
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
                allowChallenge = club.AllowChallenge,
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
                createdAt = club.CreatedAt
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

            var currentUserId = await TryGetUserIdFromToken();

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
                    allowChallenge = x.AllowChallenge,
                    owId = x.OwId,
                    createdAt = x.CreatedAt,
                    membersCount = x.ClubMembers.Count(cm => cm.IsActive)
                })
                .ToListAsync();

            var result = new List<object>();

            foreach (var item in items)
            {
                var relation = await GetMyClubRelation(item.clubId, currentUserId);

                result.Add(new
                {
                    item.clubId,
                    item.clubName,
                    item.areaText,
                    coverUrl = ToAbsoluteUrl(item.coverUrl),
                    item.ratingAvg,
                    item.reviewsCount,
                    item.matchesPlayed,
                    item.matchesWin,
                    item.matchesDraw,
                    item.matchesLoss,
                    item.allowChallenge,
                    item.owId,
                    item.createdAt,
                    item.membersCount,
                    myClubStatus = relation.MyClubStatus,
                    myMemberRole = relation.MyMemberRole,
                    canManage = relation.CanManage
                });
            }

            return Ok(new
            {
                total,
                page,
                pageSize,
                items = result
            });
        }

        // =========================================================
        // POST: api/clubs/{id}/join
        [Authorize(AuthenticationSchemes = "Bearer")]
        [HttpPost("{id:long}/join")]
        public async Task<IActionResult> JoinClub(long id)
        {
            var userId = GetUserIdFromToken();

            var club = await _db.Clubs.FirstOrDefaultAsync(x => x.ClubId == id && x.IsActive);
            if (club == null)
                return NotFound(new { message = "Club not found." });

            var existing = await _db.ClubMembers
                .FirstOrDefaultAsync(x => x.ClubId == id && x.UserId == userId);

            if (existing != null)
            {
                if (existing.IsActive)
                {
                    return BadRequest(new
                    {
                        message = existing.MemberRole == "OWNER"
                            ? "Bạn là chủ CLB."
                            : "Bạn đã là thành viên của CLB.",
                        myClubStatus = existing.MemberRole == "OWNER" ? "MANAGER" : "MEMBER"
                    });
                }

                return BadRequest(new
                {
                    message = "Yêu cầu tham gia của bạn đang chờ duyệt.",
                    myClubStatus = "PENDING"
                });
            }

            var member = new ClubMember
            {
                ClubId = id,
                UserId = userId,
                MemberRole = "MEMBER",
                JoinedAt = DateTime.UtcNow,
                IsActive = false
            };

            _db.ClubMembers.Add(member);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "Đã gửi yêu cầu tham gia CLB. Vui lòng chờ duyệt.",
                myClubStatus = "PENDING"
            });
        }

        // =========================================================
        // GET: api/clubs/{id}
        [AllowAnonymous]
        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetClubDetail(long id)
        {
            var currentUserId = await TryGetUserIdFromToken();

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
                    x.AllowChallenge,
                    x.OwId,
                    x.CreatedAt,
                    membersCount = x.ClubMembers.Count(cm => cm.IsActive),
                    pendingMembersCount = x.ClubMembers.Count(cm => !cm.IsActive)
                })
                .FirstOrDefaultAsync();

            if (club == null)
                return NotFound(new { message = "Club not found." });

            var owner = await _db.ClubMembers
                .AsNoTracking()
                .Where(x => x.ClubId == id && x.IsActive && x.MemberRole == "OWNER")
                .Select(x => new
                {
                    x.User.UserId,
                    x.User.FullName,
                    x.User.AvatarUrl
                })
                .FirstOrDefaultAsync();

            var relation = await GetMyClubRelation(club.ClubId, currentUserId);

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
                allowChallenge = club.AllowChallenge,
                club.CreatedAt,
                club.membersCount,
                club.pendingMembersCount,
                myClubStatus = relation.MyClubStatus,
                myMemberRole = relation.MyMemberRole,
                canManage = relation.CanManage,
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
            var currentUserId = await TryGetUserIdFromToken();

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
                    allowChallenge = x.AllowChallenge,
                    createdAt = x.CreatedAt,
                    membersCount = x.ClubMembers.Count(cm => cm.IsActive),
                    pendingMembersCount = x.ClubMembers.Count(cm => !cm.IsActive)
                })
                .FirstOrDefaultAsync();

            if (club == null)
                return NotFound(new { message = "Club not found." });

            var owner = await _db.ClubMembers
                .AsNoTracking()
                .Where(x => x.ClubId == id && x.IsActive && x.MemberRole == "OWNER")
                .Select(x => new
                {
                    userId = x.User.UserId,
                    fullName = x.User.FullName,
                    avatarUrl = x.User.AvatarUrl,
                    latestRating = _db.UserRatingHistories
                        .Where(r => r.UserId == x.UserId)
                        .OrderByDescending(r => r.RatedAt)
                        .ThenByDescending(r => r.RatingHistoryId)
                        .Select(r => new
                        {
                            r.RatingSingle,
                            r.RatingDouble,
                            r.RatedAt
                        })
                        .FirstOrDefault()
                })
                .FirstOrDefaultAsync();

            var relation = await GetMyClubRelation(club.clubId, currentUserId);

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
                club.allowChallenge,
                club.createdAt,
                club.membersCount,
                club.pendingMembersCount,
                myClubStatus = relation.MyClubStatus,
                myMemberRole = relation.MyMemberRole,
                canManage = relation.CanManage,
                overview = new
                {
                    title = club.clubName,
                    introduction = $"CLB {club.clubName} được thành lập và đang hoạt động tại {club.areaText ?? "chưa cập nhật khu vực"}.",
                    foundedAt = club.createdAt,
                    addressText = club.areaText,
                    level = owner?.latestRating?.RatingDouble ?? owner?.latestRating?.RatingSingle ?? 0m
                },
                owner = owner == null ? null : new
                {
                    owner.userId,
                    owner.fullName,
                    avatarUrl = ToAbsoluteUrl(owner.avatarUrl),
                    ratingSingle = owner.latestRating != null ? owner.latestRating.RatingSingle : null,
                    ratingDouble = owner.latestRating != null ? owner.latestRating.RatingDouble : null,
                    ratingUpdatedAt = owner.latestRating?.RatedAt
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
                    memberRole = x.MemberRole,
                    joinedAt = x.JoinedAt,
                    latestRating = _db.UserRatingHistories
                        .Where(r => r.UserId == x.UserId)
                        .OrderByDescending(r => r.RatedAt)
                        .ThenByDescending(r => r.RatingHistoryId)
                        .Select(r => new
                        {
                            r.RatingSingle,
                            r.RatingDouble,
                            r.RatedAt
                        })
                        .FirstOrDefault()
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
                    ratingSingle = x.latestRating != null ? x.latestRating.RatingSingle : null,
                    ratingDouble = x.latestRating != null ? x.latestRating.RatingDouble : null,
                    ratingUpdatedAt = x.latestRating?.RatedAt,
                    x.memberRole,
                    x.joinedAt
                })
            });
        }

        // =========================================================
        // GET: api/clubs/{id}/pending-members?page=1&pageSize=20
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

            var clubExists = await _db.Clubs.AnyAsync(x => x.ClubId == id && x.IsActive);
            if (!clubExists)
                return NotFound(new { message = "Club not found." });

            var isOwner = await IsClubOwner(id, userId);
            if (!isOwner)
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
                    memberRole = x.MemberRole,
                    joinedAt = x.JoinedAt,
                    latestRating = _db.UserRatingHistories
                        .Where(r => r.UserId == x.UserId)
                        .OrderByDescending(r => r.RatedAt)
                        .ThenByDescending(r => r.RatingHistoryId)
                        .Select(r => new
                        {
                            r.RatingSingle,
                            r.RatingDouble,
                            r.RatedAt
                        })
                        .FirstOrDefault()
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
                    ratingSingle = x.latestRating != null ? x.latestRating.RatingSingle : null,
                    ratingDouble = x.latestRating != null ? x.latestRating.RatingDouble : null,
                    ratingUpdatedAt = x.latestRating?.RatedAt,
                    x.memberRole,
                    x.joinedAt
                })
            });
        }

        // =========================================================
        // POST: api/clubs/{id}/pending-members/{memberUserId}/approve
        [Authorize(AuthenticationSchemes = "Bearer")]
        [HttpPost("{id:long}/pending-members/{memberUserId:long}/approve")]
        public async Task<IActionResult> ApprovePendingMember(long id, long memberUserId)
        {
            var userId = GetUserIdFromToken();

            var isOwner = await IsClubOwner(id, userId);
            if (!isOwner)
                return Forbid();

            var member = await _db.ClubMembers
                .FirstOrDefaultAsync(x => x.ClubId == id && x.UserId == memberUserId);

            if (member == null)
                return NotFound(new { message = "Member request not found." });

            if (member.IsActive)
                return BadRequest(new { message = "Member is already active." });

            member.IsActive = true;
            member.MemberRole = "MEMBER";

            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "Duyệt thành viên thành công.",
                clubId = id,
                userId = memberUserId,
                status = "MEMBER"
            });
        }

        // =========================================================
        // DELETE: api/clubs/{id}/pending-members/{memberUserId}
        [Authorize(AuthenticationSchemes = "Bearer")]
        [HttpDelete("{id:long}/pending-members/{memberUserId:long}")]
        public async Task<IActionResult> RejectPendingMember(long id, long memberUserId)
        {
            var userId = GetUserIdFromToken();

            var isOwner = await IsClubOwner(id, userId);
            if (!isOwner)
                return Forbid();

            var member = await _db.ClubMembers
                .FirstOrDefaultAsync(x => x.ClubId == id && x.UserId == memberUserId && !x.IsActive);

            if (member == null)
                return NotFound(new { message = "Pending member not found." });

            _db.ClubMembers.Remove(member);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "Đã từ chối yêu cầu tham gia.",
                clubId = id,
                userId = memberUserId
            });
        }

        // =========================================================
        // DELETE: api/clubs/{id}/members/{memberUserId}
        [Authorize(AuthenticationSchemes = "Bearer")]
        [HttpDelete("{id:long}/members/{memberUserId:long}")]
        public async Task<IActionResult> RemoveMember(long id, long memberUserId)
        {
            var userId = GetUserIdFromToken();

            var isOwner = await IsClubOwner(id, userId);
            if (!isOwner)
                return Forbid();

            var member = await _db.ClubMembers
                .FirstOrDefaultAsync(x => x.ClubId == id && x.UserId == memberUserId && x.IsActive);

            if (member == null)
                return NotFound(new { message = "Member not found." });

            if (member.MemberRole == "OWNER")
                return BadRequest(new { message = "Không thể xóa chủ CLB." });

            _db.ClubMembers.Remove(member);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "Xóa thành viên thành công.",
                clubId = id,
                userId = memberUserId
            });
        }

        // =========================================================
        // PUT: api/clubs/{id}/challenge-mode
        [Authorize(AuthenticationSchemes = "Bearer")]
        [HttpPut("{id:long}/challenge-mode")]
        public async Task<IActionResult> UpdateChallengeMode(long id, [FromBody] UpdateClubChallengeRequestDto req)
        {
            var userId = GetUserIdFromToken();

            var club = await _db.Clubs.FirstOrDefaultAsync(x => x.ClubId == id && x.IsActive);
            if (club == null)
                return NotFound(new { message = "Club not found." });

            var isOwner = await IsClubOwner(id, userId);
            if (!isOwner)
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = "Chỉ OWNER mới được bật hoặc tắt chế độ khiêu chiến."
                });

            club.AllowChallenge = req.AllowChallenge;
            club.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = req.AllowChallenge
                    ? "Đã bật chế độ khiêu chiến cho CLB."
                    : "Đã tắt chế độ khiêu chiến cho CLB.",
                clubId = club.ClubId,
                allowChallenge = club.AllowChallenge,
                updatedAt = club.UpdatedAt
            });
        }
        // =========================================================
        // GET: api/clubs/challenging?keyword=&page=1&pageSize=10
        [AllowAnonymous]
        [HttpGet("challenging")]
        public async Task<IActionResult> GetChallengingClubs(
            [FromQuery] string? keyword,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 100) pageSize = 100;

            var currentUserId = await TryGetUserIdFromToken();

            var q = _db.Clubs
                .AsNoTracking()
                .Where(x => x.IsActive && x.AllowChallenge);

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var k = keyword.Trim();
                q = q.Where(x =>
                    x.ClubName.Contains(k) ||
                    (x.AreaText != null && x.AreaText.Contains(k)));
            }

            var total = await q.CountAsync();

            var items = await q
                .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
                .ThenByDescending(x => x.CreatedAt)
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
                    allowChallenge = x.AllowChallenge,
                    owId = x.OwId,
                    createdAt = x.CreatedAt,
                    updatedAt = x.UpdatedAt,
                    membersCount = x.ClubMembers.Count(cm => cm.IsActive)
                })
                .ToListAsync();

            var result = new List<object>();

            foreach (var item in items)
            {
                var relation = await GetMyClubRelation(item.clubId, currentUserId);

                result.Add(new
                {
                    item.clubId,
                    item.clubName,
                    item.areaText,
                    coverUrl = ToAbsoluteUrl(item.coverUrl),
                    item.ratingAvg,
                    item.reviewsCount,
                    item.matchesPlayed,
                    item.matchesWin,
                    item.matchesDraw,
                    item.matchesLoss,
                    item.allowChallenge,
                    item.owId,
                    item.createdAt,
                    item.updatedAt,
                    item.membersCount,
                    myClubStatus = relation.MyClubStatus,
                    myMemberRole = relation.MyMemberRole,
                    canManage = relation.CanManage
                });
            }

            return Ok(new
            {
                total,
                page,
                pageSize,
                items = result
            });
        }
        // =========================================================
        // GET: api/clubs/my?keyword=&status=ALL&page=1&pageSize=20
        // status:
        // - ALL       : tất cả club liên quan tới tôi
        // - MEMBER    : club tôi là thành viên đã duyệt
        // - MANAGER   : club tôi là OWNER / quản lý
        // - PENDING   : club tôi đã gửi yêu cầu tham gia nhưng chưa duyệt
        [Authorize(AuthenticationSchemes = "Bearer")]
        [HttpGet("my")]
        public async Task<IActionResult> GetMyClubs(
            [FromQuery] string? keyword,
            [FromQuery] string status = "ALL",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var userId = GetUserIdFromToken();

            status = (status ?? "ALL").Trim().ToUpperInvariant();
            var allowedStatus = new[] { "ALL", "MEMBER", "MANAGER", "PENDING" };
            if (!allowedStatus.Contains(status))
            {
                return BadRequest(new
                {
                    message = "status không hợp lệ. Chỉ nhận: ALL, MEMBER, MANAGER, PENDING."
                });
            }

            var q = _db.ClubMembers
                .AsNoTracking()
                .Where(x => x.UserId == userId && x.Club.IsActive);

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var k = keyword.Trim();
                q = q.Where(x =>
                    x.Club.ClubName.Contains(k) ||
                    (x.Club.AreaText != null && x.Club.AreaText.Contains(k)));
            }

            switch (status)
            {
                case "MEMBER":
                    q = q.Where(x => x.IsActive && x.MemberRole != "OWNER");
                    break;

                case "MANAGER":
                    q = q.Where(x => x.IsActive && x.MemberRole == "OWNER");
                    break;

                case "PENDING":
                    q = q.Where(x => !x.IsActive);
                    break;

                case "ALL":
                default:
                    break;
            }

            var total = await q.CountAsync();

            var items = await q
                .OrderByDescending(x => x.IsActive) // active trước
                .ThenByDescending(x => x.MemberRole == "OWNER") // OWNER trước
                .ThenByDescending(x => x.Club.UpdatedAt ?? x.Club.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    clubId = x.Club.ClubId,
                    clubName = x.Club.ClubName,
                    areaText = x.Club.AreaText,
                    coverUrl = x.Club.CoverUrl,
                    ratingAvg = x.Club.RatingAvg,
                    reviewsCount = x.Club.ReviewsCount,
                    matchesPlayed = x.Club.MatchesPlayed,
                    matchesWin = x.Club.MatchesWin,
                    matchesDraw = x.Club.MatchesDraw,
                    matchesLoss = x.Club.MatchesLoss,
                    allowChallenge = x.Club.AllowChallenge,
                    createdAt = x.Club.CreatedAt,
                    updatedAt = x.Club.UpdatedAt,

                    myMemberRole = x.MemberRole,
                    isMembershipActive = x.IsActive,
                    joinedAt = x.JoinedAt,

                    membersCount = x.Club.ClubMembers.Count(cm => cm.IsActive),

                    owner = x.Club.ClubMembers
                        .Where(cm => cm.IsActive && cm.MemberRole == "OWNER")
                        .Select(cm => new
                        {
                            userId = cm.User.UserId,
                            fullName = cm.User.FullName,
                            avatarUrl = cm.User.AvatarUrl
                        })
                        .FirstOrDefault()
                })
                .ToListAsync();

            var result = items.Select(x =>
            {
                string myClubStatus;
                bool canManage = false;

                if (!x.isMembershipActive)
                {
                    myClubStatus = "PENDING";
                }
                else if (x.myMemberRole == "OWNER")
                {
                    myClubStatus = "MANAGER";
                    canManage = true;
                }
                else
                {
                    myClubStatus = "MEMBER";
                }

                return new
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
                    x.allowChallenge,
                    x.createdAt,
                    x.updatedAt,
                    x.joinedAt,
                    x.membersCount,
                    myClubStatus,
                    myMemberRole = x.myMemberRole,
                    canManage,
                    owner = x.owner == null ? null : new
                    {
                        x.owner.userId,
                        x.owner.fullName,
                        avatarUrl = ToAbsoluteUrl(x.owner.avatarUrl)
                    }
                };
            });

            var summaryBase = await _db.ClubMembers
                .AsNoTracking()
                .Where(x => x.UserId == userId && x.Club.IsActive)
                .GroupBy(x => 1)
                .Select(g => new
                {
                    totalAll = g.Count(),
                    totalManager = g.Count(x => x.IsActive && x.MemberRole == "OWNER"),
                    totalMember = g.Count(x => x.IsActive && x.MemberRole != "OWNER"),
                    totalPending = g.Count(x => !x.IsActive)
                })
                .FirstOrDefaultAsync();

            return Ok(new
            {
                total,
                page,
                pageSize,
                filter = status,
                summary = new
                {
                    totalAll = summaryBase?.totalAll ?? 0,
                    totalManager = summaryBase?.totalManager ?? 0,
                    totalMember = summaryBase?.totalMember ?? 0,
                    totalPending = summaryBase?.totalPending ?? 0
                },
                items = result
            });
        }
        // =========================================================
        // GET: api/clubs/chat-rooms?page=1&pageSize=20
        [Authorize(AuthenticationSchemes = "Bearer")]
        [HttpGet("chat-rooms")]
        public async Task<IActionResult> GetMyClubChatRooms(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var userId = GetUserIdFromToken();

            var memberClubIds = await _db.ClubMembers
                .AsNoTracking()
                .Where(x => x.UserId == userId && x.IsActive && x.Club.IsActive)
                .Select(x => x.ClubId)
                .ToListAsync();

            var q = _db.Clubs
                .AsNoTracking()
                .Where(x => memberClubIds.Contains(x.ClubId));

            var total = await q.CountAsync();

            var clubs = await q
                .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.ClubId,
                    x.ClubName,
                    x.CoverUrl,
                    x.AreaText,
                    membersCount = x.ClubMembers.Count(cm => cm.IsActive),
                    lastMessage = x.ClubMessages
                        .Where(m =>
                            !m.IsDeleted &&
                            !_db.UserBlocks.Any(ub =>
                                ub.BlockerUserId == userId &&
                                ub.BlockedUserId == m.SenderUserId &&
                                ub.IsActive))
                        .OrderByDescending(m => m.SentAt)
                        .Select(m => new
                        {
                            m.MessageId,
                            m.MessageType,
                            m.Content,
                            m.MediaUrl,
                            m.SentAt,
                            m.SenderUserId,
                            senderName = m.SenderUser.FullName,
                            senderAvatarUrl = m.SenderUser.AvatarUrl
                        })
                        .FirstOrDefault()
                })
                .ToListAsync();

            var items = clubs
                .Select(x => new
                {
                    clubId = x.ClubId,
                    clubName = x.ClubName,
                    clubCoverUrl = ToAbsoluteUrl(x.CoverUrl),
                    areaText = x.AreaText,
                    x.membersCount,
                    lastMessagePreview = x.lastMessage == null
                        ? null
                        : x.lastMessage.MessageType == "TEXT"
                            ? x.lastMessage.Content
                            : "[Tệp đính kèm]",
                    lastMessageType = x.lastMessage?.MessageType,
                    lastMessageAt = x.lastMessage?.SentAt,
                    lastSenderUserId = x.lastMessage?.SenderUserId,
                    lastSenderName = x.lastMessage?.senderName,
                    lastSenderAvatarUrl = ToAbsoluteUrl(x.lastMessage?.senderAvatarUrl)
                })
                .OrderByDescending(x => x.lastMessageAt ?? DateTime.MinValue)
                .ThenBy(x => x.clubName)
                .ToList();

            return Ok(new
            {
                total,
                page,
                pageSize,
                items
            });
        }
        // =========================================================
        // GET: api/clubs/{id}/messages?page=1&pageSize=30
        [Authorize(AuthenticationSchemes = "Bearer")]
        [HttpGet("{id:long}/messages")]
        public async Task<IActionResult> GetClubMessages(
            long id,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 30)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 30;
            if (pageSize > 100) pageSize = 100;

            var userId = GetUserIdFromToken();

            var clubExists = await _db.Clubs.AnyAsync(x => x.ClubId == id && x.IsActive);
            if (!clubExists)
                return NotFound(new { message = "Club not found." });

            var isMember = await IsActiveClubMember(id, userId);
            if (!isMember)
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = "Bạn phải là thành viên CLB mới xem được tin nhắn."
                });

            var q = _db.ClubMessages
                .AsNoTracking()
                .Where(x =>
                    x.ClubId == id &&
                    !x.IsDeleted &&
                    !_db.UserBlocks.Any(ub =>
                        ub.BlockerUserId == userId &&
                        ub.BlockedUserId == x.SenderUserId &&
                        ub.IsActive));

            var total = await q.CountAsync();

            var items = await q
                .OrderByDescending(x => x.SentAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.MessageId,
                    x.ClubId,
                    x.SenderUserId,
                    x.MessageType,
                    x.Content,
                    x.MediaUrl,
                    x.ReplyToId,
                    x.SentAt,
                    sender = new
                    {
                        userId = x.SenderUser.UserId,
                        fullName = x.SenderUser.FullName,
                        avatarUrl = x.SenderUser.AvatarUrl
                    },
                    replyTo = x.ReplyTo == null ? null : new
                    {
                        messageId = x.ReplyTo.MessageId,
                        content = x.ReplyTo.Content,
                        messageType = x.ReplyTo.MessageType,
                        senderUserId = x.ReplyTo.SenderUserId
                    }
                })
                .ToListAsync();

            return Ok(new
            {
                clubId = id,
                total,
                page,
                pageSize,
                items = items
                    .Select(x => new
                    {
                        messageId = x.MessageId,
                        clubId = x.ClubId,
                        senderUserId = x.SenderUserId,
                        messageType = x.MessageType,
                        content = x.Content,
                        mediaUrl = ToAbsoluteUrl(x.MediaUrl),
                        replyToId = x.ReplyToId,
                        sentAt = x.SentAt,
                        sender = new
                        {
                            x.sender.userId,
                            x.sender.fullName,
                            avatarUrl = ToAbsoluteUrl(x.sender.avatarUrl)
                        },
                        x.replyTo
                    })
                    .OrderBy(x => x.sentAt)
                    .ToList()
            });
        }
        // =========================================================
        // POST: api/clubs/{id}/messages
        [Authorize(AuthenticationSchemes = "Bearer")]
        [HttpPost("{id:long}/messages")]
        public async Task<IActionResult> SendClubMessage(long id, [FromBody] SendClubMessageRequestDto req)
        {
            var userId = GetUserIdFromToken();

            var club = await _db.Clubs.FirstOrDefaultAsync(x => x.ClubId == id && x.IsActive);
            if (club == null)
                return NotFound(new { message = "Club not found." });

            var isMember = await IsActiveClubMember(id, userId);
            if (!isMember)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = "Bạn phải là thành viên CLB mới được nhắn tin."
                });
            }

            var messageType = (req.MessageType ?? "TEXT").Trim().ToUpperInvariant();
            if (messageType != "TEXT" && messageType != "IMAGE")
                return BadRequest(new { message = "MessageType chỉ hỗ trợ TEXT hoặc IMAGE." });

            if (messageType == "TEXT" && string.IsNullOrWhiteSpace(req.Content))
                return BadRequest(new { message = "Nội dung tin nhắn không được để trống." });

            if (messageType == "TEXT" && ContainsObjectionableChatContent(req.Content))
                return BadRequest(new { message = "Nội dung tin nhắn vi phạm tiêu chuẩn cộng đồng." });

            if (req.ReplyToId.HasValue)
            {
                var replyExists = await _db.ClubMessages.AnyAsync(x =>
                    x.MessageId == req.ReplyToId.Value &&
                    x.ClubId == id &&
                    !x.IsDeleted);

                if (!replyExists)
                    return BadRequest(new { message = "Tin nhắn được trả lời không tồn tại." });
            }

            var entity = new ClubMessage
            {
                ClubId = id,
                SenderUserId = userId,
                MessageType = messageType,
                Content = string.IsNullOrWhiteSpace(req.Content) ? null : req.Content.Trim(),
                MediaUrl = NormalizeToRelative(req.MediaUrl),
                ReplyToId = req.ReplyToId,
                SentAt = DateTime.UtcNow,
                IsDeleted = false
            };

            _db.ClubMessages.Add(entity);
            club.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            var saved = await _db.ClubMessages
                .AsNoTracking()
                .Where(x => x.MessageId == entity.MessageId)
                .Select(x => new
                {
                    messageId = x.MessageId,
                    clubId = x.ClubId,
                    senderUserId = x.SenderUserId,
                    messageType = x.MessageType,
                    content = x.Content,
                    mediaUrl = x.MediaUrl,
                    replyToId = x.ReplyToId,
                    sentAt = x.SentAt,
                    sender = new
                    {
                        userId = x.SenderUser.UserId,
                        fullName = x.SenderUser.FullName,
                        avatarUrl = x.SenderUser.AvatarUrl
                    }
                })
                .FirstAsync();

            var realtimeItem = new
            {
                saved.messageId,
                saved.clubId,
                saved.senderUserId,
                saved.messageType,
                saved.content,
                mediaUrl = ToAbsoluteUrl(saved.mediaUrl),
                saved.replyToId,
                saved.sentAt,
                sender = new
                {
                    saved.sender.userId,
                    saved.sender.fullName,
                    avatarUrl = ToAbsoluteUrl(saved.sender.avatarUrl)
                }
            };

            // 1) broadcast cho ai đang mở room
            await _realtimeHub.SendClubMessageCreatedAsync(id, realtimeItem);

            // 2) gửi notification cho các thành viên khác đang online
            var memberUserIds = await _db.ClubMembers
                .AsNoTracking()
                .Where(x =>
                    x.ClubId == id &&
                    x.IsActive &&
                    x.User.IsActive &&
                    x.UserId != userId &&
                    !_db.UserBlocks.Any(ub =>
                        ub.BlockerUserId == x.UserId &&
                        ub.BlockedUserId == userId &&
                        ub.IsActive))
                .Select(x => x.UserId)
                .ToListAsync();

            foreach (var memberUserId in memberUserIds)
            {
                await _realtimeHub.SendNotificationToUserAsync(memberUserId.ToString(), new
                {
                    kind = "new_message",
                    clubId = id,
                    clubName = club.ClubName,
                    senderUserId = saved.senderUserId,
                    senderName = saved.sender.fullName,
                    messagePreview = saved.messageType == "TEXT" ? saved.content : "[Hình ảnh]",
                    sentAt = saved.sentAt
                });
            }

            return Ok(new
            {
                message = "Gửi tin nhắn thành công.",
                item = realtimeItem
            });
        }
        // =========================================================
        // DELETE: api/clubs/{id}/messages/{messageId}
        [Authorize(AuthenticationSchemes = "Bearer")]
        [HttpDelete("{id:long}/messages/{messageId:long}")]
        public async Task<IActionResult> DeleteClubMessage(long id, long messageId)
        {
            var userId = GetUserIdFromToken();

            var msg = await _db.ClubMessages.FirstOrDefaultAsync(x =>
                x.MessageId == messageId &&
                x.ClubId == id &&
                !x.IsDeleted);

            if (msg == null)
                return NotFound(new { message = "Tin nhắn không tồn tại." });

            if (msg.SenderUserId != userId)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = "Bạn chỉ có thể xoá tin nhắn của chính mình."
                });
            }

            msg.IsDeleted = true;
            msg.Content = null;
            msg.MediaUrl = null;

            await _db.SaveChangesAsync();

            await _realtimeHub.SendClubMessageDeletedAsync(id, messageId);

            return Ok(new
            {
                message = "Đã xoá tin nhắn.",
                messageId
            });
        }
        // =========================================================
        // POST: api/clubs/message-media
        [Authorize(AuthenticationSchemes = "Bearer")]
        [HttpPost("message-media")]
        [RequestSizeLimit(10_000_000)]
        public async Task<IActionResult> UploadClubMessageMedia([FromForm] IFormFile file)
        {
            var userId = GetUserIdFromToken();

            if (file == null || file.Length == 0)
                return BadRequest(new { message = "File is required." });

            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowed.Contains(ext))
                return BadRequest(new { message = "Only jpg, jpeg, png, webp are allowed." });

            var uploadsDir = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", "club-messages");
            Directory.CreateDirectory(uploadsDir);

            var fileName = $"club_msg_{userId}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);

            await using (var stream = System.IO.File.Create(filePath))
            {
                await file.CopyToAsync(stream);
            }

            var relativeUrl = $"/uploads/club-messages/{fileName}";

            return Ok(new
            {
                mediaUrl = ToAbsoluteUrl(relativeUrl),
                relativeUrl
            });
        }
        private async Task<bool> IsActiveClubMember(long clubId, long userId)
        {
            return await _db.ClubMembers.AnyAsync(x =>
                x.ClubId == clubId &&
                x.UserId == userId &&
                x.IsActive &&
                x.User.IsActive);
        }
    }
}
