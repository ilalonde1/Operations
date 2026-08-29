# DXFETABS Convergence Adversarial Audit

I would not execute the convergence plan as written. The direction is right: stop classifying PDF geometry by size and preserve source separation. The unsafe claim is that a PDF-derived DXF can feed the existing DXFETABS intake "unchanged." It cannot today.

## Ranked Findings

1. **The colour-layer DXF path the plan relies on is not reachable from the app.**

   The plan says `DxfExporter(layerByColour)` "already does it" (`docs/codex/CODEX-PDF-TO-DXF-WHOLE-SYSTEM-AUDIT.md:281`), but `DxfExporter.Export` defaults `layerByColour = false` and explicitly says existing callers get structural layers (`Kor.Operations.App/EngineeringTools/PdfToSafe/DxfExporter.cs:16`, `:28`). The app export calls it without `layerByColour` (`Kor.Operations.App/EngineeringTools/PdfToSafe/PdfToSafeWindow.xaml.cs:2219`).

   Trigger: export DXF from PdfToSafe.

   Wrong output: layers are `SLAB`/`COLUMN`/`WALL`/`BEAM`, still based on prior size/type guesses, not `PDF-RRGGBB[-MARKUP]`. The only `layerByColour: true` call is a measurement test (`Kor.Operations.App/EngineeringTools.Tests/PdfToSafe/OmarsDxfMeasurement.cs:53`).

2. **The PdfToSafe DXF is unitless, while DXFETABS now refuses unitless DXF.**

   `DxfExporter` writes `HEADER`, `$ACADVER`, `$EXTMIN`, `$EXTMAX`, then ends the header; no `$INSUNITS` is emitted (`Kor.Operations.App/EngineeringTools/PdfToSafe/DxfExporter.cs:150`). `DxfToEtabsService` requires `DxfPlanReader.UnitInInches` and throws if `$INSUNITS` is absent (`Kor.Operations.EngineeringTools.Core/Dxf/DxfToEtabsService.cs:566`, `:569`).

   Trigger: feed current PdfToSafe DXF into DXFETABS.

   Wrong outcome: not silent, but the plan's "nothing downstream to build" claim is false.

3. **PDF-to-DXF loses text and section facts that both SAFE/CSI and DXFETABS need.**

   `ExtractedGeometry` carries text annotations (`Kor.Operations.App/EngineeringTools/PdfToSafe/PdfGeometryExtractor.cs:56`), line section hints (`:42`), and column sizes (`:20`). The DXF writer emits only `POLYLINE`/`VERTEX` entities (`Kor.Operations.App/EngineeringTools/PdfToSafe/DxfExporter.cs:219`). DXFETABS thickness reading depends on `DxfPositionedTag` text inside slabs (`Kor.Operations.EngineeringTools.Core/Dxf/StructuralPlanClassifier.cs:862`), and `DxfPlanReader` only gets tags from `TEXT`/`MTEXT`/`ATTRIB` (`Kor.Operations.EngineeringTools.Core/Dxf/DxfPlanReader.cs:211`).

   Trigger: PDF with `250 THK`, `B300x600`, or column-size callouts.

   Wrong output: downstream DXFETABS sees no text tags; slab thickness/sections fall back or vanish.

4. **`VectorPageReader` is not a drop-in replacement for `PdfGeometryParser`; it would drop Bluebeam markup.**

   The plan says coordinate/scale work is "use `VectorPageReader` and `SheetScaleReader`" (`docs/codex/CODEX-PDF-TO-DXF-WHOLE-SYSTEM-AUDIT.md:281`). But `VectorPageReader` reads page words and `page.ExperimentalAccess.Paths` only (`Kor.Operations.EngineeringTools.Core/VectorPageReader.cs:91`, `:104`); it has no annotation read path and no colour/source fields in `GeomPath` (`:32`). `PdfGeometryParser.RawSubpath` carries colour and `IsAnnotation` (`Kor.Operations.App/EngineeringTools/PdfToSafe/PdfGeometryParser.cs:19`).

   Trigger: Bluebeam markup-only PDF.

   Wrong output: the shared reader would read the base drawing and miss the engineer's markup.

5. **The five misplaced Parcel 11 shapes are specifically `/Vertices` and `/L` paths.**

   From `OAP-parcel11-arch-markup.pdf`: page 1 has `MediaBox/CropBox [-1728, -1296.12, 1728, 1296.12]` and 44 annotations. The five known strays are:

   - `idx 12 /Line /L`, contents `20'-5"`, centre `(-18.63m, 20.45m)`, size `(6.23m, 0.03m)`.
   - `idx 19 /Polygon /Vertices`, `252.93 sq in`, centre `(-11.70m, 29.88m)`.
   - `idx 22 /Polygon /Vertices`, centre `(-38.48m, 29.84m)`.
   - `idx 24 /Polygon /Vertices`, centre `(-10.82m, 1.92m)`.
   - `idx 32 /Polygon /Vertices`, centre `(-39.47m, 2.69m)`.

   Code evidence: `/Vertices` is read first and then `continue`s (`Kor.Operations.App/EngineeringTools/PdfToSafe/PdfGeometryParser.cs:106`, `:135`); `/L` is read for line annotations (`:187`). Both raw readers just multiply dictionary numbers by scale (`:502`, `:512`).

   Wrong output: plausible column/line geometry appears tens of metres from the drawing body.

6. **Reclassification drops markup provenance before colour-layer export can use it.**

   `ExtractedGeometry` says annotation origin is the exact separator between engineer red and architect red (`Kor.Operations.App/EngineeringTools/PdfToSafe/PdfGeometryExtractor.cs:28`). `ReclassifyByColor` creates a new result copying metadata/text/drop candidates, but not `SlabIsAnnotation`, `ColumnIsAnnotation`, or `LineIsAnnotation` (`Kor.Operations.App/EngineeringTools/PdfToSafe/PdfGeometryExtractor.cs:274`). Its add paths append colours/sizes/hints but not annotation flags (`:289`, `:332`, `:459`). `DxfExporter` only appends `-MARKUP` when those parallel flags exist (`Kor.Operations.App/EngineeringTools/PdfToSafe/DxfExporter.cs:102`, `:169`).

   Trigger: any colour override before `layerByColour` export.

   Wrong output: `PDF-F00000-MARKUP` collapses back into `PDF-F00000`.

7. **PdfToSafe still uses the unsafe scale scanner, not `SheetScaleReader`.**

   On load it calls `PdfGeometryExtractor.DetectScale` (`Kor.Operations.App/EngineeringTools/PdfToSafe/PdfToSafeWindow.xaml.cs:111`), which delegates to `PdfGeometryParser.DetectScale` (`Kor.Operations.App/EngineeringTools/PdfToSafe/PdfGeometryExtractor.cs:164`). That joins all page words and returns the first valid `1:NNN` anywhere (`Kor.Operations.App/EngineeringTools/PdfToSafe/PdfGeometryParser.cs:214`). `SheetScaleReader` is stricter by title-block position (`Kor.Operations.EngineeringTools.Core/SheetScaleReader.cs:46`), and slab takeoff flags fallback assumptions (`Kor.Operations.EngineeringTools.Core/SlabTakeoffEngine.cs:565`).

   Trigger: a sheet with a detail caption `1:50`/`1:100` before the title-block scale in text order.

   Wrong output: all exported coordinates and quantities scale plausibly but wrongly.

8. **Pointing colour layers at DXFETABS requires semantic mapping the plan says it avoids.**

   DXFETABS decides roles by substring layer patterns only (`Kor.Operations.EngineeringTools.Core/Dxf/StructuralPlanClassifier.cs:323`); unmatched layers are skipped during classification (`:418`). The layer ledger will stop a totally unmapped set with enough unclaimed geometry (`Kor.Operations.EngineeringTools.Core/Dxf/DxfToEtabsService.cs:1069`), but making `PDF-F00000-MARKUP` mean "wall" is still a job/sheet-specific semantic decision, not a permanent format convention. That contradicts the plan's "nothing assigned structural meaning the drawing does not state" rule (`docs/codex/CODEX-PDF-TO-DXF-WHOLE-SYSTEM-AUDIT.md:47`).

## What I Would Do Instead

Converge on a shared `PageGeometry` reader first, not on DXF first. It needs one coordinate transform, source kind, reader path, colour, fill/stroke, text, annotation origin, and unit/scale evidence. Then write two adapters from that: faithful DXF with `$INSUNITS` and text preserved, and semantic `PlanGeometrySet` only after an explicit layer/colour/source mapping is supplied or confirmed.

The zero-regression gates I would add before deleting anything:

- Product DXF export must prove `PDF-RRGGBB-MARKUP` layers are reachable.
- Exported DXF must round-trip through `DxfPlanReader` with units, text count, geometry count, and bounding boxes preserved.
- Reclassification must preserve annotation flag counts.
- Every parsed scale must be backed by title-block position or an explicit flagged override.
- Every parsed PDF annotation must carry reader provenance and reject cross-reader coordinate clusters like the five above.

I did not build, test, publish, modify source files, or run git commands.
