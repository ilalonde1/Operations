# Codex 23 of N — the window reads what the CLI reads

## Goal

Make the WPF PDF → SAFE window read a page through the same reader as the CLI, so the two cannot
disagree on the same sheet. Today `Kor.Operations.App/EngineeringTools/PdfToSafe/PdfGeometryExtractor.cs`
parses with the shared `PdfPlanReader.ParsePage` and then calls `GeometryFilterService.Classify`
itself, with no sheet furniture, no wall rule, no footings, no grid axes, and the classifier's own
markup-only default — so it reads a clean issued set as empty, and even on a marked-up set it emits
none of what steps 2–8 added. `PdfDiagnostic.cs` does the same a second time. The window has no
markup-versus-drawing switch at all (no `MarkupOnly` or `annotationsOnly` anywhere under
`App/EngineeringTools/PdfToSafe`).

Repo-only. Files: `App/EngineeringTools/PdfToSafe/PdfGeometryExtractor.cs`, `PdfDiagnostic.cs`,
`PdfToSafeWindow.xaml` + `.xaml.cs`, `GeometryExportOrchestrator.cs`;
`Core/PdfToSafe/PdfPlanReader.cs` (read it, change nothing in it unless a parameter is missing).
Do not build or test; the verifier runs `Kor.Operations.App/EngineeringTools.Tests` (443 tests,
15 s) and opens the window.

## The class, in one sentence

One page, one reader: whatever the window shows for a sheet is what `PdfPlanReader.Read` returns
for that sheet with the same mode, and the mode is the engineer's choice, visible in the window.

## Change this

1. `PdfGeometryExtractor.Extract(...)` (both overloads) becomes a call to
   `PdfPlanReader.Read(doc, scaleDenominator, pageNumber, annotationsOnly, slabMinDiagonalMm,
   lineMinLengthMm, excludeGridLines)` plus `result.TextAnnotations =
   PdfGeometryParser.ExtractMarkupTextAnnotations(page, scale)` — the one thing Core does not read
   yet. Add a `bool annotationsOnly = true` parameter so the default behaviour of every existing
   caller is unchanged.
2. `PdfDiagnostic.cs`: the same replacement for its classify call.
3. `PdfToSafeWindow`: a two-state control, "Read the mark-up" (default, today's behaviour) and
   "Read the drawing", wired to that parameter. The status line already reports counts; add walls,
   footings and grid axes to it (the result carries `Walls`, `Footings`, `GridAxes` since steps 2b,
   7 and 8).
4. `GeometryExportOrchestrator`: pass the window's mode through.

## Do not

- Do not change `GeometryFilterService`, `PdfPlanReader`, `DxfExporter` or anything in Core.
- Do not change the default: a window opened as today still reads the mark-up only.
- Do not add a third mode, a setting, or a registry key.

## What the verifier checks

- `EngineeringTools.Tests` green; `GeometryFilterTests` unchanged (they call the classifier
  directly and are not this brief's concern).
- The window on 31168 p14 in "Read the drawing": walls 18, columns and slabs as the CLI's
  `pdf-takeoff` reports for the same page and scale; in "Read the mark-up" on Andrea's parking
  mark-up (41 pages, 216 annotations on p12): the same counts as before the change.
- The doc's §2 row for PDF → SAFE / SAP loses "classification is not shared".
