IF COL_LENGTH('dbo.DirectChatMessages', 'EditedAt') IS NULL
BEGIN
    ALTER TABLE dbo.DirectChatMessages
        ADD EditedAt datetime2(0) NULL;
END
GO
