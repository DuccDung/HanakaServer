using System.Collections.Generic;

namespace HanakaServer.Dtos.Coaches
{
    public class CoachPagedResponseDto
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int Total { get; set; }
        public List<CoachListItemDto> Items { get; set; } = new();
    }

    public class CreateCoachFromMeRequest
    {
        public string? CoachType { get; set; } = "COACH";
    }
    public class UpdateMyCoachProfileRequest
    {
        public string? Introduction { get; set; }
        public string? TeachingArea { get; set; }
        public string? Achievements { get; set; }
    }
    public class CoachDetailDto
    {
        public long CoachId { get; set; }
        public string? ExternalId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? City { get; set; }
        public bool Verified { get; set; }
        public decimal LevelSingle { get; set; }
        public decimal LevelDouble { get; set; }
        public string? AvatarUrl { get; set; }
        public string CoachType { get; set; } = "COACH";

        public string? Introduction { get; set; }
        public string? TeachingArea { get; set; }
        public string? Achievements { get; set; }
    }
    public class CoachListItemDto
    {
        public long CoachId { get; set; }
        public string? ExternalId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? City { get; set; }
        public bool Verified { get; set; }
        public decimal LevelSingle { get; set; }
        public decimal LevelDouble { get; set; }
        public string? AvatarUrl { get; set; }
        public string CoachType { get; set; } = "COACH";
        public bool IsMine { get; set; }
    }
}