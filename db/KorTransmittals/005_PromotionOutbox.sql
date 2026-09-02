/*
005_PromotionOutbox.sql

Creates the KorTransmittals StandardDetails promotion outbox. Run as sa in SSMS
after 002_DocumentVariantsAndDetailNumber.sql and before app code starts
recording approval promotion requests.

Before: dbo.Documents and dbo.DocumentVersions already exist. After: the app can
insert pending promotion requests in the same KorTransmittals transaction as an
approval, and a later processor can find pending rows by status and request time.
*/

USE KorTransmittals;
IF DB_NAME() <> N'KorTransmittals' BEGIN RAISERROR('Wrong database on server %s. This script runs ONLY in KorTransmittals.', 20, 1, @@SERVERNAME) WITH LOG; END;
SELECT DB_NAME() AS [You are here];

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.Documents', N'U') IS NULL
    BEGIN
        RAISERROR('Expected table dbo.Documents does not exist.', 16, 1);
    END;

    IF OBJECT_ID(N'dbo.DocumentVersions', N'U') IS NULL
    BEGIN
        RAISERROR('Expected table dbo.DocumentVersions does not exist.', 16, 1);
    END;

    IF OBJECT_ID(N'dbo.StandardDetailPromotionOutbox', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.StandardDetailPromotionOutbox
        (
            PromotionOutboxId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_StandardDetailPromotionOutbox PRIMARY KEY,
            DocumentId bigint NOT NULL,
            DocumentVersionId bigint NOT NULL,
            DetailNumber nvarchar(24) NOT NULL,
            TargetConfidence nvarchar(32) NOT NULL,
            RequestedByUserId uniqueidentifier NOT NULL,
            RequestedByUserName nvarchar(150) NULL,
            RequestedUtc datetime2 NOT NULL CONSTRAINT DF_PromoOutbox_Req DEFAULT (SYSUTCDATETIME()),
            Status tinyint NOT NULL CONSTRAINT DF_PromoOutbox_Status DEFAULT (0),
            AttemptCount int NOT NULL CONSTRAINT DF_PromoOutbox_Attempt DEFAULT (0),
            ProcessedUtc datetime2 NULL,
            ResultMessage nvarchar(1000) NULL,
            ErrorMessage nvarchar(2000) NULL,
            CONSTRAINT FK_StandardDetailPromotionOutbox_Documents
                FOREIGN KEY (DocumentId) REFERENCES dbo.Documents(DocumentId)
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.StandardDetailPromotionOutbox', N'U')
          AND name = N'IX_PromoOutbox_Status'
    )
    BEGIN
        CREATE INDEX IX_PromoOutbox_Status
        ON dbo.StandardDetailPromotionOutbox(Status, RequestedUtc);
    END;

    IF OBJECT_ID(N'dbo.StandardDetailPromotionOutbox', N'U') IS NULL
    BEGIN
        RAISERROR('Post-create assertion failed: dbo.StandardDetailPromotionOutbox does not exist.', 16, 1);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.StandardDetailPromotionOutbox', N'U')
          AND name = N'IX_PromoOutbox_Status'
    )
    BEGIN
        RAISERROR('Post-create assertion failed: IX_PromoOutbox_Status does not exist.', 16, 1);
    END;

    SELECT
        N'dbo.StandardDetailPromotionOutbox' AS TableName,
        COUNT_BIG(*) AS TotalRows
    FROM dbo.StandardDetailPromotionOutbox;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
