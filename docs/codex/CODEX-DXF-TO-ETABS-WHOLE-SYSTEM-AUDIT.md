# Codex brief — DXF→ETABS: audit the placement model as ONE system

## What I want

Not a bug list. **One coherent design** for the single question this tool keeps getting wrong —
*which storey does this member belong to, and whose building is it?* — and then a complete
enumeration of every place in the code that contradicts that design.

I have been fixing this one symptom at a time for four days. Each fix repairs one model and breaks
the other. That is the evidence that the rules are mutually inconsistent, not that any one of them
is wrong. **Do not propose another patch. Find the contradiction.**

Do not build or run anything. Read the code and reason. No file changes, no git operations.

## The system

`Kor.Operations.EngineeringTools.Core/Dxf/`

| File | Role |
|---|---|
| `DxfToEtabsService.cs` | orchestrator: read sheets → classify → place → compose → **cut** → report |
| `PlanSheetNaming.cs` | which storeys a sheet's title claims (`MatchStories`) |
| `E2kGeometryComposer.cs` | places geometry on storeys; `RisesTo` decides the storey a member rises to |
| `E2kDocument.cs` | the .e2k file; `FloorOfStorey`, `SameFloorTolerance`, the post-cut passes |
| `StructuralPlanClassifier.cs` | what a piece of linework IS (wall / column / slab) |
| `SheetSetGlossary.cs` | learns a drawing set's own shorthand (`WEST` = `BLDG A & B`) |

Two deliverables come from one composition:
- `31168-TOWERS-FROM-DRAWINGS.e2k` — the whole site, 63 storeys, three buildings.
- `31168-FROM-DRAWINGS.e2k` — building C only, 13 storeys, cut from the same composition.

**The load-bearing invariant:** the site is composed ONCE and the one-building model is cut from it
afterwards, so the smaller model is a subset of the larger *by construction*. On any storey that is
building C's alone, the two files must be **identical**. They are currently not.

## The job that breaks it (31168 YMCA Langara)

Three buildings on one shared parkade. The engineer's own reference model has ONE global storey
list, and it names storeys inconsistently — this is the root of everything:

```
C-ROOF, C-LEVEL 9 … C-LEVEL 3     building C, prefixed
A-LEVEL 36 … A-LEVEL 27           tower A, prefixed
B-LEVEL 41 … B-LEVEL 27           tower B, prefixed
LEVEL 26 … LEVEL 3                shared in NAME, but only the two towers stand there
LEVEL 2, LEVEL 1 MEZZ             genuinely shared, unprefixed
A-LEVEL 1, B-LEVEL 1              THE SHARED GROUND FLOOR, 1.67 in apart,
                                  named after two of the three buildings that stand on it.
                                  Building C has NO storey of its own at ground level.
LEVEL P1, LEVEL P2, LEVEL P3      the shared parkade, unprefixed
```

Storeys that are one physical floor sit 1.67 in to 60 in apart. Real storeys are ~110–157 in apart.

The drawings split by building at every level, including the parkade:
```
S2.14.1_1_LEVEL 2 PLAN - BLDG C          S2.15.1_1_LEVEL 2 PLAN - WEST (BLDG A & B)
S2.05.1_1_LEVEL P1 PLAN - BLDG C         S2.06.1_1_LEVEL P1 PLAN - WEST
S2.01_1_LEVEL P3 - FOUNDATION - BLDG C   S2.02_1_LEVEL P3 - FOUNDATION - BLDG A & B
```
…and ALSO draw each level undivided (`LEVEL P1 PLAN - CONCRETE OUTLINE`, no building named), and
ALSO carry uncropped Revit working views (`B-LEVEL 33.dxf`) that show every building at that
elevation while carrying one building's name in the title.

## The three rules that fight each other

Each is defensible alone. Together they are inconsistent. **This is what I want you to resolve.**

**1. `E2kGeometryComposer.RisesTo`** — a member drawn solid on sheet N belongs to storey N+1.
It must not rise onto another building's storey (tower B's headers once landed on a tower A storey
130 ft away). But building C's parkade columns stand on `LEVEL P1` and the floor above them is
`A-LEVEL 1`/`B-LEVEL 1` — named for other buildings — so refusing it makes them skip the ground
floor entirely and `LEVEL 1` ships as a plate with nothing under it.

**2. The building cut** (`DxfToEtabsService`, `TowerOnly`) — drops members whose sheet names
buildings that don't include this one. Depends entirely on rule 1 having placed them correctly.

**3. Parts-beat-whole supersede** (`DxfToEtabsService`) — where several sheets draw one storey, a
sheet stands down if others name a strict subset of its buildings and carry what it carries. Keyed
on FLOOR, which depends on `SameFloorTolerance` — which depends on what a storey height is.

And underneath all three: **"how far apart are two storeys that are really one floor?"** is computed
in two places with two different answers (`E2kDocument.SameFloorTolerance`,
`E2kGeometryComposer.HalfOfATypicalStorey`). Measured across the site the median gap is *half* a
storey, because interleaved towers put the same floor in the list twice — so half of that is a
quarter of a storey and floors stop grouping. Measured up one building's stack it is right. I fixed
this in one place and later in the other; verify they now agree and that nothing else measures it a
third way.

## Symptoms, with numbers

State as of commit `9f5a8c05` plus uncommitted work in `PlanSheetNaming.cs`,
`DxfToEtabsService.cs`, `ModelQuestionnaire.cs`. A `RisesTo` rewrite is in `git stash` — read it,
it is the attempt that fixes A and breaks B.

| Symptom | Detail |
|---|---|
| the two models disagree on building C's own storeys | site `C-ROOF` 33 walls / 56 columns; YMCA `C-ROOF` 3 / 8. Same for `C-LEVEL 9`: 40/89 vs 10/41. They are cut from one composition and must be identical. |
| site model: 8 storeys carry a floor plate with nothing under it | `A-LEVEL 33`, `A-LEVEL 34`, `B-LEVEL 30`, `B-LEVEL 31`, `B-LEVEL 32`… their members went onto the *other* tower's storey |
| with `RisesTo` reverted, YMCA `LEVEL 1` is empty | 0 walls, 0 columns under an 11,026 sq ft plate — the ground-floor naming problem above |
| seven parkade sheets were never placed at all | `MatchStories`' building-tag fallback only considered NUMBERED levels, never `LEVEL P1/P2/P3`, so every per-building parkade sheet reported "levels P1 match no storey — not placed". Only the undivided site-wide sheet landed, so building C's model stood on the whole site's parkade: 108 columns instead of 66. Fixed in the working tree — **verify the fix, and look for the same blind spot elsewhere** (roof sheets? foundation sheets? mezzanines?). |

## What I want back

1. **One statement of the placement model** — how "floor", "storey" and "building" relate, and the
   single rule for choosing a member's storey that satisfies every case above at once. Include the
   ground-floor case (`A-LEVEL 1`/`B-LEVEL 1` with no C storey) and the interleaved-tower case
   (`A-LEVEL 33` 37 in below `B-LEVEL 33`) — one rule must handle both.

2. **Every place the code contradicts it**, file and line, including ones I have not hit yet.
   Especially: any other path in `MatchStories` that handles numbered levels but not parkade
   levels, roofs, foundations or mezzanines; any second definition of a storey height; any rule
   that decides a member's building from GEOMETRY where the drawing already said whose it is.

3. **The order these must be applied in**, and which existing rules become redundant once the model
   is coherent. I suspect at least two of my geometric fallbacks exist only to compensate for the
   parkade sheets never being placed, and should be deleted rather than tuned.

4. **The invariants that would have caught each of these on day one**, phrased so they can be
   asserted against a finished `.e2k` — I have `docs/etabs-handoff/` for that, and a renderer
   (`plan_sheet.py`) that draws every storey on one sheet.

Rank everything by whether it can put wrong structure in front of a structural engineer without
anything in the report saying so. That failure mode has now happened four times.
