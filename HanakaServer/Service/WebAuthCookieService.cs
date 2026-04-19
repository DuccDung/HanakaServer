using HanakaServer.Dtos;

namespace HanakaServer.Services
{
    public sealed class WebAuthCookieService : IWebAuthCookieService
    {
        public const string AccessTokenCookieName = "Hanaka.Web.AccessToken";

        private readonly IConfiguration _config;

        public WebAuthCookieService(IConfiguration config)
        {
            _config = config;
        }

        public void SetSessionCookies(HttpResponse response, AuthResponseDto authResponse)
        {
            response.Cookies.Append(
                AccessTokenCookieName,
                authResponse.AccessToken,
                BuildCookieOptions(authResponse.ExpiresAtUtc, response.HttpContext.Request.IsHttps));
        }

        public void ClearSessionCookies(HttpResponse response)
        {
            response.Cookies.Delete(
                AccessTokenCookieName,
                new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = response.HttpContext.Request.IsHttps,
                    Path = "/"
                });
        }

        public TimeSpan GetWebTokenLifetime()
        {
            var days = int.TryParse(_config["Jwt:WebAccessTokenDays"], out var value)
                ? value
                : 30;

            if (days < 1)
            {
                days = 1;
            }

            return TimeSpan.FromDays(days);
        }

        private static CookieOptions BuildCookieOptions(DateTime expiresAtUtc, bool isHttps)
        {
            return new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = isHttps,
                Expires = new DateTimeOffset(expiresAtUtc),
                Path = "/"
            };
        }
    }
}
