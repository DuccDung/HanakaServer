namespace HanakaServer.Dtos
{
    public class RoundDto
    {
        public string RoundKey { get; set; } = null!;
        public string RoundLabel { get; set; } = null!;
    }

    public class ScheduleItemDto
    {
        public long ScheduleId { get; set; }
        public long TournamentId { get; set; }
        public string RoundKey { get; set; } = null!;
        public string Code { get; set; } = null!;
        public string? TimeText { get; set; }
        public string? CourtText { get; set; }
        public int? LeftIndex { get; set; }
        public int? RightIndex { get; set; }
        public string? TeamA { get; set; }
        public string? TeamB { get; set; }
        public int ScoreA { get; set; }
        public int ScoreB { get; set; }

        public string? Winner { get; set; } // computed
        public bool IsCompleted { get; set; }
    }

    public class CreateRoundDto
    {
        public string RoundKey { get; set; } = null!;
        public string RoundLabel { get; set; } = null!;
    }

    public class CreateMatchDto
    {
        public string RoundKey { get; set; } = null!;
        public string Code { get; set; } = null!;
        public string? TimeText { get; set; }
        public string? CourtText { get; set; }
        public string? TeamA { get; set; }
        public string? TeamB { get; set; }
        public int? LeftIndex { get; set; }
        public int? RightIndex { get; set; }
    }

    public class UpdateMatchDto
    {
        public string? TimeText { get; set; }
        public string? CourtText { get; set; }
        public string? TeamA { get; set; }
        public string? TeamB { get; set; }
        public int? LeftIndex { get; set; }
        public int? RightIndex { get; set; }
    }

    public class SetScoreDto
    {
        public int ScoreA { get; set; }
        public int ScoreB { get; set; }
    }

    public class AdvanceWinnersDto
    {
        public string FromRoundKey { get; set; } = null!;
        public string ToRoundKey { get; set; } = null!;
        public bool AutoCreateIfMissing { get; set; } = true;
        public bool OverwriteTeams { get; set; } = false;
    }

    public class StandingGroupDto
    {
        public long StandingGroupId { get; set; }
        public long TournamentId { get; set; }
        public string RoundKey { get; set; } = null!;
        public string GroupName { get; set; } = null!;
    }

    public class StandingRowDto
    {
        public long StandingRowId { get; set; }
        public long StandingGroupId { get; set; }
        public string TeamText { get; set; } = null!;
        public int Win { get; set; }
        public int Point { get; set; }
        public int Hso { get; set; }
        public int Rank { get; set; }
        public bool IsTop { get; set; }
    }

    public class CreateStandingGroupDto
    {
        public string RoundKey { get; set; } = null!;
        public string GroupName { get; set; } = null!;
    }

    public class AddStandingTeamDto
    {
        public string TeamText { get; set; } = null!;
    }
}