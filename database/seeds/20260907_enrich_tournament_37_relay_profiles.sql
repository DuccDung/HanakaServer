SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

DECLARE @TournamentId bigint = 37;
DECLARE @Now datetime2(7) = SYSUTCDATETIME();
DECLARE @AvatarSprite nvarchar(500) = N'/uploads/avatars/demo/relay37-avatar-sprite-v1.webp';

DECLARE @Profiles table
(
    TeamNo int NOT NULL,
    Position int NOT NULL,
    FullName nvarchar(150) NOT NULL,
    Gender nvarchar(20) NOT NULL,
    AvatarIndex int NOT NULL,
    PRIMARY KEY (TeamNo, Position)
);

INSERT INTO @Profiles (TeamNo, Position, FullName, Gender, AvatarIndex)
VALUES
    (1, 1, N'Nguyễn Minh Quân', N'Nam', 1),
    (1, 2, N'Trần Thu Hà', N'Nữ', 2),
    (1, 3, N'Lê Hoàng Nam', N'Nam', 3),
    (1, 4, N'Phạm Ngọc Anh', N'Nữ', 4),
    (1, 5, N'Võ Đức Huy', N'Nam', 5),
    (1, 6, N'Đặng Quốc Bảo', N'Nam', 6),
    (2, 1, N'Bùi Anh Tuấn', N'Nam', 7),
    (2, 2, N'Đỗ Thành Long', N'Nam', 8),
    (2, 3, N'Hồ Khánh Linh', N'Nữ', 9),
    (2, 4, N'Dương Mai Anh', N'Nữ', 10),
    (2, 5, N'Ngô Văn Phúc', N'Nam', 11),
    (2, 6, N'Nguyễn Trọng Nghĩa', N'Nam', 12),
    (3, 1, N'Trần Gia Hưng', N'Nam', 13),
    (3, 2, N'Lê Thảo Vy', N'Nữ', 14),
    (3, 3, N'Phạm Nhật Minh', N'Nam', 15),
    (3, 4, N'Võ Thanh Tùng', N'Nam', 16),
    (3, 5, N'Đặng Xuân Sơn', N'Nam', 17),
    (3, 6, N'Bùi Bảo Ngọc', N'Nữ', 18),
    (4, 1, N'Đỗ Quang Vinh', N'Nam', 19),
    (4, 2, N'Hồ Diệu Linh', N'Nữ', 20),
    (4, 3, N'Dương Minh Châu', N'Nữ', 21),
    (4, 4, N'Ngô Tuấn Kiệt', N'Nam', 22),
    (4, 5, N'Nguyễn Hải Yến', N'Nữ', 23),
    (4, 6, N'Trần Phương Thảo', N'Nữ', 24),
    (5, 1, N'Lê Công Thành', N'Nam', 25),
    (5, 2, N'Phạm Anh Khoa', N'Nam', 26),
    (5, 3, N'Võ Ngọc Mai', N'Nữ', 27),
    (5, 4, N'Đặng Minh Đức', N'Nam', 28),
    (5, 5, N'Bùi Quốc Khánh', N'Nam', 29),
    (5, 6, N'Đỗ Hà My', N'Nữ', 30),
    (6, 1, N'Hồ Trung Hiếu', N'Nam', 31),
    (6, 2, N'Dương Mạnh Hùng', N'Nam', 32),
    (6, 3, N'Ngô Đức Anh', N'Nam', 33),
    (6, 4, N'Nguyễn Tú Uyên', N'Nữ', 34),
    (6, 5, N'Trần Văn Dũng', N'Nam', 35),
    (6, 6, N'Lê Khánh An', N'Nữ', 36),
    (7, 1, N'Phạm Quốc Việt', N'Nam', 37),
    (7, 2, N'Võ Thanh Trúc', N'Nữ', 38),
    (7, 3, N'Đặng Ngọc Hân', N'Nữ', 39),
    (7, 4, N'Bùi Minh Triết', N'Nam', 40),
    (7, 5, N'Đỗ Nhật Tân', N'Nam', 41),
    (7, 6, N'Hồ Anh Duy', N'Nam', 42),
    (8, 1, N'Dương Quỳnh Như', N'Nữ', 43),
    (8, 2, N'Ngô Hoàng Sơn', N'Nam', 44),
    (8, 3, N'Nguyễn Thành Đạt', N'Nam', 45),
    (8, 4, N'Trần Kim Oanh', N'Nữ', 46),
    (8, 5, N'Lê Bích Ngọc', N'Nữ', 47),
    (8, 6, N'Phạm Minh Tâm', N'Nam', 48),
    (9, 1, N'Võ Quốc Cường', N'Nam', 49),
    (9, 2, N'Đặng Gia Bảo', N'Nam', 50),
    (9, 3, N'Bùi Thành Công', N'Nam', 51),
    (9, 4, N'Đỗ Văn Lâm', N'Nam', 52),
    (9, 5, N'Hồ Ngọc Ánh', N'Nữ', 53),
    (9, 6, N'Dương Đức Thịnh', N'Nam', 54),
    (10, 1, N'Ngô Thùy Dung', N'Nữ', 55),
    (10, 2, N'Nguyễn Quang Hào', N'Nam', 56),
    (10, 3, N'Trần Minh Khang', N'Nam', 57),
    (10, 4, N'Lê Ngọc Huyền', N'Nữ', 58),
    (10, 5, N'Phạm Thu Trang', N'Nữ', 59),
    (10, 6, N'Võ Anh Vũ', N'Nam', 60);

BEGIN TRANSACTION;

IF
(
    SELECT COUNT(*)
    FROM dbo.Users AS users
    JOIN @Profiles AS profile
      ON users.ExternalId = CONCAT(
          N'seed-relay-t37-team', FORMAT(profile.TeamNo, '00'),
          N'-member', FORMAT(profile.Position, '00'))
) <> 60
    THROW 51220, N'Không tìm thấy đủ 60 tài khoản seed của giải 37; không cập nhật một phần dữ liệu.', 1;

UPDATE users
SET
    users.FullName = profile.FullName,
    users.Gender = profile.Gender,
    users.AvatarUrl = CONCAT(@AvatarSprite, N'#portrait-', profile.AvatarIndex),
    users.UpdatedAt = @Now
FROM dbo.Users AS users
JOIN @Profiles AS profile
  ON users.ExternalId = CONCAT(
      N'seed-relay-t37-team', FORMAT(profile.TeamNo, '00'),
      N'-member', FORMAT(profile.Position, '00'));

IF @@ROWCOUNT <> 60
    THROW 51221, N'Không cập nhật đủ 60 hồ sơ tài khoản seed của giải 37.', 1;

UPDATE member
SET
    member.DisplayName = users.FullName,
    member.AvatarUrl = users.AvatarUrl
FROM dbo.RelayTeamMembers AS member
JOIN dbo.RelayTeams AS team
  ON team.RegistrationId = member.RegistrationId
 AND team.TournamentId = @TournamentId
JOIN dbo.Users AS users
  ON users.UserId = member.UserId
JOIN @Profiles AS profile
  ON users.ExternalId = CONCAT(
      N'seed-relay-t37-team', FORMAT(profile.TeamNo, '00'),
      N'-member', FORMAT(profile.Position, '00'));

IF @@ROWCOUNT <> 60
    THROW 51222, N'Không cập nhật đủ 60 snapshot thành viên relay của giải 37.', 1;

UPDATE registration
SET
    registration.Player1Name = COALESCE(player1.FullName, registration.Player1Name),
    registration.Player1Avatar = COALESCE(player1.AvatarUrl, registration.Player1Avatar),
    registration.Player2Name = COALESCE(player2.FullName, registration.Player2Name),
    registration.Player2Avatar = COALESCE(player2.AvatarUrl, registration.Player2Avatar)
FROM dbo.TournamentRegistrations AS registration
LEFT JOIN dbo.Users AS player1
  ON player1.UserId = registration.Player1UserId
LEFT JOIN dbo.Users AS player2
  ON player2.UserId = registration.Player2UserId
WHERE registration.TournamentId = @TournamentId
  AND
  (
      player1.ExternalId LIKE N'seed-relay-t37-team%-member%'
      OR player2.ExternalId LIKE N'seed-relay-t37-team%-member%'
  );

IF EXISTS
(
    SELECT 1
    FROM dbo.Users AS users
    JOIN @Profiles AS profile
      ON users.ExternalId = CONCAT(
          N'seed-relay-t37-team', FORMAT(profile.TeamNo, '00'),
          N'-member', FORMAT(profile.Position, '00'))
    WHERE users.FullName <> profile.FullName
       OR users.AvatarUrl <> CONCAT(@AvatarSprite, N'#portrait-', profile.AvatarIndex)
)
    THROW 51223, N'Dữ liệu hồ sơ seed của giải 37 chưa đồng bộ đầy đủ.', 1;

COMMIT TRANSACTION;

SELECT
    COUNT(*) AS UpdatedProfiles,
    COUNT(DISTINCT profile.TeamNo) AS UpdatedTeams
FROM @Profiles AS profile;
