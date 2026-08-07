namespace HanakaServer.Models;

public partial class Tournament
{
    public DateTime? RegistrationLockedAt { get; set; }
    public long? RegistrationLockedByUserId { get; set; }

    public User? RegistrationLockedByUser { get; set; }
    public ICollection<TournamentBracketApplication> BracketApplications { get; set; } = new List<TournamentBracketApplication>();
}

public partial class TournamentRoundMap
{
    public long? BracketApplicationId { get; set; }
    public string? TemplateRoundKey { get; set; }
    public string? TemplateRoundType { get; set; }

    public TournamentBracketApplication? BracketApplication { get; set; }
}

public partial class TournamentRoundGroup
{
    public long? BracketApplicationId { get; set; }
    public string? TemplateGroupKey { get; set; }
    public string? TemplateGroupType { get; set; }

    public TournamentBracketApplication? BracketApplication { get; set; }
}

public partial class TournamentGroupMatch
{
    public long? BracketApplicationId { get; set; }
    public string? TemplateMatchKey { get; set; }
    public string? TemplateMatchLabel { get; set; }
    public bool? TemplateIsTerminal { get; set; }
    public string? TemplateTerminalType { get; set; }
    public string? CompletionReason { get; set; }

    public TournamentBracketApplication? BracketApplication { get; set; }
}
