using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class Club
{
    public long ClubId { get; set; }

    public string? ExternalId { get; set; }

    public string ClubName { get; set; } = null!;

    public string? AreaText { get; set; }

    public string? CoverUrl { get; set; }

    public decimal RatingAvg { get; set; }

    public int ReviewsCount { get; set; }

    public int MatchesPlayed { get; set; }

    public int MatchesWin { get; set; }

    public int MatchesDraw { get; set; }

    public int MatchesLoss { get; set; }

    public long? OwId { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public bool AllowChallenge { get; set; }

    public virtual ICollection<ClubMember> ClubMembers { get; set; } = new List<ClubMember>();

    public virtual ICollection<ClubMessage> ClubMessages { get; set; } = new List<ClubMessage>();

    public virtual User? Ow { get; set; }
}