using System;

namespace HanakaServer.Models
{
    public partial class TournamentMatchScoreHistory
    {
        public long ScoreHistoryId { get; set; }

        public long MatchId { get; set; }

        // trọng tài chấm điểm
        public long RefereeUserId { get; set; }

        public int ScoreTeam1 { get; set; }
        public int ScoreTeam2 { get; set; }

        public bool IsCompleted { get; set; }

        public long? WinnerRegistrationId { get; set; }

        public string? Note { get; set; }

        public DateTime CreatedAt { get; set; }

        // navigation
        public virtual TournamentGroupMatch Match { get; set; } = null!;
        public virtual User RefereeUser { get; set; } = null!;
        public virtual TournamentRegistration? WinnerRegistration { get; set; }
    }
}