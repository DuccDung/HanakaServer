using HanakaServer.Data;
using HanakaServer.Models;
using HanakaServer.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers.Admin
{
    [Route("api/admin/referees")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminRefereesController : ControllerBase
    {
        private readonly PickleballDbContext _db;

        public AdminRefereesController(PickleballDbContext db)
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

            var q = _db.Referees.AsNoTracking().AsQueryable();

            keyword = (keyword ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                q = q.Where(x =>
                    x.FullName.Contains(keyword) ||
                    (x.City != null && x.City.Contains(keyword)) ||
                    (x.ExternalId != null && x.ExternalId.Contains(keyword)) ||
                    (x.RefereeType != null && x.RefereeType.Contains(keyword)));
            }

            verified = (verified ?? "ALL").Trim().ToUpperInvariant();
            if (verified == "TRUE")
                q = q.Where(x => x.Verified);
            else if (verified == "FALSE")
                q = q.Where(x => !x.Verified);

            var total = await q.CountAsync();

            var items = await q
                .OrderByDescending(x => x.RefereeId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.RefereeId,
                    x.ExternalId,
                    x.FullName,
                    x.City,
                    x.Verified,
                    x.LevelSingle,
                    x.LevelDouble,
                    x.AvatarUrl,
                    x.RefereeType,
                    x.Introduction,
                    x.WorkingArea,
                    x.Achievements,
                    x.CreatedAt,
                    x.UpdatedAt
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
            var x = await _db.Referees
                .AsNoTracking()
                .Where(x => x.RefereeId == id)
                .Select(x => new
                {
                    x.RefereeId,
                    x.ExternalId,
                    x.FullName,
                    x.City,
                    x.Verified,
                    x.LevelSingle,
                    x.LevelDouble,
                    x.AvatarUrl,
                    x.RefereeType,
                    x.Introduction,
                    x.WorkingArea,
                    x.Achievements,
                    x.CreatedAt,
                    x.UpdatedAt
                })
                .FirstOrDefaultAsync();

            if (x == null)
                return NotFound(new { message = "Không tìm thấy trọng tài." });

            return Ok(x);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] RefereeUpsertRequest req)
        {
            if (req == null)
                return BadRequest(new { message = "Dữ liệu không hợp lệ." });

            var fullName = (req.FullName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(fullName))
                return BadRequest(new { message = "Vui lòng nhập họ tên trọng tài." });

            var refereeType = string.IsNullOrWhiteSpace(req.RefereeType)
                ? "REFEREE"
                : req.RefereeType.Trim().ToUpperInvariant();

            if (!string.IsNullOrWhiteSpace(req.ExternalId))
            {
                var ext = req.ExternalId.Trim();
                var existsExt = await _db.Referees.AnyAsync(x => x.ExternalId == ext);
                if (existsExt)
                    return BadRequest(new { message = "ExternalId đã tồn tại." });
            }

            var entity = new Referee
            {
                ExternalId = string.IsNullOrWhiteSpace(req.ExternalId) ? null : req.ExternalId.Trim(),
                FullName = fullName,
                City = string.IsNullOrWhiteSpace(req.City) ? null : req.City.Trim(),
                Verified = req.Verified,
                LevelSingle = req.LevelSingle,
                LevelDouble = req.LevelDouble,
                AvatarUrl = string.IsNullOrWhiteSpace(req.AvatarUrl) ? null : req.AvatarUrl.Trim(),
                RefereeType = refereeType,
                Introduction = string.IsNullOrWhiteSpace(req.Introduction) ? null : req.Introduction.Trim(),
                WorkingArea = string.IsNullOrWhiteSpace(req.WorkingArea) ? null : req.WorkingArea.Trim(),
                Achievements = string.IsNullOrWhiteSpace(req.Achievements) ? null : req.Achievements.Trim(),
                CreatedAt = DateTime.Now,
                UpdatedAt = null
            };

            _db.Referees.Add(entity);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                entity.RefereeId,
                entity.ExternalId,
                entity.FullName,
                entity.City,
                entity.Verified,
                entity.LevelSingle,
                entity.LevelDouble,
                entity.AvatarUrl,
                entity.RefereeType,
                entity.Introduction,
                entity.WorkingArea,
                entity.Achievements,
                entity.CreatedAt,
                entity.UpdatedAt
            });
        }

        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] RefereeUpsertRequest req)
        {
            if (req == null)
                return BadRequest(new { message = "Dữ liệu không hợp lệ." });

            var entity = await _db.Referees.FirstOrDefaultAsync(x => x.RefereeId == id);
            if (entity == null)
                return NotFound(new { message = "Không tìm thấy trọng tài." });

            var fullName = (req.FullName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(fullName))
                return BadRequest(new { message = "Vui lòng nhập họ tên trọng tài." });

            var extId = string.IsNullOrWhiteSpace(req.ExternalId) ? null : req.ExternalId.Trim();
            if (!string.IsNullOrWhiteSpace(extId))
            {
                var existsExt = await _db.Referees.AnyAsync(x => x.ExternalId == extId && x.RefereeId != id);
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
            entity.RefereeType = string.IsNullOrWhiteSpace(req.RefereeType) ? "REFEREE" : req.RefereeType.Trim().ToUpperInvariant();
            entity.Introduction = string.IsNullOrWhiteSpace(req.Introduction) ? null : req.Introduction.Trim();
            entity.WorkingArea = string.IsNullOrWhiteSpace(req.WorkingArea) ? null : req.WorkingArea.Trim();
            entity.Achievements = string.IsNullOrWhiteSpace(req.Achievements) ? null : req.Achievements.Trim();
            entity.UpdatedAt = DateTime.Now;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                entity.RefereeId,
                entity.ExternalId,
                entity.FullName,
                entity.City,
                entity.Verified,
                entity.LevelSingle,
                entity.LevelDouble,
                entity.AvatarUrl,
                entity.RefereeType,
                entity.Introduction,
                entity.WorkingArea,
                entity.Achievements,
                entity.CreatedAt,
                entity.UpdatedAt
            });
        }

        [HttpPost("{id:long}/toggle-verified")]
        public async Task<IActionResult> ToggleVerified(long id)
        {
            var entity = await _db.Referees.FirstOrDefaultAsync(x => x.RefereeId == id);
            if (entity == null)
                return NotFound(new { message = "Không tìm thấy trọng tài." });

            entity.Verified = !entity.Verified;
            entity.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync();

            return Ok(new { ok = true, verified = entity.Verified });
        }

        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id)
        {
            var entity = await _db.Referees.FirstOrDefaultAsync(x => x.RefereeId == id);
            if (entity == null)
                return NotFound(new { message = "Không tìm thấy trọng tài." });

            _db.Referees.Remove(entity);
            await _db.SaveChangesAsync();

            return Ok(new { ok = true });
        }
    }
}