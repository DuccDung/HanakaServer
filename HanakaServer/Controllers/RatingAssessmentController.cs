using HanakaServer.Data;
using HanakaServer.Helpers;
using HanakaServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/rating-assessment")]
    [Authorize(Roles = $"{RoleCodes.RatingAssessor},{RoleCodes.Admin}")]
    public class RatingAssessmentController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly IUserRatingService _ratingService;

        public RatingAssessmentController(PickleballDbContext db, IUserRatingService ratingService)
        {
            _db = db;
            _ratingService = ratingService;
        }

        [HttpGet("users")]
        public async Task<IActionResult> ListUsers(
            [FromQuery] string? keyword,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken ct = default)
        {
            page = page <= 0 ? 1 : page;
            pageSize = pageSize <= 0 ? 20 : pageSize;
            pageSize = pageSize > 100 ? 100 : pageSize;

            var q = _db.Users
                .AsNoTracking()
                .Where(x => x.IsActive);

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

            var total = await q.CountAsync(ct);

            var rows = await q
                .OrderBy(x => x.FullName)
                .ThenBy(x => x.UserId)
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
                    x.CreatedAt
                })
                .ToListAsync(ct);

            var items = new List<object>();
            foreach (var row in rows)
            {
                var latestRating = await _ratingService.GetLatestRatingAsync(row.UserId, ct);
                items.Add(MapUser(row, latestRating));
            }

            return Ok(new
            {
                total,
                page,
                pageSize,
                items
            });
        }

        [HttpGet("users/{userId:long}")]
        public async Task<IActionResult> GetUser(long userId, CancellationToken ct = default)
        {
            var row = await _db.Users
                .AsNoTracking()
                .Where(x => x.UserId == userId && x.IsActive)
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
                    x.Bio,
                    x.BirthOfDate,
                    x.CreatedAt
                })
                .FirstOrDefaultAsync(ct);

            if (row == null)
                return NotFound(new { message = "Không tìm thấy vận động viên." });

            var latestRating = await _ratingService.GetLatestRatingAsync(userId, ct);
            var history = await _ratingService.GetRatingHistoryAsync(userId, 30, ct);

            return Ok(new
            {
                user = MapUser(row, latestRating),
                bio = row.Bio,
                birthOfDate = row.BirthOfDate,
                ratingHistory = history
            });
        }

        [HttpGet("users/{userId:long}/rating-history")]
        public async Task<IActionResult> GetRatingHistory(long userId, CancellationToken ct = default)
        {
            var exists = await _db.Users
                .AsNoTracking()
                .AnyAsync(x => x.UserId == userId && x.IsActive, ct);

            if (!exists)
                return NotFound(new { message = "Không tìm thấy vận động viên." });

            var history = await _ratingService.GetRatingHistoryAsync(userId, 100, ct);
            return Ok(new
            {
                userId,
                total = history.Count,
                items = history
            });
        }

        [HttpPost("users/{userId:long}/rating")]
        public async Task<IActionResult> SetRating(
            long userId,
            [FromBody] RatingAssessmentUpdateRequest req,
            CancellationToken ct = default)
        {
            if (req == null)
                return BadRequest(new { message = "Dữ liệu không hợp lệ." });

            if (!IsValidRating(req.RatingSingle) || !IsValidRating(req.RatingDouble))
                return BadRequest(new { message = "Điểm trình phải nằm trong khoảng 0 đến 5." });

            var note = req.Note?.Trim();
            if (string.IsNullOrWhiteSpace(note))
                return BadRequest(new { message = "Ghi chú là bắt buộc khi chấm trình." });

            if (note.Length > 400)
                return BadRequest(new { message = "Ghi chú không được vượt quá 400 ký tự." });

            var currentUserId = GetCurrentUserId();
            if (currentUserId.HasValue && currentUserId.Value == userId)
                return BadRequest(new { message = "Nhân viên chấm trình không được tự chấm chính mình." });

            var staffLabel = currentUserId?.ToString() ?? "Admin";

            var result = await _ratingService.SetRatingFromHanakaStaffAsync(
                userId,
                req.RatingSingle,
                req.RatingDouble,
                currentUserId,
                staffLabel,
                note,
                ct);

            if (result == null)
                return NotFound(new { message = "Không tìm thấy vận động viên đang hoạt động." });

            return Ok(new
            {
                message = "Chấm trình thành công.",
                result.UserId,
                result.FullName,
                result.RatingSingle,
                result.RatingDouble,
                result.History
            });
        }

        private long? GetCurrentUserId()
        {
            var raw = User.FindFirstValue("UserId")
                ?? User.FindFirstValue("uid")
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

            return long.TryParse(raw, out var userId) ? userId : null;
        }

        private static bool IsValidRating(decimal value)
        {
            return value >= 0m && value <= 5m;
        }

        private static object MapUser(dynamic row, UserRatingSnapshot? latestRating)
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
                ratingSingle = latestRating?.RatingSingle,
                ratingDouble = latestRating?.RatingDouble,
                ratingUpdatedAt = latestRating?.RatedAt,
                createdAt = (DateTime)row.CreatedAt
            };
        }
    }

    public sealed class RatingAssessmentUpdateRequest
    {
        public decimal RatingSingle { get; set; }
        public decimal RatingDouble { get; set; }
        public string? Note { get; set; }
    }
}
