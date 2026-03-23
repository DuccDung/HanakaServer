using HanakaServer.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/referee-auth")]
    public class RefereeAuthApiController : ControllerBase
    {
        private readonly PickleballDbContext _db;

        public RefereeAuthApiController(PickleballDbContext db)
        {
            _db = db;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] RefereeLoginDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
                return BadRequest(new { message = "Email và mật khẩu là bắt buộc." });

            var user = await _db.Users
                .AsNoTracking()
                .Where(x => x.Email == dto.Email && x.IsActive)
                .Select(x => new
                {
                    x.UserId,
                    x.FullName,
                    x.Email,
                    x.PasswordHash,
                    Roles = x.UserRoles
                        .Select(r => r.Role.RoleCode)
                        .Distinct()
                        .ToList()
                })
                .FirstOrDefaultAsync();

            if (user == null)
                return BadRequest(new { message = "Tài khoản không tồn tại hoặc đã bị khóa." });

            // TODO: thay bằng hàm verify password thật của bạn
            var passwordOk = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
            if (!passwordOk)
                return BadRequest(new { message = "Sai mật khẩu." });

            //var isReferee = user.Roles.Any(r => r == "REFEREE" || r == "Admin");
            //if (!isReferee)
            //    return BadRequest(new { message = "Tài khoản này không có quyền trọng tài." });

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim("UserId", user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.FullName ?? ""),
                new Claim(ClaimTypes.Email, user.Email ?? "")
            };

            foreach (var role in user.Roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var identity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme
            );

            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = dto.RememberMe
                });

            return Ok(new
            {
                ok = true,
                redirectUrl = "/RefereePortal/Matches"
            });
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok(new { ok = true, redirectUrl = "/RefereePortal/Login" });
        }

        [Authorize(Roles = "REFEREE,Admin")]
        [HttpGet("me")]
        public IActionResult Me()
        {
            return Ok(new
            {
                userId = User.FindFirst("UserId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                fullName = User.Identity?.Name
            });
        }
    }

    public class RefereeLoginDto
    {
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
        public bool RememberMe { get; set; }
    }
}