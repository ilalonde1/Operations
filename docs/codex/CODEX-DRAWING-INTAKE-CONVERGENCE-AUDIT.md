# Codex — adversarial audit: converging every drawing intake

## Why you are being asked

Five tools in this repo read a structural drawing. They were built separately, they duplicate each
other, and one of them keeps producing wrong output that reaches engineers. I have written a plan to
converge them (`docs/codex/CODEX-PDF-TO-DXF-WHOLE-SYSTEM-AUDIT.md`) and I am about to act on it.

**Two things I need from you, and the second matters more than the first.**

1. **Attack the convergence plan.** I wrote it; I am the wrong person to find its holes.
2. **Find what would REGRESS.** Convergence means more callers depend on shared code, so a latent
   defect in a shared reader gets amplified instead of contained. The requirement is zero regression.

I have been wrong three times in a row on the coordinate bug in §4 below, each time by reasoning
from my own model of the code. Do not adopt my model. Read the code.

---

## The systems

| tool | source | how it decides what a shape is | files |
|---|---|---|---|
| Revit → DXF bridge | Revit | Revit category → layer | `../KOR.Drafter/` (`exportdxf`, setup `JBP_STANDARD CAD EXPORT`) |
| DXF → ETABS | DXF | the layer; patterns held as rules | `Kor.Operations.EngineeringTools.Core/Dxf/` |
| Slab takeoff | PDF | native vectors + title-block scale | `Core/VectorPageReader.cs`, `Core/SheetScaleReader.cs`, `Core/SlabTakeoffEngine.cs` |
| Rebar takeoff | PDF | positioned call-out text | `Core/RebarPdfReader.cs`, `Core/RebarChangeService.cs` |
| PdfToSafe (PDF → CSI/SAFE, and now DXF) | PDF | **a size threshold** | `Kor.Operations.App/EngineeringTools/PdfToSafe/` |

Rules live in `KorStandards` on `KOR-APP01\SQLEXPRESS`, schema `analysis`, read by
`Core/Dxf/RuleSettings.cs`. Migrations are in `../KOR.Drafter/db/`.

---

## The plan you are attacking

The DXF is already the meeting point — `Revit --exportdxf--> DXF --> StructuralPlanClassifier` is how
job 31168 is built today. So the claim is:

> PDF-to-DXF is not a new tool. It is a second front-end onto an intake that already exists.
> Emit a DXF whose LAYERS carry the drawing's own separation (colour, and markup-vs-page-content),
> and the existing classifier, rules, questionnaire and publish gate apply unchanged — because
> `dxf.slab-layer-patterns` is a rule and can name a colour layer as readily as `JBP_C_SLABEDG`.

And these get deleted rather than written:

- `PdfToSafe/PdfGeometryParser.cs` — the second PDF vector reader (`Core/VectorPageReader.cs` is the first)
- `PdfToSafe/GeometryFilterService.cs`'s size-based slab/column/beam classification, on the default path
- PdfToSafe's own scale input (`Core/SheetScaleReader.cs` already reads it off the title block)

PdfToSafe's SAFE/CSI writers stay. It is the intake that converges, not the outputs.

**Tell me where this is wrong.** Specifically:

- Is the DXF actually a lossless enough meeting point, or does routing PDF through it lose something
  the SAFE/CSI path needs (thickness call-outs, section hints, text annotations)?
- `Core/VectorPageReader.cs` says it is *deliberately* independent of PdfToSafe because "a takeoff
  must read the native drawing itself". I claim that premise expired when PdfToSafe started reading
  whole pages. Is there a second reason for the split that neither comment states?
- Does the DXF→ETABS classifier make assumptions that only hold for Revit-exported DXFs (layer
  naming, units, coordinate ranges, closed-ness of outlines) and would silently misread a
  PDF-derived one?

---

## The unsolved bug — the sharpest question

An engineer's Bluebeam markup on an architectural sheet comes through with **5 of 43 shapes placed
~60 m away from the rest**, off the edge of the drawing. Four are 0.46 m squares (his corner
columns), one is a 6.23 m line (his 20'-5" dimension). The other 38 are correct.

Reproduce with `Kor.Operations.App/EngineeringTools.Tests/PdfToSafe/WhereTheMarkupLandedMeasurement.cs`
(it currently reports the five, and is meant to). Test file: `~/Desktop/OAP-parcel11-arch-markup.pdf`.

`PdfGeometryParser.ParsePage` places annotations through **five** different readers:
`/Vertices`; the appearance stream's `re` operator mapped via `/BBox`; `/Rect`; `/L`; `/InkList` —
plus page content through PdfPig, which normalises to the page corner.

**Three hypotheses I tested and which did NOT fix it** (do not repeat them):

1. Threading `/Rect` into `ReadAppearanceGeometry` and mapping BBox→Rect per PDF 32000-1 §12.5.5.
   Spec-correct, changed nothing here. (Stashed, not committed.)
2. Offsetting annotations by the MediaBox origin. This page's MediaBox is
   `[-1728 -1296.12 1728 1296.12]` — origin at the sheet centre, every raw `/Rect` negative — but
   PdfPig appears to normalise already, so the conversion was a no-op.
3. Assuming it was one annotation subtype. The raw PDF holds 38 `Square`, 4 `Polygon`, 1 `Line`;
   the five strays do not map cleanly onto one subtype.

**What I want:** which reader placed each of the five, what coordinate space that reader returns,
and where it diverges from the other four. Evidence from the code and the file, not a theory.

Worth checking: whether `Core/VectorPageReader.cs` reads these same annotations correctly — if it
does, the convergence fixes this for free and that is a strong argument for the plan.

---

## Also worth your time

- **`Core/SheetScaleReader.cs` (90 lines) is about to get many more callers.** It reads the scale by
  POSITION in the title block. Where does it silently return the wrong scale rather than null? A
  wrong scale is a silent systematic error in every quantity and every exported coordinate — this
  repo already shipped a DXF 4% oversize because the scale was assumed.
- **Duplication I have NOT found.** I searched for PDF readers and scale readers. Look for repeated
  sheet-number parsing, unit handling, title-block region conventions, layer-name matching, and
  closed-ring/polygon logic across `Core/`, `Core/Dxf/` and `App/EngineeringTools/`.
- **`Core/Dxf/StructuralPlanClassifier.cs` is 2,294 lines** and its comments record four prior
  failures on the same balance. If the convergence puts PDF-derived geometry through it, what breaks?
- Unit safety: `Core.Tests/RulesTravelBetweenUnitsTests.cs` exists because three length rules were
  never converted between units. Does the PDF path have the same class of hole?

---

## Constraints

- **Do not build, do not run the test suite, do not publish anything.** Read and report.
- **No destructive operations.** No file deletion, no git operations that discard work, no schema changes.
- Do not modify `KorStandards`.
- Findings over prose: file, line, what is wrong, what it would produce that is wrong, and how you
  know. A finding I can check in thirty seconds is worth more than a paragraph of architecture.
- Rank by what would reach an engineer. Something that silently produces plausible-but-wrong geometry
  outranks something that throws.
- Where you disagree with the convergence plan, say so plainly and say what you would do instead.
- If a claim in `CODEX-PDF-TO-DXF-WHOLE-SYSTEM-AUDIT.md` is simply wrong, that is the most useful
  thing you can tell me.
