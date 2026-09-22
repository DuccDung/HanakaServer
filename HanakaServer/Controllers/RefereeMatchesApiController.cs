using System.Security.Claims;
using HanakaServer.Data;
using HanakaServer.Helpers;
using HanakaServer.Models;
using HanakaServer.Models.Dto;
using HanakaServer.Services;
using HanakaServer.Services.Relay;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json;
using HanakaServer.Dtos.Relay;
using HanakaServer.Options;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/referee/matches")]
    [Authorize(Roles = "REFEREE,Admin")]
    public class RefereeMatchesApiController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly PublicRealtimeHub _publicRealtimeHub;
        private readonly TournamentUserNotificationService _tournamentNotificationService;
        private readonly ITournamentBracketPropagationService _bracketPropagationService;
        private readonly ILogger<RefereeMatchesApiController> _logger;
        private readonly RelayLegacyWriteGuard? _relayGuard;
        private readonly RelayMatchLineupSnapshotService? _relayLineupSnapshots;
        private readonly RelayScoringService? _relayScoring;

        public RefereeMatchesApiController(
            PickleballDbContext db,
            PublicRealtimeHub publicRealtimeHub,
            TournamentUserNotificationService tournamentNotificationService,
            ITournamentBracketPropagationService bracketPropagationService,
            ILogger<RefereeMatchesApiController> logger,
            RelayLegacyWriteGuard? relayGuard = null,
            RelayMatchLineupSnapshotService? relayLineupSnapshots = null,
            RelayScoringService? relayScoring = null)
        {
            _db = db;
            _publicRealtimeHub = publicRealtimeHub;
            _tournamentNotificationService = tournamentNotificationService;
            _bracketPropagationService = bracketPropagationService;
            _logger = logger;
            _relayGuard = relayGuard;
            _relayLineupSnapshots = relayLineupSnapshots;
            _relayScoring = relayScoring ?? (relayGuard?.Enabled == true
                ? new RelayScoringService(db, Microsoft.Extensions.Options.Options.Create(new RelayOptions { AdminPreviewEnabled = true })) : null);
        }

        private long? GetCurrentUserId()
        {
            var raw = User.FindFirstValue("UserId")
                      ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

            return long.TryParse(raw, out var id) ? id : null;
        }

        private static bool CanScoreMatchToday(DateTime? startAt)
        {
            if (!startAt.HasValue)
                return false;

            return NormalizeMatchStart(startAt.Value).Date == DateTime.Now.Date;
        }

        private static DateTime NormalizeMatchStart(DateTime startAt)
        {
            return startAt.Kind switch
            {
                DateTimeKind.Utc => startAt.ToLocalTime(),
                DateTimeKind.Local => startAt,
                _ => DateTime.SpecifyKind(startAt, DateTimeKind.Local)
            };
        }

        [HttpGet]
        public async Task<IActionResult> ListMyMatches()
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
                return Unauthorized(new { message = "Không xác định được user hiện tại." });
            var canViewVirtualTeams = User.IsInRole("Admin");
            // Hold parent reads until their parts/history have been read as well.
            await using var readTx = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, HttpContext.RequestAborted) : null;

            var matches = await (
     from m in _db.TournamentGroupMatches.AsNoTracking()
     where m.RefereeUserId == currentUserId.Value
        && m.Team1RegistrationId.HasValue
        && m.Team2RegistrationId.HasValue
     join g in _db.TournamentRoundGroups.AsNoTracking()
         on m.TournamentRoundGroupId equals g.TournamentRoundGroupId
     join rm in _db.TournamentRoundMaps.AsNoTracking()
         on g.TournamentRoundMapId equals rm.TournamentRoundMapId
     join t in _db.Tournaments.AsNoTracking()
         on m.TournamentId equals t.TournamentId
     join r1 in _db.TournamentRegistrations.AsNoTracking()
         on m.Team1RegistrationId!.Value equals r1.RegistrationId
     join r2 in _db.TournamentRegistrations.AsNoTracking()
         on m.Team2RegistrationId!.Value equals r2.RegistrationId
     where t.Status != "CLOSED"
     orderby (m.StartAt ?? DateTime.MaxValue), m.MatchId
     select new
     {
         m.MatchId,
         m.TournamentRoundGroupId,
         m.TournamentId,

         TournamentTitle = t.Title,
         RoundKey = rm.RoundKey,
         RoundLabel = rm.RoundLabel,
         GroupName = g.GroupName,

         m.Team1RegistrationId,
         Team1Text = BuildTeamText(t.GameType ?? "DOUBLE", r1, canViewVirtualTeams),

         m.Team2RegistrationId,
         Team2Text = BuildTeamText(t.GameType ?? "DOUBLE", r2, canViewVirtualTeams),

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
             : (m.WinnerRegistrationId == m.Team1RegistrationId ? "1" : "2")
     }
 ).ToListAsync();

            var matchIds = matches.Select(x => x.MatchId).ToList();

            var histories = await _db.TournamentMatchScoreHistories.AsNoTracking()
                .Where(x => matchIds.Contains(x.MatchId))
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.ScoreHistoryId)
                .Select(x => new
                {
                    x.ScoreHistoryId,
                    x.MatchId,
                    x.RefereeUserId,
                    RefereeName = x.ActorName ?? (x.RefereeUser != null ? x.RefereeUser.FullName : "Quản trị viên"),
                    x.ScoreTeam1,
                    x.ScoreTeam2,
                    x.IsCompleted,
                    x.WinnerRegistrationId,
                    WinnerTeam = x.WinnerRegistrationId == null
                        ? null
                        : (x.WinnerRegistrationId == x.Match.Team1RegistrationId ? "1" : "2"),
                    x.Note,
                    x.RelayPartNumber,
                    x.RelayPartsJson,
                    x.CreatedAt
                })
                .ToListAsync();

            var historyMap = histories
                .GroupBy(x => x.MatchId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var relayTournamentIds = new HashSet<long>();
            var relayTeamNames = new Dictionary<long, string>();
            var relaySnapshotNames = new Dictionary<(long MatchId, int Side), string>();
            if (_relayGuard?.Enabled == true)
            {
                var tournamentIds = matches.Select(x => x.TournamentId).Distinct().ToArray();
                relayTournamentIds = (await _db.RelayTournamentSettings.AsNoTracking()
                    .Where(x => tournamentIds.Contains(x.TournamentId))
                    .Select(x => x.TournamentId).ToListAsync()).ToHashSet();
                var registrationIds = matches
                    .Where(x => relayTournamentIds.Contains(x.TournamentId))
                    .SelectMany(x => new[] { x.Team1RegistrationId, x.Team2RegistrationId })
                    .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
                relayTeamNames = await _db.RelayTeams.AsNoTracking()
                    .Where(x => registrationIds.Contains(x.RegistrationId))
                    .ToDictionaryAsync(x => x.RegistrationId, x => x.TeamName);
                var relayMatchIds = matches.Where(x => relayTournamentIds.Contains(x.TournamentId))
                    .Select(x => x.MatchId).ToArray();
                relaySnapshotNames = (await _db.RelayMatchLineupSnapshots.AsNoTracking()
                    .Where(x => relayMatchIds.Contains(x.MatchId))
                    .Select(x => new { x.MatchId, x.Side, x.TeamName })
                    .ToListAsync()).ToDictionary(x => (x.MatchId, x.Side), x => x.TeamName);
            }

            var relayScores = _relayScoring == null ? new Dictionary<long, RelayMatchScore>()
                : await _relayScoring.ReadAsync(matches.Where(x => relayTournamentIds.Contains(x.TournamentId)).Select(x => x.MatchId), HttpContext.RequestAborted);

            var items = matches.Select(m => new
            {
                m.MatchId,
                m.TournamentRoundGroupId,
                m.TournamentId,
                m.TournamentTitle,
                m.RoundKey,
                m.RoundLabel,
                m.GroupName,
                m.Team1RegistrationId,
                Team1Text = relaySnapshotNames.TryGetValue((m.MatchId, 1), out var snapshotTeam1)
                    ? snapshotTeam1
                    : m.Team1RegistrationId.HasValue
                      && relayTeamNames.TryGetValue(m.Team1RegistrationId.Value, out var relayTeam1)
                        ? relayTeam1 : m.Team1Text,
                m.Team2RegistrationId,
                Team2Text = relaySnapshotNames.TryGetValue((m.MatchId, 2), out var snapshotTeam2)
                    ? snapshotTeam2
                    : m.Team2RegistrationId.HasValue
                      && relayTeamNames.TryGetValue(m.Team2RegistrationId.Value, out var relayTeam2)
                        ? relayTeam2 : m.Team2Text,
                m.StartAt,
                m.AddressText,
                m.CourtText,
                m.VideoUrl,
                m.ScoreTeam1,
                m.ScoreTeam2,
                m.IsCompleted,
                m.WinnerRegistrationId,
                m.WinnerTeam,
                IsRelay = relayTournamentIds.Contains(m.TournamentId),
                RelayScores = relayTournamentIds.Contains(m.TournamentId)
                    ? RelayScoringService.Map(relayScores.GetValueOrDefault(m.MatchId), m.ScoreTeam1, m.ScoreTeam2) : null,
                CanEditScore = CanScoreMatchToday(m.StartAt),
                ScoreHistories = historyMap.ContainsKey(m.MatchId)
                                ? historyMap[m.MatchId].Cast<object>().ToList()
                                : new List<object>()
            });

            var responseItems = items.ToArray();
            if (readTx != null) await readTx.CommitAsync(HttpContext.RequestAborted);
            return Ok(new { items = responseItems });
        }

        [HttpPut("{matchId:long}/score")]
        public async Task<IActionResult> SetScore(long matchId, [FromBody] RefereeSetScoreDto dto)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
                return Unauthorized(new { message = "Không xác định được user hiện tại." });

            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            if (_db.Database.IsRelational())
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT MatchId FROM dbo.TournamentGroupMatches WITH (UPDLOCK, HOLDLOCK) WHERE MatchId = {matchId} AND RefereeUserId = {currentUserId.Value}", HttpContext.RequestAborted);

            var m = await _db.TournamentGroupMatches
                .FirstOrDefaultAsync(x => x.MatchId == matchId && x.RefereeUserId == currentUserId.Value);

            if (m == null)
                return NotFound(new { message = "Không tìm thấy trận đấu hoặc bạn không phải trọng tài của trận này." });

            if (m.CompletionReason == MatchCompletionReasons.Bye)
                return BadRequest(new { message = "Trận miễn đấu đã tự động hoàn thành và không cần chấm điểm." });

            if (!m.Team1RegistrationId.HasValue || !m.Team2RegistrationId.HasValue)
                return BadRequest(new { message = "Trận chưa xác định đủ 2 đội nên chưa thể chấm điểm." });

            if (!m.StartAt.HasValue)
                return BadRequest(new { message = "Trận chưa có thời gian thi đấu." });

            if (!CanScoreMatchToday(m.StartAt))
                return BadRequest(new { message = "Trọng tài chỉ được chấm điểm cho trận diễn ra hôm nay." });

            RelayScoreDto? relayScores = null;
            try
            {
                if (_relayScoring != null)
                    relayScores = await _relayScoring.ApplyAsync(m, dto.Relay, dto.IsCompleted, HttpContext.RequestAborted);
                if (relayScores != null)
                {
                    dto.ScoreTeam1 = m.ScoreTeam1;
                    dto.ScoreTeam2 = m.ScoreTeam2;
                }
            }
            catch (RelayRuleException ex)
            {
                return ex.Code == "VERSION_CONFLICT" ? Conflict(new { code = ex.Code, message = ex.Message })
                    : BadRequest(new { code = ex.Code, message = ex.Message });
            }

            if (dto.ScoreTeam1 < 0 || dto.ScoreTeam2 < 0)
                return BadRequest(new { message = "Điểm phải >= 0." });

            if (dto.IsCompleted && dto.ScoreTeam1 == dto.ScoreTeam2)
                return BadRequest(new { message = "Không hỗ trợ kết quả hòa khi kết thúc trận." });

            long? winnerRegistrationId = null;
            if (dto.IsCompleted)
            {
                winnerRegistrationId = dto.ScoreTeam1 > dto.ScoreTeam2
                    ? m.Team1RegistrationId
                    : m.Team2RegistrationId;
            }

            var winnerTeam = winnerRegistrationId == null
                ? null
                : winnerRegistrationId == m.Team1RegistrationId
                    ? "1"
                    : winnerRegistrationId == m.Team2RegistrationId
                        ? "2"
                        : null;

            var wasCompleted = m.IsCompleted;
            var previousWinnerRegistrationId = m.WinnerRegistrationId;

            try
            {
                if (_relayLineupSnapshots != null)
                    await _relayLineupSnapshots.EnsureForMatchAsync(m, HttpContext.RequestAborted);
            }
            catch (RelayRuleException ex)
            {
                await tx.RollbackAsync();
                return BadRequest(new { code = ex.Code, message = ex.Message });
            }

            // update current score
            m.ScoreTeam1 = dto.ScoreTeam1;
            m.ScoreTeam2 = dto.ScoreTeam2;
            m.IsCompleted = dto.IsCompleted;
            m.CompletionReason = dto.IsCompleted ? MatchCompletionReasons.Normal : null;
            m.WinnerRegistrationId = winnerRegistrationId;
            m.UpdatedAt = DateTime.UtcNow;

            // insert history
            var history = new TournamentMatchScoreHistory
            {
                MatchId = m.MatchId,
                RefereeUserId = currentUserId.Value,
                ScoreTeam1 = dto.ScoreTeam1,
                ScoreTeam2 = dto.ScoreTeam2,
                IsCompleted = dto.IsCompleted,
                WinnerRegistrationId = winnerRegistrationId,
                Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
                RelayPartNumber = relayScores == null ? null : dto.Relay?.ChangedPart,
                RelayPartsJson = relayScores == null ? null : JsonSerializer.Serialize(relayScores.Parts, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                CreatedAt = DateTime.UtcNow
            };

            _db.TournamentMatchScoreHistories.Add(history);

            await _db.SaveChangesAsync();
            if (wasCompleted && !m.IsCompleted)
                await _bracketPropagationService.PropagateFromMatchAsync(m.MatchId, HttpContext.RequestAborted);
            await tx.CommitAsync();
            using var postCommit = CancellationCleanup.CreatePostCommitTokenSource();

            try
            {
                await _publicRealtimeHub.BroadcastMatchScoreUpdatedAsync(m.TournamentId, m.MatchId, new
                {
                    m.MatchStatus,
                    m.StateVersion,
                    m.TournamentId,
                    m.MatchId,
                    m.TournamentRoundGroupId,
                    m.ScoreTeam1,
                    m.ScoreTeam2,
                    m.IsCompleted,
                    m.WinnerRegistrationId,
                    WinnerTeam = winnerTeam,
                    WinnerSide = winnerTeam,
                    RelayScores = relayScores,
                    m.VideoUrl,
                    m.CourtText,
                    m.AddressText,
                    m.UpdatedAt
                });
            }
            catch
            {
                // Realtime broadcast must not break the scoring transaction that already committed.
            }

            if (m.IsCompleted || wasCompleted)
            {
                var bracketPropagationSucceeded = false;
                try
                {
                    if (m.IsCompleted && (!wasCompleted || previousWinnerRegistrationId != m.WinnerRegistrationId))
                        await _bracketPropagationService.PropagateFromMatchAsync(m.MatchId, postCommit.Token);

                    if (m.IsCompleted)
                        await _bracketPropagationService.PropagateFromGroupAsync(m.TournamentRoundGroupId, postCommit.Token);
                    bracketPropagationSucceeded = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Bracket propagation failed after referee saved match {MatchId}. Run bracket reconcile to retry.",
                        m.MatchId);
                }

                if (bracketPropagationSucceeded)
                {
                    try
                    {
                        await _publicRealtimeHub.BroadcastBracketUpdatedAsync(m.TournamentId, new
                        {
                            m.TournamentId,
                            SourceMatchId = m.MatchId,
                            m.TournamentRoundGroupId,
                            Reason = !m.IsCompleted ? "MATCH_REOPENED" : !wasCompleted
                                ? "MATCH_COMPLETED"
                                : previousWinnerRegistrationId != m.WinnerRegistrationId
                                    ? "WINNER_CHANGED"
                                    : "SCORE_UPDATED",
                            UpdatedAt = DateTime.UtcNow
                        });
                    }
                    catch
                    {
                        // Realtime broadcast must not break the scoring response after propagation succeeded.
                    }
                }
            }

            if (m.IsCompleted && (!wasCompleted || previousWinnerRegistrationId != m.WinnerRegistrationId))
            {
                try
                {
                    await _tournamentNotificationService.NotifyMatchWinnerAsync(m.MatchId, postCommit.Token);
                }
                catch
                {
                    // User notification must not break the scoring response after the match was saved.
                }
            }

            var savedHistory = await _db.TournamentMatchScoreHistories.AsNoTracking()
                .Where(x => x.ScoreHistoryId == history.ScoreHistoryId)
                .Select(x => new
                {
                    x.ScoreHistoryId,
                    x.MatchId,
                    x.RefereeUserId,
                    RefereeName = x.ActorName ?? (x.RefereeUser != null ? x.RefereeUser.FullName : "Quản trị viên"),
                    x.ScoreTeam1,
                    x.ScoreTeam2,
                    x.IsCompleted,
                    x.WinnerRegistrationId,
                    WinnerTeam = x.WinnerRegistrationId == null
                        ? null
                        : (x.WinnerRegistrationId == x.Match.Team1RegistrationId ? "1" : "2"),
                    x.Note,
                    x.RelayPartNumber,
                    x.RelayPartsJson,
                    x.CreatedAt
                })
                .FirstAsync();

            return Ok(new
            {
                MatchId = m.MatchId,
                m.ScoreTeam1,
                m.ScoreTeam2,
                m.IsCompleted,
                m.MatchStatus,
                m.StateVersion,
                m.WinnerRegistrationId,
                WinnerTeam = winnerTeam,
                RelayScores = relayScores,
                History = savedHistory
            });
        }

        private static string BuildTeamText(string gameType, TournamentRegistration r, bool canViewVirtualTeams)
        {
            if (r.IsVirtualTeam && !canViewVirtualTeams)
                return "Chờ cập nhật";

            gameType = (gameType ?? "DOUBLE").Trim().ToUpperInvariant();

            if (gameType == "SINGLE")
                return (r.Player1Name ?? "").Trim();

            var p1 = (r.Player1Name ?? "").Trim();
            var p2 = (r.Player2Name ?? "").Trim();

            if (string.IsNullOrWhiteSpace(p2))
                return p1;

            return $"{p1} & {p2}";
        }
    }

}
namespace HanakaServer.Models.Dto
{
    public class RefereeSetScoreDto
    {
        public int ScoreTeam1 { get; set; }
        public int ScoreTeam2 { get; set; }
        public bool IsCompleted { get; set; } = true;
        public string? Note { get; set; }
        public RelayScoreUpdate? Relay { get; set; }
    }
}
