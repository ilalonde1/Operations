# Codex 17 of N — a plan is the sheet that says PLAN, and only a plan is taken off to a DXF

## Goal

Type every sheet from what the sheet itself says, and run the geometry classifier's OUTPUT — the
DXF, the differential — on plan sheets only. The record keeps every sheet's geometry; what changes
is that a section's cut-wall poché stops reaching ETABS as walls, and a plan whose bookmark is
missing stops being "other".

Implemented by the verifier on 2026-09-08 (Codex out of usage); this brief is the record of the
intent and the acceptance, written before the change.

## Measured premise (2026-09-08, the step-2b ledgers)

Geometry the classifier emitted, by the sheet type the record assigned:

    set      type                pages   walls   slabs   columns
    31130    plan                  28     274      46      789
             details               12     107     344       25
             section/elevation      4     121      18       23
    31138    other                 20     524      95      569
             plan                  19     454     107      531
    31202    section/elevation      5     304      27       18
    31168    plan                  38     901     398    1,236

Two faults in one table. On 31138, which carries no bookmarks, the 20 "other" pages are plans:
every one has a level in its title (P4, P3, P2, P1, 1, 2 … 22 — `SheetTitleReader` reads it),
their word PLAN sits at 82–87% of the page width, and the furniture rule's title-block strip starts
at 91%, so the region-text fallback never saw it. And on every set the classifier emits walls and
slabs from section and detail sheets — S3.01 on 31168 yields 36 "walls" that are cut walls drawn
in section — because nothing asks the sheet type before writing a DXF.

## The class, in one sentence

The record knows what kind of sheet it read and the geometry path does not ask; and the kind is
guessed from a region when the title reader has already parsed the sheet's own title.

## Change this

1. **`DrawingIntake`**: `SheetType = SheetTypeOf(title?.Raw ?? bookmark ?? titleBlockText)`. The
   sheet's own title first — `SheetTitleReader` anchors on the sheet-title PLAN or LEVEL token and
   keeps the raw text — then the bookmark, then the region's words. `SheetTypeOf` unchanged.
2. **`pdf-takeoff`**: a page whose type is not `plan` is reported (`  p  type  — not a plan sheet;
   no DXF written`) and skipped. It counts as neither written nor empty. `--pages` over a whole
   set therefore yields one DXF per plan sheet and a line per sheet that is not one.
3. **`PdfVersusDxf`**: a matched sheet whose PDF page is not a plan is listed but excluded from the
   totals ("sections and details are not compared"). The Revit set exports section views under
   the same sheet numbers, and comparing cut poché to cut poché says nothing about the intake.
4. **`SheetInventory`**: a context row "geometry emitted on a non-plan sheet (walls + columns +
   slabs)", Unaccounted when > 0, so the ledger says how much geometry the classifier produced
   where no DXF is written from it.
5. Nothing in `GeometryFilterService`, the readers, the WPF, or the rows.

## What I will check

- 31138: "other" falls from 20 pages to the pages that are genuinely other; plan rises to ≥ 39.
  Across the five sets, plan counts against the bookmark titles where bookmarks exist (31130 28,
  31168 38, 31065 34, 31202 31 typed plan today from bookmarks — must not fall).
- `pdf-takeoff --pages 1-60` on 31130 writes DXFs for the plan pages only; the thirteen banked
  pages are all plans and their DXFs stay byte-identical to step 2b's.
- `pdf-vs-dxf` on 31168: S3.01 listed and excluded; the walls total over compared sheets
  reported against the DXF's plan views only.
- The ledger's DOCUMENT totals unchanged (typing moves no path); the new context row non-zero on
  every set with section sheets.
- Fast suite, `FiveStickFilesTests`, Intake tests green.
