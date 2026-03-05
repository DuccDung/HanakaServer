using System;
using System.Collections.Generic;

namespace HanakaServer.Models
{
    public partial class TournamentRoundMap
    {
        public long TournamentRoundMapId { get; set; }
        public long TournamentId { get; set; }

        public string RoundKey { get; set; } = null!;
        public string RoundLabel { get; set; } = null!;

        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }

        // navigation (optional)
        public virtual Tournament Tournament { get; set; } = null!;
        public virtual ICollection<TournamentRoundGroup> TournamentRoundGroups { get; set; } = new List<TournamentRoundGroup>();
    }
}