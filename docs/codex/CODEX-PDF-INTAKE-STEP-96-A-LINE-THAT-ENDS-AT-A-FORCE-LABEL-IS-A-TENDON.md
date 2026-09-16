# Codex — step 96: a line that ENDS at a force label is a tendon, and never a slab edge

Commit `0c496dfc` on `develop`. **Reasoning: low. Open ONLY the lines named; no grep, no listing, no build, no test
run, no drawings. Write the code and the test; I build, gate and commit. Answer in at most 20 lines: what you
changed, file and line, and the test's name.**

## The defect, measured (PdfIntake.md §101)
31130's typical-floor sheet is "CONCRETE OUTLINE PLANS & POST TENSION REINFORCING": the tendons are drawn ON the
outline sheet as long diagonals (17°–20°, 9–18 pt pens) ending in an arrowhead labelled with the force — "324 KIPS",
"108 KIPS". 18 of the page's 257 long lines carry a force word at an end; on 31168's tower plan 0 of 522. The
slab-edge pass unites the floor's cells and follows those diagonals, so the plate's south edge is the tendons
(rendered: a slanted quadrilateral). `TendonAnchors` (step 48) already names these lines tendons — but it runs AFTER
`GeometryFilterService.Classify`, which has no words, so the slab-edge pass never hears of them.
**Step 80's blunt attempt is on record as REJECTED (§86):** dropping every line within reach of a force label
emptied 31130's L3–L19 plates and fragmented 31202's L6, because the slab EDGE runs past the same labels (the
anchors sit on the edge). The rule must tell a line that points INTO a label from a line that passes BESIDE it.

## Read (about 14 KB)
1. `Kor.Operations.EngineeringTools.Core/Intake/TendonAnchors.cs` lines 1–60 — the force words (`DefaultForceWords`,
   extended by `PdfIntakeOptions.ForceWords`), `LabelReachHeights = 4`, `MinTendonLengthMm`, and how a label is
   matched to a line today.
2. `Kor.Operations.EngineeringTools.Core/PdfToSafe/SheetFurniture.cs` lines 186–230 (the `Set` record: `MatchLines`,
   `IsOnMatchLine`), 255–270 (`Scaled(factor)` into drawing mm) and 372–383 (`On(page, …)`: where `MatchLines(page)`
   is read from the page). Force labels go through the same door.
3. `Kor.Operations.EngineeringTools.Core/PdfToSafe/GeometryFilterService.cs` lines 1417–1441 (`EndsAtAnArrowhead`, the
   eligible lines, `WithoutTheRunsEndingAtAnArrowhead(eligible)`) and 1684–1700 (that helper: collinear runs joined
   by end and by `BridgesInLine`, cut whole when any piece ends at an arrowhead).
4. `Kor.Operations.EngineeringTools.Core.Tests/Intake/AFloorIsItsCellsUnitedTests.cs` lines 1–34 (the fixture helpers)
   and 100–123 (`ALineEndingAtAnArrowheadIsASectionCutNotAnEdge`, step 79's test — the shape yours follows).

## The rule to write
1. `SheetFurniture.Set` gains `ForceLabels`: `IReadOnlyList<(double X, double Y, double Reach)>` — for each page word
   whose text, trimmed of a trailing period, is one of the force words (case-insensitive) and whose nearest word
   to the left within two heights is a number: the point is the force word's centre, the reach is
   `TendonAnchors.LabelReachHeights` × the word's height. Read where `MatchLines(page)` is read; scaled with the set
   exactly as MatchLines are (X, Y and Reach by `factor`).
2. In `GeometryFilterService`, beside `EndsAtAnArrowhead`, `EndsAtAForceLabel(line)`: true when one END of the line
   lies within `Reach` of a label's point AND the label's point lies AHEAD of that end along the line's own
   direction — the angle between (end − other end) and (label − end) is 30° or less. A label beside the line (the
   slab edge running past an anchor) is off-axis and does not count. `WithoutTheRunsEndingAtAnArrowhead` cuts a run
   when any piece ends at an arrowhead OR at a force label (rename it if you like; keep one helper). Its early
   return must consider both lists.
3. A test, named for the rule, in a new file `Kor.Operations.EngineeringTools.Core.Tests/Intake/ALineThatEndsAtAForceLabelIsATendonTests.cs`:
   a rectangle of four edge lines (a floor), one diagonal tendon from a corner region to a point on the south edge
   with the words "324" and "KIPS" 15 pt beyond that end along the diagonal — the diagonal is not among the slab-edge
   candidates and the four edges are (the south edge passes the label 20 pt to the side: kept). The same page
   without the two words: the diagonal IS a candidate (so the test proves the rule, not the fixture). State in the
   summary WHAT IT COVERS and WHAT IT DOES NOT (a tendon drawn as a polyline that turns before its label; a force
   label at neither end; a label whose number is on the line's other side).

## Not asked
The arrowhead reader; `TendonAnchors.StandDownColumns`; PathFate reasons (reuse the section-cut reason the run cut
already uses); any file not named. Do not touch the six baselines — I re-bank after rendering.
