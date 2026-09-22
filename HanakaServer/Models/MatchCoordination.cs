namespace HanakaServer.Models;

public static class MatchStatuses
{
    public const string NotStarted = "NOT_STARTED";
    public const string Preparing = "PREPARING";
    public const string InProgress = "IN_PROGRESS";
    public const string Completed = "COMPLETED";
    public static bool CanCoordinate(string status) => status is NotStarted or Preparing;
}

public partial class TournamentGroupMatch
{
    public string MatchStatus { get; set; } = MatchStatuses.NotStarted;
    public long StateVersion { get; set; }
}

// Assignment is the live authority, so revocation applies to existing JWT sessions.
public sealed class TournamentCoordinator
{
    public long TournamentId { get; set; }
    public long UserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class MatchCoordinationHistory
{
    public long Id { get; set; }
    public long MatchId { get; set; }
    public long ActorUserId { get; set; }
    public string? PreviousCourt { get; set; }
    public string? Court { get; set; }
    public string PreviousStatus { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
