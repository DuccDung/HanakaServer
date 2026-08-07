SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.BracketTemplateVersions', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.BracketTemplateVersions', N'DraftGraphJson') IS NULL
    BEGIN
        ALTER TABLE dbo.BracketTemplateVersions
            ADD DraftGraphJson nvarchar(max) NULL;
    END;

    IF OBJECT_ID(N'dbo.TournamentRoundMaps', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.TournamentRoundMaps', N'TemplateRoundType') IS NULL
    BEGIN
        ALTER TABLE dbo.TournamentRoundMaps
            ADD TemplateRoundType varchar(30) NULL;
    END;

    IF OBJECT_ID(N'dbo.TournamentRoundGroups', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.TournamentRoundGroups', N'TemplateGroupType') IS NULL
    BEGIN
        ALTER TABLE dbo.TournamentRoundGroups
            ADD TemplateGroupType varchar(30) NULL;
    END;

    IF OBJECT_ID(N'dbo.TournamentGroupMatches', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.TournamentGroupMatches', N'TemplateMatchLabel') IS NULL
    BEGIN
        ALTER TABLE dbo.TournamentGroupMatches
            ADD TemplateMatchLabel nvarchar(100) NULL;
    END;

    IF OBJECT_ID(N'dbo.TournamentGroupMatches', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.TournamentGroupMatches', N'TemplateIsTerminal') IS NULL
    BEGIN
        ALTER TABLE dbo.TournamentGroupMatches
            ADD TemplateIsTerminal bit NULL;
    END;

    IF OBJECT_ID(N'dbo.TournamentGroupMatches', N'U') IS NOT NULL
       AND COL_LENGTH(N'dbo.TournamentGroupMatches', N'TemplateTerminalType') IS NULL
    BEGIN
        ALTER TABLE dbo.TournamentGroupMatches
            ADD TemplateTerminalType varchar(30) NULL;
    END;

    IF OBJECT_ID(N'dbo.TournamentRoundMaps', N'U') IS NOT NULL
       AND OBJECT_ID(N'dbo.TournamentBracketApplications', N'U') IS NOT NULL
       AND OBJECT_ID(N'dbo.BracketTemplateRounds', N'U') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'
            UPDATE runtimeRound
            SET runtimeRound.TemplateRoundType = templateRound.RoundType
            FROM dbo.TournamentRoundMaps AS runtimeRound
            INNER JOIN dbo.TournamentBracketApplications AS application
                ON application.TournamentBracketApplicationId = runtimeRound.BracketApplicationId
            INNER JOIN dbo.BracketTemplateRounds AS templateRound
                ON templateRound.BracketTemplateVersionId = application.BracketTemplateVersionId
               AND templateRound.RoundKey = runtimeRound.TemplateRoundKey
            WHERE runtimeRound.TemplateRoundType IS NULL;';
    END;

    IF OBJECT_ID(N'dbo.TournamentRoundGroups', N'U') IS NOT NULL
       AND OBJECT_ID(N'dbo.TournamentBracketApplications', N'U') IS NOT NULL
       AND OBJECT_ID(N'dbo.BracketTemplateGroups', N'U') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'
            UPDATE runtimeGroup
            SET runtimeGroup.TemplateGroupType = templateGroup.GroupType
            FROM dbo.TournamentRoundGroups AS runtimeGroup
            INNER JOIN dbo.TournamentBracketApplications AS application
                ON application.TournamentBracketApplicationId = runtimeGroup.BracketApplicationId
            INNER JOIN dbo.BracketTemplateGroups AS templateGroup
                ON templateGroup.BracketTemplateVersionId = application.BracketTemplateVersionId
               AND templateGroup.GroupKey = runtimeGroup.TemplateGroupKey
            WHERE runtimeGroup.TemplateGroupType IS NULL;';
    END;

    IF OBJECT_ID(N'dbo.TournamentGroupMatches', N'U') IS NOT NULL
       AND OBJECT_ID(N'dbo.TournamentBracketApplications', N'U') IS NOT NULL
       AND OBJECT_ID(N'dbo.BracketTemplateMatches', N'U') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'
            UPDATE runtimeMatch
            SET runtimeMatch.TemplateMatchLabel = templateMatch.MatchLabel,
                runtimeMatch.TemplateIsTerminal = templateMatch.IsTerminal,
                runtimeMatch.TemplateTerminalType = templateMatch.TerminalType
            FROM dbo.TournamentGroupMatches AS runtimeMatch
            INNER JOIN dbo.TournamentBracketApplications AS application
                ON application.TournamentBracketApplicationId = runtimeMatch.BracketApplicationId
            INNER JOIN dbo.BracketTemplateMatches AS templateMatch
                ON templateMatch.BracketTemplateVersionId = application.BracketTemplateVersionId
               AND templateMatch.MatchKey = runtimeMatch.TemplateMatchKey
            WHERE runtimeMatch.TemplateMatchLabel IS NULL
               OR runtimeMatch.TemplateIsTerminal IS NULL
               OR runtimeMatch.TemplateTerminalType IS NULL;';
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
