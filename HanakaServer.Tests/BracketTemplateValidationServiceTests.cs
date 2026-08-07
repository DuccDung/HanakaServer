using HanakaServer.Dtos.Brackets;
using HanakaServer.Models;
using HanakaServer.Services.Brackets;
using Xunit;

namespace HanakaServer.Tests;

public sealed class BracketTemplateValidationServiceTests
{
    private readonly BracketTemplateValidationService _validator = new();

    [Fact]
    public void Valid_four_team_knockout_has_no_errors()
    {
        var result = _validator.Validate(ValidKnockout());

        Assert.True(result.IsValid);
        Assert.Equal(0, result.ErrorCount);
        Assert.Contains(result.Issues, x => x.Code == "GRAPH_SUMMARY" && x.Severity == "INFO");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1025)]
    public void Initial_team_position_outside_supported_range_is_rejected(int seedNumber)
    {
        var graph = ValidKnockout();
        graph.Rounds[0].Groups[0].Matches[0].Slots[0].SeedNumber = seedNumber;

        AssertError(graph, "SEED_INVALID");
    }

    [Fact]
    public void Missing_initial_team_position_is_allowed()
    {
        var graph = ValidKnockout();
        graph.Rounds[0].Groups[0].Matches[0].Slots[0].SeedNumber = null;

        var result = _validator.Validate(graph);

        Assert.DoesNotContain(result.Issues, x => x.Code == "INITIAL_TEAM_POSITION_REQUIRED");
    }

    [Fact]
    public void Duplicate_seed_in_same_match_is_rejected()
    {
        var graph = ValidKnockout();
        graph.Rounds[0].Groups[0].Matches[0].Slots[1].SeedNumber = 1;

        AssertError(graph, "MATCH_DUPLICATE_SEED");
    }

    [Fact]
    public void Reused_seed_in_knockout_is_rejected()
    {
        var graph = ValidKnockout();
        graph.Rounds[0].Groups[0].Matches[1].Slots[0].SeedNumber = 1;

        AssertError(graph, "KNOCKOUT_SEED_REUSED");
    }

    [Fact]
    public void Self_referencing_match_is_rejected()
    {
        var graph = ValidKnockout();
        graph.Rounds[1].Groups[0].Matches[0].Slots[0].SourceMatchKey = "FINAL-M01";

        AssertError(graph, "SOURCE_MATCH_SELF");
    }

    [Fact]
    public void Missing_source_match_is_rejected()
    {
        var graph = ValidKnockout();
        graph.Rounds[1].Groups[0].Matches[0].Slots[0].SourceMatchKey = "UNKNOWN";

        AssertError(graph, "SOURCE_MATCH_NOT_FOUND");
    }

    [Fact]
    public void Source_from_later_round_is_rejected()
    {
        var graph = ValidKnockout();
        graph.Rounds[0].Groups[0].Matches[0].Slots[0] = Winner(1, "FINAL-M01");

        AssertError(graph, "SOURCE_MATCH_NOT_PREVIOUS");
    }

    [Fact]
    public void Two_match_cycle_is_rejected()
    {
        var graph = ValidKnockout();
        graph.Rounds[0].Groups[0].Matches[0].Slots[0] = Winner(1, "FINAL-M01");
        graph.Rounds[1].Groups[0].Matches[0].Slots[0] = Winner(1, "R1-M01");

        AssertError(graph, "DEPENDENCY_CYCLE");
    }

    [Fact]
    public void Multi_match_cycle_is_rejected()
    {
        var graph = ValidKnockout();
        graph.Rounds[0].Groups[0].Matches[0].Slots[0] = Winner(1, "FINAL-M01");
        graph.Rounds[0].Groups[0].Matches[1].Slots[0] = Winner(1, "R1-M01");
        graph.Rounds[1].Groups[0].Matches[0].Slots[0] = Winner(1, "R1-M02");

        AssertError(graph, "DEPENDENCY_CYCLE");
    }

    [Fact]
    public void Duplicate_stable_keys_are_rejected()
    {
        var graph = ValidKnockout();
        graph.Rounds[0].Groups[0].Matches[1].MatchKey = "R1-M01";

        AssertError(graph, "MATCH_KEY_DUPLICATE");
    }

    [Fact]
    public void Two_bye_slots_are_rejected()
    {
        var graph = ValidKnockout();
        graph.Rounds[0].Groups[0].Matches[0].Slots = [Bye(1), Bye(2)];

        AssertError(graph, "DOUBLE_BYE");
    }

    [Fact]
    public void Bye_in_group_stage_is_rejected()
    {
        var graph = ValidKnockout();
        graph.Rounds[0].RoundType = BracketRoundTypes.GroupStage;
        graph.Rounds[0].Groups[0].Matches[0].Slots[0] = Bye(1);

        AssertError(graph, "BYE_GROUP_STAGE");
    }

    [Fact]
    public void Bye_pass_requires_a_winner_target()
    {
        var graph = ValidKnockout();
        graph.Rounds[0].Groups[0].Matches[0].Slots[1] = Bye(2);
        graph.Rounds[1].Groups[0].Matches[0].Slots[0] = Winner(1, "R1-M02");

        AssertError(graph, "BYE_TARGET_REQUIRED");
    }

    [Fact]
    public void Bye_pass_cannot_be_used_as_a_loser_source()
    {
        var graph = ValidKnockout();
        graph.Rounds[0].Groups[0].Matches[0].Slots[1] = Bye(2);
        graph.Rounds[1].Groups[0].Matches[0].Slots[0] = Loser(1, "R1-M01");

        AssertError(graph, "BYE_LOSER_SOURCE_INVALID");
    }

    [Fact]
    public void Bye_pass_cannot_feed_multiple_targets()
    {
        var graph = ValidKnockout();
        graph.Rounds[0].Groups[0].Matches[0].Slots[1] = Bye(2);
        graph.Rounds[1].Groups[0].Matches.Add(
            Match("FINAL-M02", 1, Winner(1, "R1-M01"), Winner(2, "R1-M02")));

        AssertError(graph, "BYE_MULTIPLE_TARGETS");
    }

    [Fact]
    public void Missing_source_group_is_rejected()
    {
        var graph = ValidKnockout();
        graph.Rounds[1].Groups[0].Matches[0].Slots[0] = GroupRank(1, "UNKNOWN", 1);

        AssertError(graph, "SOURCE_GROUP_NOT_FOUND");
    }

    [Fact]
    public void Group_rank_zero_is_rejected()
    {
        var graph = ValidGroupToKnockout();
        graph.Rounds[1].Groups[0].Matches[0].Slots[0].SourceRank = 0;

        AssertError(graph, "SOURCE_RANK_INVALID");
    }

    [Fact]
    public void Group_rank_above_team_count_is_rejected()
    {
        var graph = ValidGroupToKnockout();
        graph.Rounds[1].Groups[0].Matches[0].Slots[0].SourceRank = 3;

        AssertError(graph, "SOURCE_RANK_EXCEEDS_GROUP");
    }

    [Fact]
    public void Group_cannot_source_rank_from_itself()
    {
        var graph = ValidGroupToKnockout();
        var final = graph.Rounds[1].Groups[0];
        final.Matches[0].Slots[0].SourceGroupKey = final.GroupKey;

        AssertError(graph, "SOURCE_GROUP_SELF");
    }

    [Fact]
    public void Round_robin_duplicate_pair_is_rejected()
    {
        var graph = ValidGroupToKnockout();
        graph.Rounds[0].Groups[0].Matches.Add(Match("A-M02", 1, Seed(1, 2), Seed(2, 1)));

        AssertError(graph, "GROUP_PAIR_DUPLICATE");
    }

    [Fact]
    public void Manual_capacity_no_longer_creates_unused_seed_errors()
    {
        var graph = ValidKnockout();
        graph.SeedCapacity = 5;

        var result = _validator.Validate(graph);

        Assert.DoesNotContain(result.Issues, x => x.Code == "SEED_UNUSED");
    }

    [Fact]
    public void Minimum_team_limit_cannot_exceed_maximum_team_limit()
    {
        var graph = ValidKnockout();
        graph.MinimumTeams = 5;
        graph.SeedCapacity = 4;

        AssertError(graph, "TEAM_RANGE_INVALID");
    }

    [Fact]
    public void Loser_without_target_is_not_reported_when_loser_branch_exists()
    {
        var graph = ValidKnockout();
        graph.Rounds[1].Groups[0].Matches[0].Slots[0] = Loser(1, "R1-M01");

        var result = _validator.Validate(graph);

        Assert.DoesNotContain(result.Issues, x => x.Code == "LOSER_NO_TARGET");
    }

    [Fact]
    public void Multiple_champion_finals_are_rejected()
    {
        var graph = ValidKnockout();
        graph.Rounds[0].Groups[0].Matches[0].IsTerminal = true;
        graph.Rounds[0].Groups[0].Matches[0].TerminalType = "CHAMPION";

        AssertError(graph, "MULTIPLE_CHAMPION_FINALS");
    }

    [Fact]
    public void Round_robin_missing_pair_is_rejected()
    {
        var graph = ValidGroupToKnockout();
        var group = graph.Rounds[0].Groups[0];
        group.Matches =
        [
            Match("A-M01", 0, Seed(1, 1), Seed(2, 2)),
            Match("A-M02", 1, Seed(1, 1), Seed(2, 3))
        ];
        graph.SeedCapacity = 4;

        AssertError(graph, "GROUP_PAIR_MISSING");
    }

    [Fact]
    public void Missing_champion_terminal_is_rejected()
    {
        var graph = ValidKnockout();
        graph.Rounds[1].Groups[0].Matches[0].IsTerminal = false;
        graph.Rounds[1].Groups[0].Matches[0].TerminalType = null;

        AssertError(graph, "TERMINAL_MATCH_MISSING");
    }

    [Fact]
    public void Same_winner_source_in_both_slots_is_rejected()
    {
        var graph = ValidKnockout();
        graph.Rounds[1].Groups[0].Matches[0].Slots[1] = Winner(2, "R1-M01");

        AssertError(graph, "MATCH_DUPLICATE_SOURCE");
    }

    [Fact]
    public void Same_loser_source_in_both_slots_is_rejected()
    {
        var graph = ValidKnockout();
        graph.Rounds[1].Groups[0].Matches[0].Slots =
        [
            Loser(1, "R1-M01"),
            Loser(2, "R1-M01")
        ];

        AssertError(graph, "MATCH_DUPLICATE_SOURCE");
    }

    [Fact]
    public void Same_group_rank_source_in_both_slots_is_rejected()
    {
        var graph = ValidGroupToKnockout();
        graph.Rounds[1].Groups[0].Matches[0].Slots[1] = GroupRank(2, "GROUP-A", 1);

        AssertError(graph, "MATCH_DUPLICATE_SOURCE");
    }

    [Fact]
    public void Winner_and_loser_of_same_match_are_distinct_sources()
    {
        var graph = ValidKnockout();
        graph.Rounds[1].Groups[0].Matches[0].Slots =
        [
            Winner(1, "R1-M01"),
            Loser(2, "R1-M01")
        ];

        var result = _validator.Validate(graph);

        Assert.DoesNotContain(result.Issues, x => x.Code == "MATCH_DUPLICATE_SOURCE");
    }

    [Fact]
    public void Unbalanced_knockout_rounds_are_reported()
    {
        var graph = ValidKnockout();
        graph.Rounds[0].Groups[0].Matches.Add(Match("R1-M03", 2, Seed(1, 1), Seed(2, 2)));

        AssertWarning(graph, "BRACKET_UNBALANCED");
    }

    private void AssertError(BracketTemplateGraphDto graph, string code)
    {
        var result = _validator.Validate(graph);
        Assert.Contains(result.Issues, x => x.Code == code && x.Severity == "ERROR");
    }

    private void AssertWarning(BracketTemplateGraphDto graph, string code)
    {
        var result = _validator.Validate(graph);
        Assert.Contains(result.Issues, x => x.Code == code && x.Severity == "WARNING");
    }

    private static BracketTemplateGraphDto ValidKnockout() => new()
    {
        BracketTemplateId = 1,
        BracketTemplateVersionId = 1,
        VersionNumber = 1,
        Status = BracketTemplateStatuses.Draft,
        MinimumTeams = 2,
        SeedCapacity = 4,
        AllowBye = true,
        DefaultSeedingMethod = BracketSeedingMethods.RegistrationOrder,
        Rounds =
        [
            new BracketTemplateRoundDto
            {
                RoundKey = "R1", RoundLabel = "Bán kết", RoundType = BracketRoundTypes.Knockout, SortOrder = 0,
                Groups =
                [
                    new BracketTemplateGroupDto
                    {
                        GroupKey = "R1-G1", GroupName = "Bán kết", GroupType = BracketGroupTypes.KnockoutBranch,
                        Matches =
                        [
                            Match("R1-M01", 0, Seed(1, 1), Seed(2, 4)),
                            Match("R1-M02", 1, Seed(1, 2), Seed(2, 3))
                        ]
                    }
                ]
            },
            new BracketTemplateRoundDto
            {
                RoundKey = "FINAL", RoundLabel = "Chung kết", RoundType = BracketRoundTypes.Final, SortOrder = 1,
                Groups =
                [
                    new BracketTemplateGroupDto
                    {
                        GroupKey = "FINAL-G1", GroupName = "Chung kết", GroupType = BracketGroupTypes.Final,
                        Matches = [Match("FINAL-M01", 0, Winner(1, "R1-M01"), Winner(2, "R1-M02"), true)]
                    }
                ]
            }
        ]
    };

    private static BracketTemplateGraphDto ValidGroupToKnockout()
    {
        var graph = new BracketTemplateGraphDto
        {
            BracketTemplateId = 2,
            BracketTemplateVersionId = 2,
            VersionNumber = 1,
            Status = BracketTemplateStatuses.Draft,
            MinimumTeams = 4,
            SeedCapacity = 4,
            AllowBye = false,
            DefaultSeedingMethod = BracketSeedingMethods.RegistrationOrder
        };
        graph.Rounds.Add(new BracketTemplateRoundDto
        {
            RoundKey = "GROUP", RoundLabel = "Vòng bảng", RoundType = BracketRoundTypes.GroupStage, SortOrder = 0,
            Groups =
            [
                new BracketTemplateGroupDto
                {
                    GroupKey = "GROUP-A", GroupName = "Bảng A", GroupType = BracketGroupTypes.RoundRobin,
                    Matches = [Match("A-M01", 0, Seed(1, 1), Seed(2, 2))]
                },
                new BracketTemplateGroupDto
                {
                    GroupKey = "GROUP-B", GroupName = "Bảng B", GroupType = BracketGroupTypes.RoundRobin,
                    Matches = [Match("B-M01", 0, Seed(1, 3), Seed(2, 4))]
                }
            ]
        });
        graph.Rounds.Add(new BracketTemplateRoundDto
        {
            RoundKey = "FINAL", RoundLabel = "Chung kết", RoundType = BracketRoundTypes.Final, SortOrder = 1,
            Groups =
            [
                new BracketTemplateGroupDto
                {
                    GroupKey = "FINAL-G1", GroupName = "Chung kết", GroupType = BracketGroupTypes.Final,
                    Matches = [Match("FINAL-M01", 0, GroupRank(1, "GROUP-A", 1), GroupRank(2, "GROUP-B", 1), true)]
                }
            ]
        });
        return graph;
    }

    private static BracketTemplateMatchDto Match(
        string key, int order, BracketTemplateSlotDto slot1, BracketTemplateSlotDto slot2, bool terminal = false) => new()
    {
        MatchKey = key,
        MatchLabel = key,
        SortOrder = order,
        IsTerminal = terminal,
        TerminalType = terminal ? "CHAMPION" : null,
        Slots = [slot1, slot2]
    };

    private static BracketTemplateSlotDto Seed(byte slot, int seed) => new() { SlotNumber = slot, SourceType = BracketTemplateSourceTypes.Seed, SeedNumber = seed };
    private static BracketTemplateSlotDto Winner(byte slot, string match) => new() { SlotNumber = slot, SourceType = BracketTemplateSourceTypes.WinnerMatch, SourceMatchKey = match };
    private static BracketTemplateSlotDto Loser(byte slot, string match) => new() { SlotNumber = slot, SourceType = BracketTemplateSourceTypes.LoserMatch, SourceMatchKey = match };
    private static BracketTemplateSlotDto GroupRank(byte slot, string group, int rank) => new() { SlotNumber = slot, SourceType = BracketTemplateSourceTypes.GroupRank, SourceGroupKey = group, SourceRank = rank };
    private static BracketTemplateSlotDto Bye(byte slot) => new() { SlotNumber = slot, SourceType = BracketTemplateSourceTypes.Bye };
}
