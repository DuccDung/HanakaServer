namespace HanakaServer.Helpers
{
    public static class MatchSourceTypes
    {
        public const string Registration = "REGISTRATION";
        public const string WinnerMatch = "WINNER_MATCH";
        public const string LoserMatch = "LOSER_MATCH";
        public const string GroupRank = "GROUP_RANK";
        public const string Bye = "BYE";

        public static bool IsMatchSource(string? sourceType)
        {
            var normalized = Normalize(sourceType);
            return normalized == WinnerMatch || normalized == LoserMatch;
        }

        public static string Normalize(string? sourceType)
        {
            var normalized = (sourceType ?? Registration).Trim().ToUpperInvariant();
            return normalized switch
            {
                Registration => Registration,
                WinnerMatch => WinnerMatch,
                LoserMatch => LoserMatch,
                GroupRank => GroupRank,
                Bye => Bye,
                _ => Registration
            };
        }

        public static bool IsValid(string? sourceType)
        {
            var normalized = (sourceType ?? "").Trim().ToUpperInvariant();
            return normalized == Registration
                || normalized == WinnerMatch
                || normalized == LoserMatch
                || normalized == GroupRank
                || normalized == Bye;
        }
    }
}
