/*
010_PurgeTheAssembledCatalogue.sql

The app-side half of KOR.Drafter\db\105_PurgeTheAssembledCatalogue.sql (Ian, 2026-10-23: "Nelson's / hard
delete"). Run it right after 105, before 106 loads the catalogue from Nelson's masters.

DELETED: every Standard Details document that carries a KOR-D number (each one belongs to the assembled
catalogue 105 removes) with its whole chain, in the order 004_PurgeStrandedTestRecords_APPLY.sql proved:
  approval records -> publication records -> versions -> variants -> promotion-queue rows -> documents ->
  the file blobs only those versions used.
KEPT: documents with no KOR-D number (reported, untouched); AuditEvents (append-only, as in 004); every
  table, procedure and grant.

GATE: set @Confirm to the exact phrase or nothing is deleted. Run as sa in KorTransmittals.
One transaction, XACT_ABORT: anything unexpected rolls ALL of it back.
*/

IF DB_NAME() <> N'KorTransmittals' BEGIN RAISERROR('Wrong database on server %s. This runs ONLY in KorTransmittals.', 20, 1, @@SERVERNAME) WITH LOG; END;
SELECT DB_NAME() AS [You are here];
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Confirm nvarchar(100) = N'';   -- type: PURGE THE ASSEMBLED CATALOGUE
DECLARE @Phrase  nvarchar(100) = N'PURGE THE ASSEMBLED CATALOGUE';

DECLARE @Docs TABLE (DocumentId bigint PRIMARY KEY);
INSERT INTO @Docs SELECT DocumentId FROM dbo.Documents WHERE DetailNumber IS NOT NULL;

SELECT 'BEFORE' AS phase, x.name AS [table], x.n AS [rows]
FROM (VALUES
    (N'Documents with a KOR-D number (deleted)', (SELECT COUNT_BIG(*) FROM @Docs)),
    (N'Documents without one (kept)',            (SELECT COUNT_BIG(*) FROM dbo.Documents WHERE DetailNumber IS NULL)),
    (N'their DocumentVersions',  (SELECT COUNT_BIG(*) FROM dbo.DocumentVersions v WHERE v.DocumentId IN (SELECT DocumentId FROM @Docs))),
    (N'their DocumentVariants',  (SELECT COUNT_BIG(*) FROM dbo.DocumentVariants d WHERE d.DocumentId IN (SELECT DocumentId FROM @Docs))),
    (N'their promotion-queue rows', (SELECT COUNT_BIG(*) FROM dbo.StandardDetailPromotionOutbox o WHERE o.DocumentId IN (SELECT DocumentId FROM @Docs))),
    (N'promotion-queue rows in all', (SELECT COUNT_BIG(*) FROM dbo.StandardDetailPromotionOutbox))
) x(name, n);

SELECT 'DOCUMENTS TO DELETE' AS phase, d.DocumentId, d.DetailNumber
FROM dbo.Documents d WHERE d.DocumentId IN (SELECT DocumentId FROM @Docs) ORDER BY d.DetailNumber;

IF @Confirm <> @Phrase
BEGIN
    SELECT N'Nothing deleted. Read the lists above, then set @Confirm to ''' + @Phrase + N''' and run again.' AS [next];
    RETURN;
END;

DECLARE @Kept bigint = (SELECT COUNT_BIG(*) FROM dbo.Documents WHERE DetailNumber IS NULL);
DECLARE @Blobs TABLE (FileBlobId bigint PRIMARY KEY);

BEGIN TRY
    BEGIN TRANSACTION;

    INSERT INTO @Blobs SELECT DISTINCT v.FileBlobId FROM dbo.DocumentVersions v
    WHERE v.FileBlobId IS NOT NULL AND v.DocumentId IN (SELECT DocumentId FROM @Docs);

    DELETE ar FROM dbo.ApprovalRecords ar
    INNER JOIN dbo.DocumentVersions v ON v.DocumentVersionId = ar.DocumentVersionId
    WHERE v.DocumentId IN (SELECT DocumentId FROM @Docs);

    DELETE pr FROM dbo.PublicationRecords pr
    INNER JOIN dbo.DocumentVersions v ON v.DocumentVersionId = pr.DocumentVersionId
    WHERE v.DocumentId IN (SELECT DocumentId FROM @Docs);

    DELETE FROM dbo.StandardDetailPromotionOutbox WHERE DocumentId IN (SELECT DocumentId FROM @Docs);
    DELETE FROM dbo.DocumentVersions WHERE DocumentId IN (SELECT DocumentId FROM @Docs);
    DELETE FROM dbo.DocumentVariants WHERE DocumentId IN (SELECT DocumentId FROM @Docs);
    DELETE FROM dbo.Documents WHERE DocumentId IN (SELECT DocumentId FROM @Docs);

    -- a blob is deleted only if no remaining version uses it
    DELETE fb FROM dbo.FileBlobs fb
    WHERE fb.FileBlobId IN (SELECT FileBlobId FROM @Blobs)
      AND NOT EXISTS (SELECT 1 FROM dbo.DocumentVersions dv WHERE dv.FileBlobId = fb.FileBlobId);

    IF EXISTS (SELECT 1 FROM dbo.Documents WHERE DetailNumber IS NOT NULL)
        RAISERROR('A document with a KOR-D number remains - rolling back everything.', 16, 1);
    IF (SELECT COUNT_BIG(*) FROM dbo.Documents WHERE DetailNumber IS NULL) <> @Kept
        RAISERROR('A document without a KOR-D number changed - rolling back everything.', 16, 1);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT 'AFTER' AS phase, x.name AS [table], x.n AS [rows]
FROM (VALUES
    (N'Documents with a KOR-D number', (SELECT COUNT_BIG(*) FROM dbo.Documents WHERE DetailNumber IS NOT NULL)),
    (N'Documents without one (kept)',  (SELECT COUNT_BIG(*) FROM dbo.Documents WHERE DetailNumber IS NULL)),
    (N'promotion-queue rows',          (SELECT COUNT_BIG(*) FROM dbo.StandardDetailPromotionOutbox))
) x(name, n);
