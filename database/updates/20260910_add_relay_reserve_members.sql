-- Optional registration reserves. Apply before deploying the corresponding server build.
-- No playing lineup, bracket, score or historical snapshot is changed.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.RelayTeams', N'U') IS NULL
    THROW 51000, 'Apply the relay foundation scripts first.', 1;

IF OBJECT_ID(N'dbo.RelayTeamReserveMembers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RelayTeamReserveMembers (
        RegistrationId bigint NOT NULL,
        Position int NOT NULL,
        UserId bigint NULL,
        DisplayName nvarchar(150) NOT NULL,
        AvatarUrl nvarchar(500) NULL,
        CONSTRAINT PK_RelayTeamReserveMembers PRIMARY KEY (RegistrationId, Position),
        CONSTRAINT CK_RelayReserves_Position CHECK (Position BETWEEN 1 AND 4),
        CONSTRAINT CK_RelayReserves_Name CHECK (LEN(LTRIM(RTRIM(DisplayName))) > 0),
        CONSTRAINT FK_RelayTeamReserveMembers_RelayTeams_RegistrationId
            FOREIGN KEY (RegistrationId) REFERENCES dbo.RelayTeams(RegistrationId),
        CONSTRAINT FK_RelayTeamReserveMembers_Users_UserId
            FOREIGN KEY (UserId) REFERENCES dbo.Users(UserId)
    );
    CREATE UNIQUE INDEX IX_RelayTeamReserveMembers_RegistrationId_UserId
        ON dbo.RelayTeamReserveMembers(RegistrationId, UserId) WHERE UserId IS NOT NULL;
    CREATE INDEX IX_RelayTeamReserveMembers_UserId ON dbo.RelayTeamReserveMembers(UserId);
END;

COMMIT;
