using HanakaServer.Data;
using HanakaServer.Helpers;
using HanakaServer.Models;
using HanakaServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/rating-assessors")]
    [Authorize(Roles = RoleCodes.Admin)]
    public class AdminRatingAssessorsController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly IUserRatingService _ratingService;

        public AdminRatingAssessorsController(PickleballDbContext db, IUserRatingService ratingService)
        {
            _db = db;
            _ratingService = ratingService;
        }

        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] string? keyword,
            [FromQuery] bool? active,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken ct = default)
        {
            page = page <= 0 ? 1 : page;
            pageSize = pageSize <= 0 ? 20 : pageSize;
            pageSize = pageSize > 100 ? 100 : pageSize;

            var role = await EnsureRatingAssessorRoleAsync(ct);

            var q = _db.Users
                .AsNoTracking()
                .Where(x => x.UserRoles.Any(ur => ur.RoleId == role.RoleId));

            keyword = keyword?.Trim();
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                q = q.Where(x =>
                    x.UserId.ToString().Contains(keyword) ||
                    x.FullName.Contains(keyword) ||
                    (x.Email != null && x.Email.Contains(keyword)) ||
                    (x.Phone != null && x.Phone.Contains(keyword)) ||
                    (x.City != null && x.City.Contains(keyword)));
            }

            if (active.HasValue)
                q = q.Where(x => x.IsActive == active.Value);

            var total = await q.CountAsync(ct);

            var rows = await q
                .OrderByDescending(x => x.UserRoles
                    .Where(ur => ur.RoleId == role.RoleId)
                    .Select(ur => ur.CreatedAt)
                    .FirstOrDefault())
                .ThenByDescending(x => x.UserId)
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
                    x.AvatarUrl,
                    x.Verified,
                    x.IsActive,
                    x.CreatedAt,
                    x.UpdatedAt,
                    AssessorSince = x.UserRoles
                        .Where(ur => ur.RoleId == role.RoleId)
                        .Select(ur => (DateTime?)ur.CreatedAt)
                        .FirstOrDefault()
                })
                .ToListAsync(ct);

            var items = new List<object>();
            foreach (var row in rows)
            {
                var latestRating = await _ratingService.GetLatestRatingAsync(row.UserId, ct);
                items.Add(MapAssessor(row, latestRating));
            }

            return Ok(new
            {
                total,
                page,
                pageSize,
                items
            });
        }

        [HttpGet("lookup-user")]
        public async Task<IActionResult> LookupUser(
            [FromQuery] long? userId,
            [FromQuery] string? keyword,
            CancellationToken ct = default)
        {
            var role = await EnsureRatingAssessorRoleAsync(ct);

            if (userId.HasValue)
            {
                var user = await _db.Users
                    .AsNoTracking()
                    .Where(x => x.UserId == userId.Value)
                    .Select(x => new
                    {
                        x.UserId,
                        x.FullName,
                        x.Email,
                        x.Phone,
                        x.City,
                        x.Gender,
                        x.AvatarUrl,
                        x.Verified,
                        x.IsActive,
                        x.CreatedAt,
                        x.UpdatedAt,
                        HasAssessorRole = x.UserRoles.Any(ur => ur.RoleId == role.RoleId)
                    })
                    .FirstOrDefaultAsync(ct);

                if (user == null)
                    return NotFound(new { message = "Không tìm thấy user." });

                var latestRating = await _ratingService.GetLatestRatingAsync(user.UserId, ct);
                return Ok(MapLookupUser(user, latestRating));
            }

            keyword = keyword?.Trim();
            if (string.IsNullOrWhiteSpace(keyword) || keyword.Length < 2)
                return Ok(new { items = Array.Empty<object>() });

            var users = await _db.Users
                .AsNoTracking()
                .Where(x =>
                    x.UserId.ToString().Contains(keyword) ||
                    x.FullName.Contains(keyword) ||
                    (x.Email != null && x.Email.Contains(keyword)) ||
                    (x.Phone != null && x.Phone.Contains(keyword)))
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.FullName)
                .Take(20)
                .Select(x => new
                {
                    x.UserId,
                    x.FullName,
                    x.Email,
                    x.Phone,
                    x.City,
                    x.Gender,
                    x.AvatarUrl,
                    x.Verified,
                    x.IsActive,
                    x.CreatedAt,
                    x.UpdatedAt,
                    HasAssessorRole = x.UserRoles.Any(ur => ur.RoleId == role.RoleId)
                })
                .ToListAsync(ct);

            var items = new List<object>();
            foreach (var user in users)
            {
                var latestRating = await _ratingService.GetLatestRatingAsync(user.UserId, ct);
                items.Add(MapLookupUser(user, latestRating));
            }

            return Ok(new { items });
        }

        [HttpPost]
        public async Task<IActionResult> EnableFromBody(
            [FromBody] RatingAssessorEnableRequest req,
            CancellationToken ct = default)
        {
            if (req == null || req.UserId <= 0)
                return BadRequest(new { message = "Thiếu UserId." });

            return await Enable(req.UserId, ct);
        }

        [HttpPost("{userId:long}/enable")]
        public async Task<IActionResult> Enable(long userId, CancellationToken ct = default)
        {
            var user = await _db.Users.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy user." });

            if (!user.IsActive)
                return BadRequest(new { message = "User đang bị khóa, không thể gán quyền chấm trình." });

            var role = await EnsureRatingAssessorRoleAsync(ct);

            var hasRole = await _db.UserRoles
                .AnyAsync(x => x.UserId == userId && x.RoleId == role.RoleId, ct);

            if (!hasRole)
            {
                _db.UserRoles.Add(new UserRole
                {
                    UserId = user.UserId,
                    RoleId = role.RoleId,
                    CreatedAt = DateTime.UtcNow
                });

                await _db.SaveChangesAsync(ct);
            }

            return Ok(await BuildAssessorDetailAsync(userId, role.RoleId, ct));
        }

        [HttpPost("{userId:long}/disable")]
        public async Task<IActionResult> Disable(long userId, CancellationToken ct = default)
        {
            var role = await EnsureRatingAssessorRoleAsync(ct);
            var userRole = await _db.UserRoles
                .FirstOrDefaultAsync(x => x.UserId == userId && x.RoleId == role.RoleId, ct);

            if (userRole == null)
                return NotFound(new { message = "User chưa có quyền chấm trình." });

            _db.UserRoles.Remove(userRole);
            await _db.SaveChangesAsync(ct);

            return Ok(new
            {
                userId,
                hasAssessorRole = false
            });
        }

        private async Task<object?> BuildAssessorDetailAsync(long userId, int roleId, CancellationToken ct)
        {
            var row = await _db.Users
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .Select(x => new
                {
                    x.UserId,
                    x.FullName,
                    x.Email,
                    x.Phone,
                    x.City,
                    x.Gender,
                    x.AvatarUrl,
                    x.Verified,
                    x.IsActive,
                    x.CreatedAt,
                    x.UpdatedAt,
                    AssessorSince = x.UserRoles
                        .Where(ur => ur.RoleId == roleId)
                        .Select(ur => (DateTime?)ur.CreatedAt)
                        .FirstOrDefault()
                })
                .FirstOrDefaultAsync(ct);

            if (row == null) return null;

            var latestRating = await _ratingService.GetLatestRatingAsync(row.UserId, ct);
            return MapAssessor(row, latestRating);
        }

        private async Task<Role> EnsureRatingAssessorRoleAsync(CancellationToken ct)
        {
            var role = await _db.Roles
                .FirstOrDefaultAsync(x => x.RoleCode == RoleCodes.RatingAssessor, ct);

            if (role != null) return role;

            role = new Role
            {
                RoleCode = RoleCodes.RatingAssessor,
                RoleName = "Người chấm trình"
            };

            _db.Roles.Add(role);
            await _db.SaveChangesAsync(ct);
            return role;
        }

        private static object MapAssessor(dynamic row, UserRatingSnapshot? latestRating)
        {
            return new
            {
                userId = (long)row.UserId,
                fullName = (string?)row.FullName,
                email = (string?)row.Email,
                phone = (string?)row.Phone,
                city = (string?)row.City,
                gender = (string?)row.Gender,
                avatarUrl = (string?)row.AvatarUrl,
                verified = (bool)row.Verified,
                isActive = (bool)row.IsActive,
                ratingSingle = latestRating?.RatingSingle,
                ratingDouble = latestRating?.RatingDouble,
                ratingUpdatedAt = latestRating?.RatedAt,
                assessorSince = (DateTime?)row.AssessorSince,
                createdAt = (DateTime)row.CreatedAt,
                updatedAt = (DateTime?)row.UpdatedAt,
                hasAssessorRole = true
            };
        }

        private static object MapLookupUser(dynamic row, UserRatingSnapshot? latestRating)
        {
            return new
            {
                userId = (long)row.UserId,
                fullName = (string?)row.FullName,
                email = (string?)row.Email,
                phone = (string?)row.Phone,
                city = (string?)row.City,
                gender = (string?)row.Gender,
                avatarUrl = (string?)row.AvatarUrl,
                verified = (bool)row.Verified,
                isActive = (bool)row.IsActive,
                ratingSingle = latestRating?.RatingSingle,
                ratingDouble = latestRating?.RatingDouble,
                ratingUpdatedAt = latestRating?.RatedAt,
                hasAssessorRole = (bool)row.HasAssessorRole,
                createdAt = (DateTime)row.CreatedAt,
                updatedAt = (DateTime?)row.UpdatedAt
            };
        }
    }

    public sealed class RatingAssessorEnableRequest
    {
        public long UserId { get; set; }
    }
}
