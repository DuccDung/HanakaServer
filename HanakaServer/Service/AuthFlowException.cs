namespace HanakaServer.Services
{
    public sealed class AuthFlowException : Exception
    {
        public AuthFlowException(int statusCode, string message)
            : base(message)
        {
            StatusCode = statusCode;
        }

        public int StatusCode { get; }
    }
}
