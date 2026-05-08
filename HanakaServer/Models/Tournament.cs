using System;
using System.Collections.Generic;

namespace HanakaServer.Models;

public partial class Tournament
{
    public long TournamentId { get; set; }

    public string? ExternalId { get; set; }

    public string Status { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? BannerUrl { get; set; }

    public string? StartTimeRaw { get; set; }

    public DateTime? StartTime { get; set; }

    public string? RegisterDeadlineRaw { get; set; }

    public DateTime? RegisterDeadline { get; set; }

    public string? FormatText { get; set; }

    public string? PlayoffType { get; set; }

    public string? GameType { get; set; }

    public string GenderCategory { get; set; } = null!;

    public decimal SingleLimit { get; set; }

    public decimal DoubleLimit { get; set; }

    public string? LocationText { get; set; }

    public string? AreaText { get; set; }

    public int ExpectedTeams { get; set; }

    public int MatchesCount { get; set; }

    public string? StatusText { get; set; }

    public string? StateText { get; set; }

    public string? Organizer { get; set; }

    public string? CreatorName { get; set; }

    public int RegisteredCount { get; set; }

    public int PairedCount { get; set; }

    public bool Remove { get; set; }

    public string? ZaloLink { get; set; }

    public string? Content { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? TournamentRule { get; set; }

    public virtual ICollection<TournamentRegistration> TournamentRegistrations { get; set; } = new List<TournamentRegistration>();
    public virtual ICollection<UserAchievement> UserAchievements { get; set; } = new List<UserAchievement>();
    public virtual ICollection<TournamentPrize> TournamentPrizes { get; set; } = new List<TournamentPrize>();
    public virtual ICollection<TournamentPairRequest> TournamentPairRequests { get; set; } = new List<TournamentPairRequest>();

}
