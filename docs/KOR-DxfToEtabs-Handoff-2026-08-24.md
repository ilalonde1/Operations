# DXF → ETABS — state at 2026-08-24, and how 31168 is built

Supersedes the 2026-08-21 handoff. Read this before touching the module.

## What is in Andrea's folder right now

`\\Kor-fs01\Projects\Projects\03 Residential\31168-01 (YMCA Langara Vancouver)\02 Engineering\02 Lateral Design\01 ETABS Models\`

| file | what it is |
|---|---|
| `31168-FROM-DRAWINGS.e2k` | **15 storeys · 349 walls · 763 columns · 15 floor plates, every storey floored.** No tower storey. Every storey carrying members carries a floor. Thickest wall 42". **Zero members that did not come from the drawings.** |
| `31168-FROM-DRAWINGS-report.txt` | sheets placed and not, every flag, every rule applied with its authority |
| `31168-QUESTIONS-for-Andrea.xlsx` | **3 questions**, the rest DECIDED |
| `KOR-31168-SUMMARY.pdf` | one page, regenerated with the model |
| `31168-reference-SHELL.e2k` | **load-bearing — see below** |

**Floor plate count can legitimately be lower than the storey count.** It counts plate OBJECTS,
and C-LEVEL 3 borrows C-LEVEL 4's, assigned a second time. Check floors per storey, never by the
total — that total fooled me for ten minutes on 24 Aug. The summary now prints "Storeys with a
floor" beside it whenever the two differ.

### Two explainer PDFs were withdrawn on 24 Aug

`KOR-Model-From-Drawings-DOSSIER.pdf` and `KOR-Model-From-Drawings-READ-THIS-FIRST.pdf` are gone
from that folder. Both described the old full-site model — **63 storeys, 1,119 walls, 2,462
columns, 82 plates** — beside a model with 15, 349, 763 and 15. The one-pager is the page an
engineer reads first, and it led with those numbers.

They survived because the old publish script copied them in and checked their counts *afterwards*, so
every run printed "the model is fine; the document describing it is not" and left them there. The
check now reads the source in `docs/` **before anything is copied**, a source that fails takes any
stale copy out of the job folder with it, and `-SkipDossier` withdraws rather than merely declining
to refresh. **The publish exits 1 until someone rewrites `docs\KOR-DxfToEtabs-dossier.html` and the
one-pager for the current model.** That is the gate working, not a fault to route around.

## How to rebuild it — the exact command

```powershell
takeoff publish 31168 `
    --reference '31168-reference-SHELL.e2k' `
    --top-storey 'C-ROOF' `
    --drop-storeys 'LEVEL 3,LEVEL 4,LEVEL 5,LEVEL 6,LEVEL 7,LEVEL 8,LEVEL 9,LEVEL 10' `
    --infer-floors `
    --land
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
3. **LEVEL 2 and LEVEL 1 MEZZ have a floor that stops well short of their structure.** Is that the
   building — a mezzanine over part of a room, a podium ending where the tower starts — or did a
   slab edge fail to close? This is NOT the hourglass, which is fixed; it is the separate question
   of how far the podium reaches.

Everything else in the workbook is DECIDED with the reasoning attached.

## The floor that does not reach the structure — found 24 Aug

Measured per storey as the fraction of the ground a storey's own members cover that its plate
reaches:

| storey | members | member extent | plate extent | covered |
|---|---|---|---|---|
| LEVEL 2 | 101 | 279 × 206 ft | 296 × 96 ft | **43%** |
| LEVEL 1 MEZZ | 96 | 281 × 206 ft | 94 × 26 ft | **4%** |
| every other storey | — | — | — | 99–100% |

Until this, "does the storey have a plate" was the only question asked, and both storeys answered
yes. It took rendering the mezzanine and looking at it. `ModelQuestionnaire` J5 asks her which it
is — a mezzanine over part of a room and a slab edge that failed to close produce the same model,
and only she can say.

### LEVEL 2's hourglass — FIXED 24 Aug, do not re-litigate

The 43% above is what remains AFTER the real defect was fixed, and the two are separate things.

LEVEL 2 arrived as ONE 16-joint ring, 296 × 96 ft, whose own edges met at (26, 248) ft with a gap
of exactly 0.00. Sensible area, sensible bounding box, an hourglass in the model. It is two podium
wings, now **two plates of 12,380 and 12,271 sq ft**.

Three wrong guesses got there, each caught by measuring the shipped model rather than by a test:

1. Split where the ring walk **revisits a node** — the wings share no vertex, so nothing happened.
2. Split where two edges **cross** — they do not; `u = 1.0000000113`, an endpoint landing ON another
   edge. A T-touch, not a crossing.
3. Split on a self-touch — worked, and left one lobe still meeting itself, because a 2-inch
   **hairline spur** produces a lobe with no area and the code gave up instead of cleaning it.

`LoopGeometry.SplitSelfCrossings` handles all three. **ETABS Check Model raised nothing about
LEVEL 2**, which is better evidence than anything measurable here.

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
## Opened in ETABS — 24 Aug, ETABS 22.6.0 on KOR-210

It imports and reads as a building. `Analyze → Check Model` was run twice.

**First run: 8 warnings.** Five were pairs of joints "too close" — 0.0005 to 0.0042 inches, four
thousandths — where two sheets reached the same corner and the 1/1000-inch quantisation grid
rounded them either side of a cell boundary. Those were walls that should have been connected and
were not. `PointAt` now reuses the nearest joint within 1/20 inch. Joints fell 3,461 → 3,456,
exactly the five ETABS named.

**Second run: 3 warnings, all the same kind, and all benign:**

| storey | member joints ON the slab edge | mesh reduction |
|---|---|---|
| LEVEL P1 | 9 | 68.50 ft² (0.090%) |
| LEVEL P2 | 6 | 36.30 ft² (0.048%) |
| LEVEL P3 | 4 | 23.41 ft² (0.031%) |

Roughly 6–8 ft² per joint, monotonic with the count. Where a perimeter wall ends exactly on the
slab edge, ETABS subdivides the plate there and drops a sliver. There are **no openings** on those
storeys, so it is not openings being cut. Walls terminating on the slab boundary is correct
modelling; this is the mesher's arithmetic, not a hole in the floor. Not fixable from our side
without moving walls off the edge, which would be wrong. **Say it to the engineer rather than
chase it.**

Check Model raised **nothing** about LEVEL 2 — the two split podium plates passed ETABS's own
geometry check, which is stronger evidence than anything measurable from the text file.

## The habit that catches this class of fault

Render every storey and look at it before anything leaves the machine. Counts cannot see eight
storeys of the wrong building, a 132" wall, a floor borrowed from under a different tower, or a
mezzanine whose slab reaches 4% of its own columns. Every one of those passed every count in the
report, and every one was found by looking at a picture.

`tools\Render-E2kModel.ps1 -E2k <file> -OutPng <png> -Storey "<name>"`. Rendering all fifteen takes
longer than ten minutes, so run it in the background or per storey.

The same rule applies to the documents: read the shipped PDF as text, and check a gate runs *before*
the thing it gates, not after.
