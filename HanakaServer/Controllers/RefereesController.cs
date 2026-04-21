using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using HanakaServer.Data;
using HanakaServer.Dtos.Referees;
using HanakaServer.Helpers;
using HanakaServer.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/referees")]
    public class RefereesController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly IConfiguration _config;

        private const string SYSTEM_RATING_NOTE_PREFIX = "Hệ thống khởi tạo điểm trình ban đầu";

        public RefereesController(PickleballDbContext db, IConfiguration config)
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
        /// Đồng thời đồng bộ shadow data sang Referee để dữ liệu cũ không bị lệch.
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

            var referee = await _db.Referees.FirstOrDefaultAsync(x => x.ExternalId == user.UserId.ToString());
            if (referee != null)
            {
                SyncRefereeShadowFromUser(referee, user, single, @double);
                referee.UpdatedAt = now;
            }

            await _db.SaveChangesAsync();
        }

        private void SyncRefereeShadowFromUser(
            Referee referee,
            User user,
            decimal? latestSingle = null,
            decimal? latestDouble = null)
        {
            var defaults = GetDefaultInitialRating(user.Gender);

            referee.ExternalId = user.UserId.ToString();
            referee.FullName = user.FullName;
            referee.City = user.City;
            referee.AvatarUrl = NormalizeAvatarToRelative(user.AvatarUrl);
            referee.LevelSingle = latestSingle ?? user.RatingSingle ?? defaults.single;
            referee.LevelDouble = latestDouble ?? user.RatingDouble ?? defaults.@double;
        }

        private RefereeUserAchievementItemDto MapAchievementItem(UserAchievement x)
        {
            var tournamentType = TournamentTypeHelper.Resolve(x.Tournament?.GameType, x.Tournament?.GenderCategory);
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

            return new RefereeUserAchievementItemDto
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

                Tournament = x.Tournament == null ? null : new RefereeAchievementTournamentDto
                {
                    TournamentId = x.Tournament.TournamentId,
                    Title = x.Tournament.Title,
                    BannerUrl = ToAbsoluteUrl(x.Tournament.BannerUrl),
                    StartTime = x.Tournament.StartTime,
                    LocationText = x.Tournament.LocationText,
                    AreaText = x.Tournament.AreaText,
                    GameType = x.Tournament.GameType,
                    GenderCategory = tournamentType.GenderCategory,
                    TournamentTypeCode = tournamentType.TournamentTypeCode,
                    TournamentTypeLabel = tournamentType.TournamentTypeLabel,
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

        private async Task<RefereeDetailDto?> BuildRefereeDetailAsync(long refereeId)
        {
            var keyRow = await (
                from r in _db.Referees.AsNoTracking()
                join u in _db.Users.AsNoTracking().Where(u => u.IsActive)
                    on r.ExternalId equals u.UserId.ToString()
                where r.RefereeId == refereeId
                select new
                {
                    r.RefereeId,
                    u.UserId
                }
            ).FirstOrDefaultAsync();

            if (keyRow == null) return null;

            await EnsureInitialRatingHistoryAsync(keyRow.UserId);

            var row = await (
                from r in _db.Referees.AsNoTracking()
                join u in _db.Users.AsNoTracking().Where(u => u.IsActive)
                    on r.ExternalId equals u.UserId.ToString()
                where r.RefereeId == refereeId
                select new
                {
                    RefereeId = r.RefereeId,
                    UserId = u.UserId,
                    r.ExternalId,

                    FullName = u.FullName,
                    City = u.City,
                    Gender = u.Gender,

                    RefereeVerified = r.Verified,
                    UserVerified = u.Verified,

                    AvatarUrl = u.AvatarUrl,
                    r.RefereeType,

                    r.Introduction,
                    r.WorkingArea,
                    r.Achievements,

                    u.Email,
                    u.Phone,
                    u.Bio,
                    u.BirthOfDate,

                    LegacyRatingSingle = u.RatingSingle,
                    LegacyRatingDouble = u.RatingDouble,

                    LatestRating = _db.UserRatingHistories
                        .Where(h => h.UserId == u.UserId)
                        .OrderByDescending(h => h.RatedAt)
                        .ThenByDescending(h => h.RatingHistoryId)
                        .Select(h => new
                        {
                            h.RatingSingle,
                            h.RatingDouble,
                            h.RatedAt
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
                .Select(x => new RefereeRatingHistoryItemDto
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

            return new RefereeDetailDto
            {
                RefereeId = row.RefereeId,
                UserId = row.UserId,
                ExternalId = row.ExternalId,

                FullName = row.FullName,
                City = row.City,
                Gender = row.Gender,

                Verified = row.RefereeVerified,
                UserVerified = row.UserVerified,

                LevelSingle = row.LatestRating?.RatingSingle ?? row.LegacyRatingSingle ?? defaults.single,
                LevelDouble = row.LatestRating?.RatingDouble ?? row.LegacyRatingDouble ?? defaults.@double,
                RatingUpdatedAt = row.LatestRating?.RatedAt,

                AvatarUrl = ToAbsoluteUrl(row.AvatarUrl),
                RefereeType = row.RefereeType ?? "REFEREE",

                Introduction = row.Introduction,
                WorkingArea = row.WorkingArea,
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
        /// GET /api/referees?query=&page=1&pageSize=10
        /// Danh sách trọng tài:
        /// - tên / city / avatar / gender lấy từ User
        /// - điểm trình lấy từ UserRatingHistories
        /// - introduction / workingArea / achievements giữ ở Referee
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

            var refereeBaseQuery =
                from r in _db.Referees.AsNoTracking()
                join u in _db.Users.AsNoTracking().Where(u => u.IsActive)
                    on r.ExternalId equals u.UserId.ToString()
                select new
                {
                    Referee = r,
                    User = u
                };

            if (!string.IsNullOrWhiteSpace(query))
            {
                var keyword = query.Trim();

                refereeBaseQuery = refereeBaseQuery.Where(x =>
                    x.User.FullName.Contains(keyword) ||
                    (x.User.City != null && x.User.City.Contains(keyword)) ||
                    (x.User.Phone != null && x.User.Phone.Contains(keyword)) ||
                    (x.User.Email != null && x.User.Email.Contains(keyword)) ||
                    (x.Referee.ExternalId != null && x.Referee.ExternalId.Contains(keyword)) ||
                    (x.Referee.WorkingArea != null && x.Referee.WorkingArea.Contains(keyword))
                );
            }

            refereeBaseQuery = refereeBaseQuery
                .OrderByDescending(x => hasCurrentUser && x.User.UserId == currentUserIdValue)
                .ThenByDescending(x => x.Referee.Verified)
                .ThenByDescending(x => x.User.Verified)
                .ThenBy(x => x.User.FullName);

            var total = await refereeBaseQuery.CountAsync();

            var rows = await refereeBaseQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.Referee.RefereeId,
                    UserId = x.User.UserId,
                    x.Referee.ExternalId,

                    FullName = x.User.FullName,
                    City = x.User.City,
                    Gender = x.User.Gender,

                    RefereeVerified = x.Referee.Verified,
                    UserVerified = x.User.Verified,

                    AvatarUrl = x.User.AvatarUrl,
                    x.Referee.RefereeType,

                    LegacyRatingSingle = x.User.RatingSingle,
                    LegacyRatingDouble = x.User.RatingDouble,

                    LatestRating = _db.UserRatingHistories
                        .Where(h => h.UserId == x.User.UserId)
                        .OrderByDescending(h => h.RatedAt)
                        .ThenByDescending(h => h.RatingHistoryId)
                        .Select(h => new
                        {
                            h.RatingSingle,
                            h.RatingDouble,
                            h.RatedAt
                        })
                        .FirstOrDefault(),

                    IsMine = hasCurrentUser && x.User.UserId == currentUserIdValue
                })
                .ToListAsync();

            var items = rows.Select(x =>
            {
                var defaults = GetDefaultInitialRating(x.Gender);

                return new RefereeListItemDto
                {
                    RefereeId = x.RefereeId,
                    UserId = x.UserId,
                    ExternalId = x.ExternalId,

                    FullName = x.FullName,
                    City = x.City,
                    Gender = x.Gender,

                    Verified = x.RefereeVerified,
                    UserVerified = x.UserVerified,

                    LevelSingle = x.LatestRating?.RatingSingle ?? x.LegacyRatingSingle ?? defaults.single,
                    LevelDouble = x.LatestRating?.RatingDouble ?? x.LegacyRatingDouble ?? defaults.@double,
                    RatingUpdatedAt = x.LatestRating?.RatedAt,

                    AvatarUrl = ToAbsoluteUrl(x.AvatarUrl),
                    RefereeType = x.RefereeType ?? "REFEREE",

                    IsMine = x.IsMine
                };
            }).ToList();

            return Ok(new RefereePagedResponseDto
            {
                Page = page,
                PageSize = pageSize,
                Total = total,
                Items = items
            });
        }

        /// <summary>
        /// GET /api/referees/{refereeId}
        /// Chi tiết trọng tài:
        /// - thông tin user
        /// - rating history
        /// - user achievements
        /// - thông tin làm việc của trọng tài
        /// </summary>
        [HttpGet("{refereeId:long}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetById([FromRoute] long refereeId)
        {
            var referee = await BuildRefereeDetailAsync(refereeId);

            if (referee == null)
                return NotFound(new { message = "Không tìm thấy trọng tài." });

            return Ok(referee);
        }

        /// <summary>
        /// POST /api/referees/register-me
        /// Tạo trọng tài từ user đang đăng nhập bằng JWT
        /// ExternalId = userId
        /// </summary>
        [HttpPost("register-me")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> RegisterMe([FromBody] CreateRefereeFromMeRequest? req)
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

                await EnsureInitialRatingHistoryAsync(user.UserId);

                var latestRating = await _db.UserRatingHistories
                    .AsNoTracking()
                    .Where(x => x.UserId == user.UserId)
                    .OrderByDescending(x => x.RatedAt)
                    .ThenByDescending(x => x.RatingHistoryId)
                    .Select(x => new
                    {
                        x.RatingSingle,
                        x.RatingDouble
                    })
                    .FirstOrDefaultAsync();

                var existedReferee = await _db.Referees
                    .FirstOrDefaultAsync(x => x.ExternalId == userId.Value.ToString());

                if (existedReferee != null)
                {
                    SyncRefereeShadowFromUser(
                        existedReferee,
                        user,
                        latestRating?.RatingSingle,
                        latestRating?.RatingDouble
                    );
                    existedReferee.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();

                    return BadRequest(new
                    {
                        message = existedReferee.Verified
                            ? "Bạn đã là trọng tài rồi."
                            : "Bạn đã đăng ký trọng tài và đang chờ xác thực."
                    });
                }

                var refereeType = string.IsNullOrWhiteSpace(req?.RefereeType)
                    ? "REFEREE"
                    : req!.RefereeType!.Trim().ToUpperInvariant();

                if (refereeType.Length > 20)
                    refereeType = refereeType[..20];

                var referee = new Referee
                {
                    Verified = false,
                    RefereeType = refereeType,
                    Introduction = null,
                    WorkingArea = null,
                    Achievements = null,
                    UpdatedAt = null
                };

                SyncRefereeShadowFromUser(
                    referee,
                    user,
                    latestRating?.RatingSingle,
                    latestRating?.RatingDouble
                );

                _db.Referees.Add(referee);
                await _db.SaveChangesAsync();

                var result = await BuildRefereeDetailAsync(referee.RefereeId);

                return Ok(new
                {
                    message = "Đăng ký trọng tài thành công. Hồ sơ đang chờ admin xác thực.",
                    data = result
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// GET /api/referees/me
        /// Lấy hồ sơ trọng tài của chính user hiện tại
        /// </summary>
        [HttpGet("me")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> GetMyRefereeProfile()
        {
            var userId = await TryGetCurrentUserIdAsync();
            if (!userId.HasValue)
            {
                return Unauthorized(new { message = "JWT không hợp lệ hoặc không tìm thấy userId." });
            }

            var refereeId = await _db.Referees
                .AsNoTracking()
                .Where(x => x.ExternalId == userId.Value.ToString())
                .Select(x => (long?)x.RefereeId)
                .FirstOrDefaultAsync();

            if (!refereeId.HasValue)
            {
                return NotFound(new { message = "Bạn chưa đăng ký trọng tài." });
            }

            var referee = await BuildRefereeDetailAsync(refereeId.Value);
            if (referee == null)
            {
                return NotFound(new { message = "Không tìm thấy hồ sơ trọng tài." });
            }

            return Ok(referee);
        }

        /// <summary>
        /// PUT /api/referees/me/profile
        /// Cập nhật phần riêng của trọng tài
        /// </summary>
        [HttpPut("me/profile")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> UpdateMyRefereeProfile([FromBody] UpdateMyRefereeProfileRequest req)
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

                var referee = await _db.Referees
                    .FirstOrDefaultAsync(x => x.ExternalId == userId.Value.ToString());

                if (referee == null)
                {
                    return NotFound(new { message = "Bạn chưa đăng ký trọng tài." });
                }

                referee.Introduction = req.Introduction;
                referee.WorkingArea = req.WorkingArea;
                referee.Achievements = req.Achievements;
                referee.UpdatedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync();

                var result = await BuildRefereeDetailAsync(referee.RefereeId);

                return Ok(new
                {
                    message = "Đã cập nhật hồ sơ trọng tài.",
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
