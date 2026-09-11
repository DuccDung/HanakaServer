namespace HanakaServer.Services.Relay;

// Database compatibility constants for historical rows. Runtime rules no longer use them.
public static class RelayPolicies
{
    public const string StopAtDeadline = "STOP_AT_DEADLINE";
    public const string FinishRally = "FINISH_RALLY";
}

public static class RelayStatuses
{
    public const string Ready = "READY";
    public const string Running = "RUNNING";
    public const string AwaitingChange = "AWAITING_CHANGE";
    public const string Completed = "COMPLETED";
}

public sealed class RelayRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record RelayRules(int TeamSize, int TargetScore, int LegDurationSeconds)
{
    public int PairCount => TeamSize / 2;

    public void Validate()
    {
        if (TeamSize is not (4 or 6 or 8))
            throw new RelayRuleException("TEAM_SIZE_INVALID", "Đội tiếp sức phải có 4, 6 hoặc 8 vận động viên.");
        if (TargetScore <= 0 || LegDurationSeconds <= 0)
            throw new RelayRuleException("RULES_INVALID", "Điểm đích và thời lượng hiển thị phải lớn hơn 0.");
    }
}

public sealed record RelayLegSnapshot(int LegNumber, int PairNumber,
    DateTimeOffset StartedAtUtc, DateTimeOffset EndsAtUtc, DateTimeOffset? FinishedAtUtc,
    int StartScoreTeam1, int StartScoreTeam2, int? EndScoreTeam1, int? EndScoreTeam2);

public sealed record RelayMatchSnapshot(long MatchId, long Team1RegistrationId, long Team2RegistrationId,
    RelayRules Rules, string Status, int ScoreTeam1, int ScoreTeam2, long? WinnerRegistrationId,
    long Version, DateTimeOffset? UpdatedAtUtc, IReadOnlyList<RelayLegSnapshot> Legs)
{
    public int LegNumber => Legs.Count;
    public int? PairNumber => Legs.LastOrDefault()?.PairNumber;
    public DateTimeOffset? LegEndsAtUtc => Legs.LastOrDefault()?.EndsAtUtc;

    public int RemainingSeconds(DateTimeOffset serverNowUtc) => Status == RelayStatuses.Running && LegEndsAtUtc.HasValue
        ? (int)Math.Max(0, Math.Ceiling((LegEndsAtUtc.Value - serverNowUtc).TotalSeconds)) : 0;
}

// Pure transitions. Persistence supplies server time, authorization, transaction,
// expected-version checks and durable request-id deduplication.
public static class RelayMatchEngine
{
    public static RelayMatchSnapshot Create(long matchId, long team1, long team2, RelayRules rules)
    {
        rules.Validate();
        if (matchId <= 0 || team1 <= 0 || team2 <= 0 || team1 == team2)
            throw new RelayRuleException("MATCH_TEAMS_INVALID", "Trận phải có hai đội chính khác nhau.");
        return new(matchId, team1, team2, rules, RelayStatuses.Ready, 0, 0, null, 0, null, []);
    }

    public static RelayMatchSnapshot Start(RelayMatchSnapshot state, DateTimeOffset now)
    {
        Require(state, RelayStatuses.Ready, now);
        if (state.Legs.Count != 0 || state.ScoreTeam1 != 0 || state.ScoreTeam2 != 0)
            throw new RelayRuleException("MATCH_ALREADY_STARTED", "Trận đã có dữ liệu thi đấu.");
        return StartLeg(state, now);
    }

    public static RelayMatchSnapshot AwardPoint(RelayMatchSnapshot state, int side, DateTimeOffset now)
    {
        Require(state, RelayStatuses.Running, now);
        // EndsAtUtc is intentionally not checked: the ten-minute clock is display-only.
        return AdvanceVersion(AddPoint(state, side, now), now);
    }

    public static RelayMatchSnapshot FinishLeg(RelayMatchSnapshot state, DateTimeOffset now)
    {
        Require(state, RelayStatuses.Running, now);
        // A referee may close the leg before or after the display clock reaches zero.
        var next = CloseLeg(state, now) with { Status = RelayStatuses.AwaitingChange };
        return AdvanceVersion(next, now);
    }

    public static RelayMatchSnapshot StartNextLeg(RelayMatchSnapshot state, DateTimeOffset now)
    {
        Require(state, RelayStatuses.AwaitingChange, now);
        return StartLeg(state, now);
    }

    private static RelayMatchSnapshot StartLeg(RelayMatchSnapshot state, DateTimeOffset now)
    {
        now = now.ToUniversalTime();
        var number = checked(state.Legs.Count + 1);
        var pair = (number - 1) % state.Rules.PairCount + 1;
        var leg = new RelayLegSnapshot(number, pair, now, now.AddSeconds(state.Rules.LegDurationSeconds),
            null, state.ScoreTeam1, state.ScoreTeam2, null, null);
        return AdvanceVersion(state with { Status = RelayStatuses.Running, Legs = [.. state.Legs, leg] }, now);
    }

    private static RelayMatchSnapshot AddPoint(RelayMatchSnapshot state, int side, DateTimeOffset now)
    {
        if (side is not (1 or 2))
            throw new RelayRuleException("SIDE_INVALID", "Chọn đội 1 hoặc đội 2.");
        var next = side == 1 ? state with { ScoreTeam1 = checked(state.ScoreTeam1 + 1) }
                            : state with { ScoreTeam2 = checked(state.ScoreTeam2 + 1) };
        if (next.ScoreTeam1 == next.Rules.TargetScore || next.ScoreTeam2 == next.Rules.TargetScore)
            return CloseLeg(next, now) with
            {
                Status = RelayStatuses.Completed,
                WinnerRegistrationId = side == 1 ? next.Team1RegistrationId : next.Team2RegistrationId
            };
        return next;
    }

    private static RelayMatchSnapshot CloseLeg(RelayMatchSnapshot state, DateTimeOffset now)
    {
        var legs = state.Legs.ToArray();
        legs[^1] = legs[^1] with
        {
            FinishedAtUtc = now.ToUniversalTime(),
            EndScoreTeam1 = state.ScoreTeam1,
            EndScoreTeam2 = state.ScoreTeam2
        };
        return state with { Legs = legs };
    }

    private static RelayMatchSnapshot AdvanceVersion(RelayMatchSnapshot state, DateTimeOffset now) =>
        state with { Version = checked(state.Version + 1), UpdatedAtUtc = now.ToUniversalTime() };

    private static void Require(RelayMatchSnapshot state, string expected, DateTimeOffset now)
    {
        state.Rules.Validate();
        if (state.Status != expected)
            throw new RelayRuleException("MATCH_STATE_INVALID", "Thao tác không hợp lệ với trạng thái trận hiện tại.");
        if (state.UpdatedAtUtc.HasValue && now < state.UpdatedAtUtc.Value)
            throw new RelayRuleException("CLOCK_MOVED_BACKWARDS", "Thời gian server sớm hơn thao tác đã lưu; cần đồng bộ lại.");
        if (state.ScoreTeam1 < 0 || state.ScoreTeam2 < 0
            || state.ScoreTeam1 >= state.Rules.TargetScore || state.ScoreTeam2 >= state.Rules.TargetScore)
            throw new RelayRuleException("MATCH_SCORE_INVALID", "Tỷ số hiện tại không hợp lệ cho thao tác.");
        if (expected != RelayStatuses.Ready && state.Legs.Count == 0)
            throw new RelayRuleException("LEG_MISSING", "Không tìm thấy dữ liệu lượt thi đấu.");
    }
}
