namespace HanakaServer.Models.Dto
{
    public class CreateTournamentGroupMatchRequest
    {
        public long Team1RegistrationId { get; set; }
        public long Team2RegistrationId { get; set; }
        public DateTime? StartAt { get; set; }
        public string? AddressText { get; set; }
        public string? CourtText { get; set; }
        public string? VideoUrl { get; set; }

        // NEW
        public long? RefereeUserId { get; set; }
    }

    public class UpdateTournamentGroupMatchRequest
    {
        public long Team1RegistrationId { get; set; }
        public long Team2RegistrationId { get; set; }

        public bool StartAtSet { get; set; }
        public DateTime? StartAt { get; set; }

        public string? AddressText { get; set; }
        public string? CourtText { get; set; }
        public string? VideoUrl { get; set; }

        // NEW
        public long? RefereeUserId { get; set; }
    }

    public class UpdateMatchScoreRequest
    {
        public int ScoreTeam1 { get; set; }
        public int ScoreTeam2 { get; set; }
        public bool IsCompleted { get; set; }
    }
}