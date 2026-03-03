using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class TournamentStandingGroup
{
    public long StandingGroupId { get; set; }

    public long TournamentId { get; set; }

    public string? ExternalId { get; set; }

    public string RoundKey { get; set; } = null!;

    public string GroupName { get; set; } = null!;

    public virtual TournamentRound RoundKeyNavigation { get; set; } = null!;

    public virtual Tournament Tournament { get; set; } = null!;

    public virtual ICollection<TournamentStandingRow> TournamentStandingRows { get; set; } = new List<TournamentStandingRow>();
}
