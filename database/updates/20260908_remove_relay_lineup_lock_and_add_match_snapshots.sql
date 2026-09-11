SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.RelayTeams', N'U') IS NULL
    THROW 51030, N'Apply 20260906_add_relay_foundation.sql first.', 1;

IF OBJECT_ID(N'dbo.RelayMatchLineupSnapshots', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RelayMatchLineupSnapshots (
        MatchId bigint NOT NULL,
        Side tinyint NOT NULL,
        RegistrationId bigint NOT NULL,
        TeamName nvarchar(150) NOT NULL,
        LineupVersion bigint NOT NULL,
        CapturedAtUtc datetimeoffset(7) NOT NULL,
        LineupJson nvarchar(max) NOT NULL,
        CONSTRAINT PK_RelayMatchLineupSnapshots PRIMARY KEY (MatchId, Side),
        CONSTRAINT FK_RelayMatchLineupSnapshots_Match FOREIGN KEY (MatchId)
            REFERENCES dbo.TournamentGroupMatches(MatchId) ON DELETE CASCADE,
        CONSTRAINT CK_RelayMatchLineupSnapshots_Side CHECK (Side IN (1,2)),
        CONSTRAINT CK_RelayMatchLineupSnapshots_Json CHECK (ISJSON(LineupJson) = 1)
    );
    CREATE INDEX IX_RelayMatchLineupSnapshots_RegistrationId
        ON dbo.RelayMatchLineupSnapshots(RegistrationId);
END;

-- Roster edits are allowed. The old lock timestamp remains as legacy data only.
IF OBJECT_ID(N'dbo.TR_RelayMembers_FixedLineup', N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_RelayMembers_FixedLineup;

IF OBJECT_ID(N'dbo.TR_RelayTeams_ValidateLock', N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_RelayTeams_ValidateLock;

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_RelayTeams_ValidateOwnership ON dbo.RelayTeams
AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN dbo.TournamentRegistrations r ON r.RegistrationId = i.RegistrationId
        WHERE r.TournamentId <> i.TournamentId)
        THROW 51003, N''Đăng ký đội phải thuộc đúng giải.'', 1;
END;');

IF OBJECT_ID(N'dbo.TR_RelaySettings_LockedRules', N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_RelaySettings_LockedRules;

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_RelaySettings_ProtectedRules ON dbo.RelayTournamentSettings
AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.TournamentId = d.TournamentId
        WHERE EXISTS (SELECT 1 FROM dbo.RelayTeams t WITH (UPDLOCK, HOLDLOCK)
                      WHERE t.TournamentId = d.TournamentId)
          AND (i.TournamentId IS NULL OR i.TeamSize <> d.TeamSize))
        THROW 51005, N''Không được đổi quy mô đội sau khi giải đã có đội hình.'', 1;
    IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.TournamentId = d.TournamentId
        WHERE EXISTS (SELECT 1 FROM dbo.RelayMatchStates m WHERE m.TournamentId = d.TournamentId)
          AND (i.TargetScore <> d.TargetScore OR i.LegDurationSeconds <> d.LegDurationSeconds))
        THROW 51006, N''Không được đổi điểm đích hoặc thời lượng hiển thị sau khi đã có trận tiếp sức.'', 1;
END;');

COMMIT TRANSACTION;
