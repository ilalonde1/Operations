# The seven sets whose frame does not register — judged, 2026-10-26

The corpus gate leaves seven sets unjudged by position (`FRAME` in its table, commit 9055a41e: column-registration median
above 1,000 mm): 30819-01, 30986-02, 31005-01, 31048-01, 31098-01, 31183-01, 70057-01 — 7 of 56 sets with a model. Asked
(operations-ee, 10-26): is it our datum or rotation, or the engineer's origin?

**Method.** `tools/CorpusGateBisect/frame_rotation_look.py` on each set's gate build (`%TEMP%\kor-drawings\corpus\<job>\out.e2k`,
built 10-25) against the engineer's model (`%TEMP%\kor-drawings\yardsticks\<job>.e2k`): per storey both name, the rigid fit
(rotation in 0.5° steps, then the translation the column pairs vote for) that puts the most of our columns within 100 mm of
hers, beside the translation-only fit the yardstick uses today. Every set's per-storey table and plot is in
`frames-unregistered-2026-10-26/` (`<job>.txt`, `<job>.png`, and close-ups). Looked at, every one.

## Verdicts

| Set | What it is | Evidence (ours within 100 mm of hers) | Whose |
|---|---|---|---|
| 30986-02 | **The engineer's model is rotated 270°** from the drawings | 4 of 4 storeys fit at 270° (P1 269.5°), one offset (96,296, 70,902) ± 0.4 m; L1 13/34, L2 16/22, L3 14/19, P1 13/21 — translation only: 1-2 | Hers (her frame) — our read is right |
| 31098-01 | **Rotated 270°** | 21 of 21 storeys at 270°, one offset (-8,245, 67,068) ± 10 mm; L1 23/32, L5 30/41, L10 11/19 — translation only: 2-3 | Hers |
| 31183-01 | **Rotated 355°** (5°) | 16 of 16 storeys at 355°; L14 19/19, L4 27/30, L1 42/69 — translation only: 1-3. Two offsets: (946, 33,140) on 14 storeys, (3,426, 61,512) on L15-L16 | Hers; the L15-L16 offset is a second question (below) |
| 31005-01 | **Frame found; our column read is short** | best fit IS translation only, 0°, offset (825, -35,652) on 18 of 20 storeys (L22 and ROOF fit nothing: 2 of 11, 1 of 2); L4 9/11 ours on hers, but hers 20 on L4 — the corners match, the interior columns are missing from ours | Ours (the read), not the frame |
| 31048-01 | **Frame found; our read is a part of her building** | 0°, offset (36,600, 53,746) both storeys, 37 of 126 / 37 of 135 on a common grid; her model runs to x ≈ 180 m, ours to ≈ 110 m. The set is drawn in quadrants: the build placed 29 of 36 sheets, 10 on the grid by axis name, and the 4 SE-quadrant sheets "where their columns and walls stand over members already placed" (no axis in common) | Ours (where the quadrants were set), not the frame |
| 30819-01 | **Cannot be judged by columns** | her model has 2-4 columns a storey (L2 2, L6 4); ours 8-20. 2 of 2 hers matched on L2/L3 at one offset | Neither: a wall building in her model; needs registration by walls |
| 70057-01 | **Cannot be decided: we read too few columns** | hers 75 a storey on L2-L4, ours 8-13; best 4 of 13 | Ours (the read) — its frame waits on its columns |

So: **3 of 7 are the engineer's frame (a rigid rotation the registration cannot find), 2 of 7 register already and are held
back by our own short column read, 2 of 7 cannot be decided by columns.** None of the 7 is our datum or rotation being wrong.

## What each implies

1. **The rotation is the yardstick's to find** (30986-02, 31098, 31183). `ModelYardstick.Register` votes for a translation
   only. Turning the engineer's points by the angle the column pairs agree on, before the translation vote, judges these three
   by position. It changes the judge, not the model: no e2k moves. Same gate as any judge change: re-judge the bank and the
   run with the same judge, and say which sets moved into "judged".
2. **31005 and 31048 are reading faults, not frame faults.** The gate's `FRAME` mark hides them behind a frame verdict when
   the frame is right. Their losses are columns we did not read or did not place (31005's interior; 31048's quadrant sheets - the SE quadrants
   were set over members already placed, which a quadrant by definition does not overlap; not looked into further tonight).
3. **30819** needs registration by wall midpoints; **70057** needs its columns read first.
4. **31183 L15-L16** fit at the same 355° but 28 m away from the other fourteen storeys: a building part placed differently
   in one of the two models. Not looked into further tonight.

COVERS: columns only, per storey both models name. DOES NOT: walls, plates, mirrored drawings, storeys named differently.
A same-class fault it would miss: a regular bay fits several turns; the verdicts above rest on the SAME angle and offset
holding on every storey, not on one storey's best fit.
