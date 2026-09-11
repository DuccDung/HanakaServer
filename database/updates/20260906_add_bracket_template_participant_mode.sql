-- Separates bracket topology (FormatType) from the kind of registration occupying a seed.
-- Existing templates remain STANDARD. The known unused TP_08 draft is classified as relay.
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.BracketTemplates', N'U') IS NULL
        THROW 51020, N'Apply the bracket template schema before the participant-mode migration.', 1;

    IF COL_LENGTH(N'dbo.BracketTemplates', N'ParticipantMode') IS NULL
    BEGIN
        ALTER TABLE dbo.BracketTemplates
            ADD ParticipantMode varchar(30) NOT NULL
                CONSTRAINT DF_BracketTemplates_ParticipantMode DEFAULT ('STANDARD') WITH VALUES;
    END;

    IF OBJECT_ID(N'dbo.CK_BracketTemplates_ParticipantMode', N'C') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'
            ALTER TABLE dbo.BracketTemplates WITH CHECK
                ADD CONSTRAINT CK_BracketTemplates_ParticipantMode
                    CHECK (ParticipantMode IN (''STANDARD'', ''RELAY_TEAM''));';
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.BracketTemplates')
          AND name = N'IX_BracketTemplates_Status_ParticipantMode_FormatType')
    BEGIN
        EXEC sys.sp_executesql N'
            CREATE INDEX IX_BracketTemplates_Status_ParticipantMode_FormatType
                ON dbo.BracketTemplates(Status, ParticipantMode, FormatType);';
    END;

    -- This draft was created specifically for relay and has never been applied.
    EXEC sys.sp_executesql N'
        UPDATE template
        SET ParticipantMode = ''RELAY_TEAM''
        FROM dbo.BracketTemplates AS template
        WHERE template.TemplateCode = ''TP_08''
          AND template.TemplateName = N''Giải tiếp sức''
          AND template.Status = ''DRAFT''
          AND template.ParticipantMode = ''STANDARD''
          AND NOT EXISTS (
              SELECT 1
              FROM dbo.TournamentBracketApplications AS application
              WHERE application.BracketTemplateId = template.BracketTemplateId);';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
