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

        public AdminTournamentGroupMatchesController(PickleballDbContext db)
        {
            _db = db;
        }

        // GET /api/admin/groups/{groupId}/matches
        [HttpGet]
        public async Task<IActionResult> List(long groupId)
        {
            var g = await _db.TournamentRoundGroups.AsNoTracking()
                .Where(x => x.TournamentRoundGroupId == groupId)
                .Select(x => new
                {
                    x.TournamentRoundGroupId,
                    x.TournamentRoundMapId,
                    x.GroupName
                })
                .FirstOrDefaultAsync();

            if (g == null)
                return NotFound(new { message = "Group not found." });

            var rm = await _db.TournamentRoundMaps.AsNoTracking()
                .Where(x => x.TournamentRoundMapId == g.TournamentRoundMapId)
                .Select(x => new
                {
                    x.TournamentRoundMapId,
                    x.TournamentId,
                    x.RoundKey,
                    x.RoundLabel
                })
                .FirstOrDefaultAsync();

            if (rm == null)
                return NotFound(new { message = "RoundMap not found." });

            var t = await _db.Tournaments.AsNoTracking()
                .Where(x => x.TournamentId == rm.TournamentId)
                .Select(x => new
                {
                    x.TournamentId,
                    x.Title,
                    x.Status,
                    x.GameType
                })
                .FirstOrDefaultAsync();

            if (t == null)
                return NotFound(new { message = "Tournament not found." });

            var itemsRaw = await (
                from m in _db.TournamentGroupMatches.AsNoTracking()
                where m.TournamentRoundGroupId == groupId
                join r1 in _db.TournamentRegistrations.AsNoTracking()
                    on m.Team1RegistrationId equals r1.RegistrationId
                join r2 in _db.TournamentRegistrations.AsNoTracking()
                    on m.Team2RegistrationId equals r2.RegistrationId
                join u in _db.Users.AsNoTracking()
                    on m.RefereeUserId equals u.UserId into refereeJoin
                from referee in refereeJoin.DefaultIfEmpty()
                orderby (m.StartAt ?? DateTime.MaxValue), m.MatchId
                select new
                {
                    m.MatchId,
                    m.TournamentRoundGroupId,
                    m.TournamentId,

                    m.Team1RegistrationId,
                    Team1Text = BuildTeamText(t.GameType ?? "DOUBLE", r1),

                    m.Team2RegistrationId,
                    Team2Text = BuildTeamText(t.GameType ?? "DOUBLE", r2),

                    m.StartAt,
                    m.AddressText,
                    m.CourtText,
                    m.VideoUrl,

                    m.ScoreTeam1,
                    m.ScoreTeam2,
                    m.IsCompleted,
                    m.WinnerRegistrationId,

                    WinnerTeam = m.WinnerRegistrationId == null
                        ? null
                        : (m.WinnerRegistrationId == m.Team1RegistrationId ? "1" : "2"),

                    m.RefereeUserId,
                    RefereeName = referee != null ? referee.FullName : null,
                    RefereePhone = referee != null ? referee.Phone : null,
                    RefereeEmail = referee != null ? referee.Email : null,
                    RefereeCity = referee != null ? referee.City : null,
                    RefereeIsActive = referee != null && referee.IsActive,

                    m.CreatedAt,
                    m.UpdatedAt
                }
            ).ToListAsync();

            var refereeExternalIds = itemsRaw
                .Where(x => x.RefereeUserId.HasValue)
                .Select(x => x.RefereeUserId!.Value.ToString())
                .Distinct()
                .ToList();

            var refereeProfileMap = await _db.Referees.AsNoTracking()
                .Where(x => refereeExternalIds.Contains(x.ExternalId))
                .Select(x => new
                {
                    x.ExternalId,
                    x.RefereeId,
                    x.Verified,
                    x.RefereeType
                })
                .ToDictionaryAsync(x => x.ExternalId, x => x);

            var items = itemsRaw.Select(m =>
            {
                var refereeKey = m.RefereeUserId?.ToString() ?? "";
                refereeProfileMap.TryGetValue(refereeKey, out var refereeProfile);

                return new
                {
                    m.MatchId,
                    m.TournamentRoundGroupId,
                    m.TournamentId,
                    m.Team1RegistrationId,
                    m.Team1Text,
                    m.Team2RegistrationId,
                    m.Team2Text,
                    m.StartAt,
                    m.AddressText,
                    m.CourtText,
                    m.VideoUrl,
                    m.ScoreTeam1,
                    m.ScoreTeam2,
                    m.IsCompleted,
                    m.WinnerRegistrationId,
                    m.WinnerTeam,
                    m.RefereeUserId,
                    m.RefereeName,
                    m.RefereePhone,
                    m.RefereeEmail,
                    m.RefereeCity,
                    RefereeVerified = refereeProfile != null && refereeProfile.Verified,
                    RefereeType = refereeProfile?.RefereeType,
                    RefereeProfileId = refereeProfile?.RefereeId,
                    m.RefereeIsActive,
                    m.CreatedAt,
                    m.UpdatedAt
                };
            }).ToList();

            return Ok(new
            {
                tournament = t,
                roundMap = rm,
                group = g,
                items
            });
        }

        // POST /api/admin/groups/{groupId}/matches
        [HttpPost]
        public async Task<IActionResult> Create(long groupId, [FromBody] CreateMatchDto dto)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var g = await _db.TournamentRoundGroups.FirstOrDefaultAsync(x => x.TournamentRoundGroupId == groupId);
            if (g == null)
                return NotFound(new { message = "Group not found." });

            var rm = await _db.TournamentRoundMaps.FirstOrDefaultAsync(x => x.TournamentRoundMapId == g.TournamentRoundMapId);
            if (rm == null)
                return NotFound(new { message = "RoundMap not found." });

            if (dto.Team1RegistrationId <= 0 || dto.Team2RegistrationId <= 0)
                return BadRequest(new { message = "Team1RegistrationId and Team2RegistrationId are required." });

            if (dto.Team1RegistrationId == dto.Team2RegistrationId)
                return BadRequest(new { message = "Team1 and Team2 cannot be the same." });

            var regs = await _db.TournamentRegistrations.AsNoTracking()
                .Where(x => x.TournamentId == rm.TournamentId
                    && (x.RegistrationId == dto.Team1RegistrationId || x.RegistrationId == dto.Team2RegistrationId))
                .Select(x => new { x.RegistrationId, x.Success })
                .ToListAsync();

            if (regs.Count != 2)
                return BadRequest(new { message = "Registrations not found in this tournament." });

            if (regs.Any(x => !x.Success))
                return BadRequest(new { message = "Only SUCCESS registrations can be used for matches." });

            var refereeValidationError = await ValidateAndEnsureRefereeAsync(dto.RefereeUserId);
            if (refereeValidationError != null)
                return BadRequest(new { message = refereeValidationError });

            var m = new TournamentGroupMatch
            {
                TournamentRoundGroupId = groupId,
                TournamentId = rm.TournamentId,

                Team1RegistrationId = dto.Team1RegistrationId,
                Team2RegistrationId = dto.Team2RegistrationId,

                StartAt = dto.StartAt,
                AddressText = string.IsNullOrWhiteSpace(dto.AddressText) ? null : dto.AddressText.Trim(),
                CourtText = string.IsNullOrWhiteSpace(dto.CourtText) ? null : dto.CourtText.Trim(),
                VideoUrl = string.IsNullOrWhiteSpace(dto.VideoUrl) ? null : dto.VideoUrl.Trim(),

                RefereeUserId = dto.RefereeUserId,

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
                await tx.CommitAsync();
            }
            catch (DbUpdateException ex)
            {
                await tx.RollbackAsync();
                return BadRequest(new
                {
                    message = "Create match failed (maybe duplicate pair in this group).",
                    detail = ex.Message
                });
            }

            return Ok(new { m.MatchId });
        }

        [HttpPut("{matchId:long}")]
        public async Task<IActionResult> Update(long groupId, long matchId, [FromBody] UpdateMatchDto dto)
        {
            var m = await _db.TournamentGroupMatches
                .FirstOrDefaultAsync(x => x.MatchId == matchId && x.TournamentRoundGroupId == groupId);

            if (m == null)
                return NotFound(new { message = "Match not found." });

            var refereeValidationError = await ValidateAndEnsureRefereeAsync(dto.RefereeUserId);
            if (refereeValidationError != null)
                return BadRequest(new { message = refereeValidationError });

            if (m.IsCompleted)
            {
                if (dto.StartAtSet) m.StartAt = dto.StartAt;
                if (dto.AddressText != null) m.AddressText = string.IsNullOrWhiteSpace(dto.AddressText) ? null : dto.AddressText.Trim();
                if (dto.CourtText != null) m.CourtText = string.IsNullOrWhiteSpace(dto.CourtText) ? null : dto.CourtText.Trim();
                if (dto.VideoUrl != null) m.VideoUrl = string.IsNullOrWhiteSpace(dto.VideoUrl) ? null : dto.VideoUrl.Trim();

                // vẫn cho sửa trọng tài
                m.RefereeUserId = dto.RefereeUserId;

                m.UpdatedAt = DateTime.UtcNow;

                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateException ex)
                {
                    return BadRequest(new
                    {
                        message = "Update completed match failed.",
                        detail = ex.Message
                    });
                }

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
            if (dto.VideoUrl != null) m.VideoUrl = string.IsNullOrWhiteSpace(dto.VideoUrl) ? null : dto.VideoUrl.Trim();

            m.RefereeUserId = dto.RefereeUserId;
            m.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                return BadRequest(new
                {
                    message = "Update match failed (maybe duplicate pair).",
                    detail = ex.Message
                });
            }

            return Ok(new { ok = true });
        }
        // DELETE /api/admin/groups/{groupId}/matches/{matchId}
        [HttpDelete("{matchId:long}")]
        public async Task<IActionResult> Delete(long groupId, long matchId)
        {
            var m = await _db.TournamentGroupMatches
                .FirstOrDefaultAsync(x => x.MatchId == matchId && x.TournamentRoundGroupId == groupId);

            if (m == null)
                return NotFound(new { message = "Match not found." });

            _db.TournamentGroupMatches.Remove(m);
            await _db.SaveChangesAsync();

            return Ok(new { ok = true });
        }

        // PUT /api/admin/groups/{groupId}/matches/{matchId}/score
        [HttpPut("{matchId:long}/score")]
        public async Task<IActionResult> SetScore(long groupId, long matchId, [FromBody] SetScoreDto dto)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var m = await _db.TournamentGroupMatches
                .FirstOrDefaultAsync(x => x.MatchId == matchId && x.TournamentRoundGroupId == groupId);

            if (m == null)
                return NotFound(new { message = "Match not found." });

            if (dto.ScoreTeam1 < 0 || dto.ScoreTeam2 < 0)
                return BadRequest(new { message = "Score must be >= 0." });

            if (dto.ScoreTeam1 == dto.ScoreTeam2)
                return BadRequest(new { message = "No draw supported. ScoreTeam1 must differ ScoreTeam2." });

            m.ScoreTeam1 = dto.ScoreTeam1;
            m.ScoreTeam2 = dto.ScoreTeam2;
            m.IsCompleted = dto.IsCompleted;
            m.WinnerRegistrationId = dto.ScoreTeam1 > dto.ScoreTeam2
                ? m.Team1RegistrationId
                : m.Team2RegistrationId;
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

            if (string.IsNullOrWhiteSpace(p2))
                return p1;

            return $"{p1} & {p2}";
        }

        private async Task<string?> ValidateAndEnsureRefereeAsync(long? refereeUserId)
        {
            if (!refereeUserId.HasValue || refereeUserId.Value <= 0)
                return "Trận đấu bắt buộc phải có trọng tài.";

            var resolvedRefereeUserId = refereeUserId.Value;

            var refereeUser = await _db.Users
                .FirstOrDefaultAsync(x => x.UserId == resolvedRefereeUserId);

            if (refereeUser == null)
                return "Không tìm thấy user trọng tài.";

            if (!refereeUser.IsActive)
                return "User trọng tài đang bị vô hiệu hóa.";

            var refereeProfile = await _db.Referees
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ExternalId == resolvedRefereeUserId.ToString());

            if (refereeProfile == null)
                return "User này chưa có hồ sơ trọng tài.";

            if (!refereeProfile.Verified)
                return "Hồ sơ trọng tài này chưa được xác minh.";

            var refereeRoleId = await _db.Roles.AsNoTracking()
                .Where(x => x.RoleCode == "REFEREE")
                .Select(x => x.RoleId)
                .FirstOrDefaultAsync();

            if (refereeRoleId == 0)
                return "Role REFEREE not found in system.";

            var hasRefereeRole = await _db.UserRoles
                .AnyAsync(x => x.UserId == resolvedRefereeUserId && x.RoleId == refereeRoleId);

            if (!hasRefereeRole)
            {
                _db.UserRoles.Add(new UserRole
                {
                    UserId = resolvedRefereeUserId,
                    RoleId = refereeRoleId,
                    CreatedAt = DateTime.UtcNow
                });
            }

            return null;
        }
    }

    public class CreateMatchDto
    {
        public long Team1RegistrationId { get; set; }
        public long Team2RegistrationId { get; set; }
        public DateTime? StartAt { get; set; }
        public string? AddressText { get; set; }
        public string? CourtText { get; set; }
        public string? VideoUrl { get; set; }

        // NEW
        public long? RefereeUserId { get; set; }
    }

    public class UpdateMatchDto
    {
        public long? Team1RegistrationId { get; set; }
        public long? Team2RegistrationId { get; set; }

        public bool StartAtSet { get; set; } = false;
        public DateTime? StartAt { get; set; }

        public string? AddressText { get; set; }
        public string? CourtText { get; set; }
        public string? VideoUrl { get; set; }

        // NEW
        public long? RefereeUserId { get; set; }
    }

    public class SetScoreDto
    {
        public int ScoreTeam1 { get; set; }
        public int ScoreTeam2 { get; set; }
        public bool IsCompleted { get; set; } = true;
    }
}
