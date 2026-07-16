using HanakaServer.Models;
using mail_service.Internal;

namespace mail_service.service
{
    public sealed class OtpDeliveryService : IOtpDeliveryService
    {
        private const string ChannelZalo = "ZALO";
        private const string ChannelEmail = "EMAIL";

        private readonly IZaloOtpSender _zaloOtpSender;
        private readonly IOtpEmailService _otpEmailService;

        public OtpDeliveryService(
            IZaloOtpSender zaloOtpSender,
            IOtpEmailService otpEmailService)
        {
            _zaloOtpSender = zaloOtpSender;
            _otpEmailService = otpEmailService;
        }

        public Task<OtpDeliveryResult> SendRegistrationOtpAsync(User user, string otp, CancellationToken ct = default)
        {
            return SendOtpAsync(
                user,
                otp,
                sendEmailAsync: _otpEmailService.SendOtpAsync,
                zaloMessage: "Mã OTP đã được gửi về Zalo theo số điện thoại của bạn.",
                emailMessage: "Mã OTP đã được gửi qua email của bạn.",
                fallbackMessage: "Không gửi được OTP về Zalo. Mã OTP đã được gửi qua email của bạn.",
                ct);
        }

        public Task<OtpDeliveryResult> SendPasswordResetOtpAsync(User user, string otp, CancellationToken ct = default)
        {
            return SendOtpAsync(
                user,
                otp,
                sendEmailAsync: _otpEmailService.SendPasswordResetOtpAsync,
                zaloMessage: "Mã OTP đặt lại mật khẩu đã được gửi về Zalo theo số điện thoại của bạn.",
                emailMessage: "Mã OTP đặt lại mật khẩu đã được gửi qua email của bạn.",
                fallbackMessage: "Không gửi được OTP về Zalo. Mã OTP đặt lại mật khẩu đã được gửi qua email của bạn.",
                ct);
        }

        private async Task<OtpDeliveryResult> SendOtpAsync(
            User user,
            string otp,
            Func<string, string, string, CancellationToken, Task> sendEmailAsync,
            string zaloMessage,
            string emailMessage,
            string fallbackMessage,
            CancellationToken ct)
        {
            var phone = user.Phone?.Trim();
            var email = user.Email?.Trim();
            var triedZalo = false;
            string? zaloError = null;

            if (!string.IsNullOrWhiteSpace(phone))
            {
                triedZalo = true;
                var zaloResult = await _zaloOtpSender.SendOtpAsync(phone, otp, ct);

                if (zaloResult.Success)
                {
                    return new OtpDeliveryResult
                    {
                        Delivered = true,
                        Channel = ChannelZalo,
                        Target = MaskPhone(phone),
                        Message = zaloMessage
                    };
                }

                zaloError = zaloResult.Error ?? zaloResult.Message;
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                try
                {
                    await sendEmailAsync(email, ResolveDisplayName(user), otp, ct);

                    return new OtpDeliveryResult
                    {
                        Delivered = true,
                        Channel = ChannelEmail,
                        Target = MaskEmail(email),
                        UsedEmailFallback = triedZalo,
                        Message = triedZalo ? fallbackMessage : emailMessage,
                        Error = triedZalo ? zaloError : null
                    };
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return new OtpDeliveryResult
                    {
                        Delivered = false,
                        Error = triedZalo
                            ? $"Zalo failed: {zaloError}; Email failed: {ex.Message}"
                            : $"Email failed: {ex.Message}"
                    };
                }
            }

            return new OtpDeliveryResult
            {
                Delivered = false,
                Error = triedZalo
                    ? $"Zalo failed: {zaloError}; missing fallback email."
                    : "Missing phone number and email."
            };
        }

        private static string ResolveDisplayName(User user)
        {
            return string.IsNullOrWhiteSpace(user.FullName) ? "bạn" : user.FullName;
        }

        private static string MaskPhone(string phone)
        {
            var digits = new string(phone.Where(char.IsDigit).ToArray());
            if (digits.Length <= 4)
            {
                return phone;
            }

            return new string('*', Math.Max(0, digits.Length - 4)) + digits[^4..];
        }

        private static string MaskEmail(string email)
        {
            var parts = email.Split('@', 2);
            if (parts.Length != 2 || parts[0].Length == 0)
            {
                return email;
            }

            var local = parts[0];
            var maskedLocal = local.Length == 1
                ? $"{local[0]}***"
                : $"{local[0]}***{local[^1]}";

            return $"{maskedLocal}@{parts[1]}";
        }
    }
}
