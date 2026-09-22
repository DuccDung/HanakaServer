using System;

namespace HanakaServer.Dtos
{
    public class PublicPlayerDto
    {
        public long? UserId { get; set; }        // null for guests or a relay team summary
        public bool IsGuest { get; set; }        // relay team summaries are not guest athletes
        public bool Verified { get; set; }       // account state, or display-only true for a relay team summary
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
        public bool Paid { get; set; }
        public DateTime? PaidAt { get; set; }
        public decimal? PaymentAmount { get; set; }

        public PublicPlayerDto Player1 { get; set; } = new();
        public PublicPlayerDto? Player2 { get; set; }  // waiting => null

        public bool IsRelay { get; set; }
        public string? TeamName { get; set; }
        public long? CaptainUserId { get; set; }
        public int TeamSize { get; set; }
        public bool IsReady { get; set; }
        // Transitional field retained for older clients; no roster lock is enforced.
        public bool LineupLocked { get; set; }
        public PublicRelayRegistrationMemberDto[] Members { get; set; } = Array.Empty<PublicRelayRegistrationMemberDto>();
        public PublicRelayReserveMemberDto[] ReserveMembers { get; set; } = [];
    }

    public class PublicRelayReserveMemberDto
    {
        public int Position { get; set; }
        public long? UserId { get; set; }
        public string Name { get; set; } = "";
        public string? Avatar { get; set; }
        public decimal Level { get; set; }
        public bool Verified { get; set; }
    }

    public class PublicRelayRegistrationMemberDto
    {
        public int Position { get; set; }
        public int PairNumber { get; set; }
        public long? UserId { get; set; }
        public string Name { get; set; } = "";
        public string? Avatar { get; set; }
        public decimal Level { get; set; }
        public bool Verified { get; set; }
        public bool IsCaptain { get; set; }
    }

    public class PublicRegistrationCountsDto
    {
        public int Success { get; set; }        // số bản ghi success (DOUBLE => số đội)
        public int Waiting { get; set; }        // số bản ghi waiting
        public int Paid { get; set; }           // số đội/bản ghi đã thanh toán
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
