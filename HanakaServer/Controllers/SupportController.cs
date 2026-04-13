using System.ComponentModel.DataAnnotations;
using System.Net;
using HanakaServer.Dtos;
using mail_service.Internal;
using Microsoft.AspNetCore.Mvc;

namespace HanakaServer.Controllers
{
    [Route("support")]
    public class SupportController : Controller
    {
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _config;

        public SupportController(IEmailSender emailSender, IConfiguration config)
        {
            _emailSender = emailSender;
            _config = config;
        }

        [HttpGet("")]
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost("send")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Send([FromForm] SupportRequestDto? request, CancellationToken ct)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Không nhận được nội dung yêu cầu hỗ trợ." });
            }

            if (!TryValidateModel(request))
            {
                var message = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .FirstOrDefault() ?? "Dữ liệu gửi lên chưa hợp lệ.";

                return BadRequest(new { message });
            }

            var inboxAddress =
                _config["Support:InboxAddress"] ??
                _config["Email:FromAddress"] ??
                "duccdung999@gmail.com";

            try
            {
                var subject = $"[Hanaka Sport Support] {request.Topic} - {request.Name}";
                var htmlBody = BuildSupportEmailHtml(request);

                await _emailSender.SendAsync(
                    inboxAddress,
                    subject,
                    htmlBody,
                    request.Email,
                    ct);

                return Ok(new
                {
                    message = "Yêu cầu hỗ trợ đã được gửi thành công. Chúng tôi sẽ phản hồi sớm nhất có thể."
                });
            }
            catch (Exception)
            {
                return StatusCode(500, new
                {
                    message = "Không thể gửi yêu cầu hỗ trợ lúc này. Vui lòng thử lại sau."
                });
            }
        }

        private static string BuildSupportEmailHtml(SupportRequestDto request)
        {
            var safeName = WebUtility.HtmlEncode(request.Name.Trim());
            var safeEmail = WebUtility.HtmlEncode(request.Email.Trim());
            var safePhone = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(request.Phone)
                ? "Không cung cấp"
                : request.Phone.Trim());
            var safeTopic = WebUtility.HtmlEncode(request.Topic.Trim());
            var safeMessage = WebUtility.HtmlEncode(request.Message.Trim())
                .Replace("\r\n", "<br />")
                .Replace("\n", "<br />");
            var createdAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");

            return $"""
            <!DOCTYPE html>
            <html lang="vi">
            <head>
                <meta charset="UTF-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <title>Hanaka Sport Support</title>
            </head>
            <body style="margin:0;padding:0;background:#f5f7fb;font-family:Arial,sans-serif;color:#1f2937;">
                <div style="max-width:720px;margin:0 auto;padding:24px;">
                    <div style="background:#ffffff;border-radius:16px;padding:32px;border:1px solid #e5e7eb;">
                        <div style="display:inline-block;background:#eff6ff;color:#1d4ed8;font-weight:700;font-size:13px;padding:8px 14px;border-radius:999px;margin-bottom:16px;">
                            Hanaka Sport Support
                        </div>

                        <h2 style="margin:0 0 16px;">Yêu cầu hỗ trợ mới từ người dùng</h2>

                        <p style="margin:0 0 20px;line-height:1.6;">
                            Một người dùng vừa gửi yêu cầu hỗ trợ từ trang hỗ trợ của Hanaka Sport.
                        </p>

                        <table style="width:100%;border-collapse:collapse;margin-bottom:20px;">
                            <tr>
                                <td style="padding:10px 12px;border:1px solid #e5e7eb;background:#f9fafb;font-weight:700;width:180px;">Họ và tên</td>
                                <td style="padding:10px 12px;border:1px solid #e5e7eb;">{safeName}</td>
                            </tr>
                            <tr>
                                <td style="padding:10px 12px;border:1px solid #e5e7eb;background:#f9fafb;font-weight:700;">Email</td>
                                <td style="padding:10px 12px;border:1px solid #e5e7eb;">{safeEmail}</td>
                            </tr>
                            <tr>
                                <td style="padding:10px 12px;border:1px solid #e5e7eb;background:#f9fafb;font-weight:700;">Số điện thoại</td>
                                <td style="padding:10px 12px;border:1px solid #e5e7eb;">{safePhone}</td>
                            </tr>
                            <tr>
                                <td style="padding:10px 12px;border:1px solid #e5e7eb;background:#f9fafb;font-weight:700;">Chủ đề</td>
                                <td style="padding:10px 12px;border:1px solid #e5e7eb;">{safeTopic}</td>
                            </tr>
                            <tr>
                                <td style="padding:10px 12px;border:1px solid #e5e7eb;background:#f9fafb;font-weight:700;">Thời gian</td>
                                <td style="padding:10px 12px;border:1px solid #e5e7eb;">{createdAt}</td>
                            </tr>
                        </table>

                        <div style="margin-top:20px;">
                            <div style="font-weight:700;margin-bottom:8px;">Mô tả vấn đề</div>
                            <div style="padding:16px;border-radius:12px;background:#f8fafc;border:1px solid #e5e7eb;line-height:1.7;">
                                {safeMessage}
                            </div>
                        </div>

                        <p style="margin:20px 0 0;line-height:1.6;color:#6b7280;">
                            Email này được gửi tự động từ biểu mẫu hỗ trợ Hanaka Sport.
                        </p>
                    </div>
                </div>
            </body>
            </html>
            """;
        }
    }
}
