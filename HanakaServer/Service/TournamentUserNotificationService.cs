using System.Globalization;
using System.Text.Json;
using HanakaServer.Data;
using HanakaServer.Models;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Services
{
    public class TournamentUserNotificationService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly PickleballDbContext _db;
        private readonly RealtimeHub _realtimeHub;

        public TournamentUserNotificationService(PickleballDbContext db, RealtimeHub realtimeHub)
        {
            _db = db;
            _realtimeHub = realtimeHub;
        }

        public async Task NotifyMatchWinnerAsync(long matchId, CancellationToken ct = default)
        {
            var match = await _db.TournamentGroupMatches
                .AsNoTracking()
                .Include(x => x.Tournament)
                .Include(x => x.TournamentRoundGroup)
                    .ThenInclude(x => x.TournamentRoundMap)
                .Include(x => x.Team1Registration)
                .Include(x => x.Team2Registration)
                .Include(x => x.RefereeUser)
                .FirstOrDefaultAsync(x => x.MatchId == matchId, ct);

            if (match == null || !match.IsCompleted || !match.WinnerRegistrationId.HasValue)
            {
                return;
            }

            var winner = match.WinnerRegistrationId == match.Team1RegistrationId
                ? match.Team1Registration
                : match.Team2Registration;
            var loser = winner.RegistrationId == match.Team1RegistrationId
                ? match.Team2Registration
                : match.Team1Registration;

            var existingNotifications = await _db.UserNotifications
                .Where(x =>
                    x.NotificationType == "MATCH_WIN" &&
                    x.RefType == "MATCH" &&
                    x.RefId == match.MatchId)
                .ToListAsync(ct);

            if (existingNotifications.Count > 0)
            {
                _db.UserNotifications.RemoveRange(existingNotifications);
            }

            var userIds = GetRegistrationUserIds(winner).Distinct().ToList();
            if (userIds.Count == 0)
            {
                await SaveAndDispatchAsync(new List<NotificationDelivery>(), ct);
                return;
            }

            var deliveries = new List<NotificationDelivery>();
            var now = DateTime.UtcNow;
            var scoreText = $"{match.ScoreTeam1}-{match.ScoreTeam2}";
            var round = match.TournamentRoundGroup.TournamentRoundMap;
            var title = $"\u0043h\u00fa\u0063 \u006d\u1eeb\u006e\u0067 \u0062\u1ea1\u006e \u0111\u00e3 \u0074\u0068\u1eaf\u006e\u0067 \u0074\u0072\u1ead\u006e \u0023{match.MatchId}";
            var body = BuildMatchWinBody(match, winner, loser, scoreText);

            foreach (var userId in userIds)
            {
                var details = new
                {
                    notificationKind = "matchWin",
                    tournamentId = match.TournamentId,
                    tournamentTitle = match.Tournament.Title,
                    matchId = match.MatchId,
                    tournamentRoundGroupId = match.TournamentRoundGroupId,
                    roundMapId = round.TournamentRoundMapId,
                    roundKey = round.RoundKey,
                    roundLabel = round.RoundLabel,
                    groupName = match.TournamentRoundGroup.GroupName,
                    winnerRegistrationId = winner.RegistrationId,
                    winnerRegCode = winner.RegCode,
                    winnerTeamText = BuildTeamText(match.Tournament.GameType, winner),
                    loserRegistrationId = loser.RegistrationId,
                    loserRegCode = loser.RegCode,
                    loserTeamText = BuildTeamText(match.Tournament.GameType, loser),
                    team1RegistrationId = match.Team1RegistrationId,
                    team1RegCode = match.Team1Registration.RegCode,
                    team1Text = BuildTeamText(match.Tournament.GameType, match.Team1Registration),
                    team2RegistrationId = match.Team2RegistrationId,
                    team2RegCode = match.Team2Registration.RegCode,
                    team2Text = BuildTeamText(match.Tournament.GameType, match.Team2Registration),
                    scoreTeam1 = match.ScoreTeam1,
                    scoreTeam2 = match.ScoreTeam2,
                    scoreText,
                    winnerSide = match.WinnerRegistrationId == match.Team1RegistrationId ? "1" : "2",
                    startAt = match.StartAt,
                    startAtText = FormatVietnamDateTime(match.StartAt),
                    addressText = match.AddressText,
                    courtText = match.CourtText,
                    videoUrl = match.VideoUrl,
                    refereeUserId = match.RefereeUserId,
                    refereeName = match.RefereeUser?.FullName,
                    createdAt = now
                };

                deliveries.Add(CreateDelivery(
                    userId,
                    "MATCH_WIN",
                    title,
                    body,
                    "MATCH",
                    match.MatchId,
                    now,
                    details));
            }

            await SaveAndDispatchAsync(deliveries, ct);
        }

        public async Task NotifyTournamentAwardsAndRatingsAsync(long tournamentId, CancellationToken ct = default)
        {
            var tournament = await _db.Tournaments
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TournamentId == tournamentId, ct);

            if (tournament == null)
            {
                return;
            }

            var previousTournamentNotifications = await _db.UserNotifications
                .Where(x =>
                    x.RefId == tournamentId &&
                    (
                        (x.NotificationType == "TOURNAMENT_PRIZE" && x.RefType == "TOURNAMENT_PRIZE") ||
                        (x.NotificationType == "RATING_UPDATED" && x.RefType == "TOURNAMENT_RATING")
                    ))
                .ToListAsync(ct);

            if (previousTournamentNotifications.Count > 0)
            {
                _db.UserNotifications.RemoveRange(previousTournamentNotifications);
            }

            var prizes = await _db.TournamentPrizes
                .AsNoTracking()
                .Include(x => x.Registration)
                .Where(x => x.TournamentId == tournamentId && x.IsConfirmed && x.RegistrationId.HasValue)
                .OrderBy(x => x.PrizeType == "FIRST" ? 1 : x.PrizeType == "SECOND" ? 2 : 3)
                .ThenBy(x => x.PrizeOrder)
                .ToListAsync(ct);

            if (prizes.Count == 0)
            {
                await SaveAndDispatchAsync(new List<NotificationDelivery>(), ct);
                return;
            }

            var prizeUserRows = prizes
                .Where(x => x.Registration != null)
                .SelectMany(prize => GetRegistrationUserIds(prize.Registration!).Select(userId => new
                {
                    UserId = userId,
                    Prize = prize,
                    Registration = prize.Registration!
                }))
                .GroupBy(x => new { x.UserId, x.Prize.TournamentPrizeId })
                .Select(x => x.First())
                .ToList();

            if (prizeUserRows.Count == 0)
            {
                await SaveAndDispatchAsync(new List<NotificationDelivery>(), ct);
                return;
            }

            var userIds = prizeUserRows.Select(x => x.UserId).Distinct().ToList();
            var ratingHistories = await _db.UserRatingHistories
                .AsNoTracking()
                .Where(x => x.TournamentId == tournamentId && userIds.Contains(x.UserId))
                .OrderBy(x => x.RatedAt)
                .ThenBy(x => x.RatingHistoryId)
                .ToListAsync(ct);

            var ratingByUser = ratingHistories
                .GroupBy(x => x.UserId)
                .ToDictionary(x => x.Key, x => x.Last());

            var previousRatings = await _db.UserRatingHistories
                .AsNoTracking()
                .Where(x => userIds.Contains(x.UserId) && x.TournamentId != tournamentId)
                .GroupBy(x => x.UserId)
                .Select(g => g
                    .OrderByDescending(x => x.RatedAt)
                    .ThenByDescending(x => x.RatingHistoryId)
                    .FirstOrDefault())
                .ToListAsync(ct);

            var previousRatingMap = previousRatings
                .Where(x => x != null)
                .ToDictionary(x => x!.UserId, x => x!);

            var deliveries = new List<NotificationDelivery>();
            var ratingDeliveredUsers = new HashSet<long>();
            var now = DateTime.UtcNow;
            var gameType = (tournament.GameType ?? string.Empty).Trim().ToUpperInvariant();
            var isDoubleTournament = gameType == "DOUBLE" || gameType == "MIXED";

            foreach (var row in prizeUserRows)
            {
                var prize = row.Prize;
                var reg = row.Registration;
                var prizeLabel = GetPrizeLabel(prize.PrizeType);
                var prizeTitle = $"\u0043h\u00fa\u0063 \u006d\u1eeb\u006e\u0067 \u0062\u1ea1\u006e \u0111\u1ea1\u0074 {prizeLabel}";
                var prizeBody = BuildPrizeBody(tournament, prize, reg, prizeLabel);
                var prizeDetails = new
                {
                    notificationKind = "tournamentPrize",
                    tournamentId = tournament.TournamentId,
                    tournamentTitle = tournament.Title,
                    tournamentPrizeId = prize.TournamentPrizeId,
                    prizeType = prize.PrizeType,
                    prizeLabel,
                    prizeOrder = prize.PrizeOrder,
                    registrationId = reg.RegistrationId,
                    regCode = reg.RegCode,
                    teamText = BuildTeamText(tournament.GameType, reg),
                    player1UserId = reg.Player1UserId,
                    player1Name = reg.Player1Name,
                    player2UserId = reg.Player2UserId,
                    player2Name = reg.Player2Name,
                    note = prize.Note,
                    createdAt = now
                };

                deliveries.Add(CreateDelivery(
                    row.UserId,
                    "TOURNAMENT_PRIZE",
                    prizeTitle,
                    prizeBody,
                    "TOURNAMENT_PRIZE",
                    tournament.TournamentId,
                    now,
                    prizeDetails));

                if (!ratingByUser.TryGetValue(row.UserId, out var ratingHistory) || !ratingDeliveredUsers.Add(row.UserId))
                {
                    continue;
                }

                var delta = GetRatingDelta(prize.PrizeType);
                if (delta <= 0)
                {
                    continue;
                }

                previousRatingMap.TryGetValue(row.UserId, out var previous);

                var newSingle = ratingHistory.RatingSingle ?? 0m;
                var newDouble = ratingHistory.RatingDouble ?? 0m;
                var oldSingle = previous?.RatingSingle ?? (isDoubleTournament ? newSingle : newSingle - delta);
                var oldDouble = previous?.RatingDouble ?? (isDoubleTournament ? newDouble - delta : newDouble);
                var ratingLabel = isDoubleTournament
                    ? "\u0111\u0069\u1ec3\u006d \u0111\u00f4\u0069"
                    : "\u0111\u0069\u1ec3\u006d \u0111\u01a1\u006e";
                var ratingBefore = isDoubleTournament ? oldDouble : oldSingle;
                var ratingAfter = isDoubleTournament ? newDouble : newSingle;
                var ratingTitle = $"\u0042\u1ea1\u006e \u0111\u01b0\u1ee3\u0063 \u0063\u1ed9\u006e\u0067 +{FormatDecimal(delta)} {ratingLabel}";
                var ratingBody = BuildRatingBody(tournament, prizeLabel, ratingLabel, delta, ratingBefore, ratingAfter);
                var ratingDetails = new
                {
                    notificationKind = "ratingUpdated",
                    tournamentId = tournament.TournamentId,
                    tournamentTitle = tournament.Title,
                    ratingHistoryId = ratingHistory.RatingHistoryId,
                    prizeType = prize.PrizeType,
                    prizeLabel,
                    ratingType = isDoubleTournament ? "DOUBLE" : "SINGLE",
                    ratingLabel,
                    ratingDelta = delta,
                    ratingBefore,
                    ratingAfter,
                    ratingSingle = newSingle,
                    ratingDouble = newDouble,
                    ratedAt = ratingHistory.RatedAt,
                    note = ratingHistory.Note,
                    registrationId = reg.RegistrationId,
                    regCode = reg.RegCode,
                    teamText = BuildTeamText(tournament.GameType, reg),
                    createdAt = now
                };

                deliveries.Add(CreateDelivery(
                    row.UserId,
                    "RATING_UPDATED",
                    ratingTitle,
                    ratingBody,
                    "TOURNAMENT_RATING",
                    tournament.TournamentId,
                    now,
                    ratingDetails));
            }

            await SaveAndDispatchAsync(deliveries, ct);
        }

        private static NotificationDelivery CreateDelivery(
            long userId,
            string notificationType,
            string title,
            string body,
            string refType,
            long refId,
            DateTime createdAt,
            object details)
        {
            var notification = new UserNotification
            {
                UserId = userId,
                NotificationType = notificationType,
                Title = TrimToMax(title, 200),
                Body = TrimToMax(body, 1000),
                RefType = refType,
                RefId = refId,
                IsRead = false,
                CreatedAt = createdAt,
                PayloadJson = JsonSerializer.Serialize(details, JsonOptions)
            };

            return new NotificationDelivery(notification, details);
        }

        private async Task SaveAndDispatchAsync(List<NotificationDelivery> deliveries, CancellationToken ct)
        {
            if (deliveries.Count > 0)
            {
                _db.UserNotifications.AddRange(deliveries.Select(x => x.Notification));
            }

            if (!_db.ChangeTracker.HasChanges())
            {
                return;
            }

            await _db.SaveChangesAsync(ct);

            foreach (var delivery in deliveries)
            {
                var notification = delivery.Notification;

                await _realtimeHub.SendTournamentNotificationToUserAsync(
                    notification.UserId.ToString(CultureInfo.InvariantCulture),
                    new
                    {
                        notification.NotificationId,
                        notification.NotificationType,
                        notification.Title,
                        notification.Body,
                        notification.RefType,
                        notification.RefId,
                        notification.CreatedAt,
                        Details = delivery.Details
                    });
            }
        }

        private static List<long> GetRegistrationUserIds(TournamentRegistration registration)
        {
            var result = new List<long>();

            if (registration.Player1UserId.HasValue && registration.Player1UserId.Value > 0)
            {
                result.Add(registration.Player1UserId.Value);
            }

            if (registration.Player2UserId.HasValue &&
                registration.Player2UserId.Value > 0 &&
                registration.Player2UserId.Value != registration.Player1UserId)
            {
                result.Add(registration.Player2UserId.Value);
            }

            return result;
        }

        private static string BuildTeamText(string? gameType, TournamentRegistration registration)
        {
            var normalizedGameType = (gameType ?? "DOUBLE").Trim().ToUpperInvariant();
            var player1 = (registration.Player1Name ?? string.Empty).Trim();
            var player2 = (registration.Player2Name ?? string.Empty).Trim();

            if (normalizedGameType == "SINGLE" || string.IsNullOrWhiteSpace(player2))
            {
                return player1;
            }

            return $"{player1} & {player2}";
        }

        private static string BuildMatchWinBody(
            TournamentGroupMatch match,
            TournamentRegistration winner,
            TournamentRegistration loser,
            string scoreText)
        {
            var parts = new List<string>
            {
                $"\u0043h\u00fa\u0063 \u006d\u1eeb\u006e\u0067 \u0062\u1ea1\u006e \u0111\u00e3 \u0074\u0068\u1eaf\u006e\u0067 \u0074\u0072\u1ead\u006e \u0023{match.MatchId} \u0074\u1ea1\u0069 \u0067\u0069\u1ea3\u0069 \"{match.Tournament.Title}\" (\u0067\u0069\u1ea3\u0069 \u0023{match.TournamentId}).",
                $"\u0110\u1ed9\u0069 \u0074\u0068\u1eaf\u006e\u0067: \u0111\u0103\u006e\u0067 \u006b\u00fd \u0023{winner.RegistrationId} - \u006d\u00e3 {winner.RegCode} - {BuildTeamText(match.Tournament.GameType, winner)}.",
                $"\u0110\u1ed1\u0069 \u0074\u0068\u1ee7: \u0111\u0103\u006e\u0067 \u006b\u00fd \u0023{loser.RegistrationId} - \u006d\u00e3 {loser.RegCode} - {BuildTeamText(match.Tournament.GameType, loser)}.",
                $"\u0054\u1ef7 \u0073\u1ed1: {scoreText}.",
                $"\u0056\u00f2\u006e\u0067/\u0062\u1ea3\u006e\u0067: {match.TournamentRoundGroup.TournamentRoundMap.RoundKey} - {match.TournamentRoundGroup.GroupName} (\u0062\u1ea3\u006e\u0067 \u0023{match.TournamentRoundGroupId})."
            };

            if (match.StartAt.HasValue)
            {
                parts.Add($"\u0054\u0068\u1eddi \u0067\u0069\u0061\u006e: {FormatVietnamDateTime(match.StartAt)}.");
            }

            if (!string.IsNullOrWhiteSpace(match.CourtText))
            {
                parts.Add($"\u0053\u00e2\u006e: {match.CourtText!.Trim()}.");
            }

            if (!string.IsNullOrWhiteSpace(match.AddressText))
            {
                parts.Add($"\u0110\u1ecb\u0061 \u0111\u0069\u1ec3\u006d: {match.AddressText!.Trim()}.");
            }

            return string.Join(" ", parts);
        }

        private static string BuildPrizeBody(
            Tournament tournament,
            TournamentPrize prize,
            TournamentRegistration registration,
            string prizeLabel)
        {
            return
                $"\u0043h\u00fa\u0063 \u006d\u1eeb\u006e\u0067 \u0062\u1ea1\u006e \u0111\u1ea1\u0074 {prizeLabel} \u0074\u1ea1\u0069 \u0067\u0069\u1ea3\u0069 \"{tournament.Title}\" (\u0067\u0069\u1ea3\u0069 \u0023{tournament.TournamentId}). " +
                $"\u0110\u1ed9\u0069 \u0111\u0103\u006e\u0067 \u006b\u00fd \u0023{registration.RegistrationId} - \u006d\u00e3 {registration.RegCode} - {BuildTeamText(tournament.GameType, registration)}. " +
                $"\u0053\u006c\u006f\u0074 \u0067\u0069\u1ea3\u0069 \u0074\u0068\u01b0\u1edf\u006e\u0067: {prize.PrizeType} \u0023{prize.PrizeOrder}.";
        }

        private static string BuildRatingBody(
            Tournament tournament,
            string prizeLabel,
            string ratingLabel,
            decimal delta,
            decimal ratingBefore,
            decimal ratingAfter)
        {
            return
                $"\u0042\u1ea1\u006e \u0111\u01b0\u1ee3\u0063 \u0063\u1ed9\u006e\u0067 +{FormatDecimal(delta)} {ratingLabel} \u006e\u0068\u1edd {prizeLabel} \u0074\u1ea1\u0069 \u0067\u0069\u1ea3\u0069 \"{tournament.Title}\". " +
                $"\u0110\u0069\u1ec3\u006d \u0074\u0072\u00ec\u006e\u0068: {FormatDecimal(ratingBefore)} -> {FormatDecimal(ratingAfter)}.";
        }

        private static decimal GetRatingDelta(string? prizeType)
        {
            return prizeType switch
            {
                "FIRST" => 0.15m,
                "SECOND" => 0.10m,
                "THIRD" => 0.05m,
                _ => 0m
            };
        }

        private static string GetPrizeLabel(string? prizeType)
        {
            return prizeType switch
            {
                "FIRST" => "\u0047\u0069\u1ea3\u0069 \u006e\u0068\u1ea5\u0074",
                "SECOND" => "\u0047\u0069\u1ea3\u0069 \u006e\u0068\u00ec",
                "THIRD" => "\u0047\u0069\u1ea3\u0069 \u0062\u0061",
                _ => "\u0047\u0069\u1ea3\u0069 \u0074\u0068\u01b0\u1edf\u006e\u0067"
            };
        }

        private static string FormatDecimal(decimal value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string FormatVietnamDateTime(DateTime? value)
        {
            if (!value.HasValue)
            {
                return string.Empty;
            }

            TimeZoneInfo vietnamTimeZone;
            try
            {
                vietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            }
            catch
            {
                vietnamTimeZone = TimeZoneInfo.Local;
            }

            var source = value.Value.Kind == DateTimeKind.Utc
                ? value.Value
                : DateTime.SpecifyKind(value.Value, DateTimeKind.Local);
            var vietnamTime = source.Kind == DateTimeKind.Utc
                ? TimeZoneInfo.ConvertTimeFromUtc(source, vietnamTimeZone)
                : TimeZoneInfo.ConvertTime(source, vietnamTimeZone);

            return vietnamTime.ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture);
        }

        private static string TrimToMax(string value, int maxLength)
        {
            var text = value.Trim();
            return text.Length <= maxLength ? text : text[..maxLength];
        }

        private sealed record NotificationDelivery(UserNotification Notification, object Details);
    }
}
