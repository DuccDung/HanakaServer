using HanakaServer.Models;

namespace mail_service.Internal
{
    public sealed class OtpDeliveryResult
    {
        public bool Delivered { get; init; }
        public string? Channel { get; init; }
        public string? Target { get; init; }
        public string? Message { get; init; }
        public bool UsedEmailFallback { get; init; }
        public string? Error { get; init; }
    }

    public interface IOtpDeliveryService
    {
        Task<OtpDeliveryResult> SendRegistrationOtpAsync(User user, string otp, CancellationToken ct = default);
        Task<OtpDeliveryResult> SendPasswordResetOtpAsync(User user, string otp, CancellationToken ct = default);
    }
}
