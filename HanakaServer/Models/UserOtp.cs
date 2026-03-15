using System;

namespace HanakaServer.Models
{
    public partial class UserOtp
    {
        public long UserOtpId { get; set; }
        public long UserId { get; set; }
        public string Email { get; set; } = null!;
        public string OtpCode { get; set; } = null!;
        public DateTime ExpiredAt { get; set; }
        public bool IsUsed { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UsedAt { get; set; }

        public virtual User User { get; set; } = null!;
    }
}