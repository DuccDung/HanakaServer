using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class Exchange
{
    public long ExchangeId { get; set; }

    public string? ExternalId { get; set; }

    public string LeftClubName { get; set; } = null!;

    public string? LeftLogoUrl { get; set; }

    public int LeftW { get; set; }

    public int LeftL { get; set; }

    public int LeftD { get; set; }

    public string RightClubName { get; set; } = null!;

    public string? RightLogoUrl { get; set; }

    public string? ScoreText { get; set; }

    public string? TimeTextRaw { get; set; }

    public DateTime? MatchTime { get; set; }

    public string? AgoText { get; set; }

    public string? LocationText { get; set; }

    public DateTime CreatedAt { get; set; }
}
