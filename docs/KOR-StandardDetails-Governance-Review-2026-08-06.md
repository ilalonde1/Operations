# KOR StandardDetails Governance Review - 2026-08-06

Scope audited: `Kor.Operations.App/StandardDetails/*`, app entry/role wiring that gates it, and SELECT-only evidence from `KorTransmittals` plus the allowed `KorStandards` views `detail.vw_PaletteCatalog` and `detail.vw_DetailPlaceable`.

No code, DB, or file-store writes were performed for this review.

## Executive Read

The Standard Details module is not a finished governance cockpit yet, but it has useful working machinery: document records, version upload, file blob storage, status workflow, approvals, publication records, audit events, group tree organization, and non-published PDF watermarking on open.

The main caution is identity/shape mismatch. Today the app governs 12 broad document records, not the 612 canonical `KOR-D-#####` details in KorStandards and not the 350-ish RFA component register. It also has no concept of variants/sheet sizes. Wiring it directly to KorStandards promotion without first adding detail linkage and variant identity would create ambiguous governance.

Recommended path: keep the module, add the missing identity layer, then wire approval to KorStandards through an auditable outbox. No rebuild is needed.

## Part 1 - Deep Machinery Findings

### 1. Approval Workflow

Current states in code:

| Value | Meaning | Evidence |
| --- | --- | --- |
| 0 | Draft | `Kor.Operations.App/StandardDetails/StandardDetailsWindow.xaml.cs:18`, `Kor.Operations.App/StandardDetails/StandardDetailsWindow.Logic.cs:270` |
| 1 | Submitted | `Kor.Operations.App/StandardDetails/StandardDetailsWindow.xaml.cs:19`, `Kor.Operations.App/StandardDetails/StandardDetailsWindow.Logic.cs:271` |
| 2 | Approved | `Kor.Operations.App/StandardDetails/StandardDetailsWindow.xaml.cs:20`, `Kor.Operations.App/StandardDetails/StandardDetailsWindow.Logic.cs:272` |
| 3 | Rejected | `Kor.Operations.App/StandardDetails/StandardDetailsWindow.xaml.cs:21`, `Kor.Operations.App/StandardDetails/StandardDetailsWindow.Logic.cs:273` |
| 4 | Published | `Kor.Operations.App/StandardDetails/StandardDetailsWindow.xaml.cs:22`, `Kor.Operations.App/StandardDetails/StandardDetailsWindow.Logic.cs:274` |

There is also an `Archived` label for status 5 in mapping only, but no UI action or repository path reaches it: `Kor.Operations.App/StandardDetails/StandardDetailsRepository.cs:629`.

Legal transitions enforced by the current app:

| Transition | Trigger | Required role | Evidence |
| --- | --- | --- | --- |
| new version -> Draft | Upload version | Contributor or above | `StandardDetailsWindow.Logic.cs:397`, `StandardDetailsRepository.cs:347` |
| Draft -> Submitted | Submit | Contributor or above | `StandardDetailsWindow.Logic.cs:416`, `StandardDetailsRepository.cs:447` |
| Submitted -> Approved | Approve | Approver, Publisher, or Admin | `StandardDetailsWindow.Logic.cs:417`, `StandardDetailsRepository.cs:487` |
| Submitted -> Rejected | Reject | Approver, Publisher, or Admin | `StandardDetailsWindow.Logic.cs:418`, `StandardDetailsRepository.cs:487` |
| Approved -> Published + current official | Publish Official | Publisher or Admin | `StandardDetailsWindow.Logic.cs:419`, `StandardDetailsRepository.cs:540` |

The UI buttons mirror these transition rules: submit is enabled only for Draft, approve/reject only for Submitted, and publish only for Approved (`StandardDetailsWindow.xaml.cs:130`). The footer presents the same workflow guide: Draft -> Submitted -> Approved/Rejected -> Published (`StandardDetailsWindow.xaml:505`).

Access policy:

- Home tile visibility is gated by the broad `StandardDetails` role (`HomeWindow.xaml.cs:244`, `HomeWindow.xaml:244`).
- Role membership is app-config based, not database/ASP.NET identity based: `SecurityGroupAccess` reads `SecurityGroup.<role>.Members` from configuration (`Kor.Operations.App/Services/SecurityGroupAccess.cs:14`).
- Current configured role members are in `App.config:125-129`.
- `StandardDetailsAccessPolicy` resolves user identity from header email, then `UserUpnOverride`, then `Environment.UserName + @korstructural.com` (`StandardDetailsAccessPolicy.cs:18`).
- The actor GUID stored in workflow rows is not a real user FK. It is a deterministic SHA-derived GUID from `Environment.UserName` (`StandardDetailsWindow.xaml.cs:23`, `StandardDetailsWindow.xaml.cs:60`).

ApprovalRecords:

- On approve/reject, `DecideAsync` updates `DocumentVersions.Status` from Submitted to target status, then inserts `ApprovalRecords(DocumentVersionId, Decision, Comment, DecidedByUserId, DecidedUtc)`, then inserts an `AuditEvents` status change (`StandardDetailsRepository.cs:496`, `StandardDetailsRepository.cs:517`, `StandardDetailsRepository.cs:527`).
- Decision values used by code are `1 = approved`, `2 = rejected` (`StandardDetailsWindow.Logic.cs:417-418`).
- Comments are hardcoded: "Approved from Operations module" or "Rejected from Operations module" (`StandardDetailsRepository.cs:522`).
- The workflow is optimistic-concurrency guarded by `RowVersion` on status updates (`StandardDetailsRepository.cs:453`, `StandardDetailsRepository.cs:496`, `StandardDetailsRepository.cs:564`).

One-current-official constraint:

- DB evidence: unique filtered index `UX_DocumentVersions_OneCurrentOfficialPerDocument` on `DocumentId` with `is_unique=1` and `has_filter=1`.
- Publish first clears existing official rows for the same `DocumentId`, then sets the selected version to `Status=4, IsCurrentOfficial=1` (`StandardDetailsRepository.cs:557`, `StandardDetailsRepository.cs:564`).
- The one-current-official rule is per document, not per detail number and not per sheet-size variant. This is a blocker for the target "one detail, five sheet sizes" model until variants are represented.

Live workflow data:

- `KorTransmittals` counts: `Documents=12`, `DocumentVersions=12`, `ApprovalRecords=1`, `PublicationRecords=1`, `AuditEvents=43`.
- Current status distribution: all 12 live `DocumentVersions` are status 0 Draft; current official count is 0.
- There are no current official collisions.
- Consistency caution: `ApprovalRecords` row 6 and `PublicationRecords` row 7 both point at `DocumentVersionId=83`, but that version is currently Draft and not official. Recent `AuditEvents` also reference deleted/non-live version ids 95-97. This means audit is historical/append-only, while approval/publication records can remain inconsistent with current status after later mutation or repair.

### 2. Publication Pipeline

Current StandardDetails publication does exactly this:

1. Verify selected version is Approved (`StandardDetailsRepository.cs:545`).
2. Clear `IsCurrentOfficial` on all versions for the same document (`StandardDetailsRepository.cs:557`).
3. Set selected version to `Status=4, IsCurrentOfficial=1` (`StandardDetailsRepository.cs:564`).
4. Insert `PublicationRecords(DocumentVersionId, ActionType=1, Comment, ActedByUserId, ActedUtc)` (`StandardDetailsRepository.cs:584`).
5. Insert an `AuditEvents` status change containing old/new status and official flag (`StandardDetailsRepository.cs:592`).

What it does not do:

- It does not create a public URL.
- It does not insert `RedirectTargets`.
- It does not log `OpenEvents` or `ClickEvents`.
- It does not notify or synchronize KorStandards.
- It does not watermark or export a published artifact; published files open from their stored original path.

RedirectTargets/OpenEvents/ClickEvents are transmittal telemetry, not StandardDetails publishing:

- `TransmittalService` inserts `RedirectTargets` when building tracking links for emailed transmittals (`Kor.Operations.App/Services/TransmittalService.cs:352`).
- `QuickTransferRunner` does the same for file transfers (`Kor.Operations.App/QuickTransferRunner.cs:478`).
- `SqlTransmittalsStore` consumes `OpenEvents` and `ClickEvents` in transmittal dashboard summary/activity queries (`Kor.Operations.Data/SqlTransmittalsStore.cs:244`, `SqlTransmittalsStore.cs:372`).
- Live telemetry counts by `Transmittals.Type`: RedirectTargets are `Transmittal=3818`, `Transfer=149`, `NULL=5`; OpenEvents are `Transmittal=8043`, `Transfer=262`; ClickEvents are `Transmittal=2417`, `Transfer=82`, `NULL=1`.

Current publication telemetry for StandardDetails is limited to `PublicationRecords` plus `AuditEvents`.

### 3. Watermarking

Watermarking is found and is deeper than a label, but it only runs on open/view copies.

Flow:

- User selects a revision and clicks open (`StandardDetailsWindow.Logic.cs:411`).
- `StandardDetailsFileStore.OpenVersionFile` checks that the stored file exists (`StandardDetailsFileStore.cs:100`).
- If status is not Published (`status != 4`), it calls `StatusWatermarkRenderer.TryPrepareOpenCopy` (`StandardDetailsFileStore.cs:108`).
- The temp output directory is `%LocalAppData%/KorTransmittals/Temp/StandardDetailsWatermarked` (`StatusWatermarkRenderer.cs:25`).
- For PDFs, `CreateWatermarkedPdf` imports every source page and appends repeated red, translucent, diagonal status text across the page (`StatusWatermarkRenderer.cs:42`, `StatusWatermarkRenderer.cs:63`).
- For non-PDFs such as DWG/DOCX, the code cannot draw a visual watermark; it copies the file to a status-tagged temp filename and returns a warning (`StatusWatermarkRenderer.cs:44`).

Status stamp text:

- Draft -> `DRAFT`
- Submitted -> `SUBMITTED`
- Approved -> `APPROVED - NOT PUBLISHED`
- Rejected -> `REJECTED`
- Other non-published values -> uppercase fallback/status text

Evidence: `StandardDetailsFileStore.cs:129`.

Important boundary: this does not permanently stamp the stored blob. It creates a local temp copy when opened. Published versions bypass watermark preparation and launch the original stored path.

### 4. Versioning and Storage

Version lifecycle:

- Documents are top-level records with `DocumentUid`, `Title`, `Description`, optional `DocumentGroupId`, created/updated user ids and rowversion.
- Versions are child rows with `VersionUid`, `DocumentId`, `VersionNumber`, `FileBlobId`, `Status`, `IsCurrentOfficial`, optional notes, created/updated ids and rowversion.
- File metadata is separate in `FileBlobs`: `BlobUid`, `StoragePath`, `OriginalFileName`, extension, content type, byte length, SHA-256, uploader, created time and rowversion.

Upload path:

- `UploadVersionAsync` locks the document and computes `nextVersion = MAX(VersionNumber)+1` under `UPDLOCK, HOLDLOCK` in a serializable transaction (`StandardDetailsRepository.cs:319`, `StandardDetailsRepository.cs:325`).
- The file is copied before DB insert into `\\Kor-fs01\Drafting\Document Details\<safe title> (ID <id>)\v<version>\<guid><ext>` unless configured otherwise (`StandardDetailsFileStore.cs:47`, `StandardDetailsFileStore.cs:52`).
- SHA-256 is calculated after copy and inserted into `FileBlobs.Sha256Hash` (`StandardDetailsFileStore.cs:58`, `StandardDetailsRepository.cs:333`).
- Version rows start as Draft and non-official (`StandardDetailsRepository.cs:347`).
- If DB commit fails, the prepared file is cleaned up (`StandardDetailsRepository.cs:360`, `StandardDetailsFileStore.cs:66`).

Delete path:

- DB deletion deletes approval records, publication records, document versions, document, and unreferenced blobs in one transaction, outputting blob storage paths (`StandardDetailsRepository.cs:387-417`).
- After DB commit, the UI deletes physical files best-effort (`StandardDetailsWindow.Logic.cs:428`, `StandardDetailsWindow.Logic.cs:430`, `StandardDetailsFileStore.cs:82`).
- If file deletion fails after DB commit, DB metadata is gone but the file can remain stranded. The code logs a warning but does not retry or queue cleanup (`StandardDetailsFileStore.cs:89`).
- Audit events are not deleted by `DeleteRecordAsync`, so historical audit can refer to deleted `DocumentVersionId` values.

Live storage health:

- DB evidence: 12 `FileBlobs`, 12 `DocumentVersions`, no versions missing a blob, no bad-length SHA rows, no blank storage paths.
- Filesystem evidence from this workstation: all 12 stored UNC paths under `\\Kor-fs01\Drafting\Document Details\...` returned `Test-Path=False`, so SHA recheck could not be performed. This could be missing files, a share/access issue, or path visibility from this execution context. It is a real operational verification item before relying on the current 12 masters.

Restore path:

- There is no application-level restore/relink workflow for stranded blobs. Existing rows can open only through `FileBlobs.StoragePath`; if that path is inaccessible, open fails with "File not found in storage" (`StandardDetailsWindow.Logic.cs:411`).
- Recovery today is an operator/DB/file-server task, not app machinery.

### 5. State of Health

Good machinery to keep:

- Straight SqlClient repository with small, explicit operations.
- Optimistic concurrency on status transitions.
- Append audit for status changes and deletes.
- Dedicated file blob table with SHA-256.
- Filtered unique official-version index.
- Group tree and basic grouping fallback if schema is unavailable.
- PDF watermark-on-open for non-published revisions.

Cautions and dead/half-built paths:

- `AspNetUsers` exists in schema but StandardDetails does not use it for real identity. Actor ids are stable hashes of Windows username.
- `DocumentVersionCategories`, `DocumentVersionTags`, `DocumentVersionKeywords`, and `DocumentGroupAssignments` exist and currently have 0 rows. Repo search found no StandardDetails C#/XAML consumer for those tables.
- `Documents` has no `DetailNumber`; `DocumentVersions` has no `SheetSize`, `Variant`, or `DocumentVariantId`.
- `_test.txt` contains only `test` and appears to be leftover debris in the module.
- `ApprovalRecords`/`PublicationRecords` can be inconsistent with current version status based on live row 83 evidence.
- Watermarking is view-time only. It is not publication-time artifact generation.
- Published status is the only status that bypasses watermarking; Approved still opens as "APPROVED - NOT PUBLISHED".
- The current module governs 12 package-like records, while KorStandards has 612 distinct detail numbers in the palette catalog.

## Part 2 - Integration Design

### A. Variant Gap

Confirmed: the current schema cannot express "one detail, five sheet sizes" correctly.

Evidence:

- `DocumentVersions` has `DocumentId`, `VersionNumber`, `FileBlobId`, `Status`, `IsCurrentOfficial`, notes and audit columns only. No `SheetSize`, `Variant`, or variant FK exists.
- Unique versioning is `IX_DocumentVersions_DocumentId_VersionNumber`, so version numbers are scoped to the whole document, not to a sheet-size variant.
- The official constraint is per `DocumentId`, so only one sheet size could be official at a time.
- KorStandards view evidence: `detail.vw_PaletteCatalog` has 1,079 rows, 612 distinct `DetailNumber` values, 432 rows with `SizeToken`, and size tokens including `D`, `D LOW RISE`, `E`, `E1`, and `1E LOW RISE`. Some details have multiple rows/size tokens.

Recommended fix: `DocumentVariants` table.

Why table over column:

- A sheet-size variant needs stable identity, not just an attribute on a version row.
- Version numbers should be scoped per variant.
- Current official should be one per variant, not one per document.
- Variant metadata will likely need lifecycle fields: active/retired, display order, KorStandards size token, caveat, and possibly target output path.
- A column on `DocumentVersions` would require changing the same indexes and code paths anyway, but would still lack a canonical variant record to rename/retire.

Migration sketch:

```sql
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

CREATE UNIQUE INDEX UX_DocumentVariants_Document_VariantKey
ON dbo.DocumentVariants(DocumentId, VariantKey);

ALTER TABLE dbo.DocumentVersions ADD DocumentVariantId bigint NULL;

-- Backfill one DEFAULT variant per existing document, then assign existing versions.
-- After backfill, make DocumentVariantId NOT NULL.

CREATE UNIQUE INDEX UX_DocumentVersions_Variant_VersionNumber
ON dbo.DocumentVersions(DocumentVariantId, VersionNumber);

CREATE UNIQUE INDEX UX_DocumentVersions_OneCurrentOfficialPerVariant
ON dbo.DocumentVersions(DocumentVariantId)
WHERE IsCurrentOfficial = 1;
```

Then deprecate/drop `IX_DocumentVersions_DocumentId_VersionNumber` and `UX_DocumentVersions_OneCurrentOfficialPerDocument` once code is moved to variant-scoped upload/publish.

Estimated effort: 2 to 3 days for schema, repository, UI selector, migration/backfill, and focused tests.

### B. DetailNumber Linkage

A `Document` needs to become the issued record for exactly one `KOR-D-#####` detail. I recommend adding explicit linkage on `Documents`, not inferring from title.

Minimal schema:

```sql
ALTER TABLE dbo.Documents ADD DetailNumber nvarchar(24) NULL;

CREATE UNIQUE INDEX UX_Documents_DetailNumber
ON dbo.Documents(DetailNumber)
WHERE DetailNumber IS NOT NULL;
```

Also add a check constraint for format if acceptable:

```sql
CHECK (DetailNumber IS NULL OR DetailNumber LIKE 'KOR-D-[0-9][0-9][0-9][0-9][0-9]')
```

Why a column first:

- The target says a Document becomes the issued record of one `KOR-D-#####`.
- A one-to-one nullable unique column is simpler and clearer than a mapping table.
- A mapping table is only justified if one app document must govern multiple detail numbers or if mappings need independent effective dating. Neither is the stated target.

UI behavior:

- New/Edit Document gets a "Link KorStandards Detail" picker fed by `detail.vw_PaletteCatalog` distinct detail numbers.
- Show collision warnings when a detail number is already linked.
- Do not let approval promotion run unless `Documents.DetailNumber` and the selected variant are resolved.

Estimated effort: 1 to 1.5 days including migration and UI picker.

### C. Promotion Hook

Target intent says approval in this app becomes promotion to `Confidence='human-confirmed'` plus a `DetailHistory` row in KorStandards.

Recommended hook point: `StandardDetailsRepository.DecideAsync`, only when `decision == 1` and the target status is Approved.

Reason:

- `DecideAsync` is where the gatekeeper decision is recorded in `ApprovalRecords`.
- It already has the transaction containing status update, approval row, and audit event.
- `PublishAsync` is a separate current-official/version-publication action today. If KorStandards placeability must follow approval, wiring publish would be semantically late.

Recommended write mechanism: KorTransmittals outbox, processed by a small app/service path with a restricted KorStandards write login.

Sketch:

```sql
CREATE TABLE dbo.StandardDetailPromotionOutbox
(
    PromotionOutboxId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_StandardDetailPromotionOutbox PRIMARY KEY,
    DocumentVersionId bigint NOT NULL,
    DocumentId bigint NOT NULL,
    DocumentVariantId bigint NULL,
    DetailNumber nvarchar(24) NOT NULL,
    TargetConfidence nvarchar(32) NOT NULL,
    RequestedByUserId uniqueidentifier NOT NULL,
    RequestedUtc datetime2 NOT NULL,
    ProcessedUtc datetime2 NULL,
    ProcessedBy nvarchar(128) NULL,
    ResultJson nvarchar(max) NULL,
    ErrorJson nvarchar(max) NULL,
    RetryCount int NOT NULL CONSTRAINT DF_StandardDetailPromotionOutbox_RetryCount DEFAULT (0)
);
```

Processing action in KorStandards:

- Resolve `DetailNumber`.
- Set detail confidence to `human-confirmed`.
- Insert `DetailHistory` with source app, actor, approval record/version/document ids, old confidence, new confidence, and timestamp.
- Return/update result in outbox.

Why outbox over direct cross-database write in `DecideAsync`:

- Avoids partial cross-system failure or distributed transaction assumptions.
- Preserves the current app workflow even if KorStandards is temporarily unavailable.
- Gives an auditable retry queue.
- Lets KorStandards use a narrow write login such as `standards_promoter` instead of giving the whole desktop app broad write rights.

Audit both sides:

- KorTransmittals: existing `ApprovalRecords` and `AuditEvents`, plus outbox request/process rows.
- KorStandards: `DetailHistory` row with source ids back to KorTransmittals.

Estimated effort: 2 to 3 days after `DetailNumber` and variants exist; 3 to 4 days if also adding a background worker, retry UI, and reconciliation view.

### D. Register Views

Details register:

- Add a StandardDetails read repository for KorStandards using the existing SqlClient style.
- Read `detail.vw_PaletteCatalog` grouped by `DetailNumber`, with child rows for `ViewName`/`SizeToken`.
- Read `detail.vw_DetailPlaceable` for currently placeable/human-confirmed detail rows. Current evidence shows 0 rows, because all 1,079 palette rows are `Confidence='unverified'` and `IsPlaceable=0`.
- Join in KorTransmittals documents by `DetailNumber` once added, showing linked document, latest app status, official version per variant, approval state, and promotion outbox state.

Components register:

- The allowed KorStandards views do not expose component/RFA rows. Do not shoehorn components into the details palette.
- Add a read-only KorStandards view such as `detail.vw_ComponentRegister` exposing component id/key, family name/path/category, status, adopted/retired flags, updated timestamp, and usage/occurrence counts if available.
- Surface it as its own tab/register beside Details.

UI sketch:

- Keep the existing left group tree for Operations document organization.
- Add top-level tabs: `Documents`, `Details Register`, `Components Register`, `Promotion Queue`.
- `Details Register`: dense grid grouped by `DetailNumber`, with status chips for KorStandards confidence/placeable, linked Operations document, variants, latest app version, and actions: Link, Open, Submit, Approve, Publish.
- `Components Register`: grid/tree for canonical `.rfa` families with adopt/retire/rename decisions. It should not share the document version grid unless a component has an attached governance document.
- `Promotion Queue`: failed/pending outbox rows, retry action for admins/publishers, and reconciliation against KorStandards confidence.

Estimated effort:

- Details read register: 2 days after `DetailNumber` exists.
- Components read register: 2 to 3 days once KorStandards provides a component view.
- Promotion queue UI: 1 to 1.5 days.

### E. Effort Honesty

Estimated A-D total:

| Work | Size |
| --- | --- |
| A. Variant table and variant-scoped versioning/current-official | 2 to 3 days |
| B. DetailNumber linkage and picker | 1 to 1.5 days |
| C. Promotion hook and KorStandards outbox processing | 2 to 4 days |
| D. Register views/UI | 4 to 6.5 days depending on component view availability |

Realistic total: 9 to 15 days including tests and live-data migration rehearsal.

Risks:

- Current file paths for all 12 live blobs were not visible from this workstation. Resolve before using current records as authoritative masters.
- Current approval/publication rows are not a reliable current-state source by themselves; current state must come from `DocumentVersions`.
- Actor identity is not enterprise identity. A new identity system must replace or map `CreateStableUserGuid(Environment.UserName)`.
- KorStandards write contract is not available through the allowed views, so promotion needs an explicit stored procedure/API/table contract from KorStandards.
- Components register cannot be implemented cleanly from the currently allowed views.
- Variant migration touches core assumptions in version numbering and official status. This is small but not a one-column cosmetic change.

What should not be done:

- Do not rebuild the module for style or framework preference.
- Do not infer `DetailNumber` from title or filename.
- Do not overload `VersionNumber` to mean both supersession and sheet size.
- Do not make KorStandards promotion a silent side effect with no outbox/retry/audit.
- Do not grant broad KorStandards write access to the desktop app.
- Do not treat RedirectTargets/OpenEvents/ClickEvents as StandardDetails publication telemetry without designing a StandardDetails-specific published-link model.
- Do not move editing into this app; keep editing in Revit as intended.

## GO / CAUTION

GO:

- Keep the current StandardDetails machinery.
- Add `Documents.DetailNumber`.
- Add `DocumentVariants`.
- Move current-official uniqueness to variant scope.
- Add KorStandards read-side register screens using views.
- Wire approval to KorStandards through an auditable promotion outbox.

CAUTION:

- Verify or restore the 12 current file paths on `\\Kor-fs01\Drafting`.
- Treat current `ApprovalRecords`/`PublicationRecords` as historical event records, not current truth.
- Replace stable username hashes with the new identity model before relying on approvals as governance-grade identity.
- Add a KorStandards component read view before building Register 1.

STDDETAILS-REVIEW - watermarking found, workflow-states 5, variant-fix table, promotion-hook 3d, rebuild-needed no
