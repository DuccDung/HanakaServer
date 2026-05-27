using HanakaServer.Data;
using HanakaServer.Helpers;
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
        private readonly ITournamentBracketPropagationService _bracketPropagationService;
        private readonly ITournamentStandingsService _standingsService;

        public AdminTournamentGroupMatchesController(
            PickleballDbContext db,
            TournamentUserNotificationService tournamentNotificationService,
            ITournamentBracketPropagationService bracketPropagationService,
            ITournamentStandingsService standingsService)
        {
            _db = db;
            _tournamentNotificationService = tournamentNotificationService;
            _bracketPropagationService = bracketPropagationService;
            _standingsService = standingsService;
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
                    m.Team1SourceType,
                    m.Team1SourceMatchId,
                    m.Team1SourceGroupId,
                    m.Team1SourceRank,
                    m.Team2RegistrationId,
                    m.Team2SourceType,
                    m.Team2SourceMatchId,
                    m.Team2SourceGroupId,
                    m.Team2SourceRank,
                    m.StartAt,
                    m.AddressText,
                    m.CourtText,
                    m.VideoUrl,
                    m.ScoreTeam1,
                    m.ScoreTeam2,
                    m.IsCompleted,
                    m.WinnerRegistrationId,
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

            var registrationIds = itemsRaw
                .SelectMany(x => new[] { x.Team1RegistrationId, x.Team2RegistrationId, x.WinnerRegistrationId })
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();

            var registrations = await _db.TournamentRegistrations.AsNoTracking()
                .Where(x => registrationIds.Contains(x.RegistrationId))
                .Select(x => new
                {
                    x.RegistrationId,
                    x.Player1Name,
                    x.Player2Name
                })
                .ToDictionaryAsync(x => x.RegistrationId, x => x);

            var sourceGroupIds = itemsRaw
                .SelectMany(x => new[] { x.Team1SourceGroupId, x.Team2SourceGroupId })
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();

            var sourceGroupMap = await _db.TournamentRoundGroups.AsNoTracking()
                .Where(x => sourceGroupIds.Contains(x.TournamentRoundGroupId))
                .Select(x => new { x.TournamentRoundGroupId, x.GroupName })
                .ToDictionaryAsync(x => x.TournamentRoundGroupId, x => x.GroupName);

            var refereeExternalIds = itemsRaw
                .Where(x => x.RefereeUserId.HasValue)
                .Select(x => x.RefereeUserId!.Value.ToString())
                .Distinct()
                .ToList();

            var refereeProfileMap = await _db.Referees.AsNoTracking()
                .Where(x => x.ExternalId != null && refereeExternalIds.Contains(x.ExternalId))
                .Select(x => new
                {
                    x.ExternalId,
                    x.RefereeId,
                    x.Verified,
                    x.RefereeType
                })
                .ToDictionaryAsync(x => x.ExternalId!, x => x);

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
                    Team1Text = m.Team1RegistrationId.HasValue && registrations.TryGetValue(m.Team1RegistrationId.Value, out var team1Reg)
                        ? BuildTeamText(t.GameType ?? "DOUBLE", team1Reg.Player1Name, team1Reg.Player2Name)
                        : BuildSourceText(m.Team1SourceType, m.Team1SourceMatchId, m.Team1SourceGroupId, m.Team1SourceRank, sourceGroupMap),
                    m.Team1SourceType,
                    m.Team1SourceMatchId,
                    m.Team1SourceGroupId,
                    m.Team1SourceRank,
                    Team1SourceText = BuildSourceText(m.Team1SourceType, m.Team1SourceMatchId, m.Team1SourceGroupId, m.Team1SourceRank, sourceGroupMap),
                    Team1Resolved = m.Team1RegistrationId.HasValue,
                    m.Team2RegistrationId,
                    Team2Text = m.Team2RegistrationId.HasValue && registrations.TryGetValue(m.Team2RegistrationId.Value, out var team2Reg)
                        ? BuildTeamText(t.GameType ?? "DOUBLE", team2Reg.Player1Name, team2Reg.Player2Name)
                        : BuildSourceText(m.Team2SourceType, m.Team2SourceMatchId, m.Team2SourceGroupId, m.Team2SourceRank, sourceGroupMap),
                    m.Team2SourceType,
                    m.Team2SourceMatchId,
                    m.Team2SourceGroupId,
                    m.Team2SourceRank,
                    Team2SourceText = BuildSourceText(m.Team2SourceType, m.Team2SourceMatchId, m.Team2SourceGroupId, m.Team2SourceRank, sourceGroupMap),
                    Team2Resolved = m.Team2RegistrationId.HasValue,
                    m.StartAt,
                    m.AddressText,
                    m.CourtText,
                    m.VideoUrl,
                    m.ScoreTeam1,
                    m.ScoreTeam2,
                    m.IsCompleted,
                    m.WinnerRegistrationId,
                    WinnerTeam = GetWinnerTeam(m.WinnerRegistrationId, m.Team1RegistrationId, m.Team2RegistrationId),
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
                    && x.WinnerRegistrationId.HasValue
                    && x.Team1RegistrationId.HasValue
                    && x.Team2RegistrationId.HasValue)
                .OrderBy(x => x.MatchId)
                .Select(x => new
                {
                    x.MatchId,
                    x.TournamentRoundGroupId,
                    Team1RegistrationId = x.Team1RegistrationId!.Value,
                    Team2RegistrationId = x.Team2RegistrationId!.Value,
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

        // GET /api/admin/groups/{groupId}/matches/source-options
        [HttpGet("source-options")]
        public async Task<IActionResult> GetSourceOptions(long groupId)
        {
            var current = await (
                from g in _db.TournamentRoundGroups.AsNoTracking()
                where g.TournamentRoundGroupId == groupId
                join rm in _db.TournamentRoundMaps.AsNoTracking()
                    on g.TournamentRoundMapId equals rm.TournamentRoundMapId
                join t in _db.Tournaments.AsNoTracking()
                    on rm.TournamentId equals t.TournamentId
                select new
                {
                    GroupId = g.TournamentRoundGroupId,
                    GroupName = g.GroupName,
                    RoundMapId = rm.TournamentRoundMapId,
                    rm.RoundKey,
                    rm.RoundLabel,
                    RoundSortOrder = rm.SortOrder,
                    rm.TournamentId,
                    t.GameType
                })
                .FirstOrDefaultAsync();

            if (current == null)
                return NotFound(new { message = "Không tìm thấy bảng đấu." });

            var rounds = await _db.TournamentRoundMaps.AsNoTracking()
                .Where(x => x.TournamentId == current.TournamentId && x.SortOrder <= current.RoundSortOrder)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.TournamentRoundMapId)
                .Select(x => new
                {
                    RoundMapId = x.TournamentRoundMapId,
                    x.RoundKey,
                    x.RoundLabel,
                    x.SortOrder
                })
                .ToListAsync();

            var roundIds = rounds.Select(x => x.RoundMapId).ToList();
            var groups = await _db.TournamentRoundGroups.AsNoTracking()
                .Where(x => roundIds.Contains(x.TournamentRoundMapId))
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.TournamentRoundGroupId)
                .Select(x => new
                {
                    GroupId = x.TournamentRoundGroupId,
                    RoundMapId = x.TournamentRoundMapId,
                    x.GroupName,
                    x.SortOrder
                })
                .ToListAsync();

            var groupIds = groups.Select(x => x.GroupId).ToList();
            var matches = await _db.TournamentGroupMatches.AsNoTracking()
                .Where(x => groupIds.Contains(x.TournamentRoundGroupId))
                .OrderBy(x => x.MatchId)
                .Select(x => new
                {
                    x.MatchId,
                    GroupId = x.TournamentRoundGroupId,
                    x.Team1RegistrationId,
                    x.Team2RegistrationId,
                    x.IsCompleted
                })
                .ToListAsync();

            var registrationIds = matches
                .SelectMany(x => new[] { x.Team1RegistrationId, x.Team2RegistrationId })
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();

            var registrations = await _db.TournamentRegistrations.AsNoTracking()
                .Where(x => registrationIds.Contains(x.RegistrationId))
                .Select(x => new { x.RegistrationId, x.Player1Name, x.Player2Name })
                .ToDictionaryAsync(x => x.RegistrationId, x => x);

            var previousRounds = rounds.Select(round => new
            {
                round.RoundMapId,
                round.RoundKey,
                round.RoundLabel,
                round.SortOrder,
                Groups = groups
                    .Where(g => g.RoundMapId == round.RoundMapId)
                    .Select(g => new
                    {
                        g.GroupId,
                        g.GroupName,
                        g.SortOrder,
                        Matches = matches
                            .Where(m => m.GroupId == g.GroupId)
                            .Select(m =>
                            {
                                registrations.TryGetValue(m.Team1RegistrationId ?? 0, out var team1);
                                registrations.TryGetValue(m.Team2RegistrationId ?? 0, out var team2);
                                return new
                                {
                                    m.MatchId,
                                    Label = BuildMatchOptionLabel(
                                        current.GameType,
                                        m.MatchId,
                                        team1?.Player1Name,
                                        team1?.Player2Name,
                                        team2?.Player1Name,
                                        team2?.Player2Name),
                                    m.IsCompleted
                                };
                            })
                            .ToList()
                    })
                    .ToList()
            }).ToList();

            return Ok(new
            {
                current = new
                {
                    current.GroupId,
                    current.GroupName,
                    current.RoundMapId,
                    current.RoundKey,
                    current.RoundLabel,
                    current.RoundSortOrder
                },
                previousRounds
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

            var team1Dto = NormalizeSlotDto(dto.Team1, dto.Team1RegistrationId);
            var team2Dto = NormalizeSlotDto(dto.Team2, dto.Team2RegistrationId);

            var team1 = await ValidateSlotAsync(rm.TournamentId, 0, groupId, team1Dto, 1, HttpContext.RequestAborted);
            if (!team1.Ok)
                return BadRequest(new { message = team1.Message });

            var team2 = await ValidateSlotAsync(rm.TournamentId, 0, groupId, team2Dto, 2, HttpContext.RequestAborted);
            if (!team2.Ok)
                return BadRequest(new { message = team2.Message });

            if (team1.Slot!.RegistrationId.HasValue
                && team2.Slot!.RegistrationId.HasValue
                && team1.Slot.RegistrationId.Value == team2.Slot.RegistrationId.Value)
            {
                return BadRequest(new { message = "Đội 1 và Đội 2 không được trùng nhau." });
            }

            var refereeValidationError = await ValidateAndEnsureRefereeAsync(dto.RefereeUserId);
            if (refereeValidationError != null)
                return BadRequest(new { message = refereeValidationError });

            var m = new TournamentGroupMatch
            {
                TournamentRoundGroupId = groupId,
                TournamentId = rm.TournamentId,

                Team1RegistrationId = team1.Slot.RegistrationId,
                Team1SourceType = team1.Slot.SourceType,
                Team1SourceMatchId = team1.Slot.SourceMatchId,
                Team1SourceGroupId = team1.Slot.SourceGroupId,
                Team1SourceRank = team1.Slot.SourceRank,

                Team2RegistrationId = team2.Slot!.RegistrationId,
                Team2SourceType = team2.Slot.SourceType,
                Team2SourceMatchId = team2.Slot.SourceMatchId,
                Team2SourceGroupId = team2.Slot.SourceGroupId,
                Team2SourceRank = team2.Slot.SourceRank,

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

            var team1Dto = dto.Team1 != null || dto.Team1RegistrationId.HasValue
                ? NormalizeSlotDto(dto.Team1, dto.Team1RegistrationId)
                : null;
            var team2Dto = dto.Team2 != null || dto.Team2RegistrationId.HasValue
                ? NormalizeSlotDto(dto.Team2, dto.Team2RegistrationId)
                : null;

            if (team1Dto != null)
            {
                var team1 = await ValidateSlotAsync(m.TournamentId, m.MatchId, groupId, team1Dto, 1, HttpContext.RequestAborted);
                if (!team1.Ok)
                    return BadRequest(new { message = team1.Message });

                m.Team1RegistrationId = team1.Slot!.RegistrationId;
                m.Team1SourceType = team1.Slot.SourceType;
                m.Team1SourceMatchId = team1.Slot.SourceMatchId;
                m.Team1SourceGroupId = team1.Slot.SourceGroupId;
                m.Team1SourceRank = team1.Slot.SourceRank;
            }

            if (team2Dto != null)
            {
                var team2 = await ValidateSlotAsync(m.TournamentId, m.MatchId, groupId, team2Dto, 2, HttpContext.RequestAborted);
                if (!team2.Ok)
                    return BadRequest(new { message = team2.Message });

                m.Team2RegistrationId = team2.Slot!.RegistrationId;
                m.Team2SourceType = team2.Slot.SourceType;
                m.Team2SourceMatchId = team2.Slot.SourceMatchId;
                m.Team2SourceGroupId = team2.Slot.SourceGroupId;
                m.Team2SourceRank = team2.Slot.SourceRank;
            }

            if (m.Team1RegistrationId.HasValue
                && m.Team2RegistrationId.HasValue
                && m.Team1RegistrationId.Value == m.Team2RegistrationId.Value)
            {
                return BadRequest(new { message = "Đội 1 và Đội 2 không được trùng nhau." });
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

            var usedAsSource = await _db.TournamentGroupMatches.AsNoTracking()
                .AnyAsync(x => x.Team1SourceMatchId == matchId || x.Team2SourceMatchId == matchId);

            if (usedAsSource)
                return BadRequest(new { message = "Trận này đang được dùng làm nguồn cho trận sau." });

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

            if (!m.Team1RegistrationId.HasValue || !m.Team2RegistrationId.HasValue)
                return BadRequest(new { message = "Trận chưa xác định đủ 2 đội nên chưa thể nhập điểm." });

            if (dto.ScoreTeam1 < 0 || dto.ScoreTeam2 < 0)
                return BadRequest(new { message = "Tỷ số phải lớn hơn hoặc bằng 0." });

            if (dto.ScoreTeam1 == dto.ScoreTeam2)
                return BadRequest(new { message = "Không hỗ trợ kết quả hòa. Tỷ số hai đội phải khác nhau." });

            var wasCompleted = m.IsCompleted;
            var previousWinnerRegistrationId = m.WinnerRegistrationId;

            m.ScoreTeam1 = dto.ScoreTeam1;
            m.ScoreTeam2 = dto.ScoreTeam2;
            m.IsCompleted = dto.IsCompleted;
            m.WinnerRegistrationId = dto.IsCompleted
                ? (dto.ScoreTeam1 > dto.ScoreTeam2
                    ? m.Team1RegistrationId
                    : m.Team2RegistrationId)
                : null;
            m.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            if (m.IsCompleted)
            {
                try
                {
                    if (!wasCompleted || previousWinnerRegistrationId != m.WinnerRegistrationId)
                        await _bracketPropagationService.PropagateFromMatchAsync(m.MatchId, HttpContext.RequestAborted);

                    await _bracketPropagationService.PropagateFromGroupAsync(groupId, HttpContext.RequestAborted);
                }
                catch
                {
                    // Bracket propagation must not break a score that is already saved.
                }
            }

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
                WinnerTeam = GetWinnerTeam(m.WinnerRegistrationId, m.Team1RegistrationId, m.Team2RegistrationId),
                m.UpdatedAt
            });
        }

        private static MatchSlotDto NormalizeSlotDto(MatchSlotDto? slot, long? legacyRegistrationId)
        {
            if (slot != null)
            {
                slot.SourceType = MatchSourceTypes.Normalize(slot.SourceType);
                if (slot.SourceType == MatchSourceTypes.Registration && !slot.RegistrationId.HasValue)
                    slot.RegistrationId = legacyRegistrationId;
                return slot;
            }

            return new MatchSlotDto
            {
                SourceType = MatchSourceTypes.Registration,
                RegistrationId = legacyRegistrationId
            };
        }

        private async Task<(bool Ok, string? Message, MatchSlotResolved? Slot)> ValidateSlotAsync(
            long tournamentId,
            long currentMatchId,
            long currentGroupId,
            MatchSlotDto dto,
            int slotNumber,
            CancellationToken ct)
        {
            var sourceType = MatchSourceTypes.Normalize(dto.SourceType);

            if (!MatchSourceTypes.IsValid(sourceType))
                return (false, $"Nguồn đội {slotNumber} không hợp lệ.", null);

            if (sourceType == MatchSourceTypes.Registration)
            {
                if (!dto.RegistrationId.HasValue || dto.RegistrationId.Value <= 0)
                    return (false, $"Bắt buộc chọn đội {slotNumber}.", null);

                var registration = await _db.TournamentRegistrations.AsNoTracking()
                    .Where(x => x.TournamentId == tournamentId && x.RegistrationId == dto.RegistrationId.Value)
                    .Select(x => new { x.RegistrationId, x.Success })
                    .FirstOrDefaultAsync(ct);

                if (registration == null)
                    return (false, $"Không tìm thấy đăng ký đội {slotNumber} trong giải đấu này.", null);

                if (!registration.Success)
                    return (false, $"Đội {slotNumber} chưa được duyệt thành công.", null);

                return (true, null, new MatchSlotResolved
                {
                    SourceType = MatchSourceTypes.Registration,
                    RegistrationId = registration.RegistrationId
                });
            }

            if (sourceType == MatchSourceTypes.WinnerMatch || sourceType == MatchSourceTypes.LoserMatch)
            {
                if (!dto.SourceMatchId.HasValue || dto.SourceMatchId.Value <= 0)
                    return (false, $"Bắt buộc chọn trận nguồn cho đội {slotNumber}.", null);

                if (currentMatchId > 0 && dto.SourceMatchId.Value == currentMatchId)
                    return (false, "Trận không được lấy nguồn từ chính nó.", null);

                var source = await (
                    from m in _db.TournamentGroupMatches.AsNoTracking()
                    where m.MatchId == dto.SourceMatchId.Value
                    join g in _db.TournamentRoundGroups.AsNoTracking()
                        on m.TournamentRoundGroupId equals g.TournamentRoundGroupId
                    join rm in _db.TournamentRoundMaps.AsNoTracking()
                        on g.TournamentRoundMapId equals rm.TournamentRoundMapId
                    select new
                    {
                        m.MatchId,
                        m.TournamentId,
                        m.Team1RegistrationId,
                        m.Team2RegistrationId,
                        m.IsCompleted,
                        m.WinnerRegistrationId,
                        RoundSortOrder = rm.SortOrder
                    })
                    .FirstOrDefaultAsync(ct);

                if (source == null || source.TournamentId != tournamentId)
                    return (false, "Trận nguồn không thuộc cùng giải đấu.", null);

                if (!await IsSourceRoundValidAsync(currentGroupId, source.RoundSortOrder, ct))
                    return (false, "Trận nguồn không được nằm ở vòng sau vòng hiện tại.", null);

                if (currentMatchId > 0
                    && await WouldCreateCycleAsync(currentMatchId, dto.SourceMatchId.Value, ct))
                {
                    return (false, "Thiết lập nguồn trận tạo vòng lặp phụ thuộc.", null);
                }

                long? resolvedRegistrationId = null;
                if (source.IsCompleted && source.WinnerRegistrationId.HasValue)
                {
                    if (sourceType == MatchSourceTypes.WinnerMatch)
                    {
                        resolvedRegistrationId = source.WinnerRegistrationId.Value;
                    }
                    else if (source.Team1RegistrationId.HasValue && source.Team2RegistrationId.HasValue)
                    {
                        resolvedRegistrationId = source.WinnerRegistrationId.Value == source.Team1RegistrationId.Value
                            ? source.Team2RegistrationId
                            : source.WinnerRegistrationId.Value == source.Team2RegistrationId.Value
                                ? source.Team1RegistrationId
                                : null;
                    }
                }

                return (true, null, new MatchSlotResolved
                {
                    SourceType = sourceType,
                    RegistrationId = resolvedRegistrationId,
                    SourceMatchId = source.MatchId
                });
            }

            if (sourceType == MatchSourceTypes.GroupRank)
            {
                if (!dto.SourceGroupId.HasValue || dto.SourceGroupId.Value <= 0)
                    return (false, $"Bắt buộc chọn bảng nguồn cho đội {slotNumber}.", null);

                if (!dto.SourceRank.HasValue || dto.SourceRank.Value < 1)
                    return (false, $"Thứ hạng nguồn của đội {slotNumber} phải >= 1.", null);

                var sourceGroup = await (
                    from g in _db.TournamentRoundGroups.AsNoTracking()
                    where g.TournamentRoundGroupId == dto.SourceGroupId.Value
                    join rm in _db.TournamentRoundMaps.AsNoTracking()
                        on g.TournamentRoundMapId equals rm.TournamentRoundMapId
                    select new
                    {
                        g.TournamentRoundGroupId,
                        rm.TournamentId,
                        RoundSortOrder = rm.SortOrder
                    })
                    .FirstOrDefaultAsync(ct);

                if (sourceGroup == null || sourceGroup.TournamentId != tournamentId)
                    return (false, "Bảng nguồn không thuộc cùng giải đấu.", null);

                if (!await IsSourceRoundValidAsync(currentGroupId, sourceGroup.RoundSortOrder, ct))
                    return (false, "Bảng nguồn không được nằm ở vòng sau vòng hiện tại.", null);

                long? resolvedRegistrationId = null;
                if (await _standingsService.IsGroupCompletedAsync(sourceGroup.TournamentRoundGroupId, ct))
                {
                    var standings = await _standingsService.GetGroupStandingsAsync(sourceGroup.TournamentRoundGroupId, ct);
                    resolvedRegistrationId = standings.FirstOrDefault(x => x.Rank == dto.SourceRank.Value)?.RegistrationId;
                }

                return (true, null, new MatchSlotResolved
                {
                    SourceType = MatchSourceTypes.GroupRank,
                    RegistrationId = resolvedRegistrationId,
                    SourceGroupId = sourceGroup.TournamentRoundGroupId,
                    SourceRank = dto.SourceRank.Value
                });
            }

            return (true, null, new MatchSlotResolved
            {
                SourceType = MatchSourceTypes.Bye
            });
        }

        private async Task<bool> IsSourceRoundValidAsync(long currentGroupId, int sourceRoundSortOrder, CancellationToken ct)
        {
            var currentRound = await (
                from g in _db.TournamentRoundGroups.AsNoTracking()
                where g.TournamentRoundGroupId == currentGroupId
                join rm in _db.TournamentRoundMaps.AsNoTracking()
                    on g.TournamentRoundMapId equals rm.TournamentRoundMapId
                select new { rm.SortOrder })
                .FirstOrDefaultAsync(ct);

            return currentRound == null || sourceRoundSortOrder <= currentRound.SortOrder;
        }

        private async Task<bool> WouldCreateCycleAsync(long currentMatchId, long sourceMatchId, CancellationToken ct)
        {
            var stack = new Stack<long>();
            var visited = new HashSet<long>();
            stack.Push(sourceMatchId);

            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (!visited.Add(id))
                    continue;

                if (id == currentMatchId)
                    return true;

                var next = await _db.TournamentGroupMatches.AsNoTracking()
                    .Where(x => x.MatchId == id)
                    .Select(x => new { x.Team1SourceMatchId, x.Team2SourceMatchId })
                    .FirstOrDefaultAsync(ct);

                if (next == null)
                    continue;

                if (next.Team1SourceMatchId.HasValue)
                    stack.Push(next.Team1SourceMatchId.Value);

                if (next.Team2SourceMatchId.HasValue)
                    stack.Push(next.Team2SourceMatchId.Value);
            }

            return false;
        }

        private static string BuildSourceText(
            string? sourceType,
            long? sourceMatchId,
            long? sourceGroupId,
            int? sourceRank,
            IReadOnlyDictionary<long, string>? sourceGroupMap = null)
        {
            sourceType = MatchSourceTypes.Normalize(sourceType);

            return sourceType switch
            {
                MatchSourceTypes.WinnerMatch => sourceMatchId.HasValue ? $"Chờ thắng trận #{sourceMatchId}" : "Chờ thắng trận",
                MatchSourceTypes.LoserMatch => sourceMatchId.HasValue ? $"Chờ thua trận #{sourceMatchId}" : "Chờ thua trận",
                MatchSourceTypes.GroupRank => BuildGroupRankSourceText(sourceGroupId, sourceRank, sourceGroupMap),
                MatchSourceTypes.Bye => "Miễn đấu",
                _ => "TBD"
            };
        }

        private static string BuildGroupRankSourceText(
            long? sourceGroupId,
            int? sourceRank,
            IReadOnlyDictionary<long, string>? sourceGroupMap)
        {
            var groupName = sourceGroupId.HasValue
                && sourceGroupMap != null
                && sourceGroupMap.TryGetValue(sourceGroupId.Value, out var found)
                    ? found
                    : (sourceGroupId.HasValue ? $"#{sourceGroupId}" : "");

            return sourceRank.HasValue
                ? $"Chờ hạng {sourceRank.Value} bảng {groupName}".Trim()
                : $"Chờ hạng bảng {groupName}".Trim();
        }

        private static string? GetWinnerTeam(long? winnerRegistrationId, long? team1RegistrationId, long? team2RegistrationId)
        {
            if (!winnerRegistrationId.HasValue)
                return null;

            if (team1RegistrationId.HasValue && winnerRegistrationId.Value == team1RegistrationId.Value)
                return "1";

            if (team2RegistrationId.HasValue && winnerRegistrationId.Value == team2RegistrationId.Value)
                return "2";

            return null;
        }

        private static string BuildTeamText(string gameType, TournamentRegistration r)
        {
            return BuildTeamText(gameType, r.Player1Name, r.Player2Name);
        }

        private static string BuildMatchOptionLabel(
            string? gameType,
            long matchId,
            string? team1Player1Name,
            string? team1Player2Name,
            string? team2Player1Name,
            string? team2Player2Name)
        {
            var team1Text = string.IsNullOrWhiteSpace(team1Player1Name)
                ? "TBD"
                : BuildTeamText(gameType ?? "DOUBLE", team1Player1Name, team1Player2Name);
            var team2Text = string.IsNullOrWhiteSpace(team2Player1Name)
                ? "TBD"
                : BuildTeamText(gameType ?? "DOUBLE", team2Player1Name, team2Player2Name);

            return $"Trận #{matchId} - {team1Text} vs {team2Text}";
        }

        private static string BuildTeamText(string gameType, string? player1Name, string? player2Name)
        {
            gameType = (gameType ?? "DOUBLE").Trim().ToUpperInvariant();

            if (gameType == "SINGLE")
                return (player1Name ?? "").Trim();

            var p1 = (player1Name ?? "").Trim();
            var p2 = (player2Name ?? "").Trim();

            if (string.IsNullOrWhiteSpace(p2))
                return p1;

            return $"{p1} & {p2}";
        }

        private sealed class MatchSlotResolved
        {
            public string SourceType { get; set; } = MatchSourceTypes.Registration;
            public long? RegistrationId { get; set; }
            public long? SourceMatchId { get; set; }
            public long? SourceGroupId { get; set; }
            public int? SourceRank { get; set; }
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
        public MatchSlotDto? Team1 { get; set; }
        public MatchSlotDto? Team2 { get; set; }
        public long? Team1RegistrationId { get; set; }
        public long? Team2RegistrationId { get; set; }
        public DateTime? StartAt { get; set; }
        public string? AddressText { get; set; }
        public string? CourtText { get; set; }
        public string? VideoUrl { get; set; }

        // NEW
        public long? RefereeUserId { get; set; }
    }

    public class UpdateMatchDto
    {
        public MatchSlotDto? Team1 { get; set; }
        public MatchSlotDto? Team2 { get; set; }
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

    public class MatchSlotDto
    {
        public string SourceType { get; set; } = MatchSourceTypes.Registration;
        public long? RegistrationId { get; set; }
        public long? SourceMatchId { get; set; }
        public long? SourceGroupId { get; set; }
        public int? SourceRank { get; set; }
    }

    public class SetScoreDto
    {
        public int ScoreTeam1 { get; set; }
        public int ScoreTeam2 { get; set; }
        public bool IsCompleted { get; set; } = true;
    }
}
