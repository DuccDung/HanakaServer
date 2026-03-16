using HanakaServer.Data;
using HanakaServer.Models;
using HanakaServer.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers.Admin
{
    [Route("api/admin/coaches")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminCoachesController : ControllerBase
    {
        private readonly PickleballDbContext _db;

        public AdminCoachesController(PickleballDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> GetList(
            [FromQuery] string? keyword,
            [FromQuery] string verified = "ALL",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            page = page <= 0 ? 1 : page;
            pageSize = pageSize <= 0 ? 20 : pageSize;

            var q = _db.Coaches.AsNoTracking().AsQueryable();

            keyword = (keyword ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                q = q.Where(x =>
                    x.FullName.Contains(keyword) ||
                    (x.City != null && x.City.Contains(keyword)) ||
                    (x.ExternalId != null && x.ExternalId.Contains(keyword)) ||
                    (x.CoachType != null && x.CoachType.Contains(keyword)));
            }

            verified = (verified ?? "ALL").Trim().ToUpperInvariant();
            if (verified == "TRUE")
                q = q.Where(x => x.Verified);
            else if (verified == "FALSE")
                q = q.Where(x => !x.Verified);

            var total = await q.CountAsync();

            var items = await q
                .OrderByDescending(x => x.CoachId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.CoachId,
                    x.ExternalId,
                    x.FullName,
                    x.City,
                    x.Verified,
                    x.LevelSingle,
                    x.LevelDouble,
                    x.AvatarUrl,
                    x.CoachType,
                    x.Introduction,
                    x.TeachingArea,
                    x.Achievements
                })
                .ToListAsync();

            return Ok(new
            {
                total,
                page,
                pageSize,
                items
            });
        }

        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetDetail(long id)
        {
            var x = await _db.Coaches
                .AsNoTracking()
                .Where(x => x.CoachId == id)
                .Select(x => new
                {
                    x.CoachId,
                    x.ExternalId,
                    x.FullName,
                    x.City,
                    x.Verified,
                    x.LevelSingle,
                    x.LevelDouble,
                    x.AvatarUrl,
                    x.CoachType,
                    x.Introduction,
                    x.TeachingArea,
                    x.Achievements
                })
                .FirstOrDefaultAsync();

            if (x == null)
                return NotFound(new { message = "Không tìm thấy coach." });

            return Ok(x);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CoachUpsertRequest req)
        {
            if (req == null)
                return BadRequest(new { message = "Dữ liệu không hợp lệ." });

            var fullName = (req.FullName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(fullName))
                return BadRequest(new { message = "Vui lòng nhập họ tên coach." });

            var coachType = string.IsNullOrWhiteSpace(req.CoachType)
                ? "COACH"
                : req.CoachType.Trim().ToUpperInvariant();

            if (!string.IsNullOrWhiteSpace(req.ExternalId))
            {
                var ext = req.ExternalId.Trim();
                var existsExt = await _db.Coaches.AnyAsync(x => x.ExternalId == ext);
                if (existsExt)
                    return BadRequest(new { message = "ExternalId đã tồn tại." });
            }

            var entity = new Coach
            {
                ExternalId = string.IsNullOrWhiteSpace(req.ExternalId) ? null : req.ExternalId.Trim(),
                FullName = fullName,
                City = string.IsNullOrWhiteSpace(req.City) ? null : req.City.Trim(),
                Verified = req.Verified,
                LevelSingle = req.LevelSingle,
                LevelDouble = req.LevelDouble,
                AvatarUrl = string.IsNullOrWhiteSpace(req.AvatarUrl) ? null : req.AvatarUrl.Trim(),
                CoachType = coachType,
                Introduction = string.IsNullOrWhiteSpace(req.Introduction) ? null : req.Introduction.Trim(),
                TeachingArea = string.IsNullOrWhiteSpace(req.TeachingArea) ? null : req.TeachingArea.Trim(),
                Achievements = string.IsNullOrWhiteSpace(req.Achievements) ? null : req.Achievements.Trim()
            };

            _db.Coaches.Add(entity);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                entity.CoachId,
                entity.ExternalId,
                entity.FullName,
                entity.City,
                entity.Verified,
                entity.LevelSingle,
                entity.LevelDouble,
                entity.AvatarUrl,
                entity.CoachType,
                entity.Introduction,
                entity.TeachingArea,
                entity.Achievements
            });
        }

        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] CoachUpsertRequest req)
        {
            if (req == null)
                return BadRequest(new { message = "Dữ liệu không hợp lệ." });

            var entity = await _db.Coaches.FirstOrDefaultAsync(x => x.CoachId == id);
            if (entity == null)
                return NotFound(new { message = "Không tìm thấy coach." });

            var fullName = (req.FullName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(fullName))
                return BadRequest(new { message = "Vui lòng nhập họ tên coach." });

            var extId = string.IsNullOrWhiteSpace(req.ExternalId) ? null : req.ExternalId.Trim();
            if (!string.IsNullOrWhiteSpace(extId))
            {
                var existsExt = await _db.Coaches.AnyAsync(x => x.ExternalId == extId && x.CoachId != id);
                if (existsExt)
                    return BadRequest(new { message = "ExternalId đã tồn tại." });
            }

            entity.ExternalId = extId;
            entity.FullName = fullName;
            entity.City = string.IsNullOrWhiteSpace(req.City) ? null : req.City.Trim();
            entity.Verified = req.Verified;
            entity.LevelSingle = req.LevelSingle;
            entity.LevelDouble = req.LevelDouble;
            entity.AvatarUrl = string.IsNullOrWhiteSpace(req.AvatarUrl) ? null : req.AvatarUrl.Trim();
            entity.CoachType = string.IsNullOrWhiteSpace(req.CoachType) ? "COACH" : req.CoachType.Trim().ToUpperInvariant();
            entity.Introduction = string.IsNullOrWhiteSpace(req.Introduction) ? null : req.Introduction.Trim();
            entity.TeachingArea = string.IsNullOrWhiteSpace(req.TeachingArea) ? null : req.TeachingArea.Trim();
            entity.Achievements = string.IsNullOrWhiteSpace(req.Achievements) ? null : req.Achievements.Trim();

            await _db.SaveChangesAsync();

            return Ok(new
            {
                entity.CoachId,
                entity.ExternalId,
                entity.FullName,
                entity.City,
                entity.Verified,
                entity.LevelSingle,
                entity.LevelDouble,
                entity.AvatarUrl,
                entity.CoachType,
                entity.Introduction,
                entity.TeachingArea,
                entity.Achievements
            });
        }

        [HttpPost("{id:long}/toggle-verified")]
        public async Task<IActionResult> ToggleVerified(long id)
        {
            var entity = await _db.Coaches.FirstOrDefaultAsync(x => x.CoachId == id);
            if (entity == null)
                return NotFound(new { message = "Không tìm thấy coach." });

            entity.Verified = !entity.Verified;
            await _db.SaveChangesAsync();

            return Ok(new { ok = true, verified = entity.Verified });
        }

        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id)
        {
            var entity = await _db.Coaches.FirstOrDefaultAsync(x => x.CoachId == id);
            if (entity == null)
                return NotFound(new { message = "Không tìm thấy coach." });

            _db.Coaches.Remove(entity);
            await _db.SaveChangesAsync();

            return Ok(new { ok = true });
        }
    }
}