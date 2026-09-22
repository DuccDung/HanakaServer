namespace HanakaServer.Models;

// Three independently editable scores. No leg clock, progression or target-score rule.
public sealed class RelayMatchScore
{
    public long MatchId { get; set; }
    public int Part1Team1 { get; set; }
    public int Part1Team2 { get; set; }
    public int Part2Team1 { get; set; }
    public int Part2Team2 { get; set; }
    public int Part3Team1 { get; set; }
    public int Part3Team2 { get; set; }
    public long Version { get; set; }
}
