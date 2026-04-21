using HanakaServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers.Api
{
    [Route("api/admin/referees")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminRefereeLookupApiController : ControllerBase
    {
        private readonly PickleballDbContext _db;

        public AdminRefereeLookupApiController(PickleballDbContext db)
        {
            _db = db;
        }

        [HttpGet("find-by-user-id/{userId:long}")]
        public async Task<IActionResult> FindByUserId(long userId)
        {
            var user = await _db.Users
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .Select(x => new
                {
                    x.UserId,
                    x.FullName,
                    x.Phone,
                    x.Email,
                    x.City,
                    x.AvatarUrl,
                    x.Verified,
                    x.IsActive,
                    x.RatingSingle,
                    x.RatingDouble
                })
                .FirstOrDefaultAsync();

            if (user == null)
                return NotFound(new { message = "Không tìm thấy user." });

            if (!user.IsActive)
                return BadRequest(new { message = "User này đang bị vô hiệu hóa, không thể gán làm trọng tài." });

            var referee = await _db.Referees
                .AsNoTracking()
                .Where(x => x.ExternalId == userId.ToString())
                .Select(x => new
                {
                    x.RefereeId,
                    x.Verified,
                    x.RefereeType
                })
                .FirstOrDefaultAsync();

            if (referee == null)
                return BadRequest(new { message = "User này chưa có hồ sơ trọng tài." });

            if (!referee.Verified)
                return BadRequest(new { message = "Hồ sơ trọng tài này chưa được xác minh." });

            return Ok(new
            {
                user.UserId,
                user.FullName,
                user.Phone,
                user.Email,
                user.City,
                user.AvatarUrl,
                user.Verified,
                user.IsActive,
                user.RatingSingle,
                user.RatingDouble,
                referee.RefereeId,
                RefereeVerified = referee.Verified,
                referee.RefereeType
            });
        }
    }
}
