using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using HanakaServer.Data;
using HanakaServer.Dtos.Referees;
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

        public RefereesController(PickleballDbContext db, IConfiguration config)
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
        /// GET /api/referees?query=&page=1&pageSize=10
        /// Danh sách trọng tài có phân trang, search theo tên và thành phố
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

            var refereeQuery = _db.Referees
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(query))
            {
                var keyword = query.Trim();

                refereeQuery = refereeQuery.Where(x =>
                    x.FullName.Contains(keyword) ||
                    (x.City != null && x.City.Contains(keyword)));
            }

            refereeQuery = refereeQuery
                .OrderByDescending(x => hasCurrentUser && x.ExternalId == currentUserIdText)
                .ThenByDescending(x => x.Verified)
                .ThenBy(x => x.FullName);

            var total = await refereeQuery.CountAsync();

            var items = await refereeQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new RefereeListItemDto
                {
                    RefereeId = x.RefereeId,
                    ExternalId = x.ExternalId,
                    FullName = x.FullName,
                    City = x.City,
                    Verified = x.Verified,
                    LevelSingle = x.LevelSingle,
                    LevelDouble = x.LevelDouble,
                    AvatarUrl = GetBaseUrl() + x.AvatarUrl,
                    RefereeType = x.RefereeType,
                    IsMine = hasCurrentUser && x.ExternalId == currentUserIdText
                })
                .ToListAsync();

            var result = new RefereePagedResponseDto
            {
                Page = page,
                PageSize = pageSize,
                Total = total,
                Items = items
            };

            return Ok(result);
        }

        /// <summary>
        /// GET /api/referees/{refereeId}
        /// Lấy chi tiết 1 trọng tài
        /// </summary>
        [HttpGet("{refereeId:long}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetById([FromRoute] long refereeId)
        {
            var referee = await _db.Referees
                .AsNoTracking()
                .Where(x => x.RefereeId == refereeId)
                .Select(x => new RefereeDetailDto
                {
                    RefereeId = x.RefereeId,
                    ExternalId = x.ExternalId,
                    FullName = x.FullName,
                    City = x.City,
                    Verified = x.Verified,
                    LevelSingle = x.LevelSingle,
                    LevelDouble = x.LevelDouble,
                    AvatarUrl = GetBaseUrl() + x.AvatarUrl,
                    RefereeType = x.RefereeType,
                    Introduction = x.Introduction,
                    WorkingArea = x.WorkingArea,
                    Achievements = x.Achievements
                })
                .FirstOrDefaultAsync();

            if (referee == null)
            {
                return NotFound(new { message = "Không tìm thấy trọng tài." });
            }

            return Ok(referee);
        }

        /// <summary>
        /// POST /api/referees/register-me
        /// Tạo trọng tài từ user đang đăng nhập bằng JWT
        /// ExternalId = userId
        /// Verified = false mặc định
        /// </summary>
        [HttpPost("register-me")]
        public async Task<IActionResult> RegisterMe([FromBody] CreateRefereeFromMeRequest? req)
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

                var existedReferee = await _db.Referees
                    .FirstOrDefaultAsync(x => x.ExternalId == userId.Value.ToString());

                if (existedReferee != null)
                {
                    return BadRequest(new
                    {
                        message = existedReferee.Verified
                            ? "Bạn đã là trọng tài rồi."
                            : "Bạn đã đăng ký trọng tài và đang chờ xác thực."
                    });
                }

                var refereeType = string.IsNullOrWhiteSpace(req?.RefereeType)
                    ? "REFEREE"
                    : req!.RefereeType!.Trim().ToUpper();

                var referee = new Referee
                {
                    ExternalId = user.UserId.ToString(),
                    FullName = user.FullName,
                    City = user.City,
                    Verified = false,
                    LevelSingle = user.RatingSingle ?? 0m,
                    LevelDouble = user.RatingDouble ?? 0m,
                    AvatarUrl = user.AvatarUrl,
                    RefereeType = refereeType,
                    Introduction = null,
                    WorkingArea = null,
                    Achievements = null
                };

                _db.Referees.Add(referee);
                await _db.SaveChangesAsync();

                var result = new RefereeDetailDto
                {
                    RefereeId = referee.RefereeId,
                    ExternalId = referee.ExternalId,
                    FullName = referee.FullName,
                    City = referee.City,
                    Verified = referee.Verified,
                    LevelSingle = referee.LevelSingle,
                    LevelDouble = referee.LevelDouble,
                    AvatarUrl = GetBaseUrl() + referee.AvatarUrl,
                    RefereeType = referee.RefereeType,
                    Introduction = referee.Introduction,
                    WorkingArea = referee.WorkingArea,
                    Achievements = referee.Achievements
                };

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
        /// Lấy hồ sơ trọng tài của user hiện tại nếu đã đăng ký
        /// </summary>
        [HttpGet("me")]
        public async Task<IActionResult> GetMyRefereeProfile()
        {
            var userId = await TryGetCurrentUserIdAsync();
            if (!userId.HasValue)
            {
                return Unauthorized(new { message = "JWT không hợp lệ hoặc không tìm thấy userId." });
            }

            var referee = await _db.Referees
                .AsNoTracking()
                .Where(x => x.ExternalId == userId.Value.ToString())
                .Select(x => new RefereeDetailDto
                {
                    RefereeId = x.RefereeId,
                    ExternalId = x.ExternalId,
                    FullName = x.FullName,
                    City = x.City,
                    Verified = x.Verified,
                    LevelSingle = x.LevelSingle,
                    LevelDouble = x.LevelDouble,
                    AvatarUrl = GetBaseUrl() + x.AvatarUrl,
                    RefereeType = x.RefereeType,
                    Introduction = x.Introduction,
                    WorkingArea = x.WorkingArea,
                    Achievements = x.Achievements
                })
                .FirstOrDefaultAsync();

            if (referee == null)
            {
                return NotFound(new { message = "Bạn chưa đăng ký trọng tài." });
            }

            return Ok(referee);
        }

        /// <summary>
        /// PUT /api/referees/me/profile
        /// Cập nhật hồ sơ trọng tài của chính user đang đăng nhập
        /// </summary>
        [HttpPut("me/profile")]
        public async Task<IActionResult> UpdateMyRefereeProfile([FromBody] UpdateMyRefereeProfileRequest req)
        {
            try
            {
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
                referee.UpdatedAt = DateTime.Now;

                await _db.SaveChangesAsync();

                var result = new RefereeDetailDto
                {
                    RefereeId = referee.RefereeId,
                    ExternalId = referee.ExternalId,
                    FullName = referee.FullName,
                    City = referee.City,
                    Verified = referee.Verified,
                    LevelSingle = referee.LevelSingle,
                    LevelDouble = referee.LevelDouble,
                    AvatarUrl = GetBaseUrl() + referee.AvatarUrl,
                    RefereeType = referee.RefereeType,
                    Introduction = referee.Introduction,
                    WorkingArea = referee.WorkingArea,
                    Achievements = referee.Achievements
                };

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