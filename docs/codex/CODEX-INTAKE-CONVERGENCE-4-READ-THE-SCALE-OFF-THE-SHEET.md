# Codex 4 of N — take the scale off the title block, using the reader that already does it

## Goal

Replace `PdfGeometryParser.DetectScale` with `Core/SheetScaleReader`, so PdfToSafe reads the scale
the way the takeoff already does — from the title block, by position — instead of scanning the page
for the first `1:NNN` it can find.

This is the first prompt in the sequence that is CONVERGENCE rather than repair: the shared reader
exists, is tested, and has been right all along. PdfToSafe just does not call it.

## The evidence

`PdfGeometryExtractor.DetectScale` returns **NULL** for
`~/Desktop/OAP-parcel11-arch-markup.pdf`, whose title block reads `SCALE: 1/8" = 1'-0"`.
Probe: `Kor.Operations.App/EngineeringTools.Tests/PdfToSafe/WhatTheScaleDetectorSaysProbe.cs`.

Two independent reasons, in `PdfGeometryParser.cs:208`:

1. The pattern is `1\s*[:/]\s*(\d{2,4})` — it demands two to four digits, so `1/8` yields `8` and
   never matches.
2. The whitelist is `{20, 25, 33, 50, 75, 100, 125, 150, 200, 250, 500, 1000}` — **96 is not in it,
   and neither is any other imperial denominator.**

**So no sheet drawn in imperial can ever have its scale detected**, which is most North American
structural drawing. The app then falls back to a typed default, and every coordinate it exports is
wrong by whatever the difference is. On Parcel 11 that is 1:100 against 1:96 — 4% oversize
everywhere, silently. I shipped that DXF to an engineer and then hard-coded 96 around it.

## The reader that already solves this

`Kor.Operations.EngineeringTools.Core/SheetScaleReader.cs` (90 lines, tested in
`Core.Tests/SheetScaleReaderTests.cs`). Its own summary states this exact failure: *"a metric set
drawn 1:100 but measured at the imperial default 1/8"=1'-0" (1:96) under-prices every area by ~8%."*

It reads **by position** — the SCALE label in the title-block region (right edge, bottom third) —
so a viewport caption like `SCALE: 1:20` under a stair detail cannot masquerade as the sheet scale.
It returns null, never a guess, when the note is `AS NOTED`/`NTS` or when two SCALE fields disagree.

Call it the way `SlabTakeoffEngine.cs:413` does:

```
SheetScaleReader.FromPage(VectorPageReader.ReadPage(path, pageNumber))
```

It returns the **note as text** (`1/8" = 1'-0"`, `1 : 100`). To get PdfToSafe's denominator, parse it
with `PlanGeometry.MetresPerPixel(note, renderDpi: 72)` — which handles architectural notes and
engineering ratios both — and convert:

```
denominator = metresPerPoint * 1000.0 / PdfToSafeConstants.PointsToMm
```

Check it against the two cases before you rely on it: `1/8" = 1'-0"` must give **96**, and
`1 : 100` must give **100**.

## When the sheet does not state a scale

**Say so. Do not silently default.** The takeoff already sets the pattern
(`SlabTakeoffEngine.cs:565`): it falls back and FLAGS the fallback, so a number that rests on an
assumption is never mistaken for one that rests on the drawing.

PdfToSafe should do the same — keep the user-set scale as the fallback, and make it visible in the
UI that the value was assumed rather than read. A wrong scale is the worst class of error this tool
can make: everything downstream stays plausible and is uniformly wrong, and nothing anywhere says so.

## Constraints

- **Do not modify `SheetScaleReader`, `VectorPageReader`, `PlanGeometry` or anything else in
  `EngineeringTools.Core`.** Those have other callers — the slab takeoff depends on all three — and
  this prompt is about PdfToSafe calling them, not about changing them. If you believe one of them is
  wrong, say so and stop; do not fix it here.
- Files: `PdfGeometryParser.cs`, `PdfGeometryExtractor.cs`, `PdfToSafeWindow.xaml.cs`.
- **Do not build, do not run tests, do not publish.** I verify.
- No destructive operations: no deletions, no git commands, no schema changes.
- Keep `DetectScale` reachable if something else calls it — check first and say what you found — but
  it must no longer be what decides the scale on load.
- Do not touch the markup/whole-page split, the coordinate transform, or the exporters. Done, and
  verified; changes there now would be unverifiable against this prompt.

## What I will check

- Parcel 11 loads at **96**, not null and not a default
- a metric sheet still loads at its own ratio
- a sheet with no stated scale is flagged as assumed rather than silently defaulted
- `TheScaleIsPrintedOnTheSheetMeasurement` still shows 1:96 reproducing the architect's printed suite
  areas within 0.6%
- `OmarsDxfMeasurement` can stop hard-coding `const int scale = 96` and read it — that constant
  existing at all is the band-aid this prompt removes
- both suites green
