using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using HanakaServer.Data;
using HanakaServer.Dtos.Brackets;
using HanakaServer.Helpers;
using HanakaServer.Models;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Services.Brackets;

public interface ITournamentBracketApplicationService
{
    Task<IReadOnlyList<BracketTemplateListItemDto>> GetApplicableTemplatesAsync(long tournamentId, CancellationToken ct);
    Task<BracketOperationResult<IReadOnlyList<TournamentBracketSeedDto>>> GetEligibleRegistrationsAsync(long tournamentId, CancellationToken ct);
    Task<BracketOperationResult<TournamentBracketPreviewDto>> PreviewAsync(long tournamentId, TournamentBracketPreviewRequest request, CancellationToken ct);
    Task<BracketOperationResult<TournamentBracketApplicationDto>> ApplyAsync(long tournamentId, ApplyTournamentBracketRequest request, long? userId, CancellationToken ct);
    Task<TournamentBracketApplicationDto?> GetActiveApplicationAsync(long tournamentId, CancellationToken ct);
    Task<IReadOnlyList<TournamentBracketApplicationDto>> GetApplicationHistoryAsync(long tournamentId, CancellationToken ct);
    Task<BracketOperationResult<bool>> SetRegistrationLockAsync(long tournamentId, bool locked, long? userId, CancellationToken ct);
    Task<BracketOperationResult<bool>> ResetAsync(long tournamentId, ResetTournamentBracketRequest request, long? userId, CancellationToken ct);
}

public sealed class TournamentBracketApplicationService : ITournamentBracketApplicationService
{
    internal const long JavaScriptMaxSafeInteger = 9_007_199_254_740_991L;

    private readonly PickleballDbContext _db;
    private readonly IBracketTemplateService _templateService;
    private readonly IBracketTemplateValidationService _validator;
    private readonly ILogger<TournamentBracketApplicationService> _logger;

    public TournamentBracketApplicationService(
        PickleballDbContext db,
        IBracketTemplateService templateService,
        IBracketTemplateValidationService validator,
        ILogger<TournamentBracketApplicationService> logger)
    {
        _db = db;
        _templateService = templateService;
        _validator = validator;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BracketTemplateListItemDto>> GetApplicableTemplatesAsync(long tournamentId, CancellationToken ct)
    {
        var tournament = await _db.Tournaments.AsNoTracking()
            .Where(x => x.TournamentId == tournamentId && !x.Remove)
            .Select(x => new { x.TournamentId, x.RegistrationFeeAmount })
            .FirstOrDefaultAsync(ct);
        if (tournament == null)
            return [];

        var teamCount = await EligibleRegistrationsQuery(tournamentId, tournament.RegistrationFeeAmount)
            .CountAsync(ct);
        var page = await _templateService.ListAsync(
            null, BracketTemplateStatuses.Published, null, 1, 100, ct);
        foreach (var template in page.Items)
        {
            template.EligibleTeamCount = teamCount;
            if (!template.MinimumTeams.HasValue || !template.SeedCapacity.HasValue)
            {
                template.IsApplicable = false;
                template.InapplicableReason = "Version chưa có cấu hình số đội.";
            }
            else if (teamCount < template.MinimumTeams.Value)
            {
                template.IsApplicable = false;
                template.InapplicableReason = $"Cần tối thiểu {template.MinimumTeams.Value} đội, hiện có {teamCount}.";
            }
            else if (teamCount > template.SeedCapacity.Value)
            {
                template.IsApplicable = false;
                template.InapplicableReason = $"Sức chứa tối đa {template.SeedCapacity.Value} đội, hiện có {teamCount}.";
            }
        }

        return page.Items;
    }

    public async Task<BracketOperationResult<IReadOnlyList<TournamentBracketSeedDto>>> GetEligibleRegistrationsAsync(
        long tournamentId,
        CancellationToken ct)
    {
        var tournament = await _db.Tournaments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TournamentId == tournamentId && !x.Remove, ct);
        if (tournament == null)
            return BracketOperationResult<IReadOnlyList<TournamentBracketSeedDto>>.Fail("TOURNAMENT_NOT_FOUND", "Không tìm thấy giải đấu.");

        var registrations = await EligibleRegistrationsQuery(tournamentId, tournament.RegistrationFeeAmount)
            .OrderBy(x => x.RegTime ?? x.CreatedAt)
            .ThenBy(x => x.RegIndex)
            .ThenBy(x => x.RegistrationId)
            .ToListAsync(ct);

        return BracketOperationResult<IReadOnlyList<TournamentBracketSeedDto>>.Ok(
            registrations.Select((x, index) => MapRegistrationSeed(x, index + 1, index + 1, false, false)).ToList());
    }

    public async Task<BracketOperationResult<TournamentBracketPreviewDto>> PreviewAsync(
        long tournamentId,
        TournamentBracketPreviewRequest request,
        CancellationToken ct)
    {
        var tournament = await _db.Tournaments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TournamentId == tournamentId && !x.Remove, ct);
        if (tournament == null)
            return BracketOperationResult<TournamentBracketPreviewDto>.Fail("TOURNAMENT_NOT_FOUND", "Không tìm thấy giải đấu.");

        var graph = await _templateService.GetGraphAsync(request.BracketTemplateVersionId, ct);
        if (graph == null)
            return BracketOperationResult<TournamentBracketPreviewDto>.Fail("VERSION_NOT_FOUND", "Không tìm thấy template version.");
        if (graph.Status != BracketTemplateStatuses.Published)
            return BracketOperationResult<TournamentBracketPreviewDto>.Fail("VERSION_NOT_PUBLISHED", "Chỉ version đã publish mới được áp dụng.");

        var template = await _db.BracketTemplates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.BracketTemplateId == graph.BracketTemplateId, ct);
        if (template == null || template.Status == BracketTemplateStatuses.Archived)
            return BracketOperationResult<TournamentBracketPreviewDto>.Fail("TEMPLATE_UNAVAILABLE", "Template không còn khả dụng.");

        var allSuccessfulCount = await _db.TournamentRegistrations.AsNoTracking()
            .CountAsync(x => x.TournamentId == tournamentId && x.Success, ct);
        var registrations = await EligibleRegistrationsQuery(tournamentId, tournament.RegistrationFeeAmount)
            .OrderBy(x => x.RegTime ?? x.CreatedAt)
            .ThenBy(x => x.RegIndex)
            .ThenBy(x => x.RegistrationId)
            .ToListAsync(ct);

        var teamCountValidation = ValidateTeamCount(
            registrations.Count,
            graph.MinimumTeams,
            graph.SeedCapacity);
        if (!teamCountValidation.Success)
            return BracketOperationResult<TournamentBracketPreviewDto>.Fail(teamCountValidation.ErrorCode!, teamCountValidation.Message!);

        var validation = _validator.Validate(graph);
        if (!validation.IsValid)
            return BracketOperationResult<TournamentBracketPreviewDto>.Fail("GRAPH_INVALID", "Template có lỗi cấu trúc và không thể áp dụng.");

        var seedingMethod = Normalize(request.SeedingMethod);
        if (string.IsNullOrWhiteSpace(seedingMethod))
            seedingMethod = graph.DefaultSeedingMethod;
        if (!IsSeedingMethod(seedingMethod))
            return BracketOperationResult<TournamentBracketPreviewDto>.Fail("SEEDING_INVALID", "Phương pháp seed không hợp lệ.");

        var seedResult = BuildSeeds(registrations, graph.SeedCapacity, seedingMethod, request.RandomSeed, request.SeedAssignments);
        if (!seedResult.Success)
            return BracketOperationResult<TournamentBracketPreviewDto>.Fail(seedResult.ErrorCode!, seedResult.Message!);

        var seeds = seedResult.Data!.Seeds;
        var randomSeed = seedResult.Data.RandomSeed;
        var hash = ComputePreviewHash(tournamentId, graph, seedingMethod, randomSeed, seeds);
        var seedByNumber = seeds.ToDictionary(x => x.SeedNumber);

        var doubleByeMatch = graph.Rounds
            .SelectMany(x => x.Groups)
            .SelectMany(x => x.Matches)
            .FirstOrDefault(match => match.Slots.Count == 2
                                     && match.Slots.All(slot => IsResolvedBye(slot, seedByNumber)));
        if (doubleByeMatch != null)
        {
            return BracketOperationResult<TournamentBracketPreviewDto>.Fail(
                "DOUBLE_BYE_MATCH",
                $"Trận {doubleByeMatch.MatchKey} có cả hai slot là BYE. Hãy đổi seed hoặc chọn template nhỏ hơn.");
        }

        if (allSuccessfulCount > registrations.Count)
        {
            validation.Issues.Add(new BracketValidationIssueDto
            {
                Severity = "WARNING",
                Code = "REGISTRATION_EXCLUDED",
                Message = $"Có {allSuccessfulCount - registrations.Count} đăng ký thành công chưa đủ điều kiện thanh toán nên không được đưa vào bracket."
            });
        }
        if (!tournament.RegistrationLockedAt.HasValue)
        {
            validation.Issues.Add(new BracketValidationIssueDto
            {
                Severity = "WARNING",
                Code = "REGISTRATION_NOT_LOCKED",
                Message = "Danh sách đăng ký chưa được khóa. Có thể preview nhưng phải khóa trước khi apply."
            });
        }

        var activeApplicationExists = await _db.TournamentBracketApplications.AsNoTracking()
            .AnyAsync(x => x.TournamentId == tournamentId && x.IsActive, ct);
        if (activeApplicationExists)
        {
            validation.Issues.Add(new BracketValidationIssueDto
            {
                Severity = "ERROR",
                Code = "ACTIVE_APPLICATION_EXISTS",
                Message = "Giải đã có bracket application đang hoạt động. Hãy reset hợp lệ trước khi tạo preview mới."
            });
        }
        else if (await _db.TournamentRoundMaps.AsNoTracking().AnyAsync(x => x.TournamentId == tournamentId, ct))
        {
            validation.Issues.Add(new BracketValidationIssueDto
            {
                Severity = "ERROR",
                Code = "RUNTIME_STRUCTURE_EXISTS",
                Message = "Giải đã có cấu trúc vòng/bảng/trận thủ công nên không thể áp dụng template trong MVP."
            });
        }

        var tournamentStatus = Normalize(tournament.Status);
        if (tournamentStatus is "ACTIVE" or "COMPLETED" or "CANCELLED")
        {
            validation.Issues.Add(new BracketValidationIssueDto
            {
                Severity = "ERROR",
                Code = "TOURNAMENT_STATUS_INVALID",
                Message = $"Không thể cấu hình bracket khi giải ở trạng thái {tournamentStatus}."
            });
        }

        var preview = new TournamentBracketPreviewDto
        {
            TournamentId = tournamentId,
            BracketTemplateId = graph.BracketTemplateId,
            BracketTemplateVersionId = graph.BracketTemplateVersionId,
            TemplateName = template.TemplateName,
            TemplateCode = template.TemplateCode,
            VersionNumber = graph.VersionNumber,
            SeedingMethod = seedingMethod,
            RandomSeed = randomSeed,
            EligibleRegistrationCount = registrations.Count,
            ExcludedRegistrationCount = Math.Max(0, allSuccessfulCount - registrations.Count),
            SeedCapacity = graph.SeedCapacity,
            ByeCount = seeds.Count(x => x.IsBye),
            RoundCount = graph.Rounds.Count,
            GroupCount = graph.Rounds.Sum(x => x.Groups.Count),
            MatchCount = graph.Rounds.Sum(x => x.Groups.Sum(g => g.Matches.Count)),
            PreviewHash = hash,
            RegistrationLocked = tournament.RegistrationLockedAt.HasValue,
            Validation = validation,
            Seeds = seeds,
            Rounds = BuildPreviewRounds(graph, seedByNumber)
        };
        return BracketOperationResult<TournamentBracketPreviewDto>.Ok(preview);
    }

    public async Task<BracketOperationResult<TournamentBracketApplicationDto>> ApplyAsync(
        long tournamentId,
        ApplyTournamentBracketRequest request,
        long? userId,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var previewResult = await PreviewAsync(tournamentId, request, ct);
        if (!previewResult.Success)
            return BracketOperationResult<TournamentBracketApplicationDto>.Fail(previewResult.ErrorCode!, previewResult.Message!);
        var preview = previewResult.Data!;

        if (string.IsNullOrWhiteSpace(request.PreviewHash)
            || !string.Equals(request.PreviewHash, preview.PreviewHash, StringComparison.OrdinalIgnoreCase))
        {
            return BracketOperationResult<TournamentBracketApplicationDto>.Fail(
                "PREVIEW_CHANGED",
                "Dữ liệu đăng ký, seed hoặc template đã thay đổi. Vui lòng xem preview lại.");
        }
        if (!preview.RegistrationLocked)
            return BracketOperationResult<TournamentBracketApplicationDto>.Fail("REGISTRATION_NOT_LOCKED", "Phải khóa danh sách đăng ký trước khi áp dụng bracket.");

        var existing = await _db.TournamentBracketApplications.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TournamentId == tournamentId && x.IsActive, ct);
        if (existing != null)
        {
            if (string.Equals(existing.PreviewHash, preview.PreviewHash, StringComparison.OrdinalIgnoreCase))
            {
                var existingDto = await GetApplicationDtoAsync(existing.TournamentBracketApplicationId, ct);
                return BracketOperationResult<TournamentBracketApplicationDto>.Ok(existingDto!, "Bracket đã được áp dụng trước đó.");
            }
            return BracketOperationResult<TournamentBracketApplicationDto>.Fail("ACTIVE_APPLICATION_EXISTS", "Giải đã có một bracket application đang hoạt động.");
        }

        if (!preview.Validation.IsValid)
        {
            var firstError = preview.Validation.Issues.First(x => x.Severity == "ERROR");
            return BracketOperationResult<TournamentBracketApplicationDto>.Fail(
                firstError.Code,
                firstError.Message);
        }

        if (!request.StartAt.HasValue)
            return BracketOperationResult<TournamentBracketApplicationDto>.Fail(
                "MATCH_START_REQUIRED", "Vui lòng nhập giờ bắt đầu dùng cho các trận đấu.");

        var addressText = request.AddressText?.Trim();
        if (string.IsNullOrWhiteSpace(addressText))
            return BracketOperationResult<TournamentBracketApplicationDto>.Fail(
                "MATCH_ADDRESS_REQUIRED", "Vui lòng nhập địa chỉ thi đấu.");
        if (addressText.Length > 400)
            return BracketOperationResult<TournamentBracketApplicationDto>.Fail(
                "MATCH_ADDRESS_TOO_LONG", "Địa chỉ thi đấu không được vượt quá 400 ký tự.");

        var refereeValidation = await ValidateAndEnsureRefereeAsync(request.RefereeUserId, ct);
        if (refereeValidation.HasValue)
            return BracketOperationResult<TournamentBracketApplicationDto>.Fail(
                refereeValidation.Value.Code, refereeValidation.Value.Message);

        var matchStartAt = NormalizeScheduleToLocal(request.StartAt.Value);

        var graph = await _templateService.GetGraphAsync(request.BracketTemplateVersionId, ct);
        if (graph == null)
            return BracketOperationResult<TournamentBracketApplicationDto>.Fail("VERSION_NOT_FOUND", "Không tìm thấy template version.");

        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;
        TournamentBracketApplication? application = null;
        try
        {
            var concurrentExisting = await _db.TournamentBracketApplications.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TournamentId == tournamentId && x.IsActive, ct);
            if (concurrentExisting != null)
            {
                if (string.Equals(concurrentExisting.PreviewHash, preview.PreviewHash, StringComparison.OrdinalIgnoreCase))
                {
                    var concurrentDto = await GetApplicationDtoAsync(
                        concurrentExisting.TournamentBracketApplicationId, ct);
                    return BracketOperationResult<TournamentBracketApplicationDto>.Ok(
                        concurrentDto!, "Bracket đã được áp dụng bởi một request đồng thời.");
                }

                return BracketOperationResult<TournamentBracketApplicationDto>.Fail(
                    "ACTIVE_APPLICATION_EXISTS", "Giải đã có bracket application đang hoạt động.");
            }
            if (await _db.TournamentRoundMaps.AnyAsync(x => x.TournamentId == tournamentId, ct))
                return BracketOperationResult<TournamentBracketApplicationDto>.Fail("RUNTIME_STRUCTURE_EXISTS", "Giải đã có vòng/bảng/trận. MVP không tự động ghi đè dữ liệu hiện tại.");

            var now = DateTime.UtcNow;
            application = new TournamentBracketApplication
            {
                TournamentId = tournamentId,
                BracketTemplateId = preview.BracketTemplateId,
                BracketTemplateVersionId = preview.BracketTemplateVersionId,
                Status = BracketApplicationStatuses.Applying,
                IsActive = true,
                SeedingMethod = preview.SeedingMethod,
                RandomSeed = preview.RandomSeed,
                EligibleRegistrationCount = preview.EligibleRegistrationCount,
                SeedCapacity = preview.SeedCapacity,
                ByeCount = preview.ByeCount,
                PreviewHash = preview.PreviewHash,
                AppliedByUserId = userId,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.TournamentBracketApplications.Add(application);
            await _db.SaveChangesAsync(ct);

            var appliedRegistrationIds = preview.Seeds
                .Where(x => x.RegistrationId.HasValue)
                .Select(x => x.RegistrationId!.Value)
                .ToList();
            var registrationMap = await _db.TournamentRegistrations.AsNoTracking()
                .Where(x => x.TournamentId == tournamentId && appliedRegistrationIds.Contains(x.RegistrationId))
                .ToDictionaryAsync(x => x.RegistrationId, ct);
            foreach (var seed in preview.Seeds)
            {
                registrationMap.TryGetValue(seed.RegistrationId ?? 0, out var registration);
                _db.TournamentBracketSeedAssignments.Add(new TournamentBracketSeedAssignment
                {
                    TournamentBracketApplicationId = application.TournamentBracketApplicationId,
                    SeedNumber = seed.SeedNumber,
                    RegistrationId = seed.RegistrationId,
                    IsBye = seed.IsBye,
                    InputOrder = seed.InputOrder,
                    AssignmentMethod = seed.IsBye ? BracketSeedingMethods.Bye : preview.SeedingMethod,
                    IsManuallyAdjusted = seed.IsManuallyAdjusted,
                    RegistrationCodeSnapshot = registration?.RegCode,
                    Player1NameSnapshot = registration?.Player1Name,
                    Player2NameSnapshot = registration?.Player2Name,
                    CreatedAt = now
                });
            }
            await _db.SaveChangesAsync(ct);

            var roundByKey = new Dictionary<string, TournamentRoundMap>(StringComparer.OrdinalIgnoreCase);
            foreach (var templateRound in graph.Rounds.OrderBy(x => x.SortOrder))
            {
                var round = new TournamentRoundMap
                {
                    TournamentId = tournamentId,
                    RoundKey = templateRound.RoundKey,
                    RoundLabel = templateRound.RoundLabel,
                    SortOrder = templateRound.SortOrder,
                    BracketApplicationId = application.TournamentBracketApplicationId,
                    TemplateRoundKey = templateRound.RoundKey,
                    TemplateRoundType = templateRound.RoundType,
                    CreatedAt = now
                };
                roundByKey[templateRound.RoundKey] = round;
                _db.TournamentRoundMaps.Add(round);
            }
            await _db.SaveChangesAsync(ct);

            var groupByKey = new Dictionary<string, TournamentRoundGroup>(StringComparer.OrdinalIgnoreCase);
            foreach (var templateRound in graph.Rounds)
            foreach (var templateGroup in templateRound.Groups)
            {
                var group = new TournamentRoundGroup
                {
                    TournamentRoundMapId = roundByKey[templateRound.RoundKey].TournamentRoundMapId,
                    GroupName = templateGroup.GroupName,
                    SortOrder = templateGroup.SortOrder,
                    BracketApplicationId = application.TournamentBracketApplicationId,
                    TemplateGroupKey = templateGroup.GroupKey,
                    TemplateGroupType = templateGroup.GroupType,
                    CreatedAt = now
                };
                groupByKey[templateGroup.GroupKey] = group;
                _db.TournamentRoundGroups.Add(group);
            }
            await _db.SaveChangesAsync(ct);

            var seedByNumber = preview.Seeds.ToDictionary(x => x.SeedNumber);
            var matchByKey = new Dictionary<string, TournamentGroupMatch>(StringComparer.OrdinalIgnoreCase);
            var templateMatchByKey = graph.Rounds.SelectMany(x => x.Groups).SelectMany(x => x.Matches)
                .ToDictionary(x => x.MatchKey, StringComparer.OrdinalIgnoreCase);
            foreach (var templateRound in graph.Rounds)
            foreach (var templateGroup in templateRound.Groups)
            foreach (var templateMatch in templateGroup.Matches)
            {
                var slots = templateMatch.Slots.OrderBy(x => x.SlotNumber).ToArray();
                var match = new TournamentGroupMatch
                {
                    TournamentRoundGroupId = groupByKey[templateGroup.GroupKey].TournamentRoundGroupId,
                    TournamentId = tournamentId,
                    ScoreTeam1 = 0,
                    ScoreTeam2 = 0,
                    IsCompleted = false,
                    WinnerRegistrationId = null,
                    StartAt = matchStartAt,
                    AddressText = addressText,
                    RefereeUserId = request.RefereeUserId,
                    BracketApplicationId = application.TournamentBracketApplicationId,
                    TemplateMatchKey = templateMatch.MatchKey,
                    TemplateMatchLabel = templateMatch.MatchLabel,
                    TemplateIsTerminal = templateMatch.IsTerminal,
                    TemplateTerminalType = templateMatch.TerminalType,
                    CreatedAt = now
                };
                SetInitialSlot(match, 1, slots[0], seedByNumber);
                SetInitialSlot(match, 2, slots[1], seedByNumber);
                matchByKey[templateMatch.MatchKey] = match;
                _db.TournamentGroupMatches.Add(match);
            }
            await _db.SaveChangesAsync(ct);

            foreach (var templateMatch in templateMatchByKey.Values)
            {
                var match = matchByKey[templateMatch.MatchKey];
                foreach (var slot in templateMatch.Slots)
                {
                    if (slot.SourceType is BracketTemplateSourceTypes.WinnerMatch or BracketTemplateSourceTypes.LoserMatch)
                        SetSourceMatchId(match, slot.SlotNumber, matchByKey[slot.SourceMatchKey!].MatchId);
                    else if (slot.SourceType == BracketTemplateSourceTypes.GroupRank)
                        SetSourceGroup(match, slot.SlotNumber, groupByKey[slot.SourceGroupKey!].TournamentRoundGroupId, slot.SourceRank!.Value);
                }
            }

            ResolveInitialByes(matchByKey.Values, now);
            await _db.SaveChangesAsync(ct);
            await ValidateGeneratedRuntimeAsync(
                application.TournamentBracketApplicationId,
                tournamentId,
                preview,
                ct);

            application.Status = BracketApplicationStatuses.Applied;
            application.AppliedAt = now;
            application.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
            if (transaction != null)
                await transaction.CommitAsync(ct);

            _logger.LogInformation(
                "Bracket application {ApplicationId} applied to tournament {TournamentId} from template {TemplateId}/version {VersionId}: {RegistrationCount} registrations, {RoundCount} rounds, {GroupCount} groups, {MatchCount} matches, {ByeCount} byes in {ElapsedMs} ms.",
                application.TournamentBracketApplicationId,
                tournamentId,
                preview.BracketTemplateId,
                preview.BracketTemplateVersionId,
                preview.EligibleRegistrationCount,
                preview.RoundCount,
                preview.GroupCount,
                preview.MatchCount,
                preview.ByeCount,
                stopwatch.ElapsedMilliseconds);

            var dto = await GetApplicationDtoAsync(application.TournamentBracketApplicationId, ct);
            return BracketOperationResult<TournamentBracketApplicationDto>.Ok(dto!, "Đã áp dụng bracket và sinh cấu trúc giải.");
        }
        catch (Exception ex)
        {
            if (transaction != null)
                await transaction.RollbackAsync(ct);
            _db.ChangeTracker.Clear();

            var existingAfterRollback = await _db.TournamentBracketApplications.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TournamentId == tournamentId && x.IsActive, ct);
            if (existingAfterRollback != null)
            {
                if (string.Equals(existingAfterRollback.PreviewHash, preview.PreviewHash, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "Bracket apply for tournament {TournamentId} resolved idempotently to concurrent application {ApplicationId}.",
                        tournamentId, existingAfterRollback.TournamentBracketApplicationId);
                    var existingDto = await GetApplicationDtoAsync(
                        existingAfterRollback.TournamentBracketApplicationId, ct);
                    return BracketOperationResult<TournamentBracketApplicationDto>.Ok(
                        existingDto!, "Bracket đã được áp dụng bởi request trước đó.");
                }

                return BracketOperationResult<TournamentBracketApplicationDto>.Fail(
                    "ACTIVE_APPLICATION_EXISTS", "Giải đã có bracket application đang hoạt động.");
            }

            _logger.LogError(ex,
                "Apply bracket failed for tournament {TournamentId}, version {VersionId}, after {ElapsedMs} ms; transaction rolled back.",
                tournamentId, request.BracketTemplateVersionId, stopwatch.ElapsedMilliseconds);
            await TryRecordFailedApplicationAsync(tournamentId, preview, userId, ex, ct);
            return BracketOperationResult<TournamentBracketApplicationDto>.Fail("APPLY_FAILED", "Áp dụng bracket thất bại; toàn bộ dữ liệu tạo dở đã được rollback.");
        }
    }

    public async Task<TournamentBracketApplicationDto?> GetActiveApplicationAsync(long tournamentId, CancellationToken ct)
    {
        var id = await _db.TournamentBracketApplications.AsNoTracking()
            .Where(x => x.TournamentId == tournamentId && x.IsActive)
            .Select(x => (long?)x.TournamentBracketApplicationId)
            .FirstOrDefaultAsync(ct);
        return id.HasValue ? await GetApplicationDtoAsync(id.Value, ct) : null;
    }

    public async Task<IReadOnlyList<TournamentBracketApplicationDto>> GetApplicationHistoryAsync(long tournamentId, CancellationToken ct)
    {
        var ids = await _db.TournamentBracketApplications.AsNoTracking()
            .Where(x => x.TournamentId == tournamentId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.TournamentBracketApplicationId)
            .ToListAsync(ct);
        var result = new List<TournamentBracketApplicationDto>(ids.Count);
        foreach (var id in ids)
        {
            var item = await GetApplicationDtoAsync(id, ct);
            if (item != null)
                result.Add(item);
        }
        return result;
    }

    public async Task<BracketOperationResult<bool>> SetRegistrationLockAsync(
        long tournamentId,
        bool locked,
        long? userId,
        CancellationToken ct)
    {
        var tournament = await _db.Tournaments.FirstOrDefaultAsync(x => x.TournamentId == tournamentId && !x.Remove, ct);
        if (tournament == null)
            return BracketOperationResult<bool>.Fail("TOURNAMENT_NOT_FOUND", "Không tìm thấy giải đấu.");

        if (!locked && await _db.TournamentBracketApplications.AnyAsync(x => x.TournamentId == tournamentId && x.IsActive, ct))
            return BracketOperationResult<bool>.Fail("ACTIVE_APPLICATION_EXISTS", "Không thể mở đăng ký khi bracket đang hoạt động.");

        tournament.RegistrationLockedAt = locked ? DateTime.UtcNow : null;
        tournament.RegistrationLockedByUserId = locked ? userId : null;
        await _db.SaveChangesAsync(ct);
        return BracketOperationResult<bool>.Ok(true, locked ? "Đã khóa danh sách đăng ký." : "Đã mở lại danh sách đăng ký.");
    }

    public async Task<BracketOperationResult<bool>> ResetAsync(
        long tournamentId,
        ResetTournamentBracketRequest request,
        long? userId,
        CancellationToken ct)
    {
        var reason = (request.Reason ?? "").Trim();
        if (reason.Length < 3)
            return BracketOperationResult<bool>.Fail("RESET_REASON_REQUIRED", "Vui lòng nhập lý do reset.");

        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;
        var application = await _db.TournamentBracketApplications
            .FirstOrDefaultAsync(x => x.TournamentId == tournamentId && x.IsActive, ct);
        if (application == null)
            return BracketOperationResult<bool>.Fail("APPLICATION_NOT_FOUND", "Giải chưa có bracket application đang hoạt động.");

        var generatedMatchIds = await _db.TournamentGroupMatches.AsNoTracking()
            .Where(x => x.BracketApplicationId == application.TournamentBracketApplicationId)
            .Select(x => x.MatchId)
            .ToListAsync(ct);
        var generatedGroupIds = await _db.TournamentRoundGroups.AsNoTracking()
            .Where(x => x.BracketApplicationId == application.TournamentBracketApplicationId)
            .Select(x => x.TournamentRoundGroupId)
            .ToListAsync(ct);

        var hasPlayed = await _db.TournamentGroupMatches.AsNoTracking()
            .AnyAsync(x => x.BracketApplicationId == application.TournamentBracketApplicationId
                           && x.IsCompleted
                           && (x.CompletionReason == null || x.CompletionReason != MatchCompletionReasons.Bye), ct);
        var scheduledStarts = await _db.TournamentGroupMatches.AsNoTracking()
            .Where(x => x.BracketApplicationId == application.TournamentBracketApplicationId
                        && x.StartAt.HasValue
                        && (x.CompletionReason == null || x.CompletionReason != MatchCompletionReasons.Bye))
            .Select(x => x.StartAt!.Value)
            .ToListAsync(ct);
        var nowLocal = DateTime.Now;
        var hasStarted = scheduledStarts.Any(x => NormalizeScheduleToLocal(x) <= nowLocal);
        var hasHistory = await _db.TournamentMatchScoreHistories.AsNoTracking()
            .AnyAsync(x => generatedMatchIds.Contains(x.MatchId), ct);
        if (hasPlayed || hasStarted || hasHistory)
            return BracketOperationResult<bool>.Fail("TOURNAMENT_ALREADY_STARTED", "Không thể reset vì giải đã có trận bắt đầu, kết quả hoặc lịch sử điểm.");

        var hasExternalMatchDependency = await _db.TournamentGroupMatches.AsNoTracking()
            .AnyAsync(x => x.BracketApplicationId != application.TournamentBracketApplicationId
                           && ((x.Team1SourceMatchId.HasValue && generatedMatchIds.Contains(x.Team1SourceMatchId.Value))
                               || (x.Team2SourceMatchId.HasValue && generatedMatchIds.Contains(x.Team2SourceMatchId.Value))
                               || (x.Team1SourceGroupId.HasValue && generatedGroupIds.Contains(x.Team1SourceGroupId.Value))
                               || (x.Team2SourceGroupId.HasValue && generatedGroupIds.Contains(x.Team2SourceGroupId.Value))), ct);
        if (hasExternalMatchDependency)
            return BracketOperationResult<bool>.Fail("EXTERNAL_DEPENDENCY", "Có trận ngoài application đang phụ thuộc vào bracket này.");

        try
        {
            if (_db.Database.IsRelational())
            {
                await _db.UserNotifications
                    .Where(x => x.RefType == "MATCH" && x.RefId.HasValue && generatedMatchIds.Contains(x.RefId.Value))
                    .ExecuteDeleteAsync(ct);
                await _db.TournamentMatchScoreHistories
                    .Where(x => generatedMatchIds.Contains(x.MatchId))
                    .ExecuteDeleteAsync(ct);
                await _db.TournamentGroupMatches
                    .Where(x => x.BracketApplicationId == application.TournamentBracketApplicationId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Team1SourceMatchId, (long?)null)
                        .SetProperty(x => x.Team2SourceMatchId, (long?)null)
                        .SetProperty(x => x.Team1SourceGroupId, (long?)null)
                        .SetProperty(x => x.Team2SourceGroupId, (long?)null), ct);
                await _db.TournamentGroupMatches
                    .Where(x => x.BracketApplicationId == application.TournamentBracketApplicationId)
                    .ExecuteDeleteAsync(ct);
                await _db.TournamentRoundGroups
                    .Where(x => x.BracketApplicationId == application.TournamentBracketApplicationId)
                    .ExecuteDeleteAsync(ct);
                await _db.TournamentRoundMaps
                    .Where(x => x.BracketApplicationId == application.TournamentBracketApplicationId)
                    .ExecuteDeleteAsync(ct);
            }
            else
            {
                _db.UserNotifications.RemoveRange(
                    await _db.UserNotifications
                        .Where(x => x.RefType == "MATCH"
                                    && x.RefId.HasValue
                                    && generatedMatchIds.Contains(x.RefId.Value))
                        .ToListAsync(ct));
                _db.TournamentMatchScoreHistories.RemoveRange(
                    await _db.TournamentMatchScoreHistories
                        .Where(x => generatedMatchIds.Contains(x.MatchId))
                        .ToListAsync(ct));

                var generatedMatches = await _db.TournamentGroupMatches
                    .Where(x => x.BracketApplicationId == application.TournamentBracketApplicationId)
                    .ToListAsync(ct);
                foreach (var match in generatedMatches)
                {
                    match.Team1SourceMatchId = null;
                    match.Team2SourceMatchId = null;
                    match.Team1SourceGroupId = null;
                    match.Team2SourceGroupId = null;
                }
                await _db.SaveChangesAsync(ct);
                _db.TournamentGroupMatches.RemoveRange(generatedMatches);
                _db.TournamentRoundGroups.RemoveRange(
                    await _db.TournamentRoundGroups
                        .Where(x => x.BracketApplicationId == application.TournamentBracketApplicationId)
                        .ToListAsync(ct));
                _db.TournamentRoundMaps.RemoveRange(
                    await _db.TournamentRoundMaps
                        .Where(x => x.BracketApplicationId == application.TournamentBracketApplicationId)
                        .ToListAsync(ct));
            }

            application.Status = BracketApplicationStatuses.Reverted;
            application.IsActive = false;
            application.RevertedAt = DateTime.UtcNow;
            application.RevertedByUserId = userId;
            application.RevertReason = reason;
            application.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            if (transaction != null)
                await transaction.CommitAsync(ct);
            _logger.LogInformation(
                "Bracket application {ApplicationId} reset for tournament {TournamentId} by user {UserId}; removed {MatchCount} matches and {GroupCount} groups. Reason: {Reason}",
                application.TournamentBracketApplicationId, tournamentId, userId, generatedMatchIds.Count, generatedGroupIds.Count, reason);
            return BracketOperationResult<bool>.Ok(true, "Đã reset bracket. Seed snapshot và lịch sử application được giữ lại.");
        }
        catch (Exception ex)
        {
            if (transaction != null)
                await transaction.RollbackAsync(ct);
            _logger.LogError(ex, "Reset bracket failed for tournament {TournamentId}.", tournamentId);
            return BracketOperationResult<bool>.Fail("RESET_FAILED", "Reset bracket thất bại; dữ liệu đã được rollback.");
        }
    }

    private IQueryable<TournamentRegistration> EligibleRegistrationsQuery(long tournamentId, decimal registrationFee)
    {
        var query = _db.TournamentRegistrations.AsNoTracking()
            .Where(x => x.TournamentId == tournamentId
                        && x.Success
                        && !x.WaitingPair
                        && x.Player1Name != null
                        && x.Player1Name != ""
                        && ((x.Tournament.GameType != null && x.Tournament.GameType.ToUpper() == "SINGLE")
                            || (x.Player2Name != null && x.Player2Name != "")));
        if (registrationFee > 0)
            query = query.Where(x => x.Paid);
        return query;
    }

    private static DateTime NormalizeScheduleToLocal(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value.ToLocalTime(),
            DateTimeKind.Local => value,
            _ => DateTime.SpecifyKind(value, DateTimeKind.Local)
        };
    }

    private async Task<(string Code, string Message)?> ValidateAndEnsureRefereeAsync(
        long? refereeUserId,
        CancellationToken ct)
    {
        if (!refereeUserId.HasValue || refereeUserId.Value <= 0)
            return ("REFEREE_REQUIRED", "Vui lòng nhập ID tài khoản trọng tài.");

        var user = await _db.Users
            .FirstOrDefaultAsync(x => x.UserId == refereeUserId.Value, ct);
        if (user == null)
            return ("REFEREE_USER_NOT_FOUND", "Không tìm thấy user trọng tài.");
        if (!user.IsActive)
            return ("REFEREE_USER_INACTIVE", "Người dùng trọng tài đang bị vô hiệu hóa.");

        var refereeProfile = await _db.Referees.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ExternalId == refereeUserId.Value.ToString(), ct);
        if (refereeProfile == null)
            return ("REFEREE_PROFILE_NOT_FOUND", "Người dùng này chưa có hồ sơ trọng tài.");
        if (!refereeProfile.Verified)
            return ("REFEREE_NOT_VERIFIED", "Hồ sơ trọng tài này chưa được xác minh.");

        var refereeRoleId = await _db.Roles.AsNoTracking()
            .Where(x => x.RoleCode == "REFEREE")
            .Select(x => x.RoleId)
            .FirstOrDefaultAsync(ct);
        if (refereeRoleId == 0)
            return ("REFEREE_ROLE_NOT_FOUND", "Không tìm thấy vai trò trọng tài trong hệ thống.");

        var hasRefereeRole = await _db.UserRoles
            .AnyAsync(x => x.UserId == refereeUserId.Value && x.RoleId == refereeRoleId, ct);
        if (!hasRefereeRole)
        {
            _db.UserRoles.Add(new UserRole
            {
                UserId = refereeUserId.Value,
                RoleId = refereeRoleId,
                CreatedAt = DateTime.UtcNow
            });
        }

        return null;
    }

    internal static BracketOperationResult<SeedBuildResult> BuildSeeds(
        IReadOnlyList<TournamentRegistration> registrations,
        int capacity,
        string method,
        long? requestedRandomSeed,
        IReadOnlyList<ManualSeedAssignmentRequest> manualAssignments)
    {
        var ordered = registrations.ToList();
        var inputOrderById = registrations
            .Select((registration, index) => new { registration.RegistrationId, InputOrder = index + 1 })
            .ToDictionary(x => x.RegistrationId, x => x.InputOrder);
        long? randomSeed = null;
        if (method == BracketSeedingMethods.Random)
        {
            if (requestedRandomSeed.HasValue
                && (requestedRandomSeed.Value < 1 || requestedRandomSeed.Value > JavaScriptMaxSafeInteger))
            {
                return BracketOperationResult<SeedBuildResult>.Fail(
                    "RANDOM_SEED_INVALID",
                    "Mã random không nằm trong phạm vi an toàn. Vui lòng tạo lại preview.");
            }
            randomSeed = requestedRandomSeed ?? GenerateRandomSeed();
            var random = new Random(unchecked((int)(randomSeed.Value ^ (randomSeed.Value >> 32))));
            for (var index = ordered.Count - 1; index > 0; index--)
            {
                var selected = random.Next(index + 1);
                (ordered[index], ordered[selected]) = (ordered[selected], ordered[index]);
            }
        }
        else if (method == BracketSeedingMethods.Ranking)
        {
            ordered = ordered
                .OrderByDescending(x => x.Points)
                .ThenByDescending(x => x.Player1Level + x.Player2Level)
                .ThenBy(x => x.RegIndex)
                .ThenBy(x => x.RegistrationId)
                .ToList();
        }

        var seeds = new List<TournamentBracketSeedDto>(capacity);
        if (method == BracketSeedingMethods.Manual)
        {
            var duplicateSeed = manualAssignments.Where(x => x.RegistrationId.HasValue)
                .GroupBy(x => x.SeedNumber).FirstOrDefault(x => x.Count() > 1);
            if (duplicateSeed != null)
                return BracketOperationResult<SeedBuildResult>.Fail("MANUAL_SEED_DUPLICATE", $"Seed {duplicateSeed.Key} được gán nhiều lần.");
            var duplicateRegistration = manualAssignments.Where(x => x.RegistrationId.HasValue)
                .GroupBy(x => x.RegistrationId!.Value).FirstOrDefault(x => x.Count() > 1);
            if (duplicateRegistration != null)
                return BracketOperationResult<SeedBuildResult>.Fail("MANUAL_REGISTRATION_DUPLICATE", "Một đội đang được gán vào nhiều seed.");

            var registrationById = registrations.ToDictionary(x => x.RegistrationId);
            var assignmentBySeed = manualAssignments
                .Where(x => x.SeedNumber >= 1 && x.SeedNumber <= capacity)
                .GroupBy(x => x.SeedNumber)
                .ToDictionary(x => x.Key, x => x.First().RegistrationId);
            var assignedIds = assignmentBySeed.Values.Where(x => x.HasValue).Select(x => x!.Value).ToHashSet();
            if (assignedIds.Any(x => !registrationById.ContainsKey(x)) || assignedIds.Count != registrations.Count)
                return BracketOperationResult<SeedBuildResult>.Fail("MANUAL_SEED_INCOMPLETE", "Seed thủ công phải chứa đúng một lần tất cả đội đủ điều kiện.");

            for (var seedNumber = 1; seedNumber <= capacity; seedNumber++)
            {
                assignmentBySeed.TryGetValue(seedNumber, out var registrationId);
                registrationById.TryGetValue(registrationId ?? 0, out var registration);
                seeds.Add(registration == null
                    ? CreateByeSeed(seedNumber)
                    : MapRegistrationSeed(registration, seedNumber, inputOrderById[registration.RegistrationId], true, false));
            }
        }
        else
        {
            for (var seedNumber = 1; seedNumber <= capacity; seedNumber++)
            {
                if (seedNumber <= ordered.Count)
                    seeds.Add(MapRegistrationSeed(
                        ordered[seedNumber - 1],
                        seedNumber,
                        inputOrderById[ordered[seedNumber - 1].RegistrationId],
                        false,
                        false));
                else
                    seeds.Add(CreateByeSeed(seedNumber));
            }
        }

        return BracketOperationResult<SeedBuildResult>.Ok(new SeedBuildResult(seeds, randomSeed));
    }

    internal static BracketOperationResult<bool> ValidateTeamCount(
        int teamCount,
        int minimumTeams,
        int capacity)
    {
        if (teamCount < minimumTeams)
            return BracketOperationResult<bool>.Fail("NOT_ENOUGH_TEAMS", $"Template cần tối thiểu {minimumTeams} đội, hiện có {teamCount} đội đủ điều kiện.");
        if (teamCount > capacity)
            return BracketOperationResult<bool>.Fail("TOO_MANY_TEAMS", $"Template tối đa {capacity} đội, hiện có {teamCount} đội đủ điều kiện.");
        return BracketOperationResult<bool>.Ok(true);
    }

    private static TournamentBracketSeedDto MapRegistrationSeed(
        TournamentRegistration registration,
        int seedNumber,
        int inputOrder,
        bool manuallyAdjusted,
        bool isBye)
    {
        var teamName = string.IsNullOrWhiteSpace(registration.Player2Name)
            ? registration.Player1Name
            : $"{registration.Player1Name}/{registration.Player2Name}";
        return new TournamentBracketSeedDto
        {
            SeedNumber = seedNumber,
            RegistrationId = registration.RegistrationId,
            IsBye = isBye,
            InputOrder = inputOrder,
            IsManuallyAdjusted = manuallyAdjusted,
            RegCode = registration.RegCode,
            TeamName = teamName,
            Player1Name = registration.Player1Name,
            Player2Name = registration.Player2Name,
            Player1UserId = registration.Player1UserId,
            Player2UserId = registration.Player2UserId,
            Player1Level = registration.Player1Level,
            Player2Level = registration.Player2Level,
            Points = registration.Points,
            Paid = registration.Paid,
            RegisteredAt = registration.RegTime ?? registration.CreatedAt
        };
    }

    private static TournamentBracketSeedDto CreateByeSeed(int seedNumber) => new()
    {
        SeedNumber = seedNumber,
        IsBye = true,
        TeamName = "BYE"
    };

    private static List<TournamentBracketPreviewRoundDto> BuildPreviewRounds(
        BracketTemplateGraphDto graph,
        IReadOnlyDictionary<int, TournamentBracketSeedDto> seedByNumber)
    {
        var matchLabels = graph.Rounds.SelectMany(x => x.Groups).SelectMany(x => x.Matches)
            .ToDictionary(x => x.MatchKey, x => x.MatchLabel ?? x.MatchKey, StringComparer.OrdinalIgnoreCase);
        var groupNames = graph.Rounds.SelectMany(x => x.Groups)
            .ToDictionary(x => x.GroupKey, x => x.GroupName, StringComparer.OrdinalIgnoreCase);
        return graph.Rounds.OrderBy(x => x.SortOrder).Select(round => new TournamentBracketPreviewRoundDto
        {
            RoundKey = round.RoundKey,
            RoundLabel = round.RoundLabel,
            RoundType = round.RoundType,
            SortOrder = round.SortOrder,
            Groups = round.Groups.OrderBy(x => x.SortOrder).Select(group => new TournamentBracketPreviewGroupDto
            {
                GroupKey = group.GroupKey,
                GroupName = group.GroupName,
                GroupType = group.GroupType,
                SortOrder = group.SortOrder,
                Matches = group.Matches.OrderBy(x => x.SortOrder).Select(match => new TournamentBracketPreviewMatchDto
                {
                    MatchKey = match.MatchKey,
                    MatchLabel = match.MatchLabel,
                    SortOrder = match.SortOrder,
                    IsTerminal = match.IsTerminal,
                    TerminalType = match.TerminalType,
                    Slots = match.Slots.OrderBy(x => x.SlotNumber)
                        .Select(slot => BuildPreviewSlot(slot, seedByNumber, matchLabels, groupNames))
                        .ToList()
                }).ToList()
            }).ToList()
        }).ToList();
    }

    private static TournamentBracketPreviewSlotDto BuildPreviewSlot(
        BracketTemplateSlotDto slot,
        IReadOnlyDictionary<int, TournamentBracketSeedDto> seedByNumber,
        IReadOnlyDictionary<string, string> matchLabels,
        IReadOnlyDictionary<string, string> groupNames)
    {
        if (slot.SourceType == BracketTemplateSourceTypes.Seed && slot.SeedNumber.HasValue
            && seedByNumber.TryGetValue(slot.SeedNumber.Value, out var seed))
        {
            return new TournamentBracketPreviewSlotDto
            {
                SlotNumber = slot.SlotNumber,
                SourceType = seed.IsBye ? BracketTemplateSourceTypes.Bye : BracketTemplateSourceTypes.Seed,
                SeedNumber = slot.SeedNumber,
                RegistrationId = seed.RegistrationId,
                IsBye = seed.IsBye,
                DisplayText = seed.IsBye ? $"Seed {slot.SeedNumber}: BYE" : $"Seed {slot.SeedNumber}: {seed.TeamName}"
            };
        }

        var display = slot.SourceType switch
        {
            BracketTemplateSourceTypes.WinnerMatch =>
                $"Thắng {ResolveLabel(matchLabels, slot.SourceMatchKey)}",
            BracketTemplateSourceTypes.LoserMatch =>
                $"Thua {ResolveLabel(matchLabels, slot.SourceMatchKey)}",
            BracketTemplateSourceTypes.GroupRank =>
                $"Hạng {slot.SourceRank} · {ResolveLabel(groupNames, slot.SourceGroupKey)}",
            BracketTemplateSourceTypes.Bye => "BYE",
            _ => "Chưa xác định"
        };
        return new TournamentBracketPreviewSlotDto
        {
            SlotNumber = slot.SlotNumber,
            SourceType = slot.SourceType,
            IsBye = slot.SourceType == BracketTemplateSourceTypes.Bye,
            DisplayText = display,
            SourceMatchKey = slot.SourceMatchKey,
            SourceGroupKey = slot.SourceGroupKey,
            SourceRank = slot.SourceRank
        };
    }

    private static string ResolveLabel(IReadOnlyDictionary<string, string> labels, string? key)
    {
        if (!string.IsNullOrWhiteSpace(key) && labels.TryGetValue(key, out var label))
            return string.Equals(label, key, StringComparison.OrdinalIgnoreCase)
                ? key
                : $"{label} ({key})";
        return key ?? "?";
    }

    private static bool IsResolvedBye(
        BracketTemplateSlotDto slot,
        IReadOnlyDictionary<int, TournamentBracketSeedDto> seedByNumber)
    {
        if (slot.SourceType == BracketTemplateSourceTypes.Bye)
            return true;

        return slot.SourceType == BracketTemplateSourceTypes.Seed
               && slot.SeedNumber.HasValue
               && seedByNumber.TryGetValue(slot.SeedNumber.Value, out var seed)
               && seed.IsBye;
    }

    private static string ComputePreviewHash(
        long tournamentId,
        BracketTemplateGraphDto graph,
        string method,
        long? randomSeed,
        IEnumerable<TournamentBracketSeedDto> seeds)
    {
        var canonical = string.Join('|', new[]
        {
            tournamentId.ToString(),
            graph.BracketTemplateVersionId.ToString(),
            graph.ConfigurationHash ?? "",
            graph.RowVersion,
            method,
            randomSeed?.ToString() ?? "",
            string.Join(';', seeds.OrderBy(x => x.SeedNumber)
                .Select(x => string.Join(':',
                    x.SeedNumber,
                    x.RegistrationId?.ToString() ?? "BYE",
                    x.Player1UserId?.ToString() ?? "",
                    x.Player2UserId?.ToString() ?? "",
                    x.Player1Name ?? "",
                    x.Player2Name ?? "",
                    x.Points,
                    x.Paid,
                    x.RegisteredAt?.Ticks ?? 0)))
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static void SetInitialSlot(
        TournamentGroupMatch match,
        int slotNumber,
        BracketTemplateSlotDto slot,
        IReadOnlyDictionary<int, TournamentBracketSeedDto> seedByNumber)
    {
        var sourceType = slot.SourceType;
        long? registrationId = null;
        if (sourceType == BracketTemplateSourceTypes.Seed && slot.SeedNumber.HasValue)
        {
            var seed = seedByNumber[slot.SeedNumber.Value];
            sourceType = seed.IsBye ? MatchSourceTypes.Bye : MatchSourceTypes.Registration;
            registrationId = seed.RegistrationId;
        }
        else if (sourceType == BracketTemplateSourceTypes.Bye)
        {
            sourceType = MatchSourceTypes.Bye;
        }

        if (slotNumber == 1)
        {
            match.Team1SourceType = sourceType;
            match.Team1RegistrationId = registrationId;
        }
        else
        {
            match.Team2SourceType = sourceType;
            match.Team2RegistrationId = registrationId;
        }
    }

    private static void SetSourceMatchId(TournamentGroupMatch match, int slotNumber, long sourceMatchId)
    {
        if (slotNumber == 1)
            match.Team1SourceMatchId = sourceMatchId;
        else
            match.Team2SourceMatchId = sourceMatchId;
    }

    private static void SetSourceGroup(TournamentGroupMatch match, int slotNumber, long sourceGroupId, int rank)
    {
        if (slotNumber == 1)
        {
            match.Team1SourceGroupId = sourceGroupId;
            match.Team1SourceRank = rank;
        }
        else
        {
            match.Team2SourceGroupId = sourceGroupId;
            match.Team2SourceRank = rank;
        }
    }

    internal static void ResolveInitialByes(IEnumerable<TournamentGroupMatch> matches, DateTime now)
    {
        var list = matches.ToList();
        var byId = list.ToDictionary(x => x.MatchId);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var match in list.Where(x => !x.IsCompleted))
            {
                if (!match.Team1RegistrationId.HasValue && match.Team1SourceMatchId.HasValue
                    && byId.TryGetValue(match.Team1SourceMatchId.Value, out var source1)
                    && source1.IsCompleted && source1.WinnerRegistrationId.HasValue
                    && match.Team1SourceType == MatchSourceTypes.WinnerMatch)
                {
                    match.Team1RegistrationId = source1.WinnerRegistrationId;
                    changed = true;
                }
                if (!match.Team2RegistrationId.HasValue && match.Team2SourceMatchId.HasValue
                    && byId.TryGetValue(match.Team2SourceMatchId.Value, out var source2)
                    && source2.IsCompleted && source2.WinnerRegistrationId.HasValue
                    && match.Team2SourceType == MatchSourceTypes.WinnerMatch)
                {
                    match.Team2RegistrationId = source2.WinnerRegistrationId;
                    changed = true;
                }

                var team1Bye = match.Team1SourceType == MatchSourceTypes.Bye;
                var team2Bye = match.Team2SourceType == MatchSourceTypes.Bye;
                if (team1Bye && match.Team2RegistrationId.HasValue)
                {
                    CompleteBye(match, match.Team2RegistrationId.Value, now);
                    changed = true;
                }
                else if (team2Bye && match.Team1RegistrationId.HasValue)
                {
                    CompleteBye(match, match.Team1RegistrationId.Value, now);
                    changed = true;
                }
            }
        }
    }

    private static void CompleteBye(TournamentGroupMatch match, long winnerId, DateTime now)
    {
        match.IsCompleted = true;
        match.WinnerRegistrationId = winnerId;
        match.CompletionReason = MatchCompletionReasons.Bye;
        match.ScoreTeam1 = 0;
        match.ScoreTeam2 = 0;
        match.UpdatedAt = now;
    }

    private async Task ValidateGeneratedRuntimeAsync(
        long applicationId,
        long tournamentId,
        TournamentBracketPreviewDto preview,
        CancellationToken ct)
    {
        var runtimeRounds = await _db.TournamentRoundMaps.AsNoTracking()
            .Where(x => x.BracketApplicationId == applicationId && x.TournamentId == tournamentId)
            .Select(x => new { x.TemplateRoundKey, x.TemplateRoundType })
            .ToListAsync(ct);
        var runtimeGroups = await _db.TournamentRoundGroups.AsNoTracking()
            .Where(x => x.BracketApplicationId == applicationId)
            .Select(x => new { x.TournamentRoundGroupId, x.TemplateGroupKey, x.TemplateGroupType })
            .ToListAsync(ct);
        var groupIds = runtimeGroups.Select(x => x.TournamentRoundGroupId).ToList();
        var matches = await _db.TournamentGroupMatches.AsNoTracking()
            .Where(x => x.BracketApplicationId == applicationId)
            .ToListAsync(ct);
        var seedCount = await _db.TournamentBracketSeedAssignments.AsNoTracking()
            .CountAsync(x => x.TournamentBracketApplicationId == applicationId, ct);

        if (runtimeRounds.Count != preview.RoundCount
            || groupIds.Count != preview.GroupCount
            || matches.Count != preview.MatchCount
            || seedCount != preview.SeedCapacity)
        {
            throw new InvalidOperationException(
                $"Runtime health check count mismatch: round {runtimeRounds.Count}/{preview.RoundCount}, group {groupIds.Count}/{preview.GroupCount}, match {matches.Count}/{preview.MatchCount}, seed {seedCount}/{preview.SeedCapacity}.");
        }

        var expectedRounds = preview.Rounds.ToDictionary(x => x.RoundKey, StringComparer.OrdinalIgnoreCase);
        var expectedGroups = preview.Rounds.SelectMany(x => x.Groups)
            .ToDictionary(x => x.GroupKey, StringComparer.OrdinalIgnoreCase);
        var expectedMatches = preview.Rounds.SelectMany(x => x.Groups).SelectMany(x => x.Matches)
            .ToDictionary(x => x.MatchKey, StringComparer.OrdinalIgnoreCase);
        if (runtimeRounds.Any(x => x.TemplateRoundKey == null
                                   || !expectedRounds.TryGetValue(x.TemplateRoundKey, out var expected)
                                   || !string.Equals(x.TemplateRoundType, expected.RoundType, StringComparison.OrdinalIgnoreCase))
            || runtimeGroups.Any(x => x.TemplateGroupKey == null
                                      || !expectedGroups.TryGetValue(x.TemplateGroupKey, out var expected)
                                      || !string.Equals(x.TemplateGroupType, expected.GroupType, StringComparison.OrdinalIgnoreCase))
            || matches.Any(x => x.TemplateMatchKey == null
                                || !expectedMatches.TryGetValue(x.TemplateMatchKey, out var expected)
                                || !string.Equals(x.TemplateMatchLabel, expected.MatchLabel, StringComparison.Ordinal)
                                || x.TemplateIsTerminal != expected.IsTerminal
                                || !string.Equals(x.TemplateTerminalType, expected.TerminalType, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Runtime health check found template metadata mismatch.");
        }

        if (matches.Any(x => x.TournamentId != tournamentId))
            throw new InvalidOperationException("Runtime health check found a match from another tournament.");

        var missingSource = matches.Any(x =>
            (x.Team1SourceType is MatchSourceTypes.WinnerMatch or MatchSourceTypes.LoserMatch && !x.Team1SourceMatchId.HasValue)
            || (x.Team2SourceType is MatchSourceTypes.WinnerMatch or MatchSourceTypes.LoserMatch && !x.Team2SourceMatchId.HasValue)
            || (x.Team1SourceType == MatchSourceTypes.GroupRank && (!x.Team1SourceGroupId.HasValue || !x.Team1SourceRank.HasValue))
            || (x.Team2SourceType == MatchSourceTypes.GroupRank && (!x.Team2SourceGroupId.HasValue || !x.Team2SourceRank.HasValue)));
        if (missingSource)
            throw new InvalidOperationException("Runtime health check found an unresolved source mapping.");

        var matchIds = matches.Select(x => x.MatchId).ToHashSet();
        var generatedGroupIds = groupIds.ToHashSet();
        var foreignSource = matches.Any(x =>
            (x.Team1SourceMatchId.HasValue && !matchIds.Contains(x.Team1SourceMatchId.Value))
            || (x.Team2SourceMatchId.HasValue && !matchIds.Contains(x.Team2SourceMatchId.Value))
            || (x.Team1SourceGroupId.HasValue && !generatedGroupIds.Contains(x.Team1SourceGroupId.Value))
            || (x.Team2SourceGroupId.HasValue && !generatedGroupIds.Contains(x.Team2SourceGroupId.Value)));
        if (foreignSource)
            throw new InvalidOperationException("Runtime health check found a source outside the generated application.");

        var dependencies = matches.ToDictionary(
            x => x.MatchId,
            x => new[] { x.Team1SourceMatchId, x.Team2SourceMatchId }
                .Where(sourceId => sourceId.HasValue)
                .Select(sourceId => sourceId!.Value)
                .Distinct()
                .ToList());
        var visitState = new Dictionary<long, byte>();
        bool Visit(long matchId)
        {
            if (visitState.TryGetValue(matchId, out var state))
                return state == 1;

            visitState[matchId] = 1;
            foreach (var sourceId in dependencies[matchId])
            {
                if (Visit(sourceId))
                    return true;
            }

            visitState[matchId] = 2;
            return false;
        }

        if (dependencies.Keys.Any(Visit))
            throw new InvalidOperationException("Runtime health check found a dependency cycle.");

        var duplicatePair = matches
            .Where(x => x.Team1RegistrationId.HasValue && x.Team2RegistrationId.HasValue)
            .GroupBy(x => new
            {
                x.TournamentRoundGroupId,
                TeamA = Math.Min(x.Team1RegistrationId!.Value, x.Team2RegistrationId!.Value),
                TeamB = Math.Max(x.Team1RegistrationId!.Value, x.Team2RegistrationId!.Value)
            })
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicatePair != null)
            throw new InvalidOperationException("Runtime health check found a duplicate team pair in one group.");
    }

    private async Task<TournamentBracketApplicationDto?> GetApplicationDtoAsync(long applicationId, CancellationToken ct)
    {
        var app = await _db.TournamentBracketApplications.AsNoTracking()
            .Include(x => x.BracketTemplate)
            .Include(x => x.BracketTemplateVersion)
            .Include(x => x.AppliedByUser)
            .Include(x => x.RevertedByUser)
            .Include(x => x.SeedAssignments.OrderBy(s => s.SeedNumber))
            .FirstOrDefaultAsync(x => x.TournamentBracketApplicationId == applicationId, ct);
        if (app == null)
            return null;

        return new TournamentBracketApplicationDto
        {
            TournamentBracketApplicationId = app.TournamentBracketApplicationId,
            TournamentId = app.TournamentId,
            BracketTemplateId = app.BracketTemplateId,
            BracketTemplateVersionId = app.BracketTemplateVersionId,
            TemplateName = app.BracketTemplate.TemplateName,
            TemplateCode = app.BracketTemplate.TemplateCode,
            VersionNumber = app.BracketTemplateVersion.VersionNumber,
            Status = app.Status,
            IsActive = app.IsActive,
            SeedingMethod = app.SeedingMethod,
            RandomSeed = app.RandomSeed,
            EligibleRegistrationCount = app.EligibleRegistrationCount,
            SeedCapacity = app.SeedCapacity,
            ByeCount = app.ByeCount,
            CreatedAt = app.CreatedAt,
            AppliedAt = app.AppliedAt,
            AppliedByName = app.AppliedByUser?.FullName,
            RevertedAt = app.RevertedAt,
            RevertedByName = app.RevertedByUser?.FullName,
            RevertReason = app.RevertReason,
            GeneratedRoundCount = await _db.TournamentRoundMaps.CountAsync(x => x.BracketApplicationId == applicationId, ct),
            GeneratedGroupCount = await _db.TournamentRoundGroups.CountAsync(x => x.BracketApplicationId == applicationId, ct),
            GeneratedMatchCount = await _db.TournamentGroupMatches.CountAsync(x => x.BracketApplicationId == applicationId, ct),
            Seeds = app.SeedAssignments.Select(seed => new TournamentBracketSeedDto
            {
                SeedNumber = seed.SeedNumber,
                RegistrationId = seed.RegistrationId,
                IsBye = seed.IsBye,
                InputOrder = seed.InputOrder,
                IsManuallyAdjusted = seed.IsManuallyAdjusted,
                RegCode = seed.RegistrationCodeSnapshot,
                Player1Name = seed.Player1NameSnapshot,
                Player2Name = seed.Player2NameSnapshot,
                TeamName = seed.IsBye
                    ? "BYE"
                    : string.IsNullOrWhiteSpace(seed.Player2NameSnapshot)
                        ? seed.Player1NameSnapshot ?? $"Đội #{seed.RegistrationId}"
                        : $"{seed.Player1NameSnapshot}/{seed.Player2NameSnapshot}"
            }).ToList()
        };
    }

    private async Task TryRecordFailedApplicationAsync(
        long tournamentId,
        TournamentBracketPreviewDto preview,
        long? userId,
        Exception exception,
        CancellationToken ct)
    {
        try
        {
            _db.TournamentBracketApplications.Add(new TournamentBracketApplication
            {
                TournamentId = tournamentId,
                BracketTemplateId = preview.BracketTemplateId,
                BracketTemplateVersionId = preview.BracketTemplateVersionId,
                Status = BracketApplicationStatuses.Failed,
                IsActive = false,
                SeedingMethod = preview.SeedingMethod,
                RandomSeed = preview.RandomSeed,
                EligibleRegistrationCount = preview.EligibleRegistrationCount,
                SeedCapacity = preview.SeedCapacity,
                ByeCount = preview.ByeCount,
                PreviewHash = preview.PreviewHash,
                AppliedByUserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                ErrorCode = "APPLY_FAILED",
                ErrorMessage = exception.GetBaseException().Message.Length > 2000
                    ? exception.GetBaseException().Message[..2000]
                    : exception.GetBaseException().Message
            });
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception logException)
        {
            _logger.LogWarning(logException, "Could not persist failed bracket application for tournament {TournamentId}.", tournamentId);
        }
    }

    private static bool IsSeedingMethod(string value) => value is
        BracketSeedingMethods.RegistrationOrder or
        BracketSeedingMethods.Random or
        BracketSeedingMethods.Manual or
        BracketSeedingMethods.Ranking;

    internal static long GenerateRandomSeed()
    {
        // JSON numbers are parsed as IEEE-754 doubles in browsers. Keep the seed within
        // 53 bits so Preview -> JavaScript -> Apply preserves the exact integer and hash.
        var value = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(long)))
                    & (ulong)JavaScriptMaxSafeInteger;
        return value == 0 ? 1 : (long)value;
    }

    private static string Normalize(string? value) => (value ?? "").Trim().ToUpperInvariant();
    internal sealed record SeedBuildResult(List<TournamentBracketSeedDto> Seeds, long? RandomSeed);
}
