using HanakaServer.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/videos")]
    public partial class TournamentVideosController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly IConfiguration _config;

        public TournamentVideosController(PickleballDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        private string? ToAbsoluteUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            return _config["PublicBaseUrl"] + url;
        }

        [HttpGet("videos")]
        public async Task<IActionResult> GetMatchVideos(
            [FromQuery] string tab = "all",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] long? tournamentId = null)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 10;
            if (pageSize > 50) pageSize = 50;

            tab = (tab ?? "all").Trim().ToLowerInvariant();

            var now = DateTime.Now;
            var todayStart = now.Date;
            var tomorrowStart = todayStart.AddDays(1);
            var weekAgo = now.AddDays(-7);

            var baseQuery = _db.TournamentGroupMatches
                .AsNoTracking()
                .Where(x => !string.IsNullOrWhiteSpace(x.VideoUrl));

            if (tournamentId.HasValue && tournamentId.Value > 0)
            {
                baseQuery = baseQuery.Where(x => x.TournamentId == tournamentId.Value);
            }

            if (tab == "suggested")
            {
                baseQuery = baseQuery.Where(x => (x.StartAt ?? x.CreatedAt) >= weekAgo);
            }
            else if (tab == "live")
            {
                baseQuery = baseQuery.Where(x =>
                    (x.StartAt ?? x.CreatedAt) >= todayStart &&
                    (x.StartAt ?? x.CreatedAt) < tomorrowStart);
            }

            var total = await baseQuery.CountAsync();

            var rawMatches = await baseQuery
                .OrderByDescending(x => x.StartAt ?? x.CreatedAt)
                .ThenByDescending(x => x.MatchId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.MatchId,
                    x.TournamentId,
                    x.TournamentRoundGroupId,
                    x.Team1RegistrationId,
                    x.Team2RegistrationId,
                    x.StartAt,
                    x.AddressText,
                    x.CourtText,
                    x.VideoUrl,
                    x.ScoreTeam1,
                    x.ScoreTeam2,
                    x.IsCompleted,
                    x.WinnerRegistrationId,
                    x.CreatedAt,
                    x.UpdatedAt
                })
                .ToListAsync();

            if (!rawMatches.Any())
            {
                return Ok(new PagedMatchVideoResponseDto
                {
                    Tab = tab,
                    Page = page,
                    PageSize = pageSize,
                    Total = total,
                    HasMore = page * pageSize < total,
                    Items = new List<MatchVideoItemDto>()
                });
            }

            var tournamentIds = rawMatches
                .Select(x => x.TournamentId)
                .Distinct()
                .ToList();

            var groupIds = rawMatches
                .Select(x => x.TournamentRoundGroupId)
                .Distinct()
                .ToList();

            var registrationIds = rawMatches
                .SelectMany(x => new long?[]
                {
                    x.Team1RegistrationId,
                    x.Team2RegistrationId,
                    x.WinnerRegistrationId
                })
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();

            var tournaments = await _db.Tournaments
                .AsNoTracking()
                .Where(x => tournamentIds.Contains(x.TournamentId))
                .Select(x => new
                {
                    x.TournamentId,
                    x.Title,
                    x.GameType,
                    x.BannerUrl,
                    x.Status
                })
                .ToListAsync();

            var groups = await _db.TournamentRoundGroups
                .AsNoTracking()
                .Where(x => groupIds.Contains(x.TournamentRoundGroupId))
                .Select(x => new
                {
                    x.TournamentRoundGroupId,
                    x.TournamentRoundMapId,
                    x.GroupName,
                    x.SortOrder
                })
                .ToListAsync();

            var roundMapIds = groups
                .Select(x => x.TournamentRoundMapId)
                .Distinct()
                .ToList();

            var roundMaps = await _db.TournamentRoundMaps
                .AsNoTracking()
                .Where(x => roundMapIds.Contains(x.TournamentRoundMapId))
                .Select(x => new
                {
                    x.TournamentRoundMapId,
                    x.RoundKey,
                    x.RoundLabel,
                    x.SortOrder
                })
                .ToListAsync();

            var regs = await _db.TournamentRegistrations
                .AsNoTracking()
                .Where(x => registrationIds.Contains(x.RegistrationId))
                .Select(x => new
                {
                    x.RegistrationId,
                    x.RegCode,
                    x.RegIndex,
                    x.Player1Name,
                    x.Player2Name,
                    Player1Avatar = !string.IsNullOrWhiteSpace(x.Player1Avatar)
                        ? x.Player1Avatar
                        : (x.Player1User != null ? x.Player1User.AvatarUrl : null),
                    Player2Avatar = !string.IsNullOrWhiteSpace(x.Player2Avatar)
                        ? x.Player2Avatar
                        : (x.Player2User != null ? x.Player2User.AvatarUrl : null)
                })
                .ToListAsync();

            var tournamentMap = tournaments.ToDictionary(x => x.TournamentId, x => x);
            var groupMap = groups.ToDictionary(x => x.TournamentRoundGroupId, x => x);
            var roundMapDict = roundMaps.ToDictionary(x => x.TournamentRoundMapId, x => x);
            var regMap = regs.ToDictionary(x => x.RegistrationId, x => x);

            var items = rawMatches.Select(m =>
            {
                tournamentMap.TryGetValue(m.TournamentId, out var tournament);
                groupMap.TryGetValue(m.TournamentRoundGroupId, out var group);
                roundMapDict.TryGetValue(group?.TournamentRoundMapId ?? 0, out var round);

                regMap.TryGetValue(m.Team1RegistrationId, out var team1Reg);
                regMap.TryGetValue(m.Team2RegistrationId, out var team2Reg);

                var team1Name = BuildTeamDisplayName(
                    tournament?.GameType,
                    team1Reg?.Player1Name,
                    team1Reg?.Player2Name
                );

                var team2Name = BuildTeamDisplayName(
                    tournament?.GameType,
                    team2Reg?.Player1Name,
                    team2Reg?.Player2Name
                );

                string? winnerSide = null;
                if (m.WinnerRegistrationId.HasValue)
                {
                    if (m.WinnerRegistrationId.Value == m.Team1RegistrationId) winnerSide = "1";
                    else if (m.WinnerRegistrationId.Value == m.Team2RegistrationId) winnerSide = "2";
                }

                return new MatchVideoItemDto
                {
                    MatchId = m.MatchId,

                    TournamentId = m.TournamentId,
                    TournamentTitle = tournament?.Title,
                    TournamentBannerUrl = ToAbsoluteUrl(tournament?.BannerUrl),
                    TournamentStatus = tournament?.Status,

                    RoundKey = round?.RoundKey,
                    RoundLabel = round?.RoundLabel,

                    GroupId = m.TournamentRoundGroupId,
                    GroupName = group?.GroupName,

                    Team1RegistrationId = m.Team1RegistrationId,
                    Team1Name = team1Name,
                    Team1Player1Name = team1Reg?.Player1Name,
                    Team1Player1Avatar = ToAbsoluteUrl(team1Reg?.Player1Avatar),
                    Team1Player2Name = team1Reg?.Player2Name,
                    Team1Player2Avatar = ToAbsoluteUrl(team1Reg?.Player2Avatar),

                    Team2RegistrationId = m.Team2RegistrationId,
                    Team2Name = team2Name,
                    Team2Player1Name = team2Reg?.Player1Name,
                    Team2Player1Avatar = ToAbsoluteUrl(team2Reg?.Player1Avatar),
                    Team2Player2Name = team2Reg?.Player2Name,
                    Team2Player2Avatar = ToAbsoluteUrl(team2Reg?.Player2Avatar),

                    StartAt = m.StartAt,
                    AddressText = m.AddressText,
                    CourtText = m.CourtText,

                    ScoreTeam1 = m.ScoreTeam1,
                    ScoreTeam2 = m.ScoreTeam2,
                    IsCompleted = m.IsCompleted,

                    WinnerRegistrationId = m.WinnerRegistrationId,
                    WinnerSide = winnerSide,

                    VideoUrl = m.VideoUrl,

                    CreatedAt = m.CreatedAt,
                    UpdatedAt = m.UpdatedAt
                };
            }).ToList();

            return Ok(new PagedMatchVideoResponseDto
            {
                Tab = tab,
                Page = page,
                PageSize = pageSize,
                Total = total,
                HasMore = page * pageSize < total,
                Items = items
            });
        }

        private static string BuildTeamDisplayName(string? gameType, string? player1Name, string? player2Name)
        {
            var gt = (gameType ?? "DOUBLE").Trim().ToUpperInvariant();
            var p1 = (player1Name ?? "").Trim();
            var p2 = (player2Name ?? "").Trim();

            if (gt == "SINGLE")
                return p1;

            if (string.IsNullOrWhiteSpace(p2))
                return p1;

            return $"{p1} & {p2}";
        }
        [HttpGet("users/{userId:long}/videos")]
        public async Task<IActionResult> GetUserMatchVideos(
    long userId,
    [FromQuery] string tab = "all",
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 10)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 10;
            if (pageSize > 50) pageSize = 50;

            tab = (tab ?? "all").Trim().ToLowerInvariant();

            var now = DateTime.Now;
            var todayStart = now.Date;
            var tomorrowStart = todayStart.AddDays(1);
            var weekAgo = now.AddDays(-7);

            var registrationIds = await _db.TournamentRegistrations
                .AsNoTracking()
                .Where(x => x.Player1UserId == userId || x.Player2UserId == userId)
                .Select(x => x.RegistrationId)
                .Distinct()
                .ToListAsync();

            if (!registrationIds.Any())
            {
                return Ok(new PagedMatchVideoResponseDto
                {
                    Tab = tab,
                    Page = page,
                    PageSize = pageSize,
                    Total = 0,
                    HasMore = false,
                    Items = new List<MatchVideoItemDto>()
                });
            }

            var baseQuery = _db.TournamentGroupMatches
                .AsNoTracking()
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.VideoUrl) &&
                    (registrationIds.Contains(x.Team1RegistrationId) ||
                     registrationIds.Contains(x.Team2RegistrationId)));

            if (tab == "suggested")
            {
                baseQuery = baseQuery.Where(x => (x.StartAt ?? x.CreatedAt) >= weekAgo);
            }
            else if (tab == "live")
            {
                baseQuery = baseQuery.Where(x =>
                    (x.StartAt ?? x.CreatedAt) >= todayStart &&
                    (x.StartAt ?? x.CreatedAt) < tomorrowStart);
            }

            var total = await baseQuery.CountAsync();

            var rawMatches = await baseQuery
                .OrderByDescending(x => x.StartAt ?? x.CreatedAt)
                .ThenByDescending(x => x.MatchId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.MatchId,
                    x.TournamentId,
                    x.TournamentRoundGroupId,
                    x.Team1RegistrationId,
                    x.Team2RegistrationId,
                    x.StartAt,
                    x.AddressText,
                    x.CourtText,
                    x.VideoUrl,
                    x.ScoreTeam1,
                    x.ScoreTeam2,
                    x.IsCompleted,
                    x.WinnerRegistrationId,
                    x.CreatedAt,
                    x.UpdatedAt
                })
                .ToListAsync();

            if (!rawMatches.Any())
            {
                return Ok(new PagedMatchVideoResponseDto
                {
                    Tab = tab,
                    Page = page,
                    PageSize = pageSize,
                    Total = total,
                    HasMore = page * pageSize < total,
                    Items = new List<MatchVideoItemDto>()
                });
            }

            var tournamentIds = rawMatches
                .Select(x => x.TournamentId)
                .Distinct()
                .ToList();

            var groupIds = rawMatches
                .Select(x => x.TournamentRoundGroupId)
                .Distinct()
                .ToList();

            var allRegistrationIds = rawMatches
                .SelectMany(x => new long?[]
                {
            x.Team1RegistrationId,
            x.Team2RegistrationId,
            x.WinnerRegistrationId
                })
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();

            var tournaments = await _db.Tournaments
                .AsNoTracking()
                .Where(x => tournamentIds.Contains(x.TournamentId))
                .Select(x => new
                {
                    x.TournamentId,
                    x.Title,
                    x.GameType,
                    x.BannerUrl,
                    x.Status
                })
                .ToListAsync();

            var groups = await _db.TournamentRoundGroups
                .AsNoTracking()
                .Where(x => groupIds.Contains(x.TournamentRoundGroupId))
                .Select(x => new
                {
                    x.TournamentRoundGroupId,
                    x.TournamentRoundMapId,
                    x.GroupName,
                    x.SortOrder
                })
                .ToListAsync();

            var roundMapIds = groups
                .Select(x => x.TournamentRoundMapId)
                .Distinct()
                .ToList();

            var roundMaps = await _db.TournamentRoundMaps
                .AsNoTracking()
                .Where(x => roundMapIds.Contains(x.TournamentRoundMapId))
                .Select(x => new
                {
                    x.TournamentRoundMapId,
                    x.RoundKey,
                    x.RoundLabel,
                    x.SortOrder
                })
                .ToListAsync();

            var regs = await _db.TournamentRegistrations
                .AsNoTracking()
                .Where(x => allRegistrationIds.Contains(x.RegistrationId))
                .Select(x => new
                {
                    x.RegistrationId,
                    x.RegCode,
                    x.RegIndex,
                    x.Player1Name,
                    x.Player2Name,
                    x.Player1UserId,
                    x.Player2UserId,
                    Player1Avatar = !string.IsNullOrWhiteSpace(x.Player1Avatar)
                        ? x.Player1Avatar
                        : (x.Player1User != null ? x.Player1User.AvatarUrl : null),
                    Player2Avatar = !string.IsNullOrWhiteSpace(x.Player2Avatar)
                        ? x.Player2Avatar
                        : (x.Player2User != null ? x.Player2User.AvatarUrl : null)
                })
                .ToListAsync();

            var tournamentMap = tournaments.ToDictionary(x => x.TournamentId, x => x);
            var groupMap = groups.ToDictionary(x => x.TournamentRoundGroupId, x => x);
            var roundMapDict = roundMaps.ToDictionary(x => x.TournamentRoundMapId, x => x);
            var regMap = regs.ToDictionary(x => x.RegistrationId, x => x);

            var items = rawMatches.Select(m =>
            {
                tournamentMap.TryGetValue(m.TournamentId, out var tournament);
                groupMap.TryGetValue(m.TournamentRoundGroupId, out var group);
                roundMapDict.TryGetValue(group?.TournamentRoundMapId ?? 0, out var round);

                regMap.TryGetValue(m.Team1RegistrationId, out var team1Reg);
                regMap.TryGetValue(m.Team2RegistrationId, out var team2Reg);

                var team1Name = BuildTeamDisplayName(
                    tournament?.GameType,
                    team1Reg?.Player1Name,
                    team1Reg?.Player2Name
                );

                var team2Name = BuildTeamDisplayName(
                    tournament?.GameType,
                    team2Reg?.Player1Name,
                    team2Reg?.Player2Name
                );

                string? winnerSide = null;
                if (m.WinnerRegistrationId.HasValue)
                {
                    if (m.WinnerRegistrationId.Value == m.Team1RegistrationId) winnerSide = "1";
                    else if (m.WinnerRegistrationId.Value == m.Team2RegistrationId) winnerSide = "2";
                }

                var isUserInTeam1 = team1Reg != null &&
                    (team1Reg.Player1UserId == userId || team1Reg.Player2UserId == userId);

                var isUserInTeam2 = team2Reg != null &&
                    (team2Reg.Player1UserId == userId || team2Reg.Player2UserId == userId);

                return new MatchVideoItemDto
                {
                    MatchId = m.MatchId,

                    TournamentId = m.TournamentId,
                    TournamentTitle = tournament?.Title,
                    TournamentBannerUrl = ToAbsoluteUrl(tournament?.BannerUrl),
                    TournamentStatus = tournament?.Status,

                    RoundKey = round?.RoundKey,
                    RoundLabel = round?.RoundLabel,

                    GroupId = m.TournamentRoundGroupId,
                    GroupName = group?.GroupName,

                    Team1RegistrationId = m.Team1RegistrationId,
                    Team1Name = team1Name,
                    Team1Player1Name = team1Reg?.Player1Name,
                    Team1Player1Avatar = ToAbsoluteUrl(team1Reg?.Player1Avatar),
                    Team1Player2Name = team1Reg?.Player2Name,
                    Team1Player2Avatar = ToAbsoluteUrl(team1Reg?.Player2Avatar),

                    Team2RegistrationId = m.Team2RegistrationId,
                    Team2Name = team2Name,
                    Team2Player1Name = team2Reg?.Player1Name,
                    Team2Player1Avatar = ToAbsoluteUrl(team2Reg?.Player1Avatar),
                    Team2Player2Name = team2Reg?.Player2Name,
                    Team2Player2Avatar = ToAbsoluteUrl(team2Reg?.Player2Avatar),

                    StartAt = m.StartAt,
                    AddressText = m.AddressText,
                    CourtText = m.CourtText,

                    ScoreTeam1 = m.ScoreTeam1,
                    ScoreTeam2 = m.ScoreTeam2,
                    IsCompleted = m.IsCompleted,

                    WinnerRegistrationId = m.WinnerRegistrationId,
                    WinnerSide = winnerSide,

                    VideoUrl = m.VideoUrl,

                    CreatedAt = m.CreatedAt,
                    UpdatedAt = m.UpdatedAt,

                    IsUserInTeam1 = isUserInTeam1,
                    IsUserInTeam2 = isUserInTeam2
                };
            }).ToList();

            return Ok(new PagedMatchVideoResponseDto
            {
                Tab = tab,
                Page = page,
                PageSize = pageSize,
                Total = total,
                HasMore = page * pageSize < total,
                Items = items
            });
        }
    }

    public class PagedMatchVideoResponseDto
    {
        public string Tab { get; set; } = "all";
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int Total { get; set; }
        public bool HasMore { get; set; }
        public List<MatchVideoItemDto> Items { get; set; } = new();
    }

    public class MatchVideoItemDto
    {
        public long MatchId { get; set; }

        public long TournamentId { get; set; }
        public string? TournamentTitle { get; set; }
        public string? TournamentBannerUrl { get; set; }
        public string? TournamentStatus { get; set; }

        public string? RoundKey { get; set; }
        public string? RoundLabel { get; set; }

        public long GroupId { get; set; }
        public string? GroupName { get; set; }

        public long Team1RegistrationId { get; set; }
        public string? Team1Name { get; set; }
        public string? Team1Player1Name { get; set; }
        public string? Team1Player1Avatar { get; set; }
        public string? Team1Player2Name { get; set; }
        public string? Team1Player2Avatar { get; set; }

        public long Team2RegistrationId { get; set; }
        public string? Team2Name { get; set; }
        public string? Team2Player1Name { get; set; }
        public string? Team2Player1Avatar { get; set; }
        public string? Team2Player2Name { get; set; }
        public string? Team2Player2Avatar { get; set; }

        public DateTime? StartAt { get; set; }
        public string? AddressText { get; set; }
        public string? CourtText { get; set; }

        public int ScoreTeam1 { get; set; }
        public int ScoreTeam2 { get; set; }
        public bool IsCompleted { get; set; }

        public long? WinnerRegistrationId { get; set; }
        public string? WinnerSide { get; set; }

        public string? VideoUrl { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public bool IsUserInTeam1 { get; set; }
        public bool IsUserInTeam2 { get; set; }
    }
}