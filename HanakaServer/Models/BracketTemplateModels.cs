namespace HanakaServer.Models;

public sealed class BracketTemplate
{
    public long BracketTemplateId { get; set; }
    public string TemplateCode { get; set; } = null!;
    public string TemplateName { get; set; } = null!;
    public string? Description { get; set; }
    public string FormatType { get; set; } = BracketTemplateFormatTypes.Custom;
    public string Status { get; set; } = BracketTemplateStatuses.Draft;
    public long? CurrentPublishedVersionId { get; set; }
    public long? CreatedByUserId { get; set; }
    public long? UpdatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [0];

    public User? CreatedByUser { get; set; }
    public User? UpdatedByUser { get; set; }
    public BracketTemplateVersion? CurrentPublishedVersion { get; set; }
    public ICollection<BracketTemplateVersion> Versions { get; set; } = new List<BracketTemplateVersion>();
    public ICollection<TournamentBracketApplication> Applications { get; set; } = new List<TournamentBracketApplication>();
}

public sealed class BracketTemplateVersion
{
    public long BracketTemplateVersionId { get; set; }
    public long BracketTemplateId { get; set; }
    public int VersionNumber { get; set; }
    public string Status { get; set; } = BracketTemplateStatuses.Draft;
    public int MinimumTeams { get; set; } = 2;
    public int SeedCapacity { get; set; }
    public bool AllowBye { get; set; }
    public string DefaultSeedingMethod { get; set; } = BracketSeedingMethods.RegistrationOrder;
    public string? ConfigurationHash { get; set; }
    public string? DraftGraphJson { get; set; }
    public long? CreatedByUserId { get; set; }
    public long? PublishedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public byte[] RowVersion { get; set; } = [0];

    public BracketTemplate BracketTemplate { get; set; } = null!;
    public User? CreatedByUser { get; set; }
    public User? PublishedByUser { get; set; }
    public ICollection<BracketTemplateRound> Rounds { get; set; } = new List<BracketTemplateRound>();
    public ICollection<TournamentBracketApplication> Applications { get; set; } = new List<TournamentBracketApplication>();
}

public sealed class BracketTemplateRound
{
    public long BracketTemplateRoundId { get; set; }
    public long BracketTemplateVersionId { get; set; }
    public string RoundKey { get; set; } = null!;
    public string RoundLabel { get; set; } = null!;
    public string RoundType { get; set; } = BracketRoundTypes.Knockout;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public BracketTemplateVersion BracketTemplateVersion { get; set; } = null!;
    public ICollection<BracketTemplateGroup> Groups { get; set; } = new List<BracketTemplateGroup>();
}

public sealed class BracketTemplateGroup
{
    public long BracketTemplateGroupId { get; set; }
    public long BracketTemplateVersionId { get; set; }
    public long BracketTemplateRoundId { get; set; }
    public string GroupKey { get; set; } = null!;
    public string GroupName { get; set; } = null!;
    public string GroupType { get; set; } = BracketGroupTypes.Generic;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public BracketTemplateRound BracketTemplateRound { get; set; } = null!;
    public ICollection<BracketTemplateMatch> Matches { get; set; } = new List<BracketTemplateMatch>();
    public ICollection<BracketTemplateMatchSlot> SourceSlots { get; set; } = new List<BracketTemplateMatchSlot>();
}

public sealed class BracketTemplateMatch
{
    public long BracketTemplateMatchId { get; set; }
    public long BracketTemplateVersionId { get; set; }
    public long BracketTemplateGroupId { get; set; }
    public string MatchKey { get; set; } = null!;
    public string? MatchLabel { get; set; }
    public int SortOrder { get; set; }
    public bool IsTerminal { get; set; }
    public string? TerminalType { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public BracketTemplateGroup BracketTemplateGroup { get; set; } = null!;
    public ICollection<BracketTemplateMatchSlot> Slots { get; set; } = new List<BracketTemplateMatchSlot>();
    public ICollection<BracketTemplateMatchSlot> SourceSlots { get; set; } = new List<BracketTemplateMatchSlot>();
}

public sealed class BracketTemplateMatchSlot
{
    public long BracketTemplateMatchSlotId { get; set; }
    public long BracketTemplateVersionId { get; set; }
    public long BracketTemplateMatchId { get; set; }
    public byte SlotNumber { get; set; }
    public string SourceType { get; set; } = BracketTemplateSourceTypes.Seed;
    public int? SeedNumber { get; set; }
    public long? SourceMatchId { get; set; }
    public long? SourceGroupId { get; set; }
    public int? SourceRank { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public BracketTemplateMatch BracketTemplateMatch { get; set; } = null!;
    public BracketTemplateMatch? SourceMatch { get; set; }
    public BracketTemplateGroup? SourceGroup { get; set; }
}

public sealed class TournamentBracketApplication
{
    public long TournamentBracketApplicationId { get; set; }
    public long TournamentId { get; set; }
    public long BracketTemplateId { get; set; }
    public long BracketTemplateVersionId { get; set; }
    public string Status { get; set; } = BracketApplicationStatuses.Applying;
    public bool IsActive { get; set; } = true;
    public string SeedingMethod { get; set; } = BracketSeedingMethods.RegistrationOrder;
    public long? RandomSeed { get; set; }
    public int EligibleRegistrationCount { get; set; }
    public int SeedCapacity { get; set; }
    public int ByeCount { get; set; }
    public string PreviewHash { get; set; } = null!;
    public long? AppliedByUserId { get; set; }
    public long? RevertedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AppliedAt { get; set; }
    public DateTime? RevertedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? RevertReason { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public byte[] RowVersion { get; set; } = [0];

    public Tournament Tournament { get; set; } = null!;
    public BracketTemplate BracketTemplate { get; set; } = null!;
    public BracketTemplateVersion BracketTemplateVersion { get; set; } = null!;
    public User? AppliedByUser { get; set; }
    public User? RevertedByUser { get; set; }
    public ICollection<TournamentBracketSeedAssignment> SeedAssignments { get; set; } = new List<TournamentBracketSeedAssignment>();
    public ICollection<TournamentRoundMap> GeneratedRounds { get; set; } = new List<TournamentRoundMap>();
    public ICollection<TournamentRoundGroup> GeneratedGroups { get; set; } = new List<TournamentRoundGroup>();
    public ICollection<TournamentGroupMatch> GeneratedMatches { get; set; } = new List<TournamentGroupMatch>();
}

public sealed class TournamentBracketSeedAssignment
{
    public long TournamentBracketSeedAssignmentId { get; set; }
    public long TournamentBracketApplicationId { get; set; }
    public int SeedNumber { get; set; }
    public long? RegistrationId { get; set; }
    public bool IsBye { get; set; }
    public int? InputOrder { get; set; }
    public string AssignmentMethod { get; set; } = BracketSeedingMethods.RegistrationOrder;
    public bool IsManuallyAdjusted { get; set; }
    public string? RegistrationCodeSnapshot { get; set; }
    public string? Player1NameSnapshot { get; set; }
    public string? Player2NameSnapshot { get; set; }
    public DateTime CreatedAt { get; set; }

    public TournamentBracketApplication TournamentBracketApplication { get; set; } = null!;
    public TournamentRegistration? Registration { get; set; }
}

public static class BracketTemplateStatuses
{
    public const string Draft = "DRAFT";
    public const string Published = "PUBLISHED";
    public const string Archived = "ARCHIVED";
}

public static class BracketTemplateFormatTypes
{
    public const string SingleElimination = "SINGLE_ELIMINATION";
    public const string GroupKnockout = "GROUP_KNOCKOUT";
    public const string DoubleElimination = "DOUBLE_ELIMINATION";
    public const string Custom = "CUSTOM";
}

public static class BracketRoundTypes
{
    public const string GroupStage = "GROUP_STAGE";
    public const string Knockout = "KNOCKOUT";
    public const string Final = "FINAL";
    public const string Placement = "PLACEMENT";
    public const string LoserBracket = "LOSER_BRACKET";
}

public static class BracketGroupTypes
{
    public const string Generic = "GENERIC";
    public const string RoundRobin = "ROUND_ROBIN";
    public const string KnockoutBranch = "KNOCKOUT_BRANCH";
    public const string Final = "FINAL";
    public const string Placement = "PLACEMENT";
}

public static class BracketTemplateSourceTypes
{
    public const string Seed = "SEED";
    public const string WinnerMatch = "WINNER_MATCH";
    public const string LoserMatch = "LOSER_MATCH";
    public const string GroupRank = "GROUP_RANK";
    public const string Bye = "BYE";
}

public static class BracketSeedingMethods
{
    public const string RegistrationOrder = "REGISTRATION_ORDER";
    public const string Random = "RANDOM";
    public const string Manual = "MANUAL";
    public const string Ranking = "RANKING";
    public const string Bye = "BYE";
}

public static class BracketApplicationStatuses
{
    public const string Applying = "APPLYING";
    public const string Applied = "APPLIED";
    public const string Failed = "FAILED";
    public const string Reverted = "REVERTED";
}

public static class MatchCompletionReasons
{
    public const string Normal = "NORMAL";
    public const string Bye = "BYE";
    public const string Walkover = "WALKOVER";
    public const string AdminOverride = "ADMIN_OVERRIDE";
}
