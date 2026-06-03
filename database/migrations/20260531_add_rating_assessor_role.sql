/*
    Add the Hanaka rating assessor role used by the dedicated rating dashboard.

    This script does not change mobile API contracts. It only seeds dbo.Roles.
*/

SET XACT_ABORT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.Roles', N'U') IS NULL
        THROW 52000, 'Table dbo.Roles was not found.', 1;

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.Roles
        WHERE RoleCode = 'RATING_ASSESSOR'
    )
    BEGIN
        INSERT INTO dbo.Roles (RoleCode, RoleName)
        VALUES ('RATING_ASSESSOR', N'Người chấm trình');
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
GO
