/*
    Hanaka Sport - Tournament Sepay payment schema
    Date: 2026-07-17

    Purpose:
    - Add per-tournament registration fee.
    - Track Sepay QR/bank-transfer transactions for TournamentRegistrations.
    - Store raw Sepay webhook payloads for idempotent processing/audit.

    This script is intentionally schema-only. It does not change existing paid data.
*/

SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.Tournaments', N'U') IS NULL
    THROW 51001, 'Table dbo.Tournaments was not found.', 1;

IF OBJECT_ID(N'dbo.TournamentRegistrations', N'U') IS NULL
    THROW 51002, 'Table dbo.TournamentRegistrations was not found.', 1;

IF COL_LENGTH(N'dbo.Tournaments', N'RegistrationFeeAmount') IS NULL
BEGIN
    ALTER TABLE dbo.Tournaments
        ADD RegistrationFeeAmount decimal(18, 2) NOT NULL
            CONSTRAINT DF_Tournaments_RegistrationFeeAmount DEFAULT (0);
END;

IF COL_LENGTH(N'dbo.Tournaments', N'RegistrationFeeCurrency') IS NULL
BEGIN
    ALTER TABLE dbo.Tournaments
        ADD RegistrationFeeCurrency varchar(10) NOT NULL
            CONSTRAINT DF_Tournaments_RegistrationFeeCurrency DEFAULT ('VND');
END;

IF COL_LENGTH(N'dbo.TournamentRegistrations', N'PaidAt') IS NULL
BEGIN
    ALTER TABLE dbo.TournamentRegistrations
        ADD PaidAt datetime2(0) NULL;
END;

IF COL_LENGTH(N'dbo.TournamentRegistrations', N'PaymentAmount') IS NULL
BEGIN
    ALTER TABLE dbo.TournamentRegistrations
        ADD PaymentAmount decimal(18, 2) NULL;
END;

IF OBJECT_ID(N'dbo.TournamentRegistrationPayments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TournamentRegistrationPayments
    (
        PaymentId bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TournamentRegistrationPayments PRIMARY KEY,

        RegistrationId bigint NOT NULL,
        TournamentId bigint NOT NULL,
        UserId bigint NULL,

        Provider varchar(30) NOT NULL
            CONSTRAINT DF_TournamentRegistrationPayments_Provider DEFAULT ('sepay'),
        PaymentMethod varchar(30) NOT NULL
            CONSTRAINT DF_TournamentRegistrationPayments_Method DEFAULT ('qr_transfer'),
        Status varchar(30) NOT NULL
            CONSTRAINT DF_TournamentRegistrationPayments_Status DEFAULT ('pending'),

        TransactionCode varchar(100) NOT NULL,
        ProviderTransactionId varchar(100) NULL,

        BankCode varchar(50) NULL,
        BankAccountNo varchar(50) NULL,
        BankAccountName nvarchar(255) NULL,

        QrImageUrl nvarchar(1000) NULL,
        TransferContent nvarchar(255) NULL,

        Amount decimal(18, 2) NOT NULL,
        PaidAmount decimal(18, 2) NULL,
        Currency varchar(10) NOT NULL
            CONSTRAINT DF_TournamentRegistrationPayments_Currency DEFAULT ('VND'),

        RawResponse nvarchar(max) NULL,
        ExpiredAt datetime2(0) NULL,
        PaidAt datetime2(0) NULL,
        CreatedAt datetime2(0) NOT NULL
            CONSTRAINT DF_TournamentRegistrationPayments_CreatedAt DEFAULT (sysdatetime()),
        UpdatedAt datetime2(0) NOT NULL
            CONSTRAINT DF_TournamentRegistrationPayments_UpdatedAt DEFAULT (sysdatetime()),

        CONSTRAINT FK_TournamentRegistrationPayments_Registrations
            FOREIGN KEY (RegistrationId)
            REFERENCES dbo.TournamentRegistrations (RegistrationId),

        CONSTRAINT FK_TournamentRegistrationPayments_Tournaments
            FOREIGN KEY (TournamentId)
            REFERENCES dbo.Tournaments (TournamentId),

        CONSTRAINT FK_TournamentRegistrationPayments_Users
            FOREIGN KEY (UserId)
            REFERENCES dbo.Users (UserId),

        CONSTRAINT CK_TournamentRegistrationPayments_Provider
            CHECK (Provider IN ('sepay')),

        CONSTRAINT CK_TournamentRegistrationPayments_Method
            CHECK (PaymentMethod IN ('bank_transfer', 'qr_transfer')),

        CONSTRAINT CK_TournamentRegistrationPayments_Status
            CHECK (Status IN ('pending', 'processing', 'paid', 'failed', 'expired', 'cancelled', 'refunded')),

        CONSTRAINT CK_TournamentRegistrationPayments_Amount
            CHECK (Amount >= 0 AND (PaidAmount IS NULL OR PaidAmount >= 0))
    );
END;

IF OBJECT_ID(N'dbo.TournamentSepayWebhooks', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TournamentSepayWebhooks
    (
        SepayWebhookId bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TournamentSepayWebhooks PRIMARY KEY,

        PaymentId bigint NULL,

        Gateway varchar(30) NOT NULL
            CONSTRAINT DF_TournamentSepayWebhooks_Gateway DEFAULT ('sepay'),
        EventType varchar(50) NULL,
        ReferenceCode varchar(100) NULL,
        AccountNumber varchar(50) NULL,
        Code varchar(100) NULL,
        ContentTransfer nvarchar(1000) NULL,
        Description nvarchar(1000) NULL,
        TransferType varchar(20) NULL,
        Amount decimal(18, 2) NULL,

        RawPayload nvarchar(max) NOT NULL,
        IsProcessed bit NOT NULL
            CONSTRAINT DF_TournamentSepayWebhooks_IsProcessed DEFAULT (0),
        ProcessedAt datetime2(0) NULL,
        CreatedAt datetime2(0) NOT NULL
            CONSTRAINT DF_TournamentSepayWebhooks_CreatedAt DEFAULT (sysdatetime()),

        CONSTRAINT FK_TournamentSepayWebhooks_Payments
            FOREIGN KEY (PaymentId)
            REFERENCES dbo.TournamentRegistrationPayments (PaymentId)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_TournamentRegistrationPayments_TransactionCode'
      AND object_id = OBJECT_ID(N'dbo.TournamentRegistrationPayments')
)
BEGIN
    CREATE UNIQUE INDEX UX_TournamentRegistrationPayments_TransactionCode
        ON dbo.TournamentRegistrationPayments (TransactionCode);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_TournamentRegistrationPayments_Registration_Status'
      AND object_id = OBJECT_ID(N'dbo.TournamentRegistrationPayments')
)
BEGIN
    CREATE INDEX IX_TournamentRegistrationPayments_Registration_Status
        ON dbo.TournamentRegistrationPayments (RegistrationId, Status, CreatedAt DESC);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_TournamentRegistrationPayments_Tournament_Status'
      AND object_id = OBJECT_ID(N'dbo.TournamentRegistrationPayments')
)
BEGIN
    CREATE INDEX IX_TournamentRegistrationPayments_Tournament_Status
        ON dbo.TournamentRegistrationPayments (TournamentId, Status, CreatedAt DESC);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_TournamentRegistrationPayments_ProviderTransactionId'
      AND object_id = OBJECT_ID(N'dbo.TournamentRegistrationPayments')
)
BEGIN
    CREATE INDEX IX_TournamentRegistrationPayments_ProviderTransactionId
        ON dbo.TournamentRegistrationPayments (ProviderTransactionId)
        WHERE ProviderTransactionId IS NOT NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_TournamentSepayWebhooks_PaymentId'
      AND object_id = OBJECT_ID(N'dbo.TournamentSepayWebhooks')
)
BEGIN
    CREATE INDEX IX_TournamentSepayWebhooks_PaymentId
        ON dbo.TournamentSepayWebhooks (PaymentId);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_TournamentSepayWebhooks_ReferenceCode'
      AND object_id = OBJECT_ID(N'dbo.TournamentSepayWebhooks')
)
BEGIN
    CREATE INDEX IX_TournamentSepayWebhooks_ReferenceCode
        ON dbo.TournamentSepayWebhooks (ReferenceCode)
        WHERE ReferenceCode IS NOT NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_TournamentSepayWebhooks_IsProcessed'
      AND object_id = OBJECT_ID(N'dbo.TournamentSepayWebhooks')
)
BEGIN
    CREATE INDEX IX_TournamentSepayWebhooks_IsProcessed
        ON dbo.TournamentSepayWebhooks (IsProcessed, CreatedAt DESC);
END;

COMMIT TRANSACTION;
GO
