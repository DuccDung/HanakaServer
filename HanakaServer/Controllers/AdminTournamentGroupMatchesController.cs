using HanakaServer.Data;
using HanakaServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/admin/groups/{groupId:long}/matches")]
    [Authorize(Roles = "Admin")]
    public class AdminTournamentGroupMatchesController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        public AdminTournamentGroupMatchesController(PickleballDbContext db) { _db = db; }

        // GET /api/admin/groups/{groupId}/matches
        [HttpGet]
        public async Task<IActionResult> List(long groupId)
        {
            var g = await _db.TournamentRoundGroups.AsNoTracking()
                .Where(x => x.TournamentRoundGroupId == groupId)
                .Select(x => new { x.TournamentRoundGroupId, x.TournamentRoundMapId, x.GroupName })
                .FirstOrDefaultAsync();

            if (g == null) return NotFound(new { message = "Group not found." });

            var rm = await _db.TournamentRoundMaps.AsNoTracking()
                .Where(x => x.TournamentRoundMapId == g.TournamentRoundMapId)
                .Select(x => new { x.TournamentRoundMapId, x.TournamentId, x.RoundKey, x.RoundLabel })
                .FirstOrDefaultAsync();

            if (rm == null) return NotFound(new { message = "RoundMap not found." });

            var t = await _db.Tournaments.AsNoTracking()
                .Where(x => x.TournamentId == rm.TournamentId)
                .Select(x => new { x.TournamentId, x.Title, x.Status, x.GameType })
                .FirstOrDefaultAsync();

            // Load matches + join registrations (để show tên đội)
            var items = await (
                from m in _db.TournamentGroupMatches.AsNoTracking()
                where m.TournamentRoundGroupId == groupId
                join r1 in _db.TournamentRegistrations.AsNoTracking() on m.Team1RegistrationId equals r1.RegistrationId
                join r2 in _db.TournamentRegistrations.AsNoTracking() on m.Team2RegistrationId equals r2.RegistrationId
                orderby (m.StartAt ?? DateTime.MaxValue), m.MatchId
                select new
                {
                    m.MatchId,
                    m.TournamentRoundGroupId,
                    m.TournamentId,

                    m.Team1RegistrationId,
                    Team1Text = BuildTeamText(t!.GameType ?? "DOUBLE", r1),

                    m.Team2RegistrationId,
                    Team2Text = BuildTeamText(t!.GameType ?? "DOUBLE", r2),

                    m.StartAt,
                    m.AddressText,
                    m.CourtText,

                    m.ScoreTeam1,
                    m.ScoreTeam2,
                    m.IsCompleted,
                    m.WinnerRegistrationId,

                    WinnerTeam = m.WinnerRegistrationId == null ? null :
                        (m.WinnerRegistrationId == m.Team1RegistrationId ? "1" : "2"),

                    m.CreatedAt,
                    m.UpdatedAt
                }
            ).ToListAsync();

            return Ok(new { tournament = t, roundMap = rm, group = g, items });
        }

        // POST /api/admin/groups/{groupId}/matches
        [HttpPost]
        public async Task<IActionResult> Create(long groupId, [FromBody] CreateMatchDto dto)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var g = await _db.TournamentRoundGroups.FirstOrDefaultAsync(x => x.TournamentRoundGroupId == groupId);
            if (g == null) return NotFound(new { message = "Group not found." });

            var rm = await _db.TournamentRoundMaps.FirstOrDefaultAsync(x => x.TournamentRoundMapId == g.TournamentRoundMapId);
            if (rm == null) return NotFound(new { message = "RoundMap not found." });

            if (dto.Team1RegistrationId <= 0 || dto.Team2RegistrationId <= 0)
                return BadRequest(new { message = "Team1RegistrationId and Team2RegistrationId are required." });

            if (dto.Team1RegistrationId == dto.Team2RegistrationId)
                return BadRequest(new { message = "Team1 and Team2 cannot be the same." });

            // validate registrations belong to tournament + success
            var regs = await _db.TournamentRegistrations.AsNoTracking()
                .Where(x => x.TournamentId == rm.TournamentId
                         && (x.RegistrationId == dto.Team1RegistrationId || x.RegistrationId == dto.Team2RegistrationId))
                .Select(x => new { x.RegistrationId, x.Success })
                .ToListAsync();

            if (regs.Count != 2) return BadRequest(new { message = "Registrations not found in this tournament." });
            if (regs.Any(x => !x.Success)) return BadRequest(new { message = "Only SUCCESS registrations can be used for matches." });

            var m = new TournamentGroupMatch
            {
                TournamentRoundGroupId = groupId,
                TournamentId = rm.TournamentId,

                Team1RegistrationId = dto.Team1RegistrationId,
                Team2RegistrationId = dto.Team2RegistrationId,

                StartAt = dto.StartAt,
                AddressText = string.IsNullOrWhiteSpace(dto.AddressText) ? null : dto.AddressText.Trim(),
                CourtText = string.IsNullOrWhiteSpace(dto.CourtText) ? null : dto.CourtText.Trim(),

                ScoreTeam1 = 0,
                ScoreTeam2 = 0,
                IsCompleted = false,
                WinnerRegistrationId = null,

                CreatedAt = DateTime.UtcNow
            };

            _db.TournamentGroupMatches.Add(m);

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                await tx.RollbackAsync();
                // thường là trùng cặp đội trong group (UX_TGM_Group_TeamPair)
                return BadRequest(new { message = "Create match failed (maybe duplicate pair in this group).", detail = ex.Message });
            }

            await tx.CommitAsync();
            return Ok(new { m.MatchId });
        }

        // PUT /api/admin/groups/{groupId}/matches/{matchId}
        [HttpPut("{matchId:long}")]
        public async Task<IActionResult> Update(long groupId, long matchId, [FromBody] UpdateMatchDto dto)
        {
            var m = await _db.TournamentGroupMatches
                .FirstOrDefaultAsync(x => x.MatchId == matchId && x.TournamentRoundGroupId == groupId);

            if (m == null) return NotFound(new { message = "Match not found." });

            // Cho sửa meta (time/address/court). Sửa team thì chỉ khi chưa completed
            if (m.IsCompleted)
            {
                // bạn có thể cho phép sửa StartAt/Address/Court dù completed
                if (dto.StartAtSet) m.StartAt = dto.StartAt;
                if (dto.AddressText != null) m.AddressText = string.IsNullOrWhiteSpace(dto.AddressText) ? null : dto.AddressText.Trim();
                if (dto.CourtText != null) m.CourtText = string.IsNullOrWhiteSpace(dto.CourtText) ? null : dto.CourtText.Trim();

                m.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                return Ok(new { ok = true });
            }

            if (dto.Team1RegistrationId.HasValue || dto.Team2RegistrationId.HasValue)
            {
                var newT1 = dto.Team1RegistrationId ?? m.Team1RegistrationId;
                var newT2 = dto.Team2RegistrationId ?? m.Team2RegistrationId;

                if (newT1 == newT2)
                    return BadRequest(new { message = "Team1 and Team2 cannot be the same." });

                m.Team1RegistrationId = newT1;
                m.Team2RegistrationId = newT2;
            }

            if (dto.StartAtSet) m.StartAt = dto.StartAt;
            if (dto.AddressText != null) m.AddressText = string.IsNullOrWhiteSpace(dto.AddressText) ? null : dto.AddressText.Trim();
            if (dto.CourtText != null) m.CourtText = string.IsNullOrWhiteSpace(dto.CourtText) ? null : dto.CourtText.Trim();

            m.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                return BadRequest(new { message = "Update match failed (maybe duplicate pair).", detail = ex.Message });
            }

            return Ok(new { ok = true });
        }

        // DELETE /api/admin/groups/{groupId}/matches/{matchId}
        [HttpDelete("{matchId:long}")]
        public async Task<IActionResult> Delete(long groupId, long matchId)
        {
            var m = await _db.TournamentGroupMatches
                .FirstOrDefaultAsync(x => x.MatchId == matchId && x.TournamentRoundGroupId == groupId);

            if (m == null) return NotFound(new { message = "Match not found." });

            _db.TournamentGroupMatches.Remove(m);
            await _db.SaveChangesAsync();

            return Ok(new { ok = true });
        }

        // PUT /api/admin/groups/{groupId}/matches/{matchId}/score
        // -> set score + complete + winner
        [HttpPut("{matchId:long}/score")]
        public async Task<IActionResult> SetScore(long groupId, long matchId, [FromBody] SetScoreDto dto)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var m = await _db.TournamentGroupMatches
                .FirstOrDefaultAsync(x => x.MatchId == matchId && x.TournamentRoundGroupId == groupId);

            if (m == null) return NotFound(new { message = "Match not found." });

            if (dto.ScoreTeam1 < 0 || dto.ScoreTeam2 < 0)
                return BadRequest(new { message = "Score must be >= 0." });

            if (dto.ScoreTeam1 == dto.ScoreTeam2)
                return BadRequest(new { message = "No draw supported. ScoreTeam1 must differ ScoreTeam2." });

            m.ScoreTeam1 = dto.ScoreTeam1;
            m.ScoreTeam2 = dto.ScoreTeam2;

            // complete?
            m.IsCompleted = dto.IsCompleted;

            // compute winner
            var winner = dto.ScoreTeam1 > dto.ScoreTeam2 ? m.Team1RegistrationId : m.Team2RegistrationId;
            m.WinnerRegistrationId = winner;

            m.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok(new
            {
                m.MatchId,
                m.ScoreTeam1,
                m.ScoreTeam2,
                m.IsCompleted,
                m.WinnerRegistrationId,
                WinnerTeam = (m.WinnerRegistrationId == m.Team1RegistrationId ? "1" : "2"),
                m.UpdatedAt
            });
        }

        private static string BuildTeamText(string gameType, TournamentRegistration r)
        {
            gameType = (gameType ?? "DOUBLE").Trim().ToUpperInvariant();

            if (gameType == "SINGLE")
                return (r.Player1Name ?? "").Trim();

            var p1 = (r.Player1Name ?? "").Trim();
            var p2 = (r.Player2Name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(p2)) return p1;
            return $"{p1} & {p2}";
        }
    }

    public class CreateMatchDto
    {
        public long Team1RegistrationId { get; set; }
        public long Team2RegistrationId { get; set; }
        public DateTime? StartAt { get; set; }
        public string? AddressText { get; set; }
        public string? CourtText { get; set; }
    }

    public class UpdateMatchDto
    {
        public long? Team1RegistrationId { get; set; }
        public long? Team2RegistrationId { get; set; }

        // để set null StartAt cũng được: StartAtSet=true và StartAt=null
        public bool StartAtSet { get; set; } = false;
        public DateTime? StartAt { get; set; }

        public string? AddressText { get; set; }
        public string? CourtText { get; set; }
    }

    public class SetScoreDto
    {
        public int ScoreTeam1 { get; set; }
        public int ScoreTeam2 { get; set; }

        // user muốn “nhấn kết thúc trận” -> true
        public bool IsCompleted { get; set; } = true;
    }
}