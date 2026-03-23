using System;

namespace HanakaServer.Models
{
    public partial class UserAchievement
    {
        public long UserAchievementId { get; set; }

        public long UserId { get; set; }

        public long TournamentId { get; set; }

        public string AchievementType { get; set; } = null!;

        public DateTime CreatedAt { get; set; }

        public string? Note { get; set; }

        // Navigation
        public virtual User User { get; set; } = null!;

        public virtual Tournament Tournament { get; set; } = null!;
    }
}