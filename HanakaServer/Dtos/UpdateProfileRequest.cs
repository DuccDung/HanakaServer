namespace HanakaServer.Dtos
{
    public class UpdateProfileRequest
    {
        public string? FullName { get; set; }
        public string? Phone { get; set; }
        public string? Gender { get; set; }   // "Nam" / "Nữ" / "Khác"
        public string? City { get; set; }     // Tỉnh/Thành
        public string? Bio { get; set; }
        public DateTime? BirthOfDate { get; set; }  // yyyy-MM-dd
        public string? AvatarUrl { get; set; }      // nếu bạn update bằng url
    }
    public class UpdateSelfRatingRequestDto
    {
        public decimal RatingSingle { get; set; }
        public decimal RatingDouble { get; set; }
    }
}