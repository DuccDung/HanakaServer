using HanakaServer.Data;
using HanakaServer.Helpers;
using HanakaServer.Models;
using HanakaServer.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers.Admin
{
    [Route("api/admin/coaches")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminCoachesController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly IConfiguration _config;

        private const string SYSTEM_RATING_NOTE_PREFIX = "Hệ thống khởi tạo điểm trình ban đầu";

        public AdminCoachesController(PickleballDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        private string? ToAbsoluteUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            url = url.Trim();

            if (Uri.TryCreate(url, UriKind.Absolute, out _))
                return url;

            var baseUrl = (_config["PublicBaseUrl"] ?? "").TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
                return url;

            if (!url.StartsWith("/"))
                url = "/" + url;

            return $"{baseUrl}{url}";
        }

        private string? NormalizeAvatarToRelative(string? avatarUrl)
        {
            if (string.IsNullOrWhiteSpace(avatarUrl)) return null;

            avatarUrl = avatarUrl.Trim();

            if (avatarUrl.StartsWith("/")) return avatarUrl;

            if (Uri.TryCreate(avatarUrl, UriKind.Absolute, out var uri))
                return uri.PathAndQuery;

            return avatarUrl;
        }

        private static long? ParseExternalUserId(string? externalId)
        {
            if (string.IsNullOrWhiteSpace(externalId)) return null;
            return long.TryParse(externalId.Trim(), out var userId) ? userId : null;
        }

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
            if (IsFemale(gender))
                return (1.8m, 1.8m);

            return (2.3m, 2.3m);
        }

        private async Task<(decimal? RatingSingle, decimal? RatingDouble, DateTime? RatedAt)> GetLatestRatingAsync(long userId)
        {
            var row = await _db.UserRatingHistories
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.RatedAt)
                .ThenByDescending(x => x.RatingHistoryId)
                .Select(x => new
                {
                    x.RatingSingle,
                    x.RatingDouble,
                    x.RatedAt
                })
                .FirstOrDefaultAsync();

            if (row == null) return (null, null, null);

            return (row.RatingSingle, row.RatingDouble, row.RatedAt);
        }

        private async Task<Dictionary<long, (decimal? RatingSingle, decimal? RatingDouble, DateTime? RatedAt)>> GetLatestRatingMapAsync(List<long> userIds)
        {
            if (userIds == null || userIds.Count == 0)
                return new Dictionary<long, (decimal? RatingSingle, decimal? RatingDouble, DateTime? RatedAt)>();

            var rows = await _db.UserRatingHistories
                .AsNoTracking()
                .Where(x => userIds.Contains(x.UserId))
                .OrderByDescending(x => x.RatedAt)
                .ThenByDescending(x => x.RatingHistoryId)
                .Select(x => new
                {
                    x.UserId,
                    x.RatingSingle,
                    x.RatingDouble,
                    x.RatedAt
                })
                .ToListAsync();

            return rows
                .GroupBy(x => x.UserId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var first = g.First();
                        return (
                            RatingSingle: first.RatingSingle,
                            RatingDouble: first.RatingDouble,
                            RatedAt: (DateTime?)first.RatedAt
                        );
                    });
        }

        /// <summary>
        /// Tạo lịch sử điểm mặc định nếu user chưa có.
        /// Đồng thời sync shadow data sang Coach nếu Coach đã tồn tại.
        /// </summary>
        private async Task EnsureInitialRatingHistoryAsync(long userId)
        {
            var user = await _db.Users.FirstOrDefaultAsync(x => x.UserId == userId && x.IsActive);
            if (user == null) return;

            var hasAnyHistory = await _db.UserRatingHistories
                .AsNoTracking()
                .AnyAsync(x => x.UserId == userId);

            if (hasAnyHistory) return;

            var (single, @double) = GetDefaultInitialRating(user.Gender);
            var now = DateTime.UtcNow;

            _db.UserRatingHistories.Add(new UserRatingHistory
            {
                UserId = user.UserId,
                RatingSingle = single,
                RatingDouble = @double,
                RatedByUserId = null,
                Note = $"{SYSTEM_RATING_NOTE_PREFIX} theo giới tính: {(string.IsNullOrWhiteSpace(user.Gender) ? "không xác định, mặc định nam" : user.Gender)}.",
                RatedAt = now
            });

            user.RatingSingle = single;
            user.RatingDouble = @double;
            user.UpdatedAt = now;

            var coach = await _db.Coaches.FirstOrDefaultAsync(x => x.ExternalId == user.UserId.ToString());
            if (coach != null)
            {
                SyncCoachShadowFromUser(coach, user, single, @double);
            }

            await _db.SaveChangesAsync();
        }

        private void SyncCoachShadowFromUser(
            Coach coach,
            User user,
            decimal? latestSingle = null,
            decimal? latestDouble = null)
        {
            var defaults = GetDefaultInitialRating(user.Gender);

            coach.ExternalId = user.UserId.ToString();
            coach.FullName = user.FullName;
            coach.City = user.City;
            coach.AvatarUrl = NormalizeAvatarToRelative(user.AvatarUrl);
            coach.LevelSingle = latestSingle ?? user.RatingSingle ?? defaults.single;
            coach.LevelDouble = latestDouble ?? user.RatingDouble ?? defaults.@double;
        }

        private string? BuildLinkWarning(string? externalId, User? linkedUser)
        {
            if (string.IsNullOrWhiteSpace(externalId))
                return "Coach chưa có ExternalId / UserId.";

            if (!long.TryParse(externalId, out _))
                return "ExternalId hiện tại không phải UserId hợp lệ.";

            if (linkedUser == null)
                return "Không tìm thấy User tương ứng với ExternalId hiện tại.";

            if (!linkedUser.IsActive)
                return "User liên kết đã bị vô hiệu hoá.";

            return null;
        }

        private object MapAchievementItem(UserAchievement x)
        {
            var tournamentType = TournamentTypeHelper.Resolve(x.Tournament?.GameType, x.Tournament?.GenderCategory);
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
                    GenderCategory = tournamentType.GenderCategory,
                    TournamentTypeCode = tournamentType.TournamentTypeCode,
                    TournamentTypeLabel = tournamentType.TournamentTypeLabel,
                    x.Tournament.Status
                },

                Title = x.Tournament != null ? x.Tournament.Title : achievementLabel,
                TournamentName = x.Tournament != null ? x.Tournament.Title : null,
                BannerUrl = x.Tournament != null ? ToAbsoluteUrl(x.Tournament.BannerUrl) : null,
                Date = x.Tournament != null && x.Tournament.StartTime.HasValue
                    ? x.Tournament.StartTime
                    : x.CreatedAt
            };
        }

        private async Task<object?> GetDetailDataAsync(long id)
        {
            var coach = await _db.Coaches
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CoachId == id);

            if (coach == null) return null;

            var parsedUserId = ParseExternalUserId(coach.ExternalId);
            User? user = null;

            if (parsedUserId.HasValue)
            {
                user = await _db.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.UserId == parsedUserId.Value);

                if (user?.IsActive == true)
                {
                    await EnsureInitialRatingHistoryAsync(user.UserId);

                    user = await _db.Users
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.UserId == parsedUserId.Value);
                }
            }

            var latestRating = parsedUserId.HasValue
                ? await GetLatestRatingAsync(parsedUserId.Value)
                : (null, null, null);

            var ratingHistory = new List<object>();
            if (parsedUserId.HasValue)
            {
                var histories = await _db.UserRatingHistories
                    .AsNoTracking()
                    .Where(x => x.UserId == parsedUserId.Value)
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
                        RatedByName = x.RatedByUserId == null
                            ? "Hệ thống"
                            : (x.RatedByUser != null ? x.RatedByUser.FullName : null)
                    })
                    .ToListAsync();

                ratingHistory = histories.Cast<object>().ToList();
            }

            var userAchievements = new List<object>();
            if (parsedUserId.HasValue)
            {
                var items = await _db.UserAchievements
                    .AsNoTracking()
                    .Where(x => x.UserId == parsedUserId.Value)
                    .Include(x => x.Tournament)
                    .OrderByDescending(x => x.CreatedAt)
                    .ThenByDescending(x => x.UserAchievementId)
                    .ToListAsync();

                userAchievements = items.Select(MapAchievementItem).ToList();
            }

            var defaults = user != null
                ? GetDefaultInitialRating(user.Gender)
                : (coach.LevelSingle, coach.LevelDouble);

            var linkWarning = BuildLinkWarning(coach.ExternalId, user);
            return new
            {
                coach.CoachId,
                UserId = user?.UserId,
                coach.ExternalId,

                HasLinkedUser = user != null,
                UserIsActive = user?.IsActive ?? false,
                LinkWarning = linkWarning,

                FullName = user?.FullName ?? coach.FullName,
                City = user?.City ?? coach.City,
                Gender = user?.Gender,

                coach.Verified,
                UserVerified = user?.Verified ?? false,

                LevelSingle = latestRating.RatingSingle ?? user?.RatingSingle,
                LevelDouble = latestRating.RatingDouble ?? user?.RatingDouble,
                RatingUpdatedAt = latestRating.RatedAt,

                AvatarUrl = ToAbsoluteUrl(user?.AvatarUrl ?? coach.AvatarUrl),
                coach.CoachType,

                coach.Introduction,
                coach.TeachingArea,
                coach.Achievements,

                user?.Email,
                user?.Phone,
                user?.Bio,
                user?.BirthOfDate,

                RatingHistory = ratingHistory,
                UserAchievements = userAchievements
            };
        }

        [HttpGet]
        public async Task<IActionResult> GetList(
            [FromQuery] string? keyword,
            [FromQuery] string verified = "ALL",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            page = page <= 0 ? 1 : page;
            pageSize = pageSize <= 0 ? 20 : pageSize;
            pageSize = pageSize > 100 ? 100 : pageSize;

            var q = _db.Coaches
                .AsNoTracking()
                .AsQueryable();

            keyword = (keyword ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                q = q.Where(x =>
                    x.FullName.Contains(keyword) ||
                    (x.City != null && x.City.Contains(keyword)) ||
                    (x.ExternalId != null && x.ExternalId.Contains(keyword)) ||
                    (x.CoachType != null && x.CoachType.Contains(keyword)) ||
                    (x.TeachingArea != null && x.TeachingArea.Contains(keyword)));
            }

            verified = (verified ?? "ALL").Trim().ToUpperInvariant();
            if (verified == "TRUE")
                q = q.Where(x => x.Verified);
            else if (verified == "FALSE")
                q = q.Where(x => !x.Verified);

            var total = await q.CountAsync();

            var coaches = await q
                .OrderByDescending(x => x.CoachId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var parsedUserIds = coaches
                .Select(x => ParseExternalUserId(x.ExternalId))
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();

            var userMap = await _db.Users
                .AsNoTracking()
                .Where(x => parsedUserIds.Contains(x.UserId))
                .ToDictionaryAsync(x => x.UserId, x => x);

            var latestRatingMap = await GetLatestRatingMapAsync(parsedUserIds);

            var items = coaches.Select(x =>
            {
                var parsedUserId = ParseExternalUserId(x.ExternalId);
                userMap.TryGetValue(parsedUserId ?? -1, out var user);
                latestRatingMap.TryGetValue(parsedUserId ?? -1, out var latestRating);

                var defaults = user != null
                    ? GetDefaultInitialRating(user.Gender)
                    : (x.LevelSingle, x.LevelDouble);

                return new
                {
                    x.CoachId,
                    UserId = user?.UserId,
                    x.ExternalId,

                    HasLinkedUser = user != null,
                    UserIsActive = user?.IsActive ?? false,
                    LinkWarning = BuildLinkWarning(x.ExternalId, user),

                    FullName = user?.FullName ?? x.FullName,
                    City = user?.City ?? x.City,
                    Gender = user?.Gender,

                    x.Verified,
                    UserVerified = user?.Verified ?? false,

                    LevelSingle = latestRating.RatingSingle ?? user?.RatingSingle,
                    LevelDouble = latestRating.RatingDouble ?? user?.RatingDouble,
                    RatingUpdatedAt = latestRating.RatedAt,

                    AvatarUrl = ToAbsoluteUrl(user?.AvatarUrl ?? x.AvatarUrl),
                    x.CoachType,

                    x.Introduction,
                    x.TeachingArea,
                    x.Achievements,

                    user?.Email,
                    user?.Phone
                };
            }).ToList();

            return Ok(new
            {
                total,
                page,
                pageSize,
                items
            });
        }

        [HttpGet("lookup-user")]
        public async Task<IActionResult> LookupUser([FromQuery] string? externalId)
        {
            var parsedUserId = ParseExternalUserId(externalId);
            if (!parsedUserId.HasValue)
            {
                return BadRequest(new { message = "ExternalId/UserId không hợp lệ." });
            }

            var user = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == parsedUserId.Value);

            if (user == null)
            {
                return NotFound(new { message = "Không tìm thấy user tương ứng." });
            }

            if (user.IsActive)
            {
                await EnsureInitialRatingHistoryAsync(user.UserId);

                user = await _db.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.UserId == parsedUserId.Value);
            }

            var latestRating = await GetLatestRatingAsync(user!.UserId);

            var achievementsCount = await _db.UserAchievements
                .AsNoTracking()
                .CountAsync(x => x.UserId == user.UserId);

            var existingCoachId = await _db.Coaches
                .AsNoTracking()
                .Where(x => x.ExternalId == user.UserId.ToString())
                .Select(x => (long?)x.CoachId)
                .FirstOrDefaultAsync();

            var defaults = GetDefaultInitialRating(user.Gender);

            return Ok(new
            {
                UserId = user.UserId,
                ExternalId = user.UserId.ToString(),
                user.FullName,
                user.City,
                user.Gender,
                user.Verified,
                user.IsActive,
                AvatarUrl = ToAbsoluteUrl(user.AvatarUrl),
                user.Email,
                user.Phone,
                user.Bio,
                user.BirthOfDate,
                RatingSingle = latestRating.RatingSingle ?? user.RatingSingle ?? defaults.single,
                RatingDouble = latestRating.RatingDouble ?? user.RatingDouble ?? defaults.@double,
                RatingUpdatedAt = latestRating.RatedAt,
                AchievementsCount = achievementsCount,
                ExistingCoachId = existingCoachId
            });
        }

        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetDetail(long id)
        {
            var data = await GetDetailDataAsync(id);
            if (data == null)
                return NotFound(new { message = "Không tìm thấy coach." });

            return Ok(data);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CoachUpsertRequest req)
        {
            if (req == null)
                return BadRequest(new { message = "Dữ liệu không hợp lệ." });

            var parsedUserId = ParseExternalUserId(req.ExternalId);
            if (!parsedUserId.HasValue)
                return BadRequest(new { message = "Vui lòng nhập UserId hợp lệ vào ExternalId." });

            var user = await _db.Users.FirstOrDefaultAsync(x => x.UserId == parsedUserId.Value);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy user tương ứng." });

            if (!user.IsActive)
                return BadRequest(new { message = "User này đang bị vô hiệu hoá, không thể tạo hồ sơ coach." });

            var existedCoach = await _db.Coaches.AnyAsync(x => x.ExternalId == user.UserId.ToString());
            if (existedCoach)
                return BadRequest(new { message = "User này đã có hồ sơ coach." });

            await EnsureInitialRatingHistoryAsync(user.UserId);
            var latestRating = await GetLatestRatingAsync(user.UserId);

            var coachType = string.IsNullOrWhiteSpace(req.CoachType)
                ? "COACH"
                : req.CoachType.Trim().ToUpperInvariant();

            if (coachType.Length > 20)
                coachType = coachType[..20];

            var entity = new Coach
            {
                Verified = req.Verified,
                CoachType = coachType,
                Introduction = string.IsNullOrWhiteSpace(req.Introduction) ? null : req.Introduction.Trim(),
                TeachingArea = string.IsNullOrWhiteSpace(req.TeachingArea) ? null : req.TeachingArea.Trim(),
                Achievements = string.IsNullOrWhiteSpace(req.Achievements) ? null : req.Achievements.Trim()
            };

            SyncCoachShadowFromUser(
                entity,
                user,
                latestRating.RatingSingle,
                latestRating.RatingDouble
            );

            _db.Coaches.Add(entity);
            await _db.SaveChangesAsync();

            return await GetDetail(entity.CoachId);
        }

        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] CoachUpsertRequest req)
        {
            if (req == null)
                return BadRequest(new { message = "Dữ liệu không hợp lệ." });

            var entity = await _db.Coaches.FirstOrDefaultAsync(x => x.CoachId == id);
            if (entity == null)
                return NotFound(new { message = "Không tìm thấy coach." });

            var parsedUserId = ParseExternalUserId(req.ExternalId);
            if (!parsedUserId.HasValue)
                return BadRequest(new { message = "Vui lòng nhập UserId hợp lệ vào ExternalId." });

            var user = await _db.Users.FirstOrDefaultAsync(x => x.UserId == parsedUserId.Value);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy user tương ứng." });

            if (!user.IsActive)
                return BadRequest(new { message = "User này đang bị vô hiệu hoá, không thể liên kết làm coach." });

            var existsExt = await _db.Coaches.AnyAsync(x => x.ExternalId == user.UserId.ToString() && x.CoachId != id);
            if (existsExt)
                return BadRequest(new { message = "User này đã được liên kết với hồ sơ coach khác." });

            await EnsureInitialRatingHistoryAsync(user.UserId);
            var latestRating = await GetLatestRatingAsync(user.UserId);

            entity.Verified = req.Verified;
            entity.CoachType = string.IsNullOrWhiteSpace(req.CoachType)
                ? "COACH"
                : req.CoachType.Trim().ToUpperInvariant();
            entity.Introduction = string.IsNullOrWhiteSpace(req.Introduction) ? null : req.Introduction.Trim();
            entity.TeachingArea = string.IsNullOrWhiteSpace(req.TeachingArea) ? null : req.TeachingArea.Trim();
            entity.Achievements = string.IsNullOrWhiteSpace(req.Achievements) ? null : req.Achievements.Trim();

            SyncCoachShadowFromUser(
                entity,
                user,
                latestRating.RatingSingle,
                latestRating.RatingDouble
            );

            await _db.SaveChangesAsync();

            return await GetDetail(entity.CoachId);
        }

        [HttpPost("{id:long}/toggle-verified")]
        public async Task<IActionResult> ToggleVerified(long id)
        {
            var entity = await _db.Coaches.FirstOrDefaultAsync(x => x.CoachId == id);
            if (entity == null)
                return NotFound(new { message = "Không tìm thấy coach." });

            entity.Verified = !entity.Verified;
            await _db.SaveChangesAsync();

            return await GetDetail(entity.CoachId);
        }

        [HttpPost("{id:long}/sync-from-user")]
        public async Task<IActionResult> SyncFromUser(long id)
        {
            var entity = await _db.Coaches.FirstOrDefaultAsync(x => x.CoachId == id);
            if (entity == null)
                return NotFound(new { message = "Không tìm thấy coach." });

            var parsedUserId = ParseExternalUserId(entity.ExternalId);
            if (!parsedUserId.HasValue)
                return BadRequest(new { message = "ExternalId hiện tại không phải UserId hợp lệ." });

            var user = await _db.Users.FirstOrDefaultAsync(x => x.UserId == parsedUserId.Value);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy user tương ứng." });

            if (!user.IsActive)
                return BadRequest(new { message = "User đang bị vô hiệu hoá, không thể đồng bộ." });

            await EnsureInitialRatingHistoryAsync(user.UserId);
            var latestRating = await GetLatestRatingAsync(user.UserId);

            SyncCoachShadowFromUser(
                entity,
                user,
                latestRating.RatingSingle,
                latestRating.RatingDouble
            );

            await _db.SaveChangesAsync();

            return await GetDetail(entity.CoachId);
        }

        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id)
        {
            var entity = await _db.Coaches.FirstOrDefaultAsync(x => x.CoachId == id);
            if (entity == null)
                return NotFound(new { message = "Không tìm thấy coach." });

            _db.Coaches.Remove(entity);
            await _db.SaveChangesAsync();

            return Ok(new { ok = true });
        }
    }
    public class CoachUpsertRequest
    {
        /// <summary>
        /// userId dạng string, lưu vào Coach.ExternalId
        /// </summary>
        public string? ExternalId { get; set; }

        public bool Verified { get; set; }

        public string? CoachType { get; set; }

        public string? Introduction { get; set; }

        public string? TeachingArea { get; set; }

        public string? Achievements { get; set; }
    }
}
