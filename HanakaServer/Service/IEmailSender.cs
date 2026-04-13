namespace mail_service.Internal
{
    public interface IEmailSender
    {
        Task SendAsync(
            string to,
            string subject,
            string htmlBody,
            string? replyTo = null,
            CancellationToken ct = default);
    }
}
