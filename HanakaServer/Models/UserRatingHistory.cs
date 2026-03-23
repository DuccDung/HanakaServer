using System;

namespace HanakaServer.Models
{
    public partial class UserRatingHistory
    {
        public long RatingHistoryId { get; set; }

        public long UserId { get; set; }
        public long? TournamentId { get; set; }
        public decimal? RatingSingle { get; set; }

        public decimal? RatingDouble { get; set; }

        public long? RatedByUserId { get; set; }

        public string? Note { get; set; }

        public DateTime RatedAt { get; set; }

        // Navigation
        public virtual User User { get; set; } = null!;

        public virtual User? RatedByUser { get; set; }
        public virtual Tournament? Tournament { get; set; }
    }
}