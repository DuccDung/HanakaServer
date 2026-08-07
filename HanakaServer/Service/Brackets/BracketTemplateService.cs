using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HanakaServer.Data;
using HanakaServer.Dtos.Brackets;
using HanakaServer.Models;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace HanakaServer.Services.Brackets;

public interface IBracketTemplateService
{
    Task<string> GetNextCodeAsync(CancellationToken ct);
    Task<PagedBracketTemplateListDto> ListAsync(string? search, string? status, string? formatType, int page, int pageSize, CancellationToken ct);
    Task<BracketTemplateDetailDto?> GetAsync(long templateId, CancellationToken ct);
    Task<BracketTemplateGraphDto?> GetGraphAsync(long versionId, CancellationToken ct);
    Task<BracketOperationResult<BracketTemplateDetailDto>> CreateAsync(CreateBracketTemplateRequest request, long? userId, CancellationToken ct);
    Task<BracketOperationResult<BracketTemplateDetailDto>> UpdateAsync(long templateId, UpdateBracketTemplateRequest request, long? userId, CancellationToken ct);
    Task<BracketOperationResult<BracketTemplateDetailDto>> UpdateSettingsAsync(long templateId, UpdateBracketTemplateSettingsRequest request, long? userId, CancellationToken ct);
    Task<BracketOperationResult<bool>> DeleteAsync(long templateId, DeleteBracketTemplateRequest request, CancellationToken ct);
    Task<BracketOperationResult<BracketTemplateGraphDto>> SaveGraphAsync(long versionId, SaveBracketTemplateGraphRequest request, CancellationToken ct);
    Task<BracketOperationResult<BracketValidationResultDto>> ValidateAsync(long versionId, CancellationToken ct);
    Task<BracketOperationResult<BracketValidationResultDto>> ValidateDraftAsync(long versionId, SaveBracketTemplateGraphRequest request, CancellationToken ct);
    Task<BracketOperationResult<BracketTemplateGraphDto>> GenerateAsync(long versionId, GenerateBracketTemplateRequest request, CancellationToken ct);
    Task<BracketOperationResult<BracketTemplateVersionSummaryDto>> PublishAsync(long versionId, long? userId, CancellationToken ct);
    Task<BracketOperationResult<BracketTemplateGraphDto>> CreateDraftVersionAsync(long templateId, long? userId, CancellationToken ct);
    Task<BracketOperationResult<BracketTemplateDetailDto>> CloneAsync(long sourceVersionId, string templateCode, string templateName, long? userId, CancellationToken ct);
    Task<BracketOperationResult<bool>> ArchiveAsync(long templateId, long? userId, CancellationToken ct);
    Task<BracketOperationResult<BracketTemplateGraphDto>> AddRoundAsync(long versionId, BracketTemplateRoundMutationRequest request, CancellationToken ct);
    Task<BracketOperationResult<BracketTemplateGraphDto>> UpdateRoundAsync(long versionId, string roundKey, BracketTemplateRoundMutationRequest request, CancellationToken ct);
    Task<BracketOperationResult<BracketTemplateGraphDto>> DeleteRoundAsync(long versionId, string roundKey, BracketTemplateDeleteRequest request, CancellationToken ct);
    Task<BracketOperationResult<BracketTemplateGraphDto>> AddGroupAsync(long versionId, string roundKey, BracketTemplateGroupMutationRequest request, CancellationToken ct);
    Task<BracketOperationResult<BracketTemplateGraphDto>> UpdateGroupAsync(long versionId, string groupKey, BracketTemplateGroupMutationRequest request, CancellationToken ct);
    Task<BracketOperationResult<BracketTemplateGraphDto>> DeleteGroupAsync(long versionId, string groupKey, BracketTemplateDeleteRequest request, CancellationToken ct);
    Task<BracketOperationResult<BracketTemplateGraphDto>> AddMatchAsync(long versionId, string groupKey, BracketTemplateMatchMutationRequest request, CancellationToken ct);
    Task<BracketOperationResult<BracketTemplateGraphDto>> UpdateMatchAsync(long versionId, string matchKey, BracketTemplateMatchMutationRequest request, CancellationToken ct);
    Task<BracketOperationResult<BracketTemplateGraphDto>> DeleteMatchAsync(long versionId, string matchKey, BracketTemplateDeleteRequest request, CancellationToken ct);
    Task<BracketOperationResult<BracketTemplateGraphDto>> UpdateSlotAsync(long versionId, string matchKey, byte slotNumber, BracketTemplateSlotMutationRequest request, CancellationToken ct);
    Task<BracketOperationResult<BracketTemplateSourceOptionsDto>> GetSourceOptionsAsync(long versionId, string matchKey, CancellationToken ct);
}

public sealed class BracketTemplateService : IBracketTemplateService
{
    private static readonly JsonSerializerOptions DraftJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly PickleballDbContext _db;
    private readonly IBracketTemplateValidationService _validator;
    private readonly ILogger<BracketTemplateService> _logger;

    public BracketTemplateService(
        PickleballDbContext db,
        IBracketTemplateValidationService validator,
        ILogger<BracketTemplateService> logger)
    {
        _db = db;
        _validator = validator;
        _logger = logger;
    }

    public async Task<string> GetNextCodeAsync(CancellationToken ct)
    {
        const string prefix = "TP_";
        var codes = await _db.BracketTemplates.AsNoTracking()
            .Where(x => x.TemplateCode.StartsWith(prefix))
            .Select(x => x.TemplateCode)
            .ToListAsync(ct);

        var highestNumber = 0;
        foreach (var code in codes)
        {
            if (int.TryParse(code[prefix.Length..], out var number) && number > highestNumber)
                highestNumber = number;
        }

        return $"{prefix}{highestNumber + 1:D2}";
    }

    public async Task<PagedBracketTemplateListDto> ListAsync(
        string? search,
        string? status,
        string? formatType,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var query = _db.BracketTemplates.AsNoTracking().AsQueryable();
        var normalizedSearch = TrimToNull(search);
        if (normalizedSearch != null)
            query = query.Where(x => x.TemplateCode.Contains(normalizedSearch) || x.TemplateName.Contains(normalizedSearch));
        var normalizedStatus = Normalize(status);
        if (normalizedStatus.Length > 0)
            query = query.Where(x => x.Status == normalizedStatus);
        var normalizedFormat = Normalize(formatType);
        if (normalizedFormat.Length > 0)
            query = query.Where(x => x.FormatType == normalizedFormat);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var totalItems = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new BracketTemplateListItemDto
            {
                BracketTemplateId = x.BracketTemplateId,
                TemplateCode = x.TemplateCode,
                TemplateName = x.TemplateName,
                Description = x.Description,
                FormatType = x.FormatType,
                Status = x.Status,
                CurrentPublishedVersionId = x.CurrentPublishedVersionId,
                CurrentVersionNumber = x.CurrentPublishedVersionId.HasValue
                    ? x.Versions.Where(v => v.BracketTemplateVersionId == x.CurrentPublishedVersionId).Select(v => (int?)v.VersionNumber).FirstOrDefault()
                    : null,
                MinimumTeams = x.CurrentPublishedVersionId.HasValue
                    ? x.Versions.Where(v => v.BracketTemplateVersionId == x.CurrentPublishedVersionId).Select(v => (int?)v.MinimumTeams).FirstOrDefault()
                    : x.Versions.Where(v => v.Status == BracketTemplateStatuses.Draft).Select(v => (int?)v.MinimumTeams).FirstOrDefault(),
                SeedCapacity = x.CurrentPublishedVersionId.HasValue
                    ? x.Versions.Where(v => v.BracketTemplateVersionId == x.CurrentPublishedVersionId).Select(v => (int?)v.SeedCapacity).FirstOrDefault()
                    : x.Versions.Where(v => v.Status == BracketTemplateStatuses.Draft).Select(v => (int?)v.SeedCapacity).FirstOrDefault(),
                AllowBye = x.CurrentPublishedVersionId.HasValue
                    ? x.Versions.Where(v => v.BracketTemplateVersionId == x.CurrentPublishedVersionId).Select(v => (bool?)v.AllowBye).FirstOrDefault()
                    : x.Versions.Where(v => v.Status == BracketTemplateStatuses.Draft).Select(v => (bool?)v.AllowBye).FirstOrDefault(),
                DefaultSeedingMethod = x.CurrentPublishedVersionId.HasValue
                    ? x.Versions.Where(v => v.BracketTemplateVersionId == x.CurrentPublishedVersionId).Select(v => v.DefaultSeedingMethod).FirstOrDefault()
                    : x.Versions.Where(v => v.Status == BracketTemplateStatuses.Draft).Select(v => v.DefaultSeedingMethod).FirstOrDefault(),
                RoundCount = x.CurrentPublishedVersionId.HasValue
                    ? x.Versions.Where(v => v.BracketTemplateVersionId == x.CurrentPublishedVersionId).SelectMany(v => v.Rounds).Count()
                    : x.Versions.Where(v => v.Status == BracketTemplateStatuses.Draft).SelectMany(v => v.Rounds).Count(),
                GroupCount = x.CurrentPublishedVersionId.HasValue
                    ? x.Versions.Where(v => v.BracketTemplateVersionId == x.CurrentPublishedVersionId).SelectMany(v => v.Rounds).SelectMany(r => r.Groups).Count()
                    : x.Versions.Where(v => v.Status == BracketTemplateStatuses.Draft).SelectMany(v => v.Rounds).SelectMany(r => r.Groups).Count(),
                MatchCount = x.CurrentPublishedVersionId.HasValue
                    ? x.Versions.Where(v => v.BracketTemplateVersionId == x.CurrentPublishedVersionId).SelectMany(v => v.Rounds).SelectMany(r => r.Groups).SelectMany(g => g.Matches).Count()
                    : x.Versions.Where(v => v.Status == BracketTemplateStatuses.Draft).SelectMany(v => v.Rounds).SelectMany(r => r.Groups).SelectMany(g => g.Matches).Count(),
                ApplicationCount = x.Applications.Count,
                CurrentVersionApplicationCount = x.CurrentPublishedVersionId.HasValue
                    ? x.Applications.Count(a => a.BracketTemplateVersionId == x.CurrentPublishedVersionId.Value)
                    : 0,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                UpdatedByName = x.UpdatedByUser != null ? x.UpdatedByUser.FullName : null,
                RowVersion = Convert.ToBase64String(x.RowVersion)
            })
            .ToListAsync(ct);

        var draftTemplateIds = items
            .Where(x => !x.CurrentPublishedVersionId.HasValue)
            .Select(x => x.BracketTemplateId)
            .ToList();
        if (draftTemplateIds.Count > 0)
        {
            var draftSnapshots = await _db.BracketTemplateVersions.AsNoTracking()
                .Where(x => draftTemplateIds.Contains(x.BracketTemplateId)
                            && x.Status == BracketTemplateStatuses.Draft
                            && x.DraftGraphJson != null)
                .Select(x => new { x.BracketTemplateId, x.DraftGraphJson })
                .ToListAsync(ct);
            foreach (var snapshot in draftSnapshots)
            {
                var item = items.FirstOrDefault(x => x.BracketTemplateId == snapshot.BracketTemplateId);
                if (item == null || !TryReadDraft(snapshot.DraftGraphJson, out var draft))
                    continue;
                item.MinimumTeams = draft.MinimumTeams;
                item.SeedCapacity = draft.SeedCapacity;
                item.AllowBye = draft.AllowBye;
                item.DefaultSeedingMethod = Normalize(draft.DefaultSeedingMethod);
                item.RoundCount = draft.Rounds.Count;
                item.GroupCount = draft.Rounds.Sum(x => x.Groups.Count);
                item.MatchCount = draft.Rounds.Sum(x => x.Groups.Sum(g => g.Matches.Count));
            }
        }

        return new PagedBracketTemplateListDto
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize))
        };
    }

    public async Task<BracketTemplateDetailDto?> GetAsync(long templateId, CancellationToken ct)
    {
        var entity = await _db.BracketTemplates.AsNoTracking()
            .Include(x => x.Versions)
            .FirstOrDefaultAsync(x => x.BracketTemplateId == templateId, ct);
        if (entity == null)
            return null;

        var applicationCount = await _db.TournamentBracketApplications.AsNoTracking()
            .CountAsync(x => x.BracketTemplateId == templateId, ct);
        var detail = MapDetail(entity, applicationCount);
        var applicationCountsByVersion = await _db.TournamentBracketApplications.AsNoTracking()
            .Where(x => x.BracketTemplateId == templateId)
            .GroupBy(x => x.BracketTemplateVersionId)
            .Select(x => new { VersionId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.VersionId, x => x.Count, ct);
        foreach (var version in detail.Versions)
        {
            version.ApplicationCount = applicationCountsByVersion.GetValueOrDefault(
                version.BracketTemplateVersionId);
        }
        return detail;
    }

    public async Task<BracketTemplateGraphDto?> GetGraphAsync(long versionId, CancellationToken ct)
    {
        var version = await _db.BracketTemplateVersions.AsNoTracking()
            .Include(x => x.Rounds.OrderBy(r => r.SortOrder).ThenBy(r => r.RoundKey))
                .ThenInclude(x => x.Groups.OrderBy(g => g.SortOrder).ThenBy(g => g.GroupKey))
                    .ThenInclude(x => x.Matches.OrderBy(m => m.SortOrder).ThenBy(m => m.MatchKey))
                        .ThenInclude(x => x.Slots.OrderBy(s => s.SlotNumber))
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.BracketTemplateVersionId == versionId, ct);
        if (version == null)
            return null;

        if (!string.IsNullOrWhiteSpace(version.DraftGraphJson))
        {
            try
            {
                var draft = JsonSerializer.Deserialize<SaveBracketTemplateGraphRequest>(
                    version.DraftGraphJson,
                    DraftJsonOptions);
                if (draft != null)
                {
                    if (version.Status == BracketTemplateStatuses.Draft)
                    {
                        var draftGraph = MapInputGraph(version, draft);
                        draftGraph.RowVersion = Convert.ToBase64String(version.RowVersion);
                        draftGraph.ConfigurationHash = version.ConfigurationHash;
                        return draftGraph;
                    }

                    var publishedGraph = MapGraph(version);
                    ApplyDraftGroupColors(publishedGraph, draft);
                    return publishedGraph;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "Could not read DraftGraphJson for bracket template version {VersionId}; falling back to normalized graph.",
                    versionId);
            }
        }

        return MapGraph(version);
    }

    public async Task<BracketOperationResult<BracketTemplateDetailDto>> CreateAsync(
        CreateBracketTemplateRequest request,
        long? userId,
        CancellationToken ct)
    {
        var code = NormalizeCode(request.TemplateCode);
        var name = TrimToNull(request.TemplateName);
        var format = Normalize(request.FormatType);
        const string seeding = BracketSeedingMethods.RegistrationOrder;

        if (code == null)
            return BracketOperationResult<BracketTemplateDetailDto>.Fail("TEMPLATE_CODE_REQUIRED", "Vui lòng nhập mã template.");
        if (name == null)
            return BracketOperationResult<BracketTemplateDetailDto>.Fail("TEMPLATE_NAME_REQUIRED", "Vui lòng nhập tên template.");
        if (!IsFormatType(format))
            return BracketOperationResult<BracketTemplateDetailDto>.Fail("FORMAT_INVALID", "Loại bracket không hợp lệ.");
        if (request.MinimumTeams is < 2 or > 1024)
            return BracketOperationResult<BracketTemplateDetailDto>.Fail("MINIMUM_TEAMS_INVALID", "Số đội tối thiểu phải nằm trong khoảng 2 đến 1024.");
        if (request.SeedCapacity is < 2 or > 1024)
            return BracketOperationResult<BracketTemplateDetailDto>.Fail("SEED_CAPACITY_INVALID", "Số đội tối đa phải nằm trong khoảng 2 đến 1024.");
        if (request.MinimumTeams > request.SeedCapacity)
            return BracketOperationResult<BracketTemplateDetailDto>.Fail("TEAM_RANGE_INVALID", "Số đội tối thiểu không được lớn hơn số đội tối đa.");
        if (await _db.BracketTemplates.AnyAsync(x => x.TemplateCode == code, ct))
            return BracketOperationResult<BracketTemplateDetailDto>.Fail("TEMPLATE_CODE_DUPLICATE", "Mã template đã tồn tại.");

        var now = DateTime.UtcNow;
        var template = new BracketTemplate
        {
            TemplateCode = code,
            TemplateName = name,
            Description = TrimToNull(request.Description),
            FormatType = format,
            Status = BracketTemplateStatuses.Draft,
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now
        };
        template.Versions.Add(new BracketTemplateVersion
        {
            VersionNumber = 1,
            Status = BracketTemplateStatuses.Draft,
            MinimumTeams = request.MinimumTeams,
            SeedCapacity = request.SeedCapacity,
            AllowBye = request.MinimumTeams < request.SeedCapacity,
            DefaultSeedingMethod = seeding,
            CreatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now
        });

        _db.BracketTemplates.Add(template);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Bracket template {TemplateId} ({TemplateCode}) created by user {UserId}.",
            template.BracketTemplateId, template.TemplateCode, userId);
        var created = await GetAsync(template.BracketTemplateId, ct);
        return BracketOperationResult<BracketTemplateDetailDto>.Ok(created!, "Đã tạo template draft.");
    }

    public async Task<BracketOperationResult<BracketTemplateDetailDto>> UpdateAsync(
        long templateId,
        UpdateBracketTemplateRequest request,
        long? userId,
        CancellationToken ct)
    {
        var template = await _db.BracketTemplates.FirstOrDefaultAsync(x => x.BracketTemplateId == templateId, ct);
        if (template == null)
            return BracketOperationResult<BracketTemplateDetailDto>.Fail("TEMPLATE_NOT_FOUND", "Không tìm thấy template.");
        if (template.Status == BracketTemplateStatuses.Archived)
            return BracketOperationResult<BracketTemplateDetailDto>.Fail("TEMPLATE_ARCHIVED", "Template đã archive.");
        if (!MatchesRowVersion(template.RowVersion, request.RowVersion))
            return BracketOperationResult<BracketTemplateDetailDto>.Fail("CONCURRENCY_CONFLICT", "Template đã được người khác cập nhật. Vui lòng tải lại.");

        var name = TrimToNull(request.TemplateName);
        var format = Normalize(request.FormatType);
        if (name == null)
            return BracketOperationResult<BracketTemplateDetailDto>.Fail("TEMPLATE_NAME_REQUIRED", "Vui lòng nhập tên template.");
        if (!IsFormatType(format))
            return BracketOperationResult<BracketTemplateDetailDto>.Fail("FORMAT_INVALID", "Loại bracket không hợp lệ.");

        template.TemplateName = name;
        template.Description = TrimToNull(request.Description);
        template.FormatType = format;
        template.UpdatedByUserId = userId;
        template.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Bracket template {TemplateId} metadata updated by user {UserId}.",
            templateId, userId);

        var updated = await GetAsync(templateId, ct);
        return BracketOperationResult<BracketTemplateDetailDto>.Ok(updated!, "Đã cập nhật template.");
    }

    public async Task<BracketOperationResult<BracketTemplateDetailDto>> UpdateSettingsAsync(
        long templateId,
        UpdateBracketTemplateSettingsRequest request,
        long? userId,
        CancellationToken ct)
    {
        var name = TrimToNull(request.TemplateName);
        if (name == null)
            return BracketOperationResult<BracketTemplateDetailDto>.Fail("TEMPLATE_NAME_REQUIRED", "Vui lòng nhập tên template.");
        if (name.Length > 150)
            return BracketOperationResult<BracketTemplateDetailDto>.Fail("TEMPLATE_NAME_TOO_LONG", "Tên template không được vượt quá 150 ký tự.");
        if (request.MinimumTeams is < 2 or > 1024)
            return BracketOperationResult<BracketTemplateDetailDto>.Fail("MINIMUM_TEAMS_INVALID", "Số đội tối thiểu phải nằm trong khoảng 2 đến 1024.");
        if (request.SeedCapacity is < 2 or > 1024)
            return BracketOperationResult<BracketTemplateDetailDto>.Fail("SEED_CAPACITY_INVALID", "Số đội tối đa phải nằm trong khoảng 2 đến 1024.");
        if (request.MinimumTeams > request.SeedCapacity)
            return BracketOperationResult<BracketTemplateDetailDto>.Fail("TEAM_RANGE_INVALID", "Số đội tối thiểu không được lớn hơn số đội tối đa.");

        var template = await _db.BracketTemplates
            .Include(x => x.Versions)
            .FirstOrDefaultAsync(x => x.BracketTemplateId == templateId, ct);
        if (template == null)
            return BracketOperationResult<BracketTemplateDetailDto>.Fail("TEMPLATE_NOT_FOUND", "Không tìm thấy template.");
        if (!MatchesRowVersion(template.RowVersion, request.RowVersion))
            return BracketOperationResult<BracketTemplateDetailDto>.Fail("CONCURRENCY_CONFLICT", "Template đã được cập nhật. Vui lòng tải lại.");

        var version = template.CurrentPublishedVersionId.HasValue
            ? template.Versions.FirstOrDefault(x => x.BracketTemplateVersionId == template.CurrentPublishedVersionId.Value)
            : template.Versions.FirstOrDefault(x => x.Status == BracketTemplateStatuses.Draft);
        if (version == null)
            return BracketOperationResult<BracketTemplateDetailDto>.Fail("VERSION_NOT_FOUND", "Template chưa có version để cập nhật sức chứa.");

        version.MinimumTeams = request.MinimumTeams;
        version.SeedCapacity = request.SeedCapacity;
        version.AllowBye = request.MinimumTeams < request.SeedCapacity;
        version.UpdatedAt = DateTime.UtcNow;

        SaveBracketTemplateGraphRequest? jsonGraph = null;
        if (TryReadDraft(version.DraftGraphJson, out var draftGraph))
        {
            draftGraph.MinimumTeams = request.MinimumTeams;
            draftGraph.SeedCapacity = request.SeedCapacity;
            draftGraph.AllowBye = version.AllowBye;
            version.DraftGraphJson = JsonSerializer.Serialize(draftGraph, DraftJsonOptions);
            jsonGraph = draftGraph;
        }

        if (version.Status == BracketTemplateStatuses.Published)
        {
            BracketTemplateGraphDto? hashGraph;
            if (jsonGraph != null)
            {
                hashGraph = MapInputGraph(version, jsonGraph);
            }
            else
            {
                hashGraph = await GetGraphAsync(version.BracketTemplateVersionId, ct);
                if (hashGraph != null)
                {
                    hashGraph.MinimumTeams = request.MinimumTeams;
                    hashGraph.SeedCapacity = request.SeedCapacity;
                    hashGraph.AllowBye = version.AllowBye;
                }
            }

            if (hashGraph != null)
                version.ConfigurationHash = ComputeGraphHash(hashGraph);
        }

        template.TemplateName = name;
        template.UpdatedByUserId = userId;
        template.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return BracketOperationResult<BracketTemplateDetailDto>.Fail("CONCURRENCY_CONFLICT", "Template đã được cập nhật. Vui lòng tải lại.");
        }

        _logger.LogInformation(
            "Bracket template {TemplateId} settings updated to {MinimumTeams}-{SeedCapacity} teams by user {UserId}.",
            templateId,
            request.MinimumTeams,
            request.SeedCapacity,
            userId);
        return BracketOperationResult<BracketTemplateDetailDto>.Ok(
            (await GetAsync(templateId, ct))!,
            "Đã cập nhật tên và giới hạn số đội.");
    }

    public async Task<BracketOperationResult<bool>> DeleteAsync(
        long templateId,
        DeleteBracketTemplateRequest request,
        CancellationToken ct)
    {
        if (!string.Equals(request.Confirmation, "XOA", StringComparison.Ordinal))
            return BracketOperationResult<bool>.Fail("DELETE_CONFIRMATION_INVALID", "Vui lòng nhập đúng XOA để xác nhận xóa template.");

        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;
        try
        {
            var template = await _db.BracketTemplates
                .Include(x => x.Versions)
                .FirstOrDefaultAsync(x => x.BracketTemplateId == templateId, ct);
            if (template == null)
                return BracketOperationResult<bool>.Fail("TEMPLATE_NOT_FOUND", "Không tìm thấy template.");
            if (!MatchesRowVersion(template.RowVersion, request.RowVersion))
                return BracketOperationResult<bool>.Fail("CONCURRENCY_CONFLICT", "Template đã được cập nhật. Vui lòng tải lại.");

            var applicationCount = await _db.TournamentBracketApplications
                .CountAsync(x => x.BracketTemplateId == templateId, ct);
            if (applicationCount > 0)
            {
                return BracketOperationResult<bool>.Fail(
                    "TEMPLATE_IN_USE",
                    $"Không thể xóa vì template đã được áp dụng {applicationCount} lần. Bạn có thể lưu trữ template thay thế.");
            }

            template.CurrentPublishedVersionId = null;
            await _db.SaveChangesAsync(ct);

            foreach (var versionId in template.Versions.Select(x => x.BracketTemplateVersionId).ToList())
                await DeleteVersionGraphAsync(versionId, ct);

            _db.BracketTemplateVersions.RemoveRange(template.Versions);
            _db.BracketTemplates.Remove(template);
            await _db.SaveChangesAsync(ct);
            if (transaction != null)
                await transaction.CommitAsync(ct);

            _logger.LogInformation("Bracket template {TemplateId} permanently deleted.", templateId);
            return BracketOperationResult<bool>.Ok(true, "Đã xóa template.");
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction != null)
                await transaction.RollbackAsync(ct);
            return BracketOperationResult<bool>.Fail("CONCURRENCY_CONFLICT", "Template đã được cập nhật. Vui lòng tải lại.");
        }
        catch (DbUpdateException ex)
        {
            if (transaction != null)
                await transaction.RollbackAsync(ct);
            _logger.LogWarning(ex, "Could not delete bracket template {TemplateId}.", templateId);
            return BracketOperationResult<bool>.Fail("TEMPLATE_DELETE_FAILED", "Không thể xóa template vì vẫn còn dữ liệu liên kết.");
        }
    }

    public async Task<BracketOperationResult<BracketTemplateGraphDto>> SaveGraphAsync(
        long versionId,
        SaveBracketTemplateGraphRequest request,
        CancellationToken ct)
    {
        var version = await _db.BracketTemplateVersions.FirstOrDefaultAsync(x => x.BracketTemplateVersionId == versionId, ct);
        if (version == null)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("VERSION_NOT_FOUND", "Không tìm thấy template version.");
        if (version.Status != BracketTemplateStatuses.Draft)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("VERSION_IMMUTABLE", "Chỉ version draft mới được chỉnh sửa.");
        if (string.IsNullOrWhiteSpace(request.RowVersion)
            || !MatchesRowVersion(version.RowVersion, request.RowVersion))
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("CONCURRENCY_CONFLICT", "Version đã được cập nhật. Vui lòng tải lại.");

        var inputGraph = MapInputGraph(version, request);
        var validation = _validator.Validate(inputGraph);
        version.MinimumTeams = inputGraph.MinimumTeams;
        version.SeedCapacity = inputGraph.SeedCapacity;
        version.AllowBye = inputGraph.AllowBye;
        version.DefaultSeedingMethod = inputGraph.DefaultSeedingMethod;
        version.DraftGraphJson = SerializeDraftGraph(inputGraph);
        version.UpdatedAt = DateTime.UtcNow;
        if (!validation.IsValid)
        {
            version.ConfigurationHash = null;
            await _db.SaveChangesAsync(ct);
            inputGraph.ConfigurationHash = null;
            inputGraph.RowVersion = Convert.ToBase64String(version.RowVersion);
            inputGraph.Validation = validation;
            _logger.LogInformation(
                "Incomplete bracket template draft {VersionId} saved with {ErrorCount} validation errors.",
                versionId,
                validation.ErrorCount);
            return BracketOperationResult<BracketTemplateGraphDto>.Ok(
                inputGraph,
                $"Đã lưu draft đang làm dở; còn {validation.ErrorCount} lỗi cần xử lý trước khi publish.");
        }

        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;
        try
        {
            await DeleteVersionGraphAsync(versionId, ct);

            version.MinimumTeams = inputGraph.MinimumTeams;
            version.SeedCapacity = inputGraph.SeedCapacity;
            version.AllowBye = inputGraph.AllowBye;
            version.DefaultSeedingMethod = inputGraph.DefaultSeedingMethod;
            version.ConfigurationHash = null;
            version.DraftGraphJson = SerializeDraftGraph(inputGraph);
            version.UpdatedAt = DateTime.UtcNow;

            var roundEntities = new List<BracketTemplateRound>();
            var groupByKey = new Dictionary<string, BracketTemplateGroup>(StringComparer.OrdinalIgnoreCase);
            var matchByKey = new Dictionary<string, BracketTemplateMatch>(StringComparer.OrdinalIgnoreCase);

            foreach (var roundInput in inputGraph.Rounds)
            {
                var round = new BracketTemplateRound
                {
                    BracketTemplateVersionId = versionId,
                    RoundKey = NormalizeCode(roundInput.RoundKey)!,
                    RoundLabel = TrimToNull(roundInput.RoundLabel)!,
                    RoundType = Normalize(roundInput.RoundType),
                    SortOrder = roundInput.SortOrder,
                    CreatedAt = DateTime.UtcNow
                };

                foreach (var groupInput in roundInput.Groups)
                {
                    var group = new BracketTemplateGroup
                    {
                        BracketTemplateVersionId = versionId,
                        GroupKey = NormalizeCode(groupInput.GroupKey)!,
                        GroupName = TrimToNull(groupInput.GroupName)!,
                        GroupType = Normalize(groupInput.GroupType),
                        SortOrder = groupInput.SortOrder,
                        CreatedAt = DateTime.UtcNow
                    };
                    groupByKey[group.GroupKey] = group;

                    foreach (var matchInput in groupInput.Matches)
                    {
                        var match = new BracketTemplateMatch
                        {
                            BracketTemplateVersionId = versionId,
                            MatchKey = NormalizeCode(matchInput.MatchKey)!,
                            MatchLabel = TrimToNull(matchInput.MatchLabel),
                            SortOrder = matchInput.SortOrder,
                            IsTerminal = matchInput.IsTerminal,
                            TerminalType = matchInput.IsTerminal ? Normalize(matchInput.TerminalType) : null,
                            CreatedAt = DateTime.UtcNow
                        };
                        matchByKey[match.MatchKey] = match;
                        group.Matches.Add(match);
                    }
                    round.Groups.Add(group);
                }
                roundEntities.Add(round);
            }

            _db.BracketTemplateRounds.AddRange(roundEntities);
            await _db.SaveChangesAsync(ct);

            var slots = new List<BracketTemplateMatchSlot>();
            foreach (var roundInput in inputGraph.Rounds)
            foreach (var groupInput in roundInput.Groups)
            foreach (var matchInput in groupInput.Matches)
            {
                var match = matchByKey[NormalizeCode(matchInput.MatchKey)!];
                foreach (var slotInput in matchInput.Slots)
                {
                    var sourceType = Normalize(slotInput.SourceType);
                    slots.Add(new BracketTemplateMatchSlot
                    {
                        BracketTemplateVersionId = versionId,
                        BracketTemplateMatchId = match.BracketTemplateMatchId,
                        SlotNumber = slotInput.SlotNumber,
                        SourceType = sourceType,
                        SeedNumber = sourceType == BracketTemplateSourceTypes.Seed ? slotInput.SeedNumber : null,
                        SourceMatchId = sourceType is BracketTemplateSourceTypes.WinnerMatch or BracketTemplateSourceTypes.LoserMatch
                            ? matchByKey[NormalizeCode(slotInput.SourceMatchKey)!].BracketTemplateMatchId
                            : null,
                        SourceGroupId = sourceType == BracketTemplateSourceTypes.GroupRank
                            ? groupByKey[NormalizeCode(slotInput.SourceGroupKey)!].BracketTemplateGroupId
                            : null,
                        SourceRank = sourceType == BracketTemplateSourceTypes.GroupRank ? slotInput.SourceRank : null,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
            _db.BracketTemplateMatchSlots.AddRange(slots);
            await _db.SaveChangesAsync(ct);
            if (transaction != null)
                await transaction.CommitAsync(ct);
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync(ct);
            throw;
        }

        var graph = await GetGraphAsync(versionId, ct);
        graph!.Validation = validation;
        _logger.LogInformation(
            "Bracket template version {VersionId} graph saved: {RoundCount} rounds, {GroupCount} groups, {MatchCount} matches.",
            versionId,
            graph.Rounds.Count,
            graph.Rounds.Sum(x => x.Groups.Count),
            graph.Rounds.Sum(x => x.Groups.Sum(g => g.Matches.Count)));
        return BracketOperationResult<BracketTemplateGraphDto>.Ok(graph!, "Đã lưu cấu trúc template.");
    }

    public async Task<BracketOperationResult<BracketValidationResultDto>> ValidateAsync(long versionId, CancellationToken ct)
    {
        var graph = await GetGraphAsync(versionId, ct);
        if (graph == null)
            return BracketOperationResult<BracketValidationResultDto>.Fail("VERSION_NOT_FOUND", "Không tìm thấy template version.");
        return BracketOperationResult<BracketValidationResultDto>.Ok(_validator.Validate(graph));
    }

    public async Task<BracketOperationResult<BracketValidationResultDto>> ValidateDraftAsync(
        long versionId,
        SaveBracketTemplateGraphRequest request,
        CancellationToken ct)
    {
        var version = await _db.BracketTemplateVersions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.BracketTemplateVersionId == versionId, ct);
        if (version == null)
            return BracketOperationResult<BracketValidationResultDto>.Fail("VERSION_NOT_FOUND", "Không tìm thấy template version.");

        return BracketOperationResult<BracketValidationResultDto>.Ok(
            _validator.Validate(MapInputGraph(version, request)));
    }

    public async Task<BracketOperationResult<BracketTemplateGraphDto>> GenerateAsync(
        long versionId,
        GenerateBracketTemplateRequest request,
        CancellationToken ct)
    {
        var version = await _db.BracketTemplateVersions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.BracketTemplateVersionId == versionId, ct);
        if (version == null)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("VERSION_NOT_FOUND", "Không tìm thấy template version.");
        if (version.Status != BracketTemplateStatuses.Draft)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("VERSION_IMMUTABLE", "Chỉ version draft mới được sinh cấu trúc.");

        SaveBracketTemplateGraphRequest generated;
        var generatorType = Normalize(request.GeneratorType);
        if (generatorType == "SINGLE_ELIMINATION")
        {
            if (!IsPowerOfTwo(request.TeamCount) || request.TeamCount is < 4 or > 64)
                return BracketOperationResult<BracketTemplateGraphDto>.Fail("TEAM_COUNT_INVALID", "Knockout hỗ trợ 4, 8, 16, 32 hoặc 64 đội.");
            generated = GenerateSingleElimination(request.TeamCount, request.IncludeThirdPlace, version.RowVersion);
        }
        else if (generatorType == "GROUP_KNOCKOUT")
        {
            if (request.GroupCount is < 2 or > 16 || request.TeamsPerGroup is < 2 or > 16 || request.QualifiersPerGroup != 2)
                return BracketOperationResult<BracketTemplateGraphDto>.Fail("GROUP_CONFIG_INVALID", "MVP yêu cầu 2-16 bảng, 2-16 đội/bảng và lấy đúng 2 đội mỗi bảng.");
            if (!IsPowerOfTwo(request.GroupCount * request.QualifiersPerGroup))
                return BracketOperationResult<BracketTemplateGraphDto>.Fail("KNOCKOUT_CAPACITY_INVALID", "Tổng số đội đi tiếp phải là lũy thừa của 2.");
            generated = GenerateGroupKnockout(request.GroupCount, request.TeamsPerGroup, request.IncludeThirdPlace, version.RowVersion);
        }
        else
        {
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("GENERATOR_INVALID", "Loại sinh bracket không hợp lệ.");
        }

        return await SaveGraphAsync(versionId, generated, ct);
    }

    public async Task<BracketOperationResult<BracketTemplateVersionSummaryDto>> PublishAsync(
        long versionId,
        long? userId,
        CancellationToken ct)
    {
        var graph = await GetGraphAsync(versionId, ct);
        if (graph == null)
            return BracketOperationResult<BracketTemplateVersionSummaryDto>.Fail("VERSION_NOT_FOUND", "Không tìm thấy template version.");
        if (graph.Status != BracketTemplateStatuses.Draft)
            return BracketOperationResult<BracketTemplateVersionSummaryDto>.Fail("VERSION_IMMUTABLE", "Version này không còn ở trạng thái draft.");

        var validation = _validator.Validate(graph);
        if (!validation.IsValid)
            return BracketOperationResult<BracketTemplateVersionSummaryDto>.Fail("GRAPH_INVALID", "Template còn lỗi và chưa thể publish.");

        // Drafts are loaded from DraftGraphJson so they can retain incomplete work
        // without replacing the last normalized graph. Before changing the status,
        // always materialize the exact validated draft into the relational graph.
        // Otherwise a published reload switches to the normalized tables and can
        // appear empty even though the JSON draft still contains the full bracket.
        var synchronized = await SaveGraphAsync(
            versionId,
            GraphToSaveRequest(graph, graph.RowVersion),
            ct);
        if (!synchronized.Success || synchronized.Data == null)
        {
            return BracketOperationResult<BracketTemplateVersionSummaryDto>.Fail(
                synchronized.ErrorCode ?? "GRAPH_SYNC_FAILED",
                synchronized.Message ?? "Không thể đồng bộ cấu trúc template trước khi publish.");
        }

        graph = synchronized.Data;
        validation = _validator.Validate(graph);
        if (!validation.IsValid)
            return BracketOperationResult<BracketTemplateVersionSummaryDto>.Fail("GRAPH_INVALID", "Template còn lỗi và chưa thể publish.");

        var expectedRoundCount = graph.Rounds.Count;
        var expectedGroupCount = graph.Rounds.Sum(x => x.Groups.Count);
        var expectedMatchCount = graph.Rounds.Sum(x => x.Groups.Sum(g => g.Matches.Count));
        var expectedSlotCount = graph.Rounds.Sum(x => x.Groups.Sum(g => g.Matches.Sum(m => m.Slots.Count)));
        var actualRoundCount = await _db.BracketTemplateRounds.AsNoTracking()
            .CountAsync(x => x.BracketTemplateVersionId == versionId, ct);
        var actualGroupCount = await _db.BracketTemplateGroups.AsNoTracking()
            .CountAsync(x => x.BracketTemplateVersionId == versionId, ct);
        var actualMatchCount = await _db.BracketTemplateMatches.AsNoTracking()
            .CountAsync(x => x.BracketTemplateVersionId == versionId, ct);
        var actualSlotCount = await _db.BracketTemplateMatchSlots.AsNoTracking()
            .CountAsync(x => x.BracketTemplateVersionId == versionId, ct);
        if (actualRoundCount != expectedRoundCount
            || actualGroupCount != expectedGroupCount
            || actualMatchCount != expectedMatchCount
            || actualSlotCount != expectedSlotCount)
        {
            _logger.LogError(
                "Refusing to publish bracket template version {VersionId}: graph synchronization mismatch. Expected {ExpectedRounds}/{ExpectedGroups}/{ExpectedMatches}/{ExpectedSlots}, persisted {ActualRounds}/{ActualGroups}/{ActualMatches}/{ActualSlots}.",
                versionId,
                expectedRoundCount,
                expectedGroupCount,
                expectedMatchCount,
                expectedSlotCount,
                actualRoundCount,
                actualGroupCount,
                actualMatchCount,
                actualSlotCount);
            return BracketOperationResult<BracketTemplateVersionSummaryDto>.Fail(
                "GRAPH_SYNC_FAILED",
                "Cấu trúc template chưa được đồng bộ đầy đủ. Hệ thống đã dừng publish để bảo vệ dữ liệu.");
        }

        var version = await _db.BracketTemplateVersions
            .Include(x => x.BracketTemplate)
            .FirstAsync(x => x.BracketTemplateVersionId == versionId, ct);
        var now = DateTime.UtcNow;
        version.Status = BracketTemplateStatuses.Published;
        version.ConfigurationHash = ComputeGraphHash(graph);
        version.PublishedAt = now;
        version.PublishedByUserId = userId;
        version.UpdatedAt = now;
        version.BracketTemplate.Status = BracketTemplateStatuses.Published;
        version.BracketTemplate.CurrentPublishedVersionId = versionId;
        version.BracketTemplate.UpdatedByUserId = userId;
        version.BracketTemplate.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Bracket template version {VersionId} published for template {TemplateId} by user {UserId}; hash {ConfigurationHash}.",
            version.BracketTemplateVersionId, version.BracketTemplateId, userId, version.ConfigurationHash);

        return BracketOperationResult<BracketTemplateVersionSummaryDto>.Ok(MapVersion(version), "Đã publish template version.");
    }

    public async Task<BracketOperationResult<BracketTemplateGraphDto>> CreateDraftVersionAsync(
        long templateId,
        long? userId,
        CancellationToken ct)
    {
        var template = await _db.BracketTemplates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.BracketTemplateId == templateId, ct);
        if (template == null)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("TEMPLATE_NOT_FOUND", "Không tìm thấy template.");
        if (template.Status == BracketTemplateStatuses.Archived)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("TEMPLATE_ARCHIVED", "Template đã archive.");
        if (await _db.BracketTemplateVersions.AnyAsync(x => x.BracketTemplateId == templateId && x.Status == BracketTemplateStatuses.Draft, ct))
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("DRAFT_EXISTS", "Template đã có một version draft.");
        if (!template.CurrentPublishedVersionId.HasValue)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("PUBLISHED_VERSION_REQUIRED", "Template chưa có version published để tạo bản mới.");

        var source = await GetGraphAsync(template.CurrentPublishedVersionId.Value, ct);
        var nextVersion = await _db.BracketTemplateVersions
            .Where(x => x.BracketTemplateId == templateId)
            .MaxAsync(x => x.VersionNumber, ct) + 1;
        var entity = new BracketTemplateVersion
        {
            BracketTemplateId = templateId,
            VersionNumber = nextVersion,
            Status = BracketTemplateStatuses.Draft,
            MinimumTeams = source!.MinimumTeams,
            SeedCapacity = source.SeedCapacity,
            AllowBye = source.AllowBye,
            DefaultSeedingMethod = source.DefaultSeedingMethod,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.BracketTemplateVersions.Add(entity);
        await _db.SaveChangesAsync(ct);

        var saveRequest = GraphToSaveRequest(source, Convert.ToBase64String(entity.RowVersion));
        return await SaveGraphAsync(entity.BracketTemplateVersionId, saveRequest, ct);
    }

    public async Task<BracketOperationResult<BracketTemplateDetailDto>> CloneAsync(
        long sourceVersionId,
        string templateCode,
        string templateName,
        long? userId,
        CancellationToken ct)
    {
        var source = await GetGraphAsync(sourceVersionId, ct);
        if (source == null)
            return BracketOperationResult<BracketTemplateDetailDto>.Fail("VERSION_NOT_FOUND", "Không tìm thấy version nguồn.");
        var sourceTemplate = await _db.BracketTemplates.AsNoTracking()
            .FirstAsync(x => x.BracketTemplateId == source.BracketTemplateId, ct);
        var create = await CreateAsync(new CreateBracketTemplateRequest
        {
            TemplateCode = templateCode,
            TemplateName = templateName,
            Description = sourceTemplate.Description,
            FormatType = sourceTemplate.FormatType,
            MinimumTeams = source.MinimumTeams,
            SeedCapacity = source.SeedCapacity,
            AllowBye = source.AllowBye,
            DefaultSeedingMethod = source.DefaultSeedingMethod
        }, userId, ct);
        if (!create.Success)
            return create;

        var draftVersion = create.Data!.Versions.Single(x => x.Status == BracketTemplateStatuses.Draft);
        var save = await SaveGraphAsync(draftVersion.BracketTemplateVersionId,
            GraphToSaveRequest(source, draftVersion.RowVersion), ct);
        if (!save.Success)
            return BracketOperationResult<BracketTemplateDetailDto>.Fail(save.ErrorCode!, save.Message!);

        return BracketOperationResult<BracketTemplateDetailDto>.Ok(
            (await GetAsync(create.Data.BracketTemplateId, ct))!, "Đã clone template.");
    }

    public async Task<BracketOperationResult<bool>> ArchiveAsync(long templateId, long? userId, CancellationToken ct)
    {
        var template = await _db.BracketTemplates.FirstOrDefaultAsync(x => x.BracketTemplateId == templateId, ct);
        if (template == null)
            return BracketOperationResult<bool>.Fail("TEMPLATE_NOT_FOUND", "Không tìm thấy template.");
        template.Status = BracketTemplateStatuses.Archived;
        template.UpdatedByUserId = userId;
        template.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Bracket template {TemplateId} archived by user {UserId}.", templateId, userId);
        return BracketOperationResult<bool>.Ok(true, "Đã archive template.");
    }

    public async Task<BracketOperationResult<BracketTemplateGraphDto>> AddRoundAsync(
        long versionId,
        BracketTemplateRoundMutationRequest request,
        CancellationToken ct)
    {
        var loaded = await LoadDraftForMutationAsync(versionId, request.RowVersion, ct);
        if (!loaded.Success)
            return loaded;

        var graph = loaded.Data!;
        var key = NormalizeCode(request.RoundKey);
        if (key == null)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("ROUND_KEY_REQUIRED", "Vui lòng nhập mã vòng.");
        if (graph.Rounds.Any(x => NormalizeCode(x.RoundKey) == key))
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("ROUND_KEY_DUPLICATE", "Mã vòng đã tồn tại.");

        graph.Rounds.Add(new BracketTemplateRoundDto
        {
            RoundKey = key,
            RoundLabel = TrimToNull(request.RoundLabel) ?? key,
            RoundType = Normalize(request.RoundType) is { Length: > 0 } roundType
                ? roundType
                : BracketRoundTypes.Knockout,
            SortOrder = request.SortOrder
        });
        return await PersistMutationAsync(graph, request.RowVersion, ct);
    }

    public async Task<BracketOperationResult<BracketTemplateGraphDto>> UpdateRoundAsync(
        long versionId,
        string roundKey,
        BracketTemplateRoundMutationRequest request,
        CancellationToken ct)
    {
        var loaded = await LoadDraftForMutationAsync(versionId, request.RowVersion, ct);
        if (!loaded.Success)
            return loaded;

        var graph = loaded.Data!;
        var currentKey = NormalizeCode(roundKey);
        var round = graph.Rounds.FirstOrDefault(x => NormalizeCode(x.RoundKey) == currentKey);
        if (round == null)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("ROUND_NOT_FOUND", "Không tìm thấy vòng trong draft.");

        var nextKey = NormalizeCode(request.RoundKey);
        if (nextKey == null)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("ROUND_KEY_REQUIRED", "Vui lòng nhập mã vòng.");
        if (graph.Rounds.Any(x => !ReferenceEquals(x, round) && NormalizeCode(x.RoundKey) == nextKey))
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("ROUND_KEY_DUPLICATE", "Mã vòng đã tồn tại.");

        round.RoundKey = nextKey;
        round.RoundLabel = TrimToNull(request.RoundLabel) ?? nextKey;
        round.RoundType = Normalize(request.RoundType);
        round.SortOrder = request.SortOrder;
        return await PersistMutationAsync(graph, request.RowVersion, ct);
    }

    public async Task<BracketOperationResult<BracketTemplateGraphDto>> DeleteRoundAsync(
        long versionId,
        string roundKey,
        BracketTemplateDeleteRequest request,
        CancellationToken ct)
    {
        var loaded = await LoadDraftForMutationAsync(versionId, request.RowVersion, ct);
        if (!loaded.Success)
            return loaded;

        var graph = loaded.Data!;
        var key = NormalizeCode(roundKey);
        var round = graph.Rounds.FirstOrDefault(x => NormalizeCode(x.RoundKey) == key);
        if (round == null)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("ROUND_NOT_FOUND", "Không tìm thấy vòng trong draft.");

        var removedMatchKeys = round.Groups.SelectMany(x => x.Matches)
            .Select(x => NormalizeCode(x.MatchKey)!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removedGroupKeys = round.Groups.Select(x => NormalizeCode(x.GroupKey)!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (HasExternalReferences(graph, removedMatchKeys, removedGroupKeys, round))
            return BracketOperationResult<BracketTemplateGraphDto>.Fail(
                "ROUND_IN_USE",
                "Không thể xóa vòng vì match/group bên trong đang được cấu trúc khác dùng làm nguồn.");

        graph.Rounds.Remove(round);
        return await PersistMutationAsync(graph, request.RowVersion, ct);
    }

    public async Task<BracketOperationResult<BracketTemplateGraphDto>> AddGroupAsync(
        long versionId,
        string roundKey,
        BracketTemplateGroupMutationRequest request,
        CancellationToken ct)
    {
        var loaded = await LoadDraftForMutationAsync(versionId, request.RowVersion, ct);
        if (!loaded.Success)
            return loaded;

        var graph = loaded.Data!;
        var round = graph.Rounds.FirstOrDefault(x => NormalizeCode(x.RoundKey) == NormalizeCode(roundKey));
        if (round == null)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("ROUND_NOT_FOUND", "Không tìm thấy vòng trong draft.");

        var key = NormalizeCode(request.GroupKey);
        if (key == null)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("GROUP_KEY_REQUIRED", "Vui lòng nhập mã bảng/nhánh.");
        if (graph.Rounds.SelectMany(x => x.Groups).Any(x => NormalizeCode(x.GroupKey) == key))
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("GROUP_KEY_DUPLICATE", "Mã bảng/nhánh đã tồn tại.");

        round.Groups.Add(new BracketTemplateGroupDto
        {
            GroupKey = key,
            GroupName = TrimToNull(request.GroupName) ?? key,
            GroupType = Normalize(request.GroupType) is { Length: > 0 } groupType
                ? groupType
                : BracketGroupTypes.Generic,
            GroupColor = NormalizeGroupColor(request.GroupColor),
            SortOrder = request.SortOrder
        });
        return await PersistMutationAsync(graph, request.RowVersion, ct);
    }

    public async Task<BracketOperationResult<BracketTemplateGraphDto>> UpdateGroupAsync(
        long versionId,
        string groupKey,
        BracketTemplateGroupMutationRequest request,
        CancellationToken ct)
    {
        var loaded = await LoadDraftForMutationAsync(versionId, request.RowVersion, ct);
        if (!loaded.Success)
            return loaded;

        var graph = loaded.Data!;
        var currentKey = NormalizeCode(groupKey);
        var group = graph.Rounds.SelectMany(x => x.Groups)
            .FirstOrDefault(x => NormalizeCode(x.GroupKey) == currentKey);
        if (group == null)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("GROUP_NOT_FOUND", "Không tìm thấy bảng/nhánh trong draft.");

        var nextKey = NormalizeCode(request.GroupKey);
        if (nextKey == null)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("GROUP_KEY_REQUIRED", "Vui lòng nhập mã bảng/nhánh.");
        if (graph.Rounds.SelectMany(x => x.Groups)
            .Any(x => !ReferenceEquals(x, group) && NormalizeCode(x.GroupKey) == nextKey))
        {
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("GROUP_KEY_DUPLICATE", "Mã bảng/nhánh đã tồn tại.");
        }

        if (!string.Equals(currentKey, nextKey, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var slot in graph.Rounds.SelectMany(x => x.Groups).SelectMany(x => x.Matches).SelectMany(x => x.Slots)
                         .Where(x => Normalize(x.SourceType) == BracketTemplateSourceTypes.GroupRank
                                     && NormalizeCode(x.SourceGroupKey) == currentKey))
            {
                slot.SourceGroupKey = nextKey;
            }
        }

        group.GroupKey = nextKey;
        group.GroupName = TrimToNull(request.GroupName) ?? nextKey;
        group.GroupType = Normalize(request.GroupType);
        group.GroupColor = NormalizeGroupColor(request.GroupColor);
        group.SortOrder = request.SortOrder;
        return await PersistMutationAsync(graph, request.RowVersion, ct);
    }

    public async Task<BracketOperationResult<BracketTemplateGraphDto>> DeleteGroupAsync(
        long versionId,
        string groupKey,
        BracketTemplateDeleteRequest request,
        CancellationToken ct)
    {
        var loaded = await LoadDraftForMutationAsync(versionId, request.RowVersion, ct);
        if (!loaded.Success)
            return loaded;

        var graph = loaded.Data!;
        var key = NormalizeCode(groupKey);
        var ownerRound = graph.Rounds.FirstOrDefault(x => x.Groups.Any(g => NormalizeCode(g.GroupKey) == key));
        var group = ownerRound?.Groups.FirstOrDefault(x => NormalizeCode(x.GroupKey) == key);
        if (group == null || ownerRound == null)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("GROUP_NOT_FOUND", "Không tìm thấy bảng/nhánh trong draft.");

        var removedMatchKeys = group.Matches.Select(x => NormalizeCode(x.MatchKey)!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removedGroupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { key! };
        if (HasExternalReferences(graph, removedMatchKeys, removedGroupKeys, group))
            return BracketOperationResult<BracketTemplateGraphDto>.Fail(
                "GROUP_IN_USE",
                "Không thể xóa bảng/nhánh vì đang được cấu trúc khác dùng làm nguồn.");

        ownerRound.Groups.Remove(group);
        return await PersistMutationAsync(graph, request.RowVersion, ct);
    }

    public async Task<BracketOperationResult<BracketTemplateGraphDto>> AddMatchAsync(
        long versionId,
        string groupKey,
        BracketTemplateMatchMutationRequest request,
        CancellationToken ct)
    {
        var loaded = await LoadDraftForMutationAsync(versionId, request.RowVersion, ct);
        if (!loaded.Success)
            return loaded;

        var graph = loaded.Data!;
        var group = graph.Rounds.SelectMany(x => x.Groups)
            .FirstOrDefault(x => NormalizeCode(x.GroupKey) == NormalizeCode(groupKey));
        if (group == null)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("GROUP_NOT_FOUND", "Không tìm thấy bảng/nhánh trong draft.");

        var key = NormalizeCode(request.MatchKey);
        if (key == null)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("MATCH_KEY_REQUIRED", "Vui lòng nhập mã trận.");
        if (graph.Rounds.SelectMany(x => x.Groups).SelectMany(x => x.Matches)
            .Any(x => NormalizeCode(x.MatchKey) == key))
        {
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("MATCH_KEY_DUPLICATE", "Mã trận đã tồn tại.");
        }

        group.Matches.Add(MapMatchMutation(request, key));
        return await PersistMutationAsync(graph, request.RowVersion, ct);
    }

    public async Task<BracketOperationResult<BracketTemplateGraphDto>> UpdateMatchAsync(
        long versionId,
        string matchKey,
        BracketTemplateMatchMutationRequest request,
        CancellationToken ct)
    {
        var loaded = await LoadDraftForMutationAsync(versionId, request.RowVersion, ct);
        if (!loaded.Success)
            return loaded;

        var graph = loaded.Data!;
        var currentKey = NormalizeCode(matchKey);
        var match = graph.Rounds.SelectMany(x => x.Groups).SelectMany(x => x.Matches)
            .FirstOrDefault(x => NormalizeCode(x.MatchKey) == currentKey);
        if (match == null)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("MATCH_NOT_FOUND", "Không tìm thấy trận trong draft.");

        var nextKey = NormalizeCode(request.MatchKey);
        if (nextKey == null)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("MATCH_KEY_REQUIRED", "Vui lòng nhập mã trận.");
        if (graph.Rounds.SelectMany(x => x.Groups).SelectMany(x => x.Matches)
            .Any(x => !ReferenceEquals(x, match) && NormalizeCode(x.MatchKey) == nextKey))
        {
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("MATCH_KEY_DUPLICATE", "Mã trận đã tồn tại.");
        }

        if (!string.Equals(currentKey, nextKey, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var slot in graph.Rounds.SelectMany(x => x.Groups).SelectMany(x => x.Matches).SelectMany(x => x.Slots)
                         .Where(x => Normalize(x.SourceType) is BracketTemplateSourceTypes.WinnerMatch or BracketTemplateSourceTypes.LoserMatch
                                     && NormalizeCode(x.SourceMatchKey) == currentKey))
            {
                slot.SourceMatchKey = nextKey;
            }
        }

        var replacement = MapMatchMutation(request, nextKey);
        match.MatchKey = replacement.MatchKey;
        match.MatchLabel = replacement.MatchLabel;
        match.SortOrder = replacement.SortOrder;
        match.IsTerminal = replacement.IsTerminal;
        match.TerminalType = replacement.TerminalType;
        match.Slots = replacement.Slots;
        return await PersistMutationAsync(graph, request.RowVersion, ct);
    }

    public async Task<BracketOperationResult<BracketTemplateGraphDto>> DeleteMatchAsync(
        long versionId,
        string matchKey,
        BracketTemplateDeleteRequest request,
        CancellationToken ct)
    {
        var loaded = await LoadDraftForMutationAsync(versionId, request.RowVersion, ct);
        if (!loaded.Success)
            return loaded;

        var graph = loaded.Data!;
        var key = NormalizeCode(matchKey);
        var ownerGroup = graph.Rounds.SelectMany(x => x.Groups)
            .FirstOrDefault(x => x.Matches.Any(m => NormalizeCode(m.MatchKey) == key));
        var match = ownerGroup?.Matches.FirstOrDefault(x => NormalizeCode(x.MatchKey) == key);
        if (match == null || ownerGroup == null)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("MATCH_NOT_FOUND", "Không tìm thấy trận trong draft.");

        var usedBy = graph.Rounds.SelectMany(x => x.Groups).SelectMany(x => x.Matches)
            .Where(x => !ReferenceEquals(x, match))
            .Where(x => x.Slots.Any(slot =>
                Normalize(slot.SourceType) is BracketTemplateSourceTypes.WinnerMatch or BracketTemplateSourceTypes.LoserMatch
                && NormalizeCode(slot.SourceMatchKey) == key))
            .Select(x => x.MatchKey)
            .ToList();
        if (usedBy.Count > 0)
        {
            return BracketOperationResult<BracketTemplateGraphDto>.Fail(
                "MATCH_IN_USE",
                $"Không thể xóa trận vì đang là nguồn của: {string.Join(", ", usedBy)}.");
        }

        ownerGroup.Matches.Remove(match);
        return await PersistMutationAsync(graph, request.RowVersion, ct);
    }

    public async Task<BracketOperationResult<BracketTemplateGraphDto>> UpdateSlotAsync(
        long versionId,
        string matchKey,
        byte slotNumber,
        BracketTemplateSlotMutationRequest request,
        CancellationToken ct)
    {
        if (slotNumber is < 1 or > 2)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("SLOT_NUMBER_INVALID", "Slot chỉ có thể là 1 hoặc 2.");

        var loaded = await LoadDraftForMutationAsync(versionId, request.RowVersion, ct);
        if (!loaded.Success)
            return loaded;

        var graph = loaded.Data!;
        var match = graph.Rounds.SelectMany(x => x.Groups).SelectMany(x => x.Matches)
            .FirstOrDefault(x => NormalizeCode(x.MatchKey) == NormalizeCode(matchKey));
        if (match == null)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("MATCH_NOT_FOUND", "Không tìm thấy trận trong draft.");

        match.Slots.RemoveAll(x => x.SlotNumber == slotNumber);
        match.Slots.Add(new BracketTemplateSlotDto
        {
            SlotNumber = slotNumber,
            SourceType = Normalize(request.SourceType),
            SeedNumber = request.SeedNumber,
            SourceMatchKey = NormalizeCode(request.SourceMatchKey),
            SourceGroupKey = NormalizeCode(request.SourceGroupKey),
            SourceRank = request.SourceRank
        });
        return await PersistMutationAsync(graph, request.RowVersion, ct);
    }

    public async Task<BracketOperationResult<BracketTemplateSourceOptionsDto>> GetSourceOptionsAsync(
        long versionId,
        string matchKey,
        CancellationToken ct)
    {
        var graph = await GetGraphAsync(versionId, ct);
        if (graph == null)
            return BracketOperationResult<BracketTemplateSourceOptionsDto>.Fail("VERSION_NOT_FOUND", "Không tìm thấy template version.");

        var locations = graph.Rounds.SelectMany(round =>
                round.Groups.SelectMany(group =>
                    group.Matches.Select(match => new { Round = round, Group = group, Match = match })))
            .ToList();
        var target = locations.FirstOrDefault(x =>
            NormalizeCode(x.Match.MatchKey) == NormalizeCode(matchKey));
        if (target == null)
            return BracketOperationResult<BracketTemplateSourceOptionsDto>.Fail("MATCH_NOT_FOUND", "Không tìm thấy trận trong template version.");

        var usedSeeds = graph.Rounds.SelectMany(x => x.Groups).SelectMany(x => x.Matches).SelectMany(x => x.Slots)
            .Where(x => Normalize(x.SourceType) == BracketTemplateSourceTypes.Seed && x.SeedNumber.HasValue)
            .Select(x => x.SeedNumber!.Value)
            .Distinct()
            .OrderBy(x => x)
            .ToList();
        var result = new BracketTemplateSourceOptionsDto
        {
            UsedSeeds = usedSeeds,
            UnusedSeeds = Enumerable.Range(1, Math.Max(0, graph.SeedCapacity))
                .Except(usedSeeds)
                .ToList(),
            MatchSources = locations
                .Where(source => !ReferenceEquals(source.Match, target.Match)
                                 && ComesBefore(source.Round, source.Group, source.Match,
                                     target.Round, target.Group, target.Match))
                .OrderBy(source => source.Round.SortOrder)
                .ThenBy(source => source.Group.SortOrder)
                .ThenBy(source => source.Match.SortOrder)
                .Select(source => new BracketTemplateMatchSourceOptionDto
                {
                    MatchKey = source.Match.MatchKey,
                    MatchLabel = source.Match.MatchLabel ?? source.Match.MatchKey,
                    RoundKey = source.Round.RoundKey,
                    RoundLabel = source.Round.RoundLabel,
                    GroupKey = source.Group.GroupKey,
                    GroupName = source.Group.GroupName
                })
                .ToList(),
            GroupSources = graph.Rounds
                .Where(round => round.SortOrder < target.Round.SortOrder)
                .OrderBy(round => round.SortOrder)
                .SelectMany(round => round.Groups.OrderBy(group => group.SortOrder)
                    .Select(group => new BracketTemplateGroupSourceOptionDto
                    {
                        GroupKey = group.GroupKey,
                        GroupName = group.GroupName,
                        RoundKey = round.RoundKey,
                        RoundLabel = round.RoundLabel,
                        TeamCount = group.Matches.SelectMany(x => x.Slots)
                            .Where(x => Normalize(x.SourceType) == BracketTemplateSourceTypes.Seed
                                        && x.SeedNumber.HasValue)
                            .Select(x => x.SeedNumber!.Value)
                            .Distinct()
                            .Count()
                    }))
                .ToList()
        };
        return BracketOperationResult<BracketTemplateSourceOptionsDto>.Ok(result);
    }

    private async Task<BracketOperationResult<BracketTemplateGraphDto>> LoadDraftForMutationAsync(
        long versionId,
        string? rowVersion,
        CancellationToken ct)
    {
        var graph = await GetGraphAsync(versionId, ct);
        if (graph == null)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("VERSION_NOT_FOUND", "Không tìm thấy template version.");
        if (graph.Status != BracketTemplateStatuses.Draft)
            return BracketOperationResult<BracketTemplateGraphDto>.Fail("VERSION_IMMUTABLE", "Chỉ version draft mới được chỉnh sửa.");
        if (string.IsNullOrWhiteSpace(rowVersion)
            || !string.Equals(graph.RowVersion, rowVersion, StringComparison.Ordinal))
        {
            return BracketOperationResult<BracketTemplateGraphDto>.Fail(
                "CONCURRENCY_CONFLICT",
                "Version đã được cập nhật. Vui lòng tải lại trước khi tiếp tục.");
        }

        return BracketOperationResult<BracketTemplateGraphDto>.Ok(graph);
    }

    private Task<BracketOperationResult<BracketTemplateGraphDto>> PersistMutationAsync(
        BracketTemplateGraphDto graph,
        string? rowVersion,
        CancellationToken ct)
    {
        return SaveGraphAsync(
            graph.BracketTemplateVersionId,
            GraphToSaveRequest(graph, rowVersion ?? ""),
            ct);
    }

    private static BracketTemplateMatchDto MapMatchMutation(
        BracketTemplateMatchMutationRequest request,
        string matchKey)
    {
        return new BracketTemplateMatchDto
        {
            MatchKey = matchKey,
            MatchLabel = TrimToNull(request.MatchLabel),
            SortOrder = request.SortOrder,
            IsTerminal = request.IsTerminal,
            TerminalType = request.IsTerminal ? Normalize(request.TerminalType) : null,
            Slots = request.Slots.Select(slot => new BracketTemplateSlotDto
            {
                SlotNumber = slot.SlotNumber,
                SourceType = Normalize(slot.SourceType),
                SeedNumber = slot.SeedNumber,
                SourceMatchKey = NormalizeCode(slot.SourceMatchKey),
                SourceGroupKey = NormalizeCode(slot.SourceGroupKey),
                SourceRank = slot.SourceRank
            }).ToList()
        };
    }

    private static bool HasExternalReferences(
        BracketTemplateGraphDto graph,
        ISet<string> removedMatchKeys,
        ISet<string> removedGroupKeys,
        object removedContainer)
    {
        IEnumerable<BracketTemplateMatchDto> removedMatches = removedContainer switch
        {
            BracketTemplateRoundDto round => round.Groups.SelectMany(x => x.Matches),
            BracketTemplateGroupDto group => group.Matches,
            _ => []
        };
        var removedSet = removedMatches.ToHashSet();

        return graph.Rounds.SelectMany(x => x.Groups).SelectMany(x => x.Matches)
            .Where(x => !removedSet.Contains(x))
            .SelectMany(x => x.Slots)
            .Any(slot =>
                (Normalize(slot.SourceType) is BracketTemplateSourceTypes.WinnerMatch or BracketTemplateSourceTypes.LoserMatch
                 && removedMatchKeys.Contains(NormalizeCode(slot.SourceMatchKey) ?? ""))
                || (Normalize(slot.SourceType) == BracketTemplateSourceTypes.GroupRank
                    && removedGroupKeys.Contains(NormalizeCode(slot.SourceGroupKey) ?? "")));
    }

    private static bool ComesBefore(
        BracketTemplateRoundDto sourceRound,
        BracketTemplateGroupDto sourceGroup,
        BracketTemplateMatchDto sourceMatch,
        BracketTemplateRoundDto targetRound,
        BracketTemplateGroupDto targetGroup,
        BracketTemplateMatchDto targetMatch)
    {
        if (sourceRound.SortOrder != targetRound.SortOrder)
            return sourceRound.SortOrder < targetRound.SortOrder;
        if (sourceGroup.SortOrder != targetGroup.SortOrder)
            return sourceGroup.SortOrder < targetGroup.SortOrder;
        return sourceMatch.SortOrder < targetMatch.SortOrder;
    }

    private async Task DeleteVersionGraphAsync(long versionId, CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
        {
            _db.BracketTemplateMatchSlots.RemoveRange(
                await _db.BracketTemplateMatchSlots
                    .Where(x => x.BracketTemplateVersionId == versionId)
                    .ToListAsync(ct));
            _db.BracketTemplateMatches.RemoveRange(
                await _db.BracketTemplateMatches
                    .Where(x => x.BracketTemplateVersionId == versionId)
                    .ToListAsync(ct));
            _db.BracketTemplateGroups.RemoveRange(
                await _db.BracketTemplateGroups
                    .Where(x => x.BracketTemplateVersionId == versionId)
                    .ToListAsync(ct));
            _db.BracketTemplateRounds.RemoveRange(
                await _db.BracketTemplateRounds
                    .Where(x => x.BracketTemplateVersionId == versionId)
                    .ToListAsync(ct));
            return;
        }

        await _db.BracketTemplateMatchSlots
            .Where(x => x.BracketTemplateVersionId == versionId)
            .ExecuteDeleteAsync(ct);
        await _db.BracketTemplateMatches
            .Where(x => x.BracketTemplateVersionId == versionId)
            .ExecuteDeleteAsync(ct);
        await _db.BracketTemplateGroups
            .Where(x => x.BracketTemplateVersionId == versionId)
            .ExecuteDeleteAsync(ct);
        await _db.BracketTemplateRounds
            .Where(x => x.BracketTemplateVersionId == versionId)
            .ExecuteDeleteAsync(ct);
    }

    private static BracketTemplateGraphDto MapGraph(BracketTemplateVersion version)
    {
        var matchKeys = version.Rounds.SelectMany(x => x.Groups).SelectMany(x => x.Matches)
            .ToDictionary(x => x.BracketTemplateMatchId, x => x.MatchKey);
        var groupKeys = version.Rounds.SelectMany(x => x.Groups)
            .ToDictionary(x => x.BracketTemplateGroupId, x => x.GroupKey);

        var graph = new BracketTemplateGraphDto
        {
            BracketTemplateId = version.BracketTemplateId,
            BracketTemplateVersionId = version.BracketTemplateVersionId,
            VersionNumber = version.VersionNumber,
            Status = version.Status,
            MinimumTeams = version.MinimumTeams,
            SeedCapacity = version.SeedCapacity,
            AllowBye = version.AllowBye,
            DefaultSeedingMethod = version.DefaultSeedingMethod,
            ConfigurationHash = version.ConfigurationHash,
            RowVersion = Convert.ToBase64String(version.RowVersion),
            Rounds = version.Rounds.OrderBy(x => x.SortOrder).ThenBy(x => x.RoundKey).Select(round => new BracketTemplateRoundDto
            {
                BracketTemplateRoundId = round.BracketTemplateRoundId,
                RoundKey = round.RoundKey,
                RoundLabel = round.RoundLabel,
                RoundType = round.RoundType,
                SortOrder = round.SortOrder,
                Groups = round.Groups.OrderBy(x => x.SortOrder).ThenBy(x => x.GroupKey).Select(group => new BracketTemplateGroupDto
                {
                    BracketTemplateGroupId = group.BracketTemplateGroupId,
                    GroupKey = group.GroupKey,
                    GroupName = group.GroupName,
                    GroupType = group.GroupType,
                    SortOrder = group.SortOrder,
                    Matches = group.Matches.OrderBy(x => x.SortOrder).ThenBy(x => x.MatchKey).Select(match => new BracketTemplateMatchDto
                    {
                        BracketTemplateMatchId = match.BracketTemplateMatchId,
                        MatchKey = match.MatchKey,
                        MatchLabel = match.MatchLabel,
                        SortOrder = match.SortOrder,
                        IsTerminal = match.IsTerminal,
                        TerminalType = match.TerminalType,
                        Slots = match.Slots.OrderBy(x => x.SlotNumber).Select(slot => new BracketTemplateSlotDto
                        {
                            BracketTemplateMatchSlotId = slot.BracketTemplateMatchSlotId,
                            SlotNumber = slot.SlotNumber,
                            SourceType = slot.SourceType,
                            SeedNumber = slot.SeedNumber,
                            SourceMatchKey = slot.SourceMatchId.HasValue && matchKeys.TryGetValue(slot.SourceMatchId.Value, out var matchKey) ? matchKey : null,
                            SourceGroupKey = slot.SourceGroupId.HasValue && groupKeys.TryGetValue(slot.SourceGroupId.Value, out var groupKey) ? groupKey : null,
                            SourceRank = slot.SourceRank
                        }).ToList()
                    }).ToList()
                }).ToList()
            }).ToList()
        };
        return graph;
    }

    private static BracketTemplateGraphDto MapInputGraph(BracketTemplateVersion version, SaveBracketTemplateGraphRequest request)
    {
        var graph = new BracketTemplateGraphDto
        {
            BracketTemplateId = version.BracketTemplateId,
            BracketTemplateVersionId = version.BracketTemplateVersionId,
            VersionNumber = version.VersionNumber,
            Status = version.Status,
            MinimumTeams = request.MinimumTeams,
            SeedCapacity = request.SeedCapacity,
            AllowBye = request.AllowBye,
            DefaultSeedingMethod = Normalize(request.DefaultSeedingMethod),
            RowVersion = request.RowVersion ?? "",
            Rounds = request.Rounds.Select(round => new BracketTemplateRoundDto
            {
                RoundKey = NormalizeCode(round.RoundKey) ?? "",
                RoundLabel = TrimToNull(round.RoundLabel) ?? "",
                RoundType = Normalize(round.RoundType),
                SortOrder = round.SortOrder,
                Groups = round.Groups.Select(group => new BracketTemplateGroupDto
                {
                    GroupKey = NormalizeCode(group.GroupKey) ?? "",
                    GroupName = TrimToNull(group.GroupName) ?? "",
                    GroupType = Normalize(group.GroupType),
                    GroupColor = NormalizeGroupColor(group.GroupColor),
                    SortOrder = group.SortOrder,
                    Matches = group.Matches.Select(match => new BracketTemplateMatchDto
                    {
                        MatchKey = NormalizeCode(match.MatchKey) ?? "",
                        MatchLabel = TrimToNull(match.MatchLabel),
                        SortOrder = match.SortOrder,
                        IsTerminal = match.IsTerminal,
                        TerminalType = match.IsTerminal ? Normalize(match.TerminalType) : null,
                        Slots = match.Slots.Select(slot => new BracketTemplateSlotDto
                        {
                            SlotNumber = slot.SlotNumber,
                            SourceType = Normalize(slot.SourceType),
                            SeedNumber = slot.SeedNumber,
                            SourceMatchKey = NormalizeCode(slot.SourceMatchKey),
                            SourceGroupKey = NormalizeCode(slot.SourceGroupKey),
                            SourceRank = slot.SourceRank
                        }).ToList()
                    }).ToList()
                }).ToList()
            }).ToList()
        };
        return graph;
    }

    private static SaveBracketTemplateGraphRequest GraphToSaveRequest(BracketTemplateGraphDto graph, string rowVersion)
    {
        return new SaveBracketTemplateGraphRequest
        {
            MinimumTeams = graph.MinimumTeams,
            SeedCapacity = graph.SeedCapacity,
            AllowBye = graph.AllowBye,
            DefaultSeedingMethod = graph.DefaultSeedingMethod,
            RowVersion = rowVersion,
            Rounds = graph.Rounds.Select(round => new BracketTemplateRoundInput
            {
                RoundKey = round.RoundKey,
                RoundLabel = round.RoundLabel,
                RoundType = round.RoundType,
                SortOrder = round.SortOrder,
                Groups = round.Groups.Select(group => new BracketTemplateGroupInput
                {
                    GroupKey = group.GroupKey,
                    GroupName = group.GroupName,
                    GroupType = group.GroupType,
                    GroupColor = NormalizeGroupColor(group.GroupColor),
                    SortOrder = group.SortOrder,
                    Matches = group.Matches.Select(match => new BracketTemplateMatchInput
                    {
                        MatchKey = match.MatchKey,
                        MatchLabel = match.MatchLabel,
                        SortOrder = match.SortOrder,
                        IsTerminal = match.IsTerminal,
                        TerminalType = match.TerminalType,
                        Slots = match.Slots.Select(slot => new BracketTemplateSlotInput
                        {
                            SlotNumber = slot.SlotNumber,
                            SourceType = slot.SourceType,
                            SeedNumber = slot.SeedNumber,
                            SourceMatchKey = slot.SourceMatchKey,
                            SourceGroupKey = slot.SourceGroupKey,
                            SourceRank = slot.SourceRank
                        }).ToList()
                    }).ToList()
                }).ToList()
            }).ToList()
        };
    }

    private static string SerializeDraftGraph(BracketTemplateGraphDto graph)
    {
        var request = GraphToSaveRequest(graph, "");
        request.RowVersion = null;
        return JsonSerializer.Serialize(request, DraftJsonOptions);
    }

    private static bool TryReadDraft(string? json, out SaveBracketTemplateGraphRequest draft)
    {
        draft = new SaveBracketTemplateGraphRequest();
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            var parsed = JsonSerializer.Deserialize<SaveBracketTemplateGraphRequest>(json, DraftJsonOptions);
            if (parsed == null)
                return false;
            draft = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void ApplyDraftGroupColors(
        BracketTemplateGraphDto graph,
        SaveBracketTemplateGraphRequest draft)
    {
        var colorsByGroupKey = draft.Rounds
            .SelectMany(round => round.Groups)
            .Select(group => new
            {
                GroupKey = NormalizeCode(group.GroupKey),
                GroupColor = NormalizeGroupColor(group.GroupColor)
            })
            .Where(group => group.GroupKey != null && group.GroupColor != null)
            .GroupBy(group => group.GroupKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().GroupColor!, StringComparer.OrdinalIgnoreCase);

        foreach (var group in graph.Rounds.SelectMany(round => round.Groups))
        {
            if (colorsByGroupKey.TryGetValue(group.GroupKey, out var color))
                group.GroupColor = color;
        }
    }

    internal static SaveBracketTemplateGraphRequest GenerateSingleElimination(int teamCount, bool includeThirdPlace, byte[] rowVersion)
    {
        var request = new SaveBracketTemplateGraphRequest
        {
            // Nửa capacity là ngưỡng thấp nhất để mỗi trận vòng đầu còn ít nhất một đội thật.
            MinimumTeams = Math.Max(2, teamCount / 2),
            SeedCapacity = teamCount,
            AllowBye = true,
            DefaultSeedingMethod = BracketSeedingMethods.RegistrationOrder,
            RowVersion = Convert.ToBase64String(rowVersion)
        };
        var rounds = BuildKnockoutRounds(teamCount, includeThirdPlace, null);
        request.Rounds.AddRange(rounds);
        return request;
    }

    internal static SaveBracketTemplateGraphRequest GenerateGroupKnockout(
        int groupCount,
        int teamsPerGroup,
        bool includeThirdPlace,
        byte[] rowVersion)
    {
        var totalTeams = groupCount * teamsPerGroup;
        var request = new SaveBracketTemplateGraphRequest
        {
            MinimumTeams = totalTeams,
            SeedCapacity = totalTeams,
            AllowBye = false,
            DefaultSeedingMethod = BracketSeedingMethods.RegistrationOrder,
            RowVersion = Convert.ToBase64String(rowVersion)
        };

        var groupRound = new BracketTemplateRoundInput
        {
            RoundKey = "GROUP",
            RoundLabel = "Vòng bảng",
            RoundType = BracketRoundTypes.GroupStage,
            SortOrder = 0
        };
        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            var groupName = ToGroupName(groupIndex);
            var group = new BracketTemplateGroupInput
            {
                GroupKey = $"GROUP-{groupName}",
                GroupName = $"Bảng {groupName}",
                GroupType = BracketGroupTypes.RoundRobin,
                SortOrder = groupIndex
            };
            var seedStart = groupIndex * teamsPerGroup + 1;
            var matchOrder = 0;
            for (var first = 0; first < teamsPerGroup; first++)
            for (var second = first + 1; second < teamsPerGroup; second++)
            {
                matchOrder++;
                group.Matches.Add(new BracketTemplateMatchInput
                {
                    MatchKey = $"GROUP-{groupName}-M{matchOrder:00}",
                    MatchLabel = $"Bảng {groupName} - Trận {matchOrder}",
                    SortOrder = matchOrder,
                    Slots =
                    [
                        SeedSlot(1, seedStart + first),
                        SeedSlot(2, seedStart + second)
                    ]
                });
            }
            groupRound.Groups.Add(group);
        }
        request.Rounds.Add(groupRound);

        var qualifierCount = groupCount * 2;
        var firstRoundSources = new List<(string GroupKey, int Rank)>();
        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            var opponent = (groupIndex + 1) % groupCount;
            firstRoundSources.Add(($"GROUP-{ToGroupName(groupIndex)}", 1));
            firstRoundSources.Add(($"GROUP-{ToGroupName(opponent)}", 2));
        }
        request.Rounds.AddRange(BuildKnockoutRounds(qualifierCount, includeThirdPlace, firstRoundSources, startSortOrder: 1));
        return request;
    }

    private static List<BracketTemplateRoundInput> BuildKnockoutRounds(
        int teamCount,
        bool includeThirdPlace,
        IReadOnlyList<(string GroupKey, int Rank)>? firstRoundGroupSources,
        int startSortOrder = 0)
    {
        var result = new List<BracketTemplateRoundInput>();
        var roundTeamCount = teamCount;
        var previousMatchKeys = new List<string>();
        var roundOrder = startSortOrder;

        while (roundTeamCount >= 2)
        {
            var roundKey = GetKnockoutRoundKey(roundTeamCount);
            var isFinal = roundTeamCount == 2;
            var isSemiFinal = roundTeamCount == 4;
            var group = new BracketTemplateGroupInput
            {
                GroupKey = $"{roundKey}-G1",
                GroupName = GetKnockoutRoundLabel(roundTeamCount),
                GroupType = isFinal ? BracketGroupTypes.Final : BracketGroupTypes.KnockoutBranch,
                SortOrder = 0
            };

            var currentMatchKeys = new List<string>();
            var matchCount = roundTeamCount / 2;
            var seedPositions = previousMatchKeys.Count == 0 && firstRoundGroupSources == null
                ? BuildSeedPositions(teamCount)
                : [];
            for (var matchIndex = 0; matchIndex < matchCount; matchIndex++)
            {
                var matchKey = $"{roundKey}-M{matchIndex + 1:00}";
                currentMatchKeys.Add(matchKey);
                var match = new BracketTemplateMatchInput
                {
                    MatchKey = matchKey,
                    MatchLabel = $"{GetKnockoutRoundLabel(roundTeamCount)} - Trận {matchIndex + 1}",
                    SortOrder = matchIndex + 1,
                    IsTerminal = isFinal,
                    TerminalType = isFinal ? "CHAMPION" : null
                };

                if (previousMatchKeys.Count > 0)
                {
                    match.Slots.Add(MatchSourceSlot(1, BracketTemplateSourceTypes.WinnerMatch, previousMatchKeys[matchIndex * 2]));
                    match.Slots.Add(MatchSourceSlot(2, BracketTemplateSourceTypes.WinnerMatch, previousMatchKeys[matchIndex * 2 + 1]));
                }
                else if (firstRoundGroupSources != null)
                {
                    var source1 = firstRoundGroupSources[matchIndex * 2];
                    var source2 = firstRoundGroupSources[matchIndex * 2 + 1];
                    match.Slots.Add(GroupRankSlot(1, source1.GroupKey, source1.Rank));
                    match.Slots.Add(GroupRankSlot(2, source2.GroupKey, source2.Rank));
                }
                else
                {
                    match.Slots.Add(SeedSlot(1, seedPositions[matchIndex * 2]));
                    match.Slots.Add(SeedSlot(2, seedPositions[matchIndex * 2 + 1]));
                }
                group.Matches.Add(match);
            }

            if (isFinal && includeThirdPlace && previousMatchKeys.Count == 2)
            {
                group.Matches.Add(new BracketTemplateMatchInput
                {
                    MatchKey = "THIRD-M01",
                    MatchLabel = "Tranh hạng ba",
                    SortOrder = 2,
                    IsTerminal = true,
                    TerminalType = "THIRD_PLACE",
                    Slots =
                    [
                        MatchSourceSlot(1, BracketTemplateSourceTypes.LoserMatch, previousMatchKeys[0]),
                        MatchSourceSlot(2, BracketTemplateSourceTypes.LoserMatch, previousMatchKeys[1])
                    ]
                });
            }

            result.Add(new BracketTemplateRoundInput
            {
                RoundKey = roundKey,
                RoundLabel = GetKnockoutRoundLabel(roundTeamCount),
                RoundType = isFinal ? BracketRoundTypes.Final : BracketRoundTypes.Knockout,
                SortOrder = roundOrder++,
                Groups = [group]
            });
            previousMatchKeys = currentMatchKeys;
            roundTeamCount /= 2;
        }
        return result;
    }

    internal static List<int> BuildSeedPositions(int capacity)
    {
        var positions = new List<int> { 1, 2 };
        for (var size = 4; size <= capacity; size *= 2)
        {
            var next = new List<int>(size);
            foreach (var seed in positions)
            {
                next.Add(seed);
                next.Add(size + 1 - seed);
            }
            positions = next;
        }
        return positions;
    }

    private static BracketTemplateSlotInput SeedSlot(byte number, int seed) => new()
    {
        SlotNumber = number,
        SourceType = BracketTemplateSourceTypes.Seed,
        SeedNumber = seed
    };

    private static BracketTemplateSlotInput MatchSourceSlot(byte number, string type, string matchKey) => new()
    {
        SlotNumber = number,
        SourceType = type,
        SourceMatchKey = matchKey
    };

    private static BracketTemplateSlotInput GroupRankSlot(byte number, string groupKey, int rank) => new()
    {
        SlotNumber = number,
        SourceType = BracketTemplateSourceTypes.GroupRank,
        SourceGroupKey = groupKey,
        SourceRank = rank
    };

    private static string GetKnockoutRoundKey(int teams) => teams switch
    {
        2 => "FINAL",
        4 => "SF",
        8 => "QF",
        _ => $"R{teams}"
    };

    private static string GetKnockoutRoundLabel(int teams) => teams switch
    {
        2 => "Chung kết",
        4 => "Bán kết",
        8 => "Tứ kết",
        _ => $"Vòng {teams} đội"
    };

    private static string ToGroupName(int index)
    {
        var value = index;
        var name = "";
        do
        {
            name = (char)('A' + value % 26) + name;
            value = value / 26 - 1;
        } while (value >= 0);
        return name;
    }

    private static BracketTemplateDetailDto MapDetail(BracketTemplate entity, int applicationCount)
    {
        var current = entity.CurrentPublishedVersionId.HasValue
            ? entity.Versions.FirstOrDefault(x => x.BracketTemplateVersionId == entity.CurrentPublishedVersionId)
            : null;
        var fallback = current ?? entity.Versions.FirstOrDefault(x => x.Status == BracketTemplateStatuses.Draft);
        return new BracketTemplateDetailDto
        {
            BracketTemplateId = entity.BracketTemplateId,
            TemplateCode = entity.TemplateCode,
            TemplateName = entity.TemplateName,
            Description = entity.Description,
            FormatType = entity.FormatType,
            Status = entity.Status,
            CurrentPublishedVersionId = entity.CurrentPublishedVersionId,
            CurrentVersionNumber = current?.VersionNumber,
            MinimumTeams = fallback?.MinimumTeams,
            SeedCapacity = fallback?.SeedCapacity,
            AllowBye = fallback?.AllowBye,
            DefaultSeedingMethod = fallback?.DefaultSeedingMethod,
            ApplicationCount = applicationCount,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            RowVersion = Convert.ToBase64String(entity.RowVersion),
            Versions = entity.Versions.OrderByDescending(x => x.VersionNumber).Select(MapVersion).ToList()
        };
    }

    private static BracketTemplateVersionSummaryDto MapVersion(BracketTemplateVersion version) => new()
    {
        BracketTemplateVersionId = version.BracketTemplateVersionId,
        VersionNumber = version.VersionNumber,
        Status = version.Status,
        MinimumTeams = version.MinimumTeams,
        SeedCapacity = version.SeedCapacity,
        AllowBye = version.AllowBye,
        DefaultSeedingMethod = version.DefaultSeedingMethod,
        CreatedAt = version.CreatedAt,
        PublishedAt = version.PublishedAt,
        RowVersion = Convert.ToBase64String(version.RowVersion)
    };

    private static string ComputeGraphHash(BracketTemplateGraphDto graph)
    {
        var payload = JsonSerializer.Serialize(new
        {
            graph.MinimumTeams,
            graph.SeedCapacity,
            graph.AllowBye,
            graph.DefaultSeedingMethod,
            graph.Rounds
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static bool MatchesRowVersion(byte[] current, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied))
            return true;
        try
        {
            return current.SequenceEqual(Convert.FromBase64String(supplied));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsFormatType(string value) => value is
        BracketTemplateFormatTypes.SingleElimination or
        BracketTemplateFormatTypes.GroupKnockout or
        BracketTemplateFormatTypes.DoubleElimination or
        BracketTemplateFormatTypes.Custom;

    private static bool IsSeedingMethod(string value) => value is
        BracketSeedingMethods.RegistrationOrder or
        BracketSeedingMethods.Random or
        BracketSeedingMethods.Manual or
        BracketSeedingMethods.Ranking;

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
    private static string Normalize(string? value) => (value ?? "").Trim().ToUpperInvariant();
    private static string? NormalizeCode(string? value) => TrimToNull(value)?.ToUpperInvariant();
    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NormalizeGroupColor(string? value)
    {
        var color = TrimToNull(value);
        return color is { Length: 7 }
               && color[0] == '#'
               && color.AsSpan(1).ToString().All(Uri.IsHexDigit)
            ? color.ToUpperInvariant()
            : null;
    }
}
