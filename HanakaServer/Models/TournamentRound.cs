using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class TournamentRound
{
    public string RoundKey { get; set; } = null!;

    public string RoundLabel { get; set; } = null!;

    public virtual ICollection<TournamentSchedule> TournamentSchedules { get; set; } = new List<TournamentSchedule>();

    public virtual ICollection<TournamentStandingGroup> TournamentStandingGroups { get; set; } = new List<TournamentStandingGroup>();
}
