/*
    Hanaka Sport - Bracket Template schema
    Target: Microsoft SQL Server / SSMS
    Revision: 2 - tách các lệnh dùng cột vừa thêm sang dynamic SQL

    Mục đích:
    - Tạo cấu trúc lưu Bracket Template có version.
    - Lưu lần áp dụng template vào giải và snapshot seed.
    - Liên kết các vòng/bảng/trận runtime với lần áp dụng.
    - Bổ sung trạng thái kết thúc trận do BYE.
    - Bổ sung thông tin khóa đăng ký trước khi áp dụng bracket.

    Lưu ý trước khi chạy:
    1. Chọn đúng database trong SSMS hoặc bỏ comment dòng USE bên dưới.
    2. Backup database trước khi chạy trên production.
    3. Script không xóa dữ liệu và không sửa dữ liệu vòng/bảng/trận hiện có.
    4. Script có transaction và XACT_ABORT; lỗi sẽ rollback toàn bộ lần chạy.
    5. Có thể chạy lại sau khi đã chạy thành công. Nếu phát hiện chỉ tồn tại một
       phần trong 8 bảng mới, script sẽ dừng để tránh che giấu schema dở dang.
    6. Nếu lần chạy trước báo lỗi và query window vẫn mở, hãy kiểm tra
       SELECT @@TRANCOUNT. Nếu kết quả lớn hơn 0, ROLLBACK transaction của lần
       chạy lỗi trước khi chạy lại Revision 2.
*/

-- USE [YourDatabaseName];

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF @@TRANCOUNT <> 0
    THROW 51007, N'Session SSMS đang có transaction chưa đóng. Hãy COMMIT hoặc ROLLBACK transaction đó trước khi chạy script.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    --------------------------------------------------------------------------
    -- 0. Preflight
    --------------------------------------------------------------------------
    IF OBJECT_ID(N'dbo.Tournaments', N'U') IS NULL
        THROW 51000, N'Không tìm thấy bảng dbo.Tournaments. Hãy chọn đúng database.', 1;

    IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
        THROW 51001, N'Không tìm thấy bảng dbo.Users. Hãy chọn đúng database.', 1;

    IF OBJECT_ID(N'dbo.TournamentRegistrations', N'U') IS NULL
        THROW 51002, N'Không tìm thấy bảng dbo.TournamentRegistrations. Hãy chọn đúng database.', 1;

    IF OBJECT_ID(N'dbo.TournamentRoundMaps', N'U') IS NULL
        THROW 51003, N'Không tìm thấy bảng dbo.TournamentRoundMaps. Hãy chọn đúng database.', 1;

    IF OBJECT_ID(N'dbo.TournamentRoundGroups', N'U') IS NULL
        THROW 51004, N'Không tìm thấy bảng dbo.TournamentRoundGroups. Hãy chọn đúng database.', 1;

    IF OBJECT_ID(N'dbo.TournamentGroupMatches', N'U') IS NULL
        THROW 51005, N'Không tìm thấy bảng dbo.TournamentGroupMatches. Hãy chọn đúng database.', 1;

    DECLARE @ExistingBracketTableCount int =
          CASE WHEN OBJECT_ID(N'dbo.BracketTemplates', N'U') IS NOT NULL THEN 1 ELSE 0 END
        + CASE WHEN OBJECT_ID(N'dbo.BracketTemplateVersions', N'U') IS NOT NULL THEN 1 ELSE 0 END
        + CASE WHEN OBJECT_ID(N'dbo.BracketTemplateRounds', N'U') IS NOT NULL THEN 1 ELSE 0 END
        + CASE WHEN OBJECT_ID(N'dbo.BracketTemplateGroups', N'U') IS NOT NULL THEN 1 ELSE 0 END
        + CASE WHEN OBJECT_ID(N'dbo.BracketTemplateMatches', N'U') IS NOT NULL THEN 1 ELSE 0 END
        + CASE WHEN OBJECT_ID(N'dbo.BracketTemplateMatchSlots', N'U') IS NOT NULL THEN 1 ELSE 0 END
        + CASE WHEN OBJECT_ID(N'dbo.TournamentBracketApplications', N'U') IS NOT NULL THEN 1 ELSE 0 END
        + CASE WHEN OBJECT_ID(N'dbo.TournamentBracketSeedAssignments', N'U') IS NOT NULL THEN 1 ELSE 0 END;

    IF @ExistingBracketTableCount NOT IN (0, 8)
        THROW 51006, N'Phát hiện schema Bracket Template chỉ tồn tại một phần. Hãy kiểm tra thủ công trước khi chạy lại.', 1;

    --------------------------------------------------------------------------
    -- 1. Tạo 8 bảng mới nếu chưa tồn tại
    --------------------------------------------------------------------------
    IF @ExistingBracketTableCount = 0
    BEGIN
        ----------------------------------------------------------------------
        -- 1.1. BracketTemplates
        ----------------------------------------------------------------------
        CREATE TABLE dbo.BracketTemplates
        (
            BracketTemplateId          bigint IDENTITY(1, 1) NOT NULL,
            TemplateCode               varchar(50) NOT NULL,
            TemplateName               nvarchar(150) NOT NULL,
            [Description]              nvarchar(1000) NULL,
            FormatType                 varchar(30) NOT NULL,
            [Status]                   varchar(20) NOT NULL
                CONSTRAINT DF_BracketTemplates_Status DEFAULT ('DRAFT'),
            CurrentPublishedVersionId  bigint NULL,
            CreatedByUserId            bigint NULL,
            UpdatedByUserId            bigint NULL,
            CreatedAt                  datetime2(0) NOT NULL
                CONSTRAINT DF_BracketTemplates_CreatedAt DEFAULT (SYSDATETIME()),
            UpdatedAt                  datetime2(0) NULL,
            [RowVersion]               rowversion NOT NULL,

            CONSTRAINT PK_BracketTemplates
                PRIMARY KEY CLUSTERED (BracketTemplateId),

            CONSTRAINT CK_BracketTemplates_FormatType
                CHECK (FormatType IN
                (
                    'SINGLE_ELIMINATION',
                    'GROUP_KNOCKOUT',
                    'DOUBLE_ELIMINATION',
                    'CUSTOM'
                )),

            CONSTRAINT CK_BracketTemplates_Status
                CHECK ([Status] IN ('DRAFT', 'PUBLISHED', 'ARCHIVED')),

            CONSTRAINT FK_BracketTemplates_CreatedByUser
                FOREIGN KEY (CreatedByUserId)
                REFERENCES dbo.Users (UserId),

            CONSTRAINT FK_BracketTemplates_UpdatedByUser
                FOREIGN KEY (UpdatedByUserId)
                REFERENCES dbo.Users (UserId)
        );

        CREATE UNIQUE INDEX UX_BracketTemplates_TemplateCode
            ON dbo.BracketTemplates (TemplateCode);

        CREATE INDEX IX_BracketTemplates_Status_FormatType
            ON dbo.BracketTemplates ([Status], FormatType)
            INCLUDE (TemplateName, CurrentPublishedVersionId, UpdatedAt);

        ----------------------------------------------------------------------
        -- 1.2. BracketTemplateVersions
        ----------------------------------------------------------------------
        CREATE TABLE dbo.BracketTemplateVersions
        (
            BracketTemplateVersionId   bigint IDENTITY(1, 1) NOT NULL,
            BracketTemplateId          bigint NOT NULL,
            VersionNumber              int NOT NULL,
            [Status]                   varchar(20) NOT NULL
                CONSTRAINT DF_BracketTemplateVersions_Status DEFAULT ('DRAFT'),
            MinimumTeams               int NOT NULL
                CONSTRAINT DF_BracketTemplateVersions_MinimumTeams DEFAULT (2),
            SeedCapacity               int NOT NULL,
            AllowBye                   bit NOT NULL
                CONSTRAINT DF_BracketTemplateVersions_AllowBye DEFAULT (0),
            DefaultSeedingMethod       varchar(30) NOT NULL
                CONSTRAINT DF_BracketTemplateVersions_Seeding DEFAULT ('REGISTRATION_ORDER'),
            ConfigurationHash          varchar(64) NULL,
            DraftGraphJson             nvarchar(max) NULL,
            CreatedByUserId            bigint NULL,
            PublishedByUserId          bigint NULL,
            CreatedAt                  datetime2(0) NOT NULL
                CONSTRAINT DF_BracketTemplateVersions_CreatedAt DEFAULT (SYSDATETIME()),
            UpdatedAt                  datetime2(0) NULL,
            PublishedAt                datetime2(0) NULL,
            [RowVersion]               rowversion NOT NULL,

            CONSTRAINT PK_BracketTemplateVersions
                PRIMARY KEY CLUSTERED (BracketTemplateVersionId),

            CONSTRAINT FK_BracketTemplateVersions_Template
                FOREIGN KEY (BracketTemplateId)
                REFERENCES dbo.BracketTemplates (BracketTemplateId)
                ON DELETE CASCADE,

            CONSTRAINT FK_BracketTemplateVersions_CreatedByUser
                FOREIGN KEY (CreatedByUserId)
                REFERENCES dbo.Users (UserId),

            CONSTRAINT FK_BracketTemplateVersions_PublishedByUser
                FOREIGN KEY (PublishedByUserId)
                REFERENCES dbo.Users (UserId),

            CONSTRAINT CK_BracketTemplateVersions_VersionNumber
                CHECK (VersionNumber > 0),

            CONSTRAINT CK_BracketTemplateVersions_Status
                CHECK ([Status] IN ('DRAFT', 'PUBLISHED', 'ARCHIVED')),

            CONSTRAINT CK_BracketTemplateVersions_TeamCapacity
                CHECK
                (
                    MinimumTeams >= 2
                    AND SeedCapacity >= MinimumTeams
                    AND SeedCapacity <= 1024
                ),

            CONSTRAINT CK_BracketTemplateVersions_SeedingMethod
                CHECK (DefaultSeedingMethod IN
                (
                    'REGISTRATION_ORDER',
                    'RANDOM',
                    'MANUAL',
                    'RANKING'
                )),

            CONSTRAINT CK_BracketTemplateVersions_PublishState
                CHECK
                (
                    ([Status] = 'PUBLISHED' AND PublishedAt IS NOT NULL)
                    OR ([Status] <> 'PUBLISHED')
                )
        );

        CREATE UNIQUE INDEX UX_BracketTemplateVersions_Template_Version
            ON dbo.BracketTemplateVersions (BracketTemplateId, VersionNumber);

        CREATE UNIQUE INDEX UX_BracketTemplateVersions_Id_Template
            ON dbo.BracketTemplateVersions
            (
                BracketTemplateVersionId,
                BracketTemplateId
            );

        CREATE UNIQUE INDEX UX_BracketTemplateVersions_OneDraft
            ON dbo.BracketTemplateVersions (BracketTemplateId)
            WHERE [Status] = 'DRAFT';

        CREATE INDEX IX_BracketTemplateVersions_Template_Status
            ON dbo.BracketTemplateVersions (BracketTemplateId, [Status], VersionNumber DESC);

        ----------------------------------------------------------------------
        -- 1.3. BracketTemplateRounds
        ----------------------------------------------------------------------
        CREATE TABLE dbo.BracketTemplateRounds
        (
            BracketTemplateRoundId     bigint IDENTITY(1, 1) NOT NULL,
            BracketTemplateVersionId   bigint NOT NULL,
            RoundKey                   varchar(20) NOT NULL,
            RoundLabel                 nvarchar(50) NOT NULL,
            RoundType                  varchar(30) NOT NULL,
            SortOrder                  int NOT NULL
                CONSTRAINT DF_BracketTemplateRounds_SortOrder DEFAULT (0),
            CreatedAt                  datetime2(0) NOT NULL
                CONSTRAINT DF_BracketTemplateRounds_CreatedAt DEFAULT (SYSDATETIME()),
            UpdatedAt                  datetime2(0) NULL,

            CONSTRAINT PK_BracketTemplateRounds
                PRIMARY KEY CLUSTERED (BracketTemplateRoundId),

            CONSTRAINT FK_BracketTemplateRounds_Version
                FOREIGN KEY (BracketTemplateVersionId)
                REFERENCES dbo.BracketTemplateVersions (BracketTemplateVersionId)
                ON DELETE CASCADE,

            CONSTRAINT CK_BracketTemplateRounds_RoundType
                CHECK (RoundType IN
                (
                    'GROUP_STAGE',
                    'KNOCKOUT',
                    'FINAL',
                    'PLACEMENT',
                    'LOSER_BRACKET'
                ))
        );

        CREATE UNIQUE INDEX UX_BracketTemplateRounds_Version_RoundKey
            ON dbo.BracketTemplateRounds (BracketTemplateVersionId, RoundKey);

        CREATE UNIQUE INDEX UX_BracketTemplateRounds_Id_Version
            ON dbo.BracketTemplateRounds
            (
                BracketTemplateRoundId,
                BracketTemplateVersionId
            );

        CREATE INDEX IX_BracketTemplateRounds_Version_SortOrder
            ON dbo.BracketTemplateRounds (BracketTemplateVersionId, SortOrder, RoundKey);

        ----------------------------------------------------------------------
        -- 1.4. BracketTemplateGroups
        ----------------------------------------------------------------------
        CREATE TABLE dbo.BracketTemplateGroups
        (
            BracketTemplateGroupId     bigint IDENTITY(1, 1) NOT NULL,
            BracketTemplateVersionId   bigint NOT NULL,
            BracketTemplateRoundId     bigint NOT NULL,
            GroupKey                   varchar(50) NOT NULL,
            GroupName                  nvarchar(50) NOT NULL,
            GroupType                  varchar(30) NOT NULL
                CONSTRAINT DF_BracketTemplateGroups_GroupType DEFAULT ('GENERIC'),
            SortOrder                  int NOT NULL
                CONSTRAINT DF_BracketTemplateGroups_SortOrder DEFAULT (0),
            CreatedAt                  datetime2(0) NOT NULL
                CONSTRAINT DF_BracketTemplateGroups_CreatedAt DEFAULT (SYSDATETIME()),
            UpdatedAt                  datetime2(0) NULL,

            CONSTRAINT PK_BracketTemplateGroups
                PRIMARY KEY CLUSTERED (BracketTemplateGroupId),

            CONSTRAINT FK_BracketTemplateGroups_RoundVersion
                FOREIGN KEY
                (
                    BracketTemplateRoundId,
                    BracketTemplateVersionId
                )
                REFERENCES dbo.BracketTemplateRounds
                (
                    BracketTemplateRoundId,
                    BracketTemplateVersionId
                )
                ON DELETE CASCADE,

            CONSTRAINT CK_BracketTemplateGroups_GroupType
                CHECK (GroupType IN
                (
                    'GENERIC',
                    'ROUND_ROBIN',
                    'KNOCKOUT_BRANCH',
                    'FINAL',
                    'PLACEMENT'
                ))
        );

        CREATE UNIQUE INDEX UX_BracketTemplateGroups_Version_GroupKey
            ON dbo.BracketTemplateGroups (BracketTemplateVersionId, GroupKey);

        CREATE UNIQUE INDEX UX_BracketTemplateGroups_Round_GroupName
            ON dbo.BracketTemplateGroups (BracketTemplateRoundId, GroupName);

        CREATE UNIQUE INDEX UX_BracketTemplateGroups_Id_Version
            ON dbo.BracketTemplateGroups
            (
                BracketTemplateGroupId,
                BracketTemplateVersionId
            );

        CREATE INDEX IX_BracketTemplateGroups_Round_SortOrder
            ON dbo.BracketTemplateGroups (BracketTemplateRoundId, SortOrder, GroupKey);

        ----------------------------------------------------------------------
        -- 1.5. BracketTemplateMatches
        ----------------------------------------------------------------------
        CREATE TABLE dbo.BracketTemplateMatches
        (
            BracketTemplateMatchId     bigint IDENTITY(1, 1) NOT NULL,
            BracketTemplateVersionId   bigint NOT NULL,
            BracketTemplateGroupId     bigint NOT NULL,
            MatchKey                   varchar(50) NOT NULL,
            MatchLabel                 nvarchar(100) NULL,
            SortOrder                  int NOT NULL
                CONSTRAINT DF_BracketTemplateMatches_SortOrder DEFAULT (0),
            IsTerminal                 bit NOT NULL
                CONSTRAINT DF_BracketTemplateMatches_IsTerminal DEFAULT (0),
            TerminalType               varchar(30) NULL,
            CreatedAt                  datetime2(0) NOT NULL
                CONSTRAINT DF_BracketTemplateMatches_CreatedAt DEFAULT (SYSDATETIME()),
            UpdatedAt                  datetime2(0) NULL,

            CONSTRAINT PK_BracketTemplateMatches
                PRIMARY KEY CLUSTERED (BracketTemplateMatchId),

            CONSTRAINT FK_BracketTemplateMatches_GroupVersion
                FOREIGN KEY
                (
                    BracketTemplateGroupId,
                    BracketTemplateVersionId
                )
                REFERENCES dbo.BracketTemplateGroups
                (
                    BracketTemplateGroupId,
                    BracketTemplateVersionId
                )
                ON DELETE CASCADE,

            CONSTRAINT CK_BracketTemplateMatches_TerminalType
                CHECK
                (
                    TerminalType IS NULL
                    OR TerminalType IN
                    (
                        'CHAMPION',
                        'RUNNER_UP',
                        'THIRD_PLACE',
                        'PLACEMENT'
                    )
                ),

            CONSTRAINT CK_BracketTemplateMatches_TerminalState
                CHECK
                (
                    (IsTerminal = 0 AND TerminalType IS NULL)
                    OR (IsTerminal = 1 AND TerminalType IS NOT NULL)
                )
        );

        CREATE UNIQUE INDEX UX_BracketTemplateMatches_Version_MatchKey
            ON dbo.BracketTemplateMatches (BracketTemplateVersionId, MatchKey);

        CREATE UNIQUE INDEX UX_BracketTemplateMatches_Id_Version
            ON dbo.BracketTemplateMatches
            (
                BracketTemplateMatchId,
                BracketTemplateVersionId
            );

        CREATE INDEX IX_BracketTemplateMatches_Group_SortOrder
            ON dbo.BracketTemplateMatches (BracketTemplateGroupId, SortOrder, MatchKey);

        ----------------------------------------------------------------------
        -- 1.6. BracketTemplateMatchSlots
        ----------------------------------------------------------------------
        CREATE TABLE dbo.BracketTemplateMatchSlots
        (
            BracketTemplateMatchSlotId bigint IDENTITY(1, 1) NOT NULL,
            BracketTemplateVersionId   bigint NOT NULL,
            BracketTemplateMatchId     bigint NOT NULL,
            SlotNumber                 tinyint NOT NULL,
            SourceType                 varchar(30) NOT NULL,
            SeedNumber                 int NULL,
            SourceMatchId              bigint NULL,
            SourceGroupId              bigint NULL,
            SourceRank                 int NULL,
            CreatedAt                  datetime2(0) NOT NULL
                CONSTRAINT DF_BracketTemplateMatchSlots_CreatedAt DEFAULT (SYSDATETIME()),
            UpdatedAt                  datetime2(0) NULL,

            CONSTRAINT PK_BracketTemplateMatchSlots
                PRIMARY KEY CLUSTERED (BracketTemplateMatchSlotId),

            CONSTRAINT FK_BracketTemplateMatchSlots_MatchVersion
                FOREIGN KEY
                (
                    BracketTemplateMatchId,
                    BracketTemplateVersionId
                )
                REFERENCES dbo.BracketTemplateMatches
                (
                    BracketTemplateMatchId,
                    BracketTemplateVersionId
                )
                ON DELETE CASCADE,

            CONSTRAINT FK_BracketTemplateMatchSlots_SourceMatchVersion
                FOREIGN KEY
                (
                    SourceMatchId,
                    BracketTemplateVersionId
                )
                REFERENCES dbo.BracketTemplateMatches
                (
                    BracketTemplateMatchId,
                    BracketTemplateVersionId
                ),

            CONSTRAINT FK_BracketTemplateMatchSlots_SourceGroupVersion
                FOREIGN KEY
                (
                    SourceGroupId,
                    BracketTemplateVersionId
                )
                REFERENCES dbo.BracketTemplateGroups
                (
                    BracketTemplateGroupId,
                    BracketTemplateVersionId
                ),

            CONSTRAINT CK_BracketTemplateMatchSlots_SlotNumber
                CHECK (SlotNumber IN (1, 2)),

            CONSTRAINT CK_BracketTemplateMatchSlots_SourceType
                CHECK (SourceType IN
                (
                    'SEED',
                    'WINNER_MATCH',
                    'LOSER_MATCH',
                    'GROUP_RANK',
                    'BYE'
                )),

            CONSTRAINT CK_BracketTemplateMatchSlots_SourceMatchNotSelf
                CHECK
                (
                    SourceMatchId IS NULL
                    OR SourceMatchId <> BracketTemplateMatchId
                ),

            CONSTRAINT CK_BracketTemplateMatchSlots_SourcePayload
                CHECK
                (
                    (
                        SourceType = 'SEED'
                        AND (SeedNumber IS NULL OR SeedNumber > 0)
                        AND SourceMatchId IS NULL
                        AND SourceGroupId IS NULL
                        AND SourceRank IS NULL
                    )
                    OR
                    (
                        SourceType IN ('WINNER_MATCH', 'LOSER_MATCH')
                        AND SeedNumber IS NULL
                        AND SourceMatchId IS NOT NULL
                        AND SourceGroupId IS NULL
                        AND SourceRank IS NULL
                    )
                    OR
                    (
                        SourceType = 'GROUP_RANK'
                        AND SeedNumber IS NULL
                        AND SourceMatchId IS NULL
                        AND SourceGroupId IS NOT NULL
                        AND SourceRank IS NOT NULL
                        AND SourceRank > 0
                    )
                    OR
                    (
                        SourceType = 'BYE'
                        AND SeedNumber IS NULL
                        AND SourceMatchId IS NULL
                        AND SourceGroupId IS NULL
                        AND SourceRank IS NULL
                    )
                )
        );

        CREATE UNIQUE INDEX UX_BracketTemplateMatchSlots_Match_Slot
            ON dbo.BracketTemplateMatchSlots (BracketTemplateMatchId, SlotNumber);

        CREATE INDEX IX_BracketTemplateMatchSlots_SourceMatch
            ON dbo.BracketTemplateMatchSlots
            (
                BracketTemplateVersionId,
                SourceMatchId,
                SourceType
            )
            WHERE SourceMatchId IS NOT NULL;

        CREATE INDEX IX_BracketTemplateMatchSlots_SourceGroup
            ON dbo.BracketTemplateMatchSlots
            (
                BracketTemplateVersionId,
                SourceGroupId,
                SourceRank
            )
            WHERE SourceGroupId IS NOT NULL;

        CREATE INDEX IX_BracketTemplateMatchSlots_Seed
            ON dbo.BracketTemplateMatchSlots
            (
                BracketTemplateVersionId,
                SeedNumber
            )
            WHERE SeedNumber IS NOT NULL;

        ----------------------------------------------------------------------
        -- 1.7. TournamentBracketApplications
        ----------------------------------------------------------------------
        CREATE TABLE dbo.TournamentBracketApplications
        (
            TournamentBracketApplicationId bigint IDENTITY(1, 1) NOT NULL,
            TournamentId                   bigint NOT NULL,
            BracketTemplateId              bigint NOT NULL,
            BracketTemplateVersionId       bigint NOT NULL,
            [Status]                       varchar(20) NOT NULL
                CONSTRAINT DF_TournamentBracketApplications_Status DEFAULT ('APPLYING'),
            IsActive                       bit NOT NULL
                CONSTRAINT DF_TournamentBracketApplications_IsActive DEFAULT (1),
            SeedingMethod                  varchar(30) NOT NULL,
            RandomSeed                     bigint NULL,
            EligibleRegistrationCount      int NOT NULL,
            SeedCapacity                   int NOT NULL,
            ByeCount                       int NOT NULL
                CONSTRAINT DF_TournamentBracketApplications_ByeCount DEFAULT (0),
            PreviewHash                    varchar(64) NOT NULL,
            AppliedByUserId                bigint NULL,
            RevertedByUserId               bigint NULL,
            CreatedAt                      datetime2(0) NOT NULL
                CONSTRAINT DF_TournamentBracketApplications_CreatedAt DEFAULT (SYSDATETIME()),
            AppliedAt                      datetime2(0) NULL,
            RevertedAt                     datetime2(0) NULL,
            UpdatedAt                      datetime2(0) NULL,
            RevertReason                   nvarchar(1000) NULL,
            ErrorCode                      varchar(100) NULL,
            ErrorMessage                   nvarchar(2000) NULL,
            [RowVersion]                   rowversion NOT NULL,

            CONSTRAINT PK_TournamentBracketApplications
                PRIMARY KEY CLUSTERED (TournamentBracketApplicationId),

            CONSTRAINT FK_TournamentBracketApplications_Tournament
                FOREIGN KEY (TournamentId)
                REFERENCES dbo.Tournaments (TournamentId),

            CONSTRAINT FK_TournamentBracketApplications_TemplateVersion
                FOREIGN KEY
                (
                    BracketTemplateVersionId,
                    BracketTemplateId
                )
                REFERENCES dbo.BracketTemplateVersions
                (
                    BracketTemplateVersionId,
                    BracketTemplateId
                ),

            CONSTRAINT FK_TournamentBracketApplications_AppliedByUser
                FOREIGN KEY (AppliedByUserId)
                REFERENCES dbo.Users (UserId),

            CONSTRAINT FK_TournamentBracketApplications_RevertedByUser
                FOREIGN KEY (RevertedByUserId)
                REFERENCES dbo.Users (UserId),

            CONSTRAINT CK_TournamentBracketApplications_Status
                CHECK ([Status] IN
                (
                    'APPLYING',
                    'APPLIED',
                    'FAILED',
                    'REVERTED'
                )),

            CONSTRAINT CK_TournamentBracketApplications_ActiveStatus
                CHECK
                (
                    ([Status] IN ('APPLYING', 'APPLIED') AND IsActive = 1)
                    OR ([Status] IN ('FAILED', 'REVERTED') AND IsActive = 0)
                ),

            CONSTRAINT CK_TournamentBracketApplications_SeedingMethod
                CHECK (SeedingMethod IN
                (
                    'REGISTRATION_ORDER',
                    'RANDOM',
                    'MANUAL',
                    'RANKING'
                )),

            CONSTRAINT CK_TournamentBracketApplications_Counts
                CHECK
                (
                    EligibleRegistrationCount >= 0
                    AND SeedCapacity >= 2
                    AND ByeCount >= 0
                    AND ByeCount <= SeedCapacity
                    AND EligibleRegistrationCount + ByeCount = SeedCapacity
                ),

            CONSTRAINT CK_TournamentBracketApplications_RandomSeed
                CHECK
                (
                    (SeedingMethod = 'RANDOM' AND RandomSeed IS NOT NULL)
                    OR (SeedingMethod <> 'RANDOM')
                )
        );

        CREATE UNIQUE INDEX UX_TournamentBracketApplications_ActiveTournament
            ON dbo.TournamentBracketApplications (TournamentId)
            WHERE IsActive = 1;

        CREATE INDEX IX_TournamentBracketApplications_TemplateVersion_Status
            ON dbo.TournamentBracketApplications
            (
                BracketTemplateVersionId,
                [Status],
                CreatedAt DESC
            );

        CREATE INDEX IX_TournamentBracketApplications_Tournament_History
            ON dbo.TournamentBracketApplications
            (
                TournamentId,
                CreatedAt DESC
            );

        ----------------------------------------------------------------------
        -- 1.8. TournamentBracketSeedAssignments
        ----------------------------------------------------------------------
        CREATE TABLE dbo.TournamentBracketSeedAssignments
        (
            TournamentBracketSeedAssignmentId bigint IDENTITY(1, 1) NOT NULL,
            TournamentBracketApplicationId    bigint NOT NULL,
            SeedNumber                         int NOT NULL,
            RegistrationId                     bigint NULL,
            IsBye                              bit NOT NULL
                CONSTRAINT DF_TournamentBracketSeedAssignments_IsBye DEFAULT (0),
            InputOrder                         int NULL,
            AssignmentMethod                   varchar(30) NOT NULL,
            IsManuallyAdjusted                 bit NOT NULL
                CONSTRAINT DF_TournamentBracketSeedAssignments_IsManual DEFAULT (0),
            RegistrationCodeSnapshot           nvarchar(50) NULL,
            Player1NameSnapshot                nvarchar(150) NULL,
            Player2NameSnapshot                nvarchar(150) NULL,
            CreatedAt                          datetime2(0) NOT NULL
                CONSTRAINT DF_TournamentBracketSeedAssignments_CreatedAt DEFAULT (SYSDATETIME()),

            CONSTRAINT PK_TournamentBracketSeedAssignments
                PRIMARY KEY CLUSTERED (TournamentBracketSeedAssignmentId),

            CONSTRAINT FK_TournamentBracketSeedAssignments_Application
                FOREIGN KEY (TournamentBracketApplicationId)
                REFERENCES dbo.TournamentBracketApplications
                (
                    TournamentBracketApplicationId
                )
                ON DELETE CASCADE,

            CONSTRAINT FK_TournamentBracketSeedAssignments_Registration
                FOREIGN KEY (RegistrationId)
                REFERENCES dbo.TournamentRegistrations (RegistrationId),

            CONSTRAINT CK_TournamentBracketSeedAssignments_SeedNumber
                CHECK (SeedNumber > 0),

            CONSTRAINT CK_TournamentBracketSeedAssignments_AssignmentMethod
                CHECK (AssignmentMethod IN
                (
                    'REGISTRATION_ORDER',
                    'RANDOM',
                    'MANUAL',
                    'RANKING',
                    'BYE'
                )),

            CONSTRAINT CK_TournamentBracketSeedAssignments_RegistrationOrBye
                CHECK
                (
                    (IsBye = 1 AND RegistrationId IS NULL AND AssignmentMethod = 'BYE')
                    OR (IsBye = 0 AND RegistrationId IS NOT NULL AND AssignmentMethod <> 'BYE')
                )
        );

        CREATE UNIQUE INDEX UX_TournamentBracketSeedAssignments_Application_Seed
            ON dbo.TournamentBracketSeedAssignments
            (
                TournamentBracketApplicationId,
                SeedNumber
            );

        CREATE UNIQUE INDEX UX_TournamentBracketSeedAssignments_Application_Registration
            ON dbo.TournamentBracketSeedAssignments
            (
                TournamentBracketApplicationId,
                RegistrationId
            )
            WHERE RegistrationId IS NOT NULL;

        CREATE INDEX IX_TournamentBracketSeedAssignments_Registration
            ON dbo.TournamentBracketSeedAssignments (RegistrationId)
            WHERE RegistrationId IS NOT NULL;
    END;

    IF COL_LENGTH(N'dbo.BracketTemplateVersions', N'DraftGraphJson') IS NULL
    BEGIN
        ALTER TABLE dbo.BracketTemplateVersions
            ADD DraftGraphJson nvarchar(max) NULL;
    END;

    --------------------------------------------------------------------------
    -- 2. FK CurrentPublishedVersion sau khi bảng version đã tồn tại
    --------------------------------------------------------------------------
    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys
        WHERE [name] = N'FK_BracketTemplates_CurrentPublishedVersion'
          AND parent_object_id = OBJECT_ID(N'dbo.BracketTemplates')
    )
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.BracketTemplates WITH CHECK
            ADD CONSTRAINT FK_BracketTemplates_CurrentPublishedVersion
                FOREIGN KEY
                (
                    CurrentPublishedVersionId,
                    BracketTemplateId
                )
                REFERENCES dbo.BracketTemplateVersions
                (
                    BracketTemplateVersionId,
                    BracketTemplateId
                );';
    END;

    --------------------------------------------------------------------------
    -- 3. Bổ sung khóa đăng ký trên Tournaments
    --------------------------------------------------------------------------
    IF COL_LENGTH(N'dbo.Tournaments', N'RegistrationLockedAt') IS NULL
    BEGIN
        ALTER TABLE dbo.Tournaments
            ADD RegistrationLockedAt datetime2(0) NULL;
    END;

    IF COL_LENGTH(N'dbo.Tournaments', N'RegistrationLockedByUserId') IS NULL
    BEGIN
        ALTER TABLE dbo.Tournaments
            ADD RegistrationLockedByUserId bigint NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys
        WHERE [name] = N'FK_Tournaments_RegistrationLockedByUser'
          AND parent_object_id = OBJECT_ID(N'dbo.Tournaments')
    )
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.Tournaments WITH CHECK
            ADD CONSTRAINT FK_Tournaments_RegistrationLockedByUser
                FOREIGN KEY (RegistrationLockedByUserId)
                REFERENCES dbo.Users (UserId);';
    END;

    --------------------------------------------------------------------------
    -- 4. Liên kết TournamentRoundMaps với application/template round
    --------------------------------------------------------------------------
    IF COL_LENGTH(N'dbo.TournamentRoundMaps', N'BracketApplicationId') IS NULL
    BEGIN
        ALTER TABLE dbo.TournamentRoundMaps
            ADD BracketApplicationId bigint NULL;
    END;

    IF COL_LENGTH(N'dbo.TournamentRoundMaps', N'TemplateRoundKey') IS NULL
    BEGIN
        ALTER TABLE dbo.TournamentRoundMaps
            ADD TemplateRoundKey varchar(20) NULL;
    END;

    IF COL_LENGTH(N'dbo.TournamentRoundMaps', N'TemplateRoundType') IS NULL
    BEGIN
        ALTER TABLE dbo.TournamentRoundMaps
            ADD TemplateRoundType varchar(30) NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys
        WHERE [name] = N'FK_TournamentRoundMaps_BracketApplication'
          AND parent_object_id = OBJECT_ID(N'dbo.TournamentRoundMaps')
    )
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.TournamentRoundMaps WITH CHECK
            ADD CONSTRAINT FK_TournamentRoundMaps_BracketApplication
                FOREIGN KEY (BracketApplicationId)
                REFERENCES dbo.TournamentBracketApplications
                (
                    TournamentBracketApplicationId
                );';
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE [name] = N'UX_TournamentRoundMaps_Application_TemplateKey'
          AND object_id = OBJECT_ID(N'dbo.TournamentRoundMaps')
    )
    BEGIN
        EXEC sys.sp_executesql N'
            CREATE UNIQUE INDEX UX_TournamentRoundMaps_Application_TemplateKey
                ON dbo.TournamentRoundMaps
                (
                    BracketApplicationId,
                    TemplateRoundKey
                )
                WHERE BracketApplicationId IS NOT NULL
                  AND TemplateRoundKey IS NOT NULL;';
    END;

    --------------------------------------------------------------------------
    -- 5. Liên kết TournamentRoundGroups với application/template group
    --------------------------------------------------------------------------
    IF COL_LENGTH(N'dbo.TournamentRoundGroups', N'BracketApplicationId') IS NULL
    BEGIN
        ALTER TABLE dbo.TournamentRoundGroups
            ADD BracketApplicationId bigint NULL;
    END;

    IF COL_LENGTH(N'dbo.TournamentRoundGroups', N'TemplateGroupKey') IS NULL
    BEGIN
        ALTER TABLE dbo.TournamentRoundGroups
            ADD TemplateGroupKey varchar(50) NULL;
    END;

    IF COL_LENGTH(N'dbo.TournamentRoundGroups', N'TemplateGroupType') IS NULL
    BEGIN
        ALTER TABLE dbo.TournamentRoundGroups
            ADD TemplateGroupType varchar(30) NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys
        WHERE [name] = N'FK_TournamentRoundGroups_BracketApplication'
          AND parent_object_id = OBJECT_ID(N'dbo.TournamentRoundGroups')
    )
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.TournamentRoundGroups WITH CHECK
            ADD CONSTRAINT FK_TournamentRoundGroups_BracketApplication
                FOREIGN KEY (BracketApplicationId)
                REFERENCES dbo.TournamentBracketApplications
                (
                    TournamentBracketApplicationId
                );';
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE [name] = N'UX_TournamentRoundGroups_Application_TemplateKey'
          AND object_id = OBJECT_ID(N'dbo.TournamentRoundGroups')
    )
    BEGIN
        EXEC sys.sp_executesql N'
            CREATE UNIQUE INDEX UX_TournamentRoundGroups_Application_TemplateKey
                ON dbo.TournamentRoundGroups
                (
                    BracketApplicationId,
                    TemplateGroupKey
                )
                WHERE BracketApplicationId IS NOT NULL
                  AND TemplateGroupKey IS NOT NULL;';
    END;

    --------------------------------------------------------------------------
    -- 6. Liên kết TournamentGroupMatches với application/template match
    --------------------------------------------------------------------------
    IF COL_LENGTH(N'dbo.TournamentGroupMatches', N'BracketApplicationId') IS NULL
    BEGIN
        ALTER TABLE dbo.TournamentGroupMatches
            ADD BracketApplicationId bigint NULL;
    END;

    IF COL_LENGTH(N'dbo.TournamentGroupMatches', N'TemplateMatchKey') IS NULL
    BEGIN
        ALTER TABLE dbo.TournamentGroupMatches
            ADD TemplateMatchKey varchar(50) NULL;
    END;

    IF COL_LENGTH(N'dbo.TournamentGroupMatches', N'TemplateMatchLabel') IS NULL
    BEGIN
        ALTER TABLE dbo.TournamentGroupMatches
            ADD TemplateMatchLabel nvarchar(100) NULL;
    END;

    IF COL_LENGTH(N'dbo.TournamentGroupMatches', N'TemplateIsTerminal') IS NULL
    BEGIN
        ALTER TABLE dbo.TournamentGroupMatches
            ADD TemplateIsTerminal bit NULL;
    END;

    IF COL_LENGTH(N'dbo.TournamentGroupMatches', N'TemplateTerminalType') IS NULL
    BEGIN
        ALTER TABLE dbo.TournamentGroupMatches
            ADD TemplateTerminalType varchar(30) NULL;
    END;

    IF COL_LENGTH(N'dbo.TournamentGroupMatches', N'CompletionReason') IS NULL
    BEGIN
        ALTER TABLE dbo.TournamentGroupMatches
            ADD CompletionReason varchar(20) NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys
        WHERE [name] = N'FK_TournamentGroupMatches_BracketApplication'
          AND parent_object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
    )
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.TournamentGroupMatches WITH CHECK
            ADD CONSTRAINT FK_TournamentGroupMatches_BracketApplication
                FOREIGN KEY (BracketApplicationId)
                REFERENCES dbo.TournamentBracketApplications
                (
                    TournamentBracketApplicationId
                );';
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE [name] = N'CK_TournamentGroupMatches_CompletionReason'
          AND parent_object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
    )
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.TournamentGroupMatches WITH CHECK
            ADD CONSTRAINT CK_TournamentGroupMatches_CompletionReason
                CHECK
                (
                    CompletionReason IS NULL
                    OR CompletionReason IN
                    (
                        ''NORMAL'',
                        ''BYE'',
                        ''WALKOVER'',
                        ''ADMIN_OVERRIDE''
                    )
                );';
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE [name] = N'UX_TournamentGroupMatches_Application_TemplateKey'
          AND object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
    )
    BEGIN
        EXEC sys.sp_executesql N'
            CREATE UNIQUE INDEX UX_TournamentGroupMatches_Application_TemplateKey
                ON dbo.TournamentGroupMatches
                (
                    BracketApplicationId,
                    TemplateMatchKey
                )
                WHERE BracketApplicationId IS NOT NULL
                  AND TemplateMatchKey IS NOT NULL;';
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE [name] = N'IX_TournamentGroupMatches_Application_Completion'
          AND object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
    )
    BEGIN
        EXEC sys.sp_executesql N'
            CREATE INDEX IX_TournamentGroupMatches_Application_Completion
                ON dbo.TournamentGroupMatches
                (
                    BracketApplicationId,
                    IsCompleted,
                    CompletionReason
                )
                WHERE BracketApplicationId IS NOT NULL;';
    END;

    --------------------------------------------------------------------------
    -- 7. Commit
    --------------------------------------------------------------------------
    COMMIT TRANSACTION;

    PRINT N'Bracket Template schema đã được tạo/cập nhật thành công.';
    PRINT N'Không có dữ liệu giải đấu hiện tại nào bị xóa hoặc cập nhật.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;

/*
    Kiểm tra nhanh sau khi chạy:

    SELECT name
    FROM sys.tables
    WHERE name IN
    (
        'BracketTemplates',
        'BracketTemplateVersions',
        'BracketTemplateRounds',
        'BracketTemplateGroups',
        'BracketTemplateMatches',
        'BracketTemplateMatchSlots',
        'TournamentBracketApplications',
        'TournamentBracketSeedAssignments'
    )
    ORDER BY name;

    SELECT
        OBJECT_NAME(object_id) AS TableName,
        name AS ColumnName
    FROM sys.columns
    WHERE
        (object_id = OBJECT_ID('dbo.Tournaments')
            AND name IN ('RegistrationLockedAt', 'RegistrationLockedByUserId'))
        OR
        (object_id = OBJECT_ID('dbo.TournamentRoundMaps')
            AND name IN ('BracketApplicationId', 'TemplateRoundKey', 'TemplateRoundType'))
        OR
        (object_id = OBJECT_ID('dbo.TournamentRoundGroups')
            AND name IN ('BracketApplicationId', 'TemplateGroupKey', 'TemplateGroupType'))
        OR
        (object_id = OBJECT_ID('dbo.TournamentGroupMatches')
            AND name IN ('BracketApplicationId', 'TemplateMatchKey', 'TemplateMatchLabel',
                         'TemplateIsTerminal', 'TemplateTerminalType', 'CompletionReason'))
    ORDER BY TableName, ColumnName;
*/
