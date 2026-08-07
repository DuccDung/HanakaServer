SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.BracketTemplateMatchSlots', N'U') IS NOT NULL
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.BracketTemplateMatchSlots')
          AND name = N'CK_BracketTemplateMatchSlots_SourcePayload'
    )
    BEGIN
        ALTER TABLE dbo.BracketTemplateMatchSlots
            DROP CONSTRAINT CK_BracketTemplateMatchSlots_SourcePayload;
    END;

    ALTER TABLE dbo.BracketTemplateMatchSlots WITH CHECK
        ADD CONSTRAINT CK_BracketTemplateMatchSlots_SourcePayload
        CHECK
        (
            (
                SourceType = 'SEED'
                AND (SeedNumber IS NULL OR SeedNumber > 0)
                AND SourceMatchId IS NULL
                AND SourceGroupId IS NULL
                AND SourceRank IS NULL
            )
            OR
            (
                SourceType IN ('WINNER_MATCH', 'LOSER_MATCH')
                AND SeedNumber IS NULL
                AND SourceMatchId IS NOT NULL
                AND SourceGroupId IS NULL
                AND SourceRank IS NULL
            )
            OR
            (
                SourceType = 'GROUP_RANK'
                AND SeedNumber IS NULL
                AND SourceMatchId IS NULL
                AND SourceGroupId IS NOT NULL
                AND SourceRank IS NOT NULL
                AND SourceRank > 0
            )
            OR
            (
                SourceType = 'BYE'
                AND SeedNumber IS NULL
                AND SourceMatchId IS NULL
                AND SourceGroupId IS NULL
                AND SourceRank IS NULL
            )
        );

    ALTER TABLE dbo.BracketTemplateMatchSlots
        CHECK CONSTRAINT CK_BracketTemplateMatchSlots_SourcePayload;
END;

COMMIT TRANSACTION;
