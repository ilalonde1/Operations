# Engineering fix A3c: the app-vs-CLI test uses a page that actually carries markup labels

**Budget.** This brief is about 3 KB, and you read one 5 KB test file and one 55-line range.

**Rules for this run:**
- Open nothing else. No repo search, no git.
- Do **not** build or test; Claude verifies.
- Reasoning: **low**.

**Response:** `docs/codex/CODEX-ENGINEERING-FIX-A3C-THE-SAME-DXF-TEST-USES-A-PAGE-THAT-HAS-LABELS-RESPONSE.md`,
under 15 lines.

## Measured 2026-10-19 on the A3 + A3b build

`TheMarkupDxfIsTheSameFromTheAppAndTheCliTests.BothRoutesWriteTheSameMarkupDxf` fails in both modes at
`Assert.NotEmpty(app.Texts)`. Its own output:

    layerByColour=False: App POLYLINE=25, TEXT=0; CLI POLYLINE=25, TEXT=0

The Regent fixture's page carries markup shapes but **no markup text**. The assumption that it has labels was wrong,
so the test fails before it ever compares the two routes.

## The fix, in `Kor.Operations.App/EngineeringTools.Tests/PdfToSafe/TheMarkupDxfIsTheSameFromTheAppAndTheCliTests.cs` only

1. **Regent case (the existing Theory):** remove `Assert.NotEmpty(app.Texts)`. Keep `NotEmpty(app.Polylines)` and
   both equality assertions. It now proves the shapes are the same.
2. **Add a second Theory over `layerByColour`, `BothRoutesWriteTheSameMarkupLabels`.**
   - Write a synthetic one-page PDF to the temp folder and run both routes on it exactly as the first Theory does
     (scale 96, page 1, `markup: true`).
   - Assert `app.Texts` is not empty, then that `app.Texts` equals `cli.Texts`, and that the polylines are equal.
   - Build the PDF as `Kor.Operations.EngineeringTools.Core.Tests/Intake/DrawingIntakeTests.cs` lines 190-241 do: a
     plain PDF 1.4 string, five objects, an xref. Copy that method into this test class as a private helper, keeping
     only what this test needs:
     - the content stream `0 G 0.5 w 750 600 300 100 re S`;
     - the page with `/Annots [6 0 R]`;
     - one annotation: `<< /Type /Annot /Subtype /Polygon /Rect [600 100 620 120] /Vertices [600 100 620 100 620 120 600 120] /C [1 0 0] /Contents (Check this column) /T (Ian) >>`.
   - Fix the object count and xref accordingly.
3. **The class remarks** now say: shapes are compared on Regent, and shapes plus labels on the synthetic page.

## Read

- `Kor.Operations.App/EngineeringTools.Tests/PdfToSafe/TheMarkupDxfIsTheSameFromTheAppAndTheCliTests.cs` (whole)
- `Kor.Operations.EngineeringTools.Core.Tests/Intake/DrawingIntakeTests.cs` lines 188-242 only

## Do not touch

Any other file.
