using HanakaServer.Models;

namespace HanakaServer.Services
{
    public sealed class UserRatingSnapshot
    {
        public long RatingHistoryId { get; set; }
        public long UserId { get; set; }
        public decimal? RatingSingle { get; set; }
        public decimal? RatingDouble { get; set; }
        public long? RatedByUserId { get; set; }
        public string? RatedByName { get; set; }
        public string? Note { get; set; }
        public DateTime RatedAt { get; set; }
    }

    public sealed class UserRatingUpdateResult
    {
        public long UserId { get; set; }
        public string FullName { get; set; } = "";
        public decimal RatingSingle { get; set; }
        public decimal RatingDouble { get; set; }
        public UserRatingSnapshot History { get; set; } = new();
    }

    public interface IUserRatingService
    {
        Task<UserRatingSnapshot?> GetLatestRatingAsync(long userId, CancellationToken ct = default);

        Task<IReadOnlyList<UserRatingSnapshot>> GetRatingHistoryAsync(
            long userId,
            int take = 30,
            CancellationToken ct = default);

        Task<UserRatingUpdateResult?> SetRatingFromHanakaStaffAsync(
            long targetUserId,
            decimal ratingSingle,
            decimal ratingDouble,
            long? staffUserId,
            string staffUserLabel,
            string note,
            CancellationToken ct = default);
    }
}
