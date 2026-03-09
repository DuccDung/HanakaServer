using Microsoft.AspNetCore.Http;

namespace HanakaServer.Dtos
{
    public class CreateTournamentRequest
    {
        public string Title { get; set; } = null!;
        public string GameType { get; set; } = null!; // SINGLE/DOUBLE/MIXED
        public string? Status { get; set; } // DRAFT/OPEN/CLOSED (default DRAFT)

        public int? ExpectedTeams { get; set; }

        public DateTime? StartTime { get; set; }
        public DateTime? RegisterDeadline { get; set; }

        public string? LocationText { get; set; }
        public string? AreaText { get; set; }

        public decimal? SingleLimit { get; set; }
        public decimal? DoubleLimit { get; set; }

        public string? Content { get; set; }

        // Optional (mở rộng)
        public string? FormatText { get; set; }   // Dạng thi đấu (mở rộng)
        public string? PlayoffType { get; set; }  // Loại playoff (nếu dùng)
        public string? StatusText { get; set; }
        public string? StateText { get; set; }
        public string? Organizer { get; set; }    // Đơn vị tổ chức
        public string? CreatorName { get; set; }  // Người tạo giải

        // File banner
        public IFormFile? BannerFile { get; set; }
    }

    public class TournamentListItemDto
    {
        public long TournamentId { get; set; }
        public string Title { get; set; } = null!;
        public string Status { get; set; } = null!;
        public DateTime? StartTime { get; set; }
        public DateTime? RegisterDeadline { get; set; }
        public string GameType { get; set; } = null!;
        public int ExpectedTeams { get; set; }
        public string? LocationText { get; set; }
        public string? AreaText { get; set; }
        public string? BannerUrl { get; set; }
        public DateTime CreatedAt { get; set; }
        public decimal? SingleLimit { get; set; }
        public decimal? DoubleLimit { get; set; }

        // ✅ Fields mở rộng
        public string? FormatText { get; set; }
        public string? PlayoffType { get; set; }
        public string? Organizer { get; set; }
        public string? CreatorName { get; set; }
        public string? StatusText { get; set; }
        public string? StateText { get; set; }
        public int? MatchesCount { get; set; }
        public int? RegisteredCount { get; set; }
        public int? PairedCount { get; set; }
        public string? Content { get; set; }
    }

    public class UpdateTournamentRequest
    {
        public string? Title { get; set; }
        public string? GameType { get; set; }
        public string? Status { get; set; }

        public int? ExpectedTeams { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? RegisterDeadline { get; set; }

        public string? LocationText { get; set; }
        public string? AreaText { get; set; }

        public decimal? SingleLimit { get; set; }
        public decimal? DoubleLimit { get; set; }

        public string? Content { get; set; }

        // ✅ NEW: cho edit các field mở rộng
        public string? FormatText { get; set; }
        public string? PlayoffType { get; set; }
        public string? Organizer { get; set; }
        public string? CreatorName { get; set; }

        public IFormFile? BannerFile { get; set; }
    }

    public record UserPickDto(long? UserId, string Name, decimal Level, string? Avatar);

    public class CreateRegistrationRequest
    {
        public string GameType { get; set; } = "DOUBLE"; // SINGLE/DOUBLE
        public UserPickDto Player1 { get; set; } = default!;
        public UserPickDto? Player2 { get; set; } // optional
        public bool WaitingPair { get; set; }
        public bool Paid { get; set; }
        public string? BtCode { get; set; }
    }
}