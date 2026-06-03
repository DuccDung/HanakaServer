using HanakaServer.Data;
using HanakaServer.Helpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/rating-auth")]
    public class RatingAuthApiController : ControllerBase
    {
        private readonly PickleballDbContext _db;

        public RatingAuthApiController(PickleballDbContext db)
        {
            _db = db;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] RatingPortalLoginDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
                return BadRequest(new { message = "Email và mật khẩu là bắt buộc." });

            var email = dto.Email.Trim().ToLowerInvariant();

            var user = await _db.Users
                .AsNoTracking()
                .Where(x => x.Email != null && x.Email.ToLower() == email && x.IsActive)
                .Select(x => new
                {
                    x.UserId,
                    x.FullName,
                    x.Email,
                    x.PasswordHash,
                    Roles = x.UserRoles
                        .Select(ur => ur.Role.RoleCode)
                        .Distinct()
                        .ToList()
                })
                .FirstOrDefaultAsync();

            if (user == null)
                return BadRequest(new { message = "Tài khoản không tồn tại hoặc đã bị khóa." });

            if (string.IsNullOrWhiteSpace(user.PasswordHash)
                || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            {
                return BadRequest(new { message = "Sai mật khẩu." });
            }

            var canAccess = user.Roles.Any(x =>
                x == RoleCodes.RatingAssessor ||
                x == RoleCodes.Admin);

            if (!canAccess)
                return BadRequest(new { message = "Tài khoản chưa được gán quyền chấm trình." });

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new("UserId", user.UserId.ToString()),
                new("uid", user.UserId.ToString()),
                new(ClaimTypes.Name, user.FullName ?? ""),
                new(ClaimTypes.Email, user.Email ?? "")
            };

            foreach (var role in user.Roles)
                claims.Add(new Claim(ClaimTypes.Role, role));

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = dto.RememberMe,
                    ExpiresUtc = dto.RememberMe
                        ? DateTimeOffset.UtcNow.AddDays(7)
                        : DateTimeOffset.UtcNow.AddHours(8)
                });

            return Ok(new
            {
                ok = true,
                redirectUrl = "/RatingPortal/Dashboard"
            });
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok(new { ok = true, redirectUrl = "/RatingPortal/Login" });
        }

        [Authorize(Roles = $"{RoleCodes.RatingAssessor},{RoleCodes.Admin}")]
        [HttpGet("me")]
        public IActionResult Me()
        {
            var roles = User.FindAll(ClaimTypes.Role)
                .Select(x => x.Value)
                .Distinct()
                .ToList();

            return Ok(new
            {
                userId = User.FindFirst("UserId")?.Value
                    ?? User.FindFirst("uid")?.Value
                    ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                fullName = User.Identity?.Name,
                roles
            });
        }
    }

    public sealed class RatingPortalLoginDto
    {
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
        public bool RememberMe { get; set; }
    }
}
