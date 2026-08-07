using HanakaServer.Dtos.Brackets;
using HanakaServer.Models;

namespace HanakaServer.Services.Brackets;

public interface IBracketTemplateValidationService
{
    BracketValidationResultDto Validate(BracketTemplateGraphDto graph);
}

public sealed class BracketTemplateValidationService : IBracketTemplateValidationService
{
    private static readonly HashSet<string> RoundTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        BracketRoundTypes.GroupStage,
        BracketRoundTypes.Knockout,
        BracketRoundTypes.Final,
        BracketRoundTypes.Placement,
        BracketRoundTypes.LoserBracket
    };

    private static readonly HashSet<string> GroupTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        BracketGroupTypes.Generic,
        BracketGroupTypes.RoundRobin,
        BracketGroupTypes.KnockoutBranch,
        BracketGroupTypes.Final,
        BracketGroupTypes.Placement
    };

    private static readonly HashSet<string> SourceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        BracketTemplateSourceTypes.Seed,
        BracketTemplateSourceTypes.WinnerMatch,
        BracketTemplateSourceTypes.LoserMatch,
        BracketTemplateSourceTypes.GroupRank,
        BracketTemplateSourceTypes.Bye
    };

    public BracketValidationResultDto Validate(BracketTemplateGraphDto graph)
    {
        var result = new BracketValidationResultDto();

        if (graph.MinimumTeams is < 2 or > 1024)
            Error(result, "MINIMUM_TEAMS_INVALID", "Số đội tối thiểu phải nằm trong khoảng 2 đến 1024.");
        if (graph.SeedCapacity is < 2 or > 1024)
            Error(result, "SEED_CAPACITY_INVALID", "Số đội tối đa phải nằm trong khoảng 2 đến 1024.");
        if (graph.MinimumTeams > graph.SeedCapacity)
            Error(result, "TEAM_RANGE_INVALID", "Số đội tối thiểu không được lớn hơn số đội tối đa.");

        if (graph.Rounds.Count == 0)
        {
            Error(result, "ROUND_REQUIRED", "Template phải có ít nhất một vòng đấu.");
            return result;
        }

        ValidateUniqueKeys(result, graph);

        var roundsByKey = graph.Rounds
            .Where(x => !string.IsNullOrWhiteSpace(x.RoundKey))
            .GroupBy(x => NormalizeKey(x.RoundKey), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var groups = graph.Rounds.SelectMany(x => x.Groups.Select(g => new GroupLocation(x, g))).ToList();
        var groupsByKey = groups
            .Where(x => !string.IsNullOrWhiteSpace(x.Group.GroupKey))
            .GroupBy(x => NormalizeKey(x.Group.GroupKey), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var matches = groups
            .SelectMany(x => x.Group.Matches.Select(m => new MatchLocation(x.Round, x.Group, m)))
            .ToList();
        var matchesByKey = matches
            .Where(x => !string.IsNullOrWhiteSpace(x.Match.MatchKey))
            .GroupBy(x => NormalizeKey(x.Match.MatchKey), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var round in graph.Rounds)
        {
            if (string.IsNullOrWhiteSpace(round.RoundKey))
                Error(result, "ROUND_KEY_REQUIRED", "Mã vòng không được để trống.");
            if (string.IsNullOrWhiteSpace(round.RoundLabel))
                Error(result, "ROUND_LABEL_REQUIRED", "Tên vòng không được để trống.", round.RoundKey);
            if (!RoundTypes.Contains(round.RoundType))
                Error(result, "ROUND_TYPE_INVALID", $"Loại vòng '{round.RoundType}' không hợp lệ.", round.RoundKey);
            if (round.Groups.Count == 0)
                Error(result, "GROUP_REQUIRED", "Vòng phải có ít nhất một bảng/nhánh.", round.RoundKey);

            foreach (var group in round.Groups)
            {
                if (string.IsNullOrWhiteSpace(group.GroupKey))
                    Error(result, "GROUP_KEY_REQUIRED", "Mã bảng không được để trống.", round.RoundKey);
                if (string.IsNullOrWhiteSpace(group.GroupName))
                    Error(result, "GROUP_NAME_REQUIRED", "Tên bảng không được để trống.", round.RoundKey, group.GroupKey);
                if (!GroupTypes.Contains(group.GroupType))
                    Error(result, "GROUP_TYPE_INVALID", $"Loại bảng '{group.GroupType}' không hợp lệ.", round.RoundKey, group.GroupKey);
                if (group.Matches.Count == 0)
                    Error(result, "MATCH_REQUIRED", "Bảng/nhánh phải có ít nhất một trận.", round.RoundKey, group.GroupKey);

                foreach (var match in group.Matches)
                    ValidateMatch(result, round, group, match, matchesByKey, groupsByKey);

                if (group.GroupType.Equals(BracketGroupTypes.RoundRobin, StringComparison.OrdinalIgnoreCase))
                {
                    var seedPairs = group.Matches
                        .Where(x => x.Slots.Count == 2
                                    && x.Slots.All(s => NormalizeKey(s.SourceType) == BracketTemplateSourceTypes.Seed && s.SeedNumber.HasValue))
                        .Select(x => new
                        {
                            Match = x,
                            SeedA = Math.Min(x.Slots[0].SeedNumber!.Value, x.Slots[1].SeedNumber!.Value),
                            SeedB = Math.Max(x.Slots[0].SeedNumber!.Value, x.Slots[1].SeedNumber!.Value)
                        })
                        .ToList();
                    var duplicatePair = seedPairs
                        .GroupBy(x => new { x.SeedA, x.SeedB })
                        .FirstOrDefault(x => x.Count() > 1);
                    if (duplicatePair != null)
                    {
                        Error(result, "GROUP_PAIR_DUPLICATE",
                            $"Cặp Seed {duplicatePair.Key.SeedA}-{duplicatePair.Key.SeedB} xuất hiện nhiều lần trong bảng.",
                            round.RoundKey, group.GroupKey, duplicatePair.First().Match.MatchKey);
                    }

                    var participatingSeeds = seedPairs
                        .SelectMany(x => new[] { x.SeedA, x.SeedB })
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();
                    var existingPairs = seedPairs
                        .Select(x => (x.SeedA, x.SeedB))
                        .ToHashSet();
                    for (var first = 0; first < participatingSeeds.Count; first++)
                    for (var second = first + 1; second < participatingSeeds.Count; second++)
                    {
                        var expectedPair = (participatingSeeds[first], participatingSeeds[second]);
                        if (!existingPairs.Contains(expectedPair))
                        {
                            Error(result, "GROUP_PAIR_MISSING",
                                $"Bảng vòng tròn thiếu cặp Seed {expectedPair.Item1}-{expectedPair.Item2}.",
                                round.RoundKey, group.GroupKey);
                        }
                    }
                }
            }
        }

        ValidateDependencies(result, matches, matchesByKey, groupsByKey);
        ValidateSeedUsage(result, matches);
        ValidateTerminalFlow(result, matches, groupsByKey);

        if (roundsByKey.Count > 0)
        {
            Info(result, "GRAPH_SUMMARY",
                $"Template có {graph.Rounds.Count} vòng, {groups.Count} bảng/nhánh và {matches.Count} trận.");
        }

        return result;
    }

    private static void ValidateUniqueKeys(BracketValidationResultDto result, BracketTemplateGraphDto graph)
    {
        AddDuplicateIssues(result, graph.Rounds.Select(x => x.RoundKey), "ROUND_KEY_DUPLICATE", "Mã vòng bị trùng");
        AddDuplicateIssues(result, graph.Rounds.SelectMany(x => x.Groups).Select(x => x.GroupKey), "GROUP_KEY_DUPLICATE", "Mã bảng bị trùng trong template");
        AddDuplicateIssues(result, graph.Rounds.SelectMany(x => x.Groups).SelectMany(x => x.Matches).Select(x => x.MatchKey), "MATCH_KEY_DUPLICATE", "Mã trận bị trùng trong template");

        foreach (var round in graph.Rounds)
        {
            var duplicateNames = round.Groups
                .Where(x => !string.IsNullOrWhiteSpace(x.GroupName))
                .GroupBy(x => x.GroupName.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(x => x.Count() > 1);
            foreach (var duplicate in duplicateNames)
                Error(result, "GROUP_NAME_DUPLICATE", $"Tên bảng '{duplicate.Key}' bị trùng trong vòng.", round.RoundKey);
        }
    }

    private static void AddDuplicateIssues(
        BracketValidationResultDto result,
        IEnumerable<string> keys,
        string code,
        string message)
    {
        foreach (var duplicate in keys
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Select(NormalizeKey)
                     .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                     .Where(x => x.Count() > 1))
        {
            Error(result, code, $"{message}: {duplicate.Key}.");
        }
    }

    private static void ValidateMatch(
        BracketValidationResultDto result,
        BracketTemplateRoundDto round,
        BracketTemplateGroupDto group,
        BracketTemplateMatchDto match,
        IReadOnlyDictionary<string, MatchLocation> matchesByKey,
        IReadOnlyDictionary<string, GroupLocation> groupsByKey)
    {
        if (string.IsNullOrWhiteSpace(match.MatchKey))
            Error(result, "MATCH_KEY_REQUIRED", "Mã trận không được để trống.", round.RoundKey, group.GroupKey);
        if (match.Slots.Count != 2)
            Error(result, "MATCH_SLOT_COUNT", "Mỗi trận phải có đúng hai slot.", round.RoundKey, group.GroupKey, match.MatchKey);
        if (match.Slots.Select(x => x.SlotNumber).Distinct().Count() != match.Slots.Count
            || match.Slots.Any(x => x.SlotNumber is < 1 or > 2))
        {
            Error(result, "MATCH_SLOT_NUMBER", "Slot của trận phải gồm duy nhất slot 1 và slot 2.", round.RoundKey, group.GroupKey, match.MatchKey);
        }

        if (match.IsTerminal && string.IsNullOrWhiteSpace(match.TerminalType))
            Error(result, "TERMINAL_TYPE_REQUIRED", "Trận terminal phải có loại kết thúc.", round.RoundKey, group.GroupKey, match.MatchKey);
        if (!match.IsTerminal && !string.IsNullOrWhiteSpace(match.TerminalType))
            Error(result, "TERMINAL_STATE_INVALID", "Trận không phải terminal không được có TerminalType.", round.RoundKey, group.GroupKey, match.MatchKey);

        foreach (var slot in match.Slots)
        {
            var sourceType = NormalizeKey(slot.SourceType);
            if (!SourceTypes.Contains(sourceType))
            {
                Error(result, "SOURCE_TYPE_INVALID", $"Nguồn slot '{slot.SourceType}' không hợp lệ.", round.RoundKey, group.GroupKey, match.MatchKey, slot.SlotNumber);
                continue;
            }

            switch (sourceType)
            {
                case BracketTemplateSourceTypes.Seed:
                    if (slot.SeedNumber is < 1 or > 1024)
                        Error(result, "SEED_INVALID", "Vị trí đội ban đầu phải nằm trong khoảng 1..1024.", round.RoundKey, group.GroupKey, match.MatchKey, slot.SlotNumber);
                    if (!string.IsNullOrWhiteSpace(slot.SourceMatchKey) || !string.IsNullOrWhiteSpace(slot.SourceGroupKey) || slot.SourceRank.HasValue)
                        Error(result, "SEED_PAYLOAD_INVALID", "Slot SEED chỉ được chứa SeedNumber.", round.RoundKey, group.GroupKey, match.MatchKey, slot.SlotNumber);
                    break;

                case BracketTemplateSourceTypes.WinnerMatch:
                case BracketTemplateSourceTypes.LoserMatch:
                    if (string.IsNullOrWhiteSpace(slot.SourceMatchKey))
                        Error(result, "SOURCE_MATCH_REQUIRED", "Nguồn thắng/thua phải chọn trận nguồn.", round.RoundKey, group.GroupKey, match.MatchKey, slot.SlotNumber);
                    else if (!matchesByKey.ContainsKey(NormalizeKey(slot.SourceMatchKey)))
                        Error(result, "SOURCE_MATCH_NOT_FOUND", $"Không tìm thấy trận nguồn '{slot.SourceMatchKey}'.", round.RoundKey, group.GroupKey, match.MatchKey, slot.SlotNumber);
                    else if (NormalizeKey(slot.SourceMatchKey) == NormalizeKey(match.MatchKey))
                        Error(result, "SOURCE_MATCH_SELF", "Trận không được tham chiếu chính nó.", round.RoundKey, group.GroupKey, match.MatchKey, slot.SlotNumber);
                    if (slot.SeedNumber.HasValue || !string.IsNullOrWhiteSpace(slot.SourceGroupKey) || slot.SourceRank.HasValue)
                        Error(result, "SOURCE_MATCH_PAYLOAD_INVALID", "Nguồn thắng/thua chỉ được chứa SourceMatchKey.", round.RoundKey, group.GroupKey, match.MatchKey, slot.SlotNumber);
                    break;

                case BracketTemplateSourceTypes.GroupRank:
                    if (string.IsNullOrWhiteSpace(slot.SourceGroupKey))
                        Error(result, "SOURCE_GROUP_REQUIRED", "Nguồn hạng bảng phải chọn bảng nguồn.", round.RoundKey, group.GroupKey, match.MatchKey, slot.SlotNumber);
                    else if (!groupsByKey.ContainsKey(NormalizeKey(slot.SourceGroupKey)))
                        Error(result, "SOURCE_GROUP_NOT_FOUND", $"Không tìm thấy bảng nguồn '{slot.SourceGroupKey}'.", round.RoundKey, group.GroupKey, match.MatchKey, slot.SlotNumber);
                    else if (NormalizeKey(slot.SourceGroupKey) == NormalizeKey(group.GroupKey))
                        Error(result, "SOURCE_GROUP_SELF", "Không được lấy hạng từ chính bảng chứa trận đích.", round.RoundKey, group.GroupKey, match.MatchKey, slot.SlotNumber);
                    if (!slot.SourceRank.HasValue || slot.SourceRank <= 0)
                        Error(result, "SOURCE_RANK_INVALID", "Hạng bảng phải lớn hơn 0.", round.RoundKey, group.GroupKey, match.MatchKey, slot.SlotNumber);
                    if (slot.SeedNumber.HasValue || !string.IsNullOrWhiteSpace(slot.SourceMatchKey))
                        Error(result, "SOURCE_GROUP_PAYLOAD_INVALID", "Nguồn hạng bảng chỉ được chứa SourceGroupKey và SourceRank.", round.RoundKey, group.GroupKey, match.MatchKey, slot.SlotNumber);
                    break;

                case BracketTemplateSourceTypes.Bye:
                    if (round.RoundType.Equals(BracketRoundTypes.GroupStage, StringComparison.OrdinalIgnoreCase))
                        Error(result, "BYE_GROUP_STAGE", "MVP không cho BYE trong vòng bảng.", round.RoundKey, group.GroupKey, match.MatchKey, slot.SlotNumber);
                    if (slot.SeedNumber.HasValue || !string.IsNullOrWhiteSpace(slot.SourceMatchKey) || !string.IsNullOrWhiteSpace(slot.SourceGroupKey) || slot.SourceRank.HasValue)
                        Error(result, "BYE_PAYLOAD_INVALID", "Slot BYE không được chứa dữ liệu nguồn.", round.RoundKey, group.GroupKey, match.MatchKey, slot.SlotNumber);
                    break;
            }
        }

        if (match.Slots.Count(x => NormalizeKey(x.SourceType) == BracketTemplateSourceTypes.Bye) == 2)
            Error(result, "DOUBLE_BYE", "Một trận không được có cả hai slot đều BYE.", round.RoundKey, group.GroupKey, match.MatchKey);

        var duplicateSeed = match.Slots
            .Where(x => NormalizeKey(x.SourceType) == BracketTemplateSourceTypes.Seed && x.SeedNumber.HasValue)
            .GroupBy(x => x.SeedNumber!.Value)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicateSeed != null)
            Error(result, "MATCH_DUPLICATE_SEED", $"Hai slot đang dùng cùng Seed {duplicateSeed.Key}.", round.RoundKey, group.GroupKey, match.MatchKey);

        if (match.Slots.Count == 2 && IsSameResolvableSource(match.Slots[0], match.Slots[1]))
        {
            Error(result, "MATCH_DUPLICATE_SOURCE",
                "Hai slot của trận không được dùng cùng một nguồn chắc chắn cho ra cùng đội.",
                round.RoundKey, group.GroupKey, match.MatchKey);
        }
    }

    private static void ValidateDependencies(
        BracketValidationResultDto result,
        IReadOnlyCollection<MatchLocation> matches,
        IReadOnlyDictionary<string, MatchLocation> matchesByKey,
        IReadOnlyDictionary<string, GroupLocation> groupsByKey)
    {
        // Duplicate keys are reported by ValidateUniqueKeys; keep validation running
        // instead of throwing while building the dependency graph.
        var dependencies = matches
            .GroupBy(x => NormalizeKey(x.Match.MatchKey), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                _ => new List<string>(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var target in matches)
        {
            foreach (var slot in target.Match.Slots)
            {
                var sourceType = NormalizeKey(slot.SourceType);
                if (sourceType is BracketTemplateSourceTypes.WinnerMatch or BracketTemplateSourceTypes.LoserMatch
                    && !string.IsNullOrWhiteSpace(slot.SourceMatchKey)
                    && matchesByKey.TryGetValue(NormalizeKey(slot.SourceMatchKey), out var source))
                {
                    dependencies[NormalizeKey(target.Match.MatchKey)].Add(NormalizeKey(source.Match.MatchKey));
                    if (sourceType == BracketTemplateSourceTypes.LoserMatch && HasByeSlot(source.Match))
                    {
                        Error(result, "BYE_LOSER_SOURCE_INVALID",
                            $"Suất BYE '{source.Match.MatchKey}' không có đội thua; hãy dùng nguồn đội thắng/BYE đi tiếp.",
                            target.Round.RoundKey, target.Group.GroupKey, target.Match.MatchKey, slot.SlotNumber);
                    }
                    if (!ComesBefore(source, target))
                    {
                        Error(result, "SOURCE_MATCH_NOT_PREVIOUS",
                            $"Trận nguồn '{source.Match.MatchKey}' phải đứng trước trận đích.",
                            target.Round.RoundKey, target.Group.GroupKey, target.Match.MatchKey, slot.SlotNumber);
                    }
                }

                if (sourceType == BracketTemplateSourceTypes.GroupRank
                    && !string.IsNullOrWhiteSpace(slot.SourceGroupKey)
                    && groupsByKey.TryGetValue(NormalizeKey(slot.SourceGroupKey), out var sourceGroup))
                {
                    if (sourceGroup.Round.SortOrder >= target.Round.SortOrder)
                    {
                        Error(result, "SOURCE_GROUP_NOT_PREVIOUS",
                            $"Bảng nguồn '{sourceGroup.Group.GroupKey}' phải thuộc vòng trước trận đích.",
                            target.Round.RoundKey, target.Group.GroupKey, target.Match.MatchKey, slot.SlotNumber);
                    }

                    var distinctSeeds = sourceGroup.Group.Matches
                        .SelectMany(x => x.Slots)
                        .Where(x => NormalizeKey(x.SourceType) == BracketTemplateSourceTypes.Seed && x.SeedNumber.HasValue)
                        .Select(x => x.SeedNumber!.Value)
                        .Distinct()
                        .Count();
                    if (slot.SourceRank.HasValue && distinctSeeds > 0 && slot.SourceRank.Value > distinctSeeds)
                    {
                        Error(result, "SOURCE_RANK_EXCEEDS_GROUP",
                            $"Hạng {slot.SourceRank.Value} vượt quá {distinctSeeds} đội có thể có trong bảng nguồn.",
                            target.Round.RoundKey, target.Group.GroupKey, target.Match.MatchKey, slot.SlotNumber);
                    }
                }
            }
        }

        var state = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var cycleReported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in dependencies.Keys)
            Visit(node, dependencies, state, cycleReported, result, matchesByKey);
    }

    private static void Visit(
        string node,
        IReadOnlyDictionary<string, List<string>> dependencies,
        IDictionary<string, byte> state,
        ISet<string> cycleReported,
        BracketValidationResultDto result,
        IReadOnlyDictionary<string, MatchLocation> matchesByKey)
    {
        if (state.TryGetValue(node, out var currentState))
        {
            if (currentState == 1 && cycleReported.Add(node) && matchesByKey.TryGetValue(node, out var cycleMatch))
                Error(result, "DEPENDENCY_CYCLE", "Phát hiện chu trình trong nguồn trận.", cycleMatch.Round.RoundKey, cycleMatch.Group.GroupKey, cycleMatch.Match.MatchKey);
            return;
        }

        state[node] = 1;
        if (dependencies.TryGetValue(node, out var sources))
        {
            foreach (var source in sources)
                Visit(source, dependencies, state, cycleReported, result, matchesByKey);
        }
        state[node] = 2;
    }

    private static void ValidateSeedUsage(
        BracketValidationResultDto result,
        IReadOnlyCollection<MatchLocation> matches)
    {
        var knockoutSeedUses = matches
            .Where(x => !x.Round.RoundType.Equals(BracketRoundTypes.GroupStage, StringComparison.OrdinalIgnoreCase))
            .SelectMany(x => x.Match.Slots
                .Where(s => NormalizeKey(s.SourceType) == BracketTemplateSourceTypes.Seed && s.SeedNumber.HasValue)
                .Select(s => new { Location = x, Seed = s.SeedNumber!.Value }))
            .GroupBy(x => x.Seed);

        foreach (var duplicate in knockoutSeedUses.Where(x => x.Count() > 1))
        {
            var first = duplicate.First().Location;
            Error(result, "KNOCKOUT_SEED_REUSED", $"Seed {duplicate.Key} xuất hiện ở nhiều trận knockout vòng đầu.", first.Round.RoundKey, first.Group.GroupKey, first.Match.MatchKey);
        }

    }

    private static void ValidateTerminalFlow(
        BracketValidationResultDto result,
        IReadOnlyCollection<MatchLocation> matches,
        IReadOnlyDictionary<string, GroupLocation> groupsByKey)
    {
        var sourceMatchKeys = matches
            .SelectMany(x => x.Match.Slots)
            .Where(x => NormalizeKey(x.SourceType) is BracketTemplateSourceTypes.WinnerMatch or BracketTemplateSourceTypes.LoserMatch)
            .Select(x => NormalizeKey(x.SourceMatchKey))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceGroupKeys = matches
            .SelectMany(x => x.Match.Slots)
            .Where(x => NormalizeKey(x.SourceType) == BracketTemplateSourceTypes.GroupRank)
            .Select(x => NormalizeKey(x.SourceGroupKey))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var winnerSourceMatchKeys = matches
            .SelectMany(x => x.Match.Slots)
            .Where(x => NormalizeKey(x.SourceType) == BracketTemplateSourceTypes.WinnerMatch)
            .Select(x => NormalizeKey(x.SourceMatchKey))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var match in matches.Where(x => !x.Match.IsTerminal))
        {
            if (HasByeSlot(match.Match)
                && !winnerSourceMatchKeys.Contains(NormalizeKey(match.Match.MatchKey)))
            {
                Error(result, "BYE_TARGET_REQUIRED",
                    "Suất BYE phải được nối bằng đầu ra đi tiếp tới một trận ở phía sau.",
                    match.Round.RoundKey, match.Group.GroupKey, match.Match.MatchKey);
            }
            if (!sourceMatchKeys.Contains(NormalizeKey(match.Match.MatchKey))
                && !sourceGroupKeys.Contains(NormalizeKey(match.Group.GroupKey)))
            {
                Warning(result, "MATCH_ORPHAN", "Trận không dẫn tới trận sau hoặc kết quả hạng bảng.", match.Round.RoundKey, match.Group.GroupKey, match.Match.MatchKey);
            }
        }

        var winnerTargets = matches
            .SelectMany(x => x.Match.Slots)
            .Where(x => NormalizeKey(x.SourceType) == BracketTemplateSourceTypes.WinnerMatch && !string.IsNullOrWhiteSpace(x.SourceMatchKey))
            .GroupBy(x => NormalizeKey(x.SourceMatchKey), StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var item in winnerTargets.Where(x => x.Count() > 1))
        {
            var source = matches.FirstOrDefault(x => NormalizeKey(x.Match.MatchKey) == item.Key);
            if (source != null && HasByeSlot(source.Match))
            {
                Error(result, "BYE_MULTIPLE_TARGETS",
                    $"Suất BYE '{item.Key}' chỉ được nối tới một vị trí đích.",
                    source.Round.RoundKey, source.Group.GroupKey, source.Match.MatchKey);
            }
            else
            {
                Warning(result, "WINNER_MULTIPLE_TARGETS", $"Winner của trận '{item.Key}' đang đi vào nhiều trận đích.");
            }
        }

        var championTerminals = matches
            .Where(x => x.Match.IsTerminal
                        && NormalizeKey(x.Match.TerminalType) == "CHAMPION")
            .ToList();
        if (championTerminals.Count == 0)
            Error(result, "TERMINAL_MATCH_MISSING", "Template phải có đúng một trận terminal xác định nhà vô địch.");
        if (championTerminals.Count > 1)
            Error(result, "MULTIPLE_CHAMPION_FINALS", "Template có nhiều hơn một trận terminal xác định nhà vô địch.");

        var knockoutRounds = matches
            .Where(x => NormalizeKey(x.Round.RoundType) is BracketRoundTypes.Knockout or BracketRoundTypes.Final
                        && NormalizeKey(x.Match.TerminalType) != "THIRD_PLACE")
            .GroupBy(x => NormalizeKey(x.Round.RoundKey))
            .Select(x => new
            {
                Round = x.First().Round,
                MatchCount = x.Count()
            })
            .OrderBy(x => x.Round.SortOrder)
            .ToList();
        for (var index = 1; index < knockoutRounds.Count; index++)
        {
            var expected = Math.Max(1, (knockoutRounds[index - 1].MatchCount + 1) / 2);
            if (knockoutRounds[index].MatchCount != expected)
            {
                Warning(result, "BRACKET_UNBALANCED",
                    $"Nhánh knockout mất cân đối: vòng '{knockoutRounds[index].Round.RoundKey}' có {knockoutRounds[index].MatchCount} trận, dự kiến {expected}.",
                    knockoutRounds[index].Round.RoundKey);
            }
        }
    }

    private static bool ComesBefore(MatchLocation source, MatchLocation target)
    {
        if (source.Round.SortOrder != target.Round.SortOrder)
            return source.Round.SortOrder < target.Round.SortOrder;
        if (source.Group.SortOrder != target.Group.SortOrder)
            return source.Group.SortOrder < target.Group.SortOrder;
        return source.Match.SortOrder < target.Match.SortOrder;
    }

    private static bool IsSameResolvableSource(BracketTemplateSlotDto first, BracketTemplateSlotDto second)
    {
        var firstType = NormalizeKey(first.SourceType);
        var secondType = NormalizeKey(second.SourceType);
        if (firstType != secondType)
            return false;

        return firstType switch
        {
            BracketTemplateSourceTypes.Seed =>
                first.SeedNumber.HasValue && first.SeedNumber == second.SeedNumber,
            BracketTemplateSourceTypes.WinnerMatch or BracketTemplateSourceTypes.LoserMatch =>
                !string.IsNullOrWhiteSpace(first.SourceMatchKey)
                && NormalizeKey(first.SourceMatchKey) == NormalizeKey(second.SourceMatchKey),
            BracketTemplateSourceTypes.GroupRank =>
                !string.IsNullOrWhiteSpace(first.SourceGroupKey)
                && NormalizeKey(first.SourceGroupKey) == NormalizeKey(second.SourceGroupKey)
                && first.SourceRank.HasValue
                && first.SourceRank == second.SourceRank,
            _ => false
        };
    }

    private static bool HasByeSlot(BracketTemplateMatchDto match) =>
        match.Slots.Count(x => NormalizeKey(x.SourceType) == BracketTemplateSourceTypes.Bye) == 1;

    private static string NormalizeKey(string? value) => (value ?? "").Trim().ToUpperInvariant();

    private static void Error(BracketValidationResultDto result, string code, string message,
        string? roundKey = null, string? groupKey = null, string? matchKey = null, byte? slotNumber = null) =>
        Add(result, "ERROR", code, message, roundKey, groupKey, matchKey, slotNumber);

    private static void Warning(BracketValidationResultDto result, string code, string message,
        string? roundKey = null, string? groupKey = null, string? matchKey = null, byte? slotNumber = null) =>
        Add(result, "WARNING", code, message, roundKey, groupKey, matchKey, slotNumber);

    private static void Info(BracketValidationResultDto result, string code, string message) =>
        Add(result, "INFO", code, message, null, null, null, null);

    private static void Add(BracketValidationResultDto result, string severity, string code, string message,
        string? roundKey, string? groupKey, string? matchKey, byte? slotNumber)
    {
        result.Issues.Add(new BracketValidationIssueDto
        {
            Severity = severity,
            Code = code,
            Message = message,
            RoundKey = roundKey,
            GroupKey = groupKey,
            MatchKey = matchKey,
            SlotNumber = slotNumber
        });
    }

    private sealed record GroupLocation(BracketTemplateRoundDto Round, BracketTemplateGroupDto Group);
    private sealed record MatchLocation(BracketTemplateRoundDto Round, BracketTemplateGroupDto Group, BracketTemplateMatchDto Match);
}
