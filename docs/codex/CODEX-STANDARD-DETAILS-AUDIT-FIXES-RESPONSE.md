# Standard Details audit fixes — handoff

2026-09-07. One pass through the six-item brief. Items 1, 2, 5, and 6 are implemented; items 3 and 4 have the scoped stops below. All edits and new files are inside `Kor.Operations.App`. No UNC paths, databases, other repositories, or remote machines were accessed for this work.

**Verification status:** no builds or tests were run, as instructed. `git diff --check -- .` passed from the App directory. The new tests have not been compiled or demonstrated failing on pre-fix code; the assertions described below are intended regression checks, not verified gates. Claude owns Debug compilation, test execution, and the live checks for items 1 and 3.

## 1. PDF capture refusal — implemented

- `MasterPublisher.cs`: `MasterPublishPdfCaptureResult.EnsurePublishAllowed` refuses incomplete capture by default; `MasterPublisher.PublishAsync` invokes it before releasing/replacing the temp master, preserving the existing failure cleanup. `allowPdfCaptureFailures` defaults to false. `MasterPublishResult.PdfCaptureComplete` reports the capture state.
- `StandardDetailsWindow.Logic.cs`: `PublishToMasterAsync` catches the capture-specific exception and offers a second confirmation, defaulting to No. Yes explicitly retries the publish with failures allowed. This is a fresh publish attempt after the refused attempt has cleaned up, so it repeats capture. `BuildMasterPublishSummary` includes every failure and uses a warning for incomplete capture.
- Regression assertions: `MasterPublisherCapturePolicyTests.Capture_failure_refuses_publish_and_names_the_detail_and_reason`; `Override_keeps_result_incomplete_and_summary_includes_failures_beyond_twenty`. A success case also preserves the normal path.
- Limits: tests do not drive the bridge, database writes, cleanup ordering, or the live confirmation. Successful PDF writes from a refused attempt are not rolled back; the existing capture sequence was retained as required.

## 2. Previous MASTER archive — implemented

- `MasterPublisher.ReplaceMaster` supplies `_archive/<master stem>.<yyyyMMdd-HHmmss>.rvt` to `File.Replace`, returns the backup path, and calls `PruneMasterBackups` after the swap. Retention keeps the newest five matching backup names for this master; other archive files are untouched. Cleanup is best-effort. First publish still uses `File.Move` without an archive.
- A same-second backup-name collision refuses replacement rather than overwriting an existing backup. The UI summary reports `MasterPublishResult.BackupPath`.
- Regression assertion: `MasterPublisherArchiveTests.Replacement_archives_previous_bytes_and_keeps_newest_five_for_this_master`. A second test covers first publish without an archive.
- Limits: local temporary-file tests only; no SMB, Revit, crash, or locked-backup test was executed.

## 3. Member composition — access implemented; numbering STOPPED

- `StandardDetailsWindow.xaml.cs.UpdateActionStates` and `StandardDetailsWindow.Logic.cs.ComposeSheet_Click` allow opening the composer with the reader configured. Missing governance/bridge configuration no longer prevents personal PDF composition.
- `SheetComposerWindow` receives `canPublish`. Its constructor, `ToggleBusy`, `Save_Click`, and `OpenPdf_Click` keep governed outputs restricted to publishers, including after an async busy cycle. The XAML starts governed buttons disabled. Personal PDF creation remains available when idle and accepts an empty sheet number. Save explains missing governance/AUTHORING configuration.
- Regression assertions: `SheetComposerAccessTests.Personal_pdf_is_available_to_members_but_governed_outputs_require_publish_permission`. `Personal_pdf_accepts_an_empty_sheet_number` preserves already-supported personal-PDF behavior; it is not a new-defect gate.
- **STOP — automatic numbering:** `LoadOccupiedDetailsAsync` returns occupied-detail records, not a complete composed-sheet inventory; safely choosing the next unused number requires changing the composer API beyond item 3's named window files. No guessed series or incomplete-inventory numbering was added.
- Limits: tests do not instantiate WPF, resolve real roles, or prove Jim's live workflow. Number entry remains manual.

## 4. Promoter actor and basis — existing setters implemented; delete STOPPED

- `KorStandardsPromoterRepository`: `SetDetailKindAsync`, `SetDetailIsSheetAsync`, `SetDetailTypeAsync`, `SetRenderedImageAsync`, `SetRenderedPdfAsync`, and both private `ExecuteSetDetail*Async` helpers now require `changedBy` and `basis`. `AddGovernanceParameters` uses the same `AddNVarChar` behavior as `PromoteAsync`: `@ChangedBy` size 150 and `@Basis` size 1000.
- `StandardDetailsWindow.Logic.cs` passes `_userIdentity` for the Type dropdown, part sync, and publish request. Bases are `Type set in Operations Standard Details`, `Sync Part Images`, and `Publish to Master <UTC date>` respectively. `MasterPublisher` passes those publish values to every PDF write.
- Added `PromoterGovernanceParameterTests.Actor_and_basis_are_sent_as_sized_unicode_parameters` for the SQL parameter contract. It does not prove every caller uses the helper or that a database journal row is written; this is a limited parameter check, not a live governance gate.
- **STOP — delete:** there is no `DeleteRenderedImageAsync` method or caller in the App source inspected. No deletion method or UI was invented, and SQL was not opened to infer its missing contract.
- Migration 079 must precede deployment, per the supplied ground truth. Neither its contents nor execution was investigated.

## 5. Timeout withdrawal — implemented

- Only `DrafterBridgeClient.SendAsync` changed. On timeout it calls the existing `TryDelete` for its own inbox request. The exception distinguishes a request still queued after failed deletion, a request already picked up (including a retained `done` file), and a withdrawn request.
- Regression assertions: `DrafterBridgeTimeoutTests.Timeout_withdraws_only_its_own_command` and `Timeout_reports_a_request_already_picked_up`; both use local temporary folders, not a bridge.
- Limits: timeout cannot cancel work already picked up by Revit. Tests do not cover SMB permissions or every pickup/deletion race.

## 6. Governed preview only — implemented

- `StandardDetailsWindow.xaml.cs.LoadPreviewAsync` uses `LoadRenderedImageAsync` and the existing placeholder when no stored image is available. Removed `SetDetailPreview`, `ResolvePreviewCacheDir`, and the hard-coded flat-PNG fallback.
- Removed the preview-cache member from `StandardDetailsMasterPublishOptions`, `StorageOptions` in `Options/AppOptions.cs`, `Services/AppConfigKeys.cs`, and `CompositionModules/CompositionHelpers.cs`.
- `App.config` has exactly one removed line: `StandardDetails.PreviewCachePath`. `PartImageRoot` was left alone.
- Static call-site review found no remaining references to the removed preview-cache members in the edited app areas. No new runtime test was added for this source-removal change; Claude should verify the missing-image placeholder in the UI. This is not a claim of a pre-fix failing test.

Five new test classes are under `EngineeringTools.Tests/StandardDetails/`. The previously supplied `MasterPublisherPdfMatchingTests` and matcher fix were left intact. No commits or deployments were made.
