using System.Security.Cryptography;
using mail_service.Internal;

namespace mail_service.service
{
    public class OtpGenerator : IOtpGenerator
    {
        public string GenerateOtp(int length = 6)
        {
            if (length <= 0) length = 6;

            var digits = new char[length];
            for (int i = 0; i < length; i++)
            {
                digits[i] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
            }

            return new string(digits);
        }
    }
}