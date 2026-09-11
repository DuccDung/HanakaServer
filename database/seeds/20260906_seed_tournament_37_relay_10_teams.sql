SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

DECLARE @TournamentId bigint = 37;
DECLARE @TeamCount int = 10;
DECLARE @ExpectedTeamSize int = 6;
DECLARE @Now datetime2(0) = SYSUTCDATETIME();
DECLARE @PasswordHash nvarchar(500) = N'$(RelayPasswordHash)';

IF @PasswordHash = N'REQUIRED' OR LEN(@PasswordHash) < 20
    THROW 51200, N'Phải truyền RelayPasswordHash hợp lệ bằng tùy chọn sqlcmd -v.', 1;

BEGIN TRANSACTION;

DECLARE @ConfiguredTeamSize int;
DECLARE @ExpectedTeams int;

SELECT
    @ConfiguredTeamSize = settings.TeamSize,
    @ExpectedTeams = tournament.ExpectedTeams
FROM dbo.Tournaments AS tournament WITH (UPDLOCK, HOLDLOCK)
JOIN dbo.RelayTournamentSettings AS settings WITH (UPDLOCK, HOLDLOCK)
    ON settings.TournamentId = tournament.TournamentId
WHERE tournament.TournamentId = @TournamentId;

IF @ConfiguredTeamSize IS NULL
    THROW 51201, N'Không tìm thấy giải tiếp sức 37 hoặc giải chưa có cấu hình tiếp sức.', 1;

IF @ConfiguredTeamSize <> @ExpectedTeamSize
    THROW 51202, N'Giải 37 không còn cấu hình đội 6 người; dừng để tránh tạo sai roster.', 1;

DECLARE @ExistingRegistrationCount int =
(
    SELECT COUNT(*)
    FROM dbo.TournamentRegistrations WITH (UPDLOCK, HOLDLOCK)
    WHERE TournamentId = @TournamentId AND IsVirtualTeam = 0
);

IF @ExistingRegistrationCount + @TeamCount > @ExpectedTeams
    THROW 51203, N'Không đủ sức chứa để thêm 10 đội vào giải 37.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.Users WITH (UPDLOCK, HOLDLOCK)
    WHERE ExternalId LIKE N'seed-relay-t37-team%'
       OR Email LIKE N'relay37.team%.member%@hanaka.test'
)
    THROW 51204, N'Đã tồn tại toàn bộ hoặc một phần tài khoản seed cho giải 37.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.TournamentRegistrations WITH (UPDLOCK, HOLDLOCK)
    WHERE ExternalId LIKE N'seed-relay-t37-team%'
)
    THROW 51205, N'Đã tồn tại toàn bộ hoặc một phần đăng ký seed cho giải 37.', 1;

DECLARE @MemberRoleId int =
(
    SELECT RoleId FROM dbo.Roles WHERE RoleCode = 'MEMBER'
);

IF @MemberRoleId IS NULL
    THROW 51206, N'Không tìm thấy role MEMBER.', 1;

DECLARE @SeedMembers table
(
    TeamNo int NOT NULL,
    Position int NOT NULL,
    ExternalId nvarchar(50) NOT NULL,
    FullName nvarchar(150) NOT NULL,
    Gender nvarchar(20) NOT NULL,
    Phone nvarchar(30) NOT NULL,
    Email nvarchar(200) NOT NULL,
    Rating decimal(4,2) NOT NULL,
    PRIMARY KEY (TeamNo, Position)
);

INSERT INTO @SeedMembers (TeamNo, Position, ExternalId, FullName, Gender, Phone, Email, Rating)
SELECT
    teams.TeamNo,
    positions.Position,
    CONCAT(N'seed-relay-t37-team', FORMAT(teams.TeamNo, '00'), N'-member', FORMAT(positions.Position, '00')),
    CONCAT(N'Tiếp sức 37 - Đội ', FORMAT(teams.TeamNo, '00'), N' - VĐV ', FORMAT(positions.Position, '00')),
    CASE WHEN positions.Position % 2 = 0 THEN N'Nữ' ELSE N'Nam' END,
    CONCAT(N'03737', FORMAT(teams.TeamNo, '00'), FORMAT(positions.Position, '000')),
    CONCAT(N'relay37.team', FORMAT(teams.TeamNo, '00'), N'.member', FORMAT(positions.Position, '00'), N'@hanaka.test'),
    CAST(CASE WHEN positions.Position % 2 = 0 THEN 2.00 ELSE 2.50 END AS decimal(4,2))
FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10)) AS teams(TeamNo)
CROSS JOIN (VALUES (1),(2),(3),(4),(5),(6)) AS positions(Position);

IF EXISTS
(
    SELECT 1
    FROM @SeedMembers AS seed
    JOIN dbo.Users AS existing
      ON existing.ExternalId = seed.ExternalId
      OR existing.Email = seed.Email
      OR REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(existing.Phone, N' ', N''), N'-', N''), N'.', N''), N'(', N''), N')', N''), N'+', N'') = seed.Phone
)
    THROW 51207, N'ExternalId, email hoặc số điện thoại dự kiến đã được tài khoản khác sử dụng.', 1;

DECLARE @CreatedUsers table
(
    UserId bigint NOT NULL PRIMARY KEY,
    ExternalId nvarchar(50) NOT NULL UNIQUE
);

INSERT INTO dbo.Users
(
    ExternalId,
    FullName,
    City,
    Gender,
    Verified,
    RatingSingle,
    RatingDouble,
    AvatarUrl,
    Phone,
    Email,
    PasswordHash,
    IsActive,
    CreatedAt,
    UpdatedAt,
    IsHiddenFromChatSearch
)
OUTPUT inserted.UserId, inserted.ExternalId INTO @CreatedUsers (UserId, ExternalId)
SELECT
    seed.ExternalId,
    seed.FullName,
    N'Bắc Ninh',
    seed.Gender,
    1,
    seed.Rating,
    seed.Rating,
    NULL,
    seed.Phone,
    seed.Email,
    @PasswordHash,
    1,
    @Now,
    @Now,
    0
FROM @SeedMembers AS seed;

INSERT INTO dbo.UserRoles (UserId, RoleId, CreatedAt)
SELECT created.UserId, @MemberRoleId, @Now
FROM @CreatedUsers AS created;

DECLARE @RatedByUserId bigint = CASE WHEN EXISTS
(
    SELECT 1 FROM dbo.Users WHERE UserId = 2
) THEN 2 ELSE NULL END;

INSERT INTO dbo.UserRatingHistory
(
    UserId,
    TournamentId,
    RatingSingle,
    RatingDouble,
    RatedByUserId,
    Note,
    RatedAt
)
SELECT
    created.UserId,
    NULL,
    seed.Rating,
    seed.Rating,
    @RatedByUserId,
    N'Dữ liệu tài khoản thi đấu tiếp sức cho giải 37.',
    @Now
FROM @SeedMembers AS seed
JOIN @CreatedUsers AS created ON created.ExternalId = seed.ExternalId;

DECLARE @StartRegIndex int =
(
    SELECT ISNULL(MAX(RegIndex), 0)
    FROM dbo.TournamentRegistrations WITH (UPDLOCK, HOLDLOCK)
    WHERE TournamentId = @TournamentId
);

DECLARE @CreatedRegistrations table
(
    RegistrationId bigint NOT NULL PRIMARY KEY,
    ExternalId nvarchar(50) NOT NULL UNIQUE
);

INSERT INTO dbo.TournamentRegistrations
(
    TournamentId,
    ExternalId,
    RegIndex,
    RegCode,
    RegTimeRaw,
    RegTime,
    Player1Name,
    Player1Avatar,
    Player1Level,
    Player1Verified,
    Player1UserId,
    Player2Name,
    Player2Avatar,
    Player2Level,
    Player2Verified,
    Player2UserId,
    Points,
    BtCode,
    Paid,
    WaitingPair,
    Success,
    CreatedAt,
    PaidAt,
    PaymentAmount,
    IsVirtualTeam,
    VirtualBracketApplicationId
)
OUTPUT inserted.RegistrationId, inserted.ExternalId
    INTO @CreatedRegistrations (RegistrationId, ExternalId)
SELECT
    @TournamentId,
    CONCAT(N'seed-relay-t37-team', FORMAT(teams.TeamNo, '00')),
    @StartRegIndex + teams.TeamNo,
    CONCAT(@TournamentId, N'-', FORMAT(@StartRegIndex + teams.TeamNo, '0000')),
    CONCAT(CONVERT(nvarchar(23), @Now, 126), N'Z'),
    @Now,
    member1.FullName,
    NULL,
    member1.Rating,
    1,
    user1.UserId,
    member2.FullName,
    NULL,
    member2.Rating,
    1,
    user2.UserId,
    0,
    NULL,
    0,
    0,
    1,
    @Now,
    NULL,
    NULL,
    0,
    NULL
FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10)) AS teams(TeamNo)
JOIN @SeedMembers AS member1 ON member1.TeamNo = teams.TeamNo AND member1.Position = 1
JOIN @CreatedUsers AS user1 ON user1.ExternalId = member1.ExternalId
JOIN @SeedMembers AS member2 ON member2.TeamNo = teams.TeamNo AND member2.Position = 2
JOIN @CreatedUsers AS user2 ON user2.ExternalId = member2.ExternalId;

INSERT INTO dbo.RelayTeams
(
    RegistrationId,
    TournamentId,
    TeamName,
    CaptainUserId,
    LineupLockedAtUtc,
    Version
)
SELECT
    registration.RegistrationId,
    @TournamentId,
    CONCAT(N'Đội tiếp sức ', FORMAT(teams.TeamNo, '00')),
    captain.UserId,
    NULL,
    1
FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10)) AS teams(TeamNo)
JOIN @CreatedRegistrations AS registration
  ON registration.ExternalId = CONCAT(N'seed-relay-t37-team', FORMAT(teams.TeamNo, '00'))
JOIN @SeedMembers AS captainSeed ON captainSeed.TeamNo = teams.TeamNo AND captainSeed.Position = 1
JOIN @CreatedUsers AS captain ON captain.ExternalId = captainSeed.ExternalId;

INSERT INTO dbo.RelayTeamMembers
(
    RegistrationId,
    Position,
    UserId,
    DisplayName,
    AvatarUrl
)
SELECT
    registration.RegistrationId,
    seed.Position,
    created.UserId,
    seed.FullName,
    NULL
FROM @SeedMembers AS seed
JOIN @CreatedUsers AS created ON created.ExternalId = seed.ExternalId
JOIN @CreatedRegistrations AS registration
  ON registration.ExternalId = CONCAT(N'seed-relay-t37-team', FORMAT(seed.TeamNo, '00'));

IF (SELECT COUNT(*) FROM @CreatedUsers) <> @TeamCount * @ExpectedTeamSize
    THROW 51208, N'Không tạo đủ 60 tài khoản.', 1;

IF (SELECT COUNT(*) FROM @CreatedRegistrations) <> @TeamCount
    THROW 51209, N'Không tạo đủ 10 đăng ký đội.', 1;

IF EXISTS
(
    SELECT team.RegistrationId
    FROM dbo.RelayTeams AS team
    JOIN @CreatedRegistrations AS registration ON registration.RegistrationId = team.RegistrationId
    LEFT JOIN dbo.RelayTeamMembers AS member ON member.RegistrationId = team.RegistrationId
    GROUP BY team.RegistrationId
    HAVING COUNT(member.Position) <> @ExpectedTeamSize
       OR COUNT(DISTINCT member.UserId) <> @ExpectedTeamSize
)
    THROW 51210, N'Có đội không đủ 6 User ID duy nhất.', 1;

COMMIT TRANSACTION;

SELECT
    registration.RegistrationId,
    tournamentRegistration.RegIndex,
    tournamentRegistration.RegCode,
    team.TeamName,
    team.CaptainUserId,
    COUNT(member.Position) AS MemberCount,
    CAST(tournamentRegistration.Paid AS int) AS Paid,
    team.LineupLockedAtUtc
FROM @CreatedRegistrations AS registration
JOIN dbo.TournamentRegistrations AS tournamentRegistration
    ON tournamentRegistration.RegistrationId = registration.RegistrationId
JOIN dbo.RelayTeams AS team ON team.RegistrationId = registration.RegistrationId
JOIN dbo.RelayTeamMembers AS member ON member.RegistrationId = registration.RegistrationId
GROUP BY
    registration.RegistrationId,
    tournamentRegistration.RegIndex,
    tournamentRegistration.RegCode,
    team.TeamName,
    team.CaptainUserId,
    tournamentRegistration.Paid,
    team.LineupLockedAtUtc
ORDER BY tournamentRegistration.RegIndex;
