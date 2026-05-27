using System;

namespace HanakaServer.Models
{
    public partial class TournamentGroupMatch
    {
        public long MatchId { get; set; }

        public long TournamentRoundGroupId { get; set; }
        public long TournamentId { get; set; }

        public long? Team1RegistrationId { get; set; }
        public long? Team2RegistrationId { get; set; }

        public string Team1SourceType { get; set; } = "REGISTRATION";
        public long? Team1SourceMatchId { get; set; }
        public long? Team1SourceGroupId { get; set; }
        public int? Team1SourceRank { get; set; }

        public string Team2SourceType { get; set; } = "REGISTRATION";
        public long? Team2SourceMatchId { get; set; }
        public long? Team2SourceGroupId { get; set; }
        public int? Team2SourceRank { get; set; }

        public DateTime? StartAt { get; set; }
        public string? AddressText { get; set; }
        public string? CourtText { get; set; }

        public int ScoreTeam1 { get; set; }
        public int ScoreTeam2 { get; set; }

        public bool IsCompleted { get; set; }
        public long? WinnerRegistrationId { get; set; }

        // NEW: trọng tài
        public long? RefereeUserId { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // computed columns (persisted)
        public long? TeamMin { get; set; }
        public long? TeamMax { get; set; }

        public string? VideoUrl { get; set; }

        // navigation
        public virtual TournamentRoundGroup TournamentRoundGroup { get; set; } = null!;
        public virtual Tournament Tournament { get; set; } = null!;
        public virtual TournamentRegistration? Team1Registration { get; set; }
        public virtual TournamentRegistration? Team2Registration { get; set; }
        public virtual TournamentRegistration? WinnerRegistration { get; set; }
        public virtual TournamentGroupMatch? Team1SourceMatch { get; set; }
        public virtual TournamentGroupMatch? Team2SourceMatch { get; set; }
        public virtual TournamentRoundGroup? Team1SourceGroup { get; set; }
        public virtual TournamentRoundGroup? Team2SourceGroup { get; set; }

        public virtual User? RefereeUser { get; set; }
        public virtual ICollection<TournamentMatchScoreHistory> TournamentMatchScoreHistories { get; set; } = new List<TournamentMatchScoreHistory>();
    }
}
