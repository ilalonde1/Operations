/*
002_DocumentVariantsAndDetailNumber.sql

Adds StandardDetails document variants and explicit KorStandards detail-number
linkage. Run after 001_BaselineAsFound.sql succeeds and before WP2 code moves
versioning and publication behavior to variant scope.

Before: dbo.Documents and dbo.DocumentVersions exist with document-scoped
versioning indexes. After: every existing DocumentVersion has a DEFAULT
DocumentVariant, variant-scoped uniqueness indexes exist, and Documents has a
nullable DetailNumber with format and uniqueness enforcement.

WINDOW DESIGN (until WP2 ships the variant-aware code):
DocumentVariantId stays NULLABLE and both new unique indexes are FILTERED to
non-null variant rows, so every current app code path (upload, publish, delete
of variant-less documents) keeps working unchanged. WP2's follow-up migration
(005) flips the column NOT NULL and tightens the indexes after the repository
writes variants itself.
*/


USE KorTransmittals;
IF DB_NAME() <> N'KorTransmittals' BEGIN RAISERROR('Wrong database on server %s. This script runs ONLY in KorTransmittals.', 20, 1, @@SERVERNAME) WITH LOG; END;
SELECT DB_NAME() AS [You are here];
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.DocumentVariants', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.DocumentVariants
        (
            DocumentVariantId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_DocumentVariants PRIMARY KEY,
            DocumentId bigint NOT NULL,
            VariantKey nvarchar(64) NOT NULL,
            SheetSize nvarchar(32) NULL,
            KorStandardsSizeToken nvarchar(32) NULL,
            IsActive bit NOT NULL CONSTRAINT DF_DocumentVariants_IsActive DEFAULT (1),
            CreatedByUserId uniqueidentifier NULL,
            CreatedUtc datetime2 NOT NULL,
            UpdatedByUserId uniqueidentifier NULL,
            UpdatedUtc datetime2 NULL,
            RowVersion rowversion NOT NULL,
            CONSTRAINT FK_DocumentVariants_Documents FOREIGN KEY (DocumentId) REFERENCES dbo.Documents(DocumentId)
        );
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.DocumentVariants', N'U')
          AND name = N'UX_DocumentVariants_Document_VariantKey'
    )
    BEGIN
        CREATE UNIQUE INDEX UX_DocumentVariants_Document_VariantKey
        ON dbo.DocumentVariants(DocumentId, VariantKey);
    END;

    IF COL_LENGTH(N'dbo.DocumentVersions', N'DocumentVariantId') IS NULL
    BEGIN
        ALTER TABLE dbo.DocumentVersions ADD DocumentVariantId bigint NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.DocumentVersions', N'U')
          AND name = N'FK_DocumentVersions_DocumentVariants'
    )
    BEGIN
        ALTER TABLE dbo.DocumentVersions WITH CHECK
        ADD CONSTRAINT FK_DocumentVersions_DocumentVariants
            FOREIGN KEY (DocumentVariantId) REFERENCES dbo.DocumentVariants(DocumentVariantId);
    END;

    INSERT INTO dbo.DocumentVariants
    (
        DocumentId,
        VariantKey,
        SheetSize,
        KorStandardsSizeToken,
        IsActive,
        CreatedByUserId,
        CreatedUtc,
        UpdatedByUserId,
        UpdatedUtc
    )
    SELECT
        d.DocumentId,
        N'DEFAULT',
        NULL,
        NULL,
        1,
        d.CreatedByUserId,
        SYSUTCDATETIME(),
        NULL,
        NULL
    FROM dbo.Documents d
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.DocumentVariants dv
        WHERE dv.DocumentId = d.DocumentId
          AND dv.VariantKey = N'DEFAULT'
    );

    UPDATE v
    SET DocumentVariantId = dv.DocumentVariantId
    FROM dbo.DocumentVersions v
    INNER JOIN dbo.DocumentVariants dv
        ON dv.DocumentId = v.DocumentId
       AND dv.VariantKey = N'DEFAULT'
    WHERE v.DocumentVariantId IS NULL;

    IF EXISTS (SELECT 1 FROM dbo.DocumentVersions WHERE DocumentVariantId IS NULL)
    BEGIN
        RAISERROR('Migration failed: one or more DocumentVersions could not be assigned a DocumentVariantId.', 16, 1);
    END;

    /* DocumentVariantId deliberately stays NULLABLE here. The live app inserts
       DocumentVersions without this column until WP2; a NOT NULL flip now would
       break UploadVersionAsync in the window. WP2's migration 005 flips it
       after the repository writes variants itself. */

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.DocumentVersions', N'U')
          AND name = N'UX_DocumentVersions_Variant_VersionNumber'
    )
    BEGIN
        /* Filtered: window-era uploads insert NULL variants, and an unfiltered
           unique index would collide on (NULL, VersionNumber) across documents.
           005 replaces this with the unfiltered form once NOT NULL lands. */
        CREATE UNIQUE INDEX UX_DocumentVersions_Variant_VersionNumber
        ON dbo.DocumentVersions(DocumentVariantId, VersionNumber)
        WHERE DocumentVariantId IS NOT NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.DocumentVersions', N'U')
          AND name = N'UX_DocumentVersions_OneCurrentOfficialPerVariant'
    )
    BEGIN
        /* Also excludes NULL variants: two window-era publishes on different
           documents would otherwise collide on the single NULL key. */
        CREATE UNIQUE INDEX UX_DocumentVersions_OneCurrentOfficialPerVariant
        ON dbo.DocumentVersions(DocumentVariantId)
        WHERE IsCurrentOfficial = 1 AND DocumentVariantId IS NOT NULL;
    END;

    IF COL_LENGTH(N'dbo.Documents', N'DetailNumber') IS NULL
    BEGIN
        ALTER TABLE dbo.Documents ADD DetailNumber nvarchar(24) NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.Documents', N'U')
          AND name = N'CK_Documents_DetailNumber_Format'
    )
    BEGIN
        ALTER TABLE dbo.Documents WITH CHECK
        ADD CONSTRAINT CK_Documents_DetailNumber_Format
            CHECK (DetailNumber IS NULL OR DetailNumber LIKE N'KOR-D-[0-9][0-9][0-9][0-9][0-9]');
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.Documents', N'U')
          AND name = N'UX_Documents_DetailNumber'
    )
    BEGIN
        CREATE UNIQUE INDEX UX_Documents_DetailNumber
        ON dbo.Documents(DetailNumber)
        WHERE DetailNumber IS NOT NULL;
    END;

    IF EXISTS (SELECT 1 FROM dbo.DocumentVersions WHERE DocumentVariantId IS NULL)
    BEGIN
        RAISERROR('Post-migration assertion failed: every DocumentVersion must have DocumentVariantId.', 16, 1);
    END;

    IF (SELECT COUNT_BIG(*) FROM dbo.DocumentVariants) <> (SELECT COUNT_BIG(*) FROM dbo.Documents)
    BEGIN
        RAISERROR('Post-migration assertion failed: variant count must equal document count.', 16, 1);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.DocumentVariants', N'U')
          AND name = N'UX_DocumentVariants_Document_VariantKey'
    )
    BEGIN
        RAISERROR('Post-migration assertion failed: UX_DocumentVariants_Document_VariantKey is missing.', 16, 1);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.DocumentVersions', N'U')
          AND name = N'UX_DocumentVersions_Variant_VersionNumber'
    )
    BEGIN
        RAISERROR('Post-migration assertion failed: UX_DocumentVersions_Variant_VersionNumber is missing.', 16, 1);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.DocumentVersions', N'U')
          AND name = N'UX_DocumentVersions_OneCurrentOfficialPerVariant'
    )
    BEGIN
        RAISERROR('Post-migration assertion failed: UX_DocumentVersions_OneCurrentOfficialPerVariant is missing.', 16, 1);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.Documents', N'U')
          AND name = N'UX_Documents_DetailNumber'
    )
    BEGIN
        RAISERROR('Post-migration assertion failed: UX_Documents_DetailNumber is missing.', 16, 1);
    END;

    SELECT 'Documents' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.Documents
    UNION ALL
    SELECT 'DocumentVariants', COUNT_BIG(*) FROM dbo.DocumentVariants
    UNION ALL
    SELECT 'DocumentVersions', COUNT_BIG(*) FROM dbo.DocumentVersions
    UNION ALL
    SELECT 'FileBlobs', COUNT_BIG(*) FROM dbo.FileBlobs
    ORDER BY TableName;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
