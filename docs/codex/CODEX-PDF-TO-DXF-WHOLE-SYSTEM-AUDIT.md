# PDF to DXF — what this system actually is, and why it keeps failing

Written 2026-08-29 after a fourth symptom in one evening, on Ian's instruction:

> "I'd like you to take a step back and look at the whole mechanism we use in all these tools. I
> think you're just band-aiding small issues as opposed to looking at the whole big picture and how
> we can actually simplify these tools to make them work better. That seems to be what worked for
> the ETABS model builder."

He is right, and repo rule 10 already says it: two regressions means stop and characterise. Last
night and this morning I shipped four fixes to one deliverable and the engineer still opened it and
found it wrong. This is the characterisation that should have come first.

---

## 1. The request, and what came back

Omar, 28 August, an architect's sheet attached:

> "I would like to get a dxf for this pdf. The tower outline, the red markups I did and the balcony
> outline."

What he opened:

- **Every piece of the architect's drawing.** Beds, kitchens, toilets, furniture, door swings,
  dimension strings, the title block, the north arrow. He asked for three things and got the sheet.
- **Five of his own markups sitting in empty space off the left edge**, outside the title block —
  four corner columns and his 20'-5" dimension — while the rest of his red was correctly placed.

Three separate fixes went in before he ever saw it (colour layering, markup-vs-page separation,
scale) and none of them touched either of those two faults, because none of them was aimed at a
cause.

---

## 2. What the system IS

A drawing PDF carries exactly four things:

| | what it is | where it lives |
|---|---|---|
| **Geometry** | paths — lines, curves, filled regions | page content stream |
| **The draughtsman's separation** | colour, line weight, fill-vs-stroke | on every path |
| **The reviewer's separation** | markup annotations, distinct from the page beneath | `/Annots` |
| **Words** | text, and the scale printed in the title block | text runs |

**The single rule that has to hold:** every shape keeps its position in ONE coordinate space, and
keeps the separators the drawing already gave it. Nothing is assigned a structural meaning the
drawing does not state.

That is the whole job. A PDF is not a model and this tool cannot make it one — what it can do is
carry the drawing across without losing what the drawing already knows.

---

## 3. Where the code contradicts it

### 3a. It classifies before it represents — and classification is a size guess

`GeometryFilterService.Classify` is the first thing every path meets, and it sorts each one into
**slab / column / beam / wall by SIZE**:

```
if (isClosed)  { diag >= slabMinDiagonal → SLAB;  bbox <= columnMaxSize → COLUMN }
else           { len >= lineMinLength → BEAM }
```

Nothing in a PDF says a shape is a slab. So on Parcel 11:

- the two **sheet borders** became the largest "slabs" in the file (117 × 88 m and 97 × 74 m)
- the **suite fills** became slabs (101.8 m² — correct as areas, meaningless as structure)
- eight **title-block rules** became slabs (9.1 × 0.1 m)
- **furniture and door swings** became 8,872 "beams"

Then `DxfExporter` writes those guesses out as the LAYER NAMES — `SLAB`, `COLUMN`, `BEAM`, `WALL` —
so the file's only organising principle is a size threshold applied to an architect's drawing.

**This is the root of "you've brought in all the architectural crap."** The tool has no concept of
what anything is beyond its size, so it has no way to keep three things and drop the rest. It was
never asked to. It cannot be asked to.

### 3b. There is no single coordinate space

Positions come from **five** different readers, each with its own assumption:

| path | source | space |
|---|---|---|
| page content | PdfPig `page.Paths` | normalised to the page corner |
| `/Vertices` | raw dictionary | raw user space |
| appearance stream `re` | content stream + `/BBox` | the stream's own space |
| `/Rect` fallback | raw dictionary | raw user space |
| `/L` (Line) | raw dictionary | raw user space |

Nothing reconciles them. Most of Omar's markup happened to come through a path that agreed with the
page; five came through one that did not, and landed roughly 60 m away.

⚠ **This is NOT yet solved, and three guesses at it failed on 29 August** — threading `/Rect` into
the appearance mapping (spec-correct, stashed, changed nothing here), and offsetting by the MediaBox
origin (this page's MediaBox is `[-1728 -1296.12 1728 1296.12]`, its origin the middle of the sheet
— but PdfPig already normalises it, so the conversion was a no-op). **The next attempt must start by
instrumenting which of the five paths each shape took**, not by guessing which one is wrong.

### 3c. Scale is an input nobody checks

Every output is linear in one number typed by whoever opens the tool. It defaulted to 1:100. The
sheet says `SCALE: 1/8" = 1'-0"`, which is 1:96, and the sheet ALSO prints every suite's area:

```
1:96    7 of 8 printed areas matched within 2%   median error 0.59%
1:100   2 of 8                                   median error 8.37%
```

A DXF 4% oversize everywhere, silently. Fixed for this file on 28 August by hard-coding 96 — which
is a band-aid: the scale is printed on the sheet and the check is printed beside it.

### 3d. It never refuses and never asks

The DXF-to-ETABS side names every region it turned down — area, position, reason — and turns the
gap into a located question in the engineer's workbook. This side emits everything and says nothing.
There is no equivalent of `slab-count.<job>.<storey>`: no way for Omar to say "the red is my shear
walls" and have that stick.

---

## 4. Why the ETABS builder works and this does not

Not because it is better written. Because of one structural difference:

> **DXF-to-ETABS never decides what a thing is. The drawing says, and where the drawing does not,
> the engineer says, and that answer is banked.**

A DXF arrives pre-separated — `JBP_V-WALL`, `JBP_C_SLABEDG`, `JBP_V_COL`. The tool reads the
draughtsman's own layering. Where a drawing is ambiguous it refuses, names what it refused, and asks
a question scoped to that job and storey; the answer goes into `analysis.FormatConvention` and holds
for ever after.

**A PDF carries the same separation and this tool throws it away.** Colour and annotation-origin are
the draughtsman's and the reviewer's own layering. Proven on Parcel 11 on 28 August: eleven colours,
and Omar's red isolated to a single value with no classifier involved — 5 shapes and 37 wall
segments, exactly his markup, separated from the architect's identical red by origin alone.

---

## 5. The simplification

Stop making it a modeller. Make it a faithful translator, in this order:

1. **One coordinate space, proved by a test.** Instrument which reader placed each shape, then
   collapse the five paths to one conversion. Nothing else in this list is safe until this is true.
2. **Read the scale off the sheet** and verify it against something else printed on the sheet
   (suite areas, a dimension string). Refuse rather than assume.
3. **Emit the drawing's own separators as layers** — colour, and markup-vs-page. Delete the
   size-based slab/column/beam classification from the default path entirely.
4. **The engineer names the layers once, and it is banked**, `pdf-layer.<job>.<colour>` in the same
   table as the ETABS rules, in the same shape as `slab-count.<job>.<storey>`.
5. **Refuse and name.** Everything not carried across is reported with position and reason, the way
   `CANDIDATE NOT MODELLED` already does on the other side.

Steps 3 and 5 are what Omar needed and what would have prevented both faults he found: he would have
opened three layers instead of a drawing, and nothing would have been placed by a reader that
disagreed with the others.

**Nothing further should be patched onto the current path.** The four fixes of 28–29 August each
repaired one symptom of §3a and §3b and left the mechanism intact, which is exactly the shape of
failure rule 10 was written about.

---

## What is already true and worth keeping

- `ColourIsTheSelectorMeasurement` — the colour census, and the evidence for §5.3.
- `TheScaleIsPrintedOnTheSheetMeasurement` — the scale check, and the evidence for §5.2.
- `PageContentVsAnnotationsMeasurement` — what the annotations-only filter throws away.
- `WhereTheMarkupLandedMeasurement` — the displaced-markup detector; it is the harness §5.1 needs
  and it currently FAILS on five shapes, which is the right state for it to be in.
- `DxfExporter(layerByColour: true)` — §5.3's exporter, already written and already correct.
