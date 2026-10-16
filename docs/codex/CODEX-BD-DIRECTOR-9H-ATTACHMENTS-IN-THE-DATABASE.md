# BD Director 9h — attachments in the database

Bounded brief for Codex. Source edits only; nothing is built, run, applied or deployed by you.
Response goes beside this file as `CODEX-BD-DIRECTOR-9H-ATTACHMENTS-IN-THE-DATABASE-RESPONSE.md`
in the 9a–9g format.

## Why this exists

Ian, 2026-10-15: "Can we store the documents in the db as opposed to links to files on disk?"
Yes, and the index already exists. Searched before writing (CLAUDE.md rule 1):

- `opportunities.OpportunityFiles` (`Schema/275_PursuitFiles.sql`) is the pursuit attachment
  index, keyed on `EngagementId` (so BD-tracking pursuits can attach too), with `FileName`,
  `Sha256`, `SizeBytes`, `ContentType`, `UploadedAtUtc`, `UploadedBy` and a `LocalPath` on a LAN
  share. It holds 1 row.
- `Kor.Opportunities.Data/Crm/SqlPursuitFileStore.cs`: `IPursuitFileStore` with
  `ListByEngagementAsync`, `AddAsync(engagementId, opportunityId, fileName, localPath, sha256,
  sizeBytes, contentType, uploadedBy)` and `RemoveAsync` (returns the path for the caller to delete).
  The store never touches bytes.
- `Kor.Operations.App/Crm/PursuitFileStorage.cs`: `IPursuitFileStorage` with `IsConfigured`,
  `StoreAsync(engagementId, pursuitLabel, sourcePath)` (copies into a per-pursuit folder under the
  `BD.PursuitFilesRoot` app setting, hashes with SHA-256) and `TryDelete`. The CRM screen's attach
  button and drag-and-drop (`CrmView.xaml.cs` `AttachFilesAsync` → `CrmViewModel.AddFilesAsync`)
  call storage then store; Open shell-executes the stored path (`CrmView.xaml.cs` line 769).
- `opportunities.OpportunityDocuments` (282) is a different thing, the source-published RFP
  documents found by the enricher, and is untouched here.
- 9g (`dcd6e883`) added `CrmEngagements.SubmittedFileName` and `FolderLink`; Mark submitted
  records a typed file name.
- **The proposal builders already exist and are database-backed.** `Kor.Operations.App/FeeProposal/`
  (`FeeProposalBuilderWindow`, `FeeProposalBuilderViewModel`, block editors, staff management,
  contact and project pickers) persists through `SqlFeeProposalStore : SqlJsonStore<FeeProposal>`
  to `dbo.FeeProposals(Id, Name, ContentJson, ModifiedAt)` in the App's own database
  (`Kor.Operations.Data`), with `dbo.ProposalBlockTemplates` as the block library and
  `dbo.ProposalStaff`. `Kor.Operations.App/Brochures/` is the modular brochure builder on
  `dbo.BrochureProposals`. The CRM screen's "Build fee proposal" (`CrmView.xaml.cs`
  `BuildFeeProposalButton_Click`) opens the builder pre-filled (`StartFromOpportunity`) and, on
  the proposal's first save, links it to the opportunity through
  `IOpportunityFeeProposalLinkStore.LinkAsync` (`opportunities.OpportunityFeeProposalLinks`) and
  advances a still-Pending pursuit to Submitted (`LinkProposalToPursuitAsync`). Rendering
  (`GeneratePdf_Click` / `GenerateDocx_Click`) writes through `IFeeProposalRenderer.RenderAsync(
  proposal, staff, path)` to a path the user picks in a save dialog; **the rendered document is
  stored nowhere.** That is the gap this brief closes: the rendered proposal becomes the
  pursuit's attachment.

The server is SQL Server 2022 Developer Edition (no size cap); KorOpportunitiesDb is 6.3 GB
of data. Bytes in the database put every attachment under the nightly full backup and end
dead links when a folder is dragged into a neighbour, which has already happened on FS01.
FILESTREAM stays off: not worth an instance change at this volume.

## The decisions, so you build the one thing

- **Bytes go in `OpportunityFiles.Content VARBINARY(MAX)`.** `LocalPath` becomes nullable and
  is kept only for the legacy rows; new rows have `Content` and no `LocalPath`. No separate
  blob table, no FILESTREAM, no SharePoint.
- **Streams, never byte arrays, on both sides**: the store writes a `Stream` through a
  `VarBinary(-1)` parameter and reads with `CommandBehavior.SequentialAccess` and `GetStream`,
  so a 200 MB recording does not sit in memory. SHA-256 and size are computed while streaming in,
  and the row is refused if the stream ends short of the declared length.
- **One cap, stated**: 250 MB per file (a constant in the store, `MaxAttachmentBytes`); larger
  files are refused with a plain message naming the limit.
- **The submitted proposal is an attachment.** `CrmEngagements` gains `SubmittedFileId BIGINT
  NULL` referencing `OpportunityFiles(Id)` with `ON DELETE NO ACTION`. Mark submitted attaches
  the PDF (browse, required) and sets `SubmittedFileId`, `SubmittedFileName` (the attached
  row's name) and `Stage`. `FolderLink` stays optional.
- **Open streams to a temp file and shell-executes it**, under `%TEMP%\kor-pursuit-files\<id>\`,
  reusing the temp copy when its SHA-256 matches the row's. Legacy rows with a `LocalPath` and
  no `Content` open as today.
- `BD.PursuitFilesRoot` is no longer required for attaching; `IsConfigured` is true for the
  database storage. The setting stays read for legacy opens only.
- No model call anywhere (CLAUDE.md rule 14).

## Pieces

### Piece 1 — migration `Kor.Opportunities.Data/Schema/354_AttachmentsInTheDatabase.sql`

Guarded, its own batch each, no data statements, no grants:
- `ALTER TABLE opportunities.OpportunityFiles ADD Content VARBINARY(MAX) NULL;`
- `ALTER TABLE opportunities.OpportunityFiles ALTER COLUMN LocalPath NVARCHAR(4000) NULL;`
  (guarded by `COLUMNPROPERTY(..., 'AllowsNull') = 0`).
- `ALTER TABLE opportunities.CrmEngagements ADD SubmittedFileId BIGINT NULL;` and the guarded
  `FK_CrmEngagements_SubmittedFile` to `opportunities.OpportunityFiles(Id)` `ON DELETE NO ACTION`.

### Piece 2 — the store

`IPursuitFileStore` gains:
- `Task<PursuitFile> AddAsync(long engagementId, long? opportunityId, string fileName,
  Stream content, long declaredLength, string? contentType, string uploadedBy, CancellationToken ct)`:
  streams `content` into `Content`, computes SHA-256 and size on the way, refuses more than
  `MaxAttachmentBytes` or a short stream, writes one row in one transaction. The existing
  path-based `AddAsync` stays for legacy callers and is marked obsolete in its summary.
- `Task<Stream?> OpenContentAsync(long id, CancellationToken ct)`: `SequentialAccess` +
  `GetStream`; null when the row has no `Content`.
- `PursuitFile` gains `bool HasContent` (from `Content IS NOT NULL`, never the bytes) and
  `LocalPath` becomes `string?`. `ListByEngagementAsync` never selects `Content`.
- `RemoveAsync` unchanged (returns the legacy path or null).
- `CrmEngagement` gains `long? SubmittedFileId`; `SqlCrmEngagementStore` reads and writes it.

### Piece 3 — the App storage and the CRM screen

- `DbPursuitFileStorage : IPursuitFileStorage` in `Kor.Operations.App/Crm/`: `IsConfigured` true;
  `StoreAsync` opens the source file as a stream and calls the new `AddAsync`, returning the
  `PursuitFileUpload` shape the CRM already consumes (StoredPath empty for database rows);
  `TryDelete` is a no-op for database rows. Registered in place of `LanPursuitFileStorage`;
  the LAN class stays only to open legacy paths.
- `IPursuitFileOpener` (new, `Kor.Operations.App/Crm/`): `OpenAsync(PursuitFile)` → for a row
  with content, stream to `%TEMP%\kor-pursuit-files\<id>\<FileName>` unless a copy with the same
  SHA-256 is already there, then shell-execute; for a legacy row, shell-execute `LocalPath`.
  `CrmView.xaml.cs` line 769's Open uses it.
- The CRM attach button and drag-and-drop change nothing visible; the "Pursuit files aren't set
  up" message goes away.

### Piece 4 — Mark submitted attaches the proposal

- `SubmittedDialog` gains a Browse button and a read-only path box above the file-name box; the
  file name defaults to the chosen file's leaf name and stays editable; Browse is required, the
  folder link stays optional.
- `BdDirectorViewModel` on OK: `AddAsync` (stream) for the chosen file against the bid's
  engagement, then `UpdateAsync(engagement with { Stage = Submitted, SubmittedFileId,
  SubmittedFileName, FolderLink })`, then reload. If the attach succeeds and the update fails, the
  attachment row stays and the message says so; nothing is deleted.
- The Pending bids File column becomes a link that opens the attachment through
  `IPursuitFileOpener` when `SubmittedFileId` is set, and plain text otherwise.

### Piece 5 — gates

**App, extend `BdDirectorPendingBidsRegressionTests` and add `PursuitFileStorageTests`:**
(1) `DbPursuitFileStorage.StoreAsync` against an in-memory `IPursuitFileStore` fake round-trips
a 3 MB random stream byte for byte and reports the same SHA-256 and size the fake computed; (2) a
stream over `MaxAttachmentBytes` is refused before any store call, with the limit in the message;
(3) `SubmittedDialog` OK without a chosen file is refused; with one, the view model calls
`AddAsync` once then `UpdateAsync` once with Stage Submitted, `SubmittedFileId` from the returned
row and `SubmittedFileName` equal to its name; an `UpdateAsync` failure leaves the attachment and
surfaces a message; (4) the opener writes to the temp folder once and reuses it when the hash
matches (fake file system); (5) the date ratchet still zero; (6) no Director or CRM file
contains "aren't set up" or `PursuitFilesRoot` outside the legacy-open path.

**Data, extend `BdDirectorRegressionTests` (the SQL source gates):** (7) migration 354 is the
three guarded statements and one guarded foreign key; (8) the streaming `AddAsync` uses a
`VarBinary(-1)` parameter and one INSERT, `OpenContentAsync` uses `SequentialAccess` and
`GetStream`, and `ListByEngagementAsync` does not select `Content`; (9) the engagement store's
write boundary extends by exactly `SubmittedFileId`.

### Piece 6 — the fee proposal builder renders into the pursuit

- **Build fee proposal… on a Pending bid.** The Director's Pending bids tab gains the button;
  it uses the same launch the CRM uses. Move `BuildFeeProposalButton_Click`'s body and
  `LinkProposalToPursuitAsync` into one shared `FeeProposalLauncher` (`Kor.Operations.App/FeeProposal/`)
  taking `(opportunityId, engagementId, opportunityKey, projectName, actor)`, called from both
  screens; the CRM's behaviour does not change (pre-fill, one-shot link on first save, Submitted,
  the self-logged activity).
- **Render attaches.** `FeeProposalBuilderViewModel` raises `ProposalRendered(proposalId,
  renderedPath, contentType)` after a successful `GeneratePdf_Click` or `GenerateDocx_Click`.
  The launcher subscribes for the builder's lifetime: when the proposal is linked to the pursuit
  (`OpportunityFeeProposalLinks` row exists, or the one-shot link just ran), it streams the
  rendered file into `OpportunityFiles` for that engagement through the new `AddAsync`, then
  updates the engagement with `SubmittedFileId` and `SubmittedFileName` set to that row. Every
  render becomes an attachment; the latest render is the submitted file. A pursuit with no
  opportunity (relationship work) renders as today and attaches nothing.
- **Mark submitted keeps Browse** for documents produced outside the builder (Leduc was written
  by hand in Word). When the bid already has a `SubmittedFileId`, the dialog shows that file's
  name and offers to replace it.
- The brochure builder is untouched in this brief; the same `ProposalRendered` pattern is for
  a later step.

**Gates for piece 6 (add to `BdDirectorPendingBidsRegressionTests` and the CRM clarity tests):**
(10) the Pending bids button and the CRM button call the one launcher with the row's ids; (11)
with a fake builder view model, `ProposalRendered` on a linked pursuit calls `AddAsync` once with
the rendered file's stream and name, then `UpdateAsync` once with `SubmittedFileId` from the
returned row; on an unlinked pursuit it calls neither; (12) a second render replaces
`SubmittedFileId` and leaves the first attachment row in place; (13) no "Drafting" literal
returns, and no Director or CRM file writes a path under `PursuitFilesRoot`.

## Claude's run after apply (not yours)

Build App tests and CLI; run the gate classes; apply 354 (Ian in SSMS); install the App
locally. Then two real proofs, in the app: (a) Leduc: Mark submitted with the actual proposal
PDF; read the row back; open it from the Pending bids tab; compare the SHA-256 of the opened
temp file with the original PDF's; (b) a fee proposal built from a Pending bid through the
builder, rendered to PDF, confirming the attachment row and `SubmittedFileId` landed without a
save dialog path being stored anywhere. Record sizes before and after in the memory file.

## Read bound

About 120 KB:
- `docs/codex/CODEX-BD-DIRECTOR-9G-…` brief and response
- `Kor.Opportunities.Data/Schema/275_PursuitFiles.sql`, `282_OpportunityDocuments.sql`, `353_PursuitSubmission.sql`
- `Kor.Opportunities.Data/Crm/SqlPursuitFileStore.cs`, `SqlCrmEngagementStore.cs`, `ICrmEngagementStore.cs`
- `Kor.Opportunities.Core/Models/CrmEngagement.cs`
- `Kor.Operations.App/Crm/PursuitFileStorage.cs`, `CrmView.xaml.cs` lines 690–780, `CrmViewModel.cs` lines 850–900
- `Kor.Operations.App/Options/AppOptions.cs`, `Services/AppConfigKeys.cs`, `CompositionModules/CompositionHelpers.cs` lines 55–75, `OpportunitiesModule.cs` (the storage registration)
- `Kor.Operations.App/BusinessDevelopment/Director/SubmittedDialog.xaml` + `.xaml.cs`, `BdDirectorViewModel.cs` lines 360–410, `BdDirectorWindow.xaml` (the Pending bids grid)
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdDirectorPendingBidsRegressionTests.cs`, `BdDirectorRegressionTests.cs`, `BdCrmClarityRegressionTests.cs` (summary)
- `Kor.Operations.App/FeeProposal/FeeProposalBuilderWindow.xaml.cs` lines 180–260, `FeeProposalBuilderViewModel.cs` (the `ProposalSaved` event and `StartFromOpportunity`), `Kor.Operations.App/Crm/CrmView.xaml.cs` lines 262–370, `Kor.Opportunities.Data/Opportunities/SqlOpportunityFeeProposalLinkStore.cs`, `Kor.Operations.Data/SqlFeeProposalStore.cs` (header), the `IFeeProposalRenderer` interface

Do not open the live database, the Books folder or the proposal content tables. App projects with
`UseWPF` have no implicit `System.IO`.

## Files

Creates: `Kor.Opportunities.Data/Schema/354_AttachmentsInTheDatabase.sql`;
`Kor.Operations.App/Crm/DbPursuitFileStorage.cs`, `PursuitFileOpener.cs`;
`Kor.Operations.App/FeeProposal/FeeProposalLauncher.cs`;
`Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/PursuitFileStorageTests.cs`; the RESPONSE.

Edits: `SqlPursuitFileStore.cs`, `SqlCrmEngagementStore.cs`, `ICrmEngagementStore.cs` if a signature moves,
`CrmEngagement.cs`; `PursuitFileStorage.cs` (nullable path, legacy note), `CrmView.xaml.cs`, `CrmViewModel.cs`,
`OpportunitiesModule.cs`; `FeeProposalBuilderWindow.xaml.cs`, `FeeProposalBuilderViewModel.cs`;
`SubmittedDialog.xaml` + `.xaml.cs`, `BdDirectorViewModel.cs`, `BdDirectorWindow.xaml`;
`BdDirectorPendingBidsRegressionTests.cs`, `BdDirectorRegressionTests.cs`, `BdCrmClarityRegressionTests.cs`.

## Response

The 9a–9g format. State plainly that nothing was built, run, applied or deployed, that no new
table was created, and that no model call exists in the change. `git diff --check` clean. Do not
commit.
