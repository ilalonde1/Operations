# Codex 9 of N — put the words back at the size and in the place they were written

## Goal

Text in the exported DXF is illegible: words pile on top of each other. Two causes, both of them
information that was **read and then thrown away**.

## The evidence

Opened in CAD, a suite label reads `RES-S COMMON 100E29 1` — four separate labels overprinted.

**Cause 1 — the height is a constant.** `DxfExporter.WriteText` writes `Num(40, 250.0)` for every
word. Measured on `~/Desktop/OAP-parcel11-arch-markup.pdf`, at its stated 1:96:

```
word heights, mm:  min 189   p25 283   median 370   p75 463   max 2305   (n=299)
the exporter writes 250 for all of them — 0.7x the median, and 9x too small for the sheet title
```

**Cause 2 — the position is the wrong point.** `PdfGeometryParser.ExtractMarkupTextAnnotations`
stores the word's CENTRE:

```csharp
var box = word.BoundingBox;
double xMm = ((box.BottomLeft.X + box.TopRight.X) / 2.0) * scale;
double yMm = ((box.BottomLeft.Y + box.TopRight.Y) / 2.0) * scale;
result.Add((word.Text, xMm, yMm));
```

DXF group codes 10/20 on a `TEXT` entity are the **baseline-left insertion point**, not the centre.
So every word is drawn starting where its middle should be — shifted right by half its width and up
by half its height. On a dense plan that is a pile.

**Both numbers were in hand.** `word.BoundingBox` is read on the line above and only its centre
survives; the shared reader's `VectorPageReader.TextToken` carries `MinX/MinY/MaxX/MaxY` and
`Height` for exactly this.

## What to change

**Carry the word's box, not its centre.** `ExtractedGeometry.TextAnnotations` is
`List<(string Text, double X, double Y)>`; it needs the height too, and X/Y should be the
**bottom-left** of the word box rather than its centre.

Then `DxfExporter.WriteText` writes that height at group code 40 and that point at 10/20.

⚠ **`TextAnnotations` has seven other consumers** — `AnnotationResolver`, `EtabsE2kExporter`,
`SafeF2kExporter`, `PdfToSafeWindow`, `PdfToSafeWindow.AiContext`, `PdfGeometryExtractor`,
`PdfGeometryParser`. Several of them match a label to the shape it sits inside, and a label's
CENTRE is the right point for that, while its bottom-left is not.

**So do not simply move the point.** Either add the extra fields and leave the existing X/Y meaning
alone (so every current consumer is unaffected and only the DXF writer uses the new ones), or change
the meaning and fix all seven — and if you choose that, say so and name each one you touched. The
first is almost certainly right; the point of saying it is that a silent change of meaning here
would move slab thicknesses onto the wrong plates in the SAFE exporter, which nothing would catch.

## What NOT to do

- Do not touch `EngineeringTools.Core`, the classifier, or the geometry paths.
- Do not "improve" the text by merging words into lines. Every word is its own entity today; that is
  fine and matches what the PDF holds.
- Do not build, do not run tests, do not publish. I verify.
- No destructive operations: no deletions, no git commands, no schema changes.

## What I will check

- text heights in the DXF vary and match the source — a spread near 189..2305, not a flat 250
- `RES-S`, `COMMON` and the area labels sit apart from each other and read cleanly
- the label→shape matching in the SAFE and ETABS exporters is unchanged, whichever route you took
- Omar's own labels still read `12" x 30"` and his dimension still measures 20'-5.1"
- both suites, and every PdfToSafe gate
