# DXF → ETABS model generator

Builds an ETABS model from the concrete-outline DXF plans drafting already exports,
so an engineer starting a model does not enter the building's geometry by hand.

    takeoff dxf-to-etabs <dxfFolder> <reference.e2k> <out.e2k> [--bldg B] [--offset x,y] [--no-floors] [--report file.txt]

## What it does

1. Reads every `*.dxf` in the folder (LINE / ARC / LWPOLYLINE / POLYLINE).
2. Stitches the loose segments back into closed outlines — Revit's CAD export writes
   plan outlines as individual lines, not polylines.
3. Classifies them by layer:
   - `JBP_V-WALL` → wall panels. A rectangle is one wall; a ribbon tracing both faces
     of a core is split face-by-face, each pair becoming one wall with its centreline
     and true thickness.
   - `JBP_V_COL` → columns, with size from the footprint.
   - `JBP_C_SLABEDG*` → floor plates; rings inside another ring are openings.
4. Places each sheet on every storey its title covers — `LEVEL 29 PLAN (L29-35)` fills
   seven storeys, `A-LEVEL 28` goes to Tower A only, `LEVEL P2` to the parkade.
5. Merges the geometry into an `.e2k` **ETABS itself exported**, so storeys, grids,
   materials and design settings stay exactly as ETABS wrote them. The output differs
   from a known-good file only by the lines we added.

Generated objects are prefixed `K` (`KW1`, `KC1`, `KP1`) and their sections `KOR-`
(`KOR-W12`, `KOR-C24x24`), so everything the tool made is selectable in ETABS.

## Getting the reference .e2k

In ETABS, with the target model open: **File → Export → ETABS Text File (.e2k)**.
That file supplies the storey table and the exact dialect of the installed version.

## Reading the report

The report lists every sheet with the storeys it landed on and its wall/column/slab
counts, then two sections that matter more than the totals:

- **Not placed** — sheets whose level could not be matched. Usually a title that names
  no level, or a level the model does not have.
- **Flags** — outlines that needed judgement: edges that would not close, and footprints
  that could not be resolved into wall panels. These are locations to look at, not
  errors; nothing is invented to cover them.

## Tuning slab closure

`--bridge <inches>` sets how far apart two ends of a slab outline may be and still be
joined (default 6"). Widening it recovers plates whose edges are interrupted at doors
and step-downs; on 31168, 6" produced 148 floors and 18" produced 450. More is not
automatically better — a tolerance wide enough to leap a corridor will invent a plate —
so the default stays conservative and the knob is there to be used deliberately, with
the resulting shapes checked.

## Limits, stated plainly

- **Slab edges are the weak input.** Drafting interrupts them wherever other linework
  crosses, so a fair number will not close. Those are dropped and flagged rather than
  guessed at. `--no-floors` skips floors entirely when only the lateral system is wanted.
- **Thicknesses come from the drawing**, not from a schedule. A wall drawn at the wrong
  width models at the wrong width.
- **Materials** are borrowed from the reference model — every generated section uses one
  concrete material. Reassign per element in ETABS.
- **No engineering is implied.** Loads, masses, spectra, stiffness modifiers, piers,
  spandrels and meshing are the engineer's, untouched by this tool.
- Placement assumes drafting's layer names hold. They are the contract; if a project
  uses different layers, pass them through `PlanClassificationOptions`.

## Where the code lives

| Piece | File |
|---|---|
| DXF entity reader | `Kor.Operations.EngineeringTools.Core/Dxf/DxfPlanReader.cs` |
| Outline stitching | `Dxf/PlanLoopBuilder.cs` |
| Core/ribbon → wall panels | `Dxf/WallOutlineDecomposer.cs` |
| Layer rules and sizing | `Dxf/StructuralPlanClassifier.cs` |
| Sheet name → storeys | `Dxf/PlanSheetNaming.cs` |
| .e2k read/merge/write | `Dxf/E2kDocument.cs`, `Dxf/E2kGeometryComposer.cs` |
| Orchestration + report | `Dxf/DxfToEtabsService.cs` |
| CLI | `TakeoffCli/Program.cs` (`dxf-to-etabs`) |
| Tests | `Core.Tests/DxfToEtabsTests.cs` |

## Verified on

31168 YMCA Langara, 62 plans exported 2026-06-28: 59 sheets placed across 58 storeys —
1,090 walls, 3,243 columns, 148 floors. Wall and column outlines in that set close
exactly, so those are recovered without guesswork; slab edges needed bridging and are
flagged where they would not close.

## Where things live

One of each, no version trails.

| | |
|---|---|
| Dossier (client-facing) | `docs/KOR-DxfToEtabs-web.pdf` — regenerate to a fresh scratch filename, then copy over this one (Edge caches by HTML path) |
| Model renderer | `tools/Render-E2kModel.ps1 -E2k <file> -OutPng <png> -Title <text>` |
| Delivered per project | `…/01 ETABS Models/`: `*-FROM-DRAWINGS.e2k`, `*-report.txt`, `*-QUESTIONS-for-Andrea.xlsx`, `*-MODEL-VIEWS.png`, `KOR-Model-From-Drawings-DOSSIER.pdf`, `READ-ME-Andrea.md` |

The renderer is not optional decoration: nothing ships without being drawn and looked at first.
Counts cannot see a two-inch wall, a plate modelled twice, or a member silently dropped — all
three shipped once because only numbers were checked. `ModelIntegrityTests` now gates the rest.

## Knowledge

The ETABS format conventions and the engineer's rulings are banked in **KorStandards** (schema in
`KOR.Drafter/db/`), not in these comments. Facts go there so the next session inherits them
instead of re-deriving them.

## Publishing

    .\tools\Publish-EtabsModel.ps1 -Project 31168

One command: builds the CLI (which `dotnet test` does not), regenerates the model, report and
questionnaire, copies the current dossier, lists what shipped, and **exits non-zero if any
deliverable predates the source that built it**.

Publishing by hand is what made things stale — the model would be regenerated and the dossier left
behind quoting counts two rounds old. Staleness was never a discipline problem; it was a four-step
ritual performed from memory. Use the script.
