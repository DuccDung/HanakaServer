using HanakaServer.Services.Relay;

namespace HanakaServer.Dtos.Relay;

public sealed record RelayMatchViewDto(
    long MatchId,
    long TournamentId,
    string Status,
    long Version,
    int ScoreTeam1,
    int ScoreTeam2,
    long? WinnerRegistrationId,
    int LegNumber,
    int? PairNumber,
    DateTimeOffset ServerNowUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? LegEndsAtUtc,
    int RemainingSeconds,
    bool IsLegExpired,
    RelayRules Rules,
    RelayTeamSummary Team1,
    RelayTeamSummary Team2,
    IReadOnlyList<RelayLegSnapshot> Legs)
{
    public static RelayMatchViewDto Create(RelayMatchReadResult result, RelayTeamSummary team1, RelayTeamSummary team2)
    {
        var state = result.Snapshot;
        return new(state.MatchId, result.TournamentId, state.Status, state.Version, state.ScoreTeam1, state.ScoreTeam2,
            state.WinnerRegistrationId, state.LegNumber, state.PairNumber, result.ServerNowUtc, state.UpdatedAtUtc,
            state.LegEndsAtUtc, state.RemainingSeconds(result.ServerNowUtc),
            state.Status == RelayStatuses.Running && state.LegEndsAtUtc <= result.ServerNowUtc,
            state.Rules, team1, team2, state.Legs);
    }
}
