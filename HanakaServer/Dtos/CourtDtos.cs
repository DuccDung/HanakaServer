using System.Collections.Generic;

namespace HanakaServer.Dtos.Courts
{
    public class CourtListItemDto
    {
        public long CourtId { get; set; }
        public string? ExternalId { get; set; }
        public string CourtName { get; set; } = string.Empty;
        public string? AreaText { get; set; }
        public string? ManagerName { get; set; }
        public string? Phone { get; set; }
        public List<string> Images { get; set; } = new();
    }

    public class CourtPagedResponseDto
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int Total { get; set; }
        public List<CourtListItemDto> Items { get; set; } = new();
    }

    public class CourtDetailDto
    {
        public long CourtId { get; set; }
        public string? ExternalId { get; set; }
        public string CourtName { get; set; } = string.Empty;
        public string? AreaText { get; set; }
        public string? ManagerName { get; set; }
        public string? Phone { get; set; }
        public List<string> Images { get; set; } = new();
    }
}