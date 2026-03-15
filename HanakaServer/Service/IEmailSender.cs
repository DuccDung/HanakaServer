namespace mail_service.Internal
{
    public interface IEmailSender
    {
        Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
    }
}