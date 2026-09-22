-- Apply before deploying the three-part scoreboard. Existing scores/history are preserved.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.TournamentGroupMatches', N'U') IS NULL
    OR OBJECT_ID(N'dbo.TournamentMatchScoreHistories', N'U') IS NULL
    THROW 51040, 'Apply the tournament and score history schema first.', 1;

IF OBJECT_ID(N'dbo.RelayMatchScores', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RelayMatchScores (
        MatchId bigint NOT NULL,
        Part1Team1 int NOT NULL, Part1Team2 int NOT NULL,
        Part2Team1 int NOT NULL, Part2Team2 int NOT NULL,
        Part3Team1 int NOT NULL, Part3Team2 int NOT NULL,
        Version bigint NOT NULL,
        CONSTRAINT PK_RelayMatchScores PRIMARY KEY (MatchId),
        CONSTRAINT FK_RelayMatchScores_Match FOREIGN KEY (MatchId)
            REFERENCES dbo.TournamentGroupMatches(MatchId) ON DELETE CASCADE,
        CONSTRAINT CK_RelayMatchScores_Values CHECK (
            Part1Team1 >= 0 AND Part1Team2 >= 0 AND Part2Team1 >= 0 AND Part2Team2 >= 0
            AND Part3Team1 >= 0 AND Part3Team2 >= 0 AND Version > 0)
    );
END;

IF COL_LENGTH(N'dbo.TournamentMatchScoreHistories', N'RelayPartNumber') IS NULL
    ALTER TABLE dbo.TournamentMatchScoreHistories ADD RelayPartNumber int NULL;
IF COL_LENGTH(N'dbo.TournamentMatchScoreHistories', N'RelayPartsJson') IS NULL
    ALTER TABLE dbo.TournamentMatchScoreHistories ADD RelayPartsJson nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.TournamentMatchScoreHistories', N'ActorName') IS NULL
    ALTER TABLE dbo.TournamentMatchScoreHistories ADD ActorName nvarchar(max) NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.TournamentMatchScoreHistories')
    AND name = N'RefereeUserId' AND is_nullable = 0)
BEGIN
    DECLARE @hadRefereeIndex bit = CASE WHEN EXISTS (SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.TournamentMatchScoreHistories') AND name = N'IX_TMSH_RefereeUserId') THEN 1 ELSE 0 END;
    DECLARE @hadRefereeFk bit = CASE WHEN OBJECT_ID(N'dbo.FK_TMSH_RefereeUser', N'F') IS NOT NULL THEN 1 ELSE 0 END;
    IF @hadRefereeFk = 1 ALTER TABLE dbo.TournamentMatchScoreHistories DROP CONSTRAINT FK_TMSH_RefereeUser;
    IF @hadRefereeIndex = 1 DROP INDEX IX_TMSH_RefereeUserId ON dbo.TournamentMatchScoreHistories;
    ALTER TABLE dbo.TournamentMatchScoreHistories ALTER COLUMN RefereeUserId bigint NULL;
    IF @hadRefereeIndex = 1 CREATE INDEX IX_TMSH_RefereeUserId ON dbo.TournamentMatchScoreHistories(RefereeUserId);
    IF @hadRefereeFk = 1 ALTER TABLE dbo.TournamentMatchScoreHistories WITH CHECK
        ADD CONSTRAINT FK_TMSH_RefereeUser FOREIGN KEY (RefereeUserId) REFERENCES dbo.Users(UserId);
END;

COMMIT;
