# CODEX-AFTER-THE-FOUR-FIXES - RESPONSE

Scope followed: no `dotnet build`, no `dotnet test`, no publish, no commit, no writes to `\\Kor-fs01`. Share access was read-only and limited to named published/reference artifacts.

## Four Change Verdicts

1. `ModelDoubleHeightMembersOnBothFloors()` stack-fill: holds on the shipped artifacts I measured. I checked 31168, 31168-TOWERS and 31138 for rounded-position stack collisions, same-row overwrites, one-row refill on rerun, and copied-assign section mismatch. Results: 0 rounded collisions, 0 same-row collisions, 0 rerun additions, 0 section mismatches in all three files.

2. Carry-down support: does not fully hold. The one-storey cap and site-model guard hold in the final files, and simulating the method again adds 0 assigns on 31168, 31168-TOWERS and 31138. The support predicate itself is still too weak: it checks only `pts[0]`, and 31138 has two generated walls where that first point is over the floor below but the other end is not.

3. `max(6, thickness/2)` for already-modelled walls: no confirmed false-drop found in this pass. Direction is correct: the rule can only skip more generated walls, and 31138 is the job to watch because the report says 353 walls were skipped as already modelled. I verified the KW164 measurement separately: the reclassification that it is a partial overlap, not a whole duplicate, is correct.

4. Diagonals and KW164 reclassification: KW164 holds; the diagonal/thin-wall argument does not fully hold. Current reports contain unresolved `JBP_V-WALL` outlines with implied thickness 4.3 in, so "every failing outline is below the 4 in minimum" is false in the shipped 31168 artifacts.

## Findings

### BLOCKING

None new in this pass.

### SERIOUS

1. Carry-down support judges a wall by its first plan point only, and 31138 has two generated walls where that says "supported" while the other end is off the supporting floor.

File: `Kor.Operations.EngineeringTools.Core/Dxf/E2kDocument.cs:1423`, `Kor.Operations.EngineeringTools.Core/Dxf/E2kDocument.cs:1426`, `\\Kor-fs01\Projects\Projects\03 Residential\31138-01 (2170 W 1st Ave Vancouver BC)\02 Engineering\02 Lateral Design\01 ETABS Models\31138-FROM-DRAWINGS.e2k:3534`, `31138-FROM-DRAWINGS.e2k:3584`, `31138-FROM-DRAWINGS.e2k:9198`, `31138-FROM-DRAWINGS.e2k:9250`.

Triggering input: current 31138 generated walls `KW156` and `KW204`. `KW156` is assigned to `L02`; the support row below is `Mezz`, with floors `F1` and `F5`. Its first endpoint `KP292` at `(230.638, -1388.6833)` is inside those floors, but `KP293` at `(230.638, -1406.6833)` is outside them, on the edge to 0.0003 in. `KW204` is assigned to `L03`; the support row below is `L02`, with floors `F132` and `F133`. Its first endpoint `KP190` at `(654.6407, -899.6835)` is inside those floors, but `KP191` at `(758.6407, -899.6835)` is outside, 100 in from the nearest of those floor rings.

Wrong output: `Supported(row, obj)` returns true if `Inside(pts[0], ring)` is true for any floor ring. A long generated wall can therefore be treated as standing on a floor when only one end, or only the first duplicated endpoint of its panel footprint, is over the plate below. Measured on the reference jobs: 0 such cases in 31168, 0 in 31168-TOWERS, 2 in 31138.

2. The diagonal/thin-wall reclassification evidence is false for current 31168: five unresolved structural wall outlines are reported at 4.3 in implied thickness, above the 4 in minimum.

File: `\\Kor-fs01\Projects\Projects\03 Residential\31168-01 (YMCA Langara Vancouver)\02 Engineering\02 Lateral Design\01 ETABS Models\31168-FROM-DRAWINGS-report.txt:247`, `31168-FROM-DRAWINGS-report.txt:252`, `31168-FROM-DRAWINGS-report.txt:263`, `31168-FROM-DRAWINGS-report.txt:270`, `31168-FROM-DRAWINGS-report.txt:274`, `Kor.Operations.EngineeringTools.Core/Dxf/WallOutlineDecomposer.cs:78`, `Kor.Operations.EngineeringTools.Core/Dxf/StructuralPlanClassifier.cs:2254`, `Kor.Operations.EngineeringTools.Core/Dxf/StructuralPlanClassifier.cs:2258`.

Triggering input: the current 31168 reports. The 31 unresolved `JBP_V-WALL`/`JBP_B-WALL` rows range from 3.1 to 4.3 in implied thickness; five `JBP_V-WALL` rows are 4.3 in. The same five are present in `31168-TOWERS-FROM-DRAWINGS-report.txt`. 31138 has two comparable unresolved wall-outline rows, both under 1 in.

Wrong output: the status argument says the diagonals are closed because every failing outline is 2.0-3.8 in against `dxf.min-wall-thickness = 4`. That is not what the shipped report says. The above-threshold outlines still may be non-structural for a different reason, but the closed conclusion is not supported by the measurement it cites.

### MINOR

None new in this pass.

## LEADs

- I could not prove a false drop from `max(6, thickness/2)` without replaying the composer against the drawings and exposing each skipped wall candidate. The shipped 31138 report only gives the aggregate: 353 wall(s) and 391 column(s) skipped as already modelled.
- The stack-fill textual copy looks acceptable in the final models: copied assign runs kept the same section as the adjoining stack and rerunning the algorithm would add nothing. I did not find a case where a copied pier, section, mesh flag or restraint was wrong.
- The rounded stack key looks safe in the final models: no generated `KW`/`KC` members in 31168, 31168-TOWERS or 31138 shared the same one-decimal plan key with different exact geometry or collided on the same storey row.
- Prior audit findings still open and not repeated here: pre-build reach fallback rules and share-missing tests returning green.
