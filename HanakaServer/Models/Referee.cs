using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class Referee
{
    public long RefereeId { get; set; }

    public string? ExternalId { get; set; }

    public string FullName { get; set; } = null!;

    public string? City { get; set; }

    public bool Verified { get; set; }

    public decimal LevelSingle { get; set; }

    public decimal LevelDouble { get; set; }

    public string? AvatarUrl { get; set; }

    public string RefereeType { get; set; } = null!;

    public string? Introduction { get; set; }

    public string? WorkingArea { get; set; }

    public string? Achievements { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}