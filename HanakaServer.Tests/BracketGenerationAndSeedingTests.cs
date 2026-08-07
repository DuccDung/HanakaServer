using HanakaServer.Dtos.Brackets;
using HanakaServer.Helpers;
using HanakaServer.Models;
using HanakaServer.Services;
using HanakaServer.Services.Brackets;
using Xunit;

namespace HanakaServer.Tests;

public sealed class BracketGenerationAndSeedingTests
{
    [Theory]
    [InlineData(4, 2, 3)]
    [InlineData(8, 3, 7)]
    [InlineData(16, 4, 15)]
    [InlineData(32, 5, 31)]
    [InlineData(64, 6, 63)]
    public void Single_elimination_generator_creates_expected_topology(
        int capacity, int expectedRounds, int expectedMatches)
    {
        var request = BracketTemplateService.GenerateSingleElimination(capacity, false, [1]);

        Assert.Equal(expectedRounds, request.Rounds.Count);
        Assert.Equal(expectedMatches, request.Rounds.Sum(x => x.Groups.Sum(g => g.Matches.Count)));
        Assert.Equal(capacity / 2, request.MinimumTeams);
        Assert.Equal(capacity, request.SeedCapacity);
        Assert.True(request.AllowBye);
        Assert.Single(request.Rounds.Last().Groups.SelectMany(x => x.Matches));
        Assert.True(request.Rounds.Last().Groups.Single().Matches.Single().IsTerminal);
    }

    [Fact]
    public void Third_place_uses_both_semifinal_losers()
    {
        var request = BracketTemplateService.GenerateSingleElimination(8, true, [1]);
        var thirdPlace = request.Rounds.SelectMany(x => x.Groups).SelectMany(x => x.Matches)
            .Single(x => x.TerminalType == "THIRD_PLACE");

        Assert.All(thirdPlace.Slots, x => Assert.Equal(BracketTemplateSourceTypes.LoserMatch, x.SourceType));
        Assert.Equal(2, thirdPlace.Slots.Select(x => x.SourceMatchKey).Distinct().Count());
    }

    [Fact]
    public void Standard_eight_seed_placement_prioritizes_top_seeds_for_bye()
    {
        var positions = BracketTemplateService.BuildSeedPositions(8);

        Assert.Equal(new[] { 1, 8, 4, 5, 2, 7, 3, 6 }, positions);
    }

    [Fact]
    public void Group_generator_creates_round_robin_and_group_rank_sources()
    {
        var request = BracketTemplateService.GenerateGroupKnockout(2, 4, false, [1]);
        var groupRound = request.Rounds.Single(x => x.RoundType == BracketRoundTypes.GroupStage);
        var knockoutMatches = request.Rounds.Where(x => x.RoundType != BracketRoundTypes.GroupStage)
            .SelectMany(x => x.Groups).SelectMany(x => x.Matches).ToList();

        Assert.Equal(2, groupRound.Groups.Count);
        Assert.All(groupRound.Groups, group => Assert.Equal(6, group.Matches.Count));
        Assert.Equal(12, groupRound.Groups.Sum(x => x.Matches.Count));
        Assert.All(knockoutMatches.First().Slots, slot => Assert.Equal(BracketTemplateSourceTypes.GroupRank, slot.SourceType));
    }

    [Fact]
    public void Registration_order_seeding_is_stable_and_adds_byes()
    {
        var registrations = Registrations(6);

        var result = TournamentBracketApplicationService.BuildSeeds(
            registrations, 8, BracketSeedingMethods.RegistrationOrder, null, []);

        Assert.True(result.Success);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6 }, result.Data!.Seeds.Take(6).Select(x => x.RegistrationId!.Value));
        Assert.All(result.Data.Seeds.Skip(6), x => Assert.True(x.IsBye));
    }

    [Fact]
    public void Random_seeding_is_reproducible_with_same_random_seed()
    {
        var registrations = Registrations(8);

        var first = TournamentBracketApplicationService.BuildSeeds(
            registrations, 8, BracketSeedingMethods.Random, 20260802, []);
        var second = TournamentBracketApplicationService.BuildSeeds(
            registrations, 8, BracketSeedingMethods.Random, 20260802, []);

        Assert.Equal(
            first.Data!.Seeds.Select(x => x.RegistrationId),
            second.Data!.Seeds.Select(x => x.RegistrationId));
        Assert.Equal(20260802, first.Data.RandomSeed);
    }

    [Fact]
    public void Generated_random_seed_survives_javascript_number_round_trip()
    {
        for (var index = 0; index < 256; index++)
        {
            var seed = TournamentBracketApplicationService.GenerateRandomSeed();

            Assert.InRange(seed, 1, TournamentBracketApplicationService.JavaScriptMaxSafeInteger);
            Assert.Equal(seed, (long)(double)seed);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9_007_199_254_740_992L)]
    public void Random_seeding_rejects_seed_that_browser_cannot_round_trip(long unsafeSeed)
    {
        var result = TournamentBracketApplicationService.BuildSeeds(
            Registrations(8), 8, BracketSeedingMethods.Random, unsafeSeed, []);

        Assert.False(result.Success);
        Assert.Equal("RANDOM_SEED_INVALID", result.ErrorCode);
    }

    [Fact]
    public void Reshuffle_with_a_new_random_seed_creates_a_new_order()
    {
        var registrations = Registrations(8);

        var first = TournamentBracketApplicationService.BuildSeeds(
            registrations, 8, BracketSeedingMethods.Random, 1, []);
        var second = TournamentBracketApplicationService.BuildSeeds(
            registrations, 8, BracketSeedingMethods.Random, 2, []);

        Assert.NotEqual(
            first.Data!.Seeds.Select(x => x.RegistrationId),
            second.Data!.Seeds.Select(x => x.RegistrationId));
    }

    [Fact]
    public void Team_count_below_template_minimum_is_rejected()
    {
        var result = TournamentBracketApplicationService.ValidateTeamCount(3, 4, 8);

        Assert.False(result.Success);
        Assert.Equal("NOT_ENOUGH_TEAMS", result.ErrorCode);
    }

    [Fact]
    public void Team_count_above_template_capacity_is_rejected()
    {
        var result = TournamentBracketApplicationService.ValidateTeamCount(9, 4, 8);

        Assert.False(result.Success);
        Assert.Equal("TOO_MANY_TEAMS", result.ErrorCode);
    }

    [Fact]
    public void Team_count_inside_manual_range_is_accepted_without_other_conditions()
    {
        var result = TournamentBracketApplicationService.ValidateTeamCount(6, 4, 8);

        Assert.True(result.Success);
    }

    [Fact]
    public void Manual_seeding_supports_bye_holes()
    {
        var registrations = Registrations(6);
        var assignments = new List<ManualSeedAssignmentRequest>
        {
            new() { SeedNumber = 1, RegistrationId = 1 },
            new() { SeedNumber = 2, RegistrationId = 2 },
            new() { SeedNumber = 3, RegistrationId = 3 },
            new() { SeedNumber = 4, RegistrationId = 4 },
            new() { SeedNumber = 6, RegistrationId = 5 },
            new() { SeedNumber = 8, RegistrationId = 6 }
        };

        var result = TournamentBracketApplicationService.BuildSeeds(
            registrations, 8, BracketSeedingMethods.Manual, null, assignments);

        Assert.True(result.Success);
        Assert.True(result.Data!.Seeds.Single(x => x.SeedNumber == 5).IsBye);
        Assert.True(result.Data.Seeds.Single(x => x.SeedNumber == 7).IsBye);
        Assert.All(result.Data.Seeds.Where(x => !x.IsBye), x => Assert.True(x.IsManuallyAdjusted));
    }

    [Fact]
    public void Manual_seeding_rejects_duplicate_registration()
    {
        var registrations = Registrations(2);
        var assignments = new List<ManualSeedAssignmentRequest>
        {
            new() { SeedNumber = 1, RegistrationId = 1 },
            new() { SeedNumber = 2, RegistrationId = 1 }
        };

        var result = TournamentBracketApplicationService.BuildSeeds(
            registrations, 4, BracketSeedingMethods.Manual, null, assignments);

        Assert.False(result.Success);
        Assert.Equal("MANUAL_REGISTRATION_DUPLICATE", result.ErrorCode);
    }

    [Fact]
    public void Ranking_seeding_uses_points_then_combined_level()
    {
        var registrations = Registrations(3);
        registrations[0].Points = 10;
        registrations[1].Points = 20;
        registrations[2].Points = 20;
        registrations[1].Player1Level = 2;
        registrations[2].Player1Level = 4;

        var result = TournamentBracketApplicationService.BuildSeeds(
            registrations, 4, BracketSeedingMethods.Ranking, null, []);

        Assert.Equal(new long?[] { 3, 2, 1 }, result.Data!.Seeds.Take(3).Select(x => x.RegistrationId));
    }

    [Fact]
    public void Initial_bye_resolution_propagates_through_multiple_levels()
    {
        var first = new TournamentGroupMatch
        {
            MatchId = 1,
            Team1SourceType = MatchSourceTypes.Registration,
            Team1RegistrationId = 101,
            Team2SourceType = MatchSourceTypes.Bye
        };
        var second = new TournamentGroupMatch
        {
            MatchId = 2,
            Team1SourceType = MatchSourceTypes.WinnerMatch,
            Team1SourceMatchId = 1,
            Team2SourceType = MatchSourceTypes.Bye
        };

        TournamentBracketApplicationService.ResolveInitialByes([second, first], new DateTime(2026, 8, 2));

        Assert.True(first.IsCompleted);
        Assert.Equal(101, first.WinnerRegistrationId);
        Assert.Equal(MatchCompletionReasons.Bye, first.CompletionReason);
        Assert.True(second.IsCompleted);
        Assert.Equal(101, second.Team1RegistrationId);
        Assert.Equal(101, second.WinnerRegistrationId);
        Assert.Equal(MatchCompletionReasons.Bye, second.CompletionReason);
    }

    [Fact]
    public void Participant_change_clears_uncompleted_target_score()
    {
        var target = new TournamentGroupMatch
        {
            Team1RegistrationId = 202,
            Team2RegistrationId = 303,
            ScoreTeam1 = 7,
            ScoreTeam2 = 4,
            WinnerRegistrationId = 202,
            CompletionReason = "TEMPORARY",
            IsCompleted = false
        };

        TournamentBracketPropagationService.ResetPendingScoreIfParticipantsChanged(target, 101, 303);

        Assert.Equal(0, target.ScoreTeam1);
        Assert.Equal(0, target.ScoreTeam2);
        Assert.Null(target.WinnerRegistrationId);
        Assert.Null(target.CompletionReason);
    }

    private static List<TournamentRegistration> Registrations(int count) => Enumerable.Range(1, count)
        .Select(index => new TournamentRegistration
        {
            RegistrationId = index,
            TournamentId = 1,
            RegCode = $"REG-{index}",
            Player1Name = $"Player {index}",
            Paid = true,
            Success = true,
            RegIndex = index,
            CreatedAt = new DateTime(2026, 8, 2).AddMinutes(index)
        })
        .ToList();
}
