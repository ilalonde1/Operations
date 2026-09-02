/*
001_BaselineAsFound.sql

Records the KorTransmittals StandardDetails schema as found on 2026-09-01.
The migration home starts here; nothing before it is scripted.

Run before applying later migrations. This verification script creates nothing,
changes nothing, and rolls back if required tables, key columns, or baseline
indexes are missing. After success, it prints the live row counts.
*/


USE KorTransmittals;
IF DB_NAME() <> N'KorTransmittals' BEGIN RAISERROR('Wrong database on server %s. This script runs ONLY in KorTransmittals.', 20, 1, @@SERVERNAME) WITH LOG; END;
SELECT DB_NAME() AS [You are here];
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

DECLARE @MissingTables TABLE
(
    SchemaName sysname NOT NULL,
    TableName sysname NOT NULL
);

INSERT INTO @MissingTables (SchemaName, TableName)
SELECT v.SchemaName, v.TableName
FROM (VALUES
    (N'dbo', N'Documents'),
    (N'dbo', N'DocumentVersions'),
    (N'dbo', N'FileBlobs'),
    (N'dbo', N'ApprovalRecords'),
    (N'dbo', N'PublicationRecords'),
    (N'dbo', N'AuditEvents'),
    (N'dbo', N'DocumentGroups')
) AS v(SchemaName, TableName)
WHERE OBJECT_ID(QUOTENAME(v.SchemaName) + N'.' + QUOTENAME(v.TableName), N'U') IS NULL;

IF EXISTS (SELECT 1 FROM @MissingTables)
BEGIN
    SELECT SchemaName, TableName
    FROM @MissingTables
    ORDER BY SchemaName, TableName;

    RAISERROR('Baseline verification failed: expected StandardDetails table is missing.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END;

DECLARE @RequiredColumns TABLE
(
    SchemaName sysname NOT NULL,
    TableName sysname NOT NULL,
    ColumnName sysname NOT NULL
);

INSERT INTO @RequiredColumns (SchemaName, TableName, ColumnName)
VALUES
    (N'dbo', N'Documents', N'DocumentId'),
    (N'dbo', N'Documents', N'DocumentUid'),
    (N'dbo', N'Documents', N'Title'),
    (N'dbo', N'Documents', N'Description'),
    (N'dbo', N'Documents', N'DocumentGroupId'),
    (N'dbo', N'Documents', N'CreatedByUserId'),
    (N'dbo', N'Documents', N'CreatedUtc'),
    (N'dbo', N'Documents', N'UpdatedByUserId'),
    (N'dbo', N'Documents', N'UpdatedUtc'),
    (N'dbo', N'Documents', N'RowVersion'),
    (N'dbo', N'DocumentVersions', N'DocumentVersionId'),
    (N'dbo', N'DocumentVersions', N'VersionUid'),
    (N'dbo', N'DocumentVersions', N'DocumentId'),
    (N'dbo', N'DocumentVersions', N'VersionNumber'),
    (N'dbo', N'DocumentVersions', N'FileBlobId'),
    (N'dbo', N'DocumentVersions', N'Status'),
    (N'dbo', N'DocumentVersions', N'IsCurrentOfficial'),
    (N'dbo', N'DocumentVersions', N'Notes'),
    (N'dbo', N'DocumentVersions', N'CreatedByUserId'),
    (N'dbo', N'DocumentVersions', N'CreatedUtc'),
    (N'dbo', N'DocumentVersions', N'UpdatedByUserId'),
    (N'dbo', N'DocumentVersions', N'UpdatedUtc'),
    (N'dbo', N'DocumentVersions', N'RowVersion'),
    (N'dbo', N'FileBlobs', N'FileBlobId'),
    (N'dbo', N'FileBlobs', N'BlobUid'),
    (N'dbo', N'FileBlobs', N'StoragePath'),
    (N'dbo', N'FileBlobs', N'OriginalFileName'),
    (N'dbo', N'FileBlobs', N'FileExtension'),
    (N'dbo', N'FileBlobs', N'ContentType'),
    (N'dbo', N'FileBlobs', N'ContentLengthBytes'),
    (N'dbo', N'FileBlobs', N'Sha256Hash'),
    (N'dbo', N'FileBlobs', N'UploadedByUserId'),
    (N'dbo', N'FileBlobs', N'CreatedUtc'),
    (N'dbo', N'FileBlobs', N'RowVersion'),
    (N'dbo', N'ApprovalRecords', N'DocumentVersionId'),
    (N'dbo', N'ApprovalRecords', N'Decision'),
    (N'dbo', N'ApprovalRecords', N'Comment'),
    (N'dbo', N'ApprovalRecords', N'DecidedByUserId'),
    (N'dbo', N'ApprovalRecords', N'DecidedUtc'),
    (N'dbo', N'PublicationRecords', N'DocumentVersionId'),
    (N'dbo', N'PublicationRecords', N'ActionType'),
    (N'dbo', N'PublicationRecords', N'Comment'),
    (N'dbo', N'PublicationRecords', N'ActedByUserId'),
    (N'dbo', N'PublicationRecords', N'ActedUtc'),
    (N'dbo', N'AuditEvents', N'EventUtc'),
    (N'dbo', N'AuditEvents', N'ActorUserId'),
    (N'dbo', N'AuditEvents', N'EntityType'),
    (N'dbo', N'AuditEvents', N'EntityId'),
    (N'dbo', N'AuditEvents', N'EventType'),
    (N'dbo', N'AuditEvents', N'OldValuesJson'),
    (N'dbo', N'AuditEvents', N'NewValuesJson'),
    (N'dbo', N'AuditEvents', N'Source'),
    (N'dbo', N'DocumentGroups', N'DocumentGroupId'),
    (N'dbo', N'DocumentGroups', N'ParentDocumentGroupId'),
    (N'dbo', N'DocumentGroups', N'Name'),
    (N'dbo', N'DocumentGroups', N'IsActive'),
    (N'dbo', N'DocumentGroups', N'CreatedByUserId'),
    (N'dbo', N'DocumentGroups', N'CreatedUtc'),
    (N'dbo', N'DocumentGroups', N'UpdatedByUserId'),
    (N'dbo', N'DocumentGroups', N'UpdatedUtc'),
    (N'dbo', N'DocumentGroups', N'RowVersion');

DECLARE @MissingColumns TABLE
(
    SchemaName sysname NOT NULL,
    TableName sysname NOT NULL,
    ColumnName sysname NOT NULL
);

INSERT INTO @MissingColumns (SchemaName, TableName, ColumnName)
SELECT rc.SchemaName, rc.TableName, rc.ColumnName
FROM @RequiredColumns rc
WHERE COL_LENGTH(rc.SchemaName + N'.' + rc.TableName, rc.ColumnName) IS NULL;

IF EXISTS (SELECT 1 FROM @MissingColumns)
BEGIN
    SELECT SchemaName, TableName, ColumnName
    FROM @MissingColumns
    ORDER BY SchemaName, TableName, ColumnName;

    RAISERROR('Baseline verification failed: expected StandardDetails column is missing.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.DocumentVersions', N'U')
      AND name = N'IX_DocumentVersions_DocumentId_VersionNumber'
)
BEGIN
    RAISERROR('Baseline verification failed: IX_DocumentVersions_DocumentId_VersionNumber is missing.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.DocumentVersions', N'U')
      AND name = N'UX_DocumentVersions_OneCurrentOfficialPerDocument'
      AND is_unique = 1
      AND has_filter = 1
)
BEGIN
    RAISERROR('Baseline verification failed: UX_DocumentVersions_OneCurrentOfficialPerDocument is missing or is not a unique filtered index.', 16, 1);
    ROLLBACK TRANSACTION;
    RETURN;
END;

SELECT 'Documents' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.Documents
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
UNION ALL
SELECT 'DocumentGroups', COUNT_BIG(*) FROM dbo.DocumentGroups
ORDER BY TableName;

COMMIT TRANSACTION;
