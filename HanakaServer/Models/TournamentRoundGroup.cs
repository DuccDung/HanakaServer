using System;
using System.Collections.Generic;

namespace HanakaServer.Models
{
    public partial class TournamentRoundGroup
    {
        public long TournamentRoundGroupId { get; set; }
        public long TournamentRoundMapId { get; set; }

        public string GroupName { get; set; } = null!;
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }

        // navigation (optional)
        public virtual TournamentRoundMap TournamentRoundMap { get; set; } = null!;
        public virtual ICollection<TournamentGroupMatch> TournamentGroupMatches { get; set; } = new List<TournamentGroupMatch>();
    }
}