using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BCrypt.Net;
using HanakaServer.Data;
using HanakaServer.Dtos;
using HanakaServer.Models;
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

        public AuthsController(PickleballDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        [HttpPost("register")]
        public async Task<ActionResult<AuthResponseDto>> Register([FromBody] RegisterRequestDto dto)
        {
            // Validate cơ bản
            if (string.IsNullOrWhiteSpace(dto.FullName))
                return BadRequest("FullName is required.");

            if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 6)
                return BadRequest("Password must be at least 6 characters.");

            if (string.IsNullOrWhiteSpace(dto.Email) && string.IsNullOrWhiteSpace(dto.Phone))
                return BadRequest("Email or Phone is required.");

            var email = dto.Email?.Trim();
            var phone = dto.Phone?.Trim();

            // Check trùng Email/Phone (nếu có)
            if (!string.IsNullOrEmpty(email))
            {
                var existsEmail = await _db.Users.AnyAsync(x => x.Email == email);
                if (existsEmail) return Conflict("Email already exists.");
            }

            if (!string.IsNullOrEmpty(phone))
            {
                var existsPhone = await _db.Users.AnyAsync(x => x.Phone == phone);
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
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password)
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            // (Optional) auto gán role MEMBER
            var memberRoleId = await _db.Roles
                .Where(r => r.RoleCode == "MEMBER")
                .Select(r => (int?)r.RoleId)
                .FirstOrDefaultAsync();

            if (memberRoleId.HasValue)
            {
                _db.UserRoles.Add(new UserRole
                {
                    UserId = user.UserId,
                    RoleId = memberRoleId.Value,
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
            }

            var auth = CreateAuthResponse(user);
            return Ok(auth);
        }

        [HttpPost("login")]
        public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginRequestDto dto)
        {
            var identifier = dto.Identifier?.Trim();
            if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(dto.Password))
                return BadRequest("Identifier and Password are required.");

            var user = await _db.Users.FirstOrDefaultAsync(x =>
                (x.Email != null && x.Email == identifier) ||
                (x.Phone != null && x.Phone == identifier));

            if (user == null) return Unauthorized("Invalid credentials.");
            if (!user.IsActive) return Unauthorized("User is inactive.");

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

            // Nếu muốn nhét roles vào token:
            // (load role codes)
            // NOTE: Nếu không cần thì bỏ để login nhanh hơn
            // var roles = _db.UserRoles
            //     .Where(ur => ur.UserId == user.UserId)
            //     .Select(ur => ur.Role.RoleCode)
            //     .ToList();
            // roles.ForEach(rc => claims.Add(new Claim(ClaimTypes.Role, rc)));

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
                    AvatarUrl =GetBaseUrl() + user.AvatarUrl
                }
            };
        }
        private string GetBaseUrl()
        {
            var baseUrl = "http://192.168.0.101:5062";
            return baseUrl;
        }
    }
}