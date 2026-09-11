using System.ComponentModel.DataAnnotations;
using HanakaServer.Services.Relay;

namespace HanakaServer.Dtos.Relay;

public sealed class SaveRelaySettingsRequest
{
    public int TeamSize { get; set; } = 6;
    [Range(1, int.MaxValue)] public int TargetScore { get; set; } = 40;
    [Range(0, long.MaxValue)] public long ExpectedVersion { get; set; }
}

public sealed class SetRelayEnabledRequest
{
    [Range(0, long.MaxValue)] public long ExpectedVersion { get; set; }
}

public sealed class SaveRelayTeamRequest
{
    [Required, StringLength(150)] public string TeamName { get; set; } = "";
    public long? CaptainUserId { get; set; }
    [Required] public List<RelayMemberInput> Members { get; set; } = [];
    [Range(0, long.MaxValue)] public long ExpectedVersion { get; set; }
}

public sealed record RelaySettingsDto(long TournamentId, int TeamSize, int PairCount, int TargetScore,
    int LegDurationSeconds, bool IsEnabled, long Version);
public sealed record RelayTeamDto(long RegistrationId, string TeamName, long? CaptainUserId,
    DateTimeOffset? LineupLockedAtUtc, bool IsReady, long Version, IReadOnlyList<RelayMemberInput> Members);
