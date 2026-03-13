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

        public CoachesController(PickleballDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }
        private string GetBaseUrl()
        {
            return _config["PublicBaseUrl"] ?? "";
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
            {
                return null;
            }

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

        /// <summary>
        /// GET /api/coaches?query=&page=1&pageSize=10
        /// Danh sách coach có phân trang, search theo tên và thành phố
        /// Nếu có JWT hợp lệ thì đánh dấu IsMine
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
            var currentUserIdText = hasCurrentUser ? currentUserId.Value.ToString() : null;

            var coachQuery = _db.Coaches
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(query))
            {
                var keyword = query.Trim();

                coachQuery = coachQuery.Where(x =>
                    x.FullName.Contains(keyword) ||
                    (x.City != null && x.City.Contains(keyword)));
            }

            coachQuery = coachQuery
                .OrderByDescending(x => hasCurrentUser && x.ExternalId == currentUserIdText)
                .ThenByDescending(x => x.Verified)
                .ThenBy(x => x.FullName);

            var total = await coachQuery.CountAsync();

            var items = await coachQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new CoachListItemDto
                {
                    CoachId = x.CoachId,
                    ExternalId = x.ExternalId,
                    FullName = x.FullName,
                    City = x.City,
                    Verified = x.Verified,
                    LevelSingle = x.LevelSingle,
                    LevelDouble = x.LevelDouble,
                    AvatarUrl = GetBaseUrl() + x.AvatarUrl,
                    CoachType = x.CoachType,
                    IsMine = hasCurrentUser && x.ExternalId == currentUserIdText
                })
                .ToListAsync();

            var result = new CoachPagedResponseDto
            {
                Page = page,
                PageSize = pageSize,
                Total = total,
                Items = items
            };

            return Ok(result);
        }

        /// <summary>
        /// GET /api/coaches/{coachId}
        /// Lấy chi tiết 1 coach
        /// </summary>
        [HttpGet("{coachId:long}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetById([FromRoute] long coachId)
        {
            var coach = await _db.Coaches
                .AsNoTracking()
                .Where(x => x.CoachId == coachId)
                .Select(x => new CoachDetailDto
                {
                    CoachId = x.CoachId,
                    ExternalId = x.ExternalId,
                    FullName = x.FullName,
                    City = x.City,
                    Verified = x.Verified,
                    LevelSingle = x.LevelSingle,
                    LevelDouble = x.LevelDouble,
                    AvatarUrl = GetBaseUrl() + x.AvatarUrl,
                    CoachType = x.CoachType,
                    Introduction = x.Introduction,
                    TeachingArea = x.TeachingArea,
                    Achievements = x.Achievements
                })
                .FirstOrDefaultAsync();

            if (coach == null)
            {
                return NotFound(new { message = "Không tìm thấy huấn luyện viên." });
            }

            return Ok(coach);
        }

        /// <summary>
        /// POST /api/coaches/register-me
        /// Tạo coach từ user đang đăng nhập bằng JWT
        /// ExternalId = userId
        /// Verified = false mặc định
        /// </summary>
        [HttpPost("register-me")]
        public async Task<IActionResult> RegisterMe([FromBody] CreateCoachFromMeRequest? req)
        {
            try
            {
                var userId = await TryGetCurrentUserIdAsync();
                if (!userId.HasValue)
                {
                    return Unauthorized(new { message = "JWT không hợp lệ hoặc không tìm thấy userId." });
                }

                var user = await _db.Users.FirstOrDefaultAsync(x => x.UserId == userId.Value);
                if (user == null)
                {
                    return NotFound(new { message = "Không tìm thấy user tương ứng." });
                }

                var existedCoach = await _db.Coaches
                    .FirstOrDefaultAsync(x => x.ExternalId == userId.Value.ToString());

                if (existedCoach != null)
                {
                    return BadRequest(new
                    {
                        message = existedCoach.Verified
                            ? "Bạn đã là huấn luyện viên rồi."
                            : "Bạn đã đăng ký huấn luyện viên và đang chờ xác thực."
                    });
                }

                var coachType = string.IsNullOrWhiteSpace(req?.CoachType)
                    ? "COACH"
                    : req!.CoachType!.Trim().ToUpper();

                var coach = new Coach
                {
                    ExternalId = user.UserId.ToString(),
                    FullName = user.FullName,
                    City = user.City,
                    Verified = false,
                    LevelSingle = user.RatingSingle ?? 0m,
                    LevelDouble = user.RatingDouble ?? 0m,
                    AvatarUrl =  user.AvatarUrl,
                    CoachType = coachType,
                    Introduction = null,
                    TeachingArea = null,
                    Achievements = null
                };

                _db.Coaches.Add(coach);
                await _db.SaveChangesAsync();

                var result = new CoachDetailDto
                {
                    CoachId = coach.CoachId,
                    ExternalId = coach.ExternalId,
                    FullName = coach.FullName,
                    City = coach.City,
                    Verified = coach.Verified,
                    LevelSingle = coach.LevelSingle,
                    LevelDouble = coach.LevelDouble,
                    AvatarUrl = GetBaseUrl() + coach.AvatarUrl,
                    CoachType = coach.CoachType,
                    Introduction = coach.Introduction,
                    TeachingArea = coach.TeachingArea,
                    Achievements = coach.Achievements
                };

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
        /// Lấy hồ sơ coach của user hiện tại nếu đã đăng ký
        /// </summary>
        [HttpGet("me")]
        public async Task<IActionResult> GetMyCoachProfile()
        {
            var userId = await TryGetCurrentUserIdAsync();
            if (!userId.HasValue)
            {
                return Unauthorized(new { message = "JWT không hợp lệ hoặc không tìm thấy userId." });
            }

            var coach = await _db.Coaches
                .AsNoTracking()
                .Where(x => x.ExternalId == userId.Value.ToString())
                .Select(x => new CoachDetailDto
                {
                    CoachId = x.CoachId,
                    ExternalId = x.ExternalId,
                    FullName = x.FullName,
                    City = x.City,
                    Verified = x.Verified,
                    LevelSingle = x.LevelSingle,
                    LevelDouble = x.LevelDouble,
                    AvatarUrl = GetBaseUrl() + x.AvatarUrl,
                    CoachType = x.CoachType,
                    Introduction = x.Introduction,
                    TeachingArea = x.TeachingArea,
                    Achievements = x.Achievements
                })
                .FirstOrDefaultAsync();

            if (coach == null)
            {
                return NotFound(new { message = "Bạn chưa đăng ký huấn luyện viên." });
            }

            return Ok(coach);
        }

        /// <summary>
        /// PUT /api/coaches/me/profile
        /// Cập nhật hồ sơ coach của chính user đang đăng nhập
        /// </summary>
        [HttpPut("me/profile")]
        public async Task<IActionResult> UpdateMyCoachProfile([FromBody] UpdateMyCoachProfileRequest req)
        {
            try
            {
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

                var result = new CoachDetailDto
                {
                    CoachId = coach.CoachId,
                    ExternalId = coach.ExternalId,
                    FullName = coach.FullName,
                    City = coach.City,
                    Verified = coach.Verified,
                    LevelSingle = coach.LevelSingle,
                    LevelDouble = coach.LevelDouble,
                    AvatarUrl =  coach.AvatarUrl,
                    CoachType = coach.CoachType,
                    Introduction = coach.Introduction,
                    TeachingArea = coach.TeachingArea,
                    Achievements = coach.Achievements
                };

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