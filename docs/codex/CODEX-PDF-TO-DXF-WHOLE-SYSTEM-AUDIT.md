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

## 1b. ⚠ WHAT THIS TOOL IS FOR — narrower than the rest of this document assumed

Ian, 29 August, after prompt 2 and before any more was built:

> "These tools all do slightly different things. For instance, this one is probably the simplest.
> It's just taking markups made on a Bluebeam file and being able to see just those — removing all
> the architectural crap."

**That is the job. Not "convert a PDF drawing to DXF" — "show me my markup without the drawing
underneath."** Everything below still holds about how a PDF should be READ; this is about what this
particular tool should hand back.

And the original design already did it. `GeometryFilterService.Classify(annotationsOnly: true)` is
the default, and its comment says so plainly: *"Bluebeam / PDF markup annotations ARE the structural
model. Page content is the architect's base drawing — skip it entirely."* That was right.

**I turned it off.** On 28 August, to serve the two architect items in Omar's sentence ("the tower
outline… and the balcony outline"), I read whole pages and shipped him every bed, kitchen, door swing
and title-block rule on the sheet. Then I answered the complaint with a README explaining which of
twelve layers to switch off, which is a note apologising for the wrong default rather than the right
one.

**So: markup-only is the DEFAULT, and page content is an explicit per-request opt-in.** That is the
same shape as everything else here — few foundational rules, flexibility per request, nothing banked.

### And the tools converge at the READING, not at the job

| tool | its job | what it hands back |
|---|---|---|
| Bluebeam markup → DXF | see the engineer's markup alone | the markup |
| Slab takeoff | quantities off a native drawing | numbers |
| Rebar takeoff | call-outs and a change report | a report |
| DXF → ETABS | a structural model | an `.e2k` |

What is shared is **how a page is read** — one coordinate space, one scale read off the title block,
one geometry reader. Converging the readers is right. Converging the tools into one thing that emits
everything and lets the engineer sort it out is what produced the complaint.

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

✅ **SOLVED, prompt 1, 29 August.** Codex's audit placed the five strays on the `/Vertices` and `/L`
readers, and a probe settled the mechanism: **PdfPig normalises everything it PARSES** — the boxes,
the page paths, and `Annotation.Rectangle`, all reading 0..3456 on this sheet — while values read
straight out of the annotation dictionary are left in the file's own space, which here starts at
−1728. `ParsePage` now derives the raw page origin once, from `/MediaBox` falling back to `/CropBox`,
and every raw reader subtracts it.

Three guesses failed before that, and the third is the useful one to remember: offsetting by
`page.MediaBox.Bounds` — which is right in substance and does nothing, because PdfPig reports 0
there. The hypothesis was never tested, only the wrong source for it. ⛔Do not take a page origin
from `Bounds`.

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

**The half of that lesson that does NOT transfer is the banking.** See §5's warning: what persists on
the ETABS side persists because the model is rebuilt, and nothing here is. The transferable half is
narrower and it is the whole point — *read what the drawing already separated, instead of deciding
for it*.

---

## 5. The simplification

Stop making it a modeller. Make it a faithful translator, in this order:

1. **One coordinate space, proved by a test.** Instrument which reader placed each shape, then
   collapse the five paths to one conversion. Nothing else in this list is safe until this is true.
2. **Read the scale off the sheet** and verify it against something else printed on the sheet
   (suite areas, a dimension string). Refuse rather than assume.
3. **Emit the drawing's own separators as layers** — colour, and markup-vs-page. Delete the
   size-based slab/column/beam classification from the default path entirely.
4. **Refuse and name.** Everything not carried across is reported with position and reason, the way
   `CANDIDATE NOT MODELLED` already does on the other side.

Steps 3 and 4 are what Omar needed and what would have prevented both faults he found: he would have
opened three layers instead of a drawing, and nothing would have been placed by a reader that
disagreed with the others.

### ⚠ AND NOT A FIFTH STEP THAT BANKS ANYTHING

The first draft of this list had one: *the engineer names the layers once and it is banked, as
`pdf-layer.<job>.<colour>`, in the same table as the ETABS rules.* Ian struck it, and he was right:

> "Didn't we move away from banking every decision and creating global rules? Didn't we pare that
> back to do the opposite — implement foundational company rules but make it flexible enough that it
> can take project specific questions and figure them out, as opposed to trying to shoehorn into a
> rule that doesn't apply or fit?"

**The ETABS pattern does not transfer, and copying it here is the same mistake in a new place.**
What makes a banked scoped fact right on that side is that Andrea's model is REBUILT — dozens of
times, and every regeneration would lose her answer if it were not written down. `slab-count.31168.
LEVEL 1 MEZZ = 3` has to persist because the thing it describes gets remade tomorrow.

A PDF conversion is not rebuilt. Omar asks once, opens the file, and the conversation is over. A
rule banked from it would outlive its own purpose and start applying to drawings nobody was talking
about — which is exactly how 73 prose rulings became global thresholds that fought each other.

**And most of it should never be a question at all.** The scale is PRINTED on the sheet. The markup
IS the annotation layer. The colours ARE the draughtsman's separation. Every one of those is
something to read, not something to ask about and store an answer to. The tool's job is to figure
out what the drawing already says.

So the shape here is:

- **Foundational, and few** — true of every drawing, no project needed: a page has one coordinate
  space; the scale is on the sheet and is checkable against the sheet; colour and annotation-origin
  are the separation the drawing was made with; nothing is invented that the drawing does not state.
- **Per request, and not stored** — "give me the red and the plate": answered by turning layers off,
  in the file, in the reply, or in the README beside it. Nothing about that belongs in a rules table.
- **Asked only where the drawing genuinely cannot say** — and then asked in the reply, with the
  answer applied to that request, not written into a table that will meet another job next month.

**Nothing further should be patched onto the current path.** The four fixes of 28–29 August each
repaired one symptom of §3a and §3b and left the mechanism intact, which is exactly the shape of
failure rule 10 was written about.

---

---

## 6. IT IS NOT A NEW TOOL. IT IS THE MISSING FIRST LEG OF ONE WE HAVE.

Added after Ian stopped the work a second time:

> "This is PDF to DXF. I want to make sure we've converged all of these tools — I can't help but
> think that's the way. The same we're doing with our Standard Details and Revit Tools — everything
> is converging. So instead of building a PDF to DXF in isolation — look at the Revit to DXF bridge
> we've already built and the rebar takeoff and PDF to CSI."

### What is already built (searched: `Kor.Operations.EngineeringTools.Core/`, `Core/Dxf/`, `App/EngineeringTools/PdfToSafe/`, `KOR.Drafter/`)

| tool | source | how it knows what a thing is | lives in |
|---|---|---|---|
| Revit → DXF bridge | Revit model | Revit category → layer | KOR.Drafter, `exportdxf`, setup `JBP_STANDARD CAD EXPORT` |
| DXF → ETABS | DXF | **the layer**, and the patterns are RULES (`dxf.wall-layer-patterns`) | `Core/Dxf/` |
| Slab takeoff | PDF | native vectors + the scale off the title block | `Core/VectorPageReader`, `Core/SheetScaleReader` |
| Rebar takeoff | PDF | positioned text call-outs | `Core/RebarPdfReader` |
| **PdfToSafe (PDF → CSI)** | PDF | **a size guess** | `App/EngineeringTools/PdfToSafe/` — its own everything |

**Four of the five defer to something the source already states. One guesses. It is the one that
keeps failing.**

### The duplication, and it is not subtle

1. **TWO PDF vector readers.** `Core/VectorPageReader` and `App/PdfToSafe/PdfGeometryParser`. The
   split is deliberate and documented in VectorPageReader's own summary — *"PdfToSafe reads the
   engineer's Bluebeam MARKUP annotations (it intentionally discards the base drawing); a takeoff
   must read the native drawing itself"* — and **that premise expired on 28 August**, when PdfToSafe
   started reading whole-page content to serve Omar. There is now no reason for two.

2. **`SheetScaleReader` already exists, is tested, and handles this exact failure.** Its summary
   names it: *"a metric set drawn 1:100 but measured at the imperial default 1/8"=1'-0" (1:96)
   under-prices every area by ~8%."* That is the error I shipped to Omar and then hard-coded around.
   The takeoff calls it. PdfToSafe re-typed the scale as a UI field and defaulted to 100.

3. **The repo has already learned this lesson once and written it down.** `RebarPdfReader`:
   *"Extracted from the overlay generator so the PDF markup and the change/weight report read the
   drawings through ONE pipeline — two extraction paths had two sheet-ownership rules and two
   tokenizers, and their reports disagreed on counts. The reports must tell one story."* It was
   applied inside the rebar tool and never across tools.

4. **PdfToSafe writes DXF; `Core/Dxf` reads DXF — with configurable layer patterns — and they never
   meet.** Everything the PDF side is missing (a classifier driven by what the drawing says, a rules
   table, a questionnaire, shipped-model invariants, a publish gate, 700-odd tests) exists on the
   other side of a file format neither uses to talk to the other.

### The convergence

The DXF is already the meeting point. This is how 31168 is built today:

```
Revit ──exportdxf──> DXF (layers) ──> StructuralPlanClassifier ──> .e2k, questions, invariants
```

So the answer to "build a PDF-to-DXF tool" is that **there is nothing to build downstream of the
DXF.** Point a second front-end at the same intake:

```
Revit ──exportdxf────┐
                     ├──> DXF (layers) ──> the existing classifier, rules, questions, gate
PDF  ──pdf2dxf───────┘
  │
  └──> VectorPageReader ──> slab & rebar takeoff        (text + geometry; no DXF in the way)
```

Two front-ends, one intake, one classifier, one rules table, one set of invariants.

And the layer patterns are already rules, so pointing the classifier at a PDF-derived DXF is a row
in `analysis.FormatConvention`, not a code change — `dxf.slab-layer-patterns` can name a colour
layer as readily as it names `JBP_C_SLABEDG`.

### What that makes of §5

Unchanged, but smaller. §5.1 (one coordinate space) and §5.2 (scale off the sheet) are **not
PdfToSafe work at all** — they are "use `VectorPageReader` and `SheetScaleReader`". §5.3 (emit the
drawing's own separators as layers) is the whole of the new front-end, and `DxfExporter(layerByColour)`
already does it. §5.4 (refuse and name) exists on the other side and comes free with the intake.

**What gets deleted, not written:** `PdfGeometryParser`'s second reader, `GeometryFilterService`'s
size-based slab/column/beam classification on the default path, and PdfToSafe's own scale field.

**What stays:** PdfToSafe's SAFE/CSI writers. Reading a marked-up PDF into an .f2k is a real and
separate job. It is the INTAKE that converges, not the outputs.

---

## ⚠ OWED TO OMAR, DEFERRED NOT DROPPED

He asked for three things and has one. The red markup is delivered and correct. **The tower outline
and the balcony outline are still owed**, and he confirmed on 29 August that he still wants them;
they are held until the app work below is finished, on Ian's instruction, so a one-off request does
not keep steering the architecture.

What is already established about them, so nobody starts from nothing:

- The **tower outline** is the outer edge of `PDF-F0C070` + `PDF-F0D080` + `PDF-F0D0A0` +
  `PDF-F0E0F0`. Those four fills tile the floor plate exactly — rendered and confirmed. One
  `BOUNDARY` click in AutoCAD. It needs the whole-page read, not the markup read.
- The **balconies** are the unfilled black boxes projecting outside that edge, on `PDF-000000`.
- ⛔A raster union algorithm was started for this and stopped: the fills already answer it. Do not
  rebuild it.

## What is already true and worth keeping

- `ColourIsTheSelectorMeasurement` — the colour census, and the evidence for §5.3.
- `TheScaleIsPrintedOnTheSheetMeasurement` — the scale check, and the evidence for §5.2.
- `PageContentVsAnnotationsMeasurement` — what the annotations-only filter throws away.
- `WhereTheMarkupLandedMeasurement` — the displaced-markup detector; it is the harness §5.1 needs
  and it currently FAILS on five shapes, which is the right state for it to be in.
- `DxfExporter(layerByColour: true)` — §5.3's exporter, already written and already correct.
