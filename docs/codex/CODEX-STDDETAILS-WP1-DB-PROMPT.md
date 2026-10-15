WP1 — ESTABLISH THE KorTransmittals MIGRATION HOME AND WRITE FOUR SQL FILES

You are writing SQL migration FILES only. Do not build, do not run tests, do not execute SQL,
do not touch git state. The files will be reviewed by Claude and run by Ian in SSMS as sa.

CONTEXT
- Blueprint: docs/KOR-StandardDetails-Governance-Review-2026-08-06.md — Part 2 sections A
  (DocumentVariants, includes a migration sketch) and B (DetailNumber). Follow its design;
  its do-nots are binding (no title-inferred DetailNumber, keep the module).
- The KorTransmittals database (KOR-APP01\SQLEXPRESS) holds the StandardDetails schema:
  dbo.Documents, dbo.DocumentVersions, dbo.FileBlobs, dbo.ApprovalRecords,
  dbo.PublicationRecords, dbo.AuditEvents, dbo.DocumentGroups (+ empty tag/category tables).
  The schema was hand-created; NO migration files exist anywhere. You are creating the home:
  db/KorTransmittals/  (new folder in this repo).
- App code that reads this schema: Kor.Operations.App/StandardDetails/StandardDetailsRepository.cs.
  Read it to match exact table/column names. Do NOT change any C# in this package.
- Live facts (verified 2026-09-01): Documents=12, DocumentVersions=12 (every one Status=0 Draft,
  IsCurrentOfficial=0), ApprovalRecords=1, PublicationRecords=1, AuditEvents=43, FileBlobs=12.
  ⚠ All 12 blob StoragePaths point under \\Kor-fs01\Drafting\Document Details\ which is EMPTY —
  every record is stranded module-test debris. They will be purged (files 003/004), so 002 must
  work correctly with 12 rows now and 0 rows later.

WRITE THESE FOUR FILES

db/KorTransmittals/001_BaselineAsFound.sql
  A VERIFICATION script, not a creator. Asserts (RAISERROR + ROLLBACK on failure) that the
  expected tables, key columns, and the two indexes named in the blueprint exist
  (IX_DocumentVersions_DocumentId_VersionNumber, UX_DocumentVersions_OneCurrentOfficialPerDocument),
  and prints the live row counts. Header comment: this file records the schema as found on
  2026-09-01 — the migration home starts here; nothing before it is scripted.

db/KorTransmittals/002_DocumentVariantsAndDetailNumber.sql
  Per the blueprint sketch, additive and re-runnable-safe (guard with IF NOT EXISTS / OBJECT_ID):
  - CREATE dbo.DocumentVariants exactly per the Part 2A sketch (all columns incl. RowVersion,
    FK to Documents, UX_DocumentVariants_Document_VariantKey).
  - ALTER dbo.DocumentVersions ADD DocumentVariantId bigint NULL + FK to DocumentVariants.
  - Backfill: one variant per existing Document (VariantKey N'DEFAULT'), assign every existing
    version to its document's DEFAULT variant. Must be a no-op when zero documents exist.
  - Then ALTER COLUMN DocumentVariantId bigint NOT NULL.
  - CREATE UNIQUE INDEX UX_DocumentVersions_Variant_VersionNumber (DocumentVariantId, VersionNumber).
  - CREATE UNIQUE INDEX UX_DocumentVersions_OneCurrentOfficialPerVariant (DocumentVariantId)
    WHERE IsCurrentOfficial = 1.
  - ⚠ KEEP the two old document-scoped indexes — they are dropped in WP2 when the code moves.
  - ALTER dbo.Documents ADD DetailNumber nvarchar(24) NULL
    + CHECK (DetailNumber IS NULL OR DetailNumber LIKE 'KOR-D-[0-9][0-9][0-9][0-9][0-9]')
    + CREATE UNIQUE INDEX UX_Documents_DetailNumber ON dbo.Documents(DetailNumber)
      WHERE DetailNumber IS NOT NULL.
  - SET XACT_ABORT ON, one transaction, and END with hard assertions (RAISERROR + ROLLBACK if
    violated): every DocumentVersion has a DocumentVariantId; variant count == document count;
    both new indexes exist. Print final counts.

db/KorTransmittals/003_PurgeStrandedTestRecords_PROPOSE.sql
  READ-ONLY. Lists exactly what 004 would delete, for Ian to read first: every Document
  (id, title, created), its versions (id, version, status), blobs (id, StoragePath,
  OriginalFileName, bytes), approval/publication rows that reference those versions, and the
  DEFAULT variants from 002. Final SELECT: one summary row of counts per table. No writes.

db/KorTransmittals/004_PurgeStrandedTestRecords_APPLY.sql
  Deletes what 003 listed, following the existing delete order in
  StandardDetailsRepository.DeleteRecordAsync (approvals → publications → versions → variants →
  documents → orphaned blobs). ⛔ Do NOT delete AuditEvents — audit is append-only history.
  SET XACT_ABORT ON, one transaction. GUARDS: abort with RAISERROR if any DocumentVersion has
  Status <> 0, or IsCurrentOfficial = 1, or Documents count <> 12 — this script may only ever
  delete the 12 known stranded drafts; if the world changed, it must refuse. End: assert all
  five tables empty (except AuditEvents), print final counts.

RULES
- SQL only, in those four files, nothing else created or modified.
- Match existing column names/types by READING StandardDetailsRepository.cs — do not guess.
- Plain ASCII in the files. Each file starts with a header comment: what it does, when to run
  it, and what must be true before/after.
- End your reply with an adversarial self-review: what could break the live app between 002
  landing and WP2 shipping (the app knows nothing of variants yet — show why additive+nullable
  backfill+kept indexes keeps every current code path working), and anything in the blueprint
  you deliberately deviated from, with why.
