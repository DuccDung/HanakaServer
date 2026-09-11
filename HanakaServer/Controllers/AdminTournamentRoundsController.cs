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

        // GET: /api/admin/tournaments/{tournamentId}/match-schedules
        [HttpGet("~/api/admin/tournaments/{tournamentId:long}/match-schedules")]
        public async Task<IActionResult> ListMatchSchedules(long tournamentId)
        {
            var tournament = await _db.Tournaments.AsNoTracking()
                .Where(x => x.TournamentId == tournamentId)
                .Select(x => new
                {
                    x.TournamentId,
                    x.Title,
                    x.GameType
                })
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Không tìm thấy giải đấu." });

            var rows = await (
                from match in _db.TournamentGroupMatches.AsNoTracking()
                join roundGroup in _db.TournamentRoundGroups.AsNoTracking()
                    on match.TournamentRoundGroupId equals roundGroup.TournamentRoundGroupId
                join round in _db.TournamentRoundMaps.AsNoTracking()
                    on roundGroup.TournamentRoundMapId equals round.TournamentRoundMapId
                join refereeUser in _db.Users.AsNoTracking()
                    on match.RefereeUserId equals refereeUser.UserId into refereeJoin
                from referee in refereeJoin.DefaultIfEmpty()
                where match.TournamentId == tournamentId && round.TournamentId == tournamentId
                orderby round.SortOrder,
                    round.RoundKey,
                    roundGroup.SortOrder,
                    roundGroup.GroupName,
                    match.StartAt ?? DateTime.MaxValue,
                    match.MatchId
                select new
                {
                    match.MatchId,
                    match.TournamentRoundGroupId,
                    IsGeneratedMatch = match.BracketApplicationId.HasValue,
                    round.TournamentRoundMapId,
                    round.RoundKey,
                    round.RoundLabel,
                    RoundSortOrder = round.SortOrder,
                    roundGroup.GroupName,
                    GroupSortOrder = roundGroup.SortOrder,
                    match.Team1RegistrationId,
                    match.Team1SourceType,
                    match.Team1SourceMatchId,
                    match.Team1SourceGroupId,
                    match.Team1SourceRank,
                    match.Team2RegistrationId,
                    match.Team2SourceType,
                    match.Team2SourceMatchId,
                    match.Team2SourceGroupId,
                    match.Team2SourceRank,
                    match.StartAt,
                    match.CourtText,
                    match.AddressText,
                    match.RefereeUserId,
                    RefereeName = referee != null ? referee.FullName : null,
                    RefereePhone = referee != null ? referee.Phone : null,
                    match.ScoreTeam1,
                    match.ScoreTeam2,
                    match.IsCompleted
                })
                .ToListAsync();

            var registrationIds = rows
                .SelectMany(x => new[] { x.Team1RegistrationId, x.Team2RegistrationId })
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();

            var registrationMap = await _db.TournamentRegistrations.AsNoTracking()
                .Where(x => registrationIds.Contains(x.RegistrationId))
                .Select(x => new ScheduleRegistrationData
                {
                    RegistrationId = x.RegistrationId,
                    Player1Name = x.Player1Name,
                    Player2Name = x.Player2Name
                })
                .ToDictionaryAsync(x => x.RegistrationId);
            var relayTeamNames = await _db.RelayTeams.AsNoTracking()
                .Where(x => registrationIds.Contains(x.RegistrationId))
                .ToDictionaryAsync(x => x.RegistrationId, x => x.TeamName);
            foreach (var registration in registrationMap.Values)
            {
                if (relayTeamNames.TryGetValue(registration.RegistrationId, out var relayTeamName))
                    registration.RelayTeamName = relayTeamName;
            }
            var scheduleMatchIds = rows.Select(x => x.MatchId).ToArray();
            var relaySnapshotNames = (await _db.RelayMatchLineupSnapshots.AsNoTracking()
                .Where(x => scheduleMatchIds.Contains(x.MatchId))
                .Select(x => new { x.MatchId, x.Side, x.TeamName })
                .ToListAsync()).ToDictionary(x => (x.MatchId, x.Side), x => x.TeamName);

            var sourceGroupIds = rows
                .SelectMany(x => new[] { x.Team1SourceGroupId, x.Team2SourceGroupId })
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();

            var sourceGroupMap = await _db.TournamentRoundGroups.AsNoTracking()
                .Where(x => sourceGroupIds.Contains(x.TournamentRoundGroupId))
                .Select(x => new { x.TournamentRoundGroupId, x.GroupName })
                .ToDictionaryAsync(x => x.TournamentRoundGroupId, x => x.GroupName);

            var items = rows.Select(x => new
            {
                x.MatchId,
                x.TournamentRoundGroupId,
                x.IsGeneratedMatch,
                x.TournamentRoundMapId,
                x.RoundKey,
                x.RoundLabel,
                x.RoundSortOrder,
                x.GroupName,
                x.GroupSortOrder,
                Team1Text = relaySnapshotNames.GetValueOrDefault((x.MatchId, 1)) ?? BuildScheduleTeamText(
                    tournament.GameType,
                    x.Team1RegistrationId,
                    x.Team1SourceType,
                    x.Team1SourceMatchId,
                    x.Team1SourceGroupId,
                    x.Team1SourceRank,
                    registrationMap,
                    sourceGroupMap),
                Team2Text = relaySnapshotNames.GetValueOrDefault((x.MatchId, 2)) ?? BuildScheduleTeamText(
                    tournament.GameType,
                    x.Team2RegistrationId,
                    x.Team2SourceType,
                    x.Team2SourceMatchId,
                    x.Team2SourceGroupId,
                    x.Team2SourceRank,
                    registrationMap,
                    sourceGroupMap),
                x.StartAt,
                x.CourtText,
                x.AddressText,
                x.RefereeUserId,
                x.RefereeName,
                x.RefereePhone,
                x.ScoreTeam1,
                x.ScoreTeam2,
                x.IsCompleted
            }).ToList();

            return Ok(new
            {
                tournament,
                total = items.Count,
                scheduledCount = items.Count(x => x.StartAt.HasValue),
                assignedRefereeCount = items.Count(x => x.RefereeUserId.HasValue),
                items
            });
        }

        // PUT: /api/admin/tournaments/{tournamentId}/match-schedules/bulk
        [HttpPut("~/api/admin/tournaments/{tournamentId:long}/match-schedules/bulk")]
        public async Task<IActionResult> BulkUpdateMatchSchedules(
            long tournamentId,
            [FromBody] BulkUpdateMatchSchedulesDto dto)
        {
            var matchIds = (dto.MatchIds ?? new List<long>())
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (matchIds.Count == 0)
                return BadRequest(new { message = "Vui lòng chọn ít nhất một trận đấu." });

            if (matchIds.Count > 500)
                return BadRequest(new { message = "Mỗi lần chỉ được cập nhật tối đa 500 trận đấu." });

            if (!dto.StartAtSet && !dto.CourtTextSet && !dto.AddressTextSet && !dto.RefereeUserIdSet)
                return BadRequest(new { message = "Vui lòng chọn ít nhất một thông tin cần cập nhật." });

            if (dto.StartAtSet && !dto.StartAt.HasValue)
                return BadRequest(new { message = "Vui lòng nhập đầy đủ ngày và giờ thi đấu." });

            if (dto.RefereeUserIdSet && (!dto.RefereeUserId.HasValue || dto.RefereeUserId.Value <= 0))
                return BadRequest(new { message = "Vui lòng chọn trọng tài áp dụng cho các trận." });

            var tournamentExists = await _db.Tournaments.AsNoTracking()
                .AnyAsync(x => x.TournamentId == tournamentId);
            if (!tournamentExists)
                return NotFound(new { message = "Không tìm thấy giải đấu." });

            var matches = await _db.TournamentGroupMatches
                .Where(x => x.TournamentId == tournamentId && matchIds.Contains(x.MatchId))
                .ToListAsync();

            if (matches.Count != matchIds.Count)
            {
                var foundIds = matches.Select(x => x.MatchId).ToHashSet();
                var missingIds = matchIds.Where(x => !foundIds.Contains(x)).ToList();
                return BadRequest(new
                {
                    message = "Một số trận không tồn tại hoặc không thuộc giải đấu này.",
                    missingMatchIds = missingIds
                });
            }

            if (dto.RefereeUserIdSet)
            {
                var refereeValidationError = await ValidateBulkRefereeAsync(dto.RefereeUserId!.Value);
                if (refereeValidationError != null)
                    return BadRequest(new { message = refereeValidationError });
            }

            var updatedAt = DateTime.UtcNow;
            var courtText = string.IsNullOrWhiteSpace(dto.CourtText) ? null : dto.CourtText.Trim();
            var addressText = string.IsNullOrWhiteSpace(dto.AddressText) ? null : dto.AddressText.Trim();

            foreach (var match in matches)
            {
                if (dto.StartAtSet)
                    match.StartAt = dto.StartAt;
                if (dto.CourtTextSet)
                    match.CourtText = courtText;
                if (dto.AddressTextSet)
                    match.AddressText = addressText;
                if (dto.RefereeUserIdSet)
                    match.RefereeUserId = dto.RefereeUserId;

                match.UpdatedAt = updatedAt;
            }

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                return BadRequest(new
                {
                    message = "Cập nhật hàng loạt lịch thi đấu thất bại.",
                    detail = ex.InnerException?.Message ?? ex.Message
                });
            }

            return Ok(new
            {
                ok = true,
                updatedCount = matches.Count,
                updatedMatchIds = matchIds
            });
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

        private static string BuildScheduleTeamText(
            string? gameType,
            long? registrationId,
            string? sourceType,
            long? sourceMatchId,
            long? sourceGroupId,
            int? sourceRank,
            IReadOnlyDictionary<long, ScheduleRegistrationData> registrationMap,
            IReadOnlyDictionary<long, string> sourceGroupMap)
        {
            if (registrationId.HasValue
                && registrationMap.TryGetValue(registrationId.Value, out var registration))
            {
                return BuildScheduleRegistrationText(
                    gameType,
                    registration.Player1Name,
                    registration.Player2Name,
                    registration.RelayTeamName);
            }

            var normalizedSourceType = MatchSourceTypes.Normalize(sourceType);
            return normalizedSourceType switch
            {
                MatchSourceTypes.WinnerMatch => sourceMatchId.HasValue
                    ? $"Chờ thắng trận #{sourceMatchId.Value}"
                    : "Chờ thắng trận",
                MatchSourceTypes.LoserMatch => sourceMatchId.HasValue
                    ? $"Chờ thua trận #{sourceMatchId.Value}"
                    : "Chờ thua trận",
                MatchSourceTypes.GroupRank => BuildScheduleGroupRankText(sourceGroupId, sourceRank, sourceGroupMap),
                MatchSourceTypes.Bye => "Miễn đấu",
                _ => "Chưa xác định"
            };
        }

        private static string BuildScheduleRegistrationText(
            string? gameType,
            string? player1Name,
            string? player2Name,
            string? relayTeamName = null)
        {
            if (!string.IsNullOrWhiteSpace(relayTeamName))
                return relayTeamName.Trim();

            if (string.Equals(gameType?.Trim(), "SINGLE", StringComparison.OrdinalIgnoreCase))
                return (player1Name ?? "").Trim();

            var player1 = (player1Name ?? "").Trim();
            var player2 = (player2Name ?? "").Trim();
            return string.IsNullOrWhiteSpace(player2) ? player1 : $"{player1} & {player2}";
        }

        private static string BuildScheduleGroupRankText(
            long? sourceGroupId,
            int? sourceRank,
            IReadOnlyDictionary<long, string> sourceGroupMap)
        {
            var groupName = sourceGroupId.HasValue
                && sourceGroupMap.TryGetValue(sourceGroupId.Value, out var foundGroupName)
                    ? foundGroupName
                    : (sourceGroupId.HasValue ? $"#{sourceGroupId.Value}" : "");

            return sourceRank.HasValue
                ? $"Chờ hạng {sourceRank.Value} bảng {groupName}".Trim()
                : $"Chờ hạng bảng {groupName}".Trim();
        }

        private async Task<string?> ValidateBulkRefereeAsync(long refereeUserId)
        {
            var refereeUser = await _db.Users
                .FirstOrDefaultAsync(x => x.UserId == refereeUserId);
            if (refereeUser == null)
                return "Không tìm thấy user trọng tài.";
            if (!refereeUser.IsActive)
                return "Người dùng trọng tài đang bị vô hiệu hóa.";

            var refereeProfile = await _db.Referees.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ExternalId == refereeUserId.ToString());
            if (refereeProfile == null)
                return "Người dùng này chưa có hồ sơ trọng tài.";
            if (!refereeProfile.Verified)
                return "Hồ sơ trọng tài này chưa được xác minh.";

            var refereeRoleId = await _db.Roles.AsNoTracking()
                .Where(x => x.RoleCode == "REFEREE")
                .Select(x => x.RoleId)
                .FirstOrDefaultAsync();
            if (refereeRoleId == 0)
                return "Không tìm thấy vai trò trọng tài trong hệ thống.";

            var hasRefereeRole = await _db.UserRoles
                .AnyAsync(x => x.UserId == refereeUserId && x.RoleId == refereeRoleId);
            if (!hasRefereeRole)
            {
                _db.UserRoles.Add(new UserRole
                {
                    UserId = refereeUserId,
                    RoleId = refereeRoleId,
                    CreatedAt = DateTime.UtcNow
                });
            }

            return null;
        }

        private sealed class ScheduleRegistrationData
        {
            public long RegistrationId { get; set; }
            public string? Player1Name { get; set; }
            public string? Player2Name { get; set; }
            public string? RelayTeamName { get; set; }
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

    public class BulkUpdateMatchSchedulesDto
    {
        public List<long>? MatchIds { get; set; }
        public bool StartAtSet { get; set; }
        public DateTime? StartAt { get; set; }
        public bool CourtTextSet { get; set; }
        public string? CourtText { get; set; }
        public bool AddressTextSet { get; set; }
        public string? AddressText { get; set; }
        public bool RefereeUserIdSet { get; set; }
        public long? RefereeUserId { get; set; }
    }
}
