using System;

namespace HanakaServer.Models
{
    public partial class TournamentGroupMatch
    {
        public long MatchId { get; set; }

        public long TournamentRoundGroupId { get; set; }
        public long TournamentId { get; set; }

        public long Team1RegistrationId { get; set; }
        public long Team2RegistrationId { get; set; }

        public DateTime? StartAt { get; set; }
        public string? AddressText { get; set; }
        public string? CourtText { get; set; }

        public int ScoreTeam1 { get; set; }
        public int ScoreTeam2 { get; set; }

        public bool IsCompleted { get; set; }
        public long? WinnerRegistrationId { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // computed columns (persisted)
        public long TeamMin { get; set; }
        public long TeamMax { get; set; }

        // navigation (optional)
        public virtual TournamentRoundGroup TournamentRoundGroup { get; set; } = null!;
        public virtual Tournament Tournament { get; set; } = null!;
        public virtual TournamentRegistration Team1Registration { get; set; } = null!;
        public virtual TournamentRegistration Team2Registration { get; set; } = null!;
        public virtual TournamentRegistration? WinnerRegistration { get; set; }
    }
}