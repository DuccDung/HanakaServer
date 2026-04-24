using HanakaServer.Data;
using HanakaServer.Models;
using HanakaServer.Services;
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
        private readonly TournamentUserNotificationService _tournamentNotificationService;

        public AdminTournamentGroupMatchesController(
            PickleballDbContext db,
            TournamentUserNotificationService tournamentNotificationService)
        {
            _db = db;
            _tournamentNotificationService = tournamentNotificationService;
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
                return NotFound(new { message = "Không tìm thấy bảng đấu." });

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
                return NotFound(new { message = "Không tìm thấy vòng đấu." });

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
                return NotFound(new { message = "Không tìm thấy giải đấu." });

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

        // GET /api/admin/groups/{groupId}/matches/winner-sources
        [HttpGet("winner-sources")]
        public async Task<IActionResult> GetWinnerSources(long groupId)
        {
            var currentGroup = await _db.TournamentRoundGroups.AsNoTracking()
                .Where(x => x.TournamentRoundGroupId == groupId)
                .Select(x => new
                {
                    x.TournamentRoundGroupId,
                    x.TournamentRoundMapId,
                    x.GroupName,
                    x.SortOrder
                })
                .FirstOrDefaultAsync();

            if (currentGroup == null)
                return NotFound(new { message = "Không tìm thấy bảng đấu." });

            var currentRound = await _db.TournamentRoundMaps.AsNoTracking()
                .Where(x => x.TournamentRoundMapId == currentGroup.TournamentRoundMapId)
                .Select(x => new
                {
                    x.TournamentRoundMapId,
                    x.TournamentId,
                    x.RoundKey,
                    x.RoundLabel,
                    x.SortOrder
                })
                .FirstOrDefaultAsync();

            if (currentRound == null)
                return NotFound(new { message = "Không tìm thấy vòng đấu." });

            var previousRounds = await _db.TournamentRoundMaps.AsNoTracking()
                .Where(x => x.TournamentId == currentRound.TournamentId
                    && (x.SortOrder < currentRound.SortOrder
                        || (x.SortOrder == currentRound.SortOrder && x.TournamentRoundMapId < currentRound.TournamentRoundMapId)))
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.TournamentRoundMapId)
                .Select(x => new
                {
                    x.TournamentRoundMapId,
                    x.RoundKey,
                    x.RoundLabel,
                    x.SortOrder
                })
                .ToListAsync();

            if (!previousRounds.Any())
            {
                return Ok(new
                {
                    current = new
                    {
                        groupId = currentGroup.TournamentRoundGroupId,
                        groupName = currentGroup.GroupName,
                        roundMapId = currentRound.TournamentRoundMapId,
                        roundKey = currentRound.RoundKey,
                        roundLabel = currentRound.RoundLabel,
                        roundSortOrder = currentRound.SortOrder
                    },
                    rounds = Array.Empty<object>()
                });
            }

            var previousRoundIds = previousRounds
                .Select(x => x.TournamentRoundMapId)
                .ToList();

            var groups = await _db.TournamentRoundGroups.AsNoTracking()
                .Where(x => previousRoundIds.Contains(x.TournamentRoundMapId))
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.TournamentRoundGroupId)
                .Select(x => new
                {
                    x.TournamentRoundGroupId,
                    x.TournamentRoundMapId,
                    x.GroupName,
                    x.SortOrder
                })
                .ToListAsync();

            var previousGroupIds = groups
                .Select(x => x.TournamentRoundGroupId)
                .ToList();

            var matches = await _db.TournamentGroupMatches.AsNoTracking()
                .Where(x => previousGroupIds.Contains(x.TournamentRoundGroupId)
                    && x.IsCompleted
                    && x.WinnerRegistrationId.HasValue)
                .OrderBy(x => x.MatchId)
                .Select(x => new
                {
                    x.MatchId,
                    x.TournamentRoundGroupId,
                    WinnerRegistrationId = x.WinnerRegistrationId!.Value
                })
                .ToListAsync();

            var roundResults = previousRounds.Select(round =>
            {
                var groupsInRound = groups
                    .Where(g => g.TournamentRoundMapId == round.TournamentRoundMapId)
                    .Select(g =>
                    {
                        var groupMatches = matches
                            .Where(m => m.TournamentRoundGroupId == g.TournamentRoundGroupId)
                            .ToList();

                        var winnerStats = groupMatches
                            .GroupBy(m => m.WinnerRegistrationId)
                            .Select(w => new
                            {
                                registrationId = w.Key,
                                winCount = w.Count(),
                                latestMatchId = w.Max(x => x.MatchId)
                            })
                            .OrderByDescending(x => x.winCount)
                            .ThenByDescending(x => x.latestMatchId)
                            .ThenBy(x => x.registrationId)
                            .ToList();

                        return new
                        {
                            groupId = g.TournamentRoundGroupId,
                            groupName = g.GroupName,
                            sortOrder = g.SortOrder,
                            completedMatchCount = groupMatches.Count,
                            winnerCount = winnerStats.Count,
                            winners = winnerStats
                        };
                    })
                    .ToList();

                return new
                {
                    roundMapId = round.TournamentRoundMapId,
                    roundKey = round.RoundKey,
                    roundLabel = round.RoundLabel,
                    sortOrder = round.SortOrder,
                    groups = groupsInRound
                };
            }).ToList();

            return Ok(new
            {
                current = new
                {
                    groupId = currentGroup.TournamentRoundGroupId,
                    groupName = currentGroup.GroupName,
                    roundMapId = currentRound.TournamentRoundMapId,
                    roundKey = currentRound.RoundKey,
                    roundLabel = currentRound.RoundLabel,
                    roundSortOrder = currentRound.SortOrder
                },
                rounds = roundResults
            });
        }

        // GET /api/admin/groups/{groupId}/matches/loser-sources
        [HttpGet("loser-sources")]
        public async Task<IActionResult> GetLoserSources(long groupId)
        {
            var currentGroup = await _db.TournamentRoundGroups.AsNoTracking()
                .Where(x => x.TournamentRoundGroupId == groupId)
                .Select(x => new
                {
                    x.TournamentRoundGroupId,
                    x.TournamentRoundMapId,
                    x.GroupName,
                    x.SortOrder
                })
                .FirstOrDefaultAsync();

            if (currentGroup == null)
                return NotFound(new { message = "Không tìm thấy bảng đấu." });

            var currentRound = await _db.TournamentRoundMaps.AsNoTracking()
                .Where(x => x.TournamentRoundMapId == currentGroup.TournamentRoundMapId)
                .Select(x => new
                {
                    x.TournamentRoundMapId,
                    x.TournamentId,
                    x.RoundKey,
                    x.RoundLabel,
                    x.SortOrder
                })
                .FirstOrDefaultAsync();

            if (currentRound == null)
                return NotFound(new { message = "Không tìm thấy vòng đấu." });

            var previousRounds = await _db.TournamentRoundMaps.AsNoTracking()
                .Where(x => x.TournamentId == currentRound.TournamentId
                    && (x.SortOrder < currentRound.SortOrder
                        || (x.SortOrder == currentRound.SortOrder && x.TournamentRoundMapId < currentRound.TournamentRoundMapId)))
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.TournamentRoundMapId)
                .Select(x => new
                {
                    x.TournamentRoundMapId,
                    x.RoundKey,
                    x.RoundLabel,
                    x.SortOrder
                })
                .ToListAsync();

            if (!previousRounds.Any())
            {
                return Ok(new
                {
                    current = new
                    {
                        groupId = currentGroup.TournamentRoundGroupId,
                        groupName = currentGroup.GroupName,
                        roundMapId = currentRound.TournamentRoundMapId,
                        roundKey = currentRound.RoundKey,
                        roundLabel = currentRound.RoundLabel,
                        roundSortOrder = currentRound.SortOrder
                    },
                    rounds = Array.Empty<object>()
                });
            }

            var previousRoundIds = previousRounds
                .Select(x => x.TournamentRoundMapId)
                .ToList();

            var groups = await _db.TournamentRoundGroups.AsNoTracking()
                .Where(x => previousRoundIds.Contains(x.TournamentRoundMapId))
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.TournamentRoundGroupId)
                .Select(x => new
                {
                    x.TournamentRoundGroupId,
                    x.TournamentRoundMapId,
                    x.GroupName,
                    x.SortOrder
                })
                .ToListAsync();

            var previousGroupIds = groups
                .Select(x => x.TournamentRoundGroupId)
                .ToList();

            var matches = await _db.TournamentGroupMatches.AsNoTracking()
                .Where(x => previousGroupIds.Contains(x.TournamentRoundGroupId)
                    && x.IsCompleted
                    && x.WinnerRegistrationId.HasValue)
                .OrderBy(x => x.MatchId)
                .Select(x => new
                {
                    x.MatchId,
                    x.TournamentRoundGroupId,
                    x.Team1RegistrationId,
                    x.Team2RegistrationId,
                    WinnerRegistrationId = x.WinnerRegistrationId!.Value
                })
                .ToListAsync();

            var roundResults = previousRounds.Select(round =>
            {
                var groupsInRound = groups
                    .Where(g => g.TournamentRoundMapId == round.TournamentRoundMapId)
                    .Select(g =>
                    {
                        var groupMatches = matches
                            .Where(m => m.TournamentRoundGroupId == g.TournamentRoundGroupId)
                            .ToList();

                        var loserStats = groupMatches
                            .Select(m => new
                            {
                                registrationId = m.WinnerRegistrationId == m.Team1RegistrationId
                                    ? m.Team2RegistrationId
                                    : m.Team1RegistrationId,
                                m.MatchId
                            })
                            .GroupBy(m => m.registrationId)
                            .Select(l => new
                            {
                                registrationId = l.Key,
                                lossCount = l.Count(),
                                latestMatchId = l.Max(x => x.MatchId)
                            })
                            .OrderByDescending(x => x.lossCount)
                            .ThenByDescending(x => x.latestMatchId)
                            .ThenBy(x => x.registrationId)
                            .ToList();

                        return new
                        {
                            groupId = g.TournamentRoundGroupId,
                            groupName = g.GroupName,
                            sortOrder = g.SortOrder,
                            completedMatchCount = groupMatches.Count,
                            loserCount = loserStats.Count,
                            losers = loserStats
                        };
                    })
                    .ToList();

                return new
                {
                    roundMapId = round.TournamentRoundMapId,
                    roundKey = round.RoundKey,
                    roundLabel = round.RoundLabel,
                    sortOrder = round.SortOrder,
                    groups = groupsInRound
                };
            }).ToList();

            return Ok(new
            {
                current = new
                {
                    groupId = currentGroup.TournamentRoundGroupId,
                    groupName = currentGroup.GroupName,
                    roundMapId = currentRound.TournamentRoundMapId,
                    roundKey = currentRound.RoundKey,
                    roundLabel = currentRound.RoundLabel,
                    roundSortOrder = currentRound.SortOrder
                },
                rounds = roundResults
            });
        }

        // POST /api/admin/groups/{groupId}/matches
        [HttpPost]
        public async Task<IActionResult> Create(long groupId, [FromBody] CreateMatchDto dto)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var g = await _db.TournamentRoundGroups.FirstOrDefaultAsync(x => x.TournamentRoundGroupId == groupId);
            if (g == null)
                return NotFound(new { message = "Không tìm thấy bảng đấu." });

            var rm = await _db.TournamentRoundMaps.FirstOrDefaultAsync(x => x.TournamentRoundMapId == g.TournamentRoundMapId);
            if (rm == null)
                return NotFound(new { message = "Không tìm thấy vòng đấu." });

            if (dto.Team1RegistrationId <= 0 || dto.Team2RegistrationId <= 0)
                return BadRequest(new { message = "Bắt buộc chọn đủ Đội 1 và Đội 2." });

            if (dto.Team1RegistrationId == dto.Team2RegistrationId)
                return BadRequest(new { message = "Đội 1 và Đội 2 không được trùng nhau." });

            var regs = await _db.TournamentRegistrations.AsNoTracking()
                .Where(x => x.TournamentId == rm.TournamentId
                    && (x.RegistrationId == dto.Team1RegistrationId || x.RegistrationId == dto.Team2RegistrationId))
                .Select(x => new { x.RegistrationId, x.Success })
                .ToListAsync();

            if (regs.Count != 2)
                return BadRequest(new { message = "Không tìm thấy đăng ký của hai đội trong giải đấu này." });

            if (regs.Any(x => !x.Success))
                return BadRequest(new { message = "Chỉ được dùng các đăng ký đã duyệt thành công để tạo trận đấu." });

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
                    message = "Tạo trận đấu thất bại. Có thể cặp đội này đã tồn tại trong bảng đấu.",
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
                return NotFound(new { message = "Không tìm thấy trận đấu." });

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
                        message = "Cập nhật trận đấu đã kết thúc thất bại.",
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
                    return BadRequest(new { message = "Đội 1 và Đội 2 không được trùng nhau." });

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
                    message = "Cập nhật trận đấu thất bại. Có thể cặp đội này đã tồn tại trong bảng đấu.",
                    detail = ex.Message
                });
            }

            return Ok(new { ok = true });
        }
        // DELETE /api/admin/groups/{groupId}/matches/{matchId}
        [HttpDelete("{matchId:long}")]
        public async Task<IActionResult> Delete(long groupId, long matchId)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            var m = await _db.TournamentGroupMatches
                .FirstOrDefaultAsync(x => x.MatchId == matchId && x.TournamentRoundGroupId == groupId);

            if (m == null)
                return NotFound(new { message = "Không tìm thấy trận đấu." });

            var scoreHistories = await _db.TournamentMatchScoreHistories
                .Where(x => x.MatchId == matchId)
                .ToListAsync();

            if (scoreHistories.Count > 0)
            {
                _db.TournamentMatchScoreHistories.RemoveRange(scoreHistories);
            }

            var relatedNotifications = await _db.UserNotifications
                .Where(x => x.RefType == "MATCH" && x.RefId == matchId)
                .ToListAsync();

            if (relatedNotifications.Count > 0)
            {
                _db.UserNotifications.RemoveRange(relatedNotifications);
            }

            _db.TournamentGroupMatches.Remove(m);

            try
            {
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (DbUpdateException ex)
            {
                await tx.RollbackAsync();

                var innerMessage = ex.InnerException?.Message ?? ex.Message;

                return BadRequest(new
                {
                    message = "Xóa trận đấu thất bại vì vẫn còn dữ liệu đang liên kết với trận này.",
                    detail = innerMessage
                });
            }

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
                return NotFound(new { message = "Không tìm thấy trận đấu." });

            if (dto.ScoreTeam1 < 0 || dto.ScoreTeam2 < 0)
                return BadRequest(new { message = "Tỷ số phải lớn hơn hoặc bằng 0." });

            if (dto.ScoreTeam1 == dto.ScoreTeam2)
                return BadRequest(new { message = "Không hỗ trợ kết quả hòa. Tỷ số hai đội phải khác nhau." });

            var wasCompleted = m.IsCompleted;
            var previousWinnerRegistrationId = m.WinnerRegistrationId;

            m.ScoreTeam1 = dto.ScoreTeam1;
            m.ScoreTeam2 = dto.ScoreTeam2;
            m.IsCompleted = dto.IsCompleted;
            m.WinnerRegistrationId = dto.ScoreTeam1 > dto.ScoreTeam2
                ? m.Team1RegistrationId
                : m.Team2RegistrationId;
            m.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            if (m.IsCompleted && (!wasCompleted || previousWinnerRegistrationId != m.WinnerRegistrationId))
            {
                try
                {
                    await _tournamentNotificationService.NotifyMatchWinnerAsync(m.MatchId);
                }
                catch
                {
                    // Realtime notification must not break a score that is already saved.
                }
            }

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
                return "Không tìm thấy vai trò trọng tài trong hệ thống.";

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
