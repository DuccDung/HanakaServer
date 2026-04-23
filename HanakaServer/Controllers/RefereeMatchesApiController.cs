using System.Security.Claims;
using HanakaServer.Data;
using HanakaServer.Models;
using HanakaServer.Models.Dto;
using HanakaServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;

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

        public RefereeMatchesApiController(
            PickleballDbContext db,
            PublicRealtimeHub publicRealtimeHub,
            TournamentUserNotificationService tournamentNotificationService)
        {
            _db = db;
            _publicRealtimeHub = publicRealtimeHub;
            _tournamentNotificationService = tournamentNotificationService;
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

            var matches = await (
     from m in _db.TournamentGroupMatches.AsNoTracking()
     where m.RefereeUserId == currentUserId.Value
     join g in _db.TournamentRoundGroups.AsNoTracking()
         on m.TournamentRoundGroupId equals g.TournamentRoundGroupId
     join rm in _db.TournamentRoundMaps.AsNoTracking()
         on g.TournamentRoundMapId equals rm.TournamentRoundMapId
     join t in _db.Tournaments.AsNoTracking()
         on m.TournamentId equals t.TournamentId
     join r1 in _db.TournamentRegistrations.AsNoTracking()
         on m.Team1RegistrationId equals r1.RegistrationId
     join r2 in _db.TournamentRegistrations.AsNoTracking()
         on m.Team2RegistrationId equals r2.RegistrationId
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
             : (m.WinnerRegistrationId == m.Team1RegistrationId ? "1" : "2")
     }
 ).ToListAsync();

            var matchIds = matches.Select(x => x.MatchId).ToList();

            var histories = await _db.TournamentMatchScoreHistories.AsNoTracking()
                .Where(x => matchIds.Contains(x.MatchId))
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new
                {
                    x.ScoreHistoryId,
                    x.MatchId,
                    x.RefereeUserId,
                    RefereeName = x.RefereeUser.FullName,
                    x.ScoreTeam1,
                    x.ScoreTeam2,
                    x.IsCompleted,
                    x.WinnerRegistrationId,
                    WinnerTeam = x.WinnerRegistrationId == null
                        ? null
                        : (x.WinnerRegistrationId == x.Match.Team1RegistrationId ? "1" : "2"),
                    x.Note,
                    x.CreatedAt
                })
                .ToListAsync();

            var historyMap = histories
                .GroupBy(x => x.MatchId)
                .ToDictionary(g => g.Key, g => g.ToList());

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
                CanEditScore = CanScoreMatchToday(m.StartAt),
                ScoreHistories = historyMap.ContainsKey(m.MatchId)
                                ? historyMap[m.MatchId].Cast<object>().ToList()
                                : new List<object>()
            });

            return Ok(new { items });
        }

        [HttpPut("{matchId:long}/score")]
        public async Task<IActionResult> SetScore(long matchId, [FromBody] RefereeSetScoreDto dto)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
                return Unauthorized(new { message = "Không xác định được user hiện tại." });

            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var m = await _db.TournamentGroupMatches
                .FirstOrDefaultAsync(x => x.MatchId == matchId && x.RefereeUserId == currentUserId.Value);

            if (m == null)
                return NotFound(new { message = "Không tìm thấy trận đấu hoặc bạn không phải trọng tài của trận này." });

            if (!m.StartAt.HasValue)
                return BadRequest(new { message = "Trận chưa có thời gian thi đấu." });

            if (!CanScoreMatchToday(m.StartAt))
                return BadRequest(new { message = "Trọng tài chỉ được chấm điểm cho trận diễn ra hôm nay." });

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

            // update current score
            m.ScoreTeam1 = dto.ScoreTeam1;
            m.ScoreTeam2 = dto.ScoreTeam2;
            m.IsCompleted = dto.IsCompleted;
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
                CreatedAt = DateTime.UtcNow
            };

            _db.TournamentMatchScoreHistories.Add(history);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            try
            {
                await _publicRealtimeHub.BroadcastMatchScoreUpdatedAsync(m.TournamentId, m.MatchId, new
                {
                    m.TournamentId,
                    m.MatchId,
                    m.TournamentRoundGroupId,
                    m.ScoreTeam1,
                    m.ScoreTeam2,
                    m.IsCompleted,
                    m.WinnerRegistrationId,
                    WinnerTeam = winnerTeam,
                    WinnerSide = winnerTeam,
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

            if (m.IsCompleted && (!wasCompleted || previousWinnerRegistrationId != m.WinnerRegistrationId))
            {
                try
                {
                    await _tournamentNotificationService.NotifyMatchWinnerAsync(m.MatchId);
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
                    RefereeName = x.RefereeUser.FullName,
                    x.ScoreTeam1,
                    x.ScoreTeam2,
                    x.IsCompleted,
                    x.WinnerRegistrationId,
                    WinnerTeam = x.WinnerRegistrationId == null
                        ? null
                        : (x.WinnerRegistrationId == x.Match.Team1RegistrationId ? "1" : "2"),
                    x.Note,
                    x.CreatedAt
                })
                .FirstAsync();

            return Ok(new
            {
                MatchId = m.MatchId,
                m.ScoreTeam1,
                m.ScoreTeam2,
                m.IsCompleted,
                m.WinnerRegistrationId,
                WinnerTeam = winnerTeam,
                History = savedHistory
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
    }
}
