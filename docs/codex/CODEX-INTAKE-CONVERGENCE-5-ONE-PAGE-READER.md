# Codex 5 of N — one PDF page reader, reached two ways

## Goal

Make `Core/VectorPageReader` the single place a PDF page is read, and turn
`App/EngineeringTools/PdfToSafe/PdfGeometryParser.ParsePage` into a projection of it.

Today there are two readers of the same file format, and the comment explaining why is out of date:

> *"This is deliberately INDEPENDENT of the PdfToSafe app. PdfToSafe reads the engineer's Bluebeam
> MARKUP annotations (it intentionally discards the base drawing); a takeoff must read the native
> drawing itself."* — `VectorPageReader.cs`

PdfToSafe reads whole pages now, so both readers read the same thing from the same files by
different code. That is how one of them ended up with five markups 60 m off the sheet while the
other was fine.

## The hard constraint, first

**`VectorPageReader` has ten production consumers.** `StructuralGridReader`, `SheetTitleReader`,
`SheetScaleReader`, `ScheduleGridReader`, `SlabTakeoffEngine`, `FootingScheduleReader`,
`StickFileSlabThicknessReader`, `DrawingDigest`, `SlabThicknessZoner`, and the CLI. The slab and
rebar takeoffs sit on top of them.

**Every one of them must see exactly what it sees today.** This is a MOVE, not a rewrite. So:

- `GeomPath` gains **init-only properties**, never positional parameters — adding a positional
  parameter changes the constructor and breaks callers that build one.
- **Annotations are opt-in.** `ReadPage(path, page)` must return what it returns today, with no
  annotation paths in it. A second overload — `ReadPage(path, page, includeAnnotations: true)` or
  equivalent — is what PdfToSafe calls. If annotations appeared in the default read,
  `StructuralGridReader` would suddenly find grid lines in an engineer's markup.
- Nothing else in `Core` changes shape.

## What to move

Into `VectorPageReader`, from `PdfGeometryParser`:

- the raw page origin derived from `/MediaBox` falling back to `/CropBox`, applied once to every
  raw-dictionary reader (prompt 1 — it belongs to reading a page, not to PdfToSafe)
- the annotation readers: `/Vertices` with `/Rotation`, the appearance-stream path, `/Rect`, `/L`,
  `/InkList`
- the per-path **colour**, and the **annotation-vs-page-content** origin

`GeomPath` then carries what the audit said a shared reader needs: points, closed/filled/stroked,
colour, and whether it came from an annotation.

`PdfGeometryParser.ParsePage` keeps its `RawSubpath` shape and becomes a mapping from `GeomPath` —
so `GeometryFilterService` and everything downstream of it is untouched.

## What NOT to do

- **Do not touch `GeometryFilterService`, the exporters, or the classification.** The size-based
  slab/column/beam guess is on the list to delete but it is not this prompt, and mixing it in makes
  this one unverifiable.
- Do not change `SheetScaleReader`, `PlanGeometry`, or any takeoff logic.
- Do not "tidy" the annotation readers while moving them. If one looks wrong, say so and leave it —
  a behaviour change hidden inside a move is the hardest kind of regression to find.
- Do not build, do not run tests, do not publish. I verify.
- No destructive operations: no deletions of test files, no git commands, no schema changes.

## What I will check

Nothing may move:

- `Core` suite: 678 tests, same result
- App suite: 439 tests, same result
- `WhereTheMarkupLandedMeasurement` — all 43 markups still on the drawing
- `DxfRoundTripGate` — units, 323 words in and out, extent 117.0 × 87.8 m, no text outside
- `MarkupSurvivesReclassificationGate` — 43 in, 43 out, `-MARKUP` present
- `ColourIsTheSelectorMeasurement` — still 11 colours, `#F00000` still 5 slabs / 37 columns / 489 lines
- Omar's markup DXF — still 43 polylines, 32 of his own text entities, and it still renders as his
  core walls, perimeter shear walls, four corner columns and his dimension

If any of those numbers changes, the move changed behaviour and I will treat it as a defect rather
than an improvement — say so up front if you know a number will move, and why.

## Why this is worth doing at all

Not tidiness. Three of the five faults in this sequence existed because two readers disagreed about
the same file, and a fourth (the scale) existed because PdfToSafe never called a Core reader that had
been solving the problem correctly for months. One reader is how those stop recurring.
