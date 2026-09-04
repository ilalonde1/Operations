# CODEX — Detail art export: PDF-export drafting views by ElementId

## Goal

Give the KOR.Drafter bridge a reliable, **vector**, one-file-per-view export of arbitrary
drafting views (and legends) from a Revit document, addressed by **ElementId**, so the
Standard Details app can capture correct art for **all 609 catalogued details** — not the 240
partial set we have today — with no garbling.

The output feeds an existing, working cropper on the app side; this brief is only the bridge
verb that produces one clean vector PDF per view.

## Why (ground truth — do not relitigate)

- `detail.DetailOccurrence` (KorStandards) records, for every catalogued detail view:
  `ViewElementId` (BIGINT), `DocumentName`, `ViewName`, `ViewKind` (DraftingView/Legend),
  `OnSheet`. This is the authoritative index of where each detail's art lives.
- The bridge already has the correct mechanism: `ExportSheets` (BridgeExec.cs ~2535–2629)
  PDF-exports `ViewSheet`s via `PDFExportOptions` + `doc.Export(folder, ids, opts)`. That is
  **vector** and correct — it is the path that produced the good PDFs we already trust.
- `exportview` (BridgeExec.cs ~2100–2200) uses `ImageExportOptions` + `doc.ExportImage`. That
  is **raster** and is the known-bad path: it collapses distinct same-sheet views into one
  identical image and throws an "improper argument" modal that wedges Revit. **Do NOT extend
  or reuse the ExportImage path.**
- `doc.Export(folder, List<ElementId>, PDFExportOptions)` accepts **any exportable views**,
  including drafting views, not only sheets.

## What to build

A new additive bridge verb — suggested name **`exportviews`** — that mirrors the structure and
conventions of `ExportSheets` but targets views by id.

### Inputs (JSON command)
- `document` — resolved with the existing `RequireDoc(app, cmd)`.
- `folder` — output directory (create it, as `ExportSheets` does).
- `views` — an array; each entry is either a view **ElementId** (integer) or an object
  `{ "id": <long>, "key": "<filename-safe key>" }`. `key` (optional) is the output file's
  base name; when absent, use the ElementId as the base name.
- `colors` — `"color"` (default) | `"bw"` | `"grayscale"`, same mapping as `ExportSheets`.

### Behaviour
1. Resolve each requested id to a `View` in `doc`. If an id resolves to nothing, or to an
   element that is not an exportable `View`, **refuse that entry and name it** in the result
   (mirror the exact/ambiguous discipline `ExportSheets` uses for sheet tokens — do not
   silently skip).
2. Export **each view to its OWN PDF file** — `PDFExportOptions.Combine = false`, or export one
   view per `doc.Export` call — so there is **no cross-view conflation**. Base the file name on
   `key` (or the ElementId). Reuse the quality settings from `ExportSheets`:
   `ExportQuality = DPI600`, `HideCropBoundaries = true`, `HideScopeBoxes = true`,
   `HideReferencePlane = true`, `HideUnreferencedViewTags = true`, `ZoomType = Zoom`,
   `ZoomPercentage = 100`, `ColorDepth` per `colors`.
3. **Drafting views not placed on any sheet must still export.** If `PDFExportOptions`/`Export`
   will not take an unplaced view directly, fall back to: in a single transaction, place the
   view on a temporary throwaway `ViewSheet`, export, then **delete that temp sheet and roll
   the model back to exactly its prior state**. The model must be byte-for-byte unchanged after
   the call regardless of which path was taken. Never leave a temp sheet behind.
4. Guard the Revit-version floor exactly as `ExportSheets` does (`PDFExportOptions` needs
   Revit 2022+); the bridge runs Revit 2026, so the modern path is the live one.

### Output (JSON result)
Return a list, one entry per requested view:
`{ "elementId": <long>, "viewName": "<name>", "key": "<base>", "pdf": "<full path>",
"exists": <bool> }`, plus a top-level list of any ids that could not be resolved/exported and
why. The caller relies on `exists` + `pdf` per entry.

## Constraints
- Additive only. Do not touch `exportview`/`ExportImage`, `exportsheets`, or any other verb.
- Follow existing `BridgeExec` conventions: `RequireDoc`, `Require`, `cmd.Get(...).AsStringOr`,
  `Compat.IdValue`, the collector + resolve + refuse-on-miss pattern from `ExportSheets`.
- No destructive model edits. The temp-sheet fallback (if needed) must fully revert.
- No build or test steps in your change; leave verification to the requester.

## Verification (done by the requester, not Codex)
Pull ~12 known `ViewElementId`s spanning disciplines from `detail.DetailOccurrence`, call
`exportviews`, confirm one vector PDF per view, crop + eyeball each, then run the full 609 and
render a contact sheet to confirm none are sheet-sized or garbled before ingesting.
