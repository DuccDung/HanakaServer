/*
    Hanaka Sport - public relay registration read indexes
    Date: 2026-09-07

    Purpose:
    - Cover the public registration counters by tournament.
    - Speed up the "latest rating" lookup for every relay member.

    This script is additive and idempotent. It does not rewrite application data.
*/

SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.TournamentRegistrations', N'U') IS NULL
    THROW 51001, N'Table dbo.TournamentRegistrations was not found.', 1;

IF OBJECT_ID(N'dbo.UserRatingHistory', N'U') IS NULL
    THROW 51002, N'Table dbo.UserRatingHistory was not found.', 1;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.TournamentRegistrations')
      AND name = N'IX_TournamentRegistrations_PublicList'
)
BEGIN
    CREATE INDEX IX_TournamentRegistrations_PublicList
        ON dbo.TournamentRegistrations(TournamentId, IsVirtualTeam, RegIndex)
        INCLUDE (Success, WaitingPair, Paid);
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.UserRatingHistory')
      AND name = N'IX_UserRatingHistory_User_RatedAt'
)
BEGIN
    CREATE INDEX IX_UserRatingHistory_User_RatedAt
        ON dbo.UserRatingHistory(UserId, RatedAt DESC, RatingHistoryId DESC)
        INCLUDE (RatingSingle, RatingDouble);
END;
GO
