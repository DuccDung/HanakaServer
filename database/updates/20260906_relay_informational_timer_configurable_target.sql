SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.RelayTournamentSettings', N'U') IS NULL
    THROW 51020, N'Apply 20260906_add_relay_foundation.sql first.', 1;

IF OBJECT_ID(N'dbo.CK_RelaySettings_Policy', N'C') IS NOT NULL
    ALTER TABLE dbo.RelayTournamentSettings DROP CONSTRAINT CK_RelaySettings_Policy;

IF OBJECT_ID(N'dbo.CK_RelayMatch_Policy', N'C') IS NOT NULL
    ALTER TABLE dbo.RelayMatchStates DROP CONSTRAINT CK_RelayMatch_Policy;

IF OBJECT_ID(N'dbo.TR_RelaySettings_LockedRules', N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_RelaySettings_LockedRules;

IF COL_LENGTH(N'dbo.RelayMatchStates', N'DeadlinePolicy') IS NOT NULL
    ALTER TABLE dbo.RelayMatchStates ALTER COLUMN DeadlinePolicy varchar(30) NULL;

UPDATE dbo.RelayTournamentSettings
SET LegDurationSeconds = 600,
    DeadlinePolicy = NULL
WHERE LegDurationSeconds <> 600 OR DeadlinePolicy IS NOT NULL;

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
          AND (i.TargetScore <> d.TargetScore OR i.LegDurationSeconds <> d.LegDurationSeconds))
        THROW 51006, N''Không được đổi điểm đích hoặc thời lượng hiển thị sau khi đã có trận tiếp sức.'', 1;
END;');

COMMIT TRANSACTION;
