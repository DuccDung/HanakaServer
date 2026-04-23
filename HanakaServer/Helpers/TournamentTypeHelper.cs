namespace HanakaServer.Helpers
{
    public sealed class TournamentTypeInfo
    {
        public string GameType { get; init; } = "DOUBLE";
        public string GenderCategory { get; init; } = "OPEN";
        public string TournamentTypeCode { get; init; } = "DOUBLE_OPEN";
        public string TournamentTypeLabel { get; init; } = "Hỗn Hợp";
        public bool IsDoubleLike { get; init; }
    }

    public static class TournamentTypeHelper
    {
        public static TournamentTypeInfo Resolve(string? gameType, string? genderCategory)
        {
            var normalizedGameType = NormalizeText(gameType, "DOUBLE");

            if (normalizedGameType == "MIXED")
            {
                return Build("DOUBLE", "MIXED");
            }

            normalizedGameType = normalizedGameType == "SINGLE" ? "SINGLE" : "DOUBLE";

            var normalizedGenderCategory = NormalizeText(genderCategory, "OPEN");
            if (normalizedGenderCategory is not ("OPEN" or "MEN" or "WOMEN" or "MIXED"))
            {
                normalizedGenderCategory = "OPEN";
            }

            if (normalizedGameType == "SINGLE" && normalizedGenderCategory == "MIXED")
            {
                normalizedGenderCategory = "OPEN";
            }

            return Build(normalizedGameType, normalizedGenderCategory);
        }

        public static string NormalizeGameType(string? gameType, string? genderCategory = null)
        {
            return Resolve(gameType, genderCategory).GameType;
        }

        public static bool IsDoubleLike(string? gameType, string? genderCategory = null)
        {
            return Resolve(gameType, genderCategory).IsDoubleLike;
        }

        private static TournamentTypeInfo Build(string gameType, string genderCategory)
        {
            return new TournamentTypeInfo
            {
                GameType = gameType,
                GenderCategory = genderCategory,
                TournamentTypeCode = $"{gameType}_{genderCategory}",
                TournamentTypeLabel = GetLabel(gameType, genderCategory),
                IsDoubleLike = gameType == "DOUBLE"
            };
        }

        private static string GetLabel(string gameType, string genderCategory)
        {
            return (gameType, genderCategory) switch
            {
                ("SINGLE", "MEN") => "Đơn Nam",
                ("SINGLE", "WOMEN") => "Đơn Nữ",
                ("DOUBLE", "MEN") => "Đôi Nam",
                ("DOUBLE", "WOMEN") => "Đôi Nữ",
                ("DOUBLE", "MIXED") => "Đôi Nam Nữ",
                ("SINGLE", _) => "Đơn Mở / Dữ liệu cũ",
                _ => "Hỗn Hợp"
            };
        }

        private static string NormalizeText(string? value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToUpperInvariant();
        }
    }
}
