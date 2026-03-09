using HanakaServer.Data;
using HanakaServer.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;

namespace HanakaServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public class UsersController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;

        public UsersController(PickleballDbContext db, IWebHostEnvironment env, IConfiguration config)
        {
            _db = db;
            _env = env;
            _config = config;
        }

        // Helper: lấy userId từ JWT claim "uid"
        private long GetUserIdFromToken()
        {
            var uid = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid) || !long.TryParse(uid, out var userId))
                throw new UnauthorizedAccessException("Invalid token: missing uid.");
            return userId;
        }

        // Helper: convert relative -> absolute để trả response
        private string? ToAbsoluteUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            return _config["AppSettings:PublicBaseUrl"] ?? "" + url;
        }

        // Helper: normalize avatar về relative để lưu DB
        private string? NormalizeAvatarToRelative(string? avatarUrl)
        {
            if (string.IsNullOrWhiteSpace(avatarUrl)) return null;

            avatarUrl = avatarUrl.Trim();

            // relative sẵn
            if (avatarUrl.StartsWith("/")) return avatarUrl;

            // absolute => lấy path
            if (Uri.TryCreate(avatarUrl, UriKind.Absolute, out var uri))
            {
                // /uploads/avatars/x.png
                return uri.PathAndQuery;
            }

            // chuỗi lạ => giữ nguyên (hoặc bạn có thể return null / BadRequest)
            return avatarUrl;
        }

        // GET: api/users/me
        [HttpGet("me")]
        public async Task<IActionResult> GetMe()
        {
            var userId = GetUserIdFromToken();

            var u = await _db.Users
                .AsNoTracking()
                .Where(x => x.UserId == userId && x.IsActive)
                .Select(x => new
                {
                    x.UserId,
                    x.FullName,
                    x.Email,
                    x.Phone,
                    x.Gender,
                    City = x.City,
                    x.Verified,
                    x.RatingSingle,
                    x.RatingDouble,
                    x.AvatarUrl,
                    x.Bio,
                    x.BirthOfDate,
                    x.CreatedAt,
                    x.UpdatedAt
                })
                .FirstOrDefaultAsync();

            if (u == null) return NotFound(new { message = "User not found." });

            return Ok(new
            {
                u.UserId,
                u.FullName,
                u.Email,
                u.Phone,
                u.Gender,
                u.City,
                u.Verified,
                u.RatingSingle,
                u.RatingDouble,
                AvatarUrl = ToAbsoluteUrl(u.AvatarUrl),
                u.Bio,
                u.BirthOfDate,
                u.CreatedAt,
                u.UpdatedAt
            });
        }

        // PUT: api/users/me
        [HttpPut("me")]
        public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest req)
        {
            var userId = GetUserIdFromToken();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive);
            if (user == null) return NotFound(new { message = "User not found." });

            if (!string.IsNullOrWhiteSpace(req.FullName))
            {
                var name = req.FullName.Trim();
                if (name.Length < 2) return BadRequest(new { message = "FullName must be at least 2 characters." });
                user.FullName = name;
            }

            if (req.Phone != null) user.Phone = req.Phone.Trim();
            if (req.Gender != null) user.Gender = req.Gender.Trim();
            if (req.City != null) user.City = req.City.Trim();
            if (req.Bio != null) user.Bio = req.Bio;
            if (req.BirthOfDate.HasValue) user.BirthOfDate = req.BirthOfDate.Value.Date;

            // AvatarUrl: normalize về relative để lưu DB
            if (req.AvatarUrl != null)
                user.AvatarUrl = NormalizeAvatarToRelative(req.AvatarUrl);

            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                user.UserId,
                user.FullName,
                user.Email,
                user.Phone,
                user.Gender,
                City = user.City,
                user.Verified,
                user.RatingSingle,
                user.RatingDouble,
                AvatarUrl = ToAbsoluteUrl(user.AvatarUrl),
                user.Bio,
                user.BirthOfDate,
                user.CreatedAt,
                user.UpdatedAt
            });
        }

        // POST: api/users/me/avatar
        [HttpPost("me/avatar")]
        [RequestSizeLimit(10_000_000)]
        public async Task<IActionResult> UploadAvatar([FromForm] IFormFile file)
        {
            var userId = GetUserIdFromToken();

            if (file == null || file.Length == 0)
                return BadRequest(new { message = "File is required." });

            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowed.Contains(ext))
                return BadRequest(new { message = "Only jpg, jpeg, png, webp are allowed." });

            var uploadsDir = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", "avatars");
            Directory.CreateDirectory(uploadsDir);

            var fileName = $"{userId}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);

            await using (var stream = System.IO.File.Create(filePath))
            {
                await file.CopyToAsync(stream);
            }

            // Lưu relative vào DB
            var relativeUrl = $"/uploads/avatars/{fileName}";

            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive);
            if (user == null) return NotFound(new { message = "User not found." });

            user.AvatarUrl = relativeUrl;
            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            // Trả absolute để client dùng luôn
            return Ok(new
            {
                avatarUrl = ToAbsoluteUrl(relativeUrl)
            });
        }

        [HttpPost("me/change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
        {
            var userId = GetUserIdFromToken();

            if (req == null)
                return BadRequest(new { message = "Invalid request." });

            if (string.IsNullOrWhiteSpace(req.CurrentPassword))
                return BadRequest(new { message = "CurrentPassword is required." });

            if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 8)
                return BadRequest(new { message = "NewPassword must be at least 8 characters." });

            if (req.NewPassword != req.ConfirmPassword)
                return BadRequest(new { message = "ConfirmPassword does not match." });

            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive);
            if (user == null) return NotFound(new { message = "User not found." });

            if (string.IsNullOrWhiteSpace(user.PasswordHash))
                return BadRequest(new { message = "User has no password set." });

            var ok = BCrypt.Net.BCrypt.Verify(req.CurrentPassword, user.PasswordHash);
            if (!ok) return BadRequest(new { message = "Mật khẩu hiện tại không đúng." });

            if (BCrypt.Net.BCrypt.Verify(req.NewPassword, user.PasswordHash))
                return BadRequest(new { message = "Mật khẩu mới không được trùng mật khẩu cũ." });

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(new { message = "Đổi mật khẩu thành công." });
        }

        // GET: api/users/members?query=abc&page=1&pageSize=20
        [AllowAnonymous]
        [HttpGet("members")]
        public async Task<IActionResult> GetMembers([FromQuery] string? query, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            query = query?.Trim();

            var q = _db.Users
                .AsNoTracking()
                .Where(u => u.IsActive)
                .Where(u => u.UserRoles.Any(ur => ur.Role.RoleCode == "MEMBER"));

            if (!string.IsNullOrWhiteSpace(query))
            {
                q = q.Where(u =>
                    u.FullName.Contains(query) ||
                    (u.Phone != null && u.Phone.Contains(query)) ||
                    (u.Email != null && u.Email.Contains(query)) ||
                    (u.City != null && u.City.Contains(query)) ||
                    (u.Gender != null && u.Gender.Contains(query)) ||
                    u.UserId.ToString().Contains(query)
                );
            }

            var total = await q.CountAsync();

            var items = await q
                .OrderByDescending(u => u.Verified)
                .ThenBy(u => u.FullName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new
                {
                    u.UserId,
                    u.FullName,
                    u.City,
                    u.Gender,
                    u.Verified,
                    u.RatingSingle,
                    u.RatingDouble,
                    u.AvatarUrl
                })
                .ToListAsync();

            // Map AvatarUrl => absolute
            var mappedItems = items.Select(x => new
            {
                x.UserId,
                x.FullName,
                x.City,
                x.Gender,
                x.Verified,
                x.RatingSingle,
                x.RatingDouble,
                AvatarUrl = ToAbsoluteUrl(x.AvatarUrl)
            });

            return Ok(new
            {
                page,
                pageSize,
                total,
                items = mappedItems
            });
        }

        // GET: api/users/{id}
        [AllowAnonymous]
        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetUserDetail(long id)
        {
            var u = await _db.Users
                .AsNoTracking()
                .Where(x => x.UserId == id && x.UserId != 2 && x.IsActive)
                .Select(x => new
                {
                    x.UserId,
                    x.FullName,
                    x.Email,
                    x.Phone,
                    x.Gender,
                    x.City,
                    x.Verified,
                    x.RatingSingle,
                    x.RatingDouble,
                    x.AvatarUrl,
                    x.Bio,
                    x.BirthOfDate,
                    x.CreatedAt
                })
                .FirstOrDefaultAsync();

            if (u == null)
                return NotFound(new { message = "User not found." });

            return Ok(new
            {
                u.UserId,
                u.FullName,
                u.Email,
                u.Phone,
                u.Gender,
                u.City,
                u.Verified,
                u.RatingSingle,
                u.RatingDouble,
                AvatarUrl = ToAbsoluteUrl(u.AvatarUrl),
                u.Bio,
                u.BirthOfDate,
                u.CreatedAt
            });
        }
    }
}