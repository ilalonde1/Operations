# Codex 1 of N — put every annotation in the same coordinate space as the page

## Goal

In `Kor.Operations.App/EngineeringTools/PdfToSafe/PdfGeometryParser.cs`, make every annotation
reader return coordinates in the SAME space as the page content, so that no markup can land off the
drawing it belongs to.

## The diagnosis, already established — do not re-derive it

An engineer's Bluebeam markup on `~/Desktop/OAP-parcel11-arch-markup.pdf` comes through with 5 of 43
shapes ~60 m from the rest, outside the sheet. The other 38 are correct.

**Cause, confirmed by probe** (`Kor.Operations.App/EngineeringTools.Tests/PdfToSafe/WhichOriginPdfPigReportsProbe.cs`):

```
raw file MediaBox   : [-1728 -1296.12 1728 1296.12]     (read from the file's bytes)
PdfPig MediaBox     : left 0.00 .. right 3456.00        <- normalised
page content x      :      0.0 ..      3456.0           <- normalised
annotation Rect x   :    550.2 ..      3073.5           <- ALSO normalised
```

**PdfPig normalises everything it parses** — the boxes, `page.ExperimentalAccess.Paths`, and
`Annotation.Rectangle`. **Values read straight out of the annotation dictionary are NOT normalised**;
they stay in the file's own user space, which on this sheet begins at −1728, −1296.12.

Two readers take that raw path and multiply by `scale` with no shift:

- `ReadAnnotVertices` — `/Vertices`. Four `/Polygon` annotations (Omar's corner columns).
- `ReadAnnotLine` — `/L`. One `/Line` annotation (his `20'-5"` dimension).

Arithmetic closes: a stray at −39.47 m is −1165 pt; +1728 pt is 563 pt, is 19.07 m, which is inside
the tower with the rest of his markup.

`/Rect` is fine — `ann.Rectangle` arrives normalised. **Do not "fix" the `/Rect` paths.**

## What done looks like

`Kor.Operations.App/EngineeringTools.Tests/PdfToSafe/WhereTheMarkupLandedMeasurement.cs` reports
**zero** shapes flagged `OFF THE DRAWING`, and the five now sit inside the body of the markup
(x roughly 19–47 m, y roughly 47–75 m). It currently reports five; that is the before-picture.

## Constraints

- **One transform, derived once, applied to every raw-dictionary reader.** The point of this change
  is that there is no longer a per-reader coordinate convention. If a sixth reader is added later it
  must be impossible for it to disagree.
- Take the origin from the RAW page dictionary (`/MediaBox`, falling back to `/CropBox`), not from
  PdfPig's normalised `page.MediaBox.Bounds` — that reads 0 and was why an earlier attempt did
  nothing.
- A page whose raw box already starts at 0,0 must come out **byte-identical** to today. Most PDFs
  are that; this must be a no-op for them.
- Touch only `PdfGeometryParser.cs`. Not `VectorPageReader`, not `GeometryFilterService`, not the
  exporters — those are later prompts.
- **Do not build, do not run tests, do not publish.** I verify. Write the code and say what you
  changed and why.
- No destructive operations: no deletions, no git commands, no schema changes.
- If `/InkList` or any other reader shares the same raw-dictionary flaw, fix it in the same pass and
  say so. If you find the diagnosis above is wrong, stop and say that instead — that is more useful
  than a change built on it.

## Context worth having

- `page.ExperimentalAccess.GetAnnotations()` yields `Annotation`; `ann.AnnotationDictionary` is the
  raw dictionary, `ann.Rectangle` is PdfPig's normalised rect.
- `scale` is millimetres per PDF point, already applied by every reader.
- There is a stash on this repo holding two earlier failed attempts at this bug. Ignore it; it is
  kept only so the dead ends are not repeated.
