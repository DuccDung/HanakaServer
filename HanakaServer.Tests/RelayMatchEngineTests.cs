using System.Text.Json;
using HanakaServer.Services.Relay;
using Xunit;

namespace HanakaServer.Tests;

public sealed class RelayMatchEngineTests
{
    private static readonly DateTimeOffset StartTime = new(2026, 9, 6, 8, 0, 0, TimeSpan.Zero);
    private static RelayMatchSnapshot New(int size = 6, int target = 40) =>
        RelayMatchEngine.Create(100, 10, 20, new(size, target, 600));

    [Fact]
    public void Full_relay_example_preserves_scores_rotates_and_finishes_at_40()
    {
        var state = RelayMatchEngine.Start(New(), StartTime);
        var totals = new[] { (15, 12), (28, 25), (37, 36) };
        foreach (var (one, two) in totals)
        {
            state = Reach(state, one, two, state.Legs[^1].StartedAtUtc.AddMinutes(1));
            var end = state.LegEndsAtUtc!.Value;
            state = RelayMatchEngine.FinishLeg(state, end);
            Assert.Equal(one, state.Legs[^1].EndScoreTeam1);
            Assert.Equal(two, state.Legs[^1].EndScoreTeam2);
            state = RelayMatchEngine.StartNextLeg(state, end.AddMinutes(2));
            Assert.Equal(one, state.Legs[^1].StartScoreTeam1);
            Assert.Equal(two, state.Legs[^1].StartScoreTeam2);
            Assert.Equal(600, state.RemainingSeconds(end.AddMinutes(2)));
        }
        Assert.Equal(4, state.LegNumber);
        Assert.Equal(1, state.PairNumber);
        state = Reach(state, 39, 38, state.Legs[^1].StartedAtUtc.AddSeconds(10));
        var beforeWin = state;
        state = RelayMatchEngine.AwardPoint(state, 1, state.Legs[^1].StartedAtUtc.AddSeconds(11));
        Assert.Equal(RelayStatuses.Completed, state.Status);
        Assert.Equal(10, state.WinnerRegistrationId);
        Assert.Equal(40, state.ScoreTeam1);
        Assert.Equal(38, state.ScoreTeam2);
        Assert.Equal(39, beforeWin.ScoreTeam1); // transition did not mutate the prior snapshot
        Assert.Throws<RelayRuleException>(() => RelayMatchEngine.StartNextLeg(state, state.LegEndsAtUtc!.Value));
        Assert.Throws<RelayRuleException>(() => RelayMatchEngine.AwardPoint(state, 2, state.LegEndsAtUtc!.Value));
    }

    [Theory]
    [InlineData(4, 2)]
    [InlineData(6, 3)]
    [InlineData(8, 4)]
    public void Multiple_cycles_do_not_create_bracket_matches_or_reset_scores(int size, int pairs)
    {
        var state = RelayMatchEngine.Start(New(size: size), StartTime);
        for (var i = 1; i <= pairs * 3; i++)
        {
            Assert.Equal((i - 1) % pairs + 1, state.PairNumber);
            state = RelayMatchEngine.AwardPoint(state, 2, state.Legs[^1].StartedAtUtc.AddSeconds(1));
            state = RelayMatchEngine.FinishLeg(state, state.LegEndsAtUtc!.Value);
            state = RelayMatchEngine.StartNextLeg(state, state.LegEndsAtUtc!.Value.AddSeconds(5));
        }
        Assert.Equal(100, state.MatchId);
        Assert.Equal(pairs * 3, state.ScoreTeam2);
        Assert.Equal(1, state.PairNumber);
    }

    [Fact]
    public void Clock_is_informational_and_manual_finish_is_allowed_before_or_after_zero()
    {
        var state = RelayMatchEngine.Start(New(), StartTime);
        state = RelayMatchEngine.AwardPoint(state, 1, StartTime.AddMinutes(10));
        state = RelayMatchEngine.AwardPoint(state, 2, StartTime.AddMinutes(12));
        Assert.Equal(RelayStatuses.Running, state.Status);
        Assert.Equal(0, state.RemainingSeconds(StartTime.AddMinutes(12)));
        var finishedAfterZero = RelayMatchEngine.FinishLeg(state, StartTime.AddMinutes(12));
        Assert.Equal(RelayStatuses.AwaitingChange, finishedAfterZero.Status);

        var early = RelayMatchEngine.Start(New(), StartTime);
        early = RelayMatchEngine.FinishLeg(early, StartTime.AddMinutes(3));
        Assert.Equal(RelayStatuses.AwaitingChange, early.Status);
    }

    [Fact]
    public void Configured_target_finishes_the_match_immediately()
    {
        var state = RelayMatchEngine.Start(New(target: 3), StartTime);
        state = RelayMatchEngine.AwardPoint(state, 1, StartTime.AddMinutes(11));
        state = RelayMatchEngine.AwardPoint(state, 2, StartTime.AddMinutes(12));
        state = RelayMatchEngine.AwardPoint(state, 1, StartTime.AddMinutes(13));
        var completed = RelayMatchEngine.AwardPoint(state, 1, StartTime.AddMinutes(14));
        Assert.Equal(RelayStatuses.Completed, completed.Status);
        Assert.Equal(3, completed.ScoreTeam1);
        Assert.Equal(10, completed.WinnerRegistrationId);
        Assert.Throws<RelayRuleException>(() => RelayMatchEngine.AwardPoint(completed, 2, StartTime.AddMinutes(15)));
    }

    [Fact]
    public void Saved_state_recovers_deadline_without_restarting_timer()
    {
        var state = RelayMatchEngine.Start(New(), StartTime);
        state = RelayMatchEngine.AwardPoint(state, 1, StartTime.AddMinutes(2));
        var recovered = JsonSerializer.Deserialize<RelayMatchSnapshot>(JsonSerializer.Serialize(state))!;
        Assert.Equal(180, recovered.RemainingSeconds(StartTime.AddMinutes(7)));
        Assert.Equal(0, recovered.RemainingSeconds(StartTime.AddMinutes(12)));
        Assert.Equal(1, recovered.ScoreTeam1);
        Assert.Equal(state.Version, recovered.Version);
        Assert.Equal(StartTime.AddMinutes(10), recovered.LegEndsAtUtc);
    }

    [Fact]
    public void Invalid_sizes_and_sides_cannot_be_used()
    {
        Assert.Throws<RelayRuleException>(() => New(size: 5));
        var state = RelayMatchEngine.Start(New(), StartTime);
        Assert.Throws<RelayRuleException>(() => RelayMatchEngine.AwardPoint(state, 0, StartTime));
        Assert.Throws<RelayRuleException>(() => RelayMatchEngine.Start(state, StartTime));
        Assert.Throws<RelayRuleException>(() => RelayMatchEngine.StartNextLeg(state, StartTime));
    }

    [Fact]
    public void Backwards_server_clock_does_not_accept_new_commands()
    {
        var state = RelayMatchEngine.Start(New(), StartTime);
        Assert.Equal("CLOCK_MOVED_BACKWARDS", Assert.Throws<RelayRuleException>(() =>
            RelayMatchEngine.AwardPoint(state, 1, StartTime.AddSeconds(-1))).Code);
    }

    private static RelayMatchSnapshot Reach(RelayMatchSnapshot state, int one, int two, DateTimeOffset now)
    {
        while (state.ScoreTeam1 < one) state = RelayMatchEngine.AwardPoint(state, 1, now);
        while (state.ScoreTeam2 < two) state = RelayMatchEngine.AwardPoint(state, 2, now);
        return state;
    }
}
