namespace HanakaServer.ViewModels.Admin
{
    public class CoachUpsertRequest
    {
        public string? ExternalId { get; set; }
        public string FullName { get; set; } = "";
        public string? City { get; set; }
        public bool Verified { get; set; }
        public decimal LevelSingle { get; set; }
        public decimal LevelDouble { get; set; }
        public string? AvatarUrl { get; set; }
        public string? CoachType { get; set; }
        public string? Introduction { get; set; }
        public string? TeachingArea { get; set; }
        public string? Achievements { get; set; }
    }
}