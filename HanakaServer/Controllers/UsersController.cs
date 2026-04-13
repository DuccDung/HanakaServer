using HanakaServer.Data;
using HanakaServer.Dtos;
using HanakaServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;

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
            return _config["PublicBaseUrl"] + url;
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

            var user = await _db.Users
                .FirstOrDefaultAsync(x => x.UserId == userId && x.IsActive);

            if (user == null)
                return NotFound(new { message = "User not found." });

            // Nếu user cũ chưa có history thì tự khởi tạo
            await EnsureInitialRatingHistoryAsync(user);

            var latestRating = await _db.UserRatingHistories
                .AsNoTracking()
                .Where(x => x.UserId == user.UserId)
                .OrderByDescending(x => x.RatedAt)
                .ThenByDescending(x => x.RatingHistoryId)
                .Select(x => new
                {
                    x.RatingSingle,
                    x.RatingDouble,
                    x.RatedAt
                })
                .FirstOrDefaultAsync();

            return Ok(new
            {
                user.UserId,
                user.FullName,
                user.Email,
                user.Phone,
                user.Gender,
                City = user.City,
                user.Verified,
                RatingSingle = latestRating?.RatingSingle,
                RatingDouble = latestRating?.RatingDouble,
                RatingUpdatedAt = latestRating?.RatedAt,
                AvatarUrl = ToAbsoluteUrl(user.AvatarUrl),
                user.Bio,
                user.BirthOfDate,
                user.CreatedAt,
                user.UpdatedAt
            });
        }

        // PUT: api/users/me
        [HttpPut("me")]
        public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest req)
        {
            var userId = GetUserIdFromToken();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive);
            if (user == null) return NotFound(new { message = "User not found." });

            var oldFullName = user.FullName;
            var oldAvatarUrl = user.AvatarUrl;

            if (!string.IsNullOrWhiteSpace(req.FullName))
            {
                var name = req.FullName.Trim();
                if (name.Length < 2)
                    return BadRequest(new { message = "FullName must be at least 2 characters." });

                user.FullName = name;
            }

            if (req.Phone != null) user.Phone = req.Phone.Trim();
            if (req.Gender != null) user.Gender = req.Gender.Trim();
            if (req.City != null) user.City = req.City.Trim();
            if (req.Bio != null) user.Bio = req.Bio;
            if (req.BirthOfDate.HasValue) user.BirthOfDate = req.BirthOfDate.Value.Date;

            if (req.AvatarUrl != null)
                user.AvatarUrl = NormalizeAvatarToRelative(req.AvatarUrl);

            user.UpdatedAt = DateTime.UtcNow;

            await SyncTournamentRegistrationsForUserAsync(
                userId,
                oldFullName,
                user.FullName,
                oldAvatarUrl,
                user.AvatarUrl
            );
            await SyncCoachShadowFromUserAsync(user);
            await SyncRefereeShadowFromUserAsync(user);
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

            var relativeUrl = $"/uploads/avatars/{fileName}";

            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive);
            if (user == null) return NotFound(new { message = "User not found." });

            var oldFullName = user.FullName;
            var oldAvatarUrl = user.AvatarUrl;

            user.AvatarUrl = relativeUrl;
            user.UpdatedAt = DateTime.UtcNow;

            await SyncTournamentRegistrationsForUserAsync(
                userId,
                oldFullName,
                user.FullName,
                oldAvatarUrl,
                user.AvatarUrl
            );
            await SyncCoachShadowFromUserAsync(user);
            await SyncRefereeShadowFromUserAsync(user);
            await _db.SaveChangesAsync();

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
                    u.AvatarUrl,
                    LatestRating = _db.UserRatingHistories
                        .Where(r => r.UserId == u.UserId)
                        .OrderByDescending(r => r.RatedAt)
                        .ThenByDescending(r => r.RatingHistoryId)
                        .Select(r => new
                        {
                            r.RatingSingle,
                            r.RatingDouble,
                            r.RatedAt
                        })
                        .FirstOrDefault()
                })
                .ToListAsync();

            // Fix for CS0173, CS8602, and CS8073 in the problematic line
            var mappedItems = items.Select(x => new
            {
                x.UserId,
                x.FullName,
                x.City,
                x.Gender,
                x.Verified,
                RatingSingle = x.LatestRating?.RatingSingle,
                RatingDouble = x.LatestRating?.RatingDouble,
                RatingUpdatedAt = x.LatestRating?.RatedAt, // Use null-conditional operator to handle nullable DateTime
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
            var user = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == id && x.UserId != 2 && x.IsActive);

            if (user == null)
                return NotFound(new { message = "User not found." });

            var latestRating = await _db.UserRatingHistories
                .AsNoTracking()
                .Where(x => x.UserId == user.UserId)
                .OrderByDescending(x => x.RatedAt)
                .ThenByDescending(x => x.RatingHistoryId)
                .Select(x => new
                {
                    x.RatingSingle,
                    x.RatingDouble,
                    x.RatedAt
                })
                .FirstOrDefaultAsync();

            return Ok(new
            {
                user.UserId,
                user.FullName,
                user.Email,
                user.Phone,
                user.Gender,
                user.City,
                user.Verified,
                RatingSingle = latestRating?.RatingSingle,
                RatingDouble = latestRating?.RatingDouble,
                RatingUpdatedAt = latestRating?.RatedAt,
                AvatarUrl = ToAbsoluteUrl(user.AvatarUrl),
                user.Bio,
                user.BirthOfDate,
                user.CreatedAt
            });
        }
        // PUT: api/users/me/self-rating
        [HttpPut("me/self-rating")]
        public async Task<IActionResult> UpdateMySelfRating([FromBody] UpdateSelfRatingRequestDto req)
        {
            var userId = GetUserIdFromToken();

            if (req == null)
                return BadRequest(new { message = "Invalid request." });

            if (req.RatingSingle < 0 || req.RatingSingle > 5)
                return BadRequest(new { message = "RatingSingle must be between 0 and 5." });

            if (req.RatingDouble < 0 || req.RatingDouble > 5)
                return BadRequest(new { message = "RatingDouble must be between 0 and 5." });

            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive);
            if (user == null)
                return NotFound(new { message = "User not found." });

            var now = DateTime.UtcNow;
            var single = Math.Round(req.RatingSingle, 1);
            var @double = Math.Round(req.RatingDouble, 1);

            // Ghi lịch sử mới
            var history = new Models.UserRatingHistory
            {
                UserId = user.UserId,
                RatingSingle = single,
                RatingDouble = @double,
                RatedByUserId = user.UserId, // tự chấm
                Note = "Người chơi tự cập nhật điểm trình.",
                RatedAt = now
            };

            _db.UserRatingHistories.Add(history);

            // vẫn sync qua Users để tương thích code cũ nếu còn nơi khác đang dùng
            user.RatingSingle = single;
            user.RatingDouble = @double;
            user.UpdatedAt = now;
            await SyncCoachShadowFromUserAsync(user, single, @double);
            await SyncRefereeShadowFromUserAsync(user, single, @double);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "Cập nhật điểm tự chấm trình thành công.",
                userId = user.UserId,
                ratingSingle = single,
                ratingDouble = @double,
                ratedAt = now
            });
        }
        // DELETE: api/users/me
        [HttpDelete("me")]
        public async Task<IActionResult> DeleteMe()
        {
            var userId = GetUserIdFromToken();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive);
            if (user == null)
                return NotFound(new { message = "User not found." });

            user.IsActive = false;
            user.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "Tài khoản đã được xóa.",
                userId = user.UserId,
                isActive = user.IsActive,
                updatedAt = user.UpdatedAt
            });
        }
        // GET: api/users/me/rating-history
        [HttpGet("me/rating-history")]
        public async Task<IActionResult> GetMyRatingHistory()
        {
            var userId = GetUserIdFromToken();

            var user = await _db.Users.FirstOrDefaultAsync(x => x.UserId == userId && x.IsActive);
            if (user == null)
                return NotFound(new { message = "User not found." });

            await EnsureInitialRatingHistoryAsync(user);

            var items = await _db.UserRatingHistories
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.RatedAt)
                .ThenByDescending(x => x.RatingHistoryId)
                .Select(x => new
                {
                    x.RatingHistoryId,
                    x.RatingSingle,
                    x.RatingDouble,
                    x.RatedAt,
                    x.Note,
                    x.RatedByUserId,
                    RatedByName = x.RatedByUserId == null ? "Hệ thống" : (x.RatedByUser != null ? x.RatedByUser.FullName : null)
                })
                .ToListAsync();

            return Ok(new
            {
                userId,
                total = items.Count,
                items
            });
        }
        // GET: api/users/{id}/rating-history
        [AllowAnonymous]
        [HttpGet("{id:long}/rating-history")]
        public async Task<IActionResult> GetUserRatingHistory(long id)
        {
            var user = await _db.Users.FirstOrDefaultAsync(x => x.UserId == id && x.IsActive);
            if (user == null)
                return NotFound(new { message = "User not found." });

            await EnsureInitialRatingHistoryAsync(user);

            var items = await _db.UserRatingHistories
                .AsNoTracking()
                .Where(x => x.UserId == id)
                .OrderByDescending(x => x.RatedAt)
                .ThenByDescending(x => x.RatingHistoryId)
                .Select(x => new
                {
                    x.RatingHistoryId,
                    x.RatingSingle,
                    x.RatingDouble,
                    x.RatedAt,
                    x.Note,
                    x.RatedByUserId,
                    RatedByName = x.RatedByUserId == null ? "Hệ thống" : (x.RatedByUser != null ? x.RatedByUser.FullName : null)
                })
                .ToListAsync();

            return Ok(new
            {
                userId = user.UserId,
                user.FullName,
                total = items.Count,
                items
            });
        }
        // GET: api/users/me/rating
        [HttpGet("me/rating")]
        public async Task<IActionResult> GetMyCurrentRating()
        {
            var userId = GetUserIdFromToken();

            var user = await _db.Users.FirstOrDefaultAsync(x => x.UserId == userId && x.IsActive);
            if (user == null)
                return NotFound(new { message = "User not found." });

            await EnsureInitialRatingHistoryAsync(user);

            var latest = await _db.UserRatingHistories
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.RatedAt)
                .ThenByDescending(x => x.RatingHistoryId)
                .Select(x => new
                {
                    x.RatingSingle,
                    x.RatingDouble,
                    x.RatedAt,
                    x.Note,
                    x.RatedByUserId,
                    RatedByName = x.RatedByUserId == null ? "Hệ thống" : (x.RatedByUser != null ? x.RatedByUser.FullName : null)
                })
                .FirstOrDefaultAsync();

            return Ok(new
            {
                userId = user.UserId,
                user.FullName,
                ratingSingle = latest?.RatingSingle,
                ratingDouble = latest?.RatingDouble,
                ratedAt = latest?.RatedAt,
                ratedByUserId = latest?.RatedByUserId,
                ratedByName = latest?.RatedByName,
                note = latest?.Note
            });
        }
        private const string SYSTEM_RATING_NOTE_PREFIX = "Hệ thống khởi tạo điểm trình ban đầu";

        private static bool IsFemale(string? gender)
        {
            if (string.IsNullOrWhiteSpace(gender)) return false;

            var g = gender.Trim().ToLowerInvariant();

            return g == "Nữ"
                || g == "nu"
                || g == "female"
                || g == "f"
                || g == "woman"
                || g == "girl";
        }

        private static (decimal single, decimal @double) GetDefaultInitialRating(string? gender)
        {
            // Nữ = 1.8, Nam = 2.6
            if (IsFemale(gender))
                return (1.8m, 1.8m);

            return (2.6m, 2.6m);
        }
        private async Task SyncRefereeShadowFromUserAsync(
                            User user,
                            decimal? latestSingle = null,
                            decimal? latestDouble = null)
        {
            var referee = await _db.Referees.FirstOrDefaultAsync(x => x.ExternalId == user.UserId.ToString());
            if (referee == null) return;

            referee.FullName = user.FullName;
            referee.City = user.City;
            referee.AvatarUrl = user.AvatarUrl;

            if (latestSingle.HasValue && latestDouble.HasValue)
            {
                referee.LevelSingle = latestSingle.Value;
                referee.LevelDouble = latestDouble.Value;
                referee.UpdatedAt = DateTime.UtcNow;
                return;
            }

            var latestRating = await _db.UserRatingHistories
                .AsNoTracking()
                .Where(x => x.UserId == user.UserId)
                .OrderByDescending(x => x.RatedAt)
                .ThenByDescending(x => x.RatingHistoryId)
                .Select(x => new
                {
                    x.RatingSingle,
                    x.RatingDouble
                })
                .FirstOrDefaultAsync();

            referee.LevelSingle = latestRating?.RatingSingle ?? user.RatingSingle ?? referee.LevelSingle;
            referee.LevelDouble = latestRating?.RatingDouble ?? user.RatingDouble ?? referee.LevelDouble;
            referee.UpdatedAt = DateTime.UtcNow;
        }
        /// <summary>
        /// Tạo lịch sử điểm trình mặc định nếu user chưa có bản ghi nào.
        /// Gọi hàm này ngay sau khi tạo user lần đầu hoặc trước khi đọc rating nếu muốn tự động vá dữ liệu cũ.
        /// </summary>
        private async Task EnsureInitialRatingHistoryAsync(User user)
        {
            var hasAnyHistory = await _db.UserRatingHistories
                .AsNoTracking()
                .AnyAsync(x => x.UserId == user.UserId);

            if (hasAnyHistory) return;

            var (single, @double) = GetDefaultInitialRating(user.Gender);

            var now = DateTime.UtcNow;

            _db.UserRatingHistories.Add(new Models.UserRatingHistory
            {
                UserId = user.UserId,
                RatingSingle = single,
                RatingDouble = @double,
                RatedByUserId = null, // hệ thống chấm
                Note = $"{SYSTEM_RATING_NOTE_PREFIX} theo giới tính: {(string.IsNullOrWhiteSpace(user.Gender) ? "không xác định, mặc định nam" : user.Gender)}.",
                RatedAt = now
            });

            // Giữ đồng bộ bảng Users nếu bạn vẫn muốn cache/legacy
            // Không dùng để đọc nữa, chỉ để tương thích code cũ
            user.RatingSingle = single;
            user.RatingDouble = @double;
            user.UpdatedAt = now;
            await SyncCoachShadowFromUserAsync(user, single, @double);
            await SyncRefereeShadowFromUserAsync(user, single, @double);
            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// Lấy điểm trình mới nhất từ bảng UserRatingHistories.
        /// </summary>
        private async Task<object?> GetLatestRatingAsync(long userId)
        {
            var latest = await _db.UserRatingHistories
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.RatedAt)
                .ThenByDescending(x => x.RatingHistoryId)
                .Select(x => new
                {
                    x.RatingSingle,
                    x.RatingDouble,
                    x.RatedAt,
                    x.Note,
                    x.RatedByUserId
                })
                .FirstOrDefaultAsync();

            return latest;
        }
        // GET: api/users/me/achievements
        [HttpGet("me/achievements")]
        public async Task<IActionResult> GetMyAchievements()
        {
            var userId = GetUserIdFromToken();

            var user = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId && x.IsActive);

            if (user == null)
                return NotFound(new { message = "User not found." });

            var items = await _db.UserAchievements
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .Include(x => x.Tournament)
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.UserAchievementId)
                .ToListAsync();

            return Ok(new
            {
                userId = user.UserId,
                user.FullName,
                total = items.Count,
                items = items.Select(MapAchievementItem)
            });
        }

        // GET: api/users/{id}/achievements
        [AllowAnonymous]
        [HttpGet("{id:long}/achievements")]
        public async Task<IActionResult> GetUserAchievements(long id)
        {
            var user = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == id && x.UserId != 2 && x.IsActive);

            if (user == null)
                return NotFound(new { message = "User not found." });

            var items = await _db.UserAchievements
                .AsNoTracking()
                .Where(x => x.UserId == id)
                .Include(x => x.Tournament)
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.UserAchievementId)
                .ToListAsync();

            return Ok(new
            {
                userId = user.UserId,
                user.FullName,
                total = items.Count,
                items = items.Select(MapAchievementItem)
            });
        }
        private object MapAchievementItem(UserAchievement x)
        {
            var achievementType = (x.AchievementType ?? "").Trim().ToUpperInvariant();

            var rank = achievementType switch
            {
                "FIRST" => 1,
                "SECOND" => 2,
                "THIRD" => 3,
                _ => 0
            };

            var achievementLabel = achievementType switch
            {
                "FIRST" => "Giải Nhất",
                "SECOND" => "Giải Nhì",
                "THIRD" => "Giải Ba",
                _ => x.AchievementType ?? "Thành tích"
            };

            return new
            {
                x.UserAchievementId,
                x.UserId,
                x.TournamentId,
                x.AchievementType,
                AchievementLabel = achievementLabel,
                Rank = rank,
                x.CreatedAt,
                AchievedAt = x.CreatedAt,
                x.Note,

                Tournament = x.Tournament == null ? null : new
                {
                    x.Tournament.TournamentId,
                    x.Tournament.Title,
                    BannerUrl = ToAbsoluteUrl(x.Tournament.BannerUrl),
                    x.Tournament.StartTime,
                    x.Tournament.LocationText,
                    x.Tournament.AreaText,
                    x.Tournament.GameType,
                    x.Tournament.Status
                },

                Title = x.Tournament != null
                    ? x.Tournament.Title
                    : achievementLabel,

                TournamentName = x.Tournament != null
                    ? x.Tournament.Title
                    : null,

                BannerUrl = x.Tournament != null
                    ? ToAbsoluteUrl(x.Tournament.BannerUrl)
                    : null,

                Date = x.Tournament != null && x.Tournament.StartTime.HasValue
                    ? x.Tournament.StartTime
                    : x.CreatedAt
            };
        }
        private async Task SyncTournamentRegistrationsForUserAsync(
         long userId,
         string? oldFullName,
         string? newFullName,
         string? oldAvatarUrl,
         string? newAvatarUrl)
        {
            var regs = await _db.TournamentRegistrations
                .Where(x => x.Player1UserId == userId || x.Player2UserId == userId)
                .ToListAsync();

            if (!regs.Any()) return;

            var newName = newFullName?.Trim();
            var newAvatar = string.IsNullOrWhiteSpace(newAvatarUrl) ? null : newAvatarUrl.Trim();

            foreach (var reg in regs)
            {
                if (reg.Player1UserId == userId)
                {
                    if (!string.IsNullOrWhiteSpace(newName))
                        reg.Player1Name = newName;

                    reg.Player1Avatar = newAvatar;
                }

                if (reg.Player2UserId == userId)
                {
                    if (!string.IsNullOrWhiteSpace(newName))
                        reg.Player2Name = newName;

                    reg.Player2Avatar = newAvatar;
                }
            }
        }
        private async Task SyncCoachShadowFromUserAsync(
        User user,
        decimal? latestSingle = null,
        decimal? latestDouble = null)
            {
                var coach = await _db.Coaches.FirstOrDefaultAsync(x => x.ExternalId == user.UserId.ToString());
                if (coach == null) return;

                coach.FullName = user.FullName;
                coach.City = user.City;
                coach.AvatarUrl = user.AvatarUrl;

                if (latestSingle.HasValue && latestDouble.HasValue)
                {
                    coach.LevelSingle = latestSingle.Value;
                    coach.LevelDouble = latestDouble.Value;
                    return;
                }

                var latestRating = await _db.UserRatingHistories
                    .AsNoTracking()
                    .Where(x => x.UserId == user.UserId)
                    .OrderByDescending(x => x.RatedAt)
                    .ThenByDescending(x => x.RatingHistoryId)
                    .Select(x => new
                    {
                        x.RatingSingle,
                        x.RatingDouble
                    })
                    .FirstOrDefaultAsync();

                coach.LevelSingle = latestRating?.RatingSingle ?? user.RatingSingle ?? coach.LevelSingle;
                coach.LevelDouble = latestRating?.RatingDouble ?? user.RatingDouble ?? coach.LevelDouble;
            }
    }
}