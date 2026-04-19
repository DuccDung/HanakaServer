using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using HanakaServer.Data;
using HanakaServer.Dtos.Coaches;
using HanakaServer.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/coaches")]
    public class CoachesController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly IConfiguration _config;

        private const string SYSTEM_RATING_NOTE_PREFIX = "Hệ thống khởi tạo điểm trình ban đầu";

        public CoachesController(PickleballDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        private string? ToAbsoluteUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            url = url.Trim();

            if (Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                return url;
            }

            if (!url.StartsWith("/"))
            {
                url = "/" + url;
            }

            if (Request?.Host.HasValue == true)
            {
                var pathBase = Request.PathBase.HasValue ? Request.PathBase.Value : string.Empty;
                return $"{Request.Scheme}://{Request.Host}{pathBase}{url}";
            }

            var baseUrl = (_config["PublicBaseUrl"] ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return url;
            }

            return $"{baseUrl}{url}";
        }

        private string? NormalizeAvatarToRelative(string? avatarUrl)
        {
            if (string.IsNullOrWhiteSpace(avatarUrl)) return null;

            avatarUrl = avatarUrl.Trim();

            if (avatarUrl.StartsWith("/")) return avatarUrl;

            if (Uri.TryCreate(avatarUrl, UriKind.Absolute, out var uri))
                return uri.PathAndQuery;

            return avatarUrl;
        }

        private async Task<long?> TryGetCurrentUserIdAsync()
        {
            var userIdClaim =
                User.FindFirstValue("uid") ??
                User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!string.IsNullOrWhiteSpace(userIdClaim) &&
                long.TryParse(userIdClaim, out var parsedUserId))
            {
                return parsedUserId;
            }

            var authResult = await HttpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);

            if (!authResult.Succeeded || authResult.Principal == null)
                return null;

            var principalUserId =
                authResult.Principal.FindFirstValue("uid") ??
                authResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(principalUserId) ||
                !long.TryParse(principalUserId, out var userId))
            {
                return null;
            }

            return userId;
        }

        private static bool IsFemale(string? gender)
        {
            if (string.IsNullOrWhiteSpace(gender)) return false;

            var g = gender.Trim().ToLowerInvariant();

            return g == "nữ"
                || g == "nu"
                || g == "female"
                || g == "f"
                || g == "woman"
                || g == "girl";
        }

        private static (decimal single, decimal @double) GetDefaultInitialRating(string? gender)
        {
            if (IsFemale(gender))
                return (1.8m, 1.8m);

            return (2.6m, 2.6m);
        }

        /// <summary>
        /// Nếu user chưa có lịch sử điểm thì tự khởi tạo giống UsersController.
        /// Đồng thời đồng bộ shadow data sang Coach để dữ liệu cũ không bị lệch.
        /// </summary>
        private async Task EnsureInitialRatingHistoryAsync(long userId)
        {
            var user = await _db.Users.FirstOrDefaultAsync(x => x.UserId == userId && x.IsActive);
            if (user == null) return;

            var hasAnyHistory = await _db.UserRatingHistories
                .AsNoTracking()
                .AnyAsync(x => x.UserId == userId);

            if (hasAnyHistory) return;

            var (single, @double) = GetDefaultInitialRating(user.Gender);
            var now = DateTime.UtcNow;

            _db.UserRatingHistories.Add(new UserRatingHistory
            {
                UserId = user.UserId,
                RatingSingle = single,
                RatingDouble = @double,
                RatedByUserId = null,
                Note = $"{SYSTEM_RATING_NOTE_PREFIX} theo giới tính: {(string.IsNullOrWhiteSpace(user.Gender) ? "không xác định, mặc định nam" : user.Gender)}.",
                RatedAt = now
            });

            user.RatingSingle = single;
            user.RatingDouble = @double;
            user.UpdatedAt = now;

            var coach = await _db.Coaches.FirstOrDefaultAsync(x => x.ExternalId == user.UserId.ToString());
            if (coach != null)
            {
                SyncCoachShadowFromUser(coach, user, single, @double);
            }

            await _db.SaveChangesAsync();
        }

        private void SyncCoachShadowFromUser(
            Coach coach,
            User user,
            decimal? latestSingle = null,
            decimal? latestDouble = null)
        {
            var defaults = GetDefaultInitialRating(user.Gender);

            coach.ExternalId = user.UserId.ToString();
            coach.FullName = user.FullName;
            coach.City = user.City;
            coach.AvatarUrl = NormalizeAvatarToRelative(user.AvatarUrl);
            coach.LevelSingle = latestSingle ?? user.RatingSingle ?? defaults.single;
            coach.LevelDouble = latestDouble ?? user.RatingDouble ?? defaults.@double;
        }

        private CoachUserAchievementItemDto MapAchievementItem(UserAchievement x)
        {
            var achievementType = (x.AchievementType ?? "").Trim().ToUpperInvariant();

            var rank = achievementType switch
            {
                "FIRST" => 1,
                "SECOND" => 2,
                "THIRD" => 3,
                _ => 0
            };

            var achievementLabel = achievementType switch
            {
                "FIRST" => "Giải Nhất",
                "SECOND" => "Giải Nhì",
                "THIRD" => "Giải Ba",
                _ => x.AchievementType ?? "Thành tích"
            };

            return new CoachUserAchievementItemDto
            {
                UserAchievementId = x.UserAchievementId,
                UserId = x.UserId,
                TournamentId = x.TournamentId,
                AchievementType = x.AchievementType,
                AchievementLabel = achievementLabel,
                Rank = rank,
                CreatedAt = x.CreatedAt,
                AchievedAt = x.CreatedAt,
                Note = x.Note,

                Tournament = x.Tournament == null ? null : new CoachAchievementTournamentDto
                {
                    TournamentId = x.Tournament.TournamentId,
                    Title = x.Tournament.Title,
                    BannerUrl = ToAbsoluteUrl(x.Tournament.BannerUrl),
                    StartTime = x.Tournament.StartTime,
                    LocationText = x.Tournament.LocationText,
                    AreaText = x.Tournament.AreaText,
                    GameType = x.Tournament.GameType,
                    Status = x.Tournament.Status
                },

                Title = x.Tournament != null ? x.Tournament.Title : achievementLabel,
                TournamentName = x.Tournament != null ? x.Tournament.Title : null,
                BannerUrl = x.Tournament != null ? ToAbsoluteUrl(x.Tournament.BannerUrl) : null,
                Date = x.Tournament != null && x.Tournament.StartTime.HasValue
                    ? x.Tournament.StartTime
                    : x.CreatedAt
            };
        }

        private async Task<CoachDetailDto?> BuildCoachDetailAsync(long coachId)
        {
            // lấy userId trước để có thể auto-generate rating history nếu user cũ chưa có
            var keyRow = await (
                from c in _db.Coaches.AsNoTracking()
                join u in _db.Users.AsNoTracking().Where(u => u.IsActive)
                    on c.ExternalId equals u.UserId.ToString()
                where c.CoachId == coachId
                select new
                {
                    c.CoachId,
                    u.UserId
                }
            ).FirstOrDefaultAsync();

            if (keyRow == null) return null;

            await EnsureInitialRatingHistoryAsync(keyRow.UserId);

            var row = await (
                from c in _db.Coaches.AsNoTracking()
                join u in _db.Users.AsNoTracking().Where(u => u.IsActive)
                    on c.ExternalId equals u.UserId.ToString()
                where c.CoachId == coachId
                select new
                {
                    CoachId = c.CoachId,
                    UserId = u.UserId,
                    c.ExternalId,

                    FullName = u.FullName,
                    City = u.City,
                    Gender = u.Gender,

                    CoachVerified = c.Verified,
                    UserVerified = u.Verified,

                    AvatarUrl = u.AvatarUrl,
                    c.CoachType,

                    c.Introduction,
                    c.TeachingArea,
                    c.Achievements,

                    u.Email,
                    u.Phone,
                    u.Bio,
                    u.BirthOfDate,

                    LegacyRatingSingle = u.RatingSingle,
                    LegacyRatingDouble = u.RatingDouble,

                    LatestRating = _db.UserRatingHistories
                        .Where(r => r.UserId == u.UserId)
                        .OrderByDescending(r => r.RatedAt)
                        .ThenByDescending(r => r.RatingHistoryId)
                        .Select(r => new
                        {
                            r.RatingSingle,
                            r.RatingDouble,
                            r.RatedAt
                        })
                        .FirstOrDefault()
                }
            ).FirstOrDefaultAsync();

            if (row == null) return null;

            var defaults = GetDefaultInitialRating(row.Gender);

            var ratingHistory = await _db.UserRatingHistories
                .AsNoTracking()
                .Where(x => x.UserId == row.UserId)
                .OrderByDescending(x => x.RatedAt)
                .ThenByDescending(x => x.RatingHistoryId)
                .Select(x => new CoachRatingHistoryItemDto
                {
                    RatingHistoryId = x.RatingHistoryId,
                    RatingSingle = x.RatingSingle,
                    RatingDouble = x.RatingDouble,
                    RatedAt = x.RatedAt,
                    Note = x.Note,
                    RatedByUserId = x.RatedByUserId,
                    RatedByName = x.RatedByUserId == null
                        ? "Hệ thống"
                        : (x.RatedByUser != null ? x.RatedByUser.FullName : null)
                })
                .ToListAsync();

            var userAchievements = await _db.UserAchievements
                .AsNoTracking()
                .Where(x => x.UserId == row.UserId)
                .Include(x => x.Tournament)
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.UserAchievementId)
                .ToListAsync();

            return new CoachDetailDto
            {
                CoachId = row.CoachId,
                UserId = row.UserId,
                ExternalId = row.ExternalId,

                FullName = row.FullName,
                City = row.City,
                Gender = row.Gender,

                Verified = row.CoachVerified,
                UserVerified = row.UserVerified,

                LevelSingle = row.LatestRating?.RatingSingle ?? row.LegacyRatingSingle ?? defaults.single,
                LevelDouble = row.LatestRating?.RatingDouble ?? row.LegacyRatingDouble ?? defaults.@double,
                RatingUpdatedAt = row.LatestRating?.RatedAt,

                AvatarUrl = ToAbsoluteUrl(row.AvatarUrl),
                CoachType = row.CoachType ?? "COACH",

                Introduction = row.Introduction,
                TeachingArea = row.TeachingArea,
                Achievements = row.Achievements,

                Email = row.Email,
                Phone = row.Phone,
                Bio = row.Bio,
                BirthOfDate = row.BirthOfDate,

                RatingHistory = ratingHistory,
                UserAchievements = userAchievements.Select(MapAchievementItem).ToList()
            };
        }

        /// <summary>
        /// GET /api/coaches?query=&page=1&pageSize=10
        /// Danh sách coach:
        /// - tên / city / avatar / gender lấy từ User
        /// - điểm trình lấy từ UserRatingHistories
        /// - introduction / teachingArea / achievements giữ ở Coach
        /// - nếu có JWT hợp lệ thì đánh dấu IsMine
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetPaged(
            [FromQuery] string? query = "",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            page = page <= 0 ? 1 : page;
            pageSize = pageSize <= 0 ? 10 : pageSize;
            pageSize = pageSize > 50 ? 50 : pageSize;

            var currentUserId = await TryGetCurrentUserIdAsync();
            var hasCurrentUser = currentUserId.HasValue;
            var currentUserIdValue = currentUserId ?? -1;

            var coachBaseQuery =
                from c in _db.Coaches.AsNoTracking()
                join u in _db.Users.AsNoTracking().Where(u => u.IsActive)
                    on c.ExternalId equals u.UserId.ToString()
                select new
                {
                    Coach = c,
                    User = u
                };

            if (!string.IsNullOrWhiteSpace(query))
            {
                var keyword = query.Trim();

                coachBaseQuery = coachBaseQuery.Where(x =>
                    x.User.FullName.Contains(keyword) ||
                    (x.User.City != null && x.User.City.Contains(keyword)) ||
                    (x.User.Phone != null && x.User.Phone.Contains(keyword)) ||
                    (x.User.Email != null && x.User.Email.Contains(keyword)) ||
                    (x.Coach.ExternalId != null && x.Coach.ExternalId.Contains(keyword)) ||
                    (x.Coach.TeachingArea != null && x.Coach.TeachingArea.Contains(keyword))
                );
            }

            coachBaseQuery = coachBaseQuery
                .OrderByDescending(x => hasCurrentUser && x.User.UserId == currentUserIdValue)
                .ThenByDescending(x => x.Coach.Verified)
                .ThenByDescending(x => x.User.Verified)
                .ThenBy(x => x.User.FullName);

            var total = await coachBaseQuery.CountAsync();

            var rows = await coachBaseQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.Coach.CoachId,
                    UserId = x.User.UserId,
                    x.Coach.ExternalId,

                    FullName = x.User.FullName,
                    City = x.User.City,
                    Gender = x.User.Gender,

                    CoachVerified = x.Coach.Verified,
                    UserVerified = x.User.Verified,

                    AvatarUrl = x.User.AvatarUrl,
                    x.Coach.CoachType,

                    LegacyRatingSingle = x.User.RatingSingle,
                    LegacyRatingDouble = x.User.RatingDouble,

                    LatestRating = _db.UserRatingHistories
                        .Where(r => r.UserId == x.User.UserId)
                        .OrderByDescending(r => r.RatedAt)
                        .ThenByDescending(r => r.RatingHistoryId)
                        .Select(r => new
                        {
                            r.RatingSingle,
                            r.RatingDouble,
                            r.RatedAt
                        })
                        .FirstOrDefault(),

                    IsMine = hasCurrentUser && x.User.UserId == currentUserIdValue
                })
                .ToListAsync();

            var items = rows.Select(x =>
            {
                var defaults = GetDefaultInitialRating(x.Gender);

                return new CoachListItemDto
                {
                    CoachId = x.CoachId,
                    UserId = x.UserId,
                    ExternalId = x.ExternalId,

                    FullName = x.FullName,
                    City = x.City,
                    Gender = x.Gender,

                    Verified = x.CoachVerified,
                    UserVerified = x.UserVerified,

                    LevelSingle = x.LatestRating?.RatingSingle ?? x.LegacyRatingSingle ?? defaults.single,
                    LevelDouble = x.LatestRating?.RatingDouble ?? x.LegacyRatingDouble ?? defaults.@double,
                    RatingUpdatedAt = x.LatestRating?.RatedAt,

                    AvatarUrl = ToAbsoluteUrl(x.AvatarUrl),
                    CoachType = x.CoachType ?? "COACH",

                    IsMine = x.IsMine
                };
            }).ToList();

            return Ok(new CoachPagedResponseDto
            {
                Page = page,
                PageSize = pageSize,
                Total = total,
                Items = items
            });
        }

        /// <summary>
        /// GET /api/coaches/{coachId}
        /// Chi tiết coach:
        /// - thông tin user
        /// - rating history
        /// - user achievements
        /// - thông tin giảng dạy của coach
        /// </summary>
        [HttpGet("{coachId:long}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetById([FromRoute] long coachId)
        {
            var coach = await BuildCoachDetailAsync(coachId);

            if (coach == null)
                return NotFound(new { message = "Không tìm thấy huấn luyện viên." });

            return Ok(coach);
        }

        /// <summary>
        /// POST /api/coaches/register-me
        /// Tạo coach từ user đang đăng nhập bằng JWT
        /// ExternalId = userId
        /// </summary>
        [HttpPost("register-me")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> RegisterMe([FromBody] CreateCoachFromMeRequest? req)
        {
            try
            {
                var userId = await TryGetCurrentUserIdAsync();
                if (!userId.HasValue)
                {
                    return Unauthorized(new { message = "JWT không hợp lệ hoặc không tìm thấy userId." });
                }

                var user = await _db.Users.FirstOrDefaultAsync(x => x.UserId == userId.Value && x.IsActive);
                if (user == null)
                {
                    return NotFound(new { message = "Không tìm thấy user tương ứng." });
                }

                var existedCoach = await _db.Coaches
                    .FirstOrDefaultAsync(x => x.ExternalId == userId.Value.ToString());

                if (existedCoach != null)
                {
                    // Đồng bộ shadow data cũ luôn để tránh lệch
                    SyncCoachShadowFromUser(existedCoach, user);
                    await _db.SaveChangesAsync();

                    return BadRequest(new
                    {
                        message = existedCoach.Verified
                            ? "Bạn đã là huấn luyện viên rồi."
                            : "Bạn đã đăng ký huấn luyện viên và đang chờ xác thực."
                    });
                }

                var coachType = string.IsNullOrWhiteSpace(req?.CoachType)
                    ? "COACH"
                    : req!.CoachType!.Trim().ToUpperInvariant();

                if (coachType.Length > 20)
                    coachType = coachType[..20];

                var coach = new Coach
                {
                    Verified = false,
                    CoachType = coachType,
                    Introduction = null,
                    TeachingArea = null,
                    Achievements = null
                };

                SyncCoachShadowFromUser(coach, user);

                _db.Coaches.Add(coach);
                await _db.SaveChangesAsync();

                var result = await BuildCoachDetailAsync(coach.CoachId);

                return Ok(new
                {
                    message = "Đăng ký huấn luyện viên thành công. Hồ sơ đang chờ admin xác thực.",
                    data = result
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// GET /api/coaches/me
        /// Lấy hồ sơ coach của chính user hiện tại
        /// </summary>
        [HttpGet("me")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> GetMyCoachProfile()
        {
            var userId = await TryGetCurrentUserIdAsync();
            if (!userId.HasValue)
            {
                return Unauthorized(new { message = "JWT không hợp lệ hoặc không tìm thấy userId." });
            }

            var coachId = await _db.Coaches
                .AsNoTracking()
                .Where(x => x.ExternalId == userId.Value.ToString())
                .Select(x => (long?)x.CoachId)
                .FirstOrDefaultAsync();

            if (!coachId.HasValue)
            {
                return NotFound(new { message = "Bạn chưa đăng ký huấn luyện viên." });
            }

            var coach = await BuildCoachDetailAsync(coachId.Value);
            if (coach == null)
            {
                return NotFound(new { message = "Không tìm thấy hồ sơ huấn luyện viên." });
            }

            return Ok(coach);
        }

        /// <summary>
        /// PUT /api/coaches/me/profile
        /// Cập nhật phần riêng của coach
        /// </summary>
        [HttpPut("me/profile")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> UpdateMyCoachProfile([FromBody] UpdateMyCoachProfileRequest req)
        {
            try
            {
                if (req == null)
                    return BadRequest(new { message = "Dữ liệu gửi lên không hợp lệ." });

                var userId = await TryGetCurrentUserIdAsync();
                if (!userId.HasValue)
                {
                    return Unauthorized(new { message = "JWT không hợp lệ hoặc không tìm thấy userId." });
                }

                var coach = await _db.Coaches
                    .FirstOrDefaultAsync(x => x.ExternalId == userId.Value.ToString());

                if (coach == null)
                {
                    return NotFound(new { message = "Bạn chưa đăng ký huấn luyện viên." });
                }

                coach.Introduction = req.Introduction;
                coach.TeachingArea = req.TeachingArea;
                coach.Achievements = req.Achievements;

                await _db.SaveChangesAsync();

                var result = await BuildCoachDetailAsync(coach.CoachId);

                return Ok(new
                {
                    message = "Đã cập nhật hồ sơ huấn luyện viên.",
                    data = result
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
