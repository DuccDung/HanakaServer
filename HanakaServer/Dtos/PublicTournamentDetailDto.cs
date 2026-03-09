namespace HanakaServer.Dtos
{
    public class PublicTournamentDetailDto
    {
        public long TournamentId { get; set; }
        public string? ExternalId { get; set; }

        public string Status { get; set; } = "";
        public string Title { get; set; } = "";

        public string? BannerUrl { get; set; }

        public string? StartTimeRaw { get; set; }
        public DateTime? StartTime { get; set; }

        public string? RegisterDeadlineRaw { get; set; }
        public DateTime? RegisterDeadline { get; set; }

        public string? FormatText { get; set; }
        public string? PlayoffType { get; set; }
        public string? GameType { get; set; }

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

        public int? RegisteredCount { get; set; }
        public int? PairedCount { get; set; }

        public string? Content { get; set; }

        public DateTime CreatedAt { get; set; }

    }
}