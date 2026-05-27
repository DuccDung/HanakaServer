/*
    Add bracket/playoff source slots to dbo.TournamentGroupMatches.

    This is a database-first update script based on hanaka.sql.
    Run on SQL Server after backing up the database.
*/

SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.TournamentGroupMatches', N'U') IS NULL
        THROW 51000, 'Table dbo.TournamentGroupMatches was not found.', 1;

    IF OBJECT_ID(N'dbo.TournamentRegistrations', N'U') IS NULL
        THROW 51001, 'Table dbo.TournamentRegistrations was not found.', 1;

    IF OBJECT_ID(N'dbo.TournamentRoundGroups', N'U') IS NULL
        THROW 51002, 'Table dbo.TournamentRoundGroups was not found.', 1;

    IF EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'UX_TGM_Group_TeamPair'
    )
        DROP INDEX [UX_TGM_Group_TeamPair] ON [dbo].[TournamentGroupMatches];

    IF EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'IX_TGM_Team1'
    )
        DROP INDEX [IX_TGM_Team1] ON [dbo].[TournamentGroupMatches];

    IF EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'IX_TGM_Team2'
    )
        DROP INDEX [IX_TGM_Team2] ON [dbo].[TournamentGroupMatches];

    IF EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'CK_TGM_TeamsDifferent'
    )
        ALTER TABLE [dbo].[TournamentGroupMatches] DROP CONSTRAINT [CK_TGM_TeamsDifferent];

    IF EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'FK_TGM_Team1'
    )
        ALTER TABLE [dbo].[TournamentGroupMatches] DROP CONSTRAINT [FK_TGM_Team1];

    IF EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'FK_TGM_Team2'
    )
        ALTER TABLE [dbo].[TournamentGroupMatches] DROP CONSTRAINT [FK_TGM_Team2];

    IF EXISTS (
        SELECT 1
        FROM sys.computed_columns
        WHERE object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'TeamMin'
    )
        ALTER TABLE [dbo].[TournamentGroupMatches] DROP COLUMN [TeamMin];

    IF EXISTS (
        SELECT 1
        FROM sys.computed_columns
        WHERE object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'TeamMax'
    )
        ALTER TABLE [dbo].[TournamentGroupMatches] DROP COLUMN [TeamMax];

    IF EXISTS (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'Team1RegistrationId'
          AND is_nullable = 0
    )
        ALTER TABLE [dbo].[TournamentGroupMatches] ALTER COLUMN [Team1RegistrationId] BIGINT NULL;

    IF EXISTS (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'Team2RegistrationId'
          AND is_nullable = 0
    )
        ALTER TABLE [dbo].[TournamentGroupMatches] ALTER COLUMN [Team2RegistrationId] BIGINT NULL;

    IF COL_LENGTH(N'dbo.TournamentGroupMatches', N'Team1SourceType') IS NULL
        ALTER TABLE [dbo].[TournamentGroupMatches]
            ADD [Team1SourceType] VARCHAR(30) NOT NULL
                CONSTRAINT [DF_TGM_Team1SourceType] DEFAULT ('REGISTRATION')
                WITH VALUES;

    IF COL_LENGTH(N'dbo.TournamentGroupMatches', N'Team1SourceMatchId') IS NULL
        ALTER TABLE [dbo].[TournamentGroupMatches] ADD [Team1SourceMatchId] BIGINT NULL;

    IF COL_LENGTH(N'dbo.TournamentGroupMatches', N'Team1SourceGroupId') IS NULL
        ALTER TABLE [dbo].[TournamentGroupMatches] ADD [Team1SourceGroupId] BIGINT NULL;

    IF COL_LENGTH(N'dbo.TournamentGroupMatches', N'Team1SourceRank') IS NULL
        ALTER TABLE [dbo].[TournamentGroupMatches] ADD [Team1SourceRank] INT NULL;

    IF COL_LENGTH(N'dbo.TournamentGroupMatches', N'Team2SourceType') IS NULL
        ALTER TABLE [dbo].[TournamentGroupMatches]
            ADD [Team2SourceType] VARCHAR(30) NOT NULL
                CONSTRAINT [DF_TGM_Team2SourceType] DEFAULT ('REGISTRATION')
                WITH VALUES;

    IF COL_LENGTH(N'dbo.TournamentGroupMatches', N'Team2SourceMatchId') IS NULL
        ALTER TABLE [dbo].[TournamentGroupMatches] ADD [Team2SourceMatchId] BIGINT NULL;

    IF COL_LENGTH(N'dbo.TournamentGroupMatches', N'Team2SourceGroupId') IS NULL
        ALTER TABLE [dbo].[TournamentGroupMatches] ADD [Team2SourceGroupId] BIGINT NULL;

    IF COL_LENGTH(N'dbo.TournamentGroupMatches', N'Team2SourceRank') IS NULL
        ALTER TABLE [dbo].[TournamentGroupMatches] ADD [Team2SourceRank] INT NULL;

    EXEC(N'
        UPDATE [dbo].[TournamentGroupMatches]
        SET
            [Team1SourceType] = CASE UPPER(COALESCE(NULLIF(LTRIM(RTRIM([Team1SourceType])), ''''), ''REGISTRATION''))
                WHEN ''REGISTRATION'' THEN ''REGISTRATION''
                WHEN ''WINNER_MATCH'' THEN ''WINNER_MATCH''
                WHEN ''LOSER_MATCH'' THEN ''LOSER_MATCH''
                WHEN ''GROUP_RANK'' THEN ''GROUP_RANK''
                WHEN ''BYE'' THEN ''BYE''
                ELSE ''REGISTRATION''
            END,
            [Team2SourceType] = CASE UPPER(COALESCE(NULLIF(LTRIM(RTRIM([Team2SourceType])), ''''), ''REGISTRATION''))
                WHEN ''REGISTRATION'' THEN ''REGISTRATION''
                WHEN ''WINNER_MATCH'' THEN ''WINNER_MATCH''
                WHEN ''LOSER_MATCH'' THEN ''LOSER_MATCH''
                WHEN ''GROUP_RANK'' THEN ''GROUP_RANK''
                WHEN ''BYE'' THEN ''BYE''
                ELSE ''REGISTRATION''
            END;
    ');

    IF NOT EXISTS (
        SELECT 1
        FROM sys.default_constraints dc
        INNER JOIN sys.columns c
            ON c.object_id = dc.parent_object_id
           AND c.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND c.name = N'Team1SourceType'
    )
        EXEC(N'
            ALTER TABLE [dbo].[TournamentGroupMatches]
                ADD CONSTRAINT [DF_TGM_Team1SourceType]
                DEFAULT (''REGISTRATION'') FOR [Team1SourceType];
        ');

    IF NOT EXISTS (
        SELECT 1
        FROM sys.default_constraints dc
        INNER JOIN sys.columns c
            ON c.object_id = dc.parent_object_id
           AND c.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND c.name = N'Team2SourceType'
    )
        EXEC(N'
            ALTER TABLE [dbo].[TournamentGroupMatches]
                ADD CONSTRAINT [DF_TGM_Team2SourceType]
                DEFAULT (''REGISTRATION'') FOR [Team2SourceType];
        ');

    IF COL_LENGTH(N'dbo.TournamentGroupMatches', N'TeamMin') IS NULL
        ALTER TABLE [dbo].[TournamentGroupMatches]
            ADD [TeamMin] AS (
                CASE
                    WHEN [Team1RegistrationId] IS NULL OR [Team2RegistrationId] IS NULL THEN NULL
                    WHEN [Team1RegistrationId] < [Team2RegistrationId] THEN [Team1RegistrationId]
                    ELSE [Team2RegistrationId]
                END
            ) PERSISTED;

    IF COL_LENGTH(N'dbo.TournamentGroupMatches', N'TeamMax') IS NULL
        ALTER TABLE [dbo].[TournamentGroupMatches]
            ADD [TeamMax] AS (
                CASE
                    WHEN [Team1RegistrationId] IS NULL OR [Team2RegistrationId] IS NULL THEN NULL
                    WHEN [Team1RegistrationId] > [Team2RegistrationId] THEN [Team1RegistrationId]
                    ELSE [Team2RegistrationId]
                END
            ) PERSISTED;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'FK_TGM_Team1'
    )
        ALTER TABLE [dbo].[TournamentGroupMatches] WITH CHECK
            ADD CONSTRAINT [FK_TGM_Team1]
            FOREIGN KEY ([Team1RegistrationId])
            REFERENCES [dbo].[TournamentRegistrations] ([RegistrationId]);

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'FK_TGM_Team2'
    )
        ALTER TABLE [dbo].[TournamentGroupMatches] WITH CHECK
            ADD CONSTRAINT [FK_TGM_Team2]
            FOREIGN KEY ([Team2RegistrationId])
            REFERENCES [dbo].[TournamentRegistrations] ([RegistrationId]);

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'FK_TGM_Team1SourceMatch'
    )
        EXEC(N'
            ALTER TABLE [dbo].[TournamentGroupMatches] WITH CHECK
                ADD CONSTRAINT [FK_TGM_Team1SourceMatch]
                FOREIGN KEY ([Team1SourceMatchId])
                REFERENCES [dbo].[TournamentGroupMatches] ([MatchId]);
        ');

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'FK_TGM_Team2SourceMatch'
    )
        EXEC(N'
            ALTER TABLE [dbo].[TournamentGroupMatches] WITH CHECK
                ADD CONSTRAINT [FK_TGM_Team2SourceMatch]
                FOREIGN KEY ([Team2SourceMatchId])
                REFERENCES [dbo].[TournamentGroupMatches] ([MatchId]);
        ');

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'FK_TGM_Team1SourceGroup'
    )
        EXEC(N'
            ALTER TABLE [dbo].[TournamentGroupMatches] WITH CHECK
                ADD CONSTRAINT [FK_TGM_Team1SourceGroup]
                FOREIGN KEY ([Team1SourceGroupId])
                REFERENCES [dbo].[TournamentRoundGroups] ([TournamentRoundGroupId]);
        ');

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'FK_TGM_Team2SourceGroup'
    )
        EXEC(N'
            ALTER TABLE [dbo].[TournamentGroupMatches] WITH CHECK
                ADD CONSTRAINT [FK_TGM_Team2SourceGroup]
                FOREIGN KEY ([Team2SourceGroupId])
                REFERENCES [dbo].[TournamentRoundGroups] ([TournamentRoundGroupId]);
        ');

    IF NOT EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'CK_TGM_TeamsDifferent'
    )
        ALTER TABLE [dbo].[TournamentGroupMatches] WITH CHECK
            ADD CONSTRAINT [CK_TGM_TeamsDifferent]
            CHECK (
                [Team1RegistrationId] IS NULL
                OR [Team2RegistrationId] IS NULL
                OR [Team1RegistrationId] <> [Team2RegistrationId]
            );

    IF NOT EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'CK_TGM_Team1SourceType'
    )
        EXEC(N'
            ALTER TABLE [dbo].[TournamentGroupMatches] WITH CHECK
                ADD CONSTRAINT [CK_TGM_Team1SourceType]
                CHECK ([Team1SourceType] IN (''REGISTRATION'', ''WINNER_MATCH'', ''LOSER_MATCH'', ''GROUP_RANK'', ''BYE''));
        ');

    IF NOT EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'CK_TGM_Team2SourceType'
    )
        EXEC(N'
            ALTER TABLE [dbo].[TournamentGroupMatches] WITH CHECK
                ADD CONSTRAINT [CK_TGM_Team2SourceType]
                CHECK ([Team2SourceType] IN (''REGISTRATION'', ''WINNER_MATCH'', ''LOSER_MATCH'', ''GROUP_RANK'', ''BYE''));
        ');

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'IX_TGM_Team1'
    )
        CREATE NONCLUSTERED INDEX [IX_TGM_Team1]
            ON [dbo].[TournamentGroupMatches] ([Team1RegistrationId] ASC);

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'IX_TGM_Team2'
    )
        CREATE NONCLUSTERED INDEX [IX_TGM_Team2]
            ON [dbo].[TournamentGroupMatches] ([Team2RegistrationId] ASC);

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'UX_TGM_Group_TeamPair'
    )
        EXEC(N'
            CREATE UNIQUE NONCLUSTERED INDEX [UX_TGM_Group_TeamPair]
                ON [dbo].[TournamentGroupMatches] ([TournamentRoundGroupId] ASC, [TeamMin] ASC, [TeamMax] ASC)
                WHERE [Team1RegistrationId] IS NOT NULL AND [Team2RegistrationId] IS NOT NULL;
        ');

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'IX_TGM_Team1SourceMatch'
    )
        EXEC(N'
            CREATE NONCLUSTERED INDEX [IX_TGM_Team1SourceMatch]
                ON [dbo].[TournamentGroupMatches] ([Team1SourceMatchId] ASC, [Team1SourceType] ASC);
        ');

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'IX_TGM_Team2SourceMatch'
    )
        EXEC(N'
            CREATE NONCLUSTERED INDEX [IX_TGM_Team2SourceMatch]
                ON [dbo].[TournamentGroupMatches] ([Team2SourceMatchId] ASC, [Team2SourceType] ASC);
        ');

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'IX_TGM_Team1SourceGroup'
    )
        EXEC(N'
            CREATE NONCLUSTERED INDEX [IX_TGM_Team1SourceGroup]
                ON [dbo].[TournamentGroupMatches] ([Team1SourceGroupId] ASC, [Team1SourceType] ASC, [Team1SourceRank] ASC);
        ');

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.TournamentGroupMatches')
          AND name = N'IX_TGM_Team2SourceGroup'
    )
        EXEC(N'
            CREATE NONCLUSTERED INDEX [IX_TGM_Team2SourceGroup]
                ON [dbo].[TournamentGroupMatches] ([Team2SourceGroupId] ASC, [Team2SourceType] ASC, [Team2SourceRank] ASC);
        ');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
GO
