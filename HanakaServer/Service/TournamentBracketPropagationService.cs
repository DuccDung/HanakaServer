using HanakaServer.Data;
using HanakaServer.Helpers;
using HanakaServer.Models;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Services
{
    public interface ITournamentBracketPropagationService
    {
        Task PropagateFromMatchAsync(long matchId, CancellationToken ct = default);
        Task PropagateFromGroupAsync(long groupId, CancellationToken ct = default);
        Task RecalculateMatchSlotsAsync(long matchId, CancellationToken ct = default);
    }

    public sealed class TournamentBracketPropagationService : ITournamentBracketPropagationService
    {
        private readonly PickleballDbContext _db;
        private readonly ITournamentStandingsService _standingsService;
        private readonly ILogger<TournamentBracketPropagationService> _logger;

        public TournamentBracketPropagationService(
            PickleballDbContext db,
            ITournamentStandingsService standingsService,
            ILogger<TournamentBracketPropagationService> logger)
        {
            _db = db;
            _standingsService = standingsService;
            _logger = logger;
        }

        public async Task PropagateFromMatchAsync(long matchId, CancellationToken ct = default)
        {
            var source = await _db.TournamentGroupMatches.AsNoTracking()
                .FirstOrDefaultAsync(x => x.MatchId == matchId, ct);

            if (source == null
                || !source.IsCompleted
                || !source.WinnerRegistrationId.HasValue
                || !source.Team1RegistrationId.HasValue
                || !source.Team2RegistrationId.HasValue)
            {
                return;
            }

            var winnerId = source.WinnerRegistrationId.Value;
            var loserId = ResolveLoser(source);
            if (!loserId.HasValue)
                return;

            var targets = await _db.TournamentGroupMatches
                .Where(x =>
                    (x.Team1SourceMatchId == matchId
                        && (x.Team1SourceType == MatchSourceTypes.WinnerMatch || x.Team1SourceType == MatchSourceTypes.LoserMatch))
                    || (x.Team2SourceMatchId == matchId
                        && (x.Team2SourceType == MatchSourceTypes.WinnerMatch || x.Team2SourceType == MatchSourceTypes.LoserMatch)))
                .ToListAsync(ct);

            foreach (var target in targets)
            {
                if (target.IsCompleted)
                {
                    _logger.LogWarning(
                        "Skip propagating from match {SourceMatchId} to completed target match {TargetMatchId}.",
                        matchId,
                        target.MatchId);
                    continue;
                }

                var originalTeam1 = target.Team1RegistrationId;
                var originalTeam2 = target.Team2RegistrationId;

                if (target.Team1SourceMatchId == matchId)
                    target.Team1RegistrationId = target.Team1SourceType == MatchSourceTypes.WinnerMatch ? winnerId : loserId.Value;

                if (target.Team2SourceMatchId == matchId)
                    target.Team2RegistrationId = target.Team2SourceType == MatchSourceTypes.WinnerMatch ? winnerId : loserId.Value;

                if (HasDuplicateResolvedTeams(target))
                {
                    target.Team1RegistrationId = originalTeam1;
                    target.Team2RegistrationId = originalTeam2;
                    _logger.LogWarning(
                        "Skip propagating to match {TargetMatchId} because both slots resolve to registration {RegistrationId}.",
                        target.MatchId,
                        target.Team1RegistrationId);
                    continue;
                }

                target.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(ct);
        }

        public async Task PropagateFromGroupAsync(long groupId, CancellationToken ct = default)
        {
            if (!await _standingsService.IsGroupCompletedAsync(groupId, ct))
                return;

            var standings = await _standingsService.GetGroupStandingsAsync(groupId, ct);
            if (standings.Count == 0)
                return;

            var standingByRank = standings.ToDictionary(x => x.Rank, x => x.RegistrationId);

            var targets = await _db.TournamentGroupMatches
                .Where(x =>
                    (x.Team1SourceType == MatchSourceTypes.GroupRank && x.Team1SourceGroupId == groupId)
                    || (x.Team2SourceType == MatchSourceTypes.GroupRank && x.Team2SourceGroupId == groupId))
                .ToListAsync(ct);

            foreach (var target in targets)
            {
                if (target.IsCompleted)
                {
                    _logger.LogWarning(
                        "Skip propagating from group {GroupId} to completed target match {TargetMatchId}.",
                        groupId,
                        target.MatchId);
                    continue;
                }

                var originalTeam1 = target.Team1RegistrationId;
                var originalTeam2 = target.Team2RegistrationId;

                if (target.Team1SourceGroupId == groupId
                    && target.Team1SourceRank.HasValue
                    && standingByRank.TryGetValue(target.Team1SourceRank.Value, out var team1Id))
                {
                    target.Team1RegistrationId = team1Id;
                }

                if (target.Team2SourceGroupId == groupId
                    && target.Team2SourceRank.HasValue
                    && standingByRank.TryGetValue(target.Team2SourceRank.Value, out var team2Id))
                {
                    target.Team2RegistrationId = team2Id;
                }

                if (HasDuplicateResolvedTeams(target))
                {
                    target.Team1RegistrationId = originalTeam1;
                    target.Team2RegistrationId = originalTeam2;
                    _logger.LogWarning(
                        "Skip propagating group rank to match {TargetMatchId} because both slots resolve to registration {RegistrationId}.",
                        target.MatchId,
                        target.Team1RegistrationId);
                    continue;
                }

                target.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(ct);
        }

        public async Task RecalculateMatchSlotsAsync(long matchId, CancellationToken ct = default)
        {
            var target = await _db.TournamentGroupMatches
                .FirstOrDefaultAsync(x => x.MatchId == matchId, ct);

            if (target == null || target.IsCompleted)
                return;

            target.Team1RegistrationId = await ResolveSlotAsync(target, slotNumber: 1, ct);
            target.Team2RegistrationId = await ResolveSlotAsync(target, slotNumber: 2, ct);

            if (HasDuplicateResolvedTeams(target))
            {
                _logger.LogWarning(
                    "Recalculate skipped duplicate teams for match {MatchId}, registration {RegistrationId}.",
                    target.MatchId,
                    target.Team1RegistrationId);
                return;
            }

            target.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        private async Task<long?> ResolveSlotAsync(TournamentGroupMatch match, int slotNumber, CancellationToken ct)
        {
            var sourceType = MatchSourceTypes.Normalize(slotNumber == 1 ? match.Team1SourceType : match.Team2SourceType);
            var registrationId = slotNumber == 1 ? match.Team1RegistrationId : match.Team2RegistrationId;
            var sourceMatchId = slotNumber == 1 ? match.Team1SourceMatchId : match.Team2SourceMatchId;
            var sourceGroupId = slotNumber == 1 ? match.Team1SourceGroupId : match.Team2SourceGroupId;
            var sourceRank = slotNumber == 1 ? match.Team1SourceRank : match.Team2SourceRank;

            if (sourceType == MatchSourceTypes.Registration)
                return registrationId;

            if (sourceType == MatchSourceTypes.WinnerMatch || sourceType == MatchSourceTypes.LoserMatch)
            {
                if (!sourceMatchId.HasValue)
                    return null;

                var source = await _db.TournamentGroupMatches.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MatchId == sourceMatchId.Value, ct);

                if (source == null || !source.IsCompleted || !source.WinnerRegistrationId.HasValue)
                    return null;

                return sourceType == MatchSourceTypes.WinnerMatch
                    ? source.WinnerRegistrationId
                    : ResolveLoser(source);
            }

            if (sourceType == MatchSourceTypes.GroupRank)
            {
                if (!sourceGroupId.HasValue || !sourceRank.HasValue)
                    return null;

                if (!await _standingsService.IsGroupCompletedAsync(sourceGroupId.Value, ct))
                    return null;

                var standings = await _standingsService.GetGroupStandingsAsync(sourceGroupId.Value, ct);
                return standings.FirstOrDefault(x => x.Rank == sourceRank.Value)?.RegistrationId;
            }

            return null;
        }

        private static long? ResolveLoser(TournamentGroupMatch match)
        {
            if (!match.WinnerRegistrationId.HasValue
                || !match.Team1RegistrationId.HasValue
                || !match.Team2RegistrationId.HasValue)
            {
                return null;
            }

            if (match.WinnerRegistrationId.Value == match.Team1RegistrationId.Value)
                return match.Team2RegistrationId.Value;

            if (match.WinnerRegistrationId.Value == match.Team2RegistrationId.Value)
                return match.Team1RegistrationId.Value;

            return null;
        }

        private static bool HasDuplicateResolvedTeams(TournamentGroupMatch match)
        {
            return match.Team1RegistrationId.HasValue
                && match.Team2RegistrationId.HasValue
                && match.Team1RegistrationId.Value == match.Team2RegistrationId.Value;
        }
    }
}
