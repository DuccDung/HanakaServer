using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class Match
{
    public long MatchId { get; set; }

    public string? ExternalId { get; set; }

    public string? MatchTimeRaw { get; set; }

    public DateTime? MatchTime { get; set; }

    public string MatchType { get; set; } = null!;

    public int ScoreA { get; set; }

    public int ScoreB { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<MatchPlayer> MatchPlayers { get; set; } = new List<MatchPlayer>();
}
