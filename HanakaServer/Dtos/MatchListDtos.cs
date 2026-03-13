namespace HanakaServer.Dtos
{
    public class MatchListQueryDto
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;

        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }

        public string? Query { get; set; }

        public long? TournamentId { get; set; }
        public bool? IsCompleted { get; set; }
    }

    public class MatchPlayerDto
    {
        public string Name { get; set; } = "";
    }

    public class MatchListItemDto
    {
        public long MatchId { get; set; }
        public long TournamentId { get; set; }
        public string TournamentTitle { get; set; } = "";
        public string RoundLabel { get; set; } = "";
        public string GroupName { get; set; } = "";

        public string GameType { get; set; } = "DOUBLE";

        public long Team1RegistrationId { get; set; }
        public long Team2RegistrationId { get; set; }

        public List<MatchPlayerDto> Team1Players { get; set; } = new();
        public List<MatchPlayerDto> Team2Players { get; set; } = new();

        public string Team1Text { get; set; } = "";
        public string Team2Text { get; set; } = "";

        public DateTime? StartAt { get; set; }
        public string? AddressText { get; set; }
        public string? CourtText { get; set; }

        public int ScoreTeam1 { get; set; }
        public int ScoreTeam2 { get; set; }

        public bool IsCompleted { get; set; }
        public long? WinnerRegistrationId { get; set; }
        public string? WinnerTeam { get; set; }

        public string? VideoUrl { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class PagedMatchListResponseDto
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages { get; set; }
        public bool HasNextPage { get; set; }
        public bool HasPreviousPage { get; set; }

        public List<MatchListItemDto> Items { get; set; } = new();
    }
}