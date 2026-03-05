namespace HanakaServer.Dtos
{
    public class SetupGroupStageDto
    {
        public string RoundKey { get; set; } = "R1";
        public string? RoundLabel { get; set; }

        // số bảng muốn chia
        public int GroupCount { get; set; } = 4;

        // nếu true: random đội trước khi chia bảng
        public bool ShuffleTeams { get; set; } = true;

        // nếu true: xoá group/standing/match cũ của round này rồi tạo lại
        public bool ResetIfExists { get; set; } = false;

        // nếu true: tự tạo roundKey trong TournamentRounds nếu chưa có
        public bool AutoCreateRoundIfMissing { get; set; } = true;

        // đặt tên bảng: "A,B,C..." hoặc "1,2,3..."
        public string GroupNameMode { get; set; } = "ABC"; // ABC | NUM
    }

    public class SetupGroupStageResultDto
    {
        public string RoundKey { get; set; } = "";
        public int GroupsCreated { get; set; }
        public int TeamsUsed { get; set; }
        public int MatchesCreated { get; set; }
    }
}