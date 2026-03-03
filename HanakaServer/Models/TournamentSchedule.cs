using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class TournamentSchedule
{
    public long ScheduleId { get; set; }

    public long TournamentId { get; set; }

    public string? ExternalId { get; set; }

    public string RoundKey { get; set; } = null!;

    public string Code { get; set; } = null!;

    public string? TimeText { get; set; }

    public string? CourtText { get; set; }

    public int? LeftIndex { get; set; }

    public int? RightIndex { get; set; }

    public string? TeamA { get; set; }

    public string? TeamB { get; set; }

    public int ScoreA { get; set; }

    public int ScoreB { get; set; }

    public virtual TournamentRound RoundKeyNavigation { get; set; } = null!;

    public virtual Tournament Tournament { get; set; } = null!;
}
