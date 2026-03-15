using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BCrypt.Net;
using HanakaServer.Data;
using HanakaServer.Dtos;
using HanakaServer.Models;
using mail_service.Internal;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace HanakaServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthsController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly IConfiguration _config;
        private readonly IOtpEmailService _otpEmailService;
        private readonly IUserOtpService _userOtpService;

        public AuthsController(
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

        private string GetBaseUrl()
        {
            return _config["PublicBaseUrl"] ?? "";
        }

        [HttpPost("register")]
        public async Task<ActionResult<RegisterResponseDto>> Register([FromBody] RegisterRequestDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.FullName))
                return BadRequest("FullName is required.");

            if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 6)
                return BadRequest("Password must be at least 6 characters.");

            if (string.IsNullOrWhiteSpace(dto.Email))
                return BadRequest("Email is required.");

            var email = dto.Email.Trim().ToLowerInvariant();
            var phone = dto.Phone?.Trim();

            var existedByEmail = await _db.Users.FirstOrDefaultAsync(x => x.Email == email, ct);
            if (existedByEmail != null)
            {
                if (existedByEmail.IsActive)
                    return Conflict("Email already exists.");

                existedByEmail.FullName = dto.FullName.Trim();
                existedByEmail.City = dto.City?.Trim();
                existedByEmail.Gender = dto.Gender?.Trim();
                existedByEmail.Phone = phone;
                existedByEmail.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
                existedByEmail.UpdatedAt = DateTime.UtcNow;

                var resend = await _userOtpService.CreateOtpAsync(existedByEmail, ct);
                await _otpEmailService.SendOtpAsync(email, existedByEmail.FullName, resend.otp, ct);

                return Ok(new RegisterResponseDto
                {
                    Message = "Tài khoản chưa kích hoạt. Hệ thống đã gửi lại OTP.",
                    Email = email,
                    OtpExpiredAtUtc = resend.expiredAtUtc
                });
            }

            if (!string.IsNullOrEmpty(phone))
            {
                var existsPhone = await _db.Users.AnyAsync(x => x.Phone == phone, ct);
                if (existsPhone) return Conflict("Phone already exists.");
            }

            var user = new User
            {
                FullName = dto.FullName.Trim(),
                City = dto.City?.Trim(),
                Gender = dto.Gender?.Trim(),
                Email = email,
                Phone = phone,
                Verified = false,
                RatingSingle = 0,
                RatingDouble = 0,
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

            var otpResult = await _userOtpService.CreateOtpAsync(user, ct);
            await _otpEmailService.SendOtpAsync(email, user.FullName, otpResult.otp, ct);

            return Ok(new RegisterResponseDto
            {
                Message = "Đăng ký thành công. Vui lòng kiểm tra email để lấy mã OTP.",
                Email = email,
                OtpExpiredAtUtc = otpResult.expiredAtUtc
            });
        }

        [HttpPost("confirm-otp")]
        public async Task<ActionResult<AuthResponseDto>> ConfirmOtp([FromBody] ConfirmOtpRequestDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Otp))
                return BadRequest("Email and OTP are required.");

            var email = dto.Email.Trim().ToLowerInvariant();
            var otp = dto.Otp.Trim();

            var user = await _db.Users.FirstOrDefaultAsync(x => x.Email == email, ct);
            if (user == null)
                return NotFound("User not found.");

            var otpEntity = await _db.UserOtps
                .Where(x => x.UserId == user.UserId
                            && x.Email == email
                            && !x.IsUsed
                            && x.OtpCode == otp)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (otpEntity == null)
                return BadRequest("OTP không đúng hoặc đã được sử dụng.");

            if (otpEntity.ExpiredAt < DateTime.UtcNow)
                return BadRequest("OTP đã hết hạn.");

            otpEntity.IsUsed = true;
            otpEntity.UsedAt = DateTime.UtcNow;

            user.IsActive = true;
            user.UpdatedAt = DateTime.UtcNow;
            // Verified giữ nguyên false

            await _db.SaveChangesAsync(ct);

            var auth = CreateAuthResponse(user);
            return Ok(auth);
        }

        [HttpPost("resend-otp")]
        public async Task<ActionResult<RegisterResponseDto>> ResendOtp([FromBody] ResendOtpRequestDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Email))
                return BadRequest("Email is required.");

            var email = dto.Email.Trim().ToLowerInvariant();

            var user = await _db.Users.FirstOrDefaultAsync(x => x.Email == email, ct);
            if (user == null)
                return NotFound("User not found.");

            if (user.IsActive)
                return BadRequest("Tài khoản đã được kích hoạt.");

            var otpResult = await _userOtpService.CreateOtpAsync(user, ct);
            await _otpEmailService.SendOtpAsync(email, user.FullName, otpResult.otp, ct);

            return Ok(new RegisterResponseDto
            {
                Message = "OTP mới đã được gửi.",
                Email = email,
                OtpExpiredAtUtc = otpResult.expiredAtUtc
            });
        }

        [HttpPost("login")]
        public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginRequestDto dto, CancellationToken ct)
        {
            var identifier = dto.Identifier?.Trim();
            if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(dto.Password))
                return BadRequest("Identifier and Password are required.");

            var user = await _db.Users.FirstOrDefaultAsync(x =>
                (x.Email != null && x.Email == identifier) ||
                (x.Phone != null && x.Phone == identifier), ct);

            if (user == null) return Unauthorized("Invalid credentials.");

            if (!user.IsActive)
                return Unauthorized("Tài khoản chưa được kích hoạt. Vui lòng xác thực OTP.");

            var ok = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
            if (!ok) return Unauthorized("Invalid credentials.");

            var auth = CreateAuthResponse(user);
            return Ok(auth);
        }

        private AuthResponseDto CreateAuthResponse(User user)
        {
            var jwtSection = _config.GetSection("Jwt");
            var issuer = jwtSection["Issuer"]!;
            var audience = jwtSection["Audience"]!;
            var key = jwtSection["Key"]!;
            var minutes = int.TryParse(jwtSection["AccessTokenMinutes"], out var m) ? m : 120;

            var expires = DateTime.UtcNow.AddMinutes(minutes);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.FullName ?? ""),
                new Claim("uid", user.UserId.ToString()),
            };

            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: expires,
                signingCredentials: creds
            );

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

            return new AuthResponseDto
            {
                AccessToken = tokenString,
                ExpiresAtUtc = expires,
                User = new AuthUserDto
                {
                    UserId = user.UserId,
                    FullName = user.FullName ?? "",
                    Email = user.Email,
                    Phone = user.Phone,
                    Verified = user.Verified,
                    RatingSingle = user.RatingSingle,
                    RatingDouble = user.RatingDouble,
                    AvatarUrl = string.IsNullOrWhiteSpace(user.AvatarUrl)
                        ? null
                        : GetBaseUrl() + user.AvatarUrl
                }
            };
        }
    }
}