using HanakaServer.Data;
using HanakaServer.Dtos;
using HanakaServer.Helpers;
using HanakaServer.Models;
using HanakaServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/tournaments")]
    public class TournamentClientController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly ITournamentStandingsService _standingsService;

        public TournamentClientController(PickleballDbContext db, ITournamentStandingsService standingsService)
        {
            _db = db;
            _standingsService = standingsService;
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
                .Where(x => x.TournamentId == tournamentId && !x.Remove)
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
                    GenderCategory = x.GenderCategory,
                    SingleLimit = x.SingleLimit,
                    DoubleLimit = x.DoubleLimit,
                    LocationText = x.LocationText,
                    AreaText = x.AreaText,
                    ExpectedTeams = x.ExpectedTeams,
                    MatchesCount = x.MatchesCount,
                    Organizer = x.Organizer,
                    CreatorName = x.CreatorName,
                    ZaloLink = x.ZaloLink,
                    RegisteredCount = x.RegisteredCount,
                    PairedCount = x.PairedCount,
                    Content = x.Content,
                    CreatedAt = x.CreatedAt
                })
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found." });

            ApplyTournamentType(tournament);

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
                    x.Team1SourceType,
                    x.Team1SourceMatchId,
                    x.Team1SourceGroupId,
                    x.Team1SourceRank,
                    x.Team2RegistrationId,
                    x.Team2SourceType,
                    x.Team2SourceMatchId,
                    x.Team2SourceGroupId,
                    x.Team2SourceRank,
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

            var sourceGroupIds = matchesRaw
                .SelectMany(x => new[] { x.Team1SourceGroupId, x.Team2SourceGroupId })
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();

            var sourceGroupMap = await _db.TournamentRoundGroups.AsNoTracking()
                .Where(x => sourceGroupIds.Contains(x.TournamentRoundGroupId))
                .Select(x => new { x.TournamentRoundGroupId, x.GroupName })
                .ToDictionaryAsync(x => x.TournamentRoundGroupId, x => x.GroupName);

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
                            TournamentRegistrationLiteDto? team1Reg = null;
                            if (m.Team1RegistrationId.HasValue)
                                regMap.TryGetValue(m.Team1RegistrationId.Value, out team1Reg);

                            TournamentRegistrationLiteDto? team2Reg = null;
                            if (m.Team2RegistrationId.HasValue)
                                regMap.TryGetValue(m.Team2RegistrationId.Value, out team2Reg);

                            TournamentRegistrationLiteDto? winnerReg = null;
                            if (m.WinnerRegistrationId.HasValue)
                                regMap.TryGetValue(m.WinnerRegistrationId.Value, out winnerReg);

                            return new TournamentMatchClientDto
                            {
                                MatchId = m.MatchId,
                                TournamentRoundGroupId = m.TournamentRoundGroupId,
                                TournamentId = m.TournamentId,

                                Team1RegistrationId = m.Team1RegistrationId,
                                Team1 = team1Reg == null
                                    ? BuildTbdTeamDto(m.Team1SourceType, m.Team1SourceMatchId, m.Team1SourceGroupId, m.Team1SourceRank, sourceGroupMap)
                                    : BuildTeamDto(tournament.GameType, team1Reg),
                                Team1Text = team1Reg == null
                                    ? BuildSourceText(m.Team1SourceType, m.Team1SourceMatchId, m.Team1SourceGroupId, m.Team1SourceRank, sourceGroupMap)
                                    : BuildTeamDto(tournament.GameType, team1Reg).DisplayName,
                                Team1SourceType = m.Team1SourceType,
                                Team1SourceMatchId = m.Team1SourceMatchId,
                                Team1SourceGroupId = m.Team1SourceGroupId,
                                Team1SourceRank = m.Team1SourceRank,
                                Team1SourceText = BuildSourceText(m.Team1SourceType, m.Team1SourceMatchId, m.Team1SourceGroupId, m.Team1SourceRank, sourceGroupMap),
                                Team1Resolved = m.Team1RegistrationId.HasValue,

                                Team2RegistrationId = m.Team2RegistrationId,
                                Team2 = team2Reg == null
                                    ? BuildTbdTeamDto(m.Team2SourceType, m.Team2SourceMatchId, m.Team2SourceGroupId, m.Team2SourceRank, sourceGroupMap)
                                    : BuildTeamDto(tournament.GameType, team2Reg),
                                Team2Text = team2Reg == null
                                    ? BuildSourceText(m.Team2SourceType, m.Team2SourceMatchId, m.Team2SourceGroupId, m.Team2SourceRank, sourceGroupMap)
                                    : BuildTeamDto(tournament.GameType, team2Reg).DisplayName,
                                Team2SourceType = m.Team2SourceType,
                                Team2SourceMatchId = m.Team2SourceMatchId,
                                Team2SourceGroupId = m.Team2SourceGroupId,
                                Team2SourceRank = m.Team2SourceRank,
                                Team2SourceText = BuildSourceText(m.Team2SourceType, m.Team2SourceMatchId, m.Team2SourceGroupId, m.Team2SourceRank, sourceGroupMap),
                                Team2Resolved = m.Team2RegistrationId.HasValue,

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
                    x.Team1SourceType,
                    x.Team1SourceMatchId,
                    x.Team1SourceGroupId,
                    x.Team1SourceRank,
                    x.Team2RegistrationId,
                    x.Team2SourceType,
                    x.Team2SourceMatchId,
                    x.Team2SourceGroupId,
                    x.Team2SourceRank,
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
                .Where(x => x.TournamentId == match.TournamentId && !x.Remove)
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
                    GenderCategory = x.GenderCategory,
                    SingleLimit = x.SingleLimit,
                    DoubleLimit = x.DoubleLimit,
                    LocationText = x.LocationText,
                    AreaText = x.AreaText,
                    ExpectedTeams = x.ExpectedTeams,
                    MatchesCount = x.MatchesCount,
                    Organizer = x.Organizer,
                    CreatorName = x.CreatorName,
                    ZaloLink = x.ZaloLink,
                    RegisteredCount = x.RegisteredCount,
                    PairedCount = x.PairedCount,
                    Content = x.Content,
                    CreatedAt = x.CreatedAt
                })
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Tournament not found." });

            ApplyTournamentType(tournament);

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

            var registrationIds = new List<long>();
            if (match.Team1RegistrationId.HasValue)
                registrationIds.Add(match.Team1RegistrationId.Value);
            if (match.Team2RegistrationId.HasValue)
                registrationIds.Add(match.Team2RegistrationId.Value);
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

            TournamentRegistrationLiteDto? team1Reg = null;
            if (match.Team1RegistrationId.HasValue)
                regMap.TryGetValue(match.Team1RegistrationId.Value, out team1Reg);

            TournamentRegistrationLiteDto? team2Reg = null;
            if (match.Team2RegistrationId.HasValue)
                regMap.TryGetValue(match.Team2RegistrationId.Value, out team2Reg);

            TournamentRegistrationLiteDto? winnerReg = null;
            if (match.WinnerRegistrationId.HasValue)
                regMap.TryGetValue(match.WinnerRegistrationId.Value, out winnerReg);

            var sourceGroupIds = new[] { match.Team1SourceGroupId, match.Team2SourceGroupId }
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();

            var sourceGroupMap = await _db.TournamentRoundGroups.AsNoTracking()
                .Where(x => sourceGroupIds.Contains(x.TournamentRoundGroupId))
                .Select(x => new { x.TournamentRoundGroupId, x.GroupName })
                .ToDictionaryAsync(x => x.TournamentRoundGroupId, x => x.GroupName);

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
                    Team1 = team1Reg == null
                        ? BuildTbdTeamDto(match.Team1SourceType, match.Team1SourceMatchId, match.Team1SourceGroupId, match.Team1SourceRank, sourceGroupMap)
                        : BuildTeamDto(tournament.GameType, team1Reg),
                    Team1Text = team1Reg == null
                        ? BuildSourceText(match.Team1SourceType, match.Team1SourceMatchId, match.Team1SourceGroupId, match.Team1SourceRank, sourceGroupMap)
                        : BuildTeamDto(tournament.GameType, team1Reg).DisplayName,
                    Team1SourceType = match.Team1SourceType,
                    Team1SourceMatchId = match.Team1SourceMatchId,
                    Team1SourceGroupId = match.Team1SourceGroupId,
                    Team1SourceRank = match.Team1SourceRank,
                    Team1SourceText = BuildSourceText(match.Team1SourceType, match.Team1SourceMatchId, match.Team1SourceGroupId, match.Team1SourceRank, sourceGroupMap),
                    Team1Resolved = match.Team1RegistrationId.HasValue,

                    Team2RegistrationId = match.Team2RegistrationId,
                    Team2 = team2Reg == null
                        ? BuildTbdTeamDto(match.Team2SourceType, match.Team2SourceMatchId, match.Team2SourceGroupId, match.Team2SourceRank, sourceGroupMap)
                        : BuildTeamDto(tournament.GameType, team2Reg),
                    Team2Text = team2Reg == null
                        ? BuildSourceText(match.Team2SourceType, match.Team2SourceMatchId, match.Team2SourceGroupId, match.Team2SourceRank, sourceGroupMap)
                        : BuildTeamDto(tournament.GameType, team2Reg).DisplayName,
                    Team2SourceType = match.Team2SourceType,
                    Team2SourceMatchId = match.Team2SourceMatchId,
                    Team2SourceGroupId = match.Team2SourceGroupId,
                    Team2SourceRank = match.Team2SourceRank,
                    Team2SourceText = BuildSourceText(match.Team2SourceType, match.Team2SourceMatchId, match.Team2SourceGroupId, match.Team2SourceRank, sourceGroupMap),
                    Team2Resolved = match.Team2RegistrationId.HasValue,

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

        private static void ApplyTournamentType(TournamentClientDto tournament)
        {
            var tournamentType = TournamentTypeHelper.Resolve(tournament.GameType, tournament.GenderCategory);
            tournament.GenderCategory = tournamentType.GenderCategory;
            tournament.TournamentTypeCode = tournamentType.TournamentTypeCode;
            tournament.TournamentTypeLabel = tournamentType.TournamentTypeLabel;
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

        private static TournamentTeamDto BuildTbdTeamDto(
            string? sourceType,
            long? sourceMatchId,
            long? sourceGroupId,
            int? sourceRank,
            IReadOnlyDictionary<long, string>? sourceGroupMap)
        {
            var displayName = BuildSourceText(sourceType, sourceMatchId, sourceGroupId, sourceRank, sourceGroupMap);
            if (string.IsNullOrWhiteSpace(displayName) || displayName == "TBD")
                displayName = "Chưa xác định";

            return new TournamentTeamDto
            {
                RegistrationId = 0,
                TournamentId = 0,
                RegCode = "",
                RegIndex = 0,
                DisplayName = displayName,
                IsSingle = false,
                Player1 = new TournamentPlayerDto
                {
                    Name = displayName
                },
                Points = 0,
                Paid = false,
                WaitingPair = false,
                Success = false,
                CreatedAt = DateTime.MinValue
            };
        }

        private static string BuildSourceText(
            string? sourceType,
            long? sourceMatchId,
            long? sourceGroupId,
            int? sourceRank,
            IReadOnlyDictionary<long, string>? sourceGroupMap)
        {
            sourceType = MatchSourceTypes.Normalize(sourceType);

            return sourceType switch
            {
                MatchSourceTypes.WinnerMatch => sourceMatchId.HasValue ? $"Chờ thắng trận #{sourceMatchId}" : "Chờ thắng trận",
                MatchSourceTypes.LoserMatch => sourceMatchId.HasValue ? $"Chờ thua trận #{sourceMatchId}" : "Chờ thua trận",
                MatchSourceTypes.GroupRank => BuildGroupRankSourceText(sourceGroupId, sourceRank, sourceGroupMap),
                MatchSourceTypes.Bye => "Miễn đấu",
                _ => "Chưa xác định"
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

        private static string BuildDoubleDisplayName(string? player1Name, string? player2Name)
        {
            var p1 = (player1Name ?? "").Trim();
            var p2 = (player2Name ?? "").Trim();

            if (string.IsNullOrWhiteSpace(p2))
                return p1;

            return $"{p1} & {p2}";
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
                .Where(x => x.TournamentId == tournamentId && !x.Remove)
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
                var ordered = (await _standingsService.GetGroupStandingsAsync(g.TournamentRoundGroupId))
                    .Select(x => new GroupStandingRowDto
                    {
                        RegistrationId = x.RegistrationId,
                        TeamName = x.TeamName,
                        Played = x.Played,
                        Wins = x.Wins,
                        Points = x.Points,
                        ScoreDiff = x.ScoreDiff,
                        ScoreFor = x.ScoreFor,
                        ScoreAgainst = x.ScoreAgainst,
                        Rank = x.Rank
                    })
                    .ToList();

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
                .Where(x => x.TournamentId == tournamentId && !x.Remove)
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
        public string GenderCategory { get; set; } = "OPEN";
        public string TournamentTypeCode { get; set; } = "DOUBLE_OPEN";
        public string TournamentTypeLabel { get; set; } = "";
        public decimal SingleLimit { get; set; }
        public decimal DoubleLimit { get; set; }
        public string? LocationText { get; set; }
        public string? AreaText { get; set; }
        public int ExpectedTeams { get; set; }
        public int MatchesCount { get; set; }
        public string? Organizer { get; set; }
        public string? CreatorName { get; set; }
        public string? ZaloLink { get; set; }
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

        public long? Team1RegistrationId { get; set; }
        public TournamentTeamDto? Team1 { get; set; }
        public string? Team1Text { get; set; }
        public string Team1SourceType { get; set; } = MatchSourceTypes.Registration;
        public long? Team1SourceMatchId { get; set; }
        public long? Team1SourceGroupId { get; set; }
        public int? Team1SourceRank { get; set; }
        public string? Team1SourceText { get; set; }
        public bool Team1Resolved { get; set; }

        public long? Team2RegistrationId { get; set; }
        public TournamentTeamDto? Team2 { get; set; }
        public string? Team2Text { get; set; }
        public string Team2SourceType { get; set; } = MatchSourceTypes.Registration;
        public long? Team2SourceMatchId { get; set; }
        public long? Team2SourceGroupId { get; set; }
        public int? Team2SourceRank { get; set; }
        public string? Team2SourceText { get; set; }
        public bool Team2Resolved { get; set; }

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
