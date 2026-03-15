namespace HanakaServer.Dtos;

public class RegisterRequestDto
{
    public string FullName { get; set; } = null!;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string Password { get; set; } = null!;
    public string? City { get; set; }
    public string? Gender { get; set; }
}

public class LoginRequestDto
{
    public string Identifier { get; set; } = null!;
    public string Password { get; set; } = null!;
}
public class ConfirmOtpRequestDto
{
    public string Email { get; set; } = null!;
    public string Otp { get; set; } = null!;
}
public class ResendOtpRequestDto
{
    public string Email { get; set; } = null!;
}
public class RegisterResponseDto
{
    public string Message { get; set; } = null!;
    public string Email { get; set; } = null!;
    public DateTime OtpExpiredAtUtc { get; set; }
}
public class AuthUserDto
{
    public long UserId { get; set; }
    public string FullName { get; set; } = null!;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool Verified { get; set; }
    public decimal? RatingSingle { get; set; }
    public decimal? RatingDouble { get; set; }
    public string? AvatarUrl { get; set; }
}

public class AuthResponseDto
{
    public string AccessToken { get; set; } = null!;
    public DateTime ExpiresAtUtc { get; set; }
    public AuthUserDto User { get; set; } = null!;
}