namespace mail_service.Internal
{
    public interface IOtpGenerator
    {
        string GenerateOtp(int length = 6);
    }
}