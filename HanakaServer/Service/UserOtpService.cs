using HanakaServer.Data;
using HanakaServer.Models;
using mail_service.Internal;
using Microsoft.EntityFrameworkCore;

namespace mail_service.service
{
    public class UserOtpService : IUserOtpService
    {
        private readonly PickleballDbContext _db;
        private readonly IOtpGenerator _otpGenerator;
        private readonly IConfiguration _config;

        public UserOtpService(
            PickleballDbContext db,
            IOtpGenerator otpGenerator,
            IConfiguration config)
        {
            _db = db;
            _otpGenerator = otpGenerator;
            _config = config;
        }

        public async Task<(string otp, DateTime expiredAtUtc)> CreateOtpAsync(User user, CancellationToken ct = default)
        {
            var email = user.Email?.Trim();
            if (string.IsNullOrWhiteSpace(email))
                throw new InvalidOperationException("User does not have email.");

            await InvalidateActiveOtpsAsync(user.UserId, email, ct);

            var otp = _otpGenerator.GenerateOtp(6);
            var minutes = int.TryParse(_config["Otp:ExpireMinutes"], out var m) ? m : 5;
            var expiredAt = DateTime.UtcNow.AddMinutes(minutes);

            var entity = new UserOtp
            {
                UserId = user.UserId,
                Email = email,
                OtpCode = otp,
                ExpiredAt = expiredAt,
                IsUsed = false,
                CreatedAt = DateTime.UtcNow
            };

            _db.UserOtps.Add(entity);
            await _db.SaveChangesAsync(ct);

            return (otp, expiredAt);
        }

        public async Task InvalidateActiveOtpsAsync(long userId, string email, CancellationToken ct = default)
        {
            var activeOtps = await _db.UserOtps
                .Where(x => x.UserId == userId
                            && x.Email == email
                            && !x.IsUsed
                            && x.ExpiredAt > DateTime.UtcNow)
                .ToListAsync(ct);

            foreach (var item in activeOtps)
            {
                item.IsUsed = true;
                item.UsedAt = DateTime.UtcNow;
            }

            if (activeOtps.Count > 0)
                await _db.SaveChangesAsync(ct);
        }
    }
}