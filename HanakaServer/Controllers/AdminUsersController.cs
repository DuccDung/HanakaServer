using HanakaServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/admin/users")]
    [Authorize(Roles = "Admin")]
    public class AdminUsersController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        public AdminUsersController(PickleballDbContext db) { _db = db; }

       

        // (optional) search nếu sau này bạn muốn autocomplete
        // GET /api/admin/users/search?q=duy
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string q)
        {
            q = (q ?? "").Trim();
            if (q.Length < 2) return Ok(Array.Empty<object>());

            var users = await _db.Users
                .Where(u => u.FullName.Contains(q))
                .OrderBy(u => u.FullName)
                .Select(u => new { u.UserId, u.FullName, u.AvatarUrl, u.RatingSingle })
                .Take(20)
                .ToListAsync();

            return Ok(users);
        }

        // GET /api/admin/users?keyword=&verified=&active=&page=&pageSize=
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? keyword,
                                              [FromQuery] bool? verified,
                                              [FromQuery] bool? active,
                                              [FromQuery] int page = 1,
                                              [FromQuery] int pageSize = 20)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var q = _db.Users.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var k = keyword.Trim();
                q = q.Where(x =>
                    x.FullName.Contains(k) ||
                    (x.Email != null && x.Email.Contains(k)) ||
                    (x.Phone != null && x.Phone.Contains(k)));
            }

            if (verified.HasValue) q = q.Where(x => x.Verified == verified.Value);
            if (active.HasValue) q = q.Where(x => x.IsActive == active.Value);

            var total = await q.CountAsync();

            var items = await q.OrderByDescending(x => x.CreatedAt)
                               .Skip((page - 1) * pageSize)
                               .Take(pageSize)
                               .Select(x => new
                               {
                                   userId = x.UserId,
                                   fullName = x.FullName,
                                   email = x.Email,
                                   phone = x.Phone,
                                   city = x.City,
                                   gender = x.Gender,
                                   bio = x.Bio,
                                   birthOfDate = x.BirthOfDate,
                                   verified = x.Verified,
                                   isActive = x.IsActive,
                                   ratingSingle = x.RatingSingle,
                                   ratingDouble = x.RatingDouble,
                                   avatarUrl = x.AvatarUrl,
                                   createdAt = x.CreatedAt,
                                   updatedAt = x.UpdatedAt
                               })
                               .ToListAsync();

            return Ok(new { total, page, pageSize, items });
        }

        // GET /api/admin/users/{id}
        [HttpGet("{id:long}")]
        public async Task<IActionResult> Detail(long id)
        {
            var u = await _db.Users.AsNoTracking()
                .Where(x => x.UserId == id)
                .Select(x => new
                {
                    userId = x.UserId,
                    fullName = x.FullName,
                    email = x.Email,
                    phone = x.Phone,
                    city = x.City,
                    gender = x.Gender,
                    bio = x.Bio,
                    birthOfDate = x.BirthOfDate,
                    verified = x.Verified,
                    isActive = x.IsActive,
                    ratingSingle = x.RatingSingle,
                    ratingDouble = x.RatingDouble,
                    avatarUrl = x.AvatarUrl,
                    createdAt = x.CreatedAt,
                    updatedAt = x.UpdatedAt
                })
                .FirstOrDefaultAsync();

            if (u == null) return NotFound(new { message = "User not found." });
            return Ok(u);
        }

        public class UpdateUserDto
        {
            public string? FullName { get; set; }
            public string? Email { get; set; }
            public string? Phone { get; set; }
            public string? City { get; set; }
            public string? Gender { get; set; }
            public string? Bio { get; set; }
            public decimal? RatingSingle { get; set; }
            public decimal? RatingDouble { get; set; }
        }

        // PUT /api/admin/users/{id}
        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] UpdateUserDto dto)
        {
            var u = await _db.Users.FirstOrDefaultAsync(x => x.UserId == id);
            if (u == null) return NotFound(new { message = "User not found." });

            if (!string.IsNullOrWhiteSpace(dto.FullName)) u.FullName = dto.FullName.Trim();
            u.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
            u.Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();
            u.City = string.IsNullOrWhiteSpace(dto.City) ? null : dto.City.Trim();
            u.Gender = string.IsNullOrWhiteSpace(dto.Gender) ? null : dto.Gender.Trim();
            u.Bio = string.IsNullOrWhiteSpace(dto.Bio) ? null : dto.Bio.Trim();

            u.RatingSingle = dto.RatingSingle;
            u.RatingDouble = dto.RatingDouble;

            u.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            // return updated detail (same shape as Detail)
            return await Detail(id);
        }

        // POST /api/admin/users/{id}/toggle-active
        [HttpPost("{id:long}/toggle-active")]
        public async Task<IActionResult> ToggleActive(long id)
        {
            var u = await _db.Users.FirstOrDefaultAsync(x => x.UserId == id);
            if (u == null) return NotFound(new { message = "User not found." });

            u.IsActive = !u.IsActive;
            u.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new { userId = u.UserId, isActive = u.IsActive });
        }

        // POST /api/admin/users/{id}/toggle-verified
        [HttpPost("{id:long}/toggle-verified")]
        public async Task<IActionResult> ToggleVerified(long id)
        {
            var u = await _db.Users.FirstOrDefaultAsync(x => x.UserId == id);
            if (u == null) return NotFound(new { message = "User not found." });

            u.Verified = !u.Verified;
            u.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new { userId = u.UserId, verified = u.Verified });
        }

        //=================================================================
        // GET /api/admin/users/123
        [HttpGet("find/{id:long}")]
        public async Task<IActionResult> GetById(long id)
        {
            try
            {
                var u = await _db.Users
                    .Where(x => x.UserId == id)
                    .Select(x => new
                    {
                        userId = x.UserId,
                        fullName = x.FullName,
                        avatarUrl = x.AvatarUrl,
                        ratingSingle = x.RatingSingle ?? 0m,
                        ratingDouble = x.RatingDouble ?? 0m
                    })
                    .FirstOrDefaultAsync();

                if (u == null) return NotFound(new { message = "User not found." });
                return Ok(u);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Get user failed.", detail = ex.Message });
            }
        }

        // GET /api/admin/users/search?q=...
        [HttpGet("search/check")]
        public async Task<IActionResult> SearchCheck([FromQuery] string q)
        {
            q = (q ?? "").Trim();
            if (q.Length < 2) return Ok(Array.Empty<object>());

            var users = await _db.Users
                .Where(u => u.FullName.Contains(q))
                .OrderBy(u => u.FullName)
                .Select(u => new
                {
                    userId = u.UserId,
                    fullName = u.FullName,
                    avatarUrl = u.AvatarUrl,
                    ratingSingle = u.RatingSingle ?? 0m,
                    ratingDouble = u.RatingDouble ?? 0m
                })
                .Take(20)
                .ToListAsync();

            return Ok(users);
        }
    }
}