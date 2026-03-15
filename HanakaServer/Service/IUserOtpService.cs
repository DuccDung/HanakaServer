using HanakaServer.Models;

namespace mail_service.Internal
{
    public interface IUserOtpService
    {
        Task<(string otp, DateTime expiredAtUtc)> CreateOtpAsync(User user, CancellationToken ct = default);
        Task InvalidateActiveOtpsAsync(long userId, string email, CancellationToken ct = default);
    }
}