namespace mail_service.Internal
{
    public interface IOtpEmailService
    {
        Task SendOtpAsync(string to, string displayName, string otp, CancellationToken ct = default);
        Task SendPasswordResetOtpAsync(string to, string displayName, string otp, CancellationToken ct = default);
    }
}
