# Step 170 — the slab edge's short runs are part of the edge

## Written 2026-10-21, after three views of 31065-01 were traced and before the gate below has judged the rule.

**Knob:** `KOR_STEP170_OFF=1` restores the 2 m floor judged per fragment and the 1 mm in-line tolerance (`GeometryFilterService`, slab pass).

**Target set:** 1 witness set, 31065-01 (3 views: S2.17.1 L7..L17 south tower, S2.16.1 L5 south tower, S2.09.1 L5 north tower).
How many of the 56 corpus sets with the engineer's model it touches is NOT yet known; the gate below measures it, and
this line is corrected with the figure when it has.

## The witness

Run against Rory's Revit volumes, 31065-01 read 78% of the slab. Per level, the even tower floors were right and the odd
ones (L7..L17) carried only the south tower's 671 sq ft core; L5 had no plate in either tower. The drawings are clean:
every one of the three views draws its outline closed. Traced (`pdf-overlay --walls`, an arrangement dump, a flood of it):

1. **Fragments judged alone.** `PlanLoopBuilder` walks each seed forward only (audit F11), so one drawn run comes out
   in pieces by arrival order; the slab pass then dropped every open chain under 2 m (`SlabEdgeChainMinMm`). S2.17.1's
   west edge round two balcony boxes came out as 1,977 / 1,240 / 1,278 mm runs - all dropped.
2. **In line within a millimetre.** The same view resumes its edge 4 mm off the line it stopped on (0.04 mm on the
   paper), 76 and 177 mm on. `BridgesInLine` judged them two lines at a 1 mm tolerance.
3. **A run walked up and back.** S2.16.1 draws 1.35 m of its west edge twice (0.96 and 0.6 pt over each other); the walk
   went up one and down the other into a balcony box, and the ring, simplified, kept the box and dropped the stretch.

| view | before | with step 170 |
|---|---:|---:|
| S2.17.1 odd floors, south tower | 671 sq ft (the core) | 6,934 sq ft (even floor beside it: 6,982) |
| S2.16.1 L5 south tower | no plate | 8,820 sq ft |
| S2.09.1 L5 north tower | no plate | 8,536 sq ft |
| S2.17.1 even floors (guard, same page) | 6,982 | 6,982 |

Each looked at, rendered over the page: the plate follows the drawn outline, balconies outside it.

## What the change is

- A short open run with an end on a kept piece or a closed ring (at its end or on its body, within the 1 mm join) is
  kept with it, transitively. A dash on its own is not.
- A drawn line with both ends on kept edge that nothing kept runs along is offered as a piece.
- `BridgesInLine` takes "on one line" as within 25.4 mm (`SlabEdgeInLineMm`), not 1 mm. The gap limit (48 in) and the
  "each runs toward the other" test are unchanged.

## What must NOT move

- The guard sets `30972-01` and `30993-01` (RESUME.md): plates, columns, walls.
- A sheet whose outline already closed: the arrangement only stands up where the walk found no floor.
- Rejected first cut, stashed: an end joined to any corner up to 48 in in line (`PlanarRings`). It made joins inside the
  stair core and took 59 sq ft off the even floor of the same page, and did not reach the west edge, which was never
  in the arrangement.

## The measurement that decides it

1. 31065-01 whole, on and off, every storey against Rory's Revit volumes and against her model.
2. The six-set gate (31065-01 is one of the six).
3. `takeoff corpus-gate --bisect KOR_STEP170_OFF` on the corpus, judged on her plate area, thickness and openings.

⛔ Run where the drawings are local. Never over the VPN.

## Judged by the corpus gate, 2026-10-22 (run a6b41b9e, committed 8fa0d9d6)

Against last night's run, 25 sets gained plate and 10 lost; the bisect named step 170 for 31037-01 (25,390 -> 5,934 sq
ft of her 64,395), 30849-01 (84,233 -> 71,260 of 123,638) and 30993-01 (510,530 -> 503,998 of 560,266).

31037's L2 hold-down plan (page 24): the step's pieces and in-line bridges took the arrangement from 3,815 to 5,057
lines and PlanarRings refused it ("Centroid snapping left an unresolved crossing or contact"), so the storey had no
floor. Each clause off in turn: only the in-line tolerance off let it close; at 6 mm it still refused. So:

- **In line within 6 mm**, not an inch (31065 needs 4 mm).
- **A line drawn twice offers only the stretch no kept edge covers**, never the overlap.
- **The step may never cost a floor:** the arrangement as it was before the step is kept beside it, and a refusal is
  answered by building that one (`arrangeAgain`). 31037 L2 back to 20,339 sq ft; 31065's three witness pages unchanged.

Re-judged on 30849-01 and 30993-01 before re-banking (below).

## Judged again, 2026-10-22 (run e7605731, the corrections above plus steps 171-173)

31037-01 25,391, 30849-01 88,446 (+4,213), 30993-01 510,640: the three losses above are closed. 31009-01 gains its
L8 floor (0 -> about 15,000 sq ft; hers 14,026). Step 170 still took plate on two sets, each rebuilt whole on and off:

- **31032-01 L2**: 30,906 -> 27,759 sq ft; rendered, the step's floor is a strip and the east half is gone.
- **31101-01 L1**: -52 sq ft and her one opening.

The selection above only built the earlier reading where the step's left a column on no floor, and no column stands on
31032 L2's lost half. **So both readings are now always built, and the step's is kept only where it floors more:
fewer columns on no floor, or as many and more floor area (by over a square metre).** 31065 S2.17.1 keeps the step's
(12,825 sq ft, 0 columns off, against 7,011 and 21).

## A stroke continuing a kept edge in line - found 2026-10-26; parked as e0fe5872, then LANDED the same night under the existing policy

**Landed, 2026-10-26 ~21:40 PDT, by operations-ee's decision (A).** The 31170-arch L5 "regression" below is one case of a
class already on develop: a storey's plates overlap. On develop, the same set's L1 and L6 carry part-plan plates 86-87% inside
the 32,076 sq ft floor, and the 24-set replay holds 67 such pairs. The build reports each one as "left as read for the
engineer", and that is the decided policy. With this fix, L5 is in the same state as L1 and L6, and it is counted, not
hidden: `tools/CorpusGateBisect/storey_plate_overlap.py` on the six sets gives 18 overlapping pairs without the fix and
19 with it. The one added is 31170-arch L5, KF10 at 8,245 sq ft over KF6 at 32,076: 79% inside, 1,705 sq ft past it.
Whether a storey's plates may overlap at all (absorb a part plan, clip a match-line overlap, keep one of a floor read twice)
is (B), for Ian and Griffin to decide; that check is its gate. The record of the find, unchanged, follows.

**The witness.** 31168 building C level 3 (S2.40.1, p30, looked at): the slab runs to the outer outline, printing 14" in the
west bays and the south strip. We wrote only the 11,885 sq ft inside the heavy inner line, which leaves 6 columns on no
floor. 8fa0d9d6 wrote 23,199 sq ft; f5a74e04 lost it.

**The class, in one sentence:** f5a74e04's drawn-twice rule offers only the stretch of a stroke past a kept edge it
OVERLAPS, so a stroke that continues a kept edge in line, end to end, overlaps nothing, reads as "not along a kept edge
at all", and is dropped. An outline drawn as several strokes in a line loses its continuations. The trace showed 9 such
strokes on p30, e.g. (84434,51397)-(84434,52860) continuing the kept (84434,52860)-(84434,55299). Its end at
(84434,52860) is the dangling end the ring was open at, 25 in from closing.

**The check:** `ALineDrawnTwiceOffersTheStretchNoKeptEdgeCoversTests` (rule extracted to
`GeometryFilterService.StretchesNoKeptEdgeCovers`). Without the continuation clause it fails 1 of 5 (the continuation);
with it, 5 of 5. Core fast 1,692 of 1,692. Halifax 1898f6b4 identical.

**Six sets against 506be566:** 31065 identical. Fixed: 31168 C-L3 11,885 -> 23,199 sq ft (the drawing's floor) and
31130 L15 18,878 -> 20,236 (the right tower's notch closed, its columns now on a floor; looked at). Small: 31138 L2
+185 sq ft and 1 wall; 31202 L4, L7-L13 thickness re-splits.

**REGRESSION, the reason it is parked:** 31170-arch L5 32,076 -> 40,320 sq ft. The architectural part plan A423 (LEVEL 5
PLAN (SW), p57) now closes a floor of 8,248 sq ft, reaching past the overall plan's edge. It shares under nine-tenths of
its ground with the L5 floor, so steps 103 and 176 (DxfToEtabsService, "the same floor read again on a part plan") no
longer absorb it, and it stands as a second plate over the first (rendered). Before the fix, A423 already read 8,159 sq ft
and was absorbed.

**So the continuation rule is right, and landing it needs the part-plan absorption to hold first.** A part plan's floor
over an overall plan's floor is the same floor, even where the part plan reaches past its edge. That is the next rule to
measure (on 31170-arch's A422-A425 at every level, and on the corpus), before this branch lands.
