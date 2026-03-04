using Microsoft.AspNetCore.Http;

namespace HanakaServer.Dtos
{
    public class CreateRegistrationForm
    {
        public string GameType { get; set; } = "DOUBLE"; // SINGLE/DOUBLE
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
}