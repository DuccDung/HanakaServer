using System.ComponentModel.DataAnnotations;

namespace HanakaServer.Dtos.Relay;

public sealed class CreateRelayUserRegistrationRequest
{
    [Required, StringLength(150, MinimumLength = 1)]
    public string TeamName { get; set; } = "";

    [Required]
    public List<RelayUserRegistrationMemberRequest> Members { get; set; } = [];

    public List<RelayUserReserveMemberRequest>? ReserveMembers { get; set; }
}

public sealed class RelayUserReserveMemberRequest
{
    [Range(1, 4)] public int Position { get; set; }
    [Range(1, long.MaxValue)] public long UserId { get; set; }
}

public sealed class RelayUserRegistrationMemberRequest
{
    [Range(2, 8)]
    public int Position { get; set; }

    [Range(1, long.MaxValue)]
    public long UserId { get; set; }
}
