using HanakaServer.Dtos;

namespace HanakaServer.Services
{
    public interface IWebAuthCookieService
    {
        void SetSessionCookies(HttpResponse response, AuthResponseDto authResponse);
        void ClearSessionCookies(HttpResponse response);
        TimeSpan GetWebTokenLifetime();
    }
}
