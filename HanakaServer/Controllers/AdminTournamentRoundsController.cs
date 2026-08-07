using HanakaServer.Data;
using HanakaServer.Helpers;
using HanakaServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;

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
            if (!exists) return NotFound(new { message = "Không tìm thấy giải đấu." });

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
                    x.BracketApplicationId,
                    x.TemplateRoundKey,
                    x.CreatedAt,
                    GroupCount = x.TournamentRoundGroups.Count()
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

            if (string.IsNullOrWhiteSpace(key)) return BadRequest(new { message = "Vui lòng nhập mã vòng đấu." });
            if (string.IsNullOrWhiteSpace(label)) label = key;

            var tExists = await _db.Tournaments.AnyAsync(x => x.TournamentId == tournamentId);
            if (!tExists) return NotFound(new { message = "Không tìm thấy giải đấu." });

            var hasActiveApplication = await _db.TournamentBracketApplications.AsNoTracking()
                .AnyAsync(x => x.TournamentId == tournamentId
                               && (x.Status == BracketApplicationStatuses.Applied
                                   || x.Status == BracketApplicationStatuses.Applying));
            if (hasActiveApplication)
            {
                return BadRequest(new
                {
                    message = "Giải đang sử dụng bracket sinh tự động. Hãy reset bracket trước khi tạo vòng đấu thủ công."
                });
            }

            var exists = await _db.TournamentRoundMaps
                .AnyAsync(x => x.TournamentId == tournamentId && x.RoundKey == key);
            if (exists) return BadRequest(new { message = "Mã vòng đấu đã tồn tại trong giải này." });

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
                row.CreatedAt,
                GroupCount = 0
            });
        }

        // PUT: /api/admin/tournaments/{tournamentId}/round-maps/{id}
        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long tournamentId, long id, [FromBody] UpdateRoundMapDto dto)
        {
            var row = await _db.TournamentRoundMaps
                .FirstOrDefaultAsync(x => x.TournamentRoundMapId == id && x.TournamentId == tournamentId);

            if (row == null) return NotFound(new { message = "Không tìm thấy vòng đấu." });

            if (row.BracketApplicationId.HasValue)
            {
                return BadRequest(new
                {
                    message = "Không thể sửa cấu trúc vòng đấu được sinh từ template. Hãy reset và áp dụng lại bracket."
                });
            }

            if (dto.RoundKey != null)
            {
                var key = dto.RoundKey.Trim();
                if (string.IsNullOrWhiteSpace(key))
                    return BadRequest(new { message = "Vui lòng nhập mã vòng đấu." });

                var exists = await _db.TournamentRoundMaps
                    .AnyAsync(x => x.TournamentId == tournamentId
                                   && x.RoundKey == key
                                   && x.TournamentRoundMapId != id);
                if (exists)
                    return BadRequest(new { message = "Mã vòng đấu đã tồn tại trong giải này." });

                row.RoundKey = key;
            }

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
                row.CreatedAt,
                GroupCount = await _db.TournamentRoundGroups
                    .AsNoTracking()
                    .CountAsync(x => x.TournamentRoundMapId == id)
            });
        }

        [HttpGet("{id:long}/delete-summary")]
        public async Task<IActionResult> DeleteSummary(long tournamentId, long id)
        {
            var row = await _db.TournamentRoundMaps.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TournamentRoundMapId == id && x.TournamentId == tournamentId);

            if (row == null) return NotFound(new { message = "Không tìm thấy vòng đấu." });

            var groupIds = await _db.TournamentRoundGroups.AsNoTracking()
                .Where(x => x.TournamentRoundMapId == id)
                .Select(x => x.TournamentRoundGroupId)
                .ToListAsync();
            var matches = await _db.TournamentGroupMatches.AsNoTracking()
                .Where(x => groupIds.Contains(x.TournamentRoundGroupId))
                .Select(x => new
                {
                    x.MatchId,
                    x.IsCompleted,
                    x.StartAt
                })
                .ToListAsync();
            var matchIds = matches.Select(x => x.MatchId).ToList();
            var dependentMatches = await _db.TournamentGroupMatches.AsNoTracking()
                .Where(x => !matchIds.Contains(x.MatchId)
                            && ((x.Team1SourceMatchId.HasValue && matchIds.Contains(x.Team1SourceMatchId.Value))
                                || (x.Team2SourceMatchId.HasValue && matchIds.Contains(x.Team2SourceMatchId.Value))
                                || (x.Team1SourceGroupId.HasValue && groupIds.Contains(x.Team1SourceGroupId.Value))
                                || (x.Team2SourceGroupId.HasValue && groupIds.Contains(x.Team2SourceGroupId.Value))))
                .Select(x => new
                {
                    x.MatchId,
                    x.Team1SourceMatchId,
                    x.Team2SourceMatchId,
                    x.Team1SourceGroupId,
                    x.Team2SourceGroupId
                })
                .ToListAsync();

            var dependentSlotCount = dependentMatches.Sum(x =>
                ((x.Team1SourceMatchId.HasValue && matchIds.Contains(x.Team1SourceMatchId.Value))
                  || (x.Team1SourceGroupId.HasValue && groupIds.Contains(x.Team1SourceGroupId.Value)) ? 1 : 0)
                + ((x.Team2SourceMatchId.HasValue && matchIds.Contains(x.Team2SourceMatchId.Value))
                   || (x.Team2SourceGroupId.HasValue && groupIds.Contains(x.Team2SourceGroupId.Value)) ? 1 : 0));
            var scoreHistoryCount = matchIds.Count == 0
                ? 0
                : await _db.TournamentMatchScoreHistories.AsNoTracking()
                    .CountAsync(x => matchIds.Contains(x.MatchId));
            var notificationCount = matchIds.Count == 0
                ? 0
                : await _db.UserNotifications.AsNoTracking()
                    .CountAsync(x => x.RefType == "MATCH" && x.RefId.HasValue && matchIds.Contains(x.RefId.Value));

            return Ok(new
            {
                row.TournamentRoundMapId,
                row.RoundKey,
                row.RoundLabel,
                ProtectedByTemplate = row.BracketApplicationId.HasValue,
                GroupCount = groupIds.Count,
                MatchCount = matches.Count,
                CompletedMatchCount = matches.Count(x => x.IsCompleted),
                ScheduledMatchCount = matches.Count(x => x.StartAt.HasValue),
                ScoreHistoryCount = scoreHistoryCount,
                NotificationCount = notificationCount,
                DependentMatchCount = dependentMatches.Count,
                DependentSlotCount = dependentSlotCount
            });
        }

        // DELETE: /api/admin/tournaments/{tournamentId}/round-maps/{id}
        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long tournamentId, long id, [FromBody] DeleteRoundMapDto? dto)
        {
            if (!string.Equals(dto?.Confirmation, "XOA", StringComparison.Ordinal))
            {
                return BadRequest(new
                {
                    message = "Vui lòng nhập đúng XOA để xác nhận xóa vòng đấu."
                });
            }

            await using var transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable)
                : null;
            var row = await _db.TournamentRoundMaps
                .FirstOrDefaultAsync(x => x.TournamentRoundMapId == id && x.TournamentId == tournamentId);

            if (row == null) return NotFound(new { message = "Không tìm thấy vòng đấu." });

            if (row.BracketApplicationId.HasValue)
            {
                return BadRequest(new
                {
                    message = "Không thể xóa vòng đấu được sinh từ template. Hãy dùng chức năng reset bracket."
                });
            }

            var groups = await _db.TournamentRoundGroups
                .Where(x => x.TournamentRoundMapId == id)
                .ToListAsync();
            var groupIds = groups.Select(x => x.TournamentRoundGroupId).ToList();
            var matches = await _db.TournamentGroupMatches
                .Where(x => groupIds.Contains(x.TournamentRoundGroupId))
                .ToListAsync();
            var matchIds = matches.Select(x => x.MatchId).ToList();
            var matchIdSet = matchIds.ToHashSet();
            var groupIdSet = groupIds.ToHashSet();

            var dependentMatches = await _db.TournamentGroupMatches
                .Where(x => !matchIds.Contains(x.MatchId)
                            && ((x.Team1SourceMatchId.HasValue && matchIds.Contains(x.Team1SourceMatchId.Value))
                                || (x.Team2SourceMatchId.HasValue && matchIds.Contains(x.Team2SourceMatchId.Value))
                                || (x.Team1SourceGroupId.HasValue && groupIds.Contains(x.Team1SourceGroupId.Value))
                                || (x.Team2SourceGroupId.HasValue && groupIds.Contains(x.Team2SourceGroupId.Value))))
                .ToListAsync();
            var detachedSlotCount = 0;
            foreach (var target in dependentMatches)
            {
                if ((target.Team1SourceMatchId.HasValue && matchIdSet.Contains(target.Team1SourceMatchId.Value))
                    || (target.Team1SourceGroupId.HasValue && groupIdSet.Contains(target.Team1SourceGroupId.Value)))
                {
                    target.Team1SourceType = MatchSourceTypes.Registration;
                    target.Team1SourceMatchId = null;
                    target.Team1SourceGroupId = null;
                    target.Team1SourceRank = null;
                    detachedSlotCount++;
                }

                if ((target.Team2SourceMatchId.HasValue && matchIdSet.Contains(target.Team2SourceMatchId.Value))
                    || (target.Team2SourceGroupId.HasValue && groupIdSet.Contains(target.Team2SourceGroupId.Value)))
                {
                    target.Team2SourceType = MatchSourceTypes.Registration;
                    target.Team2SourceMatchId = null;
                    target.Team2SourceGroupId = null;
                    target.Team2SourceRank = null;
                    detachedSlotCount++;
                }
            }

            var scoreHistories = matchIds.Count == 0
                ? []
                : await _db.TournamentMatchScoreHistories
                    .Where(x => matchIds.Contains(x.MatchId))
                    .ToListAsync();
            var relatedNotifications = matchIds.Count == 0
                ? []
                : await _db.UserNotifications
                    .Where(x => x.RefType == "MATCH" && x.RefId.HasValue && matchIds.Contains(x.RefId.Value))
                    .ToListAsync();

            _db.UserNotifications.RemoveRange(relatedNotifications);
            _db.TournamentMatchScoreHistories.RemoveRange(scoreHistories);
            _db.TournamentGroupMatches.RemoveRange(matches);
            _db.TournamentRoundGroups.RemoveRange(groups);
            _db.TournamentRoundMaps.Remove(row);

            try
            {
                await _db.SaveChangesAsync();
                if (transaction != null)
                    await transaction.CommitAsync();
            }
            catch (DbUpdateException ex)
            {
                if (transaction != null)
                    await transaction.RollbackAsync();

                return BadRequest(new
                {
                    message = "Xóa vòng đấu thất bại vì vẫn còn dữ liệu liên kết chưa được xử lý.",
                    detail = ex.InnerException?.Message ?? ex.Message
                });
            }

            return Ok(new
            {
                ok = true,
                deletedRound = row.RoundLabel,
                deletedGroupCount = groups.Count,
                deletedMatchCount = matches.Count,
                deletedScoreHistoryCount = scoreHistories.Count,
                deletedNotificationCount = relatedNotifications.Count,
                detachedDependentMatchCount = dependentMatches.Count,
                detachedDependentSlotCount = detachedSlotCount
            });
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
        public string? RoundKey { get; set; }
        public string? RoundLabel { get; set; }
        public int? SortOrder { get; set; }
    }

    public class DeleteRoundMapDto
    {
        public string? Confirmation { get; set; }
    }
}
