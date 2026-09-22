-- Run before deploying the updated server. Does not assign any account automatically.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF COL_LENGTH('dbo.TournamentGroupMatches', 'MatchStatus') IS NULL
    BEGIN
        ALTER TABLE dbo.TournamentGroupMatches ADD MatchStatus varchar(20) NOT NULL
            CONSTRAINT DF_TournamentGroupMatches_MatchStatus DEFAULT 'NOT_STARTED';
        EXEC(N'UPDATE m SET MatchStatus = CASE
            WHEN m.IsCompleted = 1 THEN ''COMPLETED''
            WHEN m.ScoreTeam1 <> 0 OR m.ScoreTeam2 <> 0 OR EXISTS (
                SELECT 1 FROM dbo.TournamentMatchScoreHistories h WHERE h.MatchId = m.MatchId
                AND h.IsCompleted = 0 AND h.ScoreTeam1 = m.ScoreTeam1 AND h.ScoreTeam2 = m.ScoreTeam2
                AND (m.UpdatedAt IS NULL OR h.CreatedAt >= m.UpdatedAt)
            ) THEN ''IN_PROGRESS'' ELSE ''NOT_STARTED'' END
            FROM dbo.TournamentGroupMatches m');
    END;
    IF COL_LENGTH('dbo.TournamentGroupMatches', 'StateVersion') IS NULL
        ALTER TABLE dbo.TournamentGroupMatches ADD StateVersion bigint NOT NULL
            CONSTRAINT DF_TournamentGroupMatches_StateVersion DEFAULT 1;
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_TournamentGroupMatches_MatchStatus')
        EXEC(N'ALTER TABLE dbo.TournamentGroupMatches ADD CONSTRAINT CK_TournamentGroupMatches_MatchStatus
            CHECK (MatchStatus IN (''NOT_STARTED'', ''PREPARING'', ''IN_PROGRESS'', ''COMPLETED''))');

    IF OBJECT_ID('dbo.TournamentCoordinators', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.TournamentCoordinators (
            TournamentId bigint NOT NULL, UserId bigint NOT NULL, CreatedAt datetime2 NOT NULL,
            CONSTRAINT PK_TournamentCoordinators PRIMARY KEY (TournamentId, UserId),
            CONSTRAINT FK_TournamentCoordinators_Tournament FOREIGN KEY (TournamentId) REFERENCES dbo.Tournaments(TournamentId) ON DELETE CASCADE,
            CONSTRAINT FK_TournamentCoordinators_User FOREIGN KEY (UserId) REFERENCES dbo.Users(UserId)
        );
    END;
    IF OBJECT_ID('dbo.MatchCoordinationHistories', 'U') IS NULL
    BEGIN
        CREATE TABLE dbo.MatchCoordinationHistories (
            Id bigint IDENTITY PRIMARY KEY, MatchId bigint NOT NULL, ActorUserId bigint NOT NULL,
            PreviousCourt nvarchar(200) NULL, Court nvarchar(200) NULL,
            PreviousStatus varchar(20) NOT NULL, Status varchar(20) NOT NULL, CreatedAt datetime2 NOT NULL,
            CONSTRAINT FK_MatchCoordinationHistories_Match FOREIGN KEY (MatchId) REFERENCES dbo.TournamentGroupMatches(MatchId) ON DELETE CASCADE,
            CONSTRAINT FK_MatchCoordinationHistories_User FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(UserId)
        );
        CREATE INDEX IX_MatchCoordinationHistories_MatchId_CreatedAt ON dbo.MatchCoordinationHistories(MatchId, CreatedAt);
    END;
    IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE RoleCode = 'COORDINATOR')
        INSERT dbo.Roles(RoleCode, RoleName) VALUES ('COORDINATOR', N'Điều phối giải đấu');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
