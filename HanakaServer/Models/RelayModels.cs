namespace HanakaServer.Models;

// Additive tables: existing tournaments/registrations/matches keep their identities.
// No navigation is added to legacy entities, so their queries do not need the new tables.
public sealed class RelayTournamentSettings
{
    public long TournamentId { get; set; }
    public int TeamSize { get; set; } = 6;
    public int TargetScore { get; set; } = 40;
    public int LegDurationSeconds { get; set; } = 600;
    // Legacy column retained for databases that applied the first relay foundation script.
    // Relay time is informational only and no runtime rule reads this value.
    public string? DeadlinePolicy { get; set; }
    public bool IsEnabled { get; set; }
    public long Version { get; set; }
}

public sealed class RelayTeam
{
    public long RegistrationId { get; set; }
    public long TournamentId { get; set; }
    public string TeamName { get; set; } = "";
    public long? CaptainUserId { get; set; }
    // Legacy column retained for databases that used the original roster-lock workflow.
    // Readiness is now derived from the configured team size and member positions.
    public DateTimeOffset? LineupLockedAtUtc { get; set; }
    public long Version { get; set; }
    public ICollection<RelayTeamMember> Members { get; set; } = new List<RelayTeamMember>();
    // Registration information only; never included in the playing lineup or match snapshots.
    public ICollection<RelayTeamReserveMember> ReserveMembers { get; set; } = new List<RelayTeamReserveMember>();
}

public sealed class RelayTeamReserveMember
{
    public long RegistrationId { get; set; }
    public int Position { get; set; }
    public long? UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public RelayTeam Team { get; set; } = null!;
}

public sealed class RelayTeamMember
{
    public long RegistrationId { get; set; }
    // Positions 1/2 = pair 1, 3/4 = pair 2, etc. The order is fixed for the tournament.
    public int Position { get; set; }
    public long? UserId { get; set; }
    public string DisplayName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public RelayTeam Team { get; set; } = null!;
}

public sealed class RelayMatchState
{
    public long MatchId { get; set; }
    public long TournamentId { get; set; }
    public long Team1RegistrationId { get; set; }
    public long Team2RegistrationId { get; set; }
    public int PairCount { get; set; }
    public int TargetScore { get; set; } = 40;
    public int LegDurationSeconds { get; set; } = 600;
    // Legacy nullable column; kept only for schema compatibility.
    public string? DeadlinePolicy { get; set; }
    public string Status { get; set; } = "READY";
    public int CurrentLegNumber { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public long Version { get; set; }
    // Current score and winner remain on TournamentGroupMatch, not duplicated here.
    public ICollection<RelayLeg> Legs { get; set; } = new List<RelayLeg>();
}

public sealed class RelayLeg
{
    public long MatchId { get; set; }
    public int LegNumber { get; set; }
    public int PairNumber { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset EndsAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public int StartScoreTeam1 { get; set; }
    public int StartScoreTeam2 { get; set; }
    public int? EndScoreTeam1 { get; set; }
    public int? EndScoreTeam2 { get; set; }
    public RelayMatchState MatchState { get; set; } = null!;
}

public sealed class RelayMatchCommand
{
    public long MatchId { get; set; }
    public Guid RequestId { get; set; }
    public long ActorUserId { get; set; }
    public string RequestHash { get; set; } = "";
    public string Operation { get; set; } = "";
    public long ResultVersion { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string ResultJson { get; set; } = "";
}

// Per application history, never part of the reusable template graph.
public sealed class RelayBracketSeedSnapshot
{
    public long TournamentBracketApplicationId { get; set; }
    public int SeedNumber { get; set; }
    public string TeamName { get; set; } = "";
    public string LineupJson { get; set; } = "";
}

// Immutable roster captured when a match receives its first score. Later admin edits
// apply to matches that have not started without rewriting played-match history.
public sealed class RelayMatchLineupSnapshot
{
    public long MatchId { get; set; }
    public int Side { get; set; }
    public long RegistrationId { get; set; }
    public string TeamName { get; set; } = "";
    public long LineupVersion { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
    public string LineupJson { get; set; } = "";
}
