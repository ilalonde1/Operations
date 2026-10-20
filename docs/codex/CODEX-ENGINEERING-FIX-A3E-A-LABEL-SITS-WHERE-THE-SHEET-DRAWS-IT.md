# Engineering fix A3e: a markup label sits where the sheet draws it, at the angle the sheet draws it

> **DO NOT RUN. Done directly by Claude on 2026-10-19, at Ian's instruction ("GIVE ME A PROPER DXF NOW").**
> The record of the change follows. Differences from the plan below: the fixture has 10 hidden notes, not 12;
> the gate is `Core.Tests/Intake/AMarkupLabelSitsWhereTheSheetDrawsItTests.cs`.

**Budget.** This brief is about 6 KB, and you read the line ranges listed under **Read**.

**Rules for this run:**
- Open nothing else. No repo search, no git.
- Do **not** build or test; Claude verifies.
- Reasoning: **medium**.

**Response:** `docs/codex/CODEX-ENGINEERING-FIX-A3E-A-LABEL-SITS-WHERE-THE-SHEET-DRAWS-IT-RESPONSE.md`, under 20 lines.

## Measured 2026-10-19 on Omar's A105 (page 6 of `Desktop\Omar\143 E 3rd Hotel Architectural 2026-10-01 - KOR prelim markup.pdf`)

`MarkupText` puts every label "above the shape, centred on it", using `/Contents`. Against the sheet:

- **"4,436 sf"** belongs to a polygon covering the whole floor, so it lands on the top edge. The sheet draws it mid-corridor.
- **Twelve "504 sq in"** labels (`/IT /RectangleSketchToScale`) have `/Contents` but **no text in their appearance stream**: Bluebeam does not draw them. The DXF shows all 12, crowding the plan.
- **"14" wall"** and the **dimension text** are drawn rotated 90° on the sheet. The DXF writes everything at 0°.

Bluebeam's normal appearance stream (`/AP /N`) is what the sheet shows. Measured on this page:
- **Drawn.** 8 annotations carry `BT … (text) Tj … ET`: 1 area, 6 dimensions, 1 FreeText.
- **Not drawn.** The 12 rectangles have content but no `BT`.
- **Reference.** In PdfPig 0.1.9 `/AP /N` arrives as an **`IndirectReferenceToken`** for every annotation, so it must be resolved through the document.
- **Frame.** `ann.Rectangle` is already shifted into the page frame. The form's `/BBox` and `/Matrix` are in the raw frame; mapping the transformed BBox onto `ann.Rectangle` (PDF 32000 §12.5.5) lands in the same frame as the shapes.

**Ian's decision (2026-10-19).** A note the sheet does not draw is kept, on its own layer **`MARKUP-NOTES`, turned off**. Parcel 11 shipped its 27 hidden "12" x 30"" notes as text, so dropping them is not allowed.

**Unchanged.** Label HEIGHT stays `PaperTextHeightMm × scale`, per `MarkupLabelsAreDrawingTextGate`. Do not use the stream's `Tf` size.

## The change

1. **`PdfGeometryModels.cs`**, `TextAnnotation`: append two positional parameters with defaults,
   `double RotationDeg = 0.0, bool HiddenOnSheet = false`. The 3-argument constructor and `Deconstruct` stay as they are.

2. **`VectorPageReader.cs`**: lift the decode in `ReadAppearanceGeometry` (`:504-535`, Filter + Flate) into
   `internal static byte[] DecodeStream(StreamToken stream)`, and call it from where it was. No other change in this file.

3. **`MarkupText.cs`**: the signature becomes `ExtractMarkupTextAnnotations(PdfDocument document, Page page, double scale)`.
   For each annotation with non-empty `Content`:
   - Resolve `/AP` → `/N`. If it is an `IndirectReferenceToken`, use `document.Structure.GetObject(r.Data).Data`; else take the token as is. Expect a `StreamToken`.
   - Decode with `VectorPageReader.DecodeStream` and read it as Latin-1. Take `/BBox` (default the Rect) and `/Matrix` (default identity).
   - **Drawn:** find the first `BT…ET` block with a non-empty `(…) Tj` or `[…] TJ` string. Its text origin is the `Tm` translation (e, f); keep `Tm`'s a, b too. The `cm` that applies is the last `a b c d e f cm` after the most recent `q` before that `BT` (identity if none). Compose `Tm × cm × Matrix`:
     - origin = that composite applied to (0, 0);
     - angle = `atan2(b, a)` of the composite, in degrees.
     - Then fit: transform the 4 BBox corners by `Matrix` and take their axis-aligned box T. Then `x = Rect.Left + (x − T.minX)·Rect.Width/T.Width`, and `y` likewise.
     - Emit `LeftX, BottomY` = that point × scale, with `RotationDeg` = the angle. `X, Y` = the centre: offset by half the estimated width and height, rotated by the angle.
   - **Not drawn:** the stream resolved but holds no text. Use today's placement, above the shape, with `HiddenOnSheet = true`.
   - **No appearance,** or anything above throws: today's placement, visible. Unknown means keep what shipped.
   - Rewrite the comment block at `:39-42` to say this: where the sheet draws it, wins; the above-the-shape rule is the fallback. Name A105.

4. **Callers** pass the document:
   - `PdfGeometryParser.cs:138-139`: the forward gains `PdfDocument document`.
   - `PdfGeometryExtractor.cs:81` and `PdfDiagnostic.cs:125` pass `doc`.
   - `DrawingIntake.cs`: `ReadPage` (`:87`) gains `PdfDocument doc` as its first parameter. `:46` passes `doc`, and `:253` passes it on.
   - `DrawingIntakeTests.cs:151` passes `doc`.

5. **`DxfExporter.cs`**:
   - The `xText` tuple (`:278-285`) carries `RotationDeg` and `HiddenOnSheet`.
   - In the layer table (`:478-509`), when any label is hidden, declare `MARKUP-NOTES` with colour **-7**. In R12 a negative colour means the layer is off. Add it to `layerCount`.
   - `WriteText` (`:605-612`) takes the rotation and the layer. It writes group `50` when the rotation is not 0, and puts hidden labels on `MARKUP-NOTES`.
   - Change nothing else.

## Gate (authored, not run)

New file `Kor.Operations.EngineeringTools.Core.Tests/Intake/AMarkupLabelSitsWhereTheSheetDrawsItTests.cs`, two facts.

**1. The page.** Open Omar's PDF (path above, under `Environment.SpecialFolder.DesktopDirectory`).
- **If it is missing, `Assert.Fail` with the path.** Do NOT return: a silent skip is how the Parcel 11 gate went blind.
- Read page 6 with `scale = 96 * PdfToSafeConstants.PointsToMm`. Divide each label's `LeftX` and `BottomY` by `scale` to get points.
- Assert 8 labels are not hidden and 12 are hidden; every hidden one reads "504 sq in".
- Assert three labels, each to within 0.5 pt and 0.5°:

  | Label | x (pt) | y (pt) | Angle |
  |---|---|---|---|
  | "4,436 sf" | 1687.67 | 774.05 | 0° |
  | "14" wall" | 1513.75 | 689.93 | 90° |
  | The "3'-0"" with the higher `BottomY` | 1662.96 | 1173.76 | 90° |

- Every `HeightMm` equals `PaperTextHeightMm * scale / PointsToMm`.

**2. The DXF.** Build an `ExtractedGeometry` with one slab and two labels: one at 90°, one hidden. `DxfExporter.ExportLines`. Assert:
- a `LAYER` named `MARKUP-NOTES` with group 62 = `-7`;
- the hidden label's `TEXT` is on that layer;
- the rotated label's `TEXT` has group 50 = 90.

## Read

| File | Lines |
|---|---|
| `Kor.Operations.EngineeringTools.Core/PdfToSafe/MarkupText.cs` | all, 53 |
| `Kor.Operations.EngineeringTools.Core/PdfToSafe/PdfGeometryModels.cs` | 18-37 |
| `Kor.Operations.EngineeringTools.Core/VectorPageReader.cs` | 494-537 |
| `Kor.Operations.EngineeringTools.Core/PdfToSafe/DxfExporter.cs` | 278-285, 478-512, 600-616 |
| `Kor.Operations.EngineeringTools.Core/Intake/DrawingIntake.cs` | 40-46, 87, 250-255 |
| `Kor.Operations.App/EngineeringTools/PdfToSafe/PdfGeometryParser.cs` | 136-140 |
| `PdfGeometryExtractor.cs` | 76-82 |
| `PdfDiagnostic.cs` | 28-31, 125 |
| `Kor.Operations.EngineeringTools.Core.Tests/Intake/DrawingIntakeTests.cs` | 145-152 |

## Do not touch

Anything else. Claude verifies:
- builds and runs both suites and the six-set gate;
- re-exports A105 and opens it in AutoCAD's engine;
- renders the result beside the sheet and looks.
