using HanakaServer.Data;
using HanakaServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/admin/round-maps/{roundMapId:long}/groups")]
    [Authorize(Roles = "Admin")]
    public class AdminTournamentRoundGroupsController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        public AdminTournamentRoundGroupsController(PickleballDbContext db) { _db = db; }

        // GET: /api/admin/round-maps/{roundMapId}/groups
        [HttpGet]
        public async Task<IActionResult> List(long roundMapId)
        {
            var rm = await _db.TournamentRoundMaps.AsNoTracking()
                .Where(x => x.TournamentRoundMapId == roundMapId)
                .Select(x => new { x.TournamentRoundMapId, x.TournamentId, x.RoundKey, x.RoundLabel })
                .FirstOrDefaultAsync();

            if (rm == null) return NotFound(new { message = "RoundMap not found." });

            var items = await _db.TournamentRoundGroups.AsNoTracking()
                .Where(x => x.TournamentRoundMapId == roundMapId)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.GroupName)
                .Select(x => new
                {
                    x.TournamentRoundGroupId,
                    x.TournamentRoundMapId,
                    x.GroupName,
                    x.SortOrder,
                    x.CreatedAt
                })
                .ToListAsync();

            return Ok(new { roundMap = rm, items });
        }

        // POST: /api/admin/round-maps/{roundMapId}/groups
        [HttpPost]
        public async Task<IActionResult> Create(long roundMapId, [FromBody] CreateGroupDto dto)
        {
            var rm = await _db.TournamentRoundMaps.FirstOrDefaultAsync(x => x.TournamentRoundMapId == roundMapId);
            if (rm == null) return NotFound(new { message = "RoundMap not found." });

            var name = (dto.GroupName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { message = "GroupName is required." });

            var exists = await _db.TournamentRoundGroups.AnyAsync(x =>
                x.TournamentRoundMapId == roundMapId && x.GroupName == name);

            if (exists) return BadRequest(new { message = "GroupName already exists in this round." });

            var g = new TournamentRoundGroup
            {
                TournamentRoundMapId = roundMapId,
                GroupName = name,
                SortOrder = dto.SortOrder ?? 0,
                CreatedAt = DateTime.UtcNow
            };

            _db.TournamentRoundGroups.Add(g);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                g.TournamentRoundGroupId,
                g.TournamentRoundMapId,
                g.GroupName,
                g.SortOrder,
                g.CreatedAt
            });
        }

        // PUT: /api/admin/round-maps/{roundMapId}/groups/{groupId}
        [HttpPut("{groupId:long}")]
        public async Task<IActionResult> Update(long roundMapId, long groupId, [FromBody] UpdateGroupDto dto)
        {
            var g = await _db.TournamentRoundGroups
                .FirstOrDefaultAsync(x => x.TournamentRoundGroupId == groupId && x.TournamentRoundMapId == roundMapId);

            if (g == null) return NotFound(new { message = "Group not found." });

            if (dto.GroupName != null)
            {
                var name = dto.GroupName.Trim();
                if (string.IsNullOrWhiteSpace(name))
                    return BadRequest(new { message = "GroupName cannot be empty." });

                var exists = await _db.TournamentRoundGroups.AnyAsync(x =>
                    x.TournamentRoundMapId == roundMapId && x.GroupName == name && x.TournamentRoundGroupId != groupId);

                if (exists) return BadRequest(new { message = "GroupName already exists in this round." });

                g.GroupName = name;
            }

            if (dto.SortOrder.HasValue) g.SortOrder = dto.SortOrder.Value;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                g.TournamentRoundGroupId,
                g.TournamentRoundMapId,
                g.GroupName,
                g.SortOrder,
                g.CreatedAt
            });
        }

        // DELETE: /api/admin/round-maps/{roundMapId}/groups/{groupId}
        [HttpDelete("{groupId:long}")]
        public async Task<IActionResult> Delete(long roundMapId, long groupId)
        {
            var g = await _db.TournamentRoundGroups
                .FirstOrDefaultAsync(x => x.TournamentRoundGroupId == groupId && x.TournamentRoundMapId == roundMapId);

            if (g == null) return NotFound(new { message = "Group not found." });

            var hasMatches = await _db.TournamentGroupMatches
                .AsNoTracking()
                .AnyAsync(x => x.TournamentRoundGroupId == groupId);

            if (hasMatches)
            {
                return BadRequest(new
                {
                    message = "Không xóa được bảng đấu vì vẫn còn trận đấu bên trong."
                });
            }

            _db.TournamentRoundGroups.Remove(g);
            await _db.SaveChangesAsync();

            return Ok(new { ok = true });
        }
    }

    public class CreateGroupDto
    {
        public string? GroupName { get; set; }
        public int? SortOrder { get; set; }
    }

    public class UpdateGroupDto
    {
        public string? GroupName { get; set; }
        public int? SortOrder { get; set; }
    }
}
