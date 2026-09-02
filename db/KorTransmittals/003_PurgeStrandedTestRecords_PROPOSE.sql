/*
003_PurgeStrandedTestRecords_PROPOSE.sql

Read-only preview of the stranded StandardDetails test records that
004_PurgeStrandedTestRecords_APPLY.sql would delete. Run after 002 has added
DocumentVariants and before running 004. It writes nothing.

Before: the current KorTransmittals StandardDetails rows are known stranded
draft/module-test debris. After: no data has changed; result sets show the exact
documents, versions, blobs, approval/publication rows, DEFAULT variants, and
summary counts in scope for deletion.
*/


USE KorTransmittals;
IF DB_NAME() <> N'KorTransmittals' BEGIN RAISERROR('Wrong database on server %s. This script runs ONLY in KorTransmittals.', 20, 1, @@SERVERNAME) WITH LOG; END;
SELECT DB_NAME() AS [You are here];
SET NOCOUNT ON;

;WITH TargetDocuments AS
(
    SELECT d.DocumentId, d.Title, d.CreatedUtc
    FROM dbo.Documents d
)
SELECT td.DocumentId, td.Title, td.CreatedUtc
FROM TargetDocuments td
ORDER BY td.DocumentId;

;WITH TargetDocuments AS
(
    SELECT d.DocumentId
    FROM dbo.Documents d
)
SELECT
    v.DocumentVersionId,
    v.DocumentId,
    v.VersionNumber,
    v.Status,
    v.IsCurrentOfficial,
    v.CreatedUtc
FROM dbo.DocumentVersions v
INNER JOIN TargetDocuments td ON td.DocumentId = v.DocumentId
ORDER BY v.DocumentId, v.VersionNumber, v.DocumentVersionId;

;WITH TargetDocuments AS
(
    SELECT d.DocumentId
    FROM dbo.Documents d
),
TargetVersions AS
(
    SELECT v.DocumentVersionId, v.FileBlobId
    FROM dbo.DocumentVersions v
    INNER JOIN TargetDocuments td ON td.DocumentId = v.DocumentId
)
SELECT DISTINCT
    fb.FileBlobId,
    fb.StoragePath,
    fb.OriginalFileName,
    fb.ContentLengthBytes AS Bytes
FROM dbo.FileBlobs fb
INNER JOIN TargetVersions tv ON tv.FileBlobId = fb.FileBlobId
ORDER BY fb.FileBlobId;

;WITH TargetDocuments AS
(
    SELECT d.DocumentId
    FROM dbo.Documents d
),
TargetVersions AS
(
    SELECT v.DocumentVersionId
    FROM dbo.DocumentVersions v
    INNER JOIN TargetDocuments td ON td.DocumentId = v.DocumentId
)
SELECT
    ar.*
FROM dbo.ApprovalRecords ar
INNER JOIN TargetVersions tv ON tv.DocumentVersionId = ar.DocumentVersionId
ORDER BY ar.DocumentVersionId;

;WITH TargetDocuments AS
(
    SELECT d.DocumentId
    FROM dbo.Documents d
),
TargetVersions AS
(
    SELECT v.DocumentVersionId
    FROM dbo.DocumentVersions v
    INNER JOIN TargetDocuments td ON td.DocumentId = v.DocumentId
)
SELECT
    pr.*
FROM dbo.PublicationRecords pr
INNER JOIN TargetVersions tv ON tv.DocumentVersionId = pr.DocumentVersionId
ORDER BY pr.DocumentVersionId;

;WITH TargetDocuments AS
(
    SELECT d.DocumentId
    FROM dbo.Documents d
)
SELECT
    dv.DocumentVariantId,
    dv.DocumentId,
    dv.VariantKey,
    dv.SheetSize,
    dv.KorStandardsSizeToken,
    dv.IsActive,
    dv.CreatedUtc
FROM dbo.DocumentVariants dv
INNER JOIN TargetDocuments td ON td.DocumentId = dv.DocumentId
WHERE dv.VariantKey = N'DEFAULT'
ORDER BY dv.DocumentId, dv.DocumentVariantId;

;WITH TargetDocuments AS
(
    SELECT d.DocumentId
    FROM dbo.Documents d
),
TargetVersions AS
(
    SELECT v.DocumentVersionId, v.FileBlobId
    FROM dbo.DocumentVersions v
    INNER JOIN TargetDocuments td ON td.DocumentId = v.DocumentId
)
SELECT
    (SELECT COUNT_BIG(*) FROM TargetDocuments) AS Documents,
    (SELECT COUNT_BIG(*) FROM TargetVersions) AS DocumentVersions,
    (SELECT COUNT_BIG(DISTINCT FileBlobId) FROM TargetVersions) AS FileBlobs,
    (SELECT COUNT_BIG(*) FROM dbo.ApprovalRecords ar INNER JOIN TargetVersions tv ON tv.DocumentVersionId = ar.DocumentVersionId) AS ApprovalRecords,
    (SELECT COUNT_BIG(*) FROM dbo.PublicationRecords pr INNER JOIN TargetVersions tv ON tv.DocumentVersionId = pr.DocumentVersionId) AS PublicationRecords,
    (SELECT COUNT_BIG(*) FROM dbo.DocumentVariants dv INNER JOIN TargetDocuments td ON td.DocumentId = dv.DocumentId WHERE dv.VariantKey = N'DEFAULT') AS DefaultDocumentVariants;
