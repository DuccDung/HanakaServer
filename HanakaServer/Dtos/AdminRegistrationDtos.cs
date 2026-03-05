using Microsoft.AspNetCore.Http;

namespace HanakaServer.Dtos
{
    public class CreateRegistrationForm
    {
        public string? GameType { get; set; } = "DOUBLE"; // SINGLE/DOUBLE
        public bool WaitingPair { get; set; }
        public bool Paid { get; set; }
        public string? BtCode { get; set; }

        // Player1: either UserId OR Guest fields
        public long? Player1UserId { get; set; }
        public string? Player1Name { get; set; }      // guest
        public decimal? Player1Level { get; set; }    // guest
        public IFormFile? Player1AvatarFile { get; set; }

        // Player2: either UserId OR Guest fields (only for DOUBLE + not waiting)
        public long? Player2UserId { get; set; }
        public string? Player2Name { get; set; }      // guest
        public decimal? Player2Level { get; set; }    // guest
        public IFormFile? Player2AvatarFile { get; set; }
    }
 
    public class RegistrationItemDto
    {
        public long RegistrationId { get; set; }
        public int RegIndex { get; set; }
        public string RegCode { get; set; }
        public DateTime RegTime { get; set; }

        public string Player1Name { get; set; }
        public string Player1Avatar { get; set; }
        public bool Player1Verified { get; set; }

        public decimal? Player1LevelSingle { get; set; }
        public decimal? Player1LevelDouble { get; set; }

        public string Player2Name { get; set; }
        public string Player2Avatar { get; set; }
        public bool Player2Verified { get; set; }

        public decimal? Player2LevelSingle { get; set; }
        public decimal? Player2LevelDouble { get; set; }

        public decimal Points { get; set; }
        public string BtCode { get; set; }
        public bool Paid { get; set; }
        public bool WaitingPair { get; set; }
    }

    public class UpdateRegistrationDto
    {
        public bool? Paid { get; set; }
        public string? BtCode { get; set; }
    }

    public class PairWaitingDto
    {
        public long WithWaitingRegistrationId { get; set; }
    }

    // Response DTO tránh serialize loop
    public class RegistrationAdminItemDto
    {
        public long RegistrationId { get; set; }
        public long TournamentId { get; set; }

        public int RegIndex { get; set; }
        public string RegCode { get; set; } = "";
        public DateTime? RegTime { get; set; }

        public string Player1Name { get; set; } = "";
        public string? Player1Avatar { get; set; }
        public decimal Player1Level { get; set; }          // Picked level (the one used to calc points)
        public bool Player1Verified { get; set; }
        public long? Player1UserId { get; set; }

        // NEW: show both ratings
        public decimal? Player1LevelSingle { get; set; }
        public decimal? Player1LevelDouble { get; set; }

        public string? Player2Name { get; set; }
        public string? Player2Avatar { get; set; }
        public decimal Player2Level { get; set; }          // Picked level
        public bool Player2Verified { get; set; }
        public long? Player2UserId { get; set; }

        // NEW: show both ratings
        public decimal? Player2LevelSingle { get; set; }
        public decimal? Player2LevelDouble { get; set; }

        public decimal Points { get; set; }
        public string? BtCode { get; set; }
        public bool Paid { get; set; }
        public bool WaitingPair { get; set; }
        public bool Success { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}