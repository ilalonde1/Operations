/*
006_VariantScopeTighten.sql

Closes the variant "window" that 002 opened: makes DocumentVersions.DocumentVariantId
NOT NULL, replaces the two FILTERED variant unique indexes with full ones, and drops
the superseded document-scoped indexes. After this, versioning and current-official are
enforced per VARIANT, not per document.

⚠ DEPLOY ORDER: run this AFTER the Task G app code is deployed (CreateDocumentAsync makes a
DEFAULT variant; UploadVersionAsync writes DocumentVariantId). The script REFUSES to run if any
existing DocumentVersion still has a NULL DocumentVariantId, so a wrong order fails safely rather
than corrupting. With zero documents it passes trivially.

Idempotent; run as sa in SSMS.
*/

USE KorTransmittals;
IF DB_NAME() <> N'KorTransmittals' BEGIN RAISERROR('Wrong database on server %s. This script runs ONLY in KorTransmittals.', 20, 1, @@SERVERNAME) WITH LOG; END;
SELECT DB_NAME() AS [You are here];
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'dbo.DocumentVersions', N'DocumentVariantId') IS NULL
    BEGIN
        RAISERROR('DocumentVariantId does not exist - run 002 first.', 16, 1);
    END;

    IF EXISTS (SELECT 1 FROM dbo.DocumentVersions WHERE DocumentVariantId IS NULL)
    BEGIN
        RAISERROR('Refused: some DocumentVersions have NULL DocumentVariantId. Deploy the Task G app code first, then re-run.', 16, 1);
    END;

    -- 1. make the column NOT NULL (safe: no NULLs remain, asserted above)
    IF EXISTS
    (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.DocumentVersions', N'U')
          AND name = N'DocumentVariantId' AND is_nullable = 1
    )
    BEGIN
        ALTER TABLE dbo.DocumentVersions ALTER COLUMN DocumentVariantId bigint NOT NULL;
    END;

    -- 2. replace the FILTERED variant indexes (from 002) with full unique indexes
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DocumentVersions', N'U') AND name = N'UX_DocumentVersions_Variant_VersionNumber')
        DROP INDEX UX_DocumentVersions_Variant_VersionNumber ON dbo.DocumentVersions;
    CREATE UNIQUE INDEX UX_DocumentVersions_Variant_VersionNumber
        ON dbo.DocumentVersions(DocumentVariantId, VersionNumber);

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DocumentVersions', N'U') AND name = N'UX_DocumentVersions_OneCurrentOfficialPerVariant')
        DROP INDEX UX_DocumentVersions_OneCurrentOfficialPerVariant ON dbo.DocumentVersions;
    CREATE UNIQUE INDEX UX_DocumentVersions_OneCurrentOfficialPerVariant
        ON dbo.DocumentVersions(DocumentVariantId)
        WHERE IsCurrentOfficial = 1;

    -- 3. drop the superseded document-scoped indexes (variant scope now covers them)
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DocumentVersions', N'U') AND name = N'IX_DocumentVersions_DocumentId_VersionNumber')
        DROP INDEX IX_DocumentVersions_DocumentId_VersionNumber ON dbo.DocumentVersions;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DocumentVersions', N'U') AND name = N'UX_DocumentVersions_OneCurrentOfficialPerDocument')
        DROP INDEX UX_DocumentVersions_OneCurrentOfficialPerDocument ON dbo.DocumentVersions;

    -- assertions
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.DocumentVersions', N'U') AND name = N'DocumentVariantId' AND is_nullable = 1)
        RAISERROR('Post assertion failed: DocumentVariantId is still nullable.', 16, 1);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DocumentVersions', N'U') AND name = N'UX_DocumentVersions_Variant_VersionNumber')
        RAISERROR('Post assertion failed: variant version-number index missing.', 16, 1);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.DocumentVersions', N'U') AND name = N'UX_DocumentVersions_OneCurrentOfficialPerVariant')
        RAISERROR('Post assertion failed: one-official-per-variant index missing.', 16, 1);

    SELECT 'DocumentVersions' AS TableName,
           COUNT_BIG(*) AS TotalRows,
           SUM(CASE WHEN DocumentVariantId IS NULL THEN 1 ELSE 0 END) AS NullVariant
    FROM dbo.DocumentVersions;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
