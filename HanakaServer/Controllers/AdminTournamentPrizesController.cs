using HanakaServer.Data;
using HanakaServer.Models;
using HanakaServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Controllers
{
    [ApiController]
    [Route("api/admin/tournament-prizes")]
    public class AdminTournamentPrizesController : ControllerBase
    {
        private readonly PickleballDbContext _db;
        private readonly TournamentUserNotificationService _tournamentNotificationService;

        public AdminTournamentPrizesController(
            PickleballDbContext db,
            TournamentUserNotificationService tournamentNotificationService)
        {
            _db = db;
            _tournamentNotificationService = tournamentNotificationService;
        }

        [HttpGet("{tournamentId:long}")]
        public async Task<IActionResult> GetSetup(long tournamentId)
        {
            var tournament = await _db.Tournaments
                .AsNoTracking()
                .Where(x => x.TournamentId == tournamentId)
                .Select(x => new
                {
                    x.TournamentId,
                    x.Title,
                    x.Status,
                    x.GameType,
                    x.StartTime,
                    x.LocationText,
                    x.AreaText
                })
                .FirstOrDefaultAsync();

            if (tournament == null)
                return NotFound(new { message = "Không tìm thấy giải đấu." });

            var items = await _db.TournamentPrizes
                .AsNoTracking()
                .Where(x => x.TournamentId == tournamentId)
                .Include(x => x.Registration)
                .OrderBy(x => x.PrizeType == "FIRST" ? 1 : x.PrizeType == "SECOND" ? 2 : 3)
                .ThenBy(x => x.PrizeOrder)
                .Select(x => new TournamentPrizeItemDto
                {
                    TournamentPrizeId = x.TournamentPrizeId,
                    PrizeType = x.PrizeType,
                    PrizeOrder = x.PrizeOrder,
                    RegistrationId = x.RegistrationId,
                    IsConfirmed = x.IsConfirmed,
                    Note = x.Note,
                    Registration = x.Registration == null
                        ? null
                        : new TournamentRegistrationLookupDto
                        {
                            RegistrationId = x.Registration.RegistrationId,
                            RegIndex = x.Registration.RegIndex,
                            RegCode = x.Registration.RegCode,
                            Player1UserId = x.Registration.Player1UserId,
                            Player1Name = x.Registration.Player1Name,
                            Player2UserId = x.Registration.Player2UserId,
                            Player2Name = x.Registration.Player2Name,
                            Points = x.Registration.Points,
                            Paid = x.Registration.Paid,
                            Success = x.Registration.Success
                        }
                })
                .ToListAsync();

            return Ok(new
            {
                tournament,
                items,
                counts = new
                {
                    first = items.Count(x => x.PrizeType == "FIRST"),
                    second = items.Count(x => x.PrizeType == "SECOND"),
                    third = items.Count(x => x.PrizeType == "THIRD")
                },
                isConfirmed = items.Any() && items.All(x => x.IsConfirmed)
            });
        }

        [HttpGet("{tournamentId:long}/registrations")]
        public async Task<IActionResult> SearchRegistrations(
            long tournamentId,
            [FromQuery] string? q,
            [FromQuery] long? registrationId)
        {
            var existsTournament = await _db.Tournaments.AnyAsync(x => x.TournamentId == tournamentId);
            if (!existsTournament)
                return NotFound(new { message = "Không tìm thấy giải đấu." });

            var query = _db.TournamentRegistrations
                .AsNoTracking()
                .Where(x => x.TournamentId == tournamentId);

            if (registrationId.HasValue)
            {
                query = query.Where(x => x.RegistrationId == registrationId.Value);
            }
            else if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();

                if (long.TryParse(q, out var parsedId))
                {
                    query = query.Where(x =>
                        x.RegistrationId == parsedId ||
                        x.RegCode.Contains(q) ||
                        x.Player1Name.Contains(q) ||
                        (x.Player2Name != null && x.Player2Name.Contains(q)));
                }
                else
                {
                    query = query.Where(x =>
                        x.RegCode.Contains(q) ||
                        x.Player1Name.Contains(q) ||
                        (x.Player2Name != null && x.Player2Name.Contains(q)));
                }
            }

            var items = await query
                .OrderBy(x => x.RegIndex)
                .ThenBy(x => x.RegistrationId)
                .Take(100)
                .Select(x => new TournamentRegistrationLookupDto
                {
                    RegistrationId = x.RegistrationId,
                    RegIndex = x.RegIndex,
                    RegCode = x.RegCode,
                    Player1UserId = x.Player1UserId,
                    Player1Name = x.Player1Name,
                    Player2UserId = x.Player2UserId,
                    Player2Name = x.Player2Name,
                    Points = x.Points,
                    Paid = x.Paid,
                    Success = x.Success
                })
                .ToListAsync();

            return Ok(new { items });
        }

        [HttpPost("{tournamentId:long}/draft")]
        public async Task<IActionResult> SaveDraft(long tournamentId, [FromBody] SaveTournamentPrizesRequest request)
        {
            var validate = await ValidateRequest(tournamentId, request, requireAssignedTeam: false);
            if (validate.ErrorResult != null)
                return validate.ErrorResult;

            await using var tx = await _db.Database.BeginTransactionAsync();

            var oldItems = await _db.TournamentPrizes
                .Where(x => x.TournamentId == tournamentId)
                .ToListAsync();

            if (oldItems.Any())
            {
                _db.TournamentPrizes.RemoveRange(oldItems);
                await _db.SaveChangesAsync();
            }

            var newItems = request.Items
                .OrderBy(x => NormalizePrizeType(x.PrizeType) == "FIRST" ? 1 : NormalizePrizeType(x.PrizeType) == "SECOND" ? 2 : 3)
                .ThenBy(x => x.PrizeOrder)
                .Select(x => new TournamentPrize
                {
                    TournamentId = tournamentId,
                    PrizeType = NormalizePrizeType(x.PrizeType),
                    PrizeOrder = x.PrizeOrder,
                    RegistrationId = x.RegistrationId,
                    IsConfirmed = false,
                    Note = string.IsNullOrWhiteSpace(x.Note) ? null : x.Note!.Trim()
                })
                .ToList();

            if (newItems.Any())
            {
                await _db.TournamentPrizes.AddRangeAsync(newItems);
                await _db.SaveChangesAsync();
            }

            await tx.CommitAsync();

            return Ok(new
            {
                message = "Đã lưu nháp giải thưởng.",
                total = newItems.Count
            });
        }
        [HttpPost("{tournamentId:long}/confirm")]
        public async Task<IActionResult> Confirm(long tournamentId, [FromBody] SaveTournamentPrizesRequest request)
        {
            var validate = await ValidateRequest(tournamentId, request, requireAssignedTeam: true);
            if (validate.ErrorResult != null)
                return validate.ErrorResult;

            var tournament = await _db.Tournaments
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TournamentId == tournamentId);

            if (tournament == null)
                return NotFound(new { message = "Không tìm thấy giải đấu." });

            var normalizedItems = (request.Items ?? new List<SaveTournamentPrizeItemRequest>())
                .Select(x => new SaveTournamentPrizeItemRequest
                {
                    PrizeType = NormalizePrizeType(x.PrizeType),
                    PrizeOrder = x.PrizeOrder,
                    RegistrationId = x.RegistrationId,
                    Note = x.Note
                })
                .ToList();

            var registrationIds = normalizedItems
                .Where(x => x.RegistrationId.HasValue)
                .Select(x => x.RegistrationId!.Value)
                .Distinct()
                .ToList();

            var registrationMap = await _db.TournamentRegistrations
                .Where(x => x.TournamentId == tournamentId && registrationIds.Contains(x.RegistrationId))
                .ToDictionaryAsync(x => x.RegistrationId, x => x);

            var gameType = (tournament.GameType ?? string.Empty).Trim().ToUpperInvariant();
            var isDoubleTournament = gameType == "DOUBLE" || gameType == "MIXED";
            var isSingleTournament = gameType == "SINGLE";

            await using var tx = await _db.Database.BeginTransactionAsync();

            // 1) Replace prizes
            var oldPrizes = await _db.TournamentPrizes
                .Where(x => x.TournamentId == tournamentId)
                .ToListAsync();

            if (oldPrizes.Any())
            {
                _db.TournamentPrizes.RemoveRange(oldPrizes);
                await _db.SaveChangesAsync();
            }

            var newPrizes = normalizedItems
                .OrderBy(x => x.PrizeType == "FIRST" ? 1 : x.PrizeType == "SECOND" ? 2 : 3)
                .ThenBy(x => x.PrizeOrder)
                .Select(x => new TournamentPrize
                {
                    TournamentId = tournamentId,
                    PrizeType = x.PrizeType!,
                    PrizeOrder = x.PrizeOrder,
                    RegistrationId = x.RegistrationId,
                    IsConfirmed = true,
                    Note = string.IsNullOrWhiteSpace(x.Note) ? null : x.Note!.Trim()
                })
                .ToList();

            if (newPrizes.Any())
            {
                await _db.TournamentPrizes.AddRangeAsync(newPrizes);
                await _db.SaveChangesAsync();
            }

            // 2) Replace achievements
            var oldAchievements = await _db.UserAchievements
                .Where(x => x.TournamentId == tournamentId)
                .ToListAsync();

            if (oldAchievements.Any())
            {
                _db.UserAchievements.RemoveRange(oldAchievements);
                await _db.SaveChangesAsync();
            }

            var achievements = new List<UserAchievement>();
            var uniqueAchievementKeys = new HashSet<string>();

            foreach (var item in normalizedItems)
            {
                if (!item.RegistrationId.HasValue) continue;
                if (!registrationMap.TryGetValue(item.RegistrationId.Value, out var reg)) continue;

                var userIds = new List<long>();
                if (reg.Player1UserId.HasValue) userIds.Add(reg.Player1UserId.Value);
                if (reg.Player2UserId.HasValue && reg.Player2UserId.Value != reg.Player1UserId)
                    userIds.Add(reg.Player2UserId.Value);

                foreach (var userId in userIds.Distinct())
                {
                    var key = $"{userId}_{tournamentId}_{item.PrizeType}";
                    if (!uniqueAchievementKeys.Add(key)) continue;

                    achievements.Add(new UserAchievement
                    {
                        UserId = userId,
                        TournamentId = tournamentId,
                        AchievementType = item.PrizeType!,
                        CreatedAt = DateTime.Now,
                        Note = BuildAchievementNote(tournament.Title, item)
                    });
                }
            }

            if (achievements.Any())
            {
                await _db.UserAchievements.AddRangeAsync(achievements);
                await _db.SaveChangesAsync();
            }

            // 3) Xóa rating history cũ của chính giải này để tránh cộng lặp
            var oldRatingHistories = await _db.UserRatingHistories
                .Where(x => x.TournamentId == tournamentId)
                .ToListAsync();

            if (oldRatingHistories.Any())
            {
                _db.UserRatingHistories.RemoveRange(oldRatingHistories);
                await _db.SaveChangesAsync();
            }

            // 4) Lấy danh sách user sẽ bị ảnh hưởng
            var allUserIds = normalizedItems
                .Where(x => x.RegistrationId.HasValue)
                .SelectMany(x =>
                {
                    if (!registrationMap.TryGetValue(x.RegistrationId!.Value, out var reg))
                        return Enumerable.Empty<long>();

                    var ids = new List<long>();
                    if (reg.Player1UserId.HasValue) ids.Add(reg.Player1UserId.Value);
                    if (reg.Player2UserId.HasValue && reg.Player2UserId.Value != reg.Player1UserId)
                        ids.Add(reg.Player2UserId.Value);
                    return ids.Distinct();
                })
                .Distinct()
                .ToList();

            // 5) Lấy history gần nhất của từng user, nhưng KHÔNG tính giải hiện tại
            var latestHistories = await _db.UserRatingHistories
                .AsNoTracking()
                .Where(x => allUserIds.Contains(x.UserId) && x.TournamentId != tournamentId)
                .GroupBy(x => x.UserId)
                .Select(g => g
                    .OrderByDescending(x => x.RatedAt)
                    .ThenByDescending(x => x.RatingHistoryId)
                    .FirstOrDefault())
                .ToListAsync();

            var currentRatings = latestHistories
                .Where(x => x != null)
                .ToDictionary(
                    x => x!.UserId,
                    x => new RatingSnapshot
                    {
                        RatingSingle = x!.RatingSingle ?? 0m,
                        RatingDouble = x!.RatingDouble ?? 0m
                    });

            foreach (var userId in allUserIds)
            {
                if (!currentRatings.ContainsKey(userId))
                {
                    currentRatings[userId] = new RatingSnapshot
                    {
                        RatingSingle = 0m,
                        RatingDouble = 0m
                    };
                }
            }

            // 6) Tạo rating histories mới cho lần confirm hiện tại
            var ratingHistories = new List<UserRatingHistory>();

            foreach (var item in normalizedItems)
            {
                if (!item.RegistrationId.HasValue) continue;
                if (!registrationMap.TryGetValue(item.RegistrationId.Value, out var reg)) continue;

                decimal addExp = item.PrizeType switch
                {
                    "FIRST" => 0.15m,
                    "SECOND" => 0.10m,
                    "THIRD" => 0.05m,
                    _ => 0m
                };

                if (addExp <= 0) continue;

                var userIds = new List<long>();
                if (reg.Player1UserId.HasValue) userIds.Add(reg.Player1UserId.Value);
                if (reg.Player2UserId.HasValue && reg.Player2UserId.Value != reg.Player1UserId)
                    userIds.Add(reg.Player2UserId.Value);

                foreach (var userId in userIds.Distinct())
                {
                    var current = currentRatings[userId];

                    decimal newSingle = current.RatingSingle;
                    decimal newDouble = current.RatingDouble;

                    if (isDoubleTournament)
                    {
                        newDouble += addExp;
                    }
                    else if (isSingleTournament)
                    {
                        newSingle += addExp;
                    }
                    else
                    {
                        newSingle += addExp;
                    }

                    current.RatingSingle = newSingle;
                    current.RatingDouble = newDouble;

                    ratingHistories.Add(new UserRatingHistory
                    {
                        UserId = userId,
                        TournamentId = tournamentId,
                        RatingSingle = newSingle,
                        RatingDouble = newDouble,
                        RatedByUserId = 2,
                        Note = BuildRatingNote(tournament.Title, item.PrizeType, addExp, isDoubleTournament),
                        RatedAt = DateTime.Now
                    });
                }
            }

            if (ratingHistories.Any())
            {
                await _db.UserRatingHistories.AddRangeAsync(ratingHistories);
            }

            var affectedUsers = await _db.Users
                .Where(x => allUserIds.Contains(x.UserId))
                .ToListAsync();

            foreach (var user in affectedUsers)
            {
                if (!currentRatings.TryGetValue(user.UserId, out var snapshot))
                {
                    continue;
                }

                user.RatingSingle = snapshot.RatingSingle;
                user.RatingDouble = snapshot.RatingDouble;
                user.UpdatedAt = DateTime.UtcNow;
            }

            if (ratingHistories.Any() || affectedUsers.Any())
            {
                await _db.SaveChangesAsync();
            }

            await tx.CommitAsync();

            try
            {
                await _tournamentNotificationService.NotifyTournamentAwardsAndRatingsAsync(tournamentId);
            }
            catch
            {
                // Notification delivery must not break a prize confirmation that has already committed.
            }

            return Ok(new
            {
                message = "Đã xác nhận giải thưởng, cập nhật thành tích và cộng điểm trình người chơi.",
                totalPrizes = newPrizes.Count,
                totalAchievements = achievements.Count,
                totalRatingHistories = ratingHistories.Count
            });
        }
        private class RatingSnapshot
        {
            public decimal RatingSingle { get; set; }
            public decimal RatingDouble { get; set; }
        }
        private static string BuildRatingNote(string tournamentTitle, string? prizeType, decimal addExp, bool isDoubleTournament)
        {
            var typeLabel = prizeType switch
            {
                "FIRST" => "Giải nhất",
                "SECOND" => "Giải nhì",
                "THIRD" => "Giải ba",
                _ => "Giải thưởng"
            };

            var ratingLabel = isDoubleTournament ? "điểm đôi" : "điểm đơn";

            return $"{tournamentTitle} - {typeLabel}: hệ thống cộng {addExp:0.##} vào {ratingLabel}";
        }
        private async Task<ValidateResult> ValidateRequest(
            long tournamentId,
            SaveTournamentPrizesRequest? request,
            bool requireAssignedTeam)
        {
            if (request == null)
            {
                return ValidateResult.Fail(BadRequest(new { message = "Dữ liệu không hợp lệ." }));
            }

            var existsTournament = await _db.Tournaments.AnyAsync(x => x.TournamentId == tournamentId);
            if (!existsTournament)
            {
                return ValidateResult.Fail(NotFound(new { message = "Không tìm thấy giải đấu." }));
            }

            request.Items ??= new List<SaveTournamentPrizeItemRequest>();

            foreach (var item in request.Items)
            {
                item.PrizeType = NormalizePrizeType(item.PrizeType);
            }

            var allowedTypes = new HashSet<string> { "FIRST", "SECOND", "THIRD" };

            if (request.Items.Any(x => string.IsNullOrWhiteSpace(x.PrizeType) || !allowedTypes.Contains(x.PrizeType!)))
            {
                return ValidateResult.Fail(BadRequest(new { message = "PrizeType chỉ được là FIRST, SECOND, THIRD." }));
            }

            if (request.Items.Any(x => x.PrizeOrder <= 0))
            {
                return ValidateResult.Fail(BadRequest(new { message = "PrizeOrder phải lớn hơn 0." }));
            }

            var duplicatedSlot = request.Items
                .GroupBy(x => new { x.PrizeType, x.PrizeOrder })
                .FirstOrDefault(g => g.Count() > 1);

            if (duplicatedSlot != null)
            {
                return ValidateResult.Fail(BadRequest(new
                {
                    message = $"Bị trùng slot giải thưởng: {duplicatedSlot.Key.PrizeType} - #{duplicatedSlot.Key.PrizeOrder}."
                }));
            }

            if (requireAssignedTeam && request.Items.Any(x => !x.RegistrationId.HasValue))
            {
                return ValidateResult.Fail(BadRequest(new
                {
                    message = "Khi xác nhận, tất cả slot giải thưởng phải được gán đội."
                }));
            }

            var assignedIds = request.Items
                .Where(x => x.RegistrationId.HasValue)
                .Select(x => x.RegistrationId!.Value)
                .ToList();

            var duplicatedReg = assignedIds
                .GroupBy(x => x)
                .FirstOrDefault(g => g.Count() > 1);

            if (duplicatedReg != null)
            {
                return ValidateResult.Fail(BadRequest(new
                {
                    message = $"Đội #{duplicatedReg.Key} đang bị gán cho nhiều slot giải thưởng."
                }));
            }

            if (assignedIds.Any())
            {
                var validIds = await _db.TournamentRegistrations
                    .Where(x => x.TournamentId == tournamentId && assignedIds.Contains(x.RegistrationId))
                    .Select(x => x.RegistrationId)
                    .ToListAsync();

                var invalidId = assignedIds.FirstOrDefault(x => !validIds.Contains(x));
                if (invalidId != 0)
                {
                    return ValidateResult.Fail(BadRequest(new
                    {
                        message = $"Đội #{invalidId} không thuộc giải đấu này hoặc không tồn tại."
                    }));
                }
            }

            return ValidateResult.Ok();
        }

        private static string NormalizePrizeType(string? value)
        {
            return (value ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static string BuildAchievementNote(string tournamentTitle, SaveTournamentPrizeItemRequest item)
        {
            var typeLabel = item.PrizeType switch
            {
                "FIRST" => "Giải nhất",
                "SECOND" => "Giải nhì",
                "THIRD" => "Giải ba",
                _ => item.PrizeType ?? "Giải thưởng"
            };

            var baseText = $"{tournamentTitle} ({typeLabel})";

            return string.IsNullOrWhiteSpace(item.Note)
                ? baseText
                : $"{baseText} - {item.Note!.Trim()}";
        }
    }

    public class SaveTournamentPrizesRequest
    {
        public List<SaveTournamentPrizeItemRequest> Items { get; set; } = new();
    }

    public class SaveTournamentPrizeItemRequest
    {
        public string? PrizeType { get; set; }
        public int PrizeOrder { get; set; }
        public long? RegistrationId { get; set; }
        public string? Note { get; set; }
    }

    public class TournamentPrizeItemDto
    {
        public long TournamentPrizeId { get; set; }
        public string PrizeType { get; set; } = null!;
        public int PrizeOrder { get; set; }
        public long? RegistrationId { get; set; }
        public bool IsConfirmed { get; set; }
        public string? Note { get; set; }
        public TournamentRegistrationLookupDto? Registration { get; set; }
    }

    public class TournamentRegistrationLookupDto
    {
        public long RegistrationId { get; set; }
        public int RegIndex { get; set; }
        public string RegCode { get; set; } = null!;
        public long? Player1UserId { get; set; }
        public string Player1Name { get; set; } = null!;
        public long? Player2UserId { get; set; }
        public string? Player2Name { get; set; }
        public decimal Points { get; set; }
        public bool Paid { get; set; }
        public bool Success { get; set; }
    }

    public class ValidateResult
    {
        public IActionResult? ErrorResult { get; set; }

        public static ValidateResult Ok() => new ValidateResult();

        public static ValidateResult Fail(IActionResult result) => new ValidateResult
        {
            ErrorResult = result
        };
    }
}
