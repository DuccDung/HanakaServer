using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class TournamentStandingRow
{
    public long StandingRowId { get; set; }

    public long StandingGroupId { get; set; }

    public string TeamText { get; set; } = null!;

    public int Win { get; set; }

    public int Point { get; set; }

    public int Hso { get; set; }

    public int Rank { get; set; }

    public bool IsTop { get; set; }

    public virtual TournamentStandingGroup StandingGroup { get; set; } = null!;
}
