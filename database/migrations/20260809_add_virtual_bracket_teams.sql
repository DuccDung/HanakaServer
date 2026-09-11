/*
    Hanaka Sport - virtual bracket teams
    Target: Microsoft SQL Server
    Safe to run repeatedly. Existing registrations remain real teams.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.TournamentRegistrations', N'U') IS NULL
        THROW 51100, N'Không tìm thấy dbo.TournamentRegistrations.', 1;
    IF OBJECT_ID(N'dbo.TournamentBracketApplications', N'U') IS NULL
        THROW 51101, N'Không tìm thấy dbo.TournamentBracketApplications.', 1;
    IF OBJECT_ID(N'dbo.TournamentBracketSeedAssignments', N'U') IS NULL
        THROW 51102, N'Không tìm thấy dbo.TournamentBracketSeedAssignments.', 1;

    IF COL_LENGTH(N'dbo.TournamentRegistrations', N'IsVirtualTeam') IS NULL
    BEGIN
        ALTER TABLE dbo.TournamentRegistrations
            ADD IsVirtualTeam bit NOT NULL
                CONSTRAINT DF_TournamentRegistrations_IsVirtualTeam DEFAULT (0) WITH VALUES;
    END;

    IF COL_LENGTH(N'dbo.TournamentRegistrations', N'VirtualBracketApplicationId') IS NULL
    BEGIN
        ALTER TABLE dbo.TournamentRegistrations
            ADD VirtualBracketApplicationId bigint NULL;
    END;

    IF COL_LENGTH(N'dbo.TournamentBracketApplications', N'VirtualTeamCount') IS NULL
    BEGIN
        ALTER TABLE dbo.TournamentBracketApplications
            ADD VirtualTeamCount int NOT NULL
                CONSTRAINT DF_TournamentBracketApplications_VirtualTeamCount DEFAULT (0) WITH VALUES;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE [name] = N'CK_TournamentBracketApplications_Counts'
          AND parent_object_id = OBJECT_ID(N'dbo.TournamentBracketApplications')
    )
    BEGIN
        ALTER TABLE dbo.TournamentBracketApplications
            DROP CONSTRAINT CK_TournamentBracketApplications_Counts;
    END;

    EXEC sys.sp_executesql N'
        ALTER TABLE dbo.TournamentBracketApplications WITH CHECK
            ADD CONSTRAINT CK_TournamentBracketApplications_Counts
                CHECK
                (
                    EligibleRegistrationCount >= 0
                    AND VirtualTeamCount >= 0
                    AND SeedCapacity >= 2
                    AND ByeCount >= 0
                    AND ByeCount <= SeedCapacity
                    AND EligibleRegistrationCount + VirtualTeamCount + ByeCount = SeedCapacity
                );';

    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE [name] = N'CK_TournamentBracketSeedAssignments_AssignmentMethod'
          AND parent_object_id = OBJECT_ID(N'dbo.TournamentBracketSeedAssignments')
    )
    BEGIN
        ALTER TABLE dbo.TournamentBracketSeedAssignments
            DROP CONSTRAINT CK_TournamentBracketSeedAssignments_AssignmentMethod;
    END;

    ALTER TABLE dbo.TournamentBracketSeedAssignments WITH CHECK
        ADD CONSTRAINT CK_TournamentBracketSeedAssignments_AssignmentMethod
            CHECK
            (
                AssignmentMethod IN
                (
                    'REGISTRATION_ORDER',
                    'RANDOM',
                    'MANUAL',
                    'RANKING',
                    'BYE',
                    'VIRTUAL'
                )
            );

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys
        WHERE [name] = N'FK_TournamentRegistrations_VirtualBracketApplication'
          AND parent_object_id = OBJECT_ID(N'dbo.TournamentRegistrations')
    )
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.TournamentRegistrations WITH CHECK
                ADD CONSTRAINT FK_TournamentRegistrations_VirtualBracketApplication
                    FOREIGN KEY (VirtualBracketApplicationId)
                    REFERENCES dbo.TournamentBracketApplications (TournamentBracketApplicationId);';
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE [name] = N'CK_TournamentRegistrations_VirtualTeam'
          AND parent_object_id = OBJECT_ID(N'dbo.TournamentRegistrations')
    )
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.TournamentRegistrations WITH CHECK
                ADD CONSTRAINT CK_TournamentRegistrations_VirtualTeam
                    CHECK
                    (
                        (IsVirtualTeam = 0 AND VirtualBracketApplicationId IS NULL)
                        OR
                        (
                            IsVirtualTeam = 1
                            AND VirtualBracketApplicationId IS NOT NULL
                            AND Player1UserId IS NULL
                            AND Player2UserId IS NULL
                        )
                    );';
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE [name] = N'IX_TournamentRegistrations_VirtualBracketApplication'
          AND object_id = OBJECT_ID(N'dbo.TournamentRegistrations')
    )
    BEGIN
        EXEC sys.sp_executesql N'
            CREATE INDEX IX_TournamentRegistrations_VirtualBracketApplication
                ON dbo.TournamentRegistrations (VirtualBracketApplicationId, IsVirtualTeam)
                WHERE VirtualBracketApplicationId IS NOT NULL;';
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
