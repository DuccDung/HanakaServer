using System;

namespace HanakaServer.Dtos
{
    public class PublicPlayerDto
    {
        public long? UserId { get; set; }        // null => guest
        public bool IsGuest { get; set; }        // true => guest
        public bool Verified { get; set; }       // nếu user thì lấy từ Users; nếu guest thì false
        public string Name { get; set; } = "";
        public string? Avatar { get; set; }
        public decimal Level { get; set; }
    }

    public class PublicRegistrationItemDto
    {
        public long RegistrationId { get; set; }
        public int RegIndex { get; set; }
        public string RegCode { get; set; } = "";
        public DateTime? RegTime { get; set; }

        public decimal Points { get; set; }

        public bool WaitingPair { get; set; }
        public bool Success { get; set; }

        public PublicPlayerDto Player1 { get; set; } = new();
        public PublicPlayerDto? Player2 { get; set; }  // waiting => null
    }

    public class PublicRegistrationCountsDto
    {
        public int Success { get; set; }        // số bản ghi success (DOUBLE => số đội)
        public int Waiting { get; set; }        // số bản ghi waiting
        public int CapacityLeft { get; set; }   // còn chỗ (theo ExpectedTeams - success)
    }

    public class PublicTournamentRegistrationsResponseDto
    {
        public object Tournament { get; set; } = default!; // trả vài field cơ bản như admin
        public PublicRegistrationCountsDto Counts { get; set; } = new();

        public PublicRegistrationItemDto[] SuccessItems { get; set; } = Array.Empty<PublicRegistrationItemDto>();
        public PublicRegistrationItemDto[] WaitingItems { get; set; } = Array.Empty<PublicRegistrationItemDto>();
    }
}