namespace HanakaServer.Dtos.Referees
{
    public class RefereePagedResponseDto
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int Total { get; set; }
        public List<RefereeListItemDto> Items { get; set; } = new();
    }

    public class CreateRefereeFromMeRequest
    {
        public string? RefereeType { get; set; } = "REFEREE";
    }

    public class UpdateMyRefereeProfileRequest
    {
        public string? Introduction { get; set; }
        public string? WorkingArea { get; set; }
        public string? Achievements { get; set; }
    }

    public class RefereeDetailDto
    {
        public long RefereeId { get; set; }
        public string? ExternalId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? City { get; set; }
        public bool Verified { get; set; }
        public decimal LevelSingle { get; set; }
        public decimal LevelDouble { get; set; }
        public string? AvatarUrl { get; set; }
        public string RefereeType { get; set; } = "REFEREE";
        public string? Introduction { get; set; }
        public string? WorkingArea { get; set; }
        public string? Achievements { get; set; }
    }

    public class RefereeListItemDto
    {
        public long RefereeId { get; set; }
        public string? ExternalId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? City { get; set; }
        public bool Verified { get; set; }
        public decimal LevelSingle { get; set; }
        public decimal LevelDouble { get; set; }
        public string? AvatarUrl { get; set; }
        public string RefereeType { get; set; } = "REFEREE";
        public bool IsMine { get; set; }
    }
}