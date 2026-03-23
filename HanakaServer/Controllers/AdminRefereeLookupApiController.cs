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

            return Ok(user);
        }
    }
}