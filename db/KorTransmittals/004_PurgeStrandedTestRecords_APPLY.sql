/*
004_PurgeStrandedTestRecords_APPLY.sql

Deletes only the 12 known stranded StandardDetails draft/module-test records
previewed by 003_PurgeStrandedTestRecords_PROPOSE.sql. Run after reading 003
output and confirming the records are still the known stranded debris.

Before: exactly 12 Documents exist, every DocumentVersion is Draft
(Status = 0), no DocumentVersion is current official, and 002 has added DEFAULT
DocumentVariants. After: Documents, DocumentVersions, FileBlobs,
ApprovalRecords, PublicationRecords, and DocumentVariants are empty. AuditEvents
is not deleted because audit history is append-only.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.DocumentVariants', N'U') IS NULL
    BEGIN
        RAISERROR('Purge refused: dbo.DocumentVariants does not exist. Run 002 first.', 16, 1);
    END;

    IF (SELECT COUNT_BIG(*) FROM dbo.Documents) <> 12
    BEGIN
        RAISERROR('Purge refused: Documents count is not exactly 12.', 16, 1);
    END;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.DocumentVersions
        WHERE Status <> 0
           OR IsCurrentOfficial = 1
    )
    BEGIN
        RAISERROR('Purge refused: every DocumentVersion must be Draft and not current official.', 16, 1);
    END;

    IF (SELECT COUNT_BIG(*) FROM dbo.DocumentVariants) <> (SELECT COUNT_BIG(*) FROM dbo.Documents)
       OR EXISTS (SELECT 1 FROM dbo.DocumentVariants WHERE VariantKey <> N'DEFAULT')
    BEGIN
        RAISERROR('Purge refused: DocumentVariants must contain exactly one DEFAULT variant per Document.', 16, 1);
    END;

    DECLARE @DocBlobIds TABLE
    (
        FileBlobId bigint NOT NULL PRIMARY KEY
    );

    INSERT INTO @DocBlobIds (FileBlobId)
    SELECT DISTINCT v.FileBlobId
    FROM dbo.DocumentVersions v
    WHERE v.FileBlobId IS NOT NULL;

    DELETE ar
    FROM dbo.ApprovalRecords ar
    INNER JOIN dbo.DocumentVersions v ON v.DocumentVersionId = ar.DocumentVersionId;

    DELETE pr
    FROM dbo.PublicationRecords pr
    INNER JOIN dbo.DocumentVersions v ON v.DocumentVersionId = pr.DocumentVersionId;

    DELETE FROM dbo.DocumentVersions;

    DELETE FROM dbo.DocumentVariants;

    DELETE FROM dbo.Documents;

    DELETE fb
    FROM dbo.FileBlobs fb
    WHERE fb.FileBlobId IN (SELECT dbi.FileBlobId FROM @DocBlobIds dbi)
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.DocumentVersions dv
          WHERE dv.FileBlobId = fb.FileBlobId
      );

    IF EXISTS (SELECT 1 FROM dbo.ApprovalRecords)
    BEGIN
        RAISERROR('Post-purge assertion failed: ApprovalRecords is not empty.', 16, 1);
    END;

    IF EXISTS (SELECT 1 FROM dbo.PublicationRecords)
    BEGIN
        RAISERROR('Post-purge assertion failed: PublicationRecords is not empty.', 16, 1);
    END;

    IF EXISTS (SELECT 1 FROM dbo.DocumentVersions)
    BEGIN
        RAISERROR('Post-purge assertion failed: DocumentVersions is not empty.', 16, 1);
    END;

    IF EXISTS (SELECT 1 FROM dbo.DocumentVariants)
    BEGIN
        RAISERROR('Post-purge assertion failed: DocumentVariants is not empty.', 16, 1);
    END;

    IF EXISTS (SELECT 1 FROM dbo.Documents)
    BEGIN
        RAISERROR('Post-purge assertion failed: Documents is not empty.', 16, 1);
    END;

    IF EXISTS (SELECT 1 FROM dbo.FileBlobs)
    BEGIN
        RAISERROR('Post-purge assertion failed: FileBlobs is not empty.', 16, 1);
    END;

    SELECT 'Documents' AS TableName, COUNT_BIG(*) AS RowCount FROM dbo.Documents
    UNION ALL
    SELECT 'DocumentVariants', COUNT_BIG(*) FROM dbo.DocumentVariants
    UNION ALL
    SELECT 'DocumentVersions', COUNT_BIG(*) FROM dbo.DocumentVersions
    UNION ALL
    SELECT 'FileBlobs', COUNT_BIG(*) FROM dbo.FileBlobs
    UNION ALL
    SELECT 'ApprovalRecords', COUNT_BIG(*) FROM dbo.ApprovalRecords
    UNION ALL
    SELECT 'PublicationRecords', COUNT_BIG(*) FROM dbo.PublicationRecords
    UNION ALL
    SELECT 'AuditEvents', COUNT_BIG(*) FROM dbo.AuditEvents
    ORDER BY TableName;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
