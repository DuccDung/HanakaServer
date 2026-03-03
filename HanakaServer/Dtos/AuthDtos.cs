namespace HanakaServer.Dtos;

public sealed class RegisterRequestDto
{
    public string FullName { get; set; } = null!;
    public string? City { get; set; }
    public string? Gender { get; set; } // Nam/Nữ/Khác...
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string Password { get; set; } = null!;
}

public sealed class LoginRequestDto
{
    // login bằng Email hoặc Phone
    public string Identifier { get; set; } = null!;
    public string Password { get; set; } = null!;
}

public sealed class AuthUserDto
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

public sealed class AuthResponseDto
{
    public string AccessToken { get; set; } = null!;
    public DateTime ExpiresAtUtc { get; set; }
    public AuthUserDto User { get; set; } = null!;
}