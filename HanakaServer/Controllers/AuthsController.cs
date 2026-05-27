using HanakaServer.Dtos;
using HanakaServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace HanakaServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthsController : ControllerBase
    {
        private readonly IAppAuthService _appAuthService;
        private readonly IWebAuthCookieService _webAuthCookieService;

        public AuthsController(
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

        [HttpPost("confirm-otp")]
        public async Task<ActionResult<AuthResponseDto>> ConfirmOtp([FromBody] ConfirmOtpRequestDto dto, CancellationToken ct)
        {
            try
            {
                return Ok(await _appAuthService.ConfirmOtpAsync(dto, ct));
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

        [HttpPost("forgot-password")]
        public async Task<ActionResult<ForgotPasswordResponseDto>> ForgotPassword([FromBody] ForgotPasswordRequestDto dto, CancellationToken ct)
        {
            try
            {
                return Ok(await _appAuthService.ForgotPasswordAsync(dto, ct));
            }
            catch (AuthFlowException ex)
            {
                return StatusCode(ex.StatusCode, ex.Message);
            }
        }

        [HttpPost("forgot-password/verify-otp")]
        public async Task<ActionResult<ForgotPasswordResponseDto>> VerifyForgotPasswordOtp(
            [FromBody] ForgotPasswordVerifyOtpRequestDto dto,
            CancellationToken ct)
        {
            try
            {
                return Ok(await _appAuthService.VerifyForgotPasswordOtpAsync(dto, ct));
            }
            catch (AuthFlowException ex)
            {
                return StatusCode(ex.StatusCode, ex.Message);
            }
        }

        [HttpPost("forgot-password/reset")]
        public async Task<ActionResult<AuthResponseDto>> ResetPasswordWithOtp(
            [FromBody] ForgotPasswordResetRequestDto dto,
            CancellationToken ct)
        {
            try
            {
                return Ok(await _appAuthService.ResetPasswordWithOtpAsync(dto, ct));
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
                return Ok(await _appAuthService.LoginAsync(dto, ct));
            }
            catch (AuthFlowException ex)
            {
                return StatusCode(ex.StatusCode, ex.Message);
            }
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
    }
}
