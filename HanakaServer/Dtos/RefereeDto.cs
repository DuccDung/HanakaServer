namespace HanakaServer.Dtos.Referees
{
    public class RefereePagedResponseDto
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int Total { get; set; }

        public List<RefereeListItemDto> Items { get; set; } = new();
    }

    public class RefereeRatingHistoryItemDto
    {
        public long RatingHistoryId { get; set; }
        public decimal? RatingSingle { get; set; }
        public decimal? RatingDouble { get; set; }
        public DateTime RatedAt { get; set; }

        public string? Note { get; set; }
        public long? RatedByUserId { get; set; }
        public string? RatedByName { get; set; }
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
        public long UserId { get; set; }
        public string? ExternalId { get; set; }

        public string FullName { get; set; } = string.Empty;
        public string? City { get; set; }
        public string? Gender { get; set; }

        // Verified của referee
        public bool Verified { get; set; }

        // Verified của user
        public bool UserVerified { get; set; }

        public decimal LevelSingle { get; set; }
        public decimal LevelDouble { get; set; }
        public DateTime? RatingUpdatedAt { get; set; }

        public string? AvatarUrl { get; set; }
        public string RefereeType { get; set; } = "REFEREE";

        // dữ liệu riêng của referee
        public string? Introduction { get; set; }
        public string? WorkingArea { get; set; }
        public string? Achievements { get; set; }

        // dữ liệu đồng bộ từ user
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Bio { get; set; }
        public DateTime? BirthOfDate { get; set; }

        public List<RefereeRatingHistoryItemDto> RatingHistory { get; set; } = new();
        public List<RefereeUserAchievementItemDto> UserAchievements { get; set; } = new();
    }

    public class RefereeListItemDto
    {
        public long RefereeId { get; set; }
        public long UserId { get; set; }
        public string? ExternalId { get; set; }

        public string FullName { get; set; } = string.Empty;
        public string? City { get; set; }
        public string? Gender { get; set; }

        // Verified của hồ sơ trọng tài
        public bool Verified { get; set; }

        // Verified của user
        public bool UserVerified { get; set; }

        public decimal LevelSingle { get; set; }
        public decimal LevelDouble { get; set; }
        public DateTime? RatingUpdatedAt { get; set; }

        public string? AvatarUrl { get; set; }
        public string RefereeType { get; set; } = "REFEREE";

        public bool IsMine { get; set; }
    }
    public class RefereeAchievementTournamentDto
    {
        public long TournamentId { get; set; }
        public string? Title { get; set; }
        public string? BannerUrl { get; set; }
        public DateTime? StartTime { get; set; }
        public string? LocationText { get; set; }
        public string? AreaText { get; set; }
        public string? GameType { get; set; }
        public string GenderCategory { get; set; } = "OPEN";
        public string TournamentTypeCode { get; set; } = "DOUBLE_OPEN";
        public string TournamentTypeLabel { get; set; } = "";
        public string? Status { get; set; }
    }
    public class RefereeUserAchievementItemDto
    {
        public long UserAchievementId { get; set; }
        public long UserId { get; set; }
        public long TournamentId { get; set; }

        public string? AchievementType { get; set; }
        public string? AchievementLabel { get; set; }
        public int Rank { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime AchievedAt { get; set; }

        public string? Note { get; set; }

        public RefereeAchievementTournamentDto? Tournament { get; set; }

        public string? Title { get; set; }
        public string? TournamentName { get; set; }
        public string? BannerUrl { get; set; }
        public DateTime? Date { get; set; }
    }
    public class CreateRefereeFromMeRequest
    {
        public string? RefereeType { get; set; }
    }
}
