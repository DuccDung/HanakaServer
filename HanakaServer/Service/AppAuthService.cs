using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HanakaServer.Data;
using HanakaServer.Dtos;
using HanakaServer.Models;
using mail_service.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace HanakaServer.Services
{
    public sealed class AppAuthService : IAppAuthService
    {
        private readonly PickleballDbContext _db;
        private readonly IConfiguration _config;
        private readonly IOtpEmailService _otpEmailService;
        private readonly IUserOtpService _userOtpService;

        public AppAuthService(
            PickleballDbContext db,
            IConfiguration config,
            IOtpEmailService otpEmailService,
            IUserOtpService userOtpService)
        {
            _db = db;
            _config = config;
            _otpEmailService = otpEmailService;
            _userOtpService = userOtpService;
        }

        public async Task<RegisterResponseDto> RegisterAsync(RegisterRequestDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.FullName))
            {
                throw new AuthFlowException(StatusCodes.Status400BadRequest, "FullName is required.");
            }

            if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 6)
            {
                throw new AuthFlowException(StatusCodes.Status400BadRequest, "Password must be at least 6 characters.");
            }

            if (string.IsNullOrWhiteSpace(dto.Email))
            {
                throw new AuthFlowException(StatusCodes.Status400BadRequest, "Email is required.");
            }

            var email = dto.Email.Trim().ToLowerInvariant();
            var phone = dto.Phone?.Trim();

            var existedByEmail = await _db.Users.FirstOrDefaultAsync(x => x.Email == email, ct);
            if (existedByEmail != null)
            {
                if (existedByEmail.IsActive)
                {
                    throw new AuthFlowException(StatusCodes.Status409Conflict, "Email already exists.");
                }

                existedByEmail.FullName = dto.FullName.Trim();
                existedByEmail.City = dto.City?.Trim();
                existedByEmail.Gender = dto.Gender?.Trim();
                existedByEmail.Phone = phone;
                existedByEmail.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
                existedByEmail.UpdatedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync(ct);
                await EnsureInitialRatingHistoryAsync(existedByEmail, ct);

                var resend = await _userOtpService.CreateOtpAsync(existedByEmail, ct);
                await _otpEmailService.SendOtpAsync(email, existedByEmail.FullName, resend.otp, ct);

                return new RegisterResponseDto
                {
                    Message = "Tài khoản chưa kích hoạt. Hệ thống đã gửi lại OTP.",
                    Email = email,
                    OtpExpiredAtUtc = resend.expiredAtUtc
                };
            }

            if (!string.IsNullOrEmpty(phone))
            {
                var existsPhone = await _db.Users.AnyAsync(x => x.Phone == phone, ct);
                if (existsPhone)
                {
                    throw new AuthFlowException(StatusCodes.Status409Conflict, "Phone already exists.");
                }
            }

            var user = new User
            {
                FullName = dto.FullName.Trim(),
                City = dto.City?.Trim(),
                Gender = dto.Gender?.Trim(),
                Email = email,
                Phone = phone,
                Verified = false,
                RatingSingle = null,
                RatingDouble = null,
                AvatarUrl = null,
                IsActive = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password)
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);

            var memberRoleId = await _db.Roles
                .Where(r => r.RoleCode == "MEMBER")
                .Select(r => (int?)r.RoleId)
                .FirstOrDefaultAsync(ct);

            if (memberRoleId.HasValue)
            {
                _db.UserRoles.Add(new UserRole
                {
                    UserId = user.UserId,
                    RoleId = memberRoleId.Value,
                    CreatedAt = DateTime.UtcNow
                });

                await _db.SaveChangesAsync(ct);
            }

            await EnsureInitialRatingHistoryAsync(user, ct);

            var otpResult = await _userOtpService.CreateOtpAsync(user, ct);
            await _otpEmailService.SendOtpAsync(email, user.FullName, otpResult.otp, ct);

            return new RegisterResponseDto
            {
                Message = "Đăng ký thành công. Vui lòng kiểm tra email để lấy mã OTP.",
                Email = email,
                OtpExpiredAtUtc = otpResult.expiredAtUtc
            };
        }

        public async Task<AuthResponseDto> ConfirmOtpAsync(
            ConfirmOtpRequestDto dto,
            CancellationToken ct = default,
            TimeSpan? accessTokenLifetime = null)
        {
            if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Otp))
            {
                throw new AuthFlowException(StatusCodes.Status400BadRequest, "Email and OTP are required.");
            }

            var email = dto.Email.Trim().ToLowerInvariant();
            var otp = dto.Otp.Trim();

            var user = await _db.Users.FirstOrDefaultAsync(x => x.Email == email, ct);
            if (user == null)
            {
                throw new AuthFlowException(StatusCodes.Status404NotFound, "User not found.");
            }

            var otpEntity = await _db.UserOtps
                .Where(x => x.UserId == user.UserId
                            && x.Email == email
                            && !x.IsUsed
                            && x.OtpCode == otp)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (otpEntity == null)
            {
                throw new AuthFlowException(StatusCodes.Status400BadRequest, "OTP không đúng hoặc đã được sử dụng.");
            }

            if (otpEntity.ExpiredAt < DateTime.UtcNow)
            {
                throw new AuthFlowException(StatusCodes.Status400BadRequest, "OTP đã hết hạn.");
            }

            otpEntity.IsUsed = true;
            otpEntity.UsedAt = DateTime.UtcNow;
            user.IsActive = true;
            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            await EnsureInitialRatingHistoryAsync(user, ct);

            return await CreateAuthResponseAsync(user, ct, accessTokenLifetime);
        }

        public async Task<RegisterResponseDto> ResendOtpAsync(ResendOtpRequestDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.Email))
            {
                throw new AuthFlowException(StatusCodes.Status400BadRequest, "Email is required.");
            }

            var email = dto.Email.Trim().ToLowerInvariant();
            var user = await _db.Users.FirstOrDefaultAsync(x => x.Email == email, ct);

            if (user == null)
            {
                throw new AuthFlowException(StatusCodes.Status404NotFound, "User not found.");
            }

            if (user.IsActive)
            {
                throw new AuthFlowException(StatusCodes.Status400BadRequest, "Tài khoản đã được kích hoạt.");
            }

            await EnsureInitialRatingHistoryAsync(user, ct);

            var otpResult = await _userOtpService.CreateOtpAsync(user, ct);
            await _otpEmailService.SendOtpAsync(email, user.FullName, otpResult.otp, ct);

            return new RegisterResponseDto
            {
                Message = "OTP mới đã được gửi.",
                Email = email,
                OtpExpiredAtUtc = otpResult.expiredAtUtc
            };
        }

        public async Task<AuthResponseDto> LoginAsync(
            LoginRequestDto dto,
            CancellationToken ct = default,
            TimeSpan? accessTokenLifetime = null)
        {
            var identifier = dto.Identifier?.Trim();
            if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(dto.Password))
            {
                throw new AuthFlowException(StatusCodes.Status400BadRequest, "Identifier and Password are required.");
            }

            var user = await _db.Users.FirstOrDefaultAsync(x =>
                (x.Email != null && x.Email == identifier) ||
                (x.Phone != null && x.Phone == identifier), ct);

            if (user == null)
            {
                throw new AuthFlowException(StatusCodes.Status401Unauthorized, "Invalid credentials.");
            }

            if (!user.IsActive)
            {
                throw new AuthFlowException(StatusCodes.Status401Unauthorized, "Tài khoản chưa được kích hoạt. Vui lòng xác thực OTP.");
            }

            var ok = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
            if (!ok)
            {
                throw new AuthFlowException(StatusCodes.Status401Unauthorized, "Invalid credentials.");
            }

            await EnsureInitialRatingHistoryAsync(user, ct);
            return await CreateAuthResponseAsync(user, ct, accessTokenLifetime);
        }

        public async Task<AuthUserDto?> GetAuthUserAsync(long userId, CancellationToken ct = default)
        {
            var user = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId && x.IsActive, ct);

            if (user == null)
            {
                return null;
            }

            var latestRating = await GetLatestRatingAsync(user.UserId, ct);

            return new AuthUserDto
            {
                UserId = user.UserId,
                FullName = user.FullName ?? string.Empty,
                Email = user.Email,
                Phone = user.Phone,
                Verified = user.Verified,
                RatingSingle = latestRating.RatingSingle,
                RatingDouble = latestRating.RatingDouble,
                AvatarUrl = string.IsNullOrWhiteSpace(user.AvatarUrl)
                    ? null
                    : GetBaseUrl() + user.AvatarUrl
            };
        }

        private string GetBaseUrl()
        {
            return _config["PublicBaseUrl"] ?? string.Empty;
        }

        private static bool IsFemale(string? gender)
        {
            if (string.IsNullOrWhiteSpace(gender))
            {
                return false;
            }

            var g = gender.Trim().ToLowerInvariant();

            return g == "nữ"
                || g == "nu"
                || g == "female"
                || g == "f"
                || g == "woman"
                || g == "girl";
        }

        private static (decimal single, decimal @double) GetDefaultInitialRating(string? gender)
        {
            if (IsFemale(gender))
            {
                return (1.8m, 1.8m);
            }

            return (2.3m, 2.3m);
        }

        private async Task EnsureInitialRatingHistoryAsync(User user, CancellationToken ct)
        {
            var exists = await _db.UserRatingHistories
                .AnyAsync(x => x.UserId == user.UserId, ct);

            if (exists)
            {
                return;
            }

            var (single, @double) = GetDefaultInitialRating(user.Gender);
            var now = DateTime.UtcNow;

            _db.UserRatingHistories.Add(new UserRatingHistory
            {
                UserId = user.UserId,
                RatingSingle = single,
                RatingDouble = @double,
                RatedByUserId = 2,
                Note = $"Hệ thống khởi tạo điểm trình ban đầu theo giới tính: {(string.IsNullOrWhiteSpace(user.Gender) ? "mặc định nam" : user.Gender)}.",
                RatedAt = now
            });

            user.RatingSingle = single;
            user.RatingDouble = @double;
            user.UpdatedAt = now;

            await _db.SaveChangesAsync(ct);
        }

        private async Task<(decimal? RatingSingle, decimal? RatingDouble)> GetLatestRatingAsync(long userId, CancellationToken ct)
        {
            var latest = await _db.UserRatingHistories
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.RatedAt)
                .ThenByDescending(x => x.RatingHistoryId)
                .Select(x => new
                {
                    x.RatingSingle,
                    x.RatingDouble
                })
                .FirstOrDefaultAsync(ct);

            return latest == null
                ? (null, null)
                : (latest.RatingSingle, latest.RatingDouble);
        }

        private async Task<AuthResponseDto> CreateAuthResponseAsync(
            User user,
            CancellationToken ct,
            TimeSpan? accessTokenLifetime = null)
        {
            var jwtSection = _config.GetSection("Jwt");
            var issuer = jwtSection["Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer is missing.");
            var audience = jwtSection["Audience"] ?? throw new InvalidOperationException("Jwt:Audience is missing.");
            var key = jwtSection["Key"] ?? throw new InvalidOperationException("Jwt:Key is missing.");

            var lifetime = accessTokenLifetime ?? GetDefaultAccessTokenLifetime(jwtSection);
            var expires = DateTime.UtcNow.Add(lifetime);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.FullName ?? string.Empty),
                new Claim("uid", user.UserId.ToString())
            };

            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: expires,
                signingCredentials: creds);

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
            var latestRating = await GetLatestRatingAsync(user.UserId, ct);

            return new AuthResponseDto
            {
                AccessToken = tokenString,
                ExpiresAtUtc = expires,
                User = new AuthUserDto
                {
                    UserId = user.UserId,
                    FullName = user.FullName ?? string.Empty,
                    Email = user.Email,
                    Phone = user.Phone,
                    Verified = user.Verified,
                    RatingSingle = latestRating.RatingSingle,
                    RatingDouble = latestRating.RatingDouble,
                    AvatarUrl = string.IsNullOrWhiteSpace(user.AvatarUrl)
                        ? null
                        : GetBaseUrl() + user.AvatarUrl
                }
            };
        }

        private static TimeSpan GetDefaultAccessTokenLifetime(IConfigurationSection jwtSection)
        {
            var minutes = int.TryParse(jwtSection["AccessTokenMinutes"], out var parsedMinutes)
                ? parsedMinutes
                : 120;

            if (minutes < 1)
            {
                minutes = 120;
            }

            return TimeSpan.FromMinutes(minutes);
        }
    }
}
