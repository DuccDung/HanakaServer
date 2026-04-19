using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using HanakaServer.Dtos;
using HanakaServer.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanakaServer.Controllers
{
    [Route("api/web-auth")]
    [ApiController]
    [AllowAnonymous]
    public class WebAuthApiController : ControllerBase
    {
        private readonly IAppAuthService _appAuthService;
        private readonly IWebAuthCookieService _webAuthCookieService;

        public WebAuthApiController(
            IAppAuthService appAuthService,
            IWebAuthCookieService webAuthCookieService)
        {
            _appAuthService = appAuthService;
            _webAuthCookieService = webAuthCookieService;
        }

        [HttpPost("register")]
        public async Task<ActionResult<RegisterResponseDto>> Register([FromBody] RegisterRequestDto dto, CancellationToken ct)
        {
            try
            {
                return Ok(await _appAuthService.RegisterAsync(dto, ct));
            }
            catch (AuthFlowException ex)
            {
                return StatusCode(ex.StatusCode, ex.Message);
            }
        }

        [HttpPost("login")]
        public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginRequestDto dto, CancellationToken ct)
        {
            try
            {
                var auth = await _appAuthService.LoginAsync(
                    dto,
                    ct,
                    _webAuthCookieService.GetWebTokenLifetime());

                _webAuthCookieService.SetSessionCookies(Response, auth);
                return Ok(auth);
            }
            catch (AuthFlowException ex)
            {
                return StatusCode(ex.StatusCode, ex.Message);
            }
        }

        [HttpPost("confirm-otp")]
        public async Task<ActionResult<AuthResponseDto>> ConfirmOtp([FromBody] ConfirmOtpRequestDto dto, CancellationToken ct)
        {
            try
            {
                var auth = await _appAuthService.ConfirmOtpAsync(
                    dto,
                    ct,
                    _webAuthCookieService.GetWebTokenLifetime());

                _webAuthCookieService.SetSessionCookies(Response, auth);
                return Ok(auth);
            }
            catch (AuthFlowException ex)
            {
                return StatusCode(ex.StatusCode, ex.Message);
            }
        }

        [HttpPost("resend-otp")]
        public async Task<ActionResult<RegisterResponseDto>> ResendOtp([FromBody] ResendOtpRequestDto dto, CancellationToken ct)
        {
            try
            {
                return Ok(await _appAuthService.ResendOtpAsync(dto, ct));
            }
            catch (AuthFlowException ex)
            {
                return StatusCode(ex.StatusCode, ex.Message);
            }
        }

        [HttpGet("me")]
        public async Task<IActionResult> GetCurrentSession(CancellationToken ct)
        {
            var authResult = await HttpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
            if (!authResult.Succeeded || authResult.Principal == null)
            {
                _webAuthCookieService.ClearSessionCookies(Response);
                return Ok(new
                {
                    isAuthenticated = false
                });
            }

            var userIdValue =
                authResult.Principal.FindFirstValue("uid") ??
                authResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!long.TryParse(userIdValue, out var userId))
            {
                _webAuthCookieService.ClearSessionCookies(Response);
                return Ok(new
                {
                    isAuthenticated = false
                });
            }

            var user = await _appAuthService.GetAuthUserAsync(userId, ct);
            if (user == null)
            {
                _webAuthCookieService.ClearSessionCookies(Response);
                return Ok(new
                {
                    isAuthenticated = false
                });
            }

            return Ok(new
            {
                isAuthenticated = true,
                expiresAtUtc = GetExpiresAtUtc(authResult.Principal),
                user
            });
        }

        [HttpPost("logout")]
        public IActionResult Logout()
        {
            _webAuthCookieService.ClearSessionCookies(Response);
            return Ok(new
            {
                message = "Đã đăng xuất."
            });
        }

        private static DateTime? GetExpiresAtUtc(ClaimsPrincipal principal)
        {
            var expValue = principal.FindFirstValue(JwtRegisteredClaimNames.Exp);
            if (!long.TryParse(expValue, out var unixSeconds))
            {
                return null;
            }

            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
        }
    }
}
