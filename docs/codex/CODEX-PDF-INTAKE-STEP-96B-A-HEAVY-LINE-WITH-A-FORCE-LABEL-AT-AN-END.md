# Codex — step 96, second cut: a HEAVY line with a force label at an end is a tendon, dropped alone

The working tree holds your step-96 change, uncommitted (`SheetFurniture.ForceLabels`, `EndsAtAForceLabel`, the run
helper, your six tests). **Reasoning: low. Open ONLY the lines named; no grep, no listing, no build, no test run.
Edit your own change in place; answer in at most 20 lines: what you changed, file and line, and the tests' names.**

## What the gate said (PdfIntake.md §109)
Your cut, at any reach and angle, empties 31130's plates and fragments 31202's L6 — exactly as step 80 did. The
mechanism: a tendon's anchor sits ON the slab edge, so a PIECE of the edge ends at the same label as the tendon and
can point into it; the run helper then drops the whole collinear edge for that one piece. The angle does not tell a
tendon from an edge piece. The pen does: 31130's tendons are 9, 16 and 18 pt against a 2 pt outline; 31202's are 16
and 9 pt against 2 pt. 31168 (no tendons, no labels) also draws 9–10 pt lines — so the pen alone is not the rule
either; the pen TOGETHER WITH a force label at an end is what no edge and no balcony line has.

## Read (about 6 KB, all in your own change)
1. `Kor.Operations.EngineeringTools.Core/PdfToSafe/GeometryFilterService.cs` lines 1380–1395 (`ForceLabelHalfAngleDegrees`,
   `ForceLabelCosSquared` — delete both and the triage line named below), 1430–1462 (`EndsAtAnArrowhead`,
   `EndsAtAForceLabel`, `eligible`, the call to the run helper) and 1705–1730 (the run helper).
   `result.LineWidths[i]` (parallel to `result.Lines`) is the pen of line i, in points.
2. `Kor.Operations.EngineeringTools.Core.Tests/Intake/ALineThatEndsAtAForceLabelIsATendonTests.cs` — your tests.
3. `Kor.Operations.EngineeringTools.Core.Tests/Intake/EveryReaderConstantIsTriagedTests.cs` line 76 — the triage line
   for `ForceLabelHalfAngleDegrees`; every `const double` in the readers needs one line here, classed. A new named
   constant of yours gets a line as `Tolerance("…")` or `Rule("…")` (not `Convention` — that class is capped).

## The rule to write, replacing the axis test
1. `IsATendon(i)`: line i has a force label within `3 × TendonAnchors.LabelReachHeights` label heights (the label's
   `Reach` × 3) of EITHER end, at any angle, AND `result.LineWidths[i] >= 3 × modalPen`, where `modalPen` is the most
   frequent pen (rounded to 0.5 pt) among the `eligible` lines of the page. Name the two factors as constants with
   triage lines: `TendonLabelReachFactor = 3` (Rule: "31130's labels stand 4.3–11 heights from the tendon ends;
   TendonAnchors' 4 is to the line's side") and `TendonPenOverOutline = 3` (Rule: "31130 9/16/18 pt over 2; 31202 9/16
   over 2; a pen under three times the outline's is the outline's own weight").
2. A tendon is removed from `eligible` ALONE — do not add it to the run helper's cut set and do not join it to a run.
   Restore the run helper to arrowheads only (its name and early return as they were before your change).
3. Delete `EndsAtAForceLabel` and the two angle constants.
4. Tests: rewrite yours to the new rule — (a) a 16 pt diagonal ending 8 heights from "324 KIPS" over four 2 pt edges:
   the diagonal is out, the four edges stay, INCLUDING an edge piece that ends at the same label (the anchor on the
   edge — the case that broke the first cut); (b) the same diagonal at 2 pt: kept (no pen); (c) the diagonal at 16 pt
   with no label: kept; (d) a 16 pt line with the label 20 heights away: kept. Keep the number-validation and the
   configured-unit tests. Summary: WHAT IT COVERS and WHAT IT DOES NOT (a tendon drawn with the outline's pen; a
   label at neither end).

## Not asked
The arrowhead reader; TendonAnchors; SheetFurniture (your label reading stands); any file not named.
