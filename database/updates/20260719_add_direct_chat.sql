/*
    Hanaka Sport - Direct 1v1 chat schema
    Date: 2026-07-19

    Purpose:
    - Add 1v1 chat rooms between two active users.
    - Store direct chat messages with recall support.
    - Store per-user room state for unread/read/archive/mute handling.
    - Reuse dbo.UserBlocks for block management; this script adds direct-chat source columns.

    Notes for backend implementation:
    - Always normalize room pair before insert:
        User1Id = smaller UserId, User2Id = larger UserId.
    - A pair can have only one direct room.
    - Block checks should use dbo.UserBlocks where IsActive = 1 in either direction.
*/

SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
    THROW 51010, 'Table dbo.Users was not found.', 1;

IF OBJECT_ID(N'dbo.UserBlocks', N'U') IS NULL
    THROW 51011, 'Table dbo.UserBlocks was not found.', 1;

IF OBJECT_ID(N'dbo.DirectChatRooms', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DirectChatRooms
    (
        DirectChatRoomId bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_DirectChatRooms PRIMARY KEY,

        User1Id bigint NOT NULL,
        User2Id bigint NOT NULL,

        LastMessageId bigint NULL,
        LastMessageAt datetime2(0) NULL,

        IsActive bit NOT NULL
            CONSTRAINT DF_DirectChatRooms_IsActive DEFAULT (1),
        CreatedAt datetime2(0) NOT NULL
            CONSTRAINT DF_DirectChatRooms_CreatedAt DEFAULT (sysdatetime()),
        UpdatedAt datetime2(0) NOT NULL
            CONSTRAINT DF_DirectChatRooms_UpdatedAt DEFAULT (sysdatetime()),

        CONSTRAINT FK_DirectChatRooms_User1
            FOREIGN KEY (User1Id)
            REFERENCES dbo.Users (UserId),

        CONSTRAINT FK_DirectChatRooms_User2
            FOREIGN KEY (User2Id)
            REFERENCES dbo.Users (UserId),

        CONSTRAINT CK_DirectChatRooms_NotSelf
            CHECK (User1Id <> User2Id),

        CONSTRAINT CK_DirectChatRooms_NormalizedPair
            CHECK (User1Id < User2Id)
    );
END;

IF OBJECT_ID(N'dbo.DirectChatMessages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DirectChatMessages
    (
        DirectChatMessageId bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_DirectChatMessages PRIMARY KEY,

        DirectChatRoomId bigint NOT NULL,
        SenderUserId bigint NOT NULL,

        MessageType varchar(30) NOT NULL
            CONSTRAINT DF_DirectChatMessages_MessageType DEFAULT ('text'),
        Content nvarchar(max) NULL,
        MediaUrl nvarchar(1000) NULL,

        ReplyToMessageId bigint NULL,
        ClientMessageId uniqueidentifier NULL,

        SentAt datetime2(0) NOT NULL
            CONSTRAINT DF_DirectChatMessages_SentAt DEFAULT (sysdatetime()),

        IsRecalled bit NOT NULL
            CONSTRAINT DF_DirectChatMessages_IsRecalled DEFAULT (0),
        RecalledAt datetime2(0) NULL,
        RecalledByUserId bigint NULL,

        IsDeleted bit NOT NULL
            CONSTRAINT DF_DirectChatMessages_IsDeleted DEFAULT (0),
        DeletedAt datetime2(0) NULL,

        CONSTRAINT FK_DirectChatMessages_Room
            FOREIGN KEY (DirectChatRoomId)
            REFERENCES dbo.DirectChatRooms (DirectChatRoomId),

        CONSTRAINT FK_DirectChatMessages_Sender
            FOREIGN KEY (SenderUserId)
            REFERENCES dbo.Users (UserId),

        CONSTRAINT FK_DirectChatMessages_ReplyTo
            FOREIGN KEY (ReplyToMessageId)
            REFERENCES dbo.DirectChatMessages (DirectChatMessageId),

        CONSTRAINT FK_DirectChatMessages_RecalledBy
            FOREIGN KEY (RecalledByUserId)
            REFERENCES dbo.Users (UserId),

        CONSTRAINT CK_DirectChatMessages_MessageType
            CHECK (MessageType IN ('text', 'image', 'video', 'file', 'system')),

        CONSTRAINT CK_DirectChatMessages_RecallState
            CHECK (
                (IsRecalled = 0 AND RecalledAt IS NULL AND RecalledByUserId IS NULL)
                OR
                (IsRecalled = 1 AND RecalledAt IS NOT NULL AND RecalledByUserId IS NOT NULL)
            )
    );
END;

IF OBJECT_ID(N'dbo.DirectChatRoomParticipants', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DirectChatRoomParticipants
    (
        DirectChatRoomId bigint NOT NULL,
        UserId bigint NOT NULL,

        LastReadMessageId bigint NULL,
        LastReadAt datetime2(0) NULL,

        IsArchived bit NOT NULL
            CONSTRAINT DF_DirectChatRoomParticipants_IsArchived DEFAULT (0),
        ArchivedAt datetime2(0) NULL,

        IsMuted bit NOT NULL
            CONSTRAINT DF_DirectChatRoomParticipants_IsMuted DEFAULT (0),
        MutedUntil datetime2(0) NULL,

        IsDeleted bit NOT NULL
            CONSTRAINT DF_DirectChatRoomParticipants_IsDeleted DEFAULT (0),
        DeletedAt datetime2(0) NULL,

        CreatedAt datetime2(0) NOT NULL
            CONSTRAINT DF_DirectChatRoomParticipants_CreatedAt DEFAULT (sysdatetime()),
        UpdatedAt datetime2(0) NOT NULL
            CONSTRAINT DF_DirectChatRoomParticipants_UpdatedAt DEFAULT (sysdatetime()),

        CONSTRAINT PK_DirectChatRoomParticipants
            PRIMARY KEY (DirectChatRoomId, UserId),

        CONSTRAINT FK_DirectChatRoomParticipants_Room
            FOREIGN KEY (DirectChatRoomId)
            REFERENCES dbo.DirectChatRooms (DirectChatRoomId),

        CONSTRAINT FK_DirectChatRoomParticipants_User
            FOREIGN KEY (UserId)
            REFERENCES dbo.Users (UserId),

        CONSTRAINT FK_DirectChatRoomParticipants_LastReadMessage
            FOREIGN KEY (LastReadMessageId)
            REFERENCES dbo.DirectChatMessages (DirectChatMessageId)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_DirectChatRooms_LastMessage'
      AND parent_object_id = OBJECT_ID(N'dbo.DirectChatRooms')
)
BEGIN
    ALTER TABLE dbo.DirectChatRooms
        ADD CONSTRAINT FK_DirectChatRooms_LastMessage
            FOREIGN KEY (LastMessageId)
            REFERENCES dbo.DirectChatMessages (DirectChatMessageId);
END;

IF COL_LENGTH(N'dbo.UserBlocks', N'SourceDirectRoomId') IS NULL
BEGIN
    ALTER TABLE dbo.UserBlocks
        ADD SourceDirectRoomId bigint NULL;
END;

IF COL_LENGTH(N'dbo.UserBlocks', N'SourceDirectMessageId') IS NULL
BEGIN
    ALTER TABLE dbo.UserBlocks
        ADD SourceDirectMessageId bigint NULL;
END;

IF EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_UserBlocks_Source'
      AND parent_object_id = OBJECT_ID(N'dbo.UserBlocks')
      AND definition NOT LIKE N'%DIRECT_CHAT%'
)
BEGIN
    ALTER TABLE dbo.UserBlocks
        DROP CONSTRAINT CK_UserBlocks_Source;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_UserBlocks_Source'
      AND parent_object_id = OBJECT_ID(N'dbo.UserBlocks')
)
BEGIN
    ALTER TABLE dbo.UserBlocks WITH CHECK
        ADD CONSTRAINT CK_UserBlocks_Source
            CHECK ([Source] IN ('CHAT', 'DIRECT_CHAT', 'PROFILE', 'ADMIN', 'SYSTEM'));

    ALTER TABLE dbo.UserBlocks
        CHECK CONSTRAINT CK_UserBlocks_Source;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_UserBlocks_SourceDirectRoom'
      AND parent_object_id = OBJECT_ID(N'dbo.UserBlocks')
)
BEGIN
    EXEC(N'
        ALTER TABLE dbo.UserBlocks
            ADD CONSTRAINT FK_UserBlocks_SourceDirectRoom
                FOREIGN KEY (SourceDirectRoomId)
                REFERENCES dbo.DirectChatRooms (DirectChatRoomId);
    ');
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_UserBlocks_SourceDirectMessage'
      AND parent_object_id = OBJECT_ID(N'dbo.UserBlocks')
)
BEGIN
    EXEC(N'
        ALTER TABLE dbo.UserBlocks
            ADD CONSTRAINT FK_UserBlocks_SourceDirectMessage
                FOREIGN KEY (SourceDirectMessageId)
                REFERENCES dbo.DirectChatMessages (DirectChatMessageId);
    ');
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_DirectChatRooms_UserPair'
      AND object_id = OBJECT_ID(N'dbo.DirectChatRooms')
)
BEGIN
    CREATE UNIQUE INDEX UX_DirectChatRooms_UserPair
        ON dbo.DirectChatRooms (User1Id, User2Id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_DirectChatRooms_User1_LastMessageAt'
      AND object_id = OBJECT_ID(N'dbo.DirectChatRooms')
)
BEGIN
    CREATE INDEX IX_DirectChatRooms_User1_LastMessageAt
        ON dbo.DirectChatRooms (User1Id, LastMessageAt DESC, UpdatedAt DESC);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_DirectChatRooms_User2_LastMessageAt'
      AND object_id = OBJECT_ID(N'dbo.DirectChatRooms')
)
BEGIN
    CREATE INDEX IX_DirectChatRooms_User2_LastMessageAt
        ON dbo.DirectChatRooms (User2Id, LastMessageAt DESC, UpdatedAt DESC);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_DirectChatMessages_Room_SentAt'
      AND object_id = OBJECT_ID(N'dbo.DirectChatMessages')
)
BEGIN
    CREATE INDEX IX_DirectChatMessages_Room_SentAt
        ON dbo.DirectChatMessages (DirectChatRoomId, SentAt DESC, DirectChatMessageId DESC);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_DirectChatMessages_Sender_SentAt'
      AND object_id = OBJECT_ID(N'dbo.DirectChatMessages')
)
BEGIN
    CREATE INDEX IX_DirectChatMessages_Sender_SentAt
        ON dbo.DirectChatMessages (SenderUserId, SentAt DESC);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_DirectChatMessages_Sender_ClientMessageId'
      AND object_id = OBJECT_ID(N'dbo.DirectChatMessages')
)
BEGIN
    CREATE UNIQUE INDEX UX_DirectChatMessages_Sender_ClientMessageId
        ON dbo.DirectChatMessages (SenderUserId, ClientMessageId)
        WHERE ClientMessageId IS NOT NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_DirectChatRoomParticipants_User_UpdatedAt'
      AND object_id = OBJECT_ID(N'dbo.DirectChatRoomParticipants')
)
BEGIN
    CREATE INDEX IX_DirectChatRoomParticipants_User_UpdatedAt
        ON dbo.DirectChatRoomParticipants (UserId, UpdatedAt DESC);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_DirectChatRoomParticipants_LastReadMessage'
      AND object_id = OBJECT_ID(N'dbo.DirectChatRoomParticipants')
)
BEGIN
    CREATE INDEX IX_DirectChatRoomParticipants_LastReadMessage
        ON dbo.DirectChatRoomParticipants (LastReadMessageId)
        WHERE LastReadMessageId IS NOT NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_UserBlocks_SourceDirectRoomId'
      AND object_id = OBJECT_ID(N'dbo.UserBlocks')
)
BEGIN
    EXEC(N'
        CREATE INDEX IX_UserBlocks_SourceDirectRoomId
            ON dbo.UserBlocks (SourceDirectRoomId)
            WHERE SourceDirectRoomId IS NOT NULL;
    ');
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_UserBlocks_SourceDirectMessageId'
      AND object_id = OBJECT_ID(N'dbo.UserBlocks')
)
BEGIN
    EXEC(N'
        CREATE INDEX IX_UserBlocks_SourceDirectMessageId
            ON dbo.UserBlocks (SourceDirectMessageId)
            WHERE SourceDirectMessageId IS NOT NULL;
    ');
END;

COMMIT TRANSACTION;
GO
