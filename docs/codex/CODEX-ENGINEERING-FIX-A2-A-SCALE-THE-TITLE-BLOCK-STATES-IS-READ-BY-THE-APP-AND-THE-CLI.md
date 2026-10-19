# Engineering fix A2: a scale the title block states is read, by the app and the CLI alike

**Budget.** This brief is about 6 KB, and the reading set is about 45 KB in the ranges at the end.

**Rules for this run:**
- Open nothing else. No repo search, no git.
- Do **not** build or test; Claude verifies.
- Reasoning: **medium**.

**Response:** `docs/codex/CODEX-ENGINEERING-FIX-A2-A-SCALE-THE-TITLE-BLOCK-STATES-IS-READ-BY-THE-APP-AND-THE-CLI-RESPONSE.md`,
under 30 lines.

## Measured 2026-10-19 on a real sheet (143 E 3rd Hotel, A105, by Formosis Architecture)

The sheet states its scale twice:
- in the title block, a label `SCALE` with the value `1/8" = 1'-0"` set **under** it, about 17 pt lower in the same
  column;
- under the viewport, as a caption.

The right-hand note column also carries the sentence `DO NOT SCALE THE DRAWINGS.`.

**What each reader does with it:**

1. **`SheetScaleReader.FromPage` finds nothing.** It wants the value on the label's baseline, to its right
   (`SheetScaleReader.cs:92-97`, ±6 pt).
2. **`TitleBlockFields.Read` returns `SCALE = "THE DRAWINGS."`.**
   - A label is accepted "at any position in a line" (`TitleBlockFields.cs:85-125`), so the `SCALE` inside that
     sentence becomes a field label and the words after it its value.
   - The intake's own ledger says so: `scale: stated without a ratio ("THE DRAWINGS.") - TitleBlockFields`.
3. **The app finds no scale at all.**
   - `PdfGeometryParser.DetectScaleForLoad` (App) uses `SheetScaleReader` and caption notes only. It never uses
     the title-block field fallback the intake has in `DrawingIntake.StatedScaleDenominator` (`DrawingIntake.cs:54-75`).
   - It returns source None, and the window silently falls back to its scale box (1:100 by default). The sheet is
     1:96, so that is 4% oversize.

**Reach.** In the corpus ledger (run 45), 783 of 4,075 plan sheets, across 117 of 266 jobs, had no scale read off the
sheet and were measured at the 1/8" fallback.

## The fix

1. **A label in the middle of a sentence is not a label.**
   - In `TitleBlockFields.Read`, a label found after the first token of its line (`i > 0`) is accepted only when
     the label's last token ends with `:` or the next token is `:`.
   - A label at the start of its line is unchanged, and the letter-spaced runs are unchanged.
   - Comment it with the A105 sentence.
   - `DRAWN BY: X  CHK'D BY: Y` still reads both fields, because the second has its colon.
2. **One stated-scale decision for both.**
   - Move the body of `DrawingIntake.StatedScaleDenominator(VectorPageReader.PageContent)` into a public
     `SheetScaleReader.StatedDenominator(VectorPageReader.PageContent? page)`.
   - Leave `DrawingIntake.StatedScaleDenominator` as an `internal` one-line forward, because
     `ASheetIsReadAtTheScaleItStatesTests` calls it.
3. **The app uses it first.** In `PdfGeometryParser.DetectScaleForLoad`, after reading the page, try
   `SheetScaleReader.StatedDenominator(page)`. If it returns a value, return it as `PdfScaleSource.TitleBlock` with
   that denominator and the field's text as the note. Leave the existing caption and conflict logic after it, unchanged.

## Gates (authored, not run), in `Kor.Operations.EngineeringTools.Core.Tests/Intake/ATitleBlocksFieldsAreItsOwnTests.cs`

Build each page the way the file already builds its synthetic pages.

- `AScaleSetUnderItsLabelIsTheScale`:
  - The right-fifth strip holds a line `DO NOT SCALE THE DRAWINGS.` near the top, and a column `SCALE` with
    `1/8" = 1'-0"` 17 pt below it near the bottom.
  - `Read` gives `SCALE = 1/8" = 1'-0"`, and `SheetScaleReader.StatedDenominator` gives 96.
- `AWordInASentenceIsNotAFieldLabel`: a strip with only `DO NOT SCALE THE DRAWINGS.` yields no `SCALE` field.
- `AMidLineLabelWithItsColonIsStillALabel`: `DRAWN BY: AB  CHK'D BY: CD` yields both fields.

## Read

- `Kor.Operations.EngineeringTools.Core/Intake/TitleBlockFields.cs` lines 1-160
- `Kor.Operations.EngineeringTools.Core/SheetScaleReader.cs` (whole, 9 KB)
- `Kor.Operations.EngineeringTools.Core/Intake/DrawingIntake.cs` lines 50-76
- `Kor.Operations.App/EngineeringTools/PdfToSafe/PdfGeometryParser.cs` lines 40-115
- `Kor.Operations.EngineeringTools.Core.Tests/Intake/ATitleBlocksFieldsAreItsOwnTests.cs`: the first 80 lines, and
  one existing test that builds a synthetic page (find it by its name in the first 80 lines)

## Do not touch

Anything else, including `Baselines/`. Claude measures the effect on the six banked models and on the local PDFs.
