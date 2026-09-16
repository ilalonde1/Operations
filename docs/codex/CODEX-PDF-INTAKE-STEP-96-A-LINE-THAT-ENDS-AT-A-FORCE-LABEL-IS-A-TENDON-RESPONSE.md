# Codex response — step 96 (2026-09-16 09:50), and what the gate said

**Codex delivered** what the brief asked: force labels read into `SheetFurniture.Set.ForceLabels` (a force word whose
nearest left word is a number; reach = `TendonAnchors.LabelReachHeights` × height; scaled like match lines), forwarded
through `DrawingIntake` and `PdfPlanReader`; `EndsAtAForceLabel` in `GeometryFilterService` (label ahead of the end
within 30°); the run helper cuts a run at an arrowhead OR a force label; six tests in
`ALineThatEndsAtAForceLabelIsATendonTests` (red without the rule — proved by stashing it).

**The six-set gate said NO** (the first pass was on a stale binary and read "byte-identical"; rebuilt, it was not):
31130 L3–L13 plates 4,794 sq ft → NONE, L14 −3 columns; 31202 L6 19,670 → 1,815 + 1,224 + 601, L7–L12 982 → 601 + 584,
ROOF and L13 reshaped. §86's record of step 80's blunt attempt, repeated to the number.

**The mechanism, found this time:** a tendon's anchor sits ON the slab edge, so an edge PIECE ends where the tendon
ends, within reach of the same label and — a short piece at the anchor — inside 30° of it; and the run helper then
drops the WHOLE collinear edge for that one piece. The angle was never the discriminator; on the page it is the
pen: 31130's tendons 9/16/18 pt, its outline 2 pt; 31202's tendons 9/16 pt (with a 7×9 anchor fill at each end and
"Kips 270" beside it), its outline 2 pt — measured with `vector-lines --long/--pens`. Codex's code stays in the tree
uncommitted; the second brief re-specifies the cut on it. §109.
