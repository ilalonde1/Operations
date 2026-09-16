# Codex review — step 89: a grid name is what the bubble says

Commit `4f194e8b` on `develop`. **Reasoning: low. Open ONLY the lines named; no grep, no listing, no build, no
test run. Answer in at most 20 lines.**

## Read (about 5 KB)
1. `Kor.Operations.EngineeringTools.Core/Dxf/GridAlignment.cs` lines 236–305 — `IsGridName` (1–3 letters or digits,
   or 4 with both; a hyphen or a point may join two parts; a prime, and a period after a numbered name, are
   trimmed), `GridNamesIn` (a bar joins the two names of one line; three or more behind bars is nothing),
   `NamedAxes` (each name pairs with the nearest grid-layer line end within reach).
2. `Kor.Operations.EngineeringTools.Core.Tests/Intake/ASheetSitsOnTheModelsGridByNameTests.cs` — the test
   `AGridNameIsWhatTheBubbleSays` only.

## The rule under review
The grammar above replaced "three characters at most". Measured on 294 sets it took 86 → 9 sets' refused grid
text down to words and dimensions ("PGNF", "1112", "F.B.").

## The one question
Give ONE text a drafter puts on a GRID layer that is NOT a grid name and that `IsGridName` now accepts — a
dimension ("3-6" for 3'-6"? "10.5"?), a note abbreviation, a detail mark — and say whether `NamedAxes` would
then write it as an axis (it must be within reach of a grid-line END). If the answer is "3-6" or "10.5", say
what one added condition refuses it without refusing "A1-5", "A.8", "0-11", "P2'", "MH14".

## Not asked
The fit by name (`SolveByName`); the GRIDS table writer; the six-set baselines; anything outside the lines named.
