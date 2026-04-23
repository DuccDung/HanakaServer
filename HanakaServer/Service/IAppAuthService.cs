using HanakaServer.Dtos;

namespace HanakaServer.Services
{
    public interface IAppAuthService
    {
        Task<RegisterResponseDto> RegisterAsync(RegisterRequestDto dto, CancellationToken ct = default);
        Task<ForgotPasswordResponseDto> ForgotPasswordAsync(ForgotPasswordRequestDto dto, CancellationToken ct = default);
        Task<ForgotPasswordResponseDto> VerifyForgotPasswordOtpAsync(
            ForgotPasswordVerifyOtpRequestDto dto,
            CancellationToken ct = default);
        Task<AuthResponseDto> ResetPasswordWithOtpAsync(
            ForgotPasswordResetRequestDto dto,
            CancellationToken ct = default,
            TimeSpan? accessTokenLifetime = null);
        Task<AuthResponseDto> ConfirmOtpAsync(
            ConfirmOtpRequestDto dto,
            CancellationToken ct = default,
            TimeSpan? accessTokenLifetime = null);
        Task<RegisterResponseDto> ResendOtpAsync(ResendOtpRequestDto dto, CancellationToken ct = default);
        Task<AuthResponseDto> LoginAsync(
            LoginRequestDto dto,
            CancellationToken ct = default,
            TimeSpan? accessTokenLifetime = null);
        Task<AuthUserDto?> GetAuthUserAsync(long userId, CancellationToken ct = default);
    }
}
