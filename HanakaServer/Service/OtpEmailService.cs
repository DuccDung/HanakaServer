using System.Net;
using mail_service.Internal;

namespace mail_service.service
{
    public class OtpEmailService : IOtpEmailService
    {
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _config;

        public OtpEmailService(IEmailSender emailSender, IConfiguration config)
        {
            _emailSender = emailSender;
            _config = config;
        }

        public async Task SendOtpAsync(string to, string displayName, string otp, CancellationToken ct = default)
        {
            var subject = "Mã OTP đăng ký tài khoản";
            var htmlBody = BuildOtpHtml(displayName, otp);

            await _emailSender.SendAsync(to, subject, htmlBody, ct: ct);
        }

        private string BuildOtpHtml(string displayName, string otp)
        {
            var safeName = WebUtility.HtmlEncode(displayName);
            var safeOtp = WebUtility.HtmlEncode(otp);
            var expireMinutes = _config["Otp:ExpireMinutes"] ?? "5";

            return $"""
            <!DOCTYPE html>
            <html lang="vi">
            <head>
                <meta charset="UTF-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <title>Mã OTP</title>
            </head>
            <body style="margin:0;padding:0;background:#f5f7fb;font-family:Arial,sans-serif;color:#1f2937;">
                <div style="max-width:600px;margin:0 auto;padding:24px;">
                    <div style="background:#ffffff;border-radius:16px;padding:32px;border:1px solid #e5e7eb;">
                        <h2 style="margin:0 0 16px;">Xin chào {safeName},</h2>
                        <p style="margin:0 0 16px;line-height:1.6;">
                            Cảm ơn bạn đã đăng ký tài khoản. Vui lòng sử dụng mã OTP bên dưới để kích hoạt tài khoản của bạn.
                        </p>

                        <div style="margin:24px 0;text-align:center;">
                            <div style="display:inline-block;background:#eff6ff;border:1px dashed #2563eb;border-radius:12px;padding:16px 28px;">
                                <span style="font-size:32px;font-weight:700;letter-spacing:8px;color:#2563eb;">{safeOtp}</span>
                            </div>
                        </div>

                        <p style="margin:0 0 8px;line-height:1.6;">
                            Mã OTP có hiệu lực trong <strong>{expireMinutes} phút</strong>.
                        </p>
                        <p style="margin:0;line-height:1.6;color:#6b7280;">
                            Nếu bạn không thực hiện yêu cầu này, vui lòng bỏ qua email.
                        </p>
                    </div>
                </div>
            </body>
            </html>
            """;
        }
    }
}
