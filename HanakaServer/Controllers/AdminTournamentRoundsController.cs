using HanakaServer.Data;
using HanakaServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/admin/tournaments/{tournamentId:long}/round-maps")]
    [Authorize(Roles = "Admin")]
    public class AdminTournamentRoundsController : ControllerBase
    {
        private readonly PickleballDbContext _db;

        public AdminTournamentRoundsController(PickleballDbContext db)
        {
            _db = db;
        }

        // GET: /api/admin/tournaments/{tournamentId}/round-maps
        [HttpGet]
        public async Task<IActionResult> List(long tournamentId)
        {
            var exists = await _db.Tournaments.AsNoTracking()
                .AnyAsync(x => x.TournamentId == tournamentId);
            if (!exists) return NotFound(new { message = "Tournament not found." });

            var items = await _db.TournamentRoundMaps.AsNoTracking()
                .Where(x => x.TournamentId == tournamentId)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.RoundKey)
                .Select(x => new
                {
                    x.TournamentRoundMapId,
                    x.TournamentId,
                    x.RoundKey,
                    x.RoundLabel,
                    x.SortOrder,
                    x.CreatedAt
                })
                .ToListAsync();

            return Ok(new { items });
        }

        // POST: /api/admin/tournaments/{tournamentId}/round-maps
        [HttpPost]
        public async Task<IActionResult> Create(long tournamentId, [FromBody] CreateRoundMapDto dto)
        {
            var key = (dto.RoundKey ?? "").Trim();
            var label = (dto.RoundLabel ?? "").Trim();

            if (string.IsNullOrWhiteSpace(key)) return BadRequest(new { message = "RoundKey is required." });
            if (string.IsNullOrWhiteSpace(label)) label = key;

            var tExists = await _db.Tournaments.AnyAsync(x => x.TournamentId == tournamentId);
            if (!tExists) return NotFound(new { message = "Tournament not found." });

            var exists = await _db.TournamentRoundMaps
                .AnyAsync(x => x.TournamentId == tournamentId && x.RoundKey == key);
            if (exists) return BadRequest(new { message = "RoundKey already exists in this tournament." });

            var row = new TournamentRoundMap
            {
                TournamentId = tournamentId,
                RoundKey = key,
                RoundLabel = label,
                SortOrder = dto.SortOrder ?? 0,
                CreatedAt = DateTime.UtcNow
            };

            _db.TournamentRoundMaps.Add(row);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                row.TournamentRoundMapId,
                row.TournamentId,
                row.RoundKey,
                row.RoundLabel,
                row.SortOrder,
                row.CreatedAt
            });
        }

        // PUT: /api/admin/tournaments/{tournamentId}/round-maps/{id}
        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long tournamentId, long id, [FromBody] UpdateRoundMapDto dto)
        {
            var row = await _db.TournamentRoundMaps
                .FirstOrDefaultAsync(x => x.TournamentRoundMapId == id && x.TournamentId == tournamentId);

            if (row == null) return NotFound(new { message = "Round not found." });

            if (dto.RoundLabel != null)
                row.RoundLabel = string.IsNullOrWhiteSpace(dto.RoundLabel) ? row.RoundKey : dto.RoundLabel.Trim();

            if (dto.SortOrder.HasValue)
                row.SortOrder = dto.SortOrder.Value;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                row.TournamentRoundMapId,
                row.TournamentId,
                row.RoundKey,
                row.RoundLabel,
                row.SortOrder,
                row.CreatedAt
            });
        }

        // DELETE: /api/admin/tournaments/{tournamentId}/round-maps/{id}
        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long tournamentId, long id)
        {
            var row = await _db.TournamentRoundMaps
                .FirstOrDefaultAsync(x => x.TournamentRoundMapId == id && x.TournamentId == tournamentId);

            if (row == null) return NotFound(new { message = "Round not found." });

            // vì FK cascade từ TournamentRoundMaps -> TournamentRoundGroups, match... bạn đã set CASCADE
            _db.TournamentRoundMaps.Remove(row);
            await _db.SaveChangesAsync();

            return Ok(new { ok = true });
        }
    }

    public class CreateRoundMapDto
    {
        public string? RoundKey { get; set; }
        public string? RoundLabel { get; set; }
        public int? SortOrder { get; set; }
    }

    public class UpdateRoundMapDto
    {
        public string? RoundLabel { get; set; }
        public int? SortOrder { get; set; }
    }
}