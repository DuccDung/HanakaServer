namespace mail_service.Internal
{
    public sealed class ZaloOtpSendResult
    {
        public bool Success { get; init; }
        public int? Code { get; init; }
        public int? SmsPerMessage { get; init; }
        public string? Message { get; init; }
        public string SmsGuid { get; init; } = string.Empty;
        public string? Error { get; init; }
    }

    public interface IZaloOtpSender
    {
        Task<ZaloOtpSendResult> SendOtpAsync(string phoneNumber, string otp, CancellationToken ct = default);
    }
}
