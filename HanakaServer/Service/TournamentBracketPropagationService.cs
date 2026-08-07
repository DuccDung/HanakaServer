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
        Task<BracketPropagationReconcileResult> ReconcileTournamentAsync(long tournamentId, CancellationToken ct = default);
    }

    public sealed class BracketPropagationReconcileResult
    {
        public long TournamentId { get; set; }
        public int PassCount { get; set; }
        public int CompletedSourceCount { get; set; }
        public int ResolvedSlotCount { get; set; }
        public int UnresolvedSlotCount { get; set; }
        public List<string> UnresolvedSlots { get; set; } = [];
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
                || !source.WinnerRegistrationId.HasValue)
            {
                return;
            }

            var winnerId = source.WinnerRegistrationId.Value;
            var loserId = ResolveLoser(source);
            var isBye = source.CompletionReason == MatchCompletionReasons.Bye;
            if (!isBye && !loserId.HasValue)
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
                {
                    target.Team1RegistrationId = target.Team1SourceType == MatchSourceTypes.WinnerMatch
                        ? winnerId
                        : loserId;
                }

                if (target.Team2SourceMatchId == matchId)
                {
                    target.Team2RegistrationId = target.Team2SourceType == MatchSourceTypes.WinnerMatch
                        ? winnerId
                        : loserId;
                }

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

                ResetPendingScoreIfParticipantsChanged(target, originalTeam1, originalTeam2);
                target.UpdatedAt = DateTime.UtcNow;

                if (!target.IsCompleted
                    && target.Team1SourceType == MatchSourceTypes.Bye
                    && target.Team2RegistrationId.HasValue)
                {
                    CompleteBye(target, target.Team2RegistrationId.Value);
                }
                else if (!target.IsCompleted
                         && target.Team2SourceType == MatchSourceTypes.Bye
                         && target.Team1RegistrationId.HasValue)
                {
                    CompleteBye(target, target.Team1RegistrationId.Value);
                }
            }

            await _db.SaveChangesAsync(ct);

            foreach (var target in targets)
            {
                if (target.Team1SourceMatchId == matchId
                    && target.Team1SourceType is MatchSourceTypes.WinnerMatch or MatchSourceTypes.LoserMatch
                    && !target.Team1RegistrationId.HasValue)
                {
                    _logger.LogWarning(
                        "Propagation incomplete from match {SourceMatchId} to match {TargetMatchId} slot 1 ({SourceType}).",
                        matchId,
                        target.MatchId,
                        target.Team1SourceType);
                }

                if (target.Team2SourceMatchId == matchId
                    && target.Team2SourceType is MatchSourceTypes.WinnerMatch or MatchSourceTypes.LoserMatch
                    && !target.Team2RegistrationId.HasValue)
                {
                    _logger.LogWarning(
                        "Propagation incomplete from match {SourceMatchId} to match {TargetMatchId} slot 2 ({SourceType}).",
                        matchId,
                        target.MatchId,
                        target.Team2SourceType);
                }
            }

            foreach (var completedByeId in targets
                         .Where(x => x.CompletionReason == MatchCompletionReasons.Bye)
                         .Select(x => x.MatchId)
                         .Distinct())
            {
                await PropagateFromMatchAsync(completedByeId, ct);
            }
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

                ResetPendingScoreIfParticipantsChanged(target, originalTeam1, originalTeam2);
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

            var originalTeam1 = target.Team1RegistrationId;
            var originalTeam2 = target.Team2RegistrationId;
            target.Team1RegistrationId = await ResolveSlotAsync(target, slotNumber: 1, ct);
            target.Team2RegistrationId = await ResolveSlotAsync(target, slotNumber: 2, ct);

            if (HasDuplicateResolvedTeams(target))
            {
                target.Team1RegistrationId = originalTeam1;
                target.Team2RegistrationId = originalTeam2;
                _logger.LogWarning(
                    "Recalculate skipped duplicate teams for match {MatchId}, registration {RegistrationId}.",
                    target.MatchId,
                    target.Team1RegistrationId);
                return;
            }

            ResetPendingScoreIfParticipantsChanged(target, originalTeam1, originalTeam2);
            target.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        public async Task<BracketPropagationReconcileResult> ReconcileTournamentAsync(
            long tournamentId,
            CancellationToken ct = default)
        {
            var applicationId = await _db.TournamentBracketApplications.AsNoTracking()
                .Where(x => x.TournamentId == tournamentId && x.IsActive)
                .Select(x => (long?)x.TournamentBracketApplicationId)
                .FirstOrDefaultAsync(ct);
            if (!applicationId.HasValue)
                return new BracketPropagationReconcileResult { TournamentId = tournamentId };

            var initialUnresolved = await CountUnresolvedSlotsAsync(applicationId.Value, ct);
            var previousUnresolved = int.MaxValue;
            var passCount = 0;
            var maximumPasses = Math.Max(1, await _db.TournamentGroupMatches.AsNoTracking()
                .CountAsync(x => x.BracketApplicationId == applicationId.Value, ct));

            while (passCount < maximumPasses)
            {
                passCount++;
                var completedMatchIds = await _db.TournamentGroupMatches.AsNoTracking()
                    .Where(x => x.BracketApplicationId == applicationId.Value
                                && x.IsCompleted
                                && x.WinnerRegistrationId.HasValue)
                    .Select(x => x.MatchId)
                    .ToListAsync(ct);
                foreach (var matchId in completedMatchIds)
                    await PropagateFromMatchAsync(matchId, ct);

                var groupIds = await _db.TournamentRoundGroups.AsNoTracking()
                    .Where(x => x.BracketApplicationId == applicationId.Value)
                    .Select(x => x.TournamentRoundGroupId)
                    .ToListAsync(ct);
                foreach (var groupId in groupIds)
                    await PropagateFromGroupAsync(groupId, ct);

                var pendingMatchIds = await _db.TournamentGroupMatches.AsNoTracking()
                    .Where(x => x.BracketApplicationId == applicationId.Value && !x.IsCompleted)
                    .Select(x => x.MatchId)
                    .ToListAsync(ct);
                foreach (var matchId in pendingMatchIds)
                    await RecalculateMatchSlotsAsync(matchId, ct);

                var currentUnresolved = await CountUnresolvedSlotsAsync(applicationId.Value, ct);
                if (currentUnresolved == 0 || currentUnresolved >= previousUnresolved)
                    break;
                previousUnresolved = currentUnresolved;
            }

            var unresolvedMatches = await _db.TournamentGroupMatches.AsNoTracking()
                .Where(x => x.BracketApplicationId == applicationId.Value && !x.IsCompleted)
                .Select(x => new
                {
                    x.MatchId,
                    x.Team1SourceType,
                    x.Team1RegistrationId,
                    x.Team2SourceType,
                    x.Team2RegistrationId
                })
                .ToListAsync(ct);
            var unresolved = unresolvedMatches.SelectMany(x => new[]
                {
                    IsUnresolved(x.Team1SourceType, x.Team1RegistrationId) ? $"Match #{x.MatchId} · slot 1" : null,
                    IsUnresolved(x.Team2SourceType, x.Team2RegistrationId) ? $"Match #{x.MatchId} · slot 2" : null
                })
                .Where(x => x != null)
                .Select(x => x!)
                .ToList();
            var completedSourceCount = await _db.TournamentGroupMatches.AsNoTracking()
                .CountAsync(x => x.BracketApplicationId == applicationId.Value
                                 && x.IsCompleted
                                 && x.WinnerRegistrationId.HasValue, ct);
            var result = new BracketPropagationReconcileResult
            {
                TournamentId = tournamentId,
                PassCount = passCount,
                CompletedSourceCount = completedSourceCount,
                ResolvedSlotCount = Math.Max(0, initialUnresolved - unresolved.Count),
                UnresolvedSlotCount = unresolved.Count,
                UnresolvedSlots = unresolved
            };

            if (unresolved.Count == 0)
            {
                _logger.LogInformation(
                    "Bracket propagation reconcile completed for tournament {TournamentId} in {PassCount} pass(es); {ResolvedSlotCount} slot(s) repaired.",
                    tournamentId, passCount, result.ResolvedSlotCount);
            }
            else
            {
                _logger.LogWarning(
                    "Bracket propagation reconcile for tournament {TournamentId} ended with {UnresolvedSlotCount} unresolved slot(s): {UnresolvedSlots}.",
                    tournamentId, unresolved.Count, string.Join(", ", unresolved));
            }
            return result;
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

            if (sourceType == MatchSourceTypes.Bye)
                return null;

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

        private async Task<int> CountUnresolvedSlotsAsync(long applicationId, CancellationToken ct)
        {
            var matches = await _db.TournamentGroupMatches.AsNoTracking()
                .Where(x => x.BracketApplicationId == applicationId && !x.IsCompleted)
                .Select(x => new
                {
                    x.Team1SourceType,
                    x.Team1RegistrationId,
                    x.Team2SourceType,
                    x.Team2RegistrationId
                })
                .ToListAsync(ct);
            return matches.Sum(x =>
                (IsUnresolved(x.Team1SourceType, x.Team1RegistrationId) ? 1 : 0)
                + (IsUnresolved(x.Team2SourceType, x.Team2RegistrationId) ? 1 : 0));
        }

        private static bool IsUnresolved(string? sourceType, long? registrationId)
        {
            var normalized = MatchSourceTypes.Normalize(sourceType);
            return (normalized is MatchSourceTypes.WinnerMatch
                or MatchSourceTypes.LoserMatch
                or MatchSourceTypes.GroupRank)
                && !registrationId.HasValue;
        }

        internal static void ResetPendingScoreIfParticipantsChanged(
            TournamentGroupMatch match,
            long? originalTeam1,
            long? originalTeam2)
        {
            if (match.IsCompleted
                || (match.Team1RegistrationId == originalTeam1
                    && match.Team2RegistrationId == originalTeam2))
            {
                return;
            }

            match.ScoreTeam1 = 0;
            match.ScoreTeam2 = 0;
            match.WinnerRegistrationId = null;
            match.CompletionReason = null;
        }

        private static void CompleteBye(TournamentGroupMatch match, long winnerRegistrationId)
        {
            match.IsCompleted = true;
            match.WinnerRegistrationId = winnerRegistrationId;
            match.ScoreTeam1 = 0;
            match.ScoreTeam2 = 0;
            match.CompletionReason = MatchCompletionReasons.Bye;
            match.UpdatedAt = DateTime.UtcNow;
        }
    }
}
