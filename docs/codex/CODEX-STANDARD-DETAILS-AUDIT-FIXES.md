# CODEX-STANDARD-DETAILS-AUDIT-FIXES — the app-side fixes from the 2026-09-07 state audit

```
IMPORTANT: Do NOT run `dotnet build` or `dotnet test`. Claude verifies on the dev box after you ping.
IMPORTANT: Stay inside C:\VIsual Studio Projects\Operations\Kor.Operations.App. Do not open any UNC path,
  any database, any other repo, or any machine. Every fact you need about them is written below. If an
  item seems to need something outside the files named, STOP that item, write one line saying what, and
  move on. Do not investigate.
IMPORTANT: No git commands beyond `git diff`. No file deletes outside the edits below.
Budget: six items, one pass each. When all six are done or stopped, write your notes and ping.
```

## Why

Jim DesRoches reviewed the Standard Details app on Sep 3. His bar: find it in 5 seconds, get it as a PDF
in Bluebeam, know it is the latest and greatest. The Sep 3-4 build burst delivered that. A state audit on
Sep 7 then found the defects below. The ship-blocker (a PDF stored under the wrong detail numbers) is
already fixed in `MasterPublisher.TryResolveExportedViewPdf`, with tests in
`EngineeringTools.Tests\StandardDetails\MasterPublisherPdfMatchingTests.cs`. Read that fix and its test
first: it is the pattern for the rest (small change, a test that fails on the defect, a summary that says
what the test does not cover).

## Ground truth (do not re-derive)

- Publish to Master: `MasterPublisher.PublishAsync` derives the master from AUTHORING through the Drafter
  bridge, verifies it, captures per-view vector PDFs into `detail.RenderedImage.Pdf` (via
  `KorStandardsPromoterRepository.SetRenderedPdfAsync`), then swaps the live master with
  `File.Replace(temp, master, destinationBackupFileName: null)`. The master and its temp are on the same
  volume (the temp is written beside it). `MasterPublishResult` carries `PdfCapture`
  (`MasterPublishPdfCaptureResult`: counts + a failure list) and `Verified: true`.
- The bridge's `exportviews` reply: successes in `views[]` (`elementId`, `key`, `pdf`, `exists`),
  failures in `failures[]` (`elementId`, `key`, `reason`). Batches are 125 views.
- Static PDFs are what engineers are served by "Open PDF". A stale PDF in the store is a silent wrong.
- `StandardDetailsAccessPolicy`: `CanApproveOrReject()`, `CanPublish()`, contributor/member checks. App.config
  groups today: Members = jmarkulin, ilalonde, jdesroches; Approvers and Publishers = ilalonde only.
  Jim (jdesroches) is a Member and nothing else. That is intended: he reads and composes, he does not govern.
- `SheetComposerWindow` has two outputs: **Save to master** (`Save_Click` → `SheetComposer.ComposeAsync`,
  writes a governed ViewSheet into AUTHORING through the bridge, needs a sheet number) and **Create PDF
  sheet** (`CreatePdfSheet_Click` → `CustomPdfSheetComposer.Build`, Revit-free, stamped UNCONTROLLED
  WORKING COPY, needs no sheet number). `StandardDetailsWindow.xaml.cs` enables `ComposeSheetButton` only
  when `_policy.CanPublish()` is true, so a Member cannot open the window at all.
- The KorStandards procs `detail.SetDetailKind`, `detail.SetDetailIsSheet`, `detail.SetRenderedImage`,
  `detail.SetRenderedPdf`, `detail.DeleteRenderedImage` gain two OPTIONAL parameters in migration 079
  (`@ChangedBy nvarchar(150) = NULL`, `@Basis nvarchar(1000) = NULL`) and journal to a new
  `detail.GovernanceLog`. Existing parameters are unchanged. 079 is staged in KOR.Drafter and will be run
  BEFORE this app build is deployed. You do not touch SQL; you pass the two parameters.
- `DrafterBridgeClient.SendAsync` writes `<id>.json` into the bridge inbox and polls the outbox until
  `timeout`, then throws `TimeoutException`. It never removes its own request. The bridge runs only while
  Revit is open, so an unanswered request sits in the inbox and executes when Revit next opens: three stale
  `ping` files were found there on Sep 7. `TryDelete(path)` already exists in the class.
- `StandardDetails.PreviewCachePath` (App.config) points at a folder of flat PNGs that predates the DB art
  store. `StandardDetailsWindow.xaml.cs` still resolves it (`ResolvePreviewCacheDir`, with a hard-coded UNC
  fallback) as a source for the detail preview. `KorStandardsReadRepository.LoadRenderedImageAsync` is the
  governed source and every placeable detail has a PNG there (0 of 603 missing on Sep 7).

## The six items

### 1. Capture failures must not silently ship a stale PDF store  (`MasterPublisher.cs`, its window wiring)
Today `CapturePublishedPdfsAsync` collects failures, and `PublishAsync` still calls `ReplaceMaster` and
returns `Verified: true`. Decision: **a capture failure refuses the publish.** The master is left
untouched, the temp is cleaned up as on any other failure, and the exception message lists the failed
detail numbers with the bridge's reasons (the matcher already surfaces them). Add a `bool
allowPdfCaptureFailures` on the publish request (default false) so the gatekeeper can knowingly publish
anyway; when they do, the result must say so (`PdfCaptureComplete = false` plus the failure list) and the
UI summary must show the failed details, not just counts. Wire the "publish anyway" as a second confirm in
`StandardDetailsWindow.Logic.cs` (the existing publish handler), never as the default. Test the policy at
the seam you can reach without a bridge: a `MasterPublishPdfCaptureResult` with failures → refused unless
allowed; with the flag → result marked incomplete. Do not touch the bridge, the temp/verify sequence, or
`ReplaceMaster` beyond what item 2 says.

### 2. Publish keeps the previous master  (`MasterPublisher.ReplaceMaster`)
`File.Replace` with `destinationBackupFileName: null` destroys the only copy of the last good master. Pass a
backup path instead: `<master folder>\_archive\<master name>.<yyyyMMdd-HHmmss>.rvt` (create `_archive`,
same volume, so `File.Replace` stays atomic). Keep the newest 5 in `_archive`, delete older ones
best-effort after a successful swap. The `File.Move` branch (no existing master) needs no backup. Report
the backup path in `MasterPublishResult`. Test the retention rule with a temp folder (pure file logic; no
bridge).

### 3. Jim can compose; only a publisher can save into AUTHORING  (`StandardDetailsWindow.xaml.cs`, `SheetComposerWindow.xaml(.cs)`)
Enable `ComposeSheetButton` for any Member with a configured KorStandards reader (drop the `CanPublish`
term there). Pass `canPublish` into `SheetComposerWindow`; inside, **Save to master** and the post-compose
Open PDF are enabled only when it is true, **Create PDF sheet** is always enabled. A sheet number is
required only for Save to master; Create PDF sheet must not demand one (check every place
`SheetNumberBox.Text` is read). When the AUTHORING model IS reachable (the existing occupancy load
succeeds and returns the sheet list), pre-fill `SheetNumberBox` with the next free number in the series
the composed sheets use today (derive the series from the highest existing composed sheet number; if none,
use `S6.01`), editable. When it is not reachable, leave the box empty and let Save explain. Keep the
occupancy load best-effort as it is now.

### 4. Every promoter write names the person  (`KorStandardsPromoterRepository.cs` and its callers)
Add `changedBy` and `basis` parameters to `SetDetailKindAsync`, `SetDetailIsSheetAsync`,
`SetDetailTypeAsync`, `SetRenderedImageAsync`, `SetRenderedPdfAsync`, `DeleteRenderedImageAsync` (and the
private `ExecuteSetDetail*Async` helpers), sent as `@ChangedBy` / `@Basis`. Callers pass `_userIdentity`
(the same value `PromoteAsync` already receives) and a short basis: the Type dropdown → "Type set in
Operations Standard Details"; publish capture → "Publish to Master <date>"; part image sync → "Sync Part
Images"; delete → the reason the UI already has. Follow exactly how `PromoteAsync` passes its actor.

### 5. An unanswered bridge command is withdrawn  (`DrafterBridgeClient.SendAsync`)
On timeout, before throwing, `TryDelete` our own `<id>.json` from the inbox if it is still there (if the
bridge has already moved it to `inbox\done`, there is nothing to delete and that is fine). Say in the
exception whether the request was withdrawn or had already been picked up. Nothing else in the class changes.

### 6. One governed source for the preview  (`StandardDetailsWindow.xaml.cs`, `AppOptions`, `AppConfigKeys`, `CompositionHelpers`, `App.config`)
The preview must come from `LoadRenderedImageAsync` only. Remove the flat-PNG path: `ResolvePreviewCacheDir`,
its hard-coded UNC fallback, the `PreviewCachePath` option, its config key, and the `MasterPublishOptions`
parameter that carries it. If a detail has no stored image, show the existing placeholder. Leave
`PartImageRoot` alone (parts thumbnails are a separate, later cleanup).

## Rules for this work
- Smallest change that holds. Do not refactor around the edits. Do not rename things that work.
- Warnings are errors repo-wide. xUnit analyzers are on: `Assert.Empty` not `Assert.Equal("", x)`.
- Every new test class carries a summary saying what it covers, what it does not, and one same-class
  fault it would not catch (see `MasterPublisherPdfMatchingTests` for the shape).
- Tests go in `Kor.Operations.App\EngineeringTools.Tests\StandardDetails\`. That project has
  `InternalsVisibleTo`; make a method `internal` if a test needs it, nothing wider.
- App.config: change only the one key item 6 removes.

## Output
A short note per item: what changed (file and member names, not line numbers), the test that fails on the
old behaviour, and anything you stopped on. Then ping. Claude builds, runs
`dotnet test Kor.Operations.App\EngineeringTools.Tests`, and proves item 1 and 3 live before commit.
