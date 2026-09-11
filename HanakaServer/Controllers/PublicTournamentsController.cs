using HanakaServer.Data;
using HanakaServer.Dtos;
using HanakaServer.Helpers;
using HanakaServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/public/tournaments")]
    [AllowAnonymous]
    public class PublicTournamentsController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly IConfiguration _config;
        private readonly ILogger<PublicTournamentsController>? _logger;
        private readonly IMemoryCache? _cache;

        public PublicTournamentsController(
            PickleballDbContext db,
            IConfiguration config,
            ILogger<PublicTournamentsController>? logger = null,
            IMemoryCache? cache = null)
        {
            _db = db;
            _config = config;
            _logger = logger;
            _cache = cache;
        }

        private string? ToAbsoluteUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            url = url.Trim();
            if (Uri.TryCreate(url, UriKind.Absolute, out _)) return url;

            var baseUrl = (_config["PublicBaseUrl"] ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl)) return url;
            return url.StartsWith('/') ? baseUrl + url : baseUrl + "/" + url;
        }

        // GET: /api/public/tournaments/{id}
        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetDetail(long id)
        {
            var t = await _db.Tournaments.AsNoTracking()
                .Where(x => x.TournamentId == id && !x.Remove && x.Status != "DRAFT")
                .Select(x => new PublicTournamentDetailDto
                {
                    TournamentId = x.TournamentId,
                    ExternalId = x.ExternalId,

                    Status = x.Status,
                    Title = x.Title,

                    BannerUrl = x.BannerUrl,

                    StartTimeRaw = x.StartTimeRaw,
                    StartTime = x.StartTime,

                    RegisterDeadlineRaw = x.RegisterDeadlineRaw,
                    RegisterDeadline = x.RegisterDeadline,

                    FormatText = x.FormatText,
                    PlayoffType = x.PlayoffType,
                    GameType = x.GameType,
                    GenderCategory = x.GenderCategory,

                    SingleLimit = x.SingleLimit,
                    DoubleLimit = x.DoubleLimit,
                    RegistrationFeeAmount = x.RegistrationFeeAmount,
                    RegistrationFeeCurrency = x.RegistrationFeeCurrency,

                    LocationText = x.LocationText,
                    AreaText = x.AreaText,

                    ExpectedTeams = x.ExpectedTeams,
                    MatchesCount = x.MatchesCount,

                    StatusText = x.StatusText,
                    StateText = x.StateText,

                    Organizer = x.Organizer,
                    CreatorName = x.CreatorName,
                    ZaloLink = x.ZaloLink,

                    RegisteredCount = null,
                    PairedCount = null,

                    Content = x.Content,
                    CreatedAt = x.CreatedAt
                })
                .FirstOrDefaultAsync();

            if (t == null) return NotFound(new { message = "Không tìm thấy giải đấu." });

            t.BannerUrl = ToAbsoluteUrl(t.BannerUrl);

            var matchCount = await _db.TournamentGroupMatches
                .AsNoTracking()
                .CountAsync(m => m.TournamentId == id);

            t.MatchesCount = matchCount;

            var regQ = _db.TournamentRegistrations
                .AsNoTracking()
                .Where(r => r.TournamentId == id && !r.IsVirtualTeam);

            var waitingCount = await regQ.CountAsync(r => r.WaitingPair);
            var successCount = await regQ.CountAsync(r => r.Success);
            var tournamentType = TournamentTypeHelper.Resolve(t.GameType, t.GenderCategory);
            var (registeredCount, pairedCount) = ComputePublicRegistrationCounts(
                t.GameType,
                t.GenderCategory,
                successCount,
                waitingCount);

            t.GenderCategory = tournamentType.GenderCategory;
            t.TournamentTypeCode = tournamentType.TournamentTypeCode;
            t.TournamentTypeLabel = tournamentType.TournamentTypeLabel;
            t.RegisteredCount = registeredCount;
            t.PairedCount = pairedCount;

            return Ok(t);
        }

        // GET: /api/public/tournaments/{tournamentId}/registrations
        [HttpGet("{tournamentId:long}/registrations")]
        public async Task<IActionResult> PublicRegistrations(
            long tournamentId,
            [FromQuery] string tab = "ALL",
            CancellationToken cancellationToken = default)
        {
            tab = (tab ?? "ALL").Trim().ToUpperInvariant();
            if (tab is not ("SUCCESS" or "WAITING")) tab = "ALL";
            var cacheSeconds = Math.Clamp(
                _config.GetValue<int?>("PublicRegistrations:CacheSeconds") ?? 5,
                0,
                30);
            var cacheKey = $"public-registrations:{tournamentId}:{tab}";

            if (cacheSeconds > 0
                && _cache?.TryGetValue(cacheKey, out PublicTournamentRegistrationsResponseDto? cached) == true
                && cached != null)
            {
                return Ok(cached);
            }

            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    var result = await LoadPublicRegistrations(tournamentId, tab, cancellationToken);
                    if (cacheSeconds > 0
                        && result is OkObjectResult { Value: PublicTournamentRegistrationsResponseDto response })
                    {
                        _cache?.Set(cacheKey, response, TimeSpan.FromSeconds(cacheSeconds));
                    }

                    return result;
                }
                catch (SqlException exception) when (exception.Number == -2)
                {
                    _logger?.LogWarning(
                        exception,
                        "SQL timeout while loading public registrations for tournament {TournamentId} (attempt {Attempt}/2).",
                        tournamentId,
                        attempt);

                    if (attempt == 2)
                    {
                        return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                        {
                            code = "DATABASE_TIMEOUT",
                            message = "Dữ liệu giải đấu đang phản hồi chậm. Vui lòng thử lại sau ít phút."
                        });
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
                }
            }

            throw new InvalidOperationException("Public registration retry loop exited unexpectedly.");
        }

        private async Task<IActionResult> LoadPublicRegistrations(
            long tournamentId,
            string tab,
            CancellationToken cancellationToken)
        {
            PublicRegistrationsTournamentRow? tournament;
            if (_config.GetValue<bool>("Relay:AdminPreviewEnabled"))
            {
                tournament = await (
                    from item in _db.Tournaments.AsNoTracking()
                    where item.TournamentId == tournamentId && !item.Remove && item.Status != "DRAFT"
                    join relaySetting in _db.RelayTournamentSettings.AsNoTracking()
                        on item.TournamentId equals relaySetting.TournamentId into relaySettings
                    from relaySetting in relaySettings.DefaultIfEmpty()
                    select new PublicRegistrationsTournamentRow
                    {
                        TournamentId = item.TournamentId,
                        ExpectedTeams = item.ExpectedTeams,
                        GameType = item.GameType ?? "DOUBLE",
                        GenderCategory = item.GenderCategory,
                        Title = item.Title,
                        Status = item.Status,
                        RegistrationFeeAmount = item.RegistrationFeeAmount,
                        RegistrationFeeCurrency = item.RegistrationFeeCurrency,
                        ZaloLink = item.ZaloLink,
                        IsRelay = relaySetting != null,
                        TeamSize = relaySetting == null ? null : relaySetting.TeamSize,
                        SuccessCount = _db.TournamentRegistrations.Count(registration =>
                            registration.TournamentId == item.TournamentId && !registration.IsVirtualTeam && registration.Success),
                        WaitingCount = _db.TournamentRegistrations.Count(registration =>
                            registration.TournamentId == item.TournamentId && !registration.IsVirtualTeam && registration.WaitingPair),
                        PaidCount = _db.TournamentRegistrations.Count(registration =>
                            registration.TournamentId == item.TournamentId && !registration.IsVirtualTeam && registration.Paid)
                    })
                    .FirstOrDefaultAsync(cancellationToken);
            }
            else
            {
                tournament = await _db.Tournaments
                    .AsNoTracking()
                    .Where(item => item.TournamentId == tournamentId && !item.Remove && item.Status != "DRAFT")
                    .Select(item => new PublicRegistrationsTournamentRow
                    {
                        TournamentId = item.TournamentId,
                        ExpectedTeams = item.ExpectedTeams,
                        GameType = item.GameType ?? "DOUBLE",
                        GenderCategory = item.GenderCategory,
                        Title = item.Title,
                        Status = item.Status,
                        RegistrationFeeAmount = item.RegistrationFeeAmount,
                        RegistrationFeeCurrency = item.RegistrationFeeCurrency,
                        ZaloLink = item.ZaloLink,
                        SuccessCount = _db.TournamentRegistrations.Count(registration =>
                            registration.TournamentId == item.TournamentId && !registration.IsVirtualTeam && registration.Success),
                        WaitingCount = _db.TournamentRegistrations.Count(registration =>
                            registration.TournamentId == item.TournamentId && !registration.IsVirtualTeam && registration.WaitingPair),
                        PaidCount = _db.TournamentRegistrations.Count(registration =>
                            registration.TournamentId == item.TournamentId && !registration.IsVirtualTeam && registration.Paid)
                    })
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (tournament == null)
                return NotFound(new { message = "Không tìm thấy giải đấu." });

            var baseQuery = _db.TournamentRegistrations
                .AsNoTracking()
                .Where(item => item.TournamentId == tournamentId && !item.IsVirtualTeam);

            var filteredQuery = baseQuery;
            if (tab == "SUCCESS") filteredQuery = filteredQuery.Where(item => item.Success);
            else if (tab == "WAITING") filteredQuery = filteredQuery.Where(item => item.WaitingPair);

            var tournamentType = TournamentTypeHelper.Resolve(tournament.GameType, tournament.GenderCategory);
            var mapped = tournament.IsRelay
                ? await LoadRelayRegistrations(filteredQuery, tournament, cancellationToken)
                : await LoadStandardRegistrations(filteredQuery, tournamentType.IsDoubleLike, cancellationToken);

            var successItems = mapped.Where(item => item.Success).ToArray();
            var waitingItems = mapped.Where(item => item.WaitingPair).ToArray();

            return Ok(new PublicTournamentRegistrationsResponseDto
            {
                Tournament = new
                {
                    tournament.TournamentId,
                    tournament.Title,
                    tournament.Status,
                    tournament.GameType,
                    GenderCategory = tournamentType.GenderCategory,
                    TournamentTypeCode = tournament.IsRelay ? "RELAY_TEAM" : tournamentType.TournamentTypeCode,
                    TournamentTypeLabel = tournament.IsRelay ? "Đồng đội tiếp sức" : tournamentType.TournamentTypeLabel,
                    tournament.IsRelay,
                    tournament.TeamSize,
                    PairCount = tournament.TeamSize / 2,
                    tournament.ExpectedTeams,
                    tournament.RegistrationFeeAmount,
                    tournament.RegistrationFeeCurrency,
                    tournament.ZaloLink
                },
                Counts = new PublicRegistrationCountsDto
                {
                    Success = tournament.SuccessCount,
                    Waiting = tournament.WaitingCount,
                    Paid = tournament.PaidCount,
                    CapacityLeft = Math.Max(0, tournament.ExpectedTeams - tournament.SuccessCount)
                },
                SuccessItems = successItems,
                WaitingItems = waitingItems
            });
        }

        private async Task<List<PublicRegistrationItemDto>> LoadStandardRegistrations(
            IQueryable<TournamentRegistration> query,
            bool isDouble,
            CancellationToken cancellationToken)
        {
            var rows = await (
                from registration in query.OrderBy(item => item.RegIndex)

                join player1User in _db.Users on registration.Player1UserId equals (long?)player1User.UserId into player1Users
                from player1User in player1Users.DefaultIfEmpty()

                join player2User in _db.Users on registration.Player2UserId equals (long?)player2User.UserId into player2Users
                from player2User in player2Users.DefaultIfEmpty()

                let player1LatestRating = _db.UserRatingHistories
                    .Where(history => player1User != null && history.UserId == player1User.UserId)
                    .OrderByDescending(history => history.RatedAt)
                    .ThenByDescending(history => history.RatingHistoryId)
                    .Select(history => new { history.RatingSingle, history.RatingDouble })
                    .FirstOrDefault()

                let player2LatestRating = _db.UserRatingHistories
                    .Where(history => player2User != null && history.UserId == player2User.UserId)
                    .OrderByDescending(history => history.RatedAt)
                    .ThenByDescending(history => history.RatingHistoryId)
                    .Select(history => new { history.RatingSingle, history.RatingDouble })
                    .FirstOrDefault()

                select new StandardPublicRegistrationRow
                {
                    RegistrationId = registration.RegistrationId,
                    RegIndex = registration.RegIndex,
                    RegCode = registration.RegCode,
                    RegTime = registration.RegTime,
                    Paid = registration.Paid,
                    PaidAt = registration.PaidAt,
                    PaymentAmount = registration.PaymentAmount,
                    WaitingPair = registration.WaitingPair,
                    Success = registration.Success,
                    Player1UserId = registration.Player1UserId,
                    Player1Name = registration.Player1Name,
                    Player1Avatar = registration.Player1Avatar,
                    Player1Level = registration.Player1Level,
                    Player2UserId = registration.Player2UserId,
                    Player2Name = registration.Player2Name,
                    Player2Avatar = registration.Player2Avatar,
                    Player2Level = registration.Player2Level,
                    Player1CurrentAvatar = player1User == null ? null : player1User.AvatarUrl,
                    Player2CurrentAvatar = player2User == null ? null : player2User.AvatarUrl,
                    Player1RatingSingle = player1LatestRating == null ? null : player1LatestRating.RatingSingle,
                    Player1RatingDouble = player1LatestRating == null ? null : player1LatestRating.RatingDouble,
                    Player2RatingSingle = player2LatestRating == null ? null : player2LatestRating.RatingSingle,
                    Player2RatingDouble = player2LatestRating == null ? null : player2LatestRating.RatingDouble,
                    Player1Verified = player1User != null && player1User.Verified,
                    Player2Verified = player2User != null && player2User.Verified
                })
                .ToListAsync(cancellationToken);

            return rows.Select(row => MapStandardRegistration(row, isDouble)).ToList();
        }

        private PublicRegistrationItemDto MapStandardRegistration(StandardPublicRegistrationRow row, bool isDouble)
        {
            var player1Level = row.Player1Level;
            var player2Level = row.Player2Level;

            if (row.Player1UserId.HasValue && player1Level == 0m)
            {
                var currentRating = isDouble ? row.Player1RatingDouble : row.Player1RatingSingle;
                if (currentRating.HasValue) player1Level = currentRating.Value;
            }

            if (row.Player2UserId.HasValue && !row.WaitingPair && !string.IsNullOrWhiteSpace(row.Player2Name))
            {
                var currentRating = isDouble ? row.Player2RatingDouble : row.Player2RatingSingle;
                if (currentRating.HasValue) player2Level = currentRating.Value;
            }

            var player1 = new PublicPlayerDto
            {
                UserId = row.Player1UserId,
                IsGuest = !row.Player1UserId.HasValue,
                Verified = row.Player1UserId.HasValue && row.Player1Verified,
                Name = row.Player1Name ?? "",
                Avatar = ToAbsoluteUrl(row.Player1UserId.HasValue ? row.Player1CurrentAvatar : row.Player1Avatar),
                Level = player1Level
            };

            PublicPlayerDto? player2 = null;
            if (!row.WaitingPair && !string.IsNullOrWhiteSpace(row.Player2Name))
            {
                player2 = new PublicPlayerDto
                {
                    UserId = row.Player2UserId,
                    IsGuest = !row.Player2UserId.HasValue,
                    Verified = row.Player2UserId.HasValue && row.Player2Verified,
                    Name = row.Player2Name,
                    Avatar = ToAbsoluteUrl(row.Player2UserId.HasValue ? row.Player2CurrentAvatar : row.Player2Avatar),
                    Level = player2Level
                };
            }

            return new PublicRegistrationItemDto
            {
                RegistrationId = row.RegistrationId,
                RegIndex = row.RegIndex,
                RegCode = row.RegCode,
                RegTime = row.RegTime,
                Points = CalcPoints(isDouble ? "DOUBLE" : "SINGLE", player1Level, player2?.Level),
                WaitingPair = row.WaitingPair,
                Success = row.Success,
                Paid = row.Paid,
                PaidAt = row.PaidAt,
                PaymentAmount = row.PaymentAmount,
                Player1 = player1,
                Player2 = player2
            };
        }

        private async Task<List<PublicRegistrationItemDto>> LoadRelayRegistrations(
            IQueryable<TournamentRegistration> query,
            PublicRegistrationsTournamentRow tournament,
            CancellationToken cancellationToken)
        {
            var rows = await (
                from registration in query
                join relayTeam in _db.RelayTeams.AsNoTracking()
                    on registration.RegistrationId equals relayTeam.RegistrationId into relayTeams
                from relayTeam in relayTeams.DefaultIfEmpty()
                join relayMember in _db.RelayTeamMembers.AsNoTracking()
                    on registration.RegistrationId equals relayMember.RegistrationId into relayMembers
                from relayMember in relayMembers.DefaultIfEmpty()
                join relayUser in _db.Users.AsNoTracking()
                    on (relayMember == null ? null : relayMember.UserId) equals (long?)relayUser.UserId into relayUsers
                from relayUser in relayUsers.DefaultIfEmpty()
                let latestRatingDouble = _db.UserRatingHistories
                    .Where(history => relayUser != null && history.UserId == relayUser.UserId)
                    .OrderByDescending(history => history.RatedAt)
                    .ThenByDescending(history => history.RatingHistoryId)
                    .Select(history => history.RatingDouble)
                    .FirstOrDefault()
                orderby registration.RegIndex, relayMember == null ? 0 : relayMember.Position
                select new RelayPublicRegistrationRow
                {
                    RegistrationId = registration.RegistrationId,
                    RegIndex = registration.RegIndex,
                    RegCode = registration.RegCode,
                    RegTime = registration.RegTime,
                    Paid = registration.Paid,
                    PaidAt = registration.PaidAt,
                    PaymentAmount = registration.PaymentAmount,
                    WaitingPair = registration.WaitingPair,
                    Success = registration.Success,
                    LegacyPlayer1UserId = registration.Player1UserId,
                    LegacyPlayer1Name = registration.Player1Name,
                    LegacyPlayer1Avatar = registration.Player1Avatar,
                    LegacyPlayer1Level = registration.Player1Level,
                    LegacyPlayer1Verified = registration.Player1Verified,
                    LegacyPlayer2UserId = registration.Player2UserId,
                    LegacyPlayer2Name = registration.Player2Name,
                    LegacyPlayer2Avatar = registration.Player2Avatar,
                    LegacyPlayer2Level = registration.Player2Level,
                    LegacyPlayer2Verified = registration.Player2Verified,
                    TeamName = relayTeam == null ? null : relayTeam.TeamName,
                    CaptainUserId = relayTeam == null ? null : relayTeam.CaptainUserId,
                    MemberPosition = relayMember == null ? null : relayMember.Position,
                    MemberUserId = relayMember == null ? null : relayMember.UserId,
                    MemberName = relayMember == null ? null : relayMember.DisplayName,
                    MemberAvatar = relayMember == null ? null : relayMember.AvatarUrl,
                    CurrentName = relayUser == null ? null : relayUser.FullName,
                    CurrentAvatar = relayUser == null ? null : relayUser.AvatarUrl,
                    CurrentRatingDouble = relayUser == null ? null : relayUser.RatingDouble,
                    LatestRatingDouble = latestRatingDouble,
                    CurrentVerified = relayUser != null && relayUser.Verified
                })
                .ToListAsync(cancellationToken);

            var items = rows.GroupBy(row => row.RegistrationId)
                .Select(group => MapRelayRegistration(group, tournament.TeamSize)).ToList();
            var ids = items.Select(x => x.RegistrationId).ToArray();
            // Separate batch avoids multiplying main roster rows by the reserve count.
            var reserves = await (
                from reserve in _db.RelayTeamReserveMembers.AsNoTracking()
                where ids.Contains(reserve.RegistrationId)
                join account in _db.Users.AsNoTracking() on reserve.UserId equals (long?)account.UserId into accounts
                from account in accounts.DefaultIfEmpty()
                select new
                {
                    reserve.RegistrationId, reserve.Position, reserve.UserId,
                    Name = account != null ? account.FullName : reserve.DisplayName,
                    Avatar = account != null ? account.AvatarUrl ?? reserve.AvatarUrl : reserve.AvatarUrl,
                    Verified = account != null && account.Verified,
                    Level = account == null ? 0m : _db.UserRatingHistories.Where(x => x.UserId == account.UserId)
                        .OrderByDescending(x => x.RatedAt).ThenByDescending(x => x.RatingHistoryId)
                        .Select(x => x.RatingDouble).FirstOrDefault() ?? account.RatingDouble ?? 0m
                }).ToListAsync(cancellationToken);
            var byRegistration = reserves.ToLookup(x => x.RegistrationId);
            foreach (var item in items)
                item.ReserveMembers = byRegistration[item.RegistrationId].OrderBy(x => x.Position)
                    .Select(x => new PublicRelayReserveMemberDto
                    {
                        Position = x.Position, UserId = x.UserId, Name = x.Name, Avatar = ToAbsoluteUrl(x.Avatar),
                        Level = x.Level, Verified = x.Verified
                    }).ToArray();
            return items;
        }

        private PublicRegistrationItemDto MapRelayRegistration(
            IGrouping<long, RelayPublicRegistrationRow> group,
            int? configuredTeamSize)
        {
            var first = group.First();
            var members = group
                .Where(row => row.MemberPosition.HasValue)
                .OrderBy(row => row.MemberPosition)
                .Select(row => new PublicRelayRegistrationMemberDto
                {
                    Position = row.MemberPosition!.Value,
                    PairNumber = (row.MemberPosition.Value + 1) / 2,
                    UserId = row.MemberUserId,
                    Name = !string.IsNullOrWhiteSpace(row.CurrentName) ? row.CurrentName : row.MemberName ?? "",
                    Avatar = ToAbsoluteUrl(row.CurrentAvatar ?? row.MemberAvatar),
                    Level = row.LatestRatingDouble ?? row.CurrentRatingDouble ?? 0m,
                    Verified = row.CurrentVerified,
                    IsCaptain = row.MemberUserId.HasValue && row.MemberUserId == first.CaptainUserId
                })
                .ToArray();

            var firstMember = members.FirstOrDefault(member => member.Position == 1);
            var secondMember = members.FirstOrDefault(member => member.Position == 2);
            var player1 = firstMember == null
                ? new PublicPlayerDto
                {
                    UserId = first.LegacyPlayer1UserId,
                    IsGuest = !first.LegacyPlayer1UserId.HasValue,
                    Verified = first.LegacyPlayer1Verified,
                    Name = first.LegacyPlayer1Name ?? "",
                    Avatar = ToAbsoluteUrl(first.LegacyPlayer1Avatar),
                    Level = first.LegacyPlayer1Level
                }
                : MapRelayMemberToPlayer(firstMember);
            var player2 = secondMember == null
                ? (!first.WaitingPair && !string.IsNullOrWhiteSpace(first.LegacyPlayer2Name)
                    ? new PublicPlayerDto
                    {
                        UserId = first.LegacyPlayer2UserId,
                        IsGuest = !first.LegacyPlayer2UserId.HasValue,
                        Verified = first.LegacyPlayer2Verified,
                        Name = first.LegacyPlayer2Name,
                        Avatar = ToAbsoluteUrl(first.LegacyPlayer2Avatar),
                        Level = first.LegacyPlayer2Level
                    }
                    : null)
                : MapRelayMemberToPlayer(secondMember);

            var teamSize = configuredTeamSize ?? members.Length;
            var isReady = members.Length == teamSize
                          && members.OrderBy(x => x.Position).Select(x => x.Position)
                              .SequenceEqual(Enumerable.Range(1, teamSize))
                          && members.All(x => !string.IsNullOrWhiteSpace(x.Name));
            return new PublicRegistrationItemDto
            {
                RegistrationId = first.RegistrationId,
                RegIndex = first.RegIndex,
                RegCode = first.RegCode,
                RegTime = first.RegTime,
                Points = 0m,
                WaitingPair = first.WaitingPair,
                Success = first.Success,
                Paid = first.Paid,
                PaidAt = first.PaidAt,
                PaymentAmount = first.PaymentAmount,
                Player1 = player1,
                Player2 = player2,
                IsRelay = true,
                TeamName = first.TeamName,
                CaptainUserId = first.CaptainUserId,
                TeamSize = teamSize,
                IsReady = isReady,
                LineupLocked = isReady,
                Members = members
            };
        }

        private static PublicPlayerDto MapRelayMemberToPlayer(PublicRelayRegistrationMemberDto member) => new()
        {
            UserId = member.UserId,
            IsGuest = !member.UserId.HasValue,
            Verified = member.Verified,
            Name = member.Name,
            Avatar = member.Avatar,
            Level = member.Level
        };

        private sealed class PublicRegistrationsTournamentRow
        {
            public long TournamentId { get; init; }
            public int ExpectedTeams { get; init; }
            public string GameType { get; init; } = "DOUBLE";
            public string GenderCategory { get; init; } = "OPEN";
            public string Title { get; init; } = "";
            public string Status { get; init; } = "";
            public decimal RegistrationFeeAmount { get; init; }
            public string RegistrationFeeCurrency { get; init; } = "VND";
            public string? ZaloLink { get; init; }
            public bool IsRelay { get; init; }
            public int? TeamSize { get; init; }
            public int SuccessCount { get; init; }
            public int WaitingCount { get; init; }
            public int PaidCount { get; init; }
        }

        private sealed class StandardPublicRegistrationRow
        {
            public long RegistrationId { get; init; }
            public int RegIndex { get; init; }
            public string RegCode { get; init; } = "";
            public DateTime? RegTime { get; init; }
            public bool Paid { get; init; }
            public DateTime? PaidAt { get; init; }
            public decimal? PaymentAmount { get; init; }
            public bool WaitingPair { get; init; }
            public bool Success { get; init; }
            public long? Player1UserId { get; init; }
            public string? Player1Name { get; init; }
            public string? Player1Avatar { get; init; }
            public decimal Player1Level { get; init; }
            public long? Player2UserId { get; init; }
            public string? Player2Name { get; init; }
            public string? Player2Avatar { get; init; }
            public decimal Player2Level { get; init; }
            public string? Player1CurrentAvatar { get; init; }
            public string? Player2CurrentAvatar { get; init; }
            public decimal? Player1RatingSingle { get; init; }
            public decimal? Player1RatingDouble { get; init; }
            public decimal? Player2RatingSingle { get; init; }
            public decimal? Player2RatingDouble { get; init; }
            public bool Player1Verified { get; init; }
            public bool Player2Verified { get; init; }
        }

        private sealed class RelayPublicRegistrationRow
        {
            public long RegistrationId { get; init; }
            public int RegIndex { get; init; }
            public string RegCode { get; init; } = "";
            public DateTime? RegTime { get; init; }
            public bool Paid { get; init; }
            public DateTime? PaidAt { get; init; }
            public decimal? PaymentAmount { get; init; }
            public bool WaitingPair { get; init; }
            public bool Success { get; init; }
            public long? LegacyPlayer1UserId { get; init; }
            public string? LegacyPlayer1Name { get; init; }
            public string? LegacyPlayer1Avatar { get; init; }
            public decimal LegacyPlayer1Level { get; init; }
            public bool LegacyPlayer1Verified { get; init; }
            public long? LegacyPlayer2UserId { get; init; }
            public string? LegacyPlayer2Name { get; init; }
            public string? LegacyPlayer2Avatar { get; init; }
            public decimal LegacyPlayer2Level { get; init; }
            public bool LegacyPlayer2Verified { get; init; }
            public string? TeamName { get; init; }
            public long? CaptainUserId { get; init; }
            public int? MemberPosition { get; init; }
            public long? MemberUserId { get; init; }
            public string? MemberName { get; init; }
            public string? MemberAvatar { get; init; }
            public string? CurrentName { get; init; }
            public string? CurrentAvatar { get; init; }
            public decimal? CurrentRatingDouble { get; init; }
            public decimal? LatestRatingDouble { get; init; }
            public bool CurrentVerified { get; init; }
        }

        private static decimal CalcPoints(string? gameType, decimal player1Level, decimal? player2Level)
        {
            var normalized = (gameType ?? "DOUBLE").Trim().ToUpperInvariant();
            return normalized == "SINGLE"
                ? player1Level
                : player1Level + (player2Level ?? 0m);
        }

        private static string? TrimToNull(string? s)
        {
            if (s == null) return null;
            var t = s.Trim();
            return string.IsNullOrWhiteSpace(t) ? null : t;
        }

        public class TournamentMobileListItemDto
        {
            public long TournamentId { get; set; }
            public string Title { get; set; } = null!;
            public string Status { get; set; } = null!;
            public string? StatusText { get; set; }
            public string? StateText { get; set; }
            public DateTime? StartTime { get; set; }
            public DateTime? RegisterDeadline { get; set; }
            public string GameType { get; set; } = "DOUBLE";
            public string GenderCategory { get; set; } = "OPEN";
            public string TournamentTypeCode { get; set; } = "DOUBLE_OPEN";
            public string TournamentTypeLabel { get; set; } = "";
            public bool IsRelay { get; set; }
            public int? RelayTeamSize { get; set; }
            public int? RelayTargetScore { get; set; }
            public decimal SingleLimit { get; set; }
            public decimal DoubleLimit { get; set; }
            public int ExpectedTeams { get; set; }
            public int RegisteredCount { get; set; }
            public int PairedCount { get; set; }
            public int MatchesCount { get; set; }
            public string? LocationText { get; set; }
            public string? AreaText { get; set; }
            public string? Organizer { get; set; }
            public string? CreatorName { get; set; }
            public string? ZaloLink { get; set; }
            public string? FormatText { get; set; }
            public string? PlayoffType { get; set; }
            public decimal RegistrationFeeAmount { get; set; }
            public string RegistrationFeeCurrency { get; set; } = "VND";
            public string? BannerUrl { get; set; }
            public string? Content { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        private TournamentMobileListItemDto MapToDto(Tournament t)
        {
            var tournamentType = TournamentTypeHelper.Resolve(t.GameType, t.GenderCategory);

            return new TournamentMobileListItemDto
            {
                TournamentId = t.TournamentId,
                Title = t.Title,
                Status = t.Status,
                StatusText = t.StatusText,
                StateText = t.StateText,
                StartTime = t.StartTime,
                RegisterDeadline = t.RegisterDeadline,
                GameType = t.GameType ?? "DOUBLE",
                GenderCategory = tournamentType.GenderCategory,
                TournamentTypeCode = tournamentType.TournamentTypeCode,
                TournamentTypeLabel = tournamentType.TournamentTypeLabel,
                SingleLimit = t.SingleLimit,
                DoubleLimit = t.DoubleLimit,
                ExpectedTeams = t.ExpectedTeams,
                RegisteredCount = t.RegisteredCount,
                PairedCount = t.PairedCount,
                MatchesCount = t.MatchesCount,
                LocationText = t.LocationText,
                AreaText = t.AreaText,
                Organizer = t.Organizer,
                CreatorName = t.CreatorName,
                ZaloLink = t.ZaloLink,
                FormatText = t.FormatText,
                PlayoffType = t.PlayoffType,
                RegistrationFeeAmount = t.RegistrationFeeAmount,
                RegistrationFeeCurrency = t.RegistrationFeeCurrency,
                BannerUrl = ToAbsoluteUrl(t.BannerUrl),
                Content = t.Content,
                CreatedAt = t.CreatedAt
            };
        }

        private static (int registeredCount, int pairedCount) ComputePublicRegistrationCounts(
            string? gameType,
            string? genderCategory,
            int successCount,
            int waitingCount)
        {
            var isDoubleLike = TournamentTypeHelper.IsDoubleLike(gameType, genderCategory);

            if (isDoubleLike)
            {
                var pairedCount = successCount * 2;
                var registeredCount = waitingCount + pairedCount;
                return (registeredCount, pairedCount);
            }

            return (successCount + waitingCount, successCount);
        }

        // GET: /api/public/tournaments?page=1&pageSize=10&status=OPEN&query=abc
        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? status = null,
            [FromQuery] string? query = null)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 10;
            if (pageSize > 50) pageSize = 50;

            status = TrimToNull(status)?.ToUpperInvariant();
            query = TrimToNull(query);

            var q = _db.Tournaments.AsNoTracking().AsQueryable();

            // Ẩn toàn bộ giải draft và giải đã xóa mềm ở public API
            q = q.Where(t => !t.Remove && t.Status != "DRAFT");

            if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
            {
                q = q.Where(x => x.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                q = q.Where(x =>
                    x.Title.Contains(query) ||
                    (x.LocationText != null && x.LocationText.Contains(query)) ||
                    (x.AreaText != null && x.AreaText.Contains(query)) ||
                    (x.Organizer != null && x.Organizer.Contains(query)) ||
                    (x.CreatorName != null && x.CreatorName.Contains(query))
                );
            }

            var total = await q.CountAsync();

            var raw = await q
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.TournamentId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var tournamentIds = raw.Select(x => x.TournamentId).ToList();

            var relaySettingsMap = new Dictionary<long, RelayTournamentSettings>();
            if (_config.GetValue<bool>("Relay:AdminPreviewEnabled") && tournamentIds.Count > 0)
            {
                relaySettingsMap = await _db.RelayTournamentSettings
                    .AsNoTracking()
                    .Where(x => tournamentIds.Contains(x.TournamentId))
                    .ToDictionaryAsync(x => x.TournamentId);
            }

            var registrationStats = await _db.TournamentRegistrations
                .AsNoTracking()
                .Where(x => tournamentIds.Contains(x.TournamentId) && !x.IsVirtualTeam)
                .GroupBy(x => x.TournamentId)
                .Select(g => new
                {
                    TournamentId = g.Key,
                    SuccessCount = g.Count(x => x.Success),
                    WaitingCount = g.Count(x => x.WaitingPair)
                })
                .ToListAsync();

            var matchStats = await _db.TournamentGroupMatches
                .AsNoTracking()
                .Where(x => tournamentIds.Contains(x.TournamentId))
                .GroupBy(x => x.TournamentId)
                .Select(g => new
                {
                    TournamentId = g.Key,
                    MatchesCount = g.Count()
                })
                .ToListAsync();

            var registrationStatsMap = registrationStats.ToDictionary(x => x.TournamentId, x => x);
            var matchStatsMap = matchStats.ToDictionary(x => x.TournamentId, x => x.MatchesCount);

            var items = raw.Select(t =>
            {
                var dto = MapToDto(t);

                if (relaySettingsMap.TryGetValue(t.TournamentId, out var relaySetting))
                {
                    dto.IsRelay = true;
                    dto.RelayTeamSize = relaySetting.TeamSize;
                    dto.RelayTargetScore = relaySetting.TargetScore;
                    dto.TournamentTypeCode = "RELAY_TEAM";
                    dto.TournamentTypeLabel = "Đội tiếp sức";
                }

                registrationStatsMap.TryGetValue(t.TournamentId, out var regStat);
                matchStatsMap.TryGetValue(t.TournamentId, out var liveMatchesCount);

                var (registeredCount, pairedCount) = ComputePublicRegistrationCounts(
                    t.GameType,
                    t.GenderCategory,
                    regStat?.SuccessCount ?? 0,
                    regStat?.WaitingCount ?? 0);

                dto.RegisteredCount = registeredCount;
                dto.PairedCount = pairedCount;
                dto.MatchesCount = liveMatchesCount;

                return dto;
            }).ToList();

            var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
            var hasNextPage = page < totalPages;

            return Ok(new
            {
                page,
                pageSize,
                total,
                totalPages,
                hasNextPage,
                items
            });
        }
    }
}
