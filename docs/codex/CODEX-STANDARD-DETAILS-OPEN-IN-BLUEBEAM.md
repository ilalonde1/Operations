# CODEX — Standard Details: open a composed sheet as a PDF in the default viewer (Bluebeam)

## Goal

When a user composes (or picks) a detail sheet in the Standard Details app, they must be able to
get it as a **PDF open in their default PDF viewer (Bluebeam) in one action** — no Save-As
dialog, no manual file shuffling. The user saves it from Bluebeam afterwards if they want to.

This is the engineer's headline workflow (per the 2026-09-03 review with the practice lead):
"find it → one button → it's open in Bluebeam." Today the composer builds the sheet inside Revit
and shows a message box; there is **no PDF output at all**. This brief adds that output.

## Why / ground truth (do not relitigate)

- `SheetComposerWindow.Save_Click` → `SheetComposer.ComposeAsync(...)` builds the sheet inside the
  authoring Revit document via the bridge (`newsheet` / `placeview` / `savedoc`) and returns a
  `SheetComposerResult` carrying `SheetNumber`/`SheetName`. It then only shows a `MessageBox`.
- The bridge already has a **vector** PDF export: verb **`exportsheets`** (BridgeExec.cs
  `ExportSheets`, Revit 2022+, `PDFExportOptions` + `doc.Export`). It resolves sheets by exact
  number then unique contains-match, writes a PDF to a caller-given `folder`, and returns
  `{ pdf, exists, sheets }`. This is the correct, non-raster path.
- The app already talks to the bridge through the composer's bridge client
  (`_bridge.SendAsync(new { verb = "savedoc" }, timeout)` etc.), and gates on
  `AssertAuthoringActiveAsync(...)`.

## What to build

An app-side action — call it **"Open PDF"** (label it for Bluebeam if you like) — that takes a
sheet number present in the document the bridge currently has active and:

1. Calls the bridge **`exportsheets`** verb for that single sheet number, targeting a
   **share-reachable output folder** the app can read back (e.g. under the existing bridge/share
   root the app already uses for Standard Details — do not invent a new local-only path on the
   Revit host that the app cannot see).
2. On success, **copies the returned PDF to a local temp file** —
   `%TEMP%\KOR-StandardDetails\<sheetnumber>-<yyyyMMdd-HHmmss>.pdf` (sanitise the sheet number for
   a filename) — and **opens it with the OS default PDF viewer**:
   `Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true })`. On these machines
   the default PDF handler is Bluebeam, so it lands there. **No `SaveFileDialog` anywhere in this
   path.**
3. Best-effort clean-up of prior temp files in that folder (older than, say, a day); never let
   clean-up throw into the UI.

### Wiring
- After `ComposeAsync` succeeds in `Save_Click`, replace the bare success message box with:
  compose → then **Open PDF** for the just-composed `SheetNumber` (a brief non-blocking
  confirmation is fine, but the PDF opening is the point).
- Also expose **Open PDF** as a standalone action the user can invoke on a sheet they have
  selected/open, so it serves pre-built sheets too (the Sheets tab is a later brief; this action
  must not depend on it — it only needs a sheet number).

### Failure handling
- If the bridge/Revit is unavailable, or `exportsheets` returns `exists=false`, or the returned
  PDF cannot be read back over the share: show a clear, non-crashing message (reuse the
  composer's existing error-message-box pattern and the `AssertAuthoringActiveAsync` guard).

## Constraints
- Additive. Do not modify the bridge, `exportsheets`, `ComposeAsync`'s Revit steps, or unrelated
  app code.
- No `SaveFileDialog` in this flow. Opening is via the default handler (`UseShellExecute = true`).
- Reuse existing plumbing: the composer's bridge client / `SendAsync`, `DrafterBridgeClient`,
  the Standard Details options/paths already in DI (`CompositionHelpers` / `StorageOptions`),
  and the existing timeout + `AssertAuthoringActiveAsync` conventions.
- Follow the repo build gate (warnings are errors); no new warnings.
- No build or test steps in your change; leave verification to the requester.

## Verification (done by the requester, not Codex)
Compose a 1–2 detail sheet in the app, confirm a vector PDF opens in Bluebeam with no Save-As
prompt, confirm the temp file exists and the details render correctly (not garbled, not a blank
sheet), and confirm a bridge-down case shows a clean message instead of crashing.
