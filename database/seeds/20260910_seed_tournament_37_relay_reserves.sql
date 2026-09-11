-- Sample reserve information for the eight existing demo teams in tournament 37.
-- Uses guest entries and existing demo portraits; creates no user accounts or notifications.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET LOCK_TIMEOUT 15000;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

IF DB_NAME() <> N'van17737_ngocanh'
    THROW 51240, N'Script này chỉ dành cho database đã kiểm tra van17737_ngocanh.', 1;

DECLARE @Profiles table (TeamNo int, Position int, DisplayName nvarchar(150), AvatarIndex int,
    PRIMARY KEY (TeamNo, Position));
INSERT @Profiles VALUES
    (1,1,N'Nguyễn Đức Minh',1), (1,2,N'Trần Bảo Châu',2),
    (1,3,N'Lê Tuấn Anh',3), (1,4,N'Phạm Khánh Hòa',4),
    (2,1,N'Vũ Minh Hải',5), (2,2,N'Đinh Ngọc Lan',9),
    (2,3,N'Đoàn Quang Huy',6), (2,4,N'Hoàng Phương Linh',10),
    (3,1,N'Nguyễn Trung Kiên',7), (3,2,N'Lê Hà Anh',14),
    (3,3,N'Phạm Việt Hoàng',8), (3,4,N'Trần Diệu Anh',18),
    (4,1,N'Bùi Đức Long',11), (4,2,N'Đỗ Ngọc Trâm',20),
    (4,3,N'Võ Thanh Bình',12), (4,4,N'Nguyễn Thảo Nhi',21),
    (5,1,N'Hoàng Tuấn Phong',13), (5,2,N'Lê Bảo Trân',23),
    (5,3,N'Trần Mạnh Dũng',15), (5,4,N'Phạm Yến Nhi',24),
    (6,1,N'Đặng Minh Khang',16), (6,2,N'Vũ Khánh Ngân',27),
    (6,3,N'Nguyễn Hữu Phúc',17), (6,4,N'Đinh Thanh Huyền',30),
    (7,1,N'Đoàn Anh Khoa',19), (7,2,N'Hoàng Ngọc Diệp',34),
    (7,3,N'Phạm Quang Minh',22), (7,4,N'Trần Tuệ An',36),
    (8,1,N'Lê Gia Khánh',25), (8,2,N'Nguyễn Minh Thư',38),
    (8,3,N'Bùi Hoàng Phúc',26), (8,4,N'Võ Lan Anh',39);

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @TeamSize int;
    SELECT @TeamSize = TeamSize FROM dbo.RelayTournamentSettings WITH (UPDLOCK,HOLDLOCK) WHERE TournamentId=37;
    IF @TeamSize <> 6 OR @TeamSize IS NULL
        THROW 51241, N'Giải 37 không còn cấu hình đội chính 6 người.', 1;

    DECLARE @Teams table (TeamNo int PRIMARY KEY, RegistrationId bigint UNIQUE);
    INSERT @Teams (TeamNo,RegistrationId)
    SELECT n.TeamNo,t.RegistrationId
    FROM (VALUES(1),(2),(3),(4),(5),(6),(7),(8)) n(TeamNo)
    JOIN dbo.TournamentRegistrations r WITH (UPDLOCK,HOLDLOCK)
      ON r.TournamentId=37 AND r.ExternalId=CONCAT(N'seed-relay-t37-team',RIGHT(CONCAT('0',n.TeamNo),2))
     AND r.IsVirtualTeam=0 AND r.Success=1
    JOIN dbo.RelayTeams t WITH (UPDLOCK,HOLDLOCK) ON t.RegistrationId=r.RegistrationId AND t.TournamentId=37;
    IF (SELECT COUNT(*) FROM @Teams) <> 8
        THROW 51242, N'Danh sách đội mẫu đã thay đổi; cần kiểm tra lại trước khi thêm dự bị.', 1;

    DECLARE @Expected table (RegistrationId bigint, Position int, DisplayName nvarchar(150), AvatarUrl nvarchar(500),
        PRIMARY KEY (RegistrationId,Position));
    INSERT @Expected
    SELECT t.RegistrationId,p.Position,p.DisplayName,
        CONCAT(N'/uploads/avatars/demo/relay37-avatar-sprite-v1.webp#portrait-',p.AvatarIndex)
    FROM @Profiles p JOIN @Teams t ON t.TeamNo=p.TeamNo;

    -- Rerunning may fill missing sample slots, but never replaces an existing reserve.
    IF EXISTS (
        SELECT 1 FROM dbo.RelayTeamReserveMembers r WITH (UPDLOCK,HOLDLOCK)
        JOIN @Expected e ON e.RegistrationId=r.RegistrationId AND e.Position=r.Position
        WHERE r.UserId IS NOT NULL OR r.DisplayName<>e.DisplayName OR ISNULL(r.AvatarUrl,N'')<>e.AvatarUrl
    ) THROW 51243, N'Có dữ liệu dự bị khác trong đội; dừng để tránh ghi đè.', 1;

    DECLARE @Added table (RegistrationId bigint, Position int);
    INSERT dbo.RelayTeamReserveMembers (RegistrationId,Position,UserId,DisplayName,AvatarUrl)
    OUTPUT inserted.RegistrationId,inserted.Position INTO @Added
    SELECT e.RegistrationId,e.Position,NULL,e.DisplayName,e.AvatarUrl
    FROM @Expected e
    WHERE NOT EXISTS (SELECT 1 FROM dbo.RelayTeamReserveMembers r WITH (UPDLOCK,HOLDLOCK)
                      WHERE r.RegistrationId=e.RegistrationId AND r.Position=e.Position);

    -- Prevent an admin form opened before this seed from overwriting the new reserves.
    UPDATE team SET Version=Version+1
    FROM dbo.RelayTeams team JOIN (SELECT DISTINCT RegistrationId FROM @Added) added ON added.RegistrationId=team.RegistrationId;

    DECLARE @InsertedCount int = (SELECT COUNT(*) FROM @Added);
    COMMIT;
    SELECT DB_NAME() AS DatabaseName, 37 AS TournamentId, @InsertedCount AS InsertedReserveCount;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK;
    THROW;
END CATCH;
