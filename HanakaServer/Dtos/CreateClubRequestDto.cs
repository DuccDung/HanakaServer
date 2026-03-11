namespace HanakaServer.Dtos
{
    public class CreateClubRequestDto
    {
        public string ClubName { get; set; } = string.Empty;

        // Tạm gom khu vực + địa chỉ vào đây vì model hiện chỉ có AreaText
        public string? Province { get; set; }
        public string? District { get; set; }
        public string? Address { get; set; }

        // Ảnh cover của CLB
        public string? CoverUrl { get; set; }

        // Các field UI hiện có nhưng DB chưa có cột
        public string? Description { get; set; }
        public string? FoundedDate { get; set; }
        public string? PlayTime { get; set; }
        public string? AvatarUrl { get; set; }
    }
}