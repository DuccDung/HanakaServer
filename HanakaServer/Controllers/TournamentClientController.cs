using HanakaServer.Data;
using HanakaServer.Dtos;
using HanakaServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/tournaments")]
    public class TournamentClientController : ControllerBase
    {
        private readonly PickleballDbContext _db;

        public TournamentClientController(PickleballDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// GET: /api/tournaments/{tournamentId}/rounds-with-matches
        /// Lấy thông tin giải đấu + toàn bộ rounds -> groups -> matches
        /// </summary>
        [HttpGet("{tournamentId:long}/rounds-with-matches")]
        public async Task<IActionResult> GetRoundsWithMatches(long tournamentId)
        {
            var tournament = await _db.Tournaments
                .AsNoTracking()
                .Where(x => x.TournamentId == tournamentId)
                .Select(x => new TournamentClientDto
                {
                    TournamentId = x.TournamentId,
                    Title = x.Title,
                    BannerUrl = x.BannerUrl,
                    Status = x.Status,
                    StatusText = x.StatusText,
                    StateText = x.StateText,
                    StartTime = x.StartTime,
                    StartTimeRaw = x.StartTimeRaw,
                    RegisterDeadline = x.RegisterDeadline,
                    RegisterDeadlineRaw = x.RegisterDeadlineRaw,
                    FormatText = x.FormatText,
                    PlayoffType = x.PlayoffType,
                    GameType = x.GameType,
                    SingleLimit = x.SingleLimit,
                    DoubleLimit = x.DoubleLimit,
                    LocationText = x.LocationText,
                    AreaText = x.AreaText,
                    ExpectedTeams = x.ExpectedTeams,
                    MatchesCount = x.MatchesCount,
                    Organizer = x.Organizer,
                    CreatorName = x.CreatorName,
                    RegisteredCount = x.RegisteredCount,
                    PairedCount = x.PairedCount,
                    Content = x.Content,
                    CreatedAt = x.CreatedAt
                })
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found." });

            var roundMaps = await _db.TournamentRoundMaps
                .AsNoTracking()
                .Where(x => x.TournamentId == tournamentId)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.RoundKey)
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

            var roundMapIds = roundMaps.Select(x => x.TournamentRoundMapId).ToList();

            var groups = await _db.TournamentRoundGroups
                .AsNoTracking()
                .Where(x => roundMapIds.Contains(x.TournamentRoundMapId))
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.GroupName)
                .Select(x => new
                {
                    x.TournamentRoundGroupId,
                    x.TournamentRoundMapId,
                    x.GroupName,
                    x.SortOrder,
                    x.CreatedAt
                })
                .ToListAsync();

            var groupIds = groups.Select(x => x.TournamentRoundGroupId).ToList();

            var matchesRaw = await _db.TournamentGroupMatches
                .AsNoTracking()
                .Where(x => x.TournamentId == tournamentId && groupIds.Contains(x.TournamentRoundGroupId))
                .OrderBy(x => x.StartAt ?? DateTime.MaxValue)
                .ThenBy(x => x.MatchId)
                .Select(x => new
                {
                    x.MatchId,
                    x.TournamentRoundGroupId,
                    x.TournamentId,
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

            var registrationIds = matchesRaw
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

            var regs = await _db.TournamentRegistrations
                .AsNoTracking()
                .Where(x => registrationIds.Contains(x.RegistrationId))
                .Select(x => new TournamentRegistrationLiteDto
                {
                    RegistrationId = x.RegistrationId,
                    TournamentId = x.TournamentId,
                    RegCode = x.RegCode,
                    RegIndex = x.RegIndex,
                    Player1Name = x.Player1Name,
                    Player1Avatar = x.Player1Avatar,
                    Player1Level = x.Player1Level,
                    Player1Verified = x.Player1Verified,
                    Player1UserId = x.Player1UserId,
                    Player2Name = x.Player2Name,
                    Player2Avatar = x.Player2Avatar,
                    Player2Level = x.Player2Level,
                    Player2Verified = x.Player2Verified,
                    Player2UserId = x.Player2UserId,
                    Points = x.Points,
                    Paid = x.Paid,
                    WaitingPair = x.WaitingPair,
                    Success = x.Success,
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync();

            var regMap = regs.ToDictionary(x => x.RegistrationId, x => x);

            var groupDtos = groups
                .Select(g => new TournamentRoundGroupClientDto
                {
                    TournamentRoundGroupId = g.TournamentRoundGroupId,
                    TournamentRoundMapId = g.TournamentRoundMapId,
                    GroupName = g.GroupName,
                    SortOrder = g.SortOrder,
                    CreatedAt = g.CreatedAt,
                    Matches = matchesRaw
                        .Where(m => m.TournamentRoundGroupId == g.TournamentRoundGroupId)
                        .Select(m =>
                        {
                            regMap.TryGetValue(m.Team1RegistrationId, out var team1Reg);
                            regMap.TryGetValue(m.Team2RegistrationId, out var team2Reg);

                            TournamentRegistrationLiteDto? winnerReg = null;
                            if (m.WinnerRegistrationId.HasValue)
                                regMap.TryGetValue(m.WinnerRegistrationId.Value, out winnerReg);

                            return new TournamentMatchClientDto
                            {
                                MatchId = m.MatchId,
                                TournamentRoundGroupId = m.TournamentRoundGroupId,
                                TournamentId = m.TournamentId,

                                Team1RegistrationId = m.Team1RegistrationId,
                                Team1 = team1Reg == null ? null : BuildTeamDto(tournament.GameType, team1Reg),

                                Team2RegistrationId = m.Team2RegistrationId,
                                Team2 = team2Reg == null ? null : BuildTeamDto(tournament.GameType, team2Reg),

                                StartAt = m.StartAt,
                                AddressText = m.AddressText,
                                CourtText = m.CourtText,
                                VideoUrl = m.VideoUrl,

                                ScoreTeam1 = m.ScoreTeam1,
                                ScoreTeam2 = m.ScoreTeam2,
                                IsCompleted = m.IsCompleted,
                                WinnerRegistrationId = m.WinnerRegistrationId,
                                WinnerTeam = GetWinnerTeam(m.WinnerRegistrationId, m.Team1RegistrationId, m.Team2RegistrationId),
                                Winner = winnerReg == null ? null : BuildTeamDto(tournament.GameType, winnerReg),

                                CreatedAt = m.CreatedAt,
                                UpdatedAt = m.UpdatedAt
                            };
                        })
                        .ToList()
                })
                .ToList();

            var roundDtos = roundMaps
                .Select(r => new TournamentRoundMapClientDto
                {
                    TournamentRoundMapId = r.TournamentRoundMapId,
                    TournamentId = r.TournamentId,
                    RoundKey = r.RoundKey,
                    RoundLabel = r.RoundLabel,
                    SortOrder = r.SortOrder,
                    CreatedAt = r.CreatedAt,
                    Groups = groupDtos
                        .Where(g => g.TournamentRoundMapId == r.TournamentRoundMapId)
                        .ToList()
                })
                .ToList();

            var response = new TournamentRoundsWithMatchesResponseDto
            {
                Tournament = tournament,
                Rounds = roundDtos
            };

            return Ok(response);
        }

        /// <summary>
        /// GET: /api/tournaments/matches/{matchId}
        /// Lấy chi tiết 1 trận đấu cho client
        /// </summary>
        [HttpGet("matches/{matchId:long}")]
        public async Task<IActionResult> GetMatchDetail(long matchId)
        {
            var match = await _db.TournamentGroupMatches
                .AsNoTracking()
                .Where(x => x.MatchId == matchId)
                .Select(x => new
                {
                    x.MatchId,
                    x.TournamentRoundGroupId,
                    x.TournamentId,
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
                .FirstOrDefaultAsync();

            if (match == null)
                return NotFound(new { message = "Match not found." });

            var tournament = await _db.Tournaments
                .AsNoTracking()
                .Where(x => x.TournamentId == match.TournamentId)
                .Select(x => new TournamentClientDto
                {
                    TournamentId = x.TournamentId,
                    Title = x.Title,
                    BannerUrl = x.BannerUrl,
                    Status = x.Status,
                    StatusText = x.StatusText,
                    StateText = x.StateText,
                    StartTime = x.StartTime,
                    StartTimeRaw = x.StartTimeRaw,
                    RegisterDeadline = x.RegisterDeadline,
                    RegisterDeadlineRaw = x.RegisterDeadlineRaw,
                    FormatText = x.FormatText,
                    PlayoffType = x.PlayoffType,
                    GameType = x.GameType,
                    SingleLimit = x.SingleLimit,
                    DoubleLimit = x.DoubleLimit,
                    LocationText = x.LocationText,
                    AreaText = x.AreaText,
                    ExpectedTeams = x.ExpectedTeams,
                    MatchesCount = x.MatchesCount,
                    Organizer = x.Organizer,
                    CreatorName = x.CreatorName,
                    RegisteredCount = x.RegisteredCount,
                    PairedCount = x.PairedCount,
                    Content = x.Content,
                    CreatedAt = x.CreatedAt
                })
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found." });

            var group = await _db.TournamentRoundGroups
                .AsNoTracking()
                .Where(x => x.TournamentRoundGroupId == match.TournamentRoundGroupId)
                .Select(x => new
                {
                    x.TournamentRoundGroupId,
                    x.TournamentRoundMapId,
                    x.GroupName,
                    x.SortOrder,
                    x.CreatedAt
                })
                .FirstOrDefaultAsync();

            if (group == null)
                return NotFound(new { message = "Group not found." });

            var round = await _db.TournamentRoundMaps
                .AsNoTracking()
                .Where(x => x.TournamentRoundMapId == group.TournamentRoundMapId)
                .Select(x => new
                {
                    x.TournamentRoundMapId,
                    x.TournamentId,
                    x.RoundKey,
                    x.RoundLabel,
                    x.SortOrder,
                    x.CreatedAt
                })
                .FirstOrDefaultAsync();

            if (round == null)
                return NotFound(new { message = "Round not found." });

            var registrationIds = new List<long> { match.Team1RegistrationId, match.Team2RegistrationId };
            if (match.WinnerRegistrationId.HasValue)
                registrationIds.Add(match.WinnerRegistrationId.Value);

            registrationIds = registrationIds.Distinct().ToList();

            var regs = await _db.TournamentRegistrations
                .AsNoTracking()
                .Where(x => registrationIds.Contains(x.RegistrationId))
                .Select(x => new TournamentRegistrationLiteDto
                {
                    RegistrationId = x.RegistrationId,
                    TournamentId = x.TournamentId,
                    RegCode = x.RegCode,
                    RegIndex = x.RegIndex,
                    Player1Name = x.Player1Name,
                    Player1Avatar = x.Player1Avatar,
                    Player1Level = x.Player1Level,
                    Player1Verified = x.Player1Verified,
                    Player1UserId = x.Player1UserId,
                    Player2Name = x.Player2Name,
                    Player2Avatar = x.Player2Avatar,
                    Player2Level = x.Player2Level,
                    Player2Verified = x.Player2Verified,
                    Player2UserId = x.Player2UserId,
                    Points = x.Points,
                    Paid = x.Paid,
                    WaitingPair = x.WaitingPair,
                    Success = x.Success,
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync();

            var regMap = regs.ToDictionary(x => x.RegistrationId, x => x);

            regMap.TryGetValue(match.Team1RegistrationId, out var team1Reg);
            regMap.TryGetValue(match.Team2RegistrationId, out var team2Reg);

            TournamentRegistrationLiteDto? winnerReg = null;
            if (match.WinnerRegistrationId.HasValue)
                regMap.TryGetValue(match.WinnerRegistrationId.Value, out winnerReg);

            var response = new TournamentMatchDetailResponseDto
            {
                Tournament = tournament,
                Round = new TournamentRoundMapClientDto
                {
                    TournamentRoundMapId = round.TournamentRoundMapId,
                    TournamentId = round.TournamentId,
                    RoundKey = round.RoundKey,
                    RoundLabel = round.RoundLabel,
                    SortOrder = round.SortOrder,
                    CreatedAt = round.CreatedAt,
                    Groups = new List<TournamentRoundGroupClientDto>()
                },
                Group = new TournamentRoundGroupClientDto
                {
                    TournamentRoundGroupId = group.TournamentRoundGroupId,
                    TournamentRoundMapId = group.TournamentRoundMapId,
                    GroupName = group.GroupName,
                    SortOrder = group.SortOrder,
                    CreatedAt = group.CreatedAt,
                    Matches = new List<TournamentMatchClientDto>()
                },
                Match = new TournamentMatchClientDto
                {
                    MatchId = match.MatchId,
                    TournamentRoundGroupId = match.TournamentRoundGroupId,
                    TournamentId = match.TournamentId,

                    Team1RegistrationId = match.Team1RegistrationId,
                    Team1 = team1Reg == null ? null : BuildTeamDto(tournament.GameType, team1Reg),

                    Team2RegistrationId = match.Team2RegistrationId,
                    Team2 = team2Reg == null ? null : BuildTeamDto(tournament.GameType, team2Reg),

                    StartAt = match.StartAt,
                    AddressText = match.AddressText,
                    CourtText = match.CourtText,
                    VideoUrl = match.VideoUrl,

                    ScoreTeam1 = match.ScoreTeam1,
                    ScoreTeam2 = match.ScoreTeam2,
                    IsCompleted = match.IsCompleted,
                    WinnerRegistrationId = match.WinnerRegistrationId,
                    WinnerTeam = GetWinnerTeam(match.WinnerRegistrationId, match.Team1RegistrationId, match.Team2RegistrationId),
                    Winner = winnerReg == null ? null : BuildTeamDto(tournament.GameType, winnerReg),

                    CreatedAt = match.CreatedAt,
                    UpdatedAt = match.UpdatedAt
                }
            };

            return Ok(response);
        }

        private static TournamentTeamDto BuildTeamDto(string? gameType, TournamentRegistrationLiteDto reg)
        {
            gameType = (gameType ?? "DOUBLE").Trim().ToUpperInvariant();

            var displayName = gameType == "SINGLE"
                ? (reg.Player1Name ?? "").Trim()
                : BuildDoubleDisplayName(reg.Player1Name, reg.Player2Name);

            return new TournamentTeamDto
            {
                RegistrationId = reg.RegistrationId,
                TournamentId = reg.TournamentId,
                RegCode = reg.RegCode,
                RegIndex = reg.RegIndex,
                DisplayName = displayName,
                IsSingle = gameType == "SINGLE",
                Player1 = new TournamentPlayerDto
                {
                    UserId = reg.Player1UserId,
                    Name = reg.Player1Name,
                    Avatar = reg.Player1Avatar,
                    Level = reg.Player1Level,
                    Verified = reg.Player1Verified
                },
                Player2 = string.IsNullOrWhiteSpace(reg.Player2Name) ? null : new TournamentPlayerDto
                {
                    UserId = reg.Player2UserId,
                    Name = reg.Player2Name,
                    Avatar = reg.Player2Avatar,
                    Level = reg.Player2Level,
                    Verified = reg.Player2Verified
                },
                Points = reg.Points,
                Paid = reg.Paid,
                WaitingPair = reg.WaitingPair,
                Success = reg.Success,
                CreatedAt = reg.CreatedAt
            };
        }

        private static string BuildDoubleDisplayName(string? player1Name, string? player2Name)
        {
            var p1 = (player1Name ?? "").Trim();
            var p2 = (player2Name ?? "").Trim();

            if (string.IsNullOrWhiteSpace(p2))
                return p1;

            return $"{p1} & {p2}";
        }

        private static string? GetWinnerTeam(long? winnerRegistrationId, long team1RegistrationId, long team2RegistrationId)
        {
            if (!winnerRegistrationId.HasValue)
                return null;

            if (winnerRegistrationId.Value == team1RegistrationId)
                return "1";

            if (winnerRegistrationId.Value == team2RegistrationId)
                return "2";

            return null;
        }

        // GET /api/tournaments/{tournamentId}/round-maps/{roundMapId}/standings
        [HttpGet("{tournamentId:long}/round-maps/{roundMapId:long}/standings")]
        public async Task<IActionResult> GetRoundStandings(long tournamentId, long roundMapId)
        {
            var roundMap = await _db.TournamentRoundMaps
                .AsNoTracking()
                .Where(x => x.TournamentRoundMapId == roundMapId && x.TournamentId == tournamentId)
                .Select(x => new
                {
                    x.TournamentRoundMapId,
                    x.TournamentId,
                    x.RoundKey,
                    x.RoundLabel
                })
                .FirstOrDefaultAsync();

            if (roundMap == null)
                return NotFound(new { message = "Round not found." });

            var tournament = await _db.Tournaments
                .AsNoTracking()
                .Where(x => x.TournamentId == tournamentId)
                .Select(x => new
                {
                    x.TournamentId,
                    x.GameType
                })
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found." });

            var groups = await _db.TournamentRoundGroups
                .AsNoTracking()
                .Where(x => x.TournamentRoundMapId == roundMapId)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.GroupName)
                .Select(x => new
                {
                    x.TournamentRoundGroupId,
                    x.GroupName,
                    x.SortOrder
                })
                .ToListAsync();

            var groupIds = groups.Select(x => x.TournamentRoundGroupId).ToList();

            var matches = await _db.TournamentGroupMatches
                .AsNoTracking()
                .Where(x => groupIds.Contains(x.TournamentRoundGroupId) && x.IsCompleted)
                .Select(x => new
                {
                    x.MatchId,
                    x.TournamentRoundGroupId,
                    x.Team1RegistrationId,
                    x.Team2RegistrationId,
                    x.ScoreTeam1,
                    x.ScoreTeam2,
                    x.WinnerRegistrationId
                })
                .ToListAsync();

            var registrationIds = matches
                .SelectMany(x => new[] { x.Team1RegistrationId, x.Team2RegistrationId })
                .Distinct()
                .ToList();

            var registrations = await _db.TournamentRegistrations
                .AsNoTracking()
                .Where(x => registrationIds.Contains(x.RegistrationId))
                .Select(x => new
                {
                    x.RegistrationId,
                    x.RegIndex,
                    x.Player1Name,
                    x.Player2Name
                })
                .ToListAsync();

            var regMap = registrations.ToDictionary(
                x => x.RegistrationId,
                x => new
                {
                    x.RegIndex,
                    TeamName = BuildTeamName(tournament.GameType, x.Player1Name, x.Player2Name)
                });

            var result = new RoundStandingResponseDto
            {
                TournamentId = tournamentId,
                RoundMapId = roundMap.TournamentRoundMapId,
                RoundKey = roundMap.RoundKey,
                RoundLabel = roundMap.RoundLabel,
                Groups = new List<GroupStandingDto>()
            };

            foreach (var g in groups)
            {
                var groupMatches = matches
                    .Where(m => m.TournamentRoundGroupId == g.TournamentRoundGroupId)
                    .ToList();

                var stats = new Dictionary<long, GroupStandingRowDto>();

                void EnsureTeam(long registrationId)
                {
                    if (!stats.ContainsKey(registrationId))
                    {
                        var reg = regMap[registrationId];
                        stats[registrationId] = new GroupStandingRowDto
                        {
                            RegistrationId = registrationId,
                            TeamName = reg.TeamName,
                            Played = 0,
                            Wins = 0,
                            Points = 0,
                            ScoreDiff = 0,
                            ScoreFor = 0,
                            ScoreAgainst = 0,
                            Rank = 0
                        };
                    }
                }

                foreach (var m in groupMatches)
                {
                    EnsureTeam(m.Team1RegistrationId);
                    EnsureTeam(m.Team2RegistrationId);

                    var team1 = stats[m.Team1RegistrationId];
                    var team2 = stats[m.Team2RegistrationId];

                    team1.Played++;
                    team2.Played++;

                    team1.ScoreFor += m.ScoreTeam1;
                    team1.ScoreAgainst += m.ScoreTeam2;
                    team1.ScoreDiff = team1.ScoreFor - team1.ScoreAgainst;

                    team2.ScoreFor += m.ScoreTeam2;
                    team2.ScoreAgainst += m.ScoreTeam1;
                    team2.ScoreDiff = team2.ScoreFor - team2.ScoreAgainst;

                    if (m.ScoreTeam1 > m.ScoreTeam2)
                    {
                        team1.Wins++;
                        team1.Points += 1;
                    }
                    else if (m.ScoreTeam2 > m.ScoreTeam1)
                    {
                        team2.Wins++;
                        team2.Points += 1;
                    }
                }

                var ordered = stats.Values
                    .OrderByDescending(x => x.Points)
                    .ThenByDescending(x => x.Wins)
                    .ThenByDescending(x => x.ScoreDiff)
                    .ThenByDescending(x => x.ScoreFor)
                    .ThenBy(x => regMap[x.RegistrationId].RegIndex)
                    .ToList();

                for (int i = 0; i < ordered.Count; i++)
                    ordered[i].Rank = i + 1;

                result.Groups.Add(new GroupStandingDto
                {
                    GroupId = g.TournamentRoundGroupId,
                    GroupName = g.GroupName,
                    Rows = ordered
                });
            }

            return Ok(result);
        }

        private static string BuildTeamName(string? gameType, string? player1Name, string? player2Name)
        {
            var gt = (gameType ?? "DOUBLE").Trim().ToUpperInvariant();
            var p1 = (player1Name ?? "").Trim();
            var p2 = (player2Name ?? "").Trim();

            if (gt == "SINGLE")
                return p1;

            if (string.IsNullOrWhiteSpace(p2))
                return p1;

            return $"{p1}/{p2}";
        }

        /// <summary>
        /// GET: /api/tournaments/{tournamentId}/rule
        /// Lấy riêng thể lệ giải của tournament
        /// </summary>
        [HttpGet("{tournamentId:long}/rule")]
        public async Task<IActionResult> GetTournamentRule(long tournamentId)
        {
            var item = await _db.Tournaments
                .AsNoTracking()
                .Where(x => x.TournamentId == tournamentId)
                .Select(x => new TournamentRuleResponseDto
                {
                    TournamentId = x.TournamentId,
                    Title = x.Title,
                    TournamentRule = x.TournamentRule
                })
                .FirstOrDefaultAsync();

            if (item == null)
                return NotFound(new { message = "Tournament not found." });

            return Ok(item);
        }
    }

    #region Response DTOs

    public class TournamentRuleResponseDto
    {
        public long TournamentId { get; set; }
        public string Title { get; set; } = null!;
        public string? TournamentRule { get; set; }
    }

    public class TournamentRoundsWithMatchesResponseDto
    {
        public TournamentClientDto Tournament { get; set; } = null!;
        public List<TournamentRoundMapClientDto> Rounds { get; set; } = new();
    }

    public class TournamentMatchDetailResponseDto
    {
        public TournamentClientDto Tournament { get; set; } = null!;
        public TournamentRoundMapClientDto Round { get; set; } = null!;
        public TournamentRoundGroupClientDto Group { get; set; } = null!;
        public TournamentMatchClientDto Match { get; set; } = null!;
    }

    public class TournamentClientDto
    {
        public long TournamentId { get; set; }
        public string Title { get; set; } = null!;
        public string? BannerUrl { get; set; }
        public string Status { get; set; } = null!;
        public string? StatusText { get; set; }
        public string? StateText { get; set; }
        public DateTime? StartTime { get; set; }
        public string? StartTimeRaw { get; set; }
        public DateTime? RegisterDeadline { get; set; }
        public string? RegisterDeadlineRaw { get; set; }
        public string? FormatText { get; set; }
        public string? PlayoffType { get; set; }
        public string? GameType { get; set; }
        public decimal SingleLimit { get; set; }
        public decimal DoubleLimit { get; set; }
        public string? LocationText { get; set; }
        public string? AreaText { get; set; }
        public int ExpectedTeams { get; set; }
        public int MatchesCount { get; set; }
        public string? Organizer { get; set; }
        public string? CreatorName { get; set; }
        public int RegisteredCount { get; set; }
        public int PairedCount { get; set; }
        public string? Content { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class TournamentRoundMapClientDto
    {
        public long TournamentRoundMapId { get; set; }
        public long TournamentId { get; set; }
        public string RoundKey { get; set; } = null!;
        public string RoundLabel { get; set; } = null!;
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<TournamentRoundGroupClientDto> Groups { get; set; } = new();
    }

    public class TournamentRoundGroupClientDto
    {
        public long TournamentRoundGroupId { get; set; }
        public long TournamentRoundMapId { get; set; }
        public string GroupName { get; set; } = null!;
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<TournamentMatchClientDto> Matches { get; set; } = new();
    }

    public class TournamentMatchClientDto
    {
        public long MatchId { get; set; }
        public long TournamentRoundGroupId { get; set; }
        public long TournamentId { get; set; }

        public long Team1RegistrationId { get; set; }
        public TournamentTeamDto? Team1 { get; set; }

        public long Team2RegistrationId { get; set; }
        public TournamentTeamDto? Team2 { get; set; }

        public DateTime? StartAt { get; set; }
        public string? AddressText { get; set; }
        public string? CourtText { get; set; }
        public string? VideoUrl { get; set; }

        public int ScoreTeam1 { get; set; }
        public int ScoreTeam2 { get; set; }

        public bool IsCompleted { get; set; }
        public long? WinnerRegistrationId { get; set; }
        public string? WinnerTeam { get; set; }
        public TournamentTeamDto? Winner { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class TournamentTeamDto
    {
        public long RegistrationId { get; set; }
        public long TournamentId { get; set; }
        public string RegCode { get; set; } = null!;
        public int RegIndex { get; set; }
        public string DisplayName { get; set; } = null!;
        public bool IsSingle { get; set; }

        public TournamentPlayerDto Player1 { get; set; } = null!;
        public TournamentPlayerDto? Player2 { get; set; }

        public decimal Points { get; set; }
        public bool Paid { get; set; }
        public bool WaitingPair { get; set; }
        public bool Success { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    public class TournamentPlayerDto
    {
        public long? UserId { get; set; }
        public string? Name { get; set; }
        public string? Avatar { get; set; }
        public decimal Level { get; set; }
        public bool Verified { get; set; }
    }

    internal class TournamentRegistrationLiteDto
    {
        public long RegistrationId { get; set; }
        public long TournamentId { get; set; }
        public string RegCode { get; set; } = null!;
        public int RegIndex { get; set; }

        public string Player1Name { get; set; } = null!;
        public string? Player1Avatar { get; set; }
        public decimal Player1Level { get; set; }
        public bool Player1Verified { get; set; }
        public long? Player1UserId { get; set; }

        public string? Player2Name { get; set; }
        public string? Player2Avatar { get; set; }
        public decimal Player2Level { get; set; }
        public bool Player2Verified { get; set; }
        public long? Player2UserId { get; set; }

        public decimal Points { get; set; }
        public bool Paid { get; set; }
        public bool WaitingPair { get; set; }
        public bool Success { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    #endregion
}