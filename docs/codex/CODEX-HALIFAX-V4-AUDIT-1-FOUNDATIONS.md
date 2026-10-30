# CODEX — Halifax v4 adversarial audit 1 of 3: FOUNDATIONS

Reading set: 105,745 bytes of source (measured 30 Oct at f02c52d). Reasoning: medium. Expected time: under 20 minutes.
Repo: `C:\VIsual Studio Projects\KOR.Drafter`, commit `f02c52d` (branch standard-details-rendered-image-store; the
working tree is at that commit). Read the files as they are in that tree.

## Paste this to Codex

You are auditing, adversarially, one part of a pipeline that turns an engineer's marked-up PDF into a Revit model, an
ETABS model and a drawing set. Your job is to find ways it can be WRONG and still pass its own checks. Assume the author
is confident and mistaken. Do not praise, do not summarise the code, do not propose refactors.

THE QUESTION: can a pad footing or a perimeter strip footing reach Revit (and so the drawing) at a different POSITION,
SIZE, TURN or COUNT than the engineer drew it, while every gate below still passes?

Ground truth you may rely on (measured by us, not for you to re-derive):
- The engineer draws each pad as a Bluebeam "Rectangle Sketch to Scale" polygon. Bluebeam stores the UNTURNED rectangle in
  /Vertices and the turn separately in /Rotation (and in the appearance stream's /Matrix).
- Revit's `--FTG PAD` family lays the type's WIDTH along its own X axis; the type is asked for with Length = the schedule's
  first dimension and Width = its second. A rectangular pad's request turns by the bearing of its Width edge.
- A combined pad (one drawn pad under two columns, mark F6) must be requested ONCE, at the drawn outline's centre. A single
  pad sits on its column.
- Perimeter strip footings are L-shaped: the strip's outer edge is flush with the wall's OUTSIDE face and its width runs
  inward. The outside is away from the P1 columns' centroid.
- The Revit bridge's `newfooting` verb honours `rotation_deg`, rotating about the footing's own insertion point.

Open ONLY these files, ONLY these line ranges. Do not grep, list, search or open anything else. Do not build or run tests.
| file (tools/) | lines |
|---|---|
| dump_annots.py | all |
| markup_to_plan.py | 780-1040 |
| plan_to_revit_requests.py | all |
| plan_footing_angles_check.py | all |
| plan_pad_footprint_check.py | all |
| plan_strip_footings_check.py | all |
| revit_audit_vs_plan.py | all |

Hunt specifically for:
1. A gate that compares our output with our own input (same frame, same function) so it agrees with a shared mistake.
   Name the shared assumption and the line where each side takes it.
2. Any place the family convention (Width along X) or the pad rule (one per drawn pad) is re-derived instead of taken
   from the one function that states it. A second copy is a future disagreement.
3. Turned squares vs rectangles: mod-90 vs mod-180 comparisons that let a rectangle pass at 90 degrees off.
4. Tolerances or slops (mm, degrees, % overlap) that are wide enough to pass a real error. Give the error they admit.
5. Silent skips: a pad, strip or column that is left out of a check (no match, no mark, a try/except, an early continue)
   without being counted and reported.
6. The strip "outside" rule failing on a concave or re-entrant perimeter, a wall whose midpoint is nearer the centroid
   than its ends, or a building with interior foundation walls.

For each finding give: file:line, the concrete input that triggers it, what Revit then builds, which gate passes and
why, and a severity (CRITICAL = a wrong footing ships; MAJOR = a wrong footing is possible on another job; MINOR).
If you find nothing in a category, write the category and "none found in the lines read", nothing more.

Write your answer to `C:\VIsual Studio Projects\Operations\docs\codex\CODEX-HALIFAX-V4-AUDIT-1-FOUNDATIONS-RESPONSE.md`,
at most 80 lines. Do not edit any other file.
