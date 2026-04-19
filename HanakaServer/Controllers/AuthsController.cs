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

        public AuthsController(IAppAuthService appAuthService)
        {
            _appAuthService = appAuthService;
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
    }
}
