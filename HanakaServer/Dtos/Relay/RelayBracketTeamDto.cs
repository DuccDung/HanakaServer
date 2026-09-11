namespace HanakaServer.Dtos.Relay;

// Public roster fields only; no captain management rights/contact information in bracket snapshots.
public sealed record RelayBracketMemberDto(int Position, long? UserId, string DisplayName, string? AvatarUrl)
{
    public int PairNumber => (Position + 1) / 2;
}

public sealed record RelayBracketTeamDto(int TeamSize, long LineupVersion, DateTimeOffset? LineupLockedAtUtc,
    IReadOnlyList<RelayBracketMemberDto> Members);
