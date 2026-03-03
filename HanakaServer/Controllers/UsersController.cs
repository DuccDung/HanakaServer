using HanakaServer.Data;
using HanakaServer.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HanakaServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(AuthenticationSchemes = "Bearer")] // bắt JWT
    public class UsersController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly IWebHostEnvironment _env;

        public UsersController(PickleballDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        // Helper: lấy userId từ JWT claim "uid"
        private long GetUserIdFromToken()
        {
            var uid = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(uid) || !long.TryParse(uid, out var userId))
                throw new UnauthorizedAccessException("Invalid token: missing uid.");
            return userId;
        }

        // ✅ GET: api/users/me
        [HttpGet("me")]
        public async Task<IActionResult> GetMe()
        {
            var userId = GetUserIdFromToken();

            var user = await _db.Users
                .AsNoTracking()
                .Where(u => u.UserId == userId && u.IsActive)
                .Select(u => new
                {
                    u.UserId,
                    u.FullName,
                    u.Email,
                    u.Phone,
                    u.Gender,
                    City = u.City,
                    u.Verified,
                    u.RatingSingle,
                    u.RatingDouble,
                    u.AvatarUrl,
                    u.Bio,
                    u.BirthOfDate,
                    u.CreatedAt,
                    u.UpdatedAt
                })
                .FirstOrDefaultAsync();

            if (user == null) return NotFound(new { message = "User not found." });

            return Ok(user);
        }

        // ✅ PUT: api/users/me  (update profile)
        [HttpPut("me")]
        public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest req)
        {
            var userId = GetUserIdFromToken();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive);
            if (user == null) return NotFound(new { message = "User not found." });

            // Validate tối thiểu
            if (!string.IsNullOrWhiteSpace(req.FullName))
            {
                var name = req.FullName.Trim();
                if (name.Length < 2) return BadRequest(new { message = "FullName must be at least 2 characters." });
                user.FullName = name;
            }

            // Optional fields (có thì update, null thì giữ nguyên)
            if (req.Phone != null) user.Phone = req.Phone.Trim();
            if (req.Gender != null) user.Gender = req.Gender.Trim();
            if (req.City != null) user.City = req.City.Trim();
            if (req.Bio != null) user.Bio = req.Bio; // có thể trim nếu bạn muốn
            if (req.BirthOfDate.HasValue) user.BirthOfDate = req.BirthOfDate.Value.Date;

            // Nếu bạn muốn cho phép update avatar bằng URL (không upload file)
            if (req.AvatarUrl != null) user.AvatarUrl = req.AvatarUrl.Trim();

            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            // Trả về user sau update
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
                user.AvatarUrl,
                user.Bio,
                user.BirthOfDate,
                user.CreatedAt,
                user.UpdatedAt
            });
        }

        // ✅ POST: api/users/me/avatar  (upload avatar)
        // form-data: file=<image>
        [HttpPost("me/avatar")]
        [RequestSizeLimit(10_000_000)] // 10MB
        public async Task<IActionResult> UploadAvatar([FromForm] IFormFile file)
        {
            var userId = GetUserIdFromToken();

            if (file == null || file.Length == 0)
                return BadRequest(new { message = "File is required." });

            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowed.Contains(ext))
                return BadRequest(new { message = "Only jpg, jpeg, png, webp are allowed." });

            // Ensure folder
            var uploadsDir = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", "avatars");
            Directory.CreateDirectory(uploadsDir);

            // Generate filename: userId_timestamp.ext
            var fileName = $"{userId}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);

            await using (var stream = System.IO.File.Create(filePath))
            {
                await file.CopyToAsync(stream);
            }

            // Build public URL
            var scheme = Request.Scheme;
            var host = Request.Host.Value;
            var publicUrl = $"{scheme}://{host}/uploads/avatars/{fileName}";

            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive);
            if (user == null) return NotFound(new { message = "User not found." });

            user.AvatarUrl = publicUrl;
            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(new { avatarUrl = publicUrl });
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
    }
}