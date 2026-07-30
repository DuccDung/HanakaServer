/*
    Hanaka Sport - Direct chat message reports
    Date: 2026-07-22

    Purpose:
    - Link moderation reports to direct 1v1 chat rooms/messages.
    - Keep existing ClubMessage report flow unchanged.
*/

SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ModerationReports', N'U') IS NULL
    THROW 51030, 'Table dbo.ModerationReports was not found.', 1;

IF OBJECT_ID(N'dbo.DirectChatRooms', N'U') IS NULL
    THROW 51031, 'Table dbo.DirectChatRooms was not found.', 1;

IF OBJECT_ID(N'dbo.DirectChatMessages', N'U') IS NULL
    THROW 51032, 'Table dbo.DirectChatMessages was not found.', 1;

IF COL_LENGTH(N'dbo.ModerationReports', N'DirectChatRoomId') IS NULL
BEGIN
    ALTER TABLE dbo.ModerationReports
        ADD DirectChatRoomId bigint NULL;
END;

IF COL_LENGTH(N'dbo.ModerationReports', N'DirectChatMessageId') IS NULL
BEGIN
    ALTER TABLE dbo.ModerationReports
        ADD DirectChatMessageId bigint NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_ModerationReports_DirectChatRoom'
      AND parent_object_id = OBJECT_ID(N'dbo.ModerationReports')
)
BEGIN
    EXEC(N'
        ALTER TABLE dbo.ModerationReports
            ADD CONSTRAINT FK_ModerationReports_DirectChatRoom
                FOREIGN KEY (DirectChatRoomId)
                REFERENCES dbo.DirectChatRooms (DirectChatRoomId);
    ');
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_ModerationReports_DirectChatMessage'
      AND parent_object_id = OBJECT_ID(N'dbo.ModerationReports')
)
BEGIN
    EXEC(N'
        ALTER TABLE dbo.ModerationReports
            ADD CONSTRAINT FK_ModerationReports_DirectChatMessage
                FOREIGN KEY (DirectChatMessageId)
                REFERENCES dbo.DirectChatMessages (DirectChatMessageId);
    ');
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.ModerationReports')
      AND name = N'IX_ModerationReports_DirectChatRoomId'
)
BEGIN
    EXEC(N'
        CREATE INDEX IX_ModerationReports_DirectChatRoomId
            ON dbo.ModerationReports (DirectChatRoomId)
            WHERE DirectChatRoomId IS NOT NULL;
    ');
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.ModerationReports')
      AND name = N'IX_ModerationReports_DirectChatMessageId'
)
BEGIN
    EXEC(N'
        CREATE INDEX IX_ModerationReports_DirectChatMessageId
            ON dbo.ModerationReports (DirectChatMessageId)
            WHERE DirectChatMessageId IS NOT NULL;
    ');
END;

COMMIT TRANSACTION;
GO

SELECT
    c.name,
    t.name AS type_name,
    c.is_nullable
FROM sys.columns c
JOIN sys.types t
    ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID(N'dbo.ModerationReports')
  AND c.name IN (N'DirectChatRoomId', N'DirectChatMessageId');
GO
