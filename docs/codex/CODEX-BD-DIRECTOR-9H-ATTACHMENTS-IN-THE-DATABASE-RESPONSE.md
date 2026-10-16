# BD Director 9h — attachments in the database

Implemented as source edits on 2026-10-15. **Nothing was built, run, applied to a
database or deployed.** No application, CLI, test, gate, render, service action or
commit was performed. The live database, Books folder and proposal content tables
were not opened. Source inspection and whitespace checks only; `git diff --check`
is clean, including separate checks of the new files. No pre-fix failure,
post-fix pass, SQL execution or visual proof is claimed.

**No new table was created. No model call exists in this change.** The existing
OpportunityFiles row holds the bytes. OpportunityDocuments and the brochure
builder are untouched.

Prior art searched/read: the supplied 9h brief, 9g brief/response, existing schema
and attachment/engagement stores, CRM attach/open/launch paths, Director pending
bid contracts and screens, proposal persistence/render events, storage registration,
and the existing regression fixtures. These already provide the attachment index,
pursuit lifecycle, proposal builder and first-save linking; the change extends them.
Unrelated NetworkOps edits, PDFs, supplied briefs and vocabulary artifact were not
edited.

## Applied changes

- **Piece 1 — migration 354:** Four separate guarded batches: Content addition,
  nullable LocalPath, SubmittedFileId addition, and the named foreign key with
  ON DELETE NO ACTION. No data statements, grants or table creation.
- **Piece 2 — storage:** The new stream overload uses a VarBinary(-1) parameter
  and one INSERT. A counting/hash stream enforces the declared length and 250 MB
  cap while SqlClient reads. A metadata UPDATE records the resulting SHA-256 and
  size in the same transaction before commit. Short, long, cancelled or failed
  uploads cannot commit a partial row. OpenContentAsync uses SequentialAccess and
  GetStream; the returned stream owns the reader, command and connection and
  releases them on disposal. Lists return metadata and HasContent, never the
  content bytes. LocalPath is nullable; the path overload remains with an obsolete
  summary, and RemoveAsync retains its signature and behavior.
- **Engagement fields:** SubmittedFileId is appended at reader ordinal 32;
  deleted.Stage moves to 33. INSERT, UPDATE, parameters and row images include it.
  Existing concurrency, history, opportunity-status synchronization, provenance
  and close-out behavior remain in the existing store.
- **Piece 3 — App/CRM:** DbPursuitFileStorage is registered and always configured.
  It passes the actor and opportunity id, opens a source stream, rejects oversized
  files before calling the store, and returns the committed row in PursuitFileUpload
  with an empty StoredPath. CRM consumes that row without inserting it again.
  Attach and drag/drop use the same controls. Share-specific status and tooltip
  wording is removed. Database deletion needs no filesystem action; legacy deletion
  retains best-effort cleanup.
- **Opening:** IPursuitFileOpener downloads to the specified temp/id folder with a
  sanitized leaf name. It reuses a matching SHA-256 copy, otherwise writes a unique
  partial file, verifies its hash and moves it into place before shell opening.
  Failed downloads clean their partial files. Opens are serialized by the singleton
  opener. Legacy rows use their existing path; the legacy root is resolved lazily
  only when opening a relative legacy path. The LAN class no longer stores uploads.
- **Piece 4 — submission:** The dialog has Browse, a read-only source path, editable
  PDF leaf name and optional folder link. A new submission requires a selected PDF.
  The view model reads the current engagement, attaches once, then updates Stage,
  SubmittedFileId, the returned row's FileName and FolderLink, preserving its
  concurrency token. An update failure retains the attachment and surfaces that
  fact in status and the dialog error. Pending bids display a file hyperlink only
  when an attachment id exists; legacy names remain plain text.
- **Piece 6 — proposal builder:** Both screens call FeeProposalLauncher with their
  row ids, name/key and actor. It retains prefill, first-save-only linking, the
  Deliverable activity and advancement of a still-Pending pursuit. Both PDF and
  DOCX success paths await ProposalRendered. The launch session waits for first-save
  linkage, then checks the proposal link when needed, streams the output into an
  attachment and updates a freshly read engagement. Renders are serialized within
  the session; each creates a new row, and the latest becomes SubmittedFileId.
  Earlier attachments remain. Relationship pursuits and unlinked proposals attach
  nothing. Closing the builder unsubscribes its events.

## Source-driven integration adjustments

- IPursuitFileStorage now receives opportunityId and uploadedBy and its result
  carries the saved PursuitFile. The former signature lacked the context needed
  to save the row correctly, and retaining CRM's second AddAsync would duplicate it.
- PendingBidRow and SqlBdDirectorStore now project SubmittedFileId. The Director
  row exposes link/action visibility; without this read-side addition the requested
  hyperlink could not work.
- IOpportunityFeeProposalLinkStore gains a SELECT-only IsLinkedAsync, allowing a
  render of an already-linked proposal while refusing a different unlinked proposal
  opened in the same builder.
- The Director receives the attachment store as a required dependency. Existing
  Director/render fixtures supply it; the other fixture changes are constructor
  wiring only.
- An existing SubmittedFileId may be kept or replaced in the submission dialog.
  This also permits replacement on an already-Submitted bid carrying an attachment.
  A legacy Submitted bid without an attachment still follows the previous refusal
  rule. Keeping/replacing a file on an already-Submitted row does not add another
  stage-history transition.

## Gates authored, not executed

- **PursuitFileStorageTests:** A 3 MB random file round-trips through StoreAsync and
  an in-memory store; bytes, SHA-256, size, ids, actor and empty database path are
  checked. Oversize streams are refused before the fake store or SQL connection.
  A fake filesystem checks one-write cache reuse, altered-copy replacement, corrupt
  and interrupted-download cleanup, legacy opens and leaf-name containment.
- **BdDirectorPendingBidsRegressionTests:** Submission validation, returned-row
  identity/name, add-before-update ordering, fresh concurrency token, cancellation,
  moved-bid refusal, retention/error on update failure, and keep/replace behavior.
  The existing themed modal helper now checks OK-without-Browse refusal before
  exercising selection and OK/Cancel. Shared-launcher wiring and File links are
  checked. Fake builder events cover existing links, first-save completion races,
  unlinked/relationship negatives, repeated renders, repurposed builders,
  unsubscription and retention on update failure. The existing zero-date ratchet
  remains in place.
- **BdDirectorRegressionTests:** Exact four migration batches, streaming parameter,
  transaction and single-INSERT boundary, hash/length guards, reader ownership,
  SequentialAccess/GetStream, metadata-only listing, bid id projection and exact
  engagement write columns/ordinals. The prior write boundary extends by exactly
  SubmittedFileId.
- **BdCrmClarityRegressionTests:** Database registration, no share-setup wording or
  root usage in CRM/Director, no duplicate CRM insert, shared launcher/opener calls,
  both render notifications and event cleanup. Existing no-Drafting and date gates
  are retained.

## Limits and handoff

The fakes and SQL source gates do not prove SqlClient streaming behavior, SQL
permissions, transaction rollback, FK behavior, actual heap usage, shell execution
or visual layout. Those checks remain required after the authorized build/apply
step. The modal helper runs through the existing themed render harness, not the
ordinary filtered gate run. No gate is claimed verified against pre-fix code.

Six new files: migration 354, DbPursuitFileStorage, PursuitFileOpener,
FeeProposalLauncher, PursuitFileStorageTests and this response. Other edits cover
the described Core/Data/App paths and the affected test fixtures. No commit made.

Claude owns the affected-project Debug builds, CLI build, filtered gate execution,
pre-fix negative controls, rendering and local App installation. Ian applies 354
in SSMS. Then perform both real proofs from the brief: Leduc's actual proposal PDF
through Mark submitted and the Pending bids File link, comparing original and temp
SHA-256; and a Pending-bid builder render producing its attachment and submitted
file reference without persisting the save-dialog path. Record database sizes
before and after in the memory file.
