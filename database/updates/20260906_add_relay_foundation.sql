-- Relay foundation only. Apply to a test database first; application does not auto-run this script.
-- Requires the current legacy schema. No legacy row is rewritten and no tournament is enabled.
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.Tournaments', N'U') IS NULL
 OR OBJECT_ID(N'dbo.TournamentRegistrations', N'U') IS NULL
 OR OBJECT_ID(N'dbo.TournamentGroupMatches', N'U') IS NULL
 OR OBJECT_ID(N'dbo.Users', N'U') IS NULL
    THROW 51000, N'Apply the existing Hanaka schema before the relay migration.', 1;

IF OBJECT_ID(N'dbo.RelayTournamentSettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RelayTournamentSettings (
        TournamentId bigint NOT NULL CONSTRAINT PK_RelayTournamentSettings PRIMARY KEY,
        TeamSize int NOT NULL, TargetScore int NOT NULL, LegDurationSeconds int NOT NULL,
        DeadlinePolicy varchar(30) NULL, IsEnabled bit NOT NULL, Version bigint NOT NULL,
        CONSTRAINT FK_RelaySettings_Tournament FOREIGN KEY (TournamentId) REFERENCES dbo.Tournaments(TournamentId),
        CONSTRAINT CK_RelaySettings_TeamSize CHECK (TeamSize IN (4,6,8)),
        CONSTRAINT CK_RelaySettings_Rules CHECK (TargetScore > 0 AND LegDurationSeconds > 0 AND Version >= 0),
        CONSTRAINT CK_RelaySettings_Policy CHECK ((DeadlinePolicy IS NULL AND IsEnabled = 0) OR
            (DeadlinePolicy IS NOT NULL AND DeadlinePolicy IN ('STOP_AT_DEADLINE','FINISH_RALLY')))
    );
END;

IF OBJECT_ID(N'dbo.RelayTeams', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RelayTeams (
        RegistrationId bigint NOT NULL CONSTRAINT PK_RelayTeams PRIMARY KEY,
        TournamentId bigint NOT NULL, TeamName nvarchar(150) NOT NULL, CaptainUserId bigint NULL,
        LineupLockedAtUtc datetimeoffset(7) NULL, Version bigint NOT NULL,
        CONSTRAINT CK_RelayTeams_Version CHECK (Version >= 0),
        CONSTRAINT FK_RelayTeams_Registration FOREIGN KEY (RegistrationId) REFERENCES dbo.TournamentRegistrations(RegistrationId),
        CONSTRAINT FK_RelayTeams_Settings FOREIGN KEY (TournamentId) REFERENCES dbo.RelayTournamentSettings(TournamentId),
        CONSTRAINT FK_RelayTeams_Captain FOREIGN KEY (CaptainUserId) REFERENCES dbo.Users(UserId)
    );
    CREATE INDEX IX_RelayTeams_TournamentId ON dbo.RelayTeams(TournamentId);
END;

IF OBJECT_ID(N'dbo.RelayTeamMembers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RelayTeamMembers (
        RegistrationId bigint NOT NULL, Position int NOT NULL, UserId bigint NULL,
        DisplayName nvarchar(150) NOT NULL, AvatarUrl nvarchar(500) NULL,
        CONSTRAINT PK_RelayTeamMembers PRIMARY KEY (RegistrationId, Position),
        CONSTRAINT CK_RelayMembers_Position CHECK (Position BETWEEN 1 AND 8),
        CONSTRAINT FK_RelayMembers_Team FOREIGN KEY (RegistrationId) REFERENCES dbo.RelayTeams(RegistrationId),
        CONSTRAINT FK_RelayMembers_User FOREIGN KEY (UserId) REFERENCES dbo.Users(UserId)
    );
    CREATE UNIQUE INDEX IX_RelayTeamMembers_RegistrationId_UserId
        ON dbo.RelayTeamMembers(RegistrationId, UserId) WHERE UserId IS NOT NULL;
END;

IF OBJECT_ID(N'dbo.RelayMatchStates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RelayMatchStates (
        MatchId bigint NOT NULL CONSTRAINT PK_RelayMatchStates PRIMARY KEY,
        TournamentId bigint NOT NULL, Team1RegistrationId bigint NOT NULL, Team2RegistrationId bigint NOT NULL,
        PairCount int NOT NULL, TargetScore int NOT NULL, LegDurationSeconds int NOT NULL,
        DeadlinePolicy varchar(30) NOT NULL, Status varchar(30) NOT NULL,
        CurrentLegNumber int NOT NULL, UpdatedAtUtc datetimeoffset(7) NULL, Version bigint NOT NULL,
        CONSTRAINT FK_RelayMatch_Match FOREIGN KEY (MatchId) REFERENCES dbo.TournamentGroupMatches(MatchId),
        CONSTRAINT FK_RelayMatch_Settings FOREIGN KEY (TournamentId) REFERENCES dbo.RelayTournamentSettings(TournamentId),
        CONSTRAINT FK_RelayMatch_Team1 FOREIGN KEY (Team1RegistrationId) REFERENCES dbo.RelayTeams(RegistrationId),
        CONSTRAINT FK_RelayMatch_Team2 FOREIGN KEY (Team2RegistrationId) REFERENCES dbo.RelayTeams(RegistrationId),
        CONSTRAINT CK_RelayMatch_Rules CHECK (PairCount IN (2,3,4) AND TargetScore > 0 AND LegDurationSeconds > 0 AND Version >= 0 AND CurrentLegNumber >= 0),
        CONSTRAINT CK_RelayMatch_Teams CHECK (Team1RegistrationId <> Team2RegistrationId),
        CONSTRAINT CK_RelayMatch_Status CHECK (Status IN ('READY','RUNNING','AWAITING_CHANGE','COMPLETED')),
        CONSTRAINT CK_RelayMatch_Policy CHECK (DeadlinePolicy IN ('STOP_AT_DEADLINE','FINISH_RALLY'))
    );
    CREATE INDEX IX_RelayMatchStates_TournamentId ON dbo.RelayMatchStates(TournamentId);
    CREATE INDEX IX_RelayMatchStates_Team1RegistrationId ON dbo.RelayMatchStates(Team1RegistrationId);
    CREATE INDEX IX_RelayMatchStates_Team2RegistrationId ON dbo.RelayMatchStates(Team2RegistrationId);
END;

IF OBJECT_ID(N'dbo.RelayLegs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RelayLegs (
        MatchId bigint NOT NULL, LegNumber int NOT NULL, PairNumber int NOT NULL,
        StartedAtUtc datetimeoffset(7) NOT NULL, EndsAtUtc datetimeoffset(7) NOT NULL, FinishedAtUtc datetimeoffset(7) NULL,
        StartScoreTeam1 int NOT NULL, StartScoreTeam2 int NOT NULL, EndScoreTeam1 int NULL, EndScoreTeam2 int NULL,
        CONSTRAINT PK_RelayLegs PRIMARY KEY (MatchId, LegNumber),
        CONSTRAINT FK_RelayLegs_State FOREIGN KEY (MatchId) REFERENCES dbo.RelayMatchStates(MatchId),
        CONSTRAINT CK_RelayLeg_Order CHECK (LegNumber > 0 AND PairNumber BETWEEN 1 AND 4),
        CONSTRAINT CK_RelayLeg_Time CHECK (EndsAtUtc > StartedAtUtc AND (FinishedAtUtc IS NULL OR FinishedAtUtc >= StartedAtUtc)),
        CONSTRAINT CK_RelayLeg_Scores CHECK (StartScoreTeam1 >= 0 AND StartScoreTeam2 >= 0
            AND (EndScoreTeam1 IS NULL OR EndScoreTeam1 >= StartScoreTeam1)
            AND (EndScoreTeam2 IS NULL OR EndScoreTeam2 >= StartScoreTeam2))
    );
END;

IF OBJECT_ID(N'dbo.RelayMatchCommands', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RelayMatchCommands (
        MatchId bigint NOT NULL, RequestId uniqueidentifier NOT NULL, ActorUserId bigint NOT NULL,
        RequestHash varchar(64) NOT NULL, Operation varchar(40) NOT NULL, ResultVersion bigint NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL, ResultJson nvarchar(max) NOT NULL,
        CONSTRAINT PK_RelayMatchCommands PRIMARY KEY (MatchId, RequestId),
        CONSTRAINT FK_RelayCommands_State FOREIGN KEY (MatchId) REFERENCES dbo.RelayMatchStates(MatchId),
        CONSTRAINT FK_RelayCommands_Actor FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(UserId)
    );
    CREATE UNIQUE INDEX IX_RelayMatchCommands_MatchId_ResultVersion ON dbo.RelayMatchCommands(MatchId, ResultVersion);
END;

-- Dynamic batches allow CREATE OR ALTER TRIGGER inside the migration transaction.
EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_RelayMembers_FixedLineup ON dbo.RelayTeamMembers
AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM dbo.RelayTeams t WITH (UPDLOCK, HOLDLOCK)
        WHERE t.LineupLockedAtUtc IS NOT NULL
          AND (t.RegistrationId IN (SELECT RegistrationId FROM inserted)
               OR t.RegistrationId IN (SELECT RegistrationId FROM deleted))
    ) THROW 51001, N''Đội hình đã chốt, không được đổi thành viên hoặc thứ tự.'', 1;
END;');

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_RelayTeams_ValidateLock ON dbo.RelayTeams
AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.RegistrationId = d.RegistrationId
        WHERE d.LineupLockedAtUtc IS NOT NULL AND
          (i.RegistrationId IS NULL OR i.LineupLockedAtUtc IS NULL OR i.LineupLockedAtUtc <> d.LineupLockedAtUtc
           OR i.TournamentId <> d.TournamentId))
        THROW 51002, N''Không được xóa hoặc mở lại đội hình đã chốt.'', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.TournamentRegistrations r ON r.RegistrationId = i.RegistrationId
        WHERE r.TournamentId <> i.TournamentId)
        THROW 51003, N''Đăng ký đội phải thuộc đúng giải.'', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.RelayTournamentSettings s ON s.TournamentId = i.TournamentId
        WHERE i.LineupLockedAtUtc IS NOT NULL AND (
          (SELECT COUNT(*) FROM dbo.RelayTeamMembers m WITH (UPDLOCK, HOLDLOCK) WHERE m.RegistrationId = i.RegistrationId) <> s.TeamSize
          OR EXISTS (SELECT 1 FROM dbo.RelayTeamMembers m WHERE m.RegistrationId = i.RegistrationId
              AND (m.Position > s.TeamSize OR LEN(LTRIM(RTRIM(m.DisplayName))) = 0))))
        THROW 51004, N''Đội hình chưa đủ thành viên hợp lệ để chốt.'', 1;
END;');

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_RelaySettings_LockedRules ON dbo.RelayTournamentSettings
AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.TournamentId = d.TournamentId
        WHERE EXISTS (SELECT 1 FROM dbo.RelayTeams t WITH (UPDLOCK, HOLDLOCK)
                      WHERE t.TournamentId = d.TournamentId AND t.LineupLockedAtUtc IS NOT NULL)
          AND (i.TournamentId IS NULL OR i.TeamSize <> d.TeamSize))
        THROW 51005, N''Không được đổi quy mô đội sau khi đã chốt đội hình.'', 1;
    IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.TournamentId = d.TournamentId
        WHERE EXISTS (SELECT 1 FROM dbo.RelayMatchStates m WHERE m.TournamentId = d.TournamentId)
          AND (i.TargetScore <> d.TargetScore OR i.LegDurationSeconds <> d.LegDurationSeconds
               OR ISNULL(i.DeadlinePolicy, '''') <> ISNULL(d.DeadlinePolicy, '''')))
        THROW 51006, N''Không được đổi luật thời gian/điểm sau khi đã có trận tiếp sức.'', 1;
END;');

COMMIT TRANSACTION;
