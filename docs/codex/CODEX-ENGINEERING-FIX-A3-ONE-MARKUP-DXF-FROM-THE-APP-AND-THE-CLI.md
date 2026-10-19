# Engineering fix A3: one markup DXF, the same from the app and the CLI, layered by the engineer's colours

**Budget.** This brief is about 6 KB, and the reading set is about 55 KB in the ranges listed at the end.

**Rules for this run:**
- Open nothing else. No repo search, no git.
- Do **not** run `dotnet build`, `restore` or `test`; Claude verifies on the dev box.
- Reasoning: **medium**.

**Response:** `docs/codex/CODEX-ENGINEERING-FIX-A3-ONE-MARKUP-DXF-FROM-THE-APP-AND-THE-CLI-RESPONSE.md`, under 40 lines.

## What is wrong, measured 2026-10-19 on a real job (143 E 3rd, sheet A105, Omar's Bluebeam markup)

His markup is 27 annotations: 10 column rectangles, 8 wall rectangles and a core outline (all burgundy), a floor
outline, 6 Bluebeam **length measurements** (red), and text.

1. **The CLI drops every markup label.** The app reads markup text with
   `PdfGeometryParser.ExtractMarkupTextAnnotations` (App, `PdfGeometryParser.cs:142`), called at
   `PdfGeometryExtractor.cs:81`. Core's `DrawingIntake`, which `takeoff pdf-takeoff --markup` uses, never reads it.
   - Result on A105: the app's DXF has 18 TEXT, the CLI's has 0.
   - Their 26 polylines are identical vertex for vertex.
2. **Neither path can export by colour.**
   - The default layering puts his walls on SLAB/COLUMN and his dimension lines on BEAM, so 15 of his 26 shapes are on
     layers that say something false.
   - `DxfExporter` already has `layerByColour` (`DxfExporter.cs:33-45`), written for Omar's previous request, but the
     app hardcodes it off (`PdfToSafeWindow.xaml.cs:2296`) and the CLI has no flag.
   - Exported by colour, A105 is truthful: `PDF-800000-MARKUP` 19, `PDF-004000-MARKUP` 1, `PDF-F00000-MARKUP` 6,
     `PDF-TEXT` 18.

**Do not change classification.** Leaving markup out of the wall rule is a deliberate, tested decision (intake brief
15): the engineer's colour says what a markup shape means.

## The fix

1. **Markup text is read in Core, once.**
   - Move `ExtractMarkupTextAnnotations(Page page, double scale)` and its private helpers unchanged from App
     `PdfGeometryParser.cs` into a new Core file `Kor.Operations.EngineeringTools.Core/PdfToSafe/MarkupText.cs`
     (`public static class MarkupText`, namespace `Kor.Operations.EngineeringTools.PdfToSafe`).
   - Leave `PdfGeometryParser.ExtractMarkupTextAnnotations` as a one-line forward to it, so the App's two callers
     (`PdfGeometryExtractor.cs:81`, `PdfDiagnostic.cs:125`) are untouched.
   - In `DrawingIntake.ReadPage`, **only when `request.MarkupOnly`**, add `MarkupText.ExtractMarkupTextAnnotations(page,
     scaleFactor)` to `geometry.TextAnnotations`, using the same scale argument the App passes (read the App call to
     match it).
   - Page-content mode is unchanged: stick-file models must not gain markup text.
2. **Layer by colour is a choice in both.**
   - **CLI:** `takeoff pdf-takeoff ... --layer-by-colour`. Thread a `bool layerByColour` through
     `PdfOnlyBuild.WriteSheets` (next to `korLayers`, default `false`) into both `DxfExporter.ExportLines` calls
     (lines 214 and 226). Add it to the verb's usage line. `--layer-by-colour` together with `--kor-layers` is refused
     with a one-line message.
   - **App:** a checkbox "Layers by markup colour" next to the Export DXF button (`PdfToSafeWindow.xaml:317`). Bound to
     a new `FirmDefaults.DefaultDxfLayerByColour` (bool, default `false`), so the choice persists like the other firm
     defaults. Pass it at `PdfToSafeWindow.xaml.cs:2296` instead of the literal `false`.

## Gates (authored, not run)

1. **App test**, `Kor.Operations.App/EngineeringTools.Tests/PdfToSafe/TheMarkupDxfIsTheSameFromTheAppAndTheCliTests.cs`.
   On the repo fixture `Fixtures/regent_typ_floor.pdf` (page 1, scale 96), for `layerByColour` false and true:
   - **The app's DXF:** `PdfGeometryExtractor.Extract` + `PdfGeometryExtractor.ExportDxf`.
   - **The CLI's DXF:** `PdfOnlyBuild.WriteSheets(pdf, out, 1, 1, 96, markup: true, korLayers: false, PdfIntakeOptions.Default, layerByColour: ...)`.
   - Assert that both write the same multiset of POLYLINE (layer + vertices to 0.1 mm) and TEXT (layer, string,
     position to 0.1 mm), and print the counts.
   - State in the class remarks what it does **not** compare: scale detection, page-content mode, and colour
     settings from the grid.
2. **Core test:** `DxfExporter` with `layerByColour: true` on a two-colour geometry writes one layer per colour and
   no SLAB, BEAM or COLUMN layer.

## Read

- `Kor.Operations.App/EngineeringTools/PdfToSafe/PdfGeometryParser.cs`: whole (12 KB)
- `Kor.Operations.App/EngineeringTools/PdfToSafe/PdfGeometryExtractor.cs`: 40-112
- `Kor.Operations.App/EngineeringTools/PdfToSafe/FirmDefaults.cs`: whole (11 KB)
- `Kor.Operations.App/EngineeringTools/PdfToSafe/PdfToSafeWindow.xaml`: 300-330
- `Kor.Operations.App/EngineeringTools/PdfToSafe/PdfToSafeWindow.xaml.cs`: 2041-2060 and 2270-2305
- `Kor.Operations.EngineeringTools.Core/Intake/DrawingIntake.cs`: 100-150 and 300-335
- `Kor.Operations.EngineeringTools.Core/Intake/PdfOnlyBuild.cs`: 113-130 and 200-240
- `Kor.Operations.EngineeringTools.Core/PdfToSafe/DxfExporter.cs`: 30-125
- `Kor.Operations.EngineeringTools.TakeoffCli/Verbs/PdfTakeoffVerb.cs`: whole (9 KB)
- How the existing App tests load a fixture: `Kor.Operations.App/EngineeringTools.Tests/PdfToSafe/VirtualPipelineTests.cs`, first 40 lines

## Do not touch

Anything else, including `GeometryFilterService` and `ReclassifyByColor`, and `Baselines/`.
