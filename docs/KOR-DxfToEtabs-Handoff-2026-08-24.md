# DXF → ETABS — state at 2026-08-24, and how 31168 is built

Supersedes the 2026-08-21 handoff. Read this before touching the module.

## What is in Andrea's folder right now

`\\Kor-fs01\Projects\Projects\03 Residential\31168-01 (YMCA Langara Vancouver)\02 Engineering\02 Lateral Design\01 ETABS Models\`

| file | what it is |
|---|---|
| `31168-FROM-DRAWINGS.e2k` | **15 storeys · 386 walls · 763 columns · 15 floors.** No tower storeys. Every storey has a plate. Thickest wall 42". **Zero members that did not come from the drawings.** |
| `31168-FROM-DRAWINGS-report.txt` | sheets placed and not, every flag, every rule applied with its authority |
| `31168-QUESTIONS-for-Andrea.xlsx` | **2 questions**, the rest DECIDED |
| `KOR-31168-SUMMARY.pdf` | one page, regenerated with the model |
| `31168-reference-SHELL.e2k` | **new, and load-bearing — see below** |
| `KOR-Model-From-Drawings-READ-THIS-FIRST.pdf` | general explainer; its 31168 numbers are stale (says 62 sheets placed) |

The stale `KOR-Model-From-Drawings-DOSSIER.pdf` was removed 21 Aug (backed up in the session
scratchpad). It claimed 1,119 walls and 2,462 columns — a model that does not exist.

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
- **`-InferFloors`** — flood fill now recovers Level 1, the mezzanine and both ground floors, but
  **C-LEVEL 3 still needs this**, or it stands with 45 columns and 21 walls and no plate.

Rebuilding the shell if it is ever lost: strip `POINT COORDINATES`, `LINE/AREA CONNECTIVITIES`, all
three `ASSIGNS` sections and `PIER/SPANDREL NAMES` from the reference, keep everything else.

## What Andrea still needs to answer — two things, and only two

1. **C-ROOF carries a plate with no wall or column beneath it.** Does the structure stop below, or
   is it on a sheet we did not place?
2. **Four storeys took their plate from the storey below** (`--infer-floors`). Are the edges right?

Everything else in the workbook is DECIDED with the reasoning attached. The 18 questions about
2–4" linework are gone: the flag now carries implied thickness, and under 6" it is drafting
scratch, not concrete, so it is answered rather than asked.

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

## The habit that catches this class of fault

Render every storey and look at it before anything leaves the machine. Counts cannot see eight
storeys of the wrong building, a 132" wall, or a floor borrowed from under a different tower.
`tools\Render-E2kModel.ps1 -E2k <file> -OutPng <png> -Storey "<name>"`. Rendering all fifteen takes
longer than ten minutes, so run it in the background or per storey.
