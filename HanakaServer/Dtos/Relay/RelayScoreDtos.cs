namespace HanakaServer.Dtos.Relay;

public sealed record RelayPartScoreDto(int PartNumber, int ScoreTeam1, int ScoreTeam2);
public sealed record RelayScoreDto(long Version, bool RequiresAllocation, IReadOnlyList<RelayPartScoreDto> Parts);

public sealed class RelayScoreUpdate
{
    public long ExpectedVersion { get; set; }
    public int? ChangedPart { get; set; }
    public bool AllocateExisting { get; set; }
    public List<RelayPartScoreDto> Parts { get; set; } = [];
}
