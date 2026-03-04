namespace HanakaServer.Dtos
{
    public class MemberListItemDto
    {
        public long UserId { get; set; }
        public string FullName { get; set; } = "";
        public string? City { get; set; }
        public string? Gender { get; set; }
        public bool Verified { get; set; }
        public decimal? RatingSingle { get; set; }
        public decimal? RatingDouble { get; set; }
        public string? AvatarUrl { get; set; }
    }
}