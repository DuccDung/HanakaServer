using HanakaServer.Data;
using HanakaServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/admin/users")]
    [Authorize(Roles = "Admin")]
    public class AdminUsersController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        public AdminUsersController(PickleballDbContext db) { _db = db; }

        private sealed class RatingSnapshot
        {
            public decimal? RatingSingle { get; set; }
            public decimal? RatingDouble { get; set; }
            public DateTime? RatedAt { get; set; }
        }

        private async Task<RatingSnapshot?> GetLatestRatingAsync(long userId)
        {
            return await _db.UserRatingHistories
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.RatedAt)
                .ThenByDescending(x => x.RatingHistoryId)
                .Select(x => new RatingSnapshot
                {
                    RatingSingle = x.RatingSingle,
                    RatingDouble = x.RatingDouble,
                    RatedAt = x.RatedAt
                })
                .FirstOrDefaultAsync();
        }

        private static decimal? NormalizeRating(decimal? value)
        {
            return value.HasValue ? Math.Round(value.Value, 2) : null;
        }

        private static bool IsValidRating(decimal? value)
        {
            return !value.HasValue || (value.Value >= 0m && value.Value <= 5m);
        }

        private long? GetRatedByUserId()
        {
            var raw = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            return long.TryParse(raw, out var userId) ? userId : null;
        }

        private async Task SyncCoachShadowFromUserAsync(User user, decimal? latestSingle, decimal? latestDouble)
        {
            var coach = await _db.Coaches.FirstOrDefaultAsync(x => x.ExternalId == user.UserId.ToString());
            if (coach == null) return;

            coach.FullName = user.FullName;
            coach.City = user.City;
            coach.AvatarUrl = user.AvatarUrl;

            if (latestSingle.HasValue)
                coach.LevelSingle = latestSingle.Value;

            if (latestDouble.HasValue)
                coach.LevelDouble = latestDouble.Value;
        }

        private async Task SyncRefereeShadowFromUserAsync(User user, decimal? latestSingle, decimal? latestDouble)
        {
            var referee = await _db.Referees.FirstOrDefaultAsync(x => x.ExternalId == user.UserId.ToString());
            if (referee == null) return;

            referee.FullName = user.FullName;
            referee.City = user.City;
            referee.AvatarUrl = user.AvatarUrl;

            if (latestSingle.HasValue)
                referee.LevelSingle = latestSingle.Value;

            if (latestDouble.HasValue)
                referee.LevelDouble = latestDouble.Value;

            referee.UpdatedAt = DateTime.UtcNow;
        }

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
                .Select(u => new
                {
                    u.UserId,
                    u.FullName,
                    u.AvatarUrl,
                    LegacyRatingSingle = u.RatingSingle,
                    LatestRating = _db.UserRatingHistories
                        .Where(r => r.UserId == u.UserId)
                        .OrderByDescending(r => r.RatedAt)
                        .ThenByDescending(r => r.RatingHistoryId)
                        .Select(r => new { r.RatingSingle })
                        .FirstOrDefault()
                })
                .Take(20)
                .ToListAsync();

            return Ok(users.Select(u => new
            {
                u.UserId,
                u.FullName,
                u.AvatarUrl,
                RatingSingle = u.LatestRating?.RatingSingle ?? u.LegacyRatingSingle
            }));
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

            var rows = await q.OrderByDescending(x => x.CreatedAt)
                              .Skip((page - 1) * pageSize)
                              .Take(pageSize)
                              .Select(x => new
                              {
                                  x.UserId,
                                  x.FullName,
                                  x.Email,
                                  x.Phone,
                                  x.City,
                                  x.Gender,
                                  x.Bio,
                                  x.BirthOfDate,
                                  x.Verified,
                                  x.IsActive,
                                  LegacyRatingSingle = x.RatingSingle,
                                  LegacyRatingDouble = x.RatingDouble,
                                  x.AvatarUrl,
                                  x.CreatedAt,
                                  x.UpdatedAt,
                                  LatestRating = _db.UserRatingHistories
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

            var items = rows.Select(x => new
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
                ratingSingle = x.LatestRating?.RatingSingle ?? x.LegacyRatingSingle,
                ratingDouble = x.LatestRating?.RatingDouble ?? x.LegacyRatingDouble,
                ratingUpdatedAt = x.LatestRating?.RatedAt,
                avatarUrl = x.AvatarUrl,
                createdAt = x.CreatedAt,
                updatedAt = x.UpdatedAt
            });

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
                    x.UserId,
                    x.FullName,
                    x.Email,
                    x.Phone,
                    x.City,
                    x.Gender,
                    x.Bio,
                    x.BirthOfDate,
                    x.Verified,
                    x.IsActive,
                    LegacyRatingSingle = x.RatingSingle,
                    LegacyRatingDouble = x.RatingDouble,
                    x.AvatarUrl,
                    x.CreatedAt,
                    x.UpdatedAt,
                    LatestRating = _db.UserRatingHistories
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

            if (u == null) return NotFound(new { message = "User not found." });
            return Ok(new
            {
                userId = u.UserId,
                fullName = u.FullName,
                email = u.Email,
                phone = u.Phone,
                city = u.City,
                gender = u.Gender,
                bio = u.Bio,
                birthOfDate = u.BirthOfDate,
                verified = u.Verified,
                isActive = u.IsActive,
                ratingSingle = u.LatestRating?.RatingSingle ?? u.LegacyRatingSingle,
                ratingDouble = u.LatestRating?.RatingDouble ?? u.LegacyRatingDouble,
                ratingUpdatedAt = u.LatestRating?.RatedAt,
                avatarUrl = u.AvatarUrl,
                createdAt = u.CreatedAt,
                updatedAt = u.UpdatedAt
            });
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

            if (!IsValidRating(dto.RatingSingle) || !IsValidRating(dto.RatingDouble))
                return BadRequest(new { message = "Điểm trình phải nằm trong khoảng 0 đến 5." });

            if (!string.IsNullOrWhiteSpace(dto.FullName)) u.FullName = dto.FullName.Trim();
            u.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
            u.Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();
            u.City = string.IsNullOrWhiteSpace(dto.City) ? null : dto.City.Trim();
            u.Gender = string.IsNullOrWhiteSpace(dto.Gender) ? null : dto.Gender.Trim();
            u.Bio = string.IsNullOrWhiteSpace(dto.Bio) ? null : dto.Bio.Trim();

            var latestRating = await GetLatestRatingAsync(u.UserId);
            var ratingWasSubmitted = dto.RatingSingle.HasValue || dto.RatingDouble.HasValue;
            var targetSingle = dto.RatingSingle.HasValue
                ? NormalizeRating(dto.RatingSingle)
                : latestRating?.RatingSingle ?? u.RatingSingle;
            var targetDouble = dto.RatingDouble.HasValue
                ? NormalizeRating(dto.RatingDouble)
                : latestRating?.RatingDouble ?? u.RatingDouble;

            if (ratingWasSubmitted)
            {
                var hasRatingValue = targetSingle.HasValue || targetDouble.HasValue;
                var ratingChanged = latestRating == null
                    || latestRating.RatingSingle != targetSingle
                    || latestRating.RatingDouble != targetDouble;

                if (hasRatingValue && ratingChanged)
                {
                    _db.UserRatingHistories.Add(new UserRatingHistory
                    {
                        UserId = u.UserId,
                        RatingSingle = targetSingle,
                        RatingDouble = targetDouble,
                        RatedByUserId = GetRatedByUserId(),
                        Note = "Admin cập nhật điểm trình.",
                        RatedAt = DateTime.UtcNow
                    });
                }

                u.RatingSingle = targetSingle;
                u.RatingDouble = targetDouble;
            }

            u.UpdatedAt = DateTime.UtcNow;
            await SyncCoachShadowFromUserAsync(u, u.RatingSingle, u.RatingDouble);
            await SyncRefereeShadowFromUserAsync(u, u.RatingSingle, u.RatingDouble);

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
                        legacyRatingSingle = x.RatingSingle,
                        legacyRatingDouble = x.RatingDouble,
                        latestRating = _db.UserRatingHistories
                            .Where(r => r.UserId == x.UserId)
                            .OrderByDescending(r => r.RatedAt)
                            .ThenByDescending(r => r.RatingHistoryId)
                            .Select(r => new
                            {
                                r.RatingSingle,
                                r.RatingDouble
                            })
                            .FirstOrDefault()
                    })
                    .FirstOrDefaultAsync();

                if (u == null) return NotFound(new { message = "User not found." });
                return Ok(new
                {
                    u.userId,
                    u.fullName,
                    u.avatarUrl,
                    ratingSingle = u.latestRating?.RatingSingle ?? u.legacyRatingSingle ?? 0m,
                    ratingDouble = u.latestRating?.RatingDouble ?? u.legacyRatingDouble ?? 0m
                });
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
                    legacyRatingSingle = u.RatingSingle,
                    legacyRatingDouble = u.RatingDouble,
                    latestRating = _db.UserRatingHistories
                        .Where(r => r.UserId == u.UserId)
                        .OrderByDescending(r => r.RatedAt)
                        .ThenByDescending(r => r.RatingHistoryId)
                        .Select(r => new
                        {
                            r.RatingSingle,
                            r.RatingDouble
                        })
                        .FirstOrDefault()
                })
                .Take(20)
                .ToListAsync();

            return Ok(users.Select(u => new
            {
                u.userId,
                u.fullName,
                u.avatarUrl,
                ratingSingle = u.latestRating?.RatingSingle ?? u.legacyRatingSingle ?? 0m,
                ratingDouble = u.latestRating?.RatingDouble ?? u.legacyRatingDouble ?? 0m
            }));
        }
    }
}
