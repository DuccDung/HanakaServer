/*
    Hanaka Sport - Editable SePay settings
    Date: 2026-07-31

    The table contains one row only (SepaySettingId = 1).
    Existing appsettings values are seeded so payment behavior remains unchanged.
*/

SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.SepaySettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SepaySettings
    (
        SepaySettingId int NOT NULL
            CONSTRAINT PK_SepaySettings PRIMARY KEY,
        ApiBaseUrl nvarchar(500) NOT NULL,
        ApiToken nvarchar(2000) NULL,
        BankAccountId int NULL,
        QrBaseUrl nvarchar(500) NOT NULL,
        ReceiverBankShortName nvarchar(50) NOT NULL,
        ReceiverBankName nvarchar(100) NOT NULL,
        ReceiverAccountNumber nvarchar(50) NOT NULL,
        ReceiverAccountName nvarchar(255) NOT NULL,
        WebhookApiKey nvarchar(500) NULL,
        TransferCodePrefix nvarchar(20) NOT NULL,
        PaymentExpireMinutes int NOT NULL,
        UpdatedAt datetime2(0) NOT NULL
            CONSTRAINT DF_SepaySettings_UpdatedAt DEFAULT (sysutcdatetime()),
        UpdatedBy nvarchar(255) NULL,

        CONSTRAINT CK_SepaySettings_Singleton CHECK (SepaySettingId = 1),
        CONSTRAINT CK_SepaySettings_BankAccountId CHECK (BankAccountId IS NULL OR BankAccountId > 0),
        CONSTRAINT CK_SepaySettings_PaymentExpireMinutes CHECK (PaymentExpireMinutes BETWEEN 0 AND 10080)
    );
END;

IF NOT EXISTS (SELECT 1 FROM dbo.SepaySettings WHERE SepaySettingId = 1)
BEGIN
    INSERT INTO dbo.SepaySettings
    (
        SepaySettingId,
        ApiBaseUrl,
        ApiToken,
        BankAccountId,
        QrBaseUrl,
        ReceiverBankShortName,
        ReceiverBankName,
        ReceiverAccountNumber,
        ReceiverAccountName,
        WebhookApiKey,
        TransferCodePrefix,
        PaymentExpireMinutes,
        UpdatedAt,
        UpdatedBy
    )
    VALUES
    (
        1,
        N'https://my.sepay.vn',
        NULL,
        NULL,
        N'https://qr.sepay.vn',
        N'MBBank',
        N'MBBank',
        N'02299597799999',
        N'NGUYEN XUAN PHONG',
        NULL,
        N'HNK',
        15,
        SYSUTCDATETIME(),
        N'database migration'
    );
END;

COMMIT TRANSACTION;
GO
