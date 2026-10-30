# CODEX — Halifax v4 adversarial audit 2 of 3: WALL OPENINGS AND SLAB EDGES

Reading set: ~50 KB of source (measured at handover; see the table). Reasoning: medium. Expected time: under 20 minutes.
Repo: `C:\VIsual Studio Projects\KOR.Drafter`, commit `<PACKAGE_COMMIT>` (filled in at handover). Read files AT THAT COMMIT.

## Paste this to Codex

You are auditing, adversarially, part of a pipeline that turns an engineer's marked-up PDF (drawn over an architect's
background) into ETABS and Revit models and a drawing set. Find ways it can be WRONG and still pass its own checks.
Assume the author is confident and mistaken. No praise, no summary, no refactors.

THE QUESTION: can a wall opening (a door or garage cut into a perimeter wall) or a slab edge reach ETABS, Revit or the
drawing differently from what the architect drew and the engineer asked for, while every check below still passes?

Ground truth you may rely on (measured by us):
- The architect's background has NO curves (flattened): a door swing is a run of short grey strokes. Openings are read
  from those strokes; the engineer's own red wall line may run straight across an opening he does not want.
- An opening splits a wall into segments for the models. The ETABS converter makes no header over an opening wider than
  72" (dxf.max-opening-span); the header step adds one: header depth = storey height minus the opening height.
- The engineer asked that "slab extends to edge of balconies". Balconies are read from the architect's background. A
  short step (under 600 mm) between parallel slab-edge segments that is not a balcony is a fault and is closed.
- Storeys L2-L21 take their slab edge from the Level 1 sheet's outline plus the balconies.

Open ONLY these files, ONLY these line ranges. Do not grep, list, search or open anything else. Do not build or run tests.
| file (tools/) | lines |
|---|---|
| arch_doors.py | all |
| manifest_add_wall_openings.py | all |
| e2k_header_depth.py | all |
| plan_balconies_from_arch.py | all |
| plan_slab_edge_check.py | all |

Hunt specifically for:
1. An opening placed on the wrong wall, at the wrong offset along it, or with the wrong width, when two walls are near
   each other, a wall is diagonal, or a swing's hinge sits near a wall corner.
2. Wall splitting that leaves a stub short enough to be read downstream as something else (a column, a pier), or that
   changes a segment nobody asked to split. Say what the stub becomes.
3. Header depth wrong when the opening height, the storey height or the slab thickness is missing, zero, or in other
   units; any default value that silently stands in for a missing one.
4. A header added where none was wanted, or one skipped, because the match between plan openings and ETABS areas is
   by position tolerance. Give the tolerance and the case it fails.
5. Slab edges: a real balcony removed as a "jog", a jog kept because it is just over the threshold, a balcony that is
   not closed (drawn in broken line) taken or dropped wrongly, or the bridge between plate and balcony still overrunning
   the balcony's ends.
6. Silent skips in any of the five files: an item dropped without being counted and reported.

For each finding give: file:line, the concrete input that triggers it, what reaches the model or drawing, which check
passes and why, and a severity (CRITICAL = a wrong opening or slab edge ships; MAJOR = possible on another job; MINOR).
If you find nothing in a category, write the category and "none found in the lines read", nothing more.

Write your answer to
`C:\VIsual Studio Projects\Operations\docs\codex\CODEX-HALIFAX-V4-AUDIT-2-OPENINGS-AND-SLAB-EDGES-RESPONSE.md`,
at most 80 lines. Do not edit any other file.
