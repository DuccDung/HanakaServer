-- Apply after 20260906_add_relay_foundation.sql and the existing bracket library schema.
-- Additive only. No template/registration/match data is rewritten.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.RelayTeams', N'U') IS NULL
    THROW 51110, 'Apply relay foundation schema first.', 1;
IF OBJECT_ID(N'dbo.TournamentBracketApplications', N'U') IS NULL
    THROW 51111, 'Apply existing bracket library schema first.', 1;

IF OBJECT_ID(N'dbo.RelayBracketSeedSnapshots', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RelayBracketSeedSnapshots (
        TournamentBracketApplicationId bigint NOT NULL,
        SeedNumber int NOT NULL,
        TeamName nvarchar(150) NOT NULL,
        LineupJson nvarchar(max) NOT NULL,
        CONSTRAINT PK_RelayBracketSeedSnapshots PRIMARY KEY (TournamentBracketApplicationId, SeedNumber),
        CONSTRAINT FK_RelayBracketSeedSnapshots_Application FOREIGN KEY (TournamentBracketApplicationId)
            REFERENCES dbo.TournamentBracketApplications(TournamentBracketApplicationId),
        CONSTRAINT CK_RelayBracketSeed_Number CHECK (SeedNumber > 0),
        CONSTRAINT CK_RelayBracketSeed_Json CHECK (ISJSON(LineupJson) = 1)
    );
END;
COMMIT TRANSACTION;
