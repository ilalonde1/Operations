# Codex 14 of N — a path that draws nothing is not geometry

## Goal

Stop the classifier from emitting invisible paths as slabs, columns or lines. A vector path with
neither fill nor stroke draws nothing on the page — it is a clipping boundary or a construction
artefact — and today it passes every structural test and is written to the DXF as a floor plate.
This is the first change to what the intake emits since the ledger existed, so it is deliberately
small: one rule, one universal statement, one differential.

## What the ledger says

Step 1's ledger (`takeoff pdf-inventory … --scale`, commit 01c97622) prints, per set, a context
row "no-ink paths (clip or invisible) the classifier emitted as slabs, columns or lines":

    31130-01     518      31168-01   1,417      31138-01     986      31065-01   1,298      31202-01   1,697

On 31130 that is 518 of the 1,557 slabs the DXF carries — one in three. The differential against
31168's Revit DXF (`takeoff pdf-vs-dxf`) reads PDF slabs 781 against DXF slabs 45 over the 24
issued sheets; these paths are the bulk of the gap.

## The class, in one sentence

`GeometryFilterService.Classify` has an invisible-ink rule for paper-coloured FILLS
(`GeometryFilterService.cs:122-124`) and none for a path with no ink at all, so a stroke-less,
fill-less closed rectangle — the clipping box a CAD exporter draws around a viewport — reaches
the slab test, has a diagonal above the minimum, and becomes a floor plate.

## Measured premise

Every one of the 294 pages carries such paths (7,489 on 31130, 84 on its page 11 alone), and
none of them puts ink on the paper: `IsFilled == false && IsStroked == false`. Where the page's
own structure is concerned they carry no information the drawn geometry lacks — a viewport's clip
box outlines the plan region, which the drawn slab edge already does. A path that is a clip AND
is meant as structure does not exist in these five sets; if one ever does, the ledger will show
it as `NoInk` beside a drawn slab that is missing, and the rule can be revisited from evidence.

## Change this

In `GeometryFilterService.Classify`, immediately after the markup-only check and before the
furniture check, add one discard for content paths (never for annotation paths):

```csharp
// A PATH THAT DRAWS NOTHING IS NOT GEOMETRY. A closed path with neither fill nor stroke is a
// clipping boundary or a construction artefact; it puts no ink on the page. On 31130 it was
// 518 of the 1,557 "slabs" the DXF carried (2026-09-08).
if (!sub.IsAnnotation && !sub.IsFilled && !sub.IsStroked) { Fate(PathReason.NoInk); continue; }
```

Add `NoInk` to `PathReason` (Discarded in `PathFate.DispositionOf`), and to the reason
enumeration in `EveryPathHasExactlyOneFateTests`. Nothing else in `Classify` changes.

In `SheetInventory`, the context row "no-ink paths … the classifier emitted" stays and must read
0 on every set after this change; the primary row `paths: NoInk` appears in its place.

## What NOT to do

- Do not touch any other condition, threshold or order in `Classify`. One rule.
- Do not change `VectorPageReader`, the furniture, the grid, the schedule readers, or any WPF file.
- Do not change the DXF exporter. Fewer slabs is the intended difference; nothing else may differ.
- Repo only. No tests run; build Core and TakeoffCli once. I verify.
- Files: `GeometryFilterService.cs`, `Intake/PathFate.cs`, `Core.Tests/Intake/EveryPathHasExactlyOneFateTests.cs`,
  and one new test in `Core.Tests/Intake/` — nothing else.

## What I will check

1. **The differential moves in one direction.** On the thirteen baseline pages
   (`%LOCALAPPDATA%\Temp\kor-drawings\harness\step1-baseline\`), the new DXFs differ from the
   baseline ONLY by removed SLAB-layer polylines: same COLUMN count, same BEAM count, slab count
   down by exactly the page's no-ink-emitted number. Measured on 31130 p11 before the change: 17
   slabs, 84 no-ink paths, 6 of them emitted — so 17 → 11 if all six are slabs; the ledger's
   per-reason rows say which they are.
2. **The ledger.** `no-ink paths … emitted` = 0 on all five sets; `paths: NoInk` = 7,489 on 31130
   (its no-ink total); every other path row unchanged from §7 of `docs/PdfIntake.md`.
3. **`pdf-vs-dxf` on 31168**: PDF slabs fall from 781 towards the DXF's 45; columns unchanged on
   every sheet (equal on 8 of 24, within six on every single-view sheet, as before).
4. `FiveStickFilesTests` 20 of 25 unchanged in value (footings, marks, walls, coverage floors); the
   fate ratchet still holds. Fast suite green.
5. The overlay on 31130 p11 (`takeoff pdf-overlay … --scale 96`): the grey rectangles that were the
   notes-box clips and the viewport clip are gone; the footing outlines and core remain, and are
   the subject of the next brief.
