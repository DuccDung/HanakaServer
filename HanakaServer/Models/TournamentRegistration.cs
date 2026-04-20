using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class TournamentRegistration
{
    public long RegistrationId { get; set; }

    public long TournamentId { get; set; }

    public string? ExternalId { get; set; }

    public int RegIndex { get; set; }

    public string RegCode { get; set; } = null!;

    public string? RegTimeRaw { get; set; }

    public DateTime? RegTime { get; set; }

    public string Player1Name { get; set; } = null!;

    public string? Player1Avatar { get; set; }

    public decimal Player1Level { get; set; }

    public bool Player1Verified { get; set; }

    public long? Player1UserId { get; set; }

    public string? Player2Name { get; set; }

    public string? Player2Avatar { get; set; }

    public decimal Player2Level { get; set; }

    public bool Player2Verified { get; set; }

    public long? Player2UserId { get; set; }

    public decimal Points { get; set; }

    public string? BtCode { get; set; }

    public bool Paid { get; set; }

    public bool WaitingPair { get; set; }

    public bool Success { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User? Player1User { get; set; }

    public virtual User? Player2User { get; set; }

    public virtual Tournament Tournament { get; set; } = null!;
    public virtual ICollection<TournamentPrize> TournamentPrizes { get; set; } = new List<TournamentPrize>();
    public virtual ICollection<TournamentPairRequest> TournamentPairRequests { get; set; } = new List<TournamentPairRequest>();
}
