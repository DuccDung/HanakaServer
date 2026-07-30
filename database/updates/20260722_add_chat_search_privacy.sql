/*
    Hanaka Sport - Chat search privacy
    Date: 2026-07-22

    Purpose:
    - Allow a user to hide only from direct chat user search.
    - Default is visible in chat search for all existing users.

    Backend usage:
    - Filter /api/direct-chats/users/search with:
        Users.IsHiddenFromChatSearch = 0
    - Do not use this flag for other user search surfaces.
*/

SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
    THROW 51020, 'Table dbo.Users was not found.', 1;

IF COL_LENGTH(N'dbo.Users', N'IsHiddenFromChatSearch') IS NULL
BEGIN
    EXEC(N'
        ALTER TABLE dbo.Users
            ADD IsHiddenFromChatSearch bit NOT NULL
                CONSTRAINT DF_Users_IsHiddenFromChatSearch DEFAULT (0);
    ');
END
ELSE
BEGIN
    EXEC(N'
        UPDATE dbo.Users
        SET IsHiddenFromChatSearch = 0
        WHERE IsHiddenFromChatSearch IS NULL;
    ');

    IF EXISTS
    (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.Users')
          AND name = N'IsHiddenFromChatSearch'
          AND is_nullable = 1
    )
    BEGIN
        EXEC(N'
            ALTER TABLE dbo.Users
                ALTER COLUMN IsHiddenFromChatSearch bit NOT NULL;
        ');
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.default_constraints dc
        JOIN sys.columns c
            ON c.object_id = dc.parent_object_id
           AND c.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = OBJECT_ID(N'dbo.Users')
          AND c.name = N'IsHiddenFromChatSearch'
    )
    BEGIN
        EXEC(N'
            ALTER TABLE dbo.Users
                ADD CONSTRAINT DF_Users_IsHiddenFromChatSearch
                DEFAULT (0) FOR IsHiddenFromChatSearch;
        ');
    END;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Users')
      AND name = N'IX_Users_DirectChatSearchVisibility'
)
BEGIN
    EXEC(N'
        CREATE INDEX IX_Users_DirectChatSearchVisibility
            ON dbo.Users (IsActive, IsHiddenFromChatSearch, UserId)
            INCLUDE (FullName, Phone, City, Gender, Verified, AvatarUrl);
    ');
END;

COMMIT TRANSACTION;
GO

SELECT
    c.name,
    t.name AS type_name,
    c.is_nullable,
    dc.name AS default_constraint,
    dc.definition AS default_definition
FROM sys.columns c
JOIN sys.types t
    ON t.user_type_id = c.user_type_id
LEFT JOIN sys.default_constraints dc
    ON dc.parent_object_id = c.object_id
   AND dc.parent_column_id = c.column_id
WHERE c.object_id = OBJECT_ID(N'dbo.Users')
  AND c.name = N'IsHiddenFromChatSearch';
GO
