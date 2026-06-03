using HanakaServer.Data;
using HanakaServer.Models;
using Microsoft.EntityFrameworkCore;

namespace HanakaServer.Services
{
    public sealed class UserRatingService : IUserRatingService
    {
        private const int MaxNoteLength = 500;

        private readonly PickleballDbContext _db;

        public UserRatingService(PickleballDbContext db)
        {
            _db = db;
        }

        public async Task<UserRatingSnapshot?> GetLatestRatingAsync(long userId, CancellationToken ct = default)
        {
            return await _db.UserRatingHistories
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.RatedAt)
                .ThenByDescending(x => x.RatingHistoryId)
                .Select(x => new UserRatingSnapshot
                {
                    RatingHistoryId = x.RatingHistoryId,
                    UserId = x.UserId,
                    RatingSingle = x.RatingSingle,
                    RatingDouble = x.RatingDouble,
                    RatedByUserId = x.RatedByUserId,
                    RatedByName = x.RatedByUserId == null
                        ? "He thong"
                        : (x.RatedByUser != null ? x.RatedByUser.FullName : null),
                    Note = x.Note,
                    RatedAt = x.RatedAt
                })
                .FirstOrDefaultAsync(ct);
        }

        public async Task<IReadOnlyList<UserRatingSnapshot>> GetRatingHistoryAsync(
            long userId,
            int take = 30,
            CancellationToken ct = default)
        {
            if (take <= 0) take = 30;
            if (take > 100) take = 100;

            return await _db.UserRatingHistories
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.RatedAt)
                .ThenByDescending(x => x.RatingHistoryId)
                .Take(take)
                .Select(x => new UserRatingSnapshot
                {
                    RatingHistoryId = x.RatingHistoryId,
                    UserId = x.UserId,
                    RatingSingle = x.RatingSingle,
                    RatingDouble = x.RatingDouble,
                    RatedByUserId = x.RatedByUserId,
                    RatedByName = x.RatedByUserId == null
                        ? "He thong"
                        : (x.RatedByUser != null ? x.RatedByUser.FullName : null),
                    Note = x.Note,
                    RatedAt = x.RatedAt
                })
                .ToListAsync(ct);
        }

        public async Task<UserRatingUpdateResult?> SetRatingFromHanakaStaffAsync(
            long targetUserId,
            decimal ratingSingle,
            decimal ratingDouble,
            long? staffUserId,
            string staffUserLabel,
            string note,
            CancellationToken ct = default)
        {
            var user = await _db.Users.FirstOrDefaultAsync(x => x.UserId == targetUserId && x.IsActive, ct);
            if (user == null) return null;

            var now = DateTime.UtcNow;
            var single = NormalizeRating(ratingSingle);
            var doubleScore = NormalizeRating(ratingDouble);
            var auditNote = BuildHanakaStaffNote(note, staffUserLabel);

            var history = new UserRatingHistory
            {
                UserId = user.UserId,
                RatingSingle = single,
                RatingDouble = doubleScore,
                RatedByUserId = staffUserId,
                Note = auditNote,
                RatedAt = now
            };

            _db.UserRatingHistories.Add(history);

            // Users.RatingSingle/RatingDouble is only a legacy cache. UserRatingHistory remains the source of truth.
            user.RatingSingle = single;
            user.RatingDouble = doubleScore;
            user.UpdatedAt = now;

            await SyncShadowProfilesAsync(user, single, doubleScore, now, ct);
            await _db.SaveChangesAsync(ct);

            return new UserRatingUpdateResult
            {
                UserId = user.UserId,
                FullName = user.FullName,
                RatingSingle = single,
                RatingDouble = doubleScore,
                History = new UserRatingSnapshot
                {
                    RatingHistoryId = history.RatingHistoryId,
                    UserId = user.UserId,
                    RatingSingle = single,
                    RatingDouble = doubleScore,
                    RatedByUserId = staffUserId,
                    Note = auditNote,
                    RatedAt = now
                }
            };
        }

        private static decimal NormalizeRating(decimal value)
        {
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private static string BuildHanakaStaffNote(string note, string staffUserLabel)
        {
            var trimmedNote = (note ?? "").Trim();
            var staffLabel = string.IsNullOrWhiteSpace(staffUserLabel)
                ? "unknown"
                : staffUserLabel.Trim();

            var audit = $"nhân viên Hanaka userid:{staffLabel}. Ghi chú: {trimmedNote}";
            return audit.Length <= MaxNoteLength ? audit : audit[..MaxNoteLength];
        }

        private async Task SyncShadowProfilesAsync(
            User user,
            decimal ratingSingle,
            decimal ratingDouble,
            DateTime updatedAt,
            CancellationToken ct)
        {
            var userIdText = user.UserId.ToString();

            var coach = await _db.Coaches.FirstOrDefaultAsync(x => x.ExternalId == userIdText, ct);
            if (coach != null)
            {
                coach.FullName = user.FullName;
                coach.City = user.City;
                coach.AvatarUrl = user.AvatarUrl;
                coach.LevelSingle = ratingSingle;
                coach.LevelDouble = ratingDouble;
            }

            var referee = await _db.Referees.FirstOrDefaultAsync(x => x.ExternalId == userIdText, ct);
            if (referee != null)
            {
                referee.FullName = user.FullName;
                referee.City = user.City;
                referee.AvatarUrl = user.AvatarUrl;
                referee.LevelSingle = ratingSingle;
                referee.LevelDouble = ratingDouble;
                referee.UpdatedAt = updatedAt;
            }
        }
    }
}
