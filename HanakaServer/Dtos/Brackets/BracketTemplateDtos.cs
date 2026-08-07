namespace HanakaServer.Dtos.Brackets;

public sealed class CreateBracketTemplateRequest
{
    public string? TemplateCode { get; set; }
    public string? TemplateName { get; set; }
    public string? Description { get; set; }
    public string? FormatType { get; set; } = "CUSTOM";
    public int MinimumTeams { get; set; } = 2;
    public int SeedCapacity { get; set; } = 2;
    public bool AllowBye { get; set; }
    public string? DefaultSeedingMethod { get; set; } = "REGISTRATION_ORDER";
}

public sealed class UpdateBracketTemplateRequest
{
    public string? TemplateName { get; set; }
    public string? Description { get; set; }
    public string? FormatType { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class UpdateBracketTemplateSettingsRequest
{
    public string? TemplateName { get; set; }
    public int MinimumTeams { get; set; }
    public int SeedCapacity { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class DeleteBracketTemplateRequest
{
    public string? RowVersion { get; set; }
    public string? Confirmation { get; set; }
}

public sealed class CloneBracketTemplateRequest
{
    public long SourceVersionId { get; set; }
    public string? TemplateCode { get; set; }
    public string? TemplateName { get; set; }
}

public sealed class SaveBracketTemplateGraphRequest
{
    public int MinimumTeams { get; set; } = 2;
    public int SeedCapacity { get; set; }
    public bool AllowBye { get; set; }
    public string? DefaultSeedingMethod { get; set; }
    public string? RowVersion { get; set; }
    public List<BracketTemplateRoundInput> Rounds { get; set; } = [];
}

public sealed class BracketTemplateRoundInput
{
    public string? RoundKey { get; set; }
    public string? RoundLabel { get; set; }
    public string? RoundType { get; set; }
    public int SortOrder { get; set; }
    public List<BracketTemplateGroupInput> Groups { get; set; } = [];
}

public sealed class BracketTemplateGroupInput
{
    public string? GroupKey { get; set; }
    public string? GroupName { get; set; }
    public string? GroupType { get; set; }
    public string? GroupColor { get; set; }
    public int SortOrder { get; set; }
    public List<BracketTemplateMatchInput> Matches { get; set; } = [];
}

public sealed class BracketTemplateMatchInput
{
    public string? MatchKey { get; set; }
    public string? MatchLabel { get; set; }
    public int SortOrder { get; set; }
    public bool IsTerminal { get; set; }
    public string? TerminalType { get; set; }
    public List<BracketTemplateSlotInput> Slots { get; set; } = [];
}

public sealed class BracketTemplateSlotInput
{
    public byte SlotNumber { get; set; }
    public string? SourceType { get; set; }
    public int? SeedNumber { get; set; }
    public string? SourceMatchKey { get; set; }
    public string? SourceGroupKey { get; set; }
    public int? SourceRank { get; set; }
}

public sealed class BracketTemplateRoundMutationRequest
{
    public string? RowVersion { get; set; }
    public string? RoundKey { get; set; }
    public string? RoundLabel { get; set; }
    public string? RoundType { get; set; }
    public int SortOrder { get; set; }
}

public sealed class BracketTemplateGroupMutationRequest
{
    public string? RowVersion { get; set; }
    public string? GroupKey { get; set; }
    public string? GroupName { get; set; }
    public string? GroupType { get; set; }
    public string? GroupColor { get; set; }
    public int SortOrder { get; set; }
}

public sealed class BracketTemplateMatchMutationRequest
{
    public string? RowVersion { get; set; }
    public string? MatchKey { get; set; }
    public string? MatchLabel { get; set; }
    public int SortOrder { get; set; }
    public bool IsTerminal { get; set; }
    public string? TerminalType { get; set; }
    public List<BracketTemplateSlotInput> Slots { get; set; } = [];
}

public sealed class BracketTemplateSlotMutationRequest
{
    public string? RowVersion { get; set; }
    public string? SourceType { get; set; }
    public int? SeedNumber { get; set; }
    public string? SourceMatchKey { get; set; }
    public string? SourceGroupKey { get; set; }
    public int? SourceRank { get; set; }
}

public sealed class BracketTemplateDeleteRequest
{
    public string? RowVersion { get; set; }
}

public sealed class BracketTemplateSourceOptionsDto
{
    public List<int> UsedSeeds { get; set; } = [];
    public List<int> UnusedSeeds { get; set; } = [];
    public List<BracketTemplateMatchSourceOptionDto> MatchSources { get; set; } = [];
    public List<BracketTemplateGroupSourceOptionDto> GroupSources { get; set; } = [];
}

public sealed class BracketTemplateMatchSourceOptionDto
{
    public string MatchKey { get; set; } = "";
    public string MatchLabel { get; set; } = "";
    public string RoundKey { get; set; } = "";
    public string RoundLabel { get; set; } = "";
    public string GroupKey { get; set; } = "";
    public string GroupName { get; set; } = "";
}

public sealed class BracketTemplateGroupSourceOptionDto
{
    public string GroupKey { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string RoundKey { get; set; } = "";
    public string RoundLabel { get; set; } = "";
    public int TeamCount { get; set; }
}

public sealed class GenerateBracketTemplateRequest
{
    public string? GeneratorType { get; set; }
    public int TeamCount { get; set; }
    public bool IncludeThirdPlace { get; set; }
    public int GroupCount { get; set; }
    public int TeamsPerGroup { get; set; }
    public int QualifiersPerGroup { get; set; } = 2;
}

public class BracketTemplateListItemDto
{
    public long BracketTemplateId { get; set; }
    public string TemplateCode { get; set; } = "";
    public string TemplateName { get; set; } = "";
    public string? Description { get; set; }
    public string FormatType { get; set; } = "";
    public string Status { get; set; } = "";
    public int? CurrentVersionNumber { get; set; }
    public long? CurrentPublishedVersionId { get; set; }
    public int? MinimumTeams { get; set; }
    public int? SeedCapacity { get; set; }
    public bool? AllowBye { get; set; }
    public string? DefaultSeedingMethod { get; set; }
    public int RoundCount { get; set; }
    public int GroupCount { get; set; }
    public int MatchCount { get; set; }
    public int? EligibleTeamCount { get; set; }
    public bool IsApplicable { get; set; } = true;
    public string? InapplicableReason { get; set; }
    public int ApplicationCount { get; set; }
    public int CurrentVersionApplicationCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByName { get; set; }
    public string RowVersion { get; set; } = "";
}

public sealed class PagedBracketTemplateListDto
{
    public IReadOnlyList<BracketTemplateListItemDto> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
}

public sealed class BracketTemplateDetailDto : BracketTemplateListItemDto
{
    public List<BracketTemplateVersionSummaryDto> Versions { get; set; } = [];
}

public sealed class BracketTemplateVersionSummaryDto
{
    public long BracketTemplateVersionId { get; set; }
    public int VersionNumber { get; set; }
    public string Status { get; set; } = "";
    public int MinimumTeams { get; set; }
    public int SeedCapacity { get; set; }
    public bool AllowBye { get; set; }
    public string DefaultSeedingMethod { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int ApplicationCount { get; set; }
    public string RowVersion { get; set; } = "";
}

public sealed class BracketTemplateGraphDto
{
    public long BracketTemplateId { get; set; }
    public long BracketTemplateVersionId { get; set; }
    public int VersionNumber { get; set; }
    public string Status { get; set; } = "";
    public int MinimumTeams { get; set; }
    public int SeedCapacity { get; set; }
    public bool AllowBye { get; set; }
    public string DefaultSeedingMethod { get; set; } = "";
    public string? ConfigurationHash { get; set; }
    public string RowVersion { get; set; } = "";
    public BracketValidationResultDto? Validation { get; set; }
    public List<BracketTemplateRoundDto> Rounds { get; set; } = [];
}

public sealed class BracketTemplateRoundDto
{
    public long BracketTemplateRoundId { get; set; }
    public string RoundKey { get; set; } = "";
    public string RoundLabel { get; set; } = "";
    public string RoundType { get; set; } = "";
    public int SortOrder { get; set; }
    public List<BracketTemplateGroupDto> Groups { get; set; } = [];
}

public sealed class BracketTemplateGroupDto
{
    public long BracketTemplateGroupId { get; set; }
    public string GroupKey { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string GroupType { get; set; } = "";
    public string? GroupColor { get; set; }
    public int SortOrder { get; set; }
    public List<BracketTemplateMatchDto> Matches { get; set; } = [];
}

public sealed class BracketTemplateMatchDto
{
    public long BracketTemplateMatchId { get; set; }
    public string MatchKey { get; set; } = "";
    public string? MatchLabel { get; set; }
    public int SortOrder { get; set; }
    public bool IsTerminal { get; set; }
    public string? TerminalType { get; set; }
    public List<BracketTemplateSlotDto> Slots { get; set; } = [];
}

public sealed class BracketTemplateSlotDto
{
    public long BracketTemplateMatchSlotId { get; set; }
    public byte SlotNumber { get; set; }
    public string SourceType { get; set; } = "";
    public int? SeedNumber { get; set; }
    public string? SourceMatchKey { get; set; }
    public string? SourceGroupKey { get; set; }
    public int? SourceRank { get; set; }
}

public sealed class BracketValidationResultDto
{
    public bool IsValid => Issues.All(x => x.Severity != "ERROR");
    public int ErrorCount => Issues.Count(x => x.Severity == "ERROR");
    public int WarningCount => Issues.Count(x => x.Severity == "WARNING");
    public int InfoCount => Issues.Count(x => x.Severity == "INFO");
    public List<BracketValidationIssueDto> Issues { get; set; } = [];
}

public sealed class BracketValidationIssueDto
{
    public string Severity { get; set; } = "ERROR";
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string? RoundKey { get; set; }
    public string? GroupKey { get; set; }
    public string? MatchKey { get; set; }
    public byte? SlotNumber { get; set; }
}

public class TournamentBracketPreviewRequest
{
    public long BracketTemplateVersionId { get; set; }
    public string? SeedingMethod { get; set; }
    public long? RandomSeed { get; set; }
    public List<ManualSeedAssignmentRequest> SeedAssignments { get; set; } = [];
}

public sealed class ManualSeedAssignmentRequest
{
    public int SeedNumber { get; set; }
    public long? RegistrationId { get; set; }
}

public sealed class ApplyTournamentBracketRequest : TournamentBracketPreviewRequest
{
    public string? PreviewHash { get; set; }
    public DateTime? StartAt { get; set; }
    public long? RefereeUserId { get; set; }
    public string? AddressText { get; set; }
}

public sealed class ResetTournamentBracketRequest
{
    public string? Reason { get; set; }
}

public sealed class SetTournamentRegistrationLockRequest
{
    public bool Locked { get; set; }
}

public sealed class TournamentBracketPreviewDto
{
    public long TournamentId { get; set; }
    public long BracketTemplateId { get; set; }
    public long BracketTemplateVersionId { get; set; }
    public string TemplateName { get; set; } = "";
    public string TemplateCode { get; set; } = "";
    public int VersionNumber { get; set; }
    public string SeedingMethod { get; set; } = "";
    public long? RandomSeed { get; set; }
    public int EligibleRegistrationCount { get; set; }
    public int ExcludedRegistrationCount { get; set; }
    public int SeedCapacity { get; set; }
    public int ByeCount { get; set; }
    public int RoundCount { get; set; }
    public int GroupCount { get; set; }
    public int MatchCount { get; set; }
    public string PreviewHash { get; set; } = "";
    public bool RegistrationLocked { get; set; }
    public BracketValidationResultDto Validation { get; set; } = new();
    public List<TournamentBracketSeedDto> Seeds { get; set; } = [];
    public List<TournamentBracketPreviewRoundDto> Rounds { get; set; } = [];
}

public sealed class TournamentBracketSeedDto
{
    public int SeedNumber { get; set; }
    public long? RegistrationId { get; set; }
    public bool IsBye { get; set; }
    public int? InputOrder { get; set; }
    public bool IsManuallyAdjusted { get; set; }
    public string? RegCode { get; set; }
    public string TeamName { get; set; } = "";
    public string? Player1Name { get; set; }
    public string? Player2Name { get; set; }
    public long? Player1UserId { get; set; }
    public long? Player2UserId { get; set; }
    public decimal Player1Level { get; set; }
    public decimal Player2Level { get; set; }
    public decimal Points { get; set; }
    public bool Paid { get; set; }
    public DateTime? RegisteredAt { get; set; }
}

public sealed class TournamentBracketPreviewRoundDto
{
    public string RoundKey { get; set; } = "";
    public string RoundLabel { get; set; } = "";
    public string RoundType { get; set; } = "";
    public int SortOrder { get; set; }
    public List<TournamentBracketPreviewGroupDto> Groups { get; set; } = [];
}

public sealed class TournamentBracketPreviewGroupDto
{
    public string GroupKey { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string GroupType { get; set; } = "";
    public int SortOrder { get; set; }
    public List<TournamentBracketPreviewMatchDto> Matches { get; set; } = [];
}

public sealed class TournamentBracketPreviewMatchDto
{
    public string MatchKey { get; set; } = "";
    public string? MatchLabel { get; set; }
    public int SortOrder { get; set; }
    public bool IsTerminal { get; set; }
    public string? TerminalType { get; set; }
    public List<TournamentBracketPreviewSlotDto> Slots { get; set; } = [];
}

public sealed class TournamentBracketPreviewSlotDto
{
    public byte SlotNumber { get; set; }
    public string SourceType { get; set; } = "";
    public int? SeedNumber { get; set; }
    public long? RegistrationId { get; set; }
    public bool IsBye { get; set; }
    public string DisplayText { get; set; } = "";
    public string? SourceMatchKey { get; set; }
    public string? SourceGroupKey { get; set; }
    public int? SourceRank { get; set; }
}

public sealed class TournamentBracketApplicationDto
{
    public long TournamentBracketApplicationId { get; set; }
    public long TournamentId { get; set; }
    public long BracketTemplateId { get; set; }
    public long BracketTemplateVersionId { get; set; }
    public string TemplateName { get; set; } = "";
    public string TemplateCode { get; set; } = "";
    public int VersionNumber { get; set; }
    public string Status { get; set; } = "";
    public bool IsActive { get; set; }
    public string SeedingMethod { get; set; } = "";
    public long? RandomSeed { get; set; }
    public int EligibleRegistrationCount { get; set; }
    public int SeedCapacity { get; set; }
    public int ByeCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AppliedAt { get; set; }
    public string? AppliedByName { get; set; }
    public DateTime? RevertedAt { get; set; }
    public string? RevertedByName { get; set; }
    public string? RevertReason { get; set; }
    public int GeneratedRoundCount { get; set; }
    public int GeneratedGroupCount { get; set; }
    public int GeneratedMatchCount { get; set; }
    public List<TournamentBracketSeedDto> Seeds { get; set; } = [];
}

public sealed class BracketOperationResult<T>
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public T? Data { get; init; }

    public static BracketOperationResult<T> Ok(T data, string? message = null) =>
        new() { Success = true, Data = data, Message = message };

    public static BracketOperationResult<T> Fail(string code, string message) =>
        new() { Success = false, ErrorCode = code, Message = message };
}
