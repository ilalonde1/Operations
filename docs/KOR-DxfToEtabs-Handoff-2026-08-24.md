# DXF → ETABS — state at 2026-08-24, and how 31168 is built

Supersedes the 2026-08-21 handoff. Read this before touching the module.

## What is in Andrea's folder right now

`\\Kor-fs01\Projects\Projects\03 Residential\31168-01 (YMCA Langara Vancouver)\02 Engineering\02 Lateral Design\01 ETABS Models\`

| file | what it is |
|---|---|
| `31168-FROM-DRAWINGS.e2k` | **15 storeys · 349 walls · 763 columns · 14 floor plates across 15 floored storeys.** No tower storey. Every storey carrying members carries a floor. Thickest wall 42". **Zero members that did not come from the drawings.** |
| `31168-FROM-DRAWINGS-report.txt` | sheets placed and not, every flag, every rule applied with its authority |
| `31168-QUESTIONS-for-Andrea.xlsx` | **3 questions**, the rest DECIDED |
| `KOR-31168-SUMMARY.pdf` | one page, regenerated with the model |
| `31168-reference-SHELL.e2k` | **load-bearing — see below** |

**14 floor plates across 15 floored storeys is correct, not a missing floor.** The count is of
plate OBJECTS; C-LEVEL 3 borrows C-LEVEL 4's object, assigned a second time. Check floors per
storey, never by the total. That total fooled me for ten minutes on 24 Aug.

### Two explainer PDFs were withdrawn on 24 Aug

`KOR-Model-From-Drawings-DOSSIER.pdf` and `KOR-Model-From-Drawings-READ-THIS-FIRST.pdf` are gone
from that folder. Both described the old full-site model — **63 storeys, 1,119 walls, 2,462
columns, 82 plates** — beside a model with 15, 349, 763 and 14. The one-pager is the page an
engineer reads first, and it led with those numbers.

They survived because the publish script copied them in and checked their counts *afterwards*, so
every run printed "the model is fine; the document describing it is not" and left them there. The
check now reads the source in `docs/` **before anything is copied**, a source that fails takes any
stale copy out of the job folder with it, and `-SkipDossier` withdraws rather than merely declining
to refresh. **The publish exits 1 until someone rewrites `docs\KOR-DxfToEtabs-dossier.html` and the
one-pager for the current model.** That is the gate working, not a fault to route around.

## How to rebuild it — the exact command

```powershell
.\tools\Publish-EtabsModel.ps1 -Project 31168 `
    -Reference '31168-reference-SHELL.e2k' `
    -TopStorey 'C-ROOF' `
    -DropStoreys 'LEVEL 3','LEVEL 4','LEVEL 5','LEVEL 6','LEVEL 7','LEVEL 8','LEVEL 9','LEVEL 10' `
    -InferFloors
```

Every argument is load-bearing:

- **`-Reference '31168-reference-SHELL.e2k'`** — the shell is `31168-reference.e2k` with its members
  stripped and its storeys, grids, materials and sections kept. Without it the tool merges the
  CSiXRevit export's own members into the output, and four of them — `W18/W21/W24/W27`, 36" thick,
  a 466×732 rectangle on C-LEVEL 3/4/5 — are what Andrea circled in ETABS saying "these are not
  walls". They are not towers and not ours; they are the Revit export's. Built against the shell,
  **everything in the model came from the drawings.**
- **`-DropStoreys`** — `-TopStorey C-ROOF` alone does NOT remove the towers. Tower `LEVEL 3`–`LEVEL 10`
  carry no prefix and sit BELOW the mid-rise roof, so an elevation cut keeps all eight. That is what
  shipped to her on 21 Aug.
- **`-InferFloors`** — flood fill now recovers Level 1, the mezzanine and both ground floors, so this
  reaches exactly one storey: **C-LEVEL 3**, which otherwise stands with 54 members and no plate.

Rebuilding the shell if it is ever lost: strip `POINT COORDINATES`, `LINE/AREA CONNECTIVITIES`, all
three `ASSIGNS` sections and `PIER/SPANDREL NAMES` from the reference, keep everything else.

## What Andrea has to answer — three things

1. **C-ROOF carries a plate with no wall or column beneath it.** Does the structure stop below, or
   is it on a sheet we did not place?
2. **C-LEVEL 3 took its plate from C-LEVEL 4** (`-InferFloors`). Are the edges right?
3. **LEVEL 2 and LEVEL 1 MEZZ have a floor that stops well short of their structure** — new on
   24 Aug, see below. Is that the building, or a slab edge that failed to close?

Everything else in the workbook is DECIDED with the reasoning attached.

## The floor that does not reach the structure — found 24 Aug

Measured per storey as the fraction of the ground a storey's own members cover that its plate
reaches:

| storey | members | member extent | plate extent | covered |
|---|---|---|---|---|
| LEVEL 2 | 101 | 279 × 206 ft | 296 × 96 ft | **43%** |
| LEVEL 1 MEZZ | 96 | 281 × 206 ft | 94 × 26 ft | **4%** |
| every other storey | — | — | — | 99–100% |

LEVEL 2's plate is a 16-joint outline that pinches to a near-point at x ≈ 27 ft — two wings meeting
at a waist about 0.8 ft wide — and renders as an hourglass. Either the podium's real shape, or a
slab edge that closed through itself. A self-touching area is not a good thing to hand ETABS either
way.

**Reported, not repaired**, and that is deliberate: a mezzanine over part of a room and a slab edge
that failed to close produce the same model. `ModelQuestionnaire` J5 asks her which.

Until this, "does the storey have a plate" was the only question asked, and both storeys answered
yes. It took rendering the mezzanine and looking at it.

## Two things to tell her before she finds them

- **Plate edges are traced off a raster** where the slab edge would not close, so they come out
  slightly stepped rather than crisp. She may want to clean the outline.
- **33% of generated walls are over 24", thickest 42".** Her own walls run 10–16" plus a 36" core.
  `JobCalibration` names anything above the job's own p90 in the report, with locations. It flags;
  it does not fix.

## What is measured, and what is not

- **31065** — never seen before, valid benchmark: 94–95% of her wall length on L2/L3/L10/L15,
  **1,077 of 1,097 columns within 6", median residual 0.0"**.
- **31138** — valid benchmark: 0 drawn members read and then lost.
- **31104 — retired as a benchmark.** Its `.e2k` is a different revision of the building. We read
  its drawings correctly, proven by rendering our extraction over the sheet. Do not re-litigate.
- **Autodesk samples** prove the pipeline is portable — Revit → bridge → DXF → `.e2k` with nothing
  typed — but cannot measure accuracy: they are beam-framed and this tool does not model beams.
- **Never opened in ETABS since the shell rebuild.** No ETABS on this machine; KOR-210 has it.

## The habit that catches this class of fault

Render every storey and look at it before anything leaves the machine. Counts cannot see eight
storeys of the wrong building, a 132" wall, a floor borrowed from under a different tower, or a
mezzanine whose slab reaches 4% of its own columns. Every one of those passed every count in the
report, and every one was found by looking at a picture.

`tools\Render-E2kModel.ps1 -E2k <file> -OutPng <png> -Storey "<name>"`. Rendering all fifteen takes
longer than ten minutes, so run it in the background or per storey.

The same rule applies to the documents: read the shipped PDF as text, and check a gate runs *before*
the thing it gates, not after.
