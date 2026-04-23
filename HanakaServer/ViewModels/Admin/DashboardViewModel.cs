using System.Collections.Generic;

namespace HanakaServer.ViewModels.Admin
{
    public class DashboardViewModel
    {
        public int TotalUsers { get; set; }
        public int InactiveUsers { get; set; }
        public int VerifiedUsers { get; set; }
        public int UnverifiedUsers { get; set; }

        public int TotalTournaments { get; set; }
        public int ActiveTournaments { get; set; }
        public int CompletedTournaments { get; set; }
        public int RemovedTournaments { get; set; }

        public int TotalRegistrations { get; set; }
        public int SuccessfulRegistrations { get; set; }
        public int PaidRegistrations { get; set; }
        public int WaitingPairRegistrations { get; set; }

        public int TotalMatches { get; set; }
        public int CompletedMatches { get; set; }
        public int UpcomingMatches { get; set; }

        public int TotalRoundMaps { get; set; }
        public int TotalRoundGroups { get; set; }

        public int TotalBanners { get; set; }
        public int ActiveBanners { get; set; }

        public int TotalClubs { get; set; }
        public int ActiveClubs { get; set; }

        public int TotalCoaches { get; set; }
        public int VerifiedCoaches { get; set; }

        public int TotalReferees { get; set; }
        public int VerifiedReferees { get; set; }

        public int TotalCourts { get; set; }
        public int TotalLinks { get; set; }

        public List<RoleStatItem> RoleStats { get; set; } = new();
        public List<RecentTournamentItem> RecentTournaments { get; set; } = new();
    }

    public class RoleStatItem
    {
        public int RoleId { get; set; }
        public string RoleName { get; set; } = "";
        public int UserCount { get; set; }
    }

    public class RecentTournamentItem
    {
        public long TournamentId { get; set; }
        public string Title { get; set; } = "";
        public string Status { get; set; } = "";
        public int RegisteredCount { get; set; }
        public int MatchesCount { get; set; }
        public int CompletedMatchesCount { get; set; }
        public int RoundCount { get; set; }
        public int GroupCount { get; set; }
        public System.DateTime CreatedAt { get; set; }
    }
}
