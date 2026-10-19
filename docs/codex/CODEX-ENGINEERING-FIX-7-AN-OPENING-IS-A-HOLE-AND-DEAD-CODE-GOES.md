# Engineering fix 7 of 7: in PdfToSafe an "Opening" becomes a hole, and dead code goes

> ⛔ **WITHDRAWN 2026-10-19 — DO NOT RUN.** Dropping an Opening-typed shape is a deliberate fix of 2026-04-16
> (928d8f07: before it they exported as slabs). Whether it should cut a hole instead is Ian's decision, not a defect.
> Superseded by the plan of 2026-10-19. Kept as a record.

Bounded brief for Codex. Source edits and gates only. **Do NOT run `dotnet build` or `dotnet test`**: Claude verifies
on the dev box. Apply, read your own diff back, write the response, stop. No git operations beyond your own edits
(deleting the three files below is an edit; use your normal file delete, not `git rm`).

The response goes beside this file as `...-RESPONSE.md`. Reasoning: medium. Reading set, measured: about 115 KB
(listed at the end). Read only the ranges given.

## Two defects, verified on 2026-10-19

1. **A shape the engineer types "Opening" disappears instead of cutting a hole.**
   - In the Color Properties grid or by right-click, "Opening" is offered as a type.
   - The window's own guidance and tool text say it "becomes a hole in the parent slab".
   - `PdfGeometryExtractor.ReclassifyByColor` drops it. Around lines 278-284 (slab loop) and 326-330 (column loop),
     the `Opening` and `Ignore` branches are both "Explicitly suppressed — do not include in output".
   - `F2kModelPrep` already has the hole machinery. `openingPairs` (around line 240) is a list of
     (ParentIndex, ChildIndex) turned into `openingRows` (around 385-392), alongside the wall-enclosed shaft openings
     (around 394-408).
   - But `openingPairs` is always empty. The comment there says users "can explicitly set shapes to Opening via
     right-click", and nothing carries those shapes through.
2. **Four classes are dead.**
   - `HtmlReportBuilder`, `GeometryExportOrchestrator` and `PdfDiagnostic` are referenced nowhere outside their own
     files: 0 references each, repo-wide, tests included.
   - `PdfGeometryAnalysisService` is constructed once (`PdfToSafeWindow.xaml.cs:57` field, `:67` construction), and
     none of its methods is ever called.

## The fixes

1. **Opening is a hole.**
   - `ReclassifyByColor` keeps `Opening`-typed slab-bucket polygons in a new list on the reclassified geometry
     (`Openings`), instead of discarding them. `Ignore` keeps its current behaviour.
   - `F2kModelPrep` pairs each opening with the slab whose polygon contains the opening's centroid (use the containment
     helper the file already uses, if there is one) and appends it to `openingRows` exactly as the shaft openings are
     appended. An opening inside no slab is reported in the export summary as "opening N lies on no slab — not cut",
     never dropped silently.
   - **SAFE `.f2k`:** the existing FLOOR OBJECT OPENINGS path. Nothing new is needed beyond feeding it.
   - **Live push** (`ExportOrchestrator`): use the same `SetOpening` path the shaft openings use.
   - **ETABS `.e2k`** (`EtabsE2kExporter`): today it writes no openings. Write each one as an `AREA` with the opening's
     points, assigned `OPENING "Yes"` on its storey, in the form ETABS writes:
     `AREAASSIGN "<name>" "<story>" ... OPENING "Yes"`.
   - Update the export summary's opening count to include them.
2. **Delete** `HtmlReportBuilder.cs`, `GeometryExportOrchestrator.cs` and `PdfDiagnostic.cs`. Delete
   `PdfGeometryAnalysisService.cs` together with its field and construction in `PdfToSafeWindow.xaml.cs`. Remove any
   `using` that becomes unused. Change nothing else in the window.

**Out of scope, and do not touch:**
- The window's AI wiring, its AI tools, and the AI service the window actually uses. Ian is deciding separately how
  those should work.
- `QuantityTakeoffWindow` and `RebarChangeWindow`, which are registered on purpose until the consolidated tool is
  field-verified (`AppModule.cs:200`).

## Gates (authored, not run), in `Kor.Operations.App/EngineeringTools.Tests/PdfToSafe/`

- `AShapeTypedOpeningIsCutFromItsSlab`: a geometry with one large slab and one small polygon inside it, the small one's
  colour typed `Opening`. After reclassify plus `F2kModelPrep`, there is 1 opening row whose parent is the large slab,
  and the small polygon is not also a slab.
- `AnOpeningOnNoSlabIsReportedNotDropped`: the opening polygon outside every slab produces 0 rows and one summary line.
- `TheE2kCarriesTheOpening`: `EtabsE2kExporter` output for the first case contains an `AREAASSIGN` line with
  `OPENING "Yes"`.
- Update any existing test that asserted Opening shapes vanish. Name it in the response.

## Never read

`Fixtures/*.pdf`, any `.e2k`, `.f2k` or `.dxf`, and `Baselines/`.

## Read

- `Kor.Operations.App/EngineeringTools/PdfToSafe/PdfGeometryExtractor.cs` 140-420
- `Kor.Operations.App/EngineeringTools/PdfToSafe/F2kModelPrep.cs` (whole, 21 KB)
- `Kor.Operations.App/EngineeringTools/PdfToSafe/ExportOrchestrator.cs` 380-450
- `Kor.Operations.App/EngineeringTools/PdfToSafe/EtabsE2kExporter.cs` (whole, 19 KB)
- `Kor.Operations.App/EngineeringTools/PdfToSafe/ExportSummaryReport.cs` (the opening count only)
- `Kor.Operations.App/EngineeringTools/PdfToSafe/PdfToSafeWindow.xaml.cs` 50-75 only

## Response

Files changed and deleted, gates added, and the tests updated. Under 50 lines.
