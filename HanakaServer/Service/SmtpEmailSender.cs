using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using mail_service.Internal;

namespace mail_service.service
{
    public class SmtpEmailSender : IEmailSender
    {
        private readonly IConfiguration _config;

        public SmtpEmailSender(IConfiguration config)
        {
            _config = config;
        }

        public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        {
            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress(_config["Email:FromName"], _config["Email:FromAddress"]));
            msg.To.Add(MailboxAddress.Parse(to));
            msg.Subject = subject;
            msg.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

            using var client = new SmtpClient();

            var host = _config["Smtp:Host"];
            var port = int.Parse(_config["Smtp:Port"] ?? "587");
            var useStartTls = bool.Parse(_config["Smtp:UseStartTls"] ?? "true");
            var timeout = TimeSpan.FromSeconds(int.Parse(_config["Smtp:TimeoutSeconds"] ?? "30"));

            client.Timeout = (int)timeout.TotalMilliseconds;

            await client.ConnectAsync(
                host,
                port,
                useStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto,
                ct);

            var user = _config["Smtp:User"];
            var pass = _config["Smtp:Pass"];

            if (!string.IsNullOrWhiteSpace(user))
            {
                await client.AuthenticateAsync(user, pass, ct);
            }

            await client.SendAsync(msg, ct);
            await client.DisconnectAsync(true, ct);
        }
    }
}