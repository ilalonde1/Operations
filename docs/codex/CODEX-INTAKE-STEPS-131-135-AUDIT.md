# CODEX — audit intake steps 131, 133, 134 and 135

**Read-only.** Everything you need is on stdin: this brief, then the code. Do not ask for more files; if a question
cannot be answered from what is there, name the one function you would need and move on.

## Why

These four rules were banked on 2026-09-19/20 against a **six-set** gate — six drawing sets out of a 293-set
corpus. That gate can only say "nothing else moved on these six"; it cannot say a rule is right. One rule banked
the same way (step 131, ungated) cost another set 41 storeys and was found a day later. All four are in the
product now and have never been judged against the fifty sets the engineer has her own ETABS model of.

So: **where will each of these read something that is not a floor, or miss one that is?** Be concrete about
geometry. A finding I can test beats a caution I cannot.

## The four rules, as the code means them

1. **Step 131 — a stroke on the grid is a piece of the edge.** Strokes drawn along a grid axis, heavier than the
   grid's own pen, are offered to the slab-edge arrangement as *pieces* (bridged in line across a column's box,
   carried through a column) when they are 300 mm or longer; shorter ones are offered as drawn. The 300 mm gate
   exists because one sheet drew 1,380 dashes of 4 mm along its axes and they were bridged dash-to-dash into an
   arrangement the embedding refused.
2. **Step 133 — a jog lands on whatever line it reaches.** A short stroke (under the length gate) whose two ends
   each meet the END of a long line is that edge's jog and is kept as a line. It used to require both long lines
   to carry the jog's own pen; now the far end may be any long line of the same colour, while the jog's own pen
   must still match one of the two.
3. **Step 134 — an end's T is not spent by its bridge.** In the planar arrangement, an end that stops short of
   another edge's body (within the bridge) joins that body at the foot of its perpendicular, whether or not the
   same end also has a cheaper end-to-end bridge. The T is judged among T's alone; end-to-end bridges still need
   both ends to agree.
4. **Step 135 — a floor's worth of columns outside the walk's floor is another floor.** If the chain walk found a
   floor, the arrangement was skipped unless that floor held under half the page's columns. Now the arrangement
   is also built when four or more of the page's columns lie outside every floor the walk found; and a walk floor
   stands down only to an arrangement floor lying OVER it.

## Questions

1. For each rule, name the **drawing** that breaks it — a geometry, in coordinates or in plain description, that
   the rule reads wrongly. Say which of the other gates (minimum plate area, the invention share, two loose ends,
   a thickness call-out inside the ring, the neighbourhood/holds test) would still catch it, and if none would,
   say so plainly.
2. **Step 131's 300 mm**: what is drawn on a structural plan, along a grid line, heavier than the grid pen, and
   between 300 mm and a metre long, that is NOT a slab edge? (Think about what a structural drafter puts on a
   grid line.) Would it now be bridged into the outline?
3. **Step 133**: the jog's far end may now be a line of another pen. Name a case where the far line belongs to
   something else entirely — a dimension line, a leader, a hatch boundary — and the jog therefore welds the slab
   edge to it.
4. **Step 135's four columns**: a page's "columns" include anything the column reader read. What on a sheet is
   read as a column but is not one (a schedule's symbols, a legend, a north arrow), and can four of them off the
   plan force an arrangement that manufactures a floor?
5. **Step 134**: can the T rule now make a ring by joining an end to a body it should not — for instance a stair
   flight's line, a door swing, a dimension tick — and is anything stopping it?
6. Rank all findings by what they would cost in square feet on a real set, and say which ONE you would test first.

## How to answer

A numbered list, worst first. For each: the rule, the geometry, what survives it, and the cheapest experiment
that would prove or disprove it. If a rule looks sound to you, say so in one line — a clean verdict is useful, an
invented one is not.
