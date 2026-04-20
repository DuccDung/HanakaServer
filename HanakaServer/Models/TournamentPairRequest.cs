using System;

namespace HanakaServer.Models;

public partial class TournamentPairRequest
{
    public long PairRequestId { get; set; }

    public long TournamentId { get; set; }

    public long RequestedByUserId { get; set; }

    public long RequestedToUserId { get; set; }

    public long? RegistrationId { get; set; }

    public string Status { get; set; } = null!;

    public DateTime RequestedAt { get; set; }

    public DateTime? RespondedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public string? ResponseNote { get; set; }

    public virtual Tournament Tournament { get; set; } = null!;

    public virtual User RequestedByUser { get; set; } = null!;

    public virtual User RequestedToUser { get; set; } = null!;

    public virtual TournamentRegistration? Registration { get; set; }
}
