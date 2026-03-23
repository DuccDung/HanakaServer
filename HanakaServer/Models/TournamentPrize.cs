using System;

namespace HanakaServer.Models
{
    public partial class TournamentPrize
    {
        public long TournamentPrizeId { get; set; }

        public long TournamentId { get; set; }

        public long? RegistrationId { get; set; }

        public string PrizeType { get; set; } = null!; // FIRST / SECOND / THIRD

        public int PrizeOrder { get; set; }

        public bool IsConfirmed { get; set; }

        public string? Note { get; set; }

        public DateTime CreatedAt { get; set; }

        public virtual Tournament Tournament { get; set; } = null!;

        public virtual TournamentRegistration? Registration { get; set; }
    }
}