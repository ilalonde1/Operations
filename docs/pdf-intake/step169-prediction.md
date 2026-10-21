# Step 169 — a ceiling under half a storey a floor is another building's level

## Written 2026-10-21, after the corpus gate named 31017-01 and before the gate below has judged it.

**Knob:** `KOR_STEP169_OFF=1` restores the even share (`StoreysFromPlans.Merge`).

**Target set:** 3 of 56 corpus sets built on 2026-10-21 (run 24b09de9) carry the SPACED EVENLY "FAR UNDER" warning this
rule acts on - 31017-01, 31168-01, 30993-01 - and all 3 are among the 56 with the engineer's model.

## The witness

31017-01 (Langford Central) draws a tower (L1..L24) and a commercial podium (C1..C4) on one set. The elevations state L4
at 15,057 mm and C4 at 18,512 mm; L5..L24 are named by plans only. The ladder shared those twenty storeys out over the
podium's last 3,455 mm: 164 mm a storey. `SpacingNotes` has warned of exactly this since 2026-09-23 and the code said
fixing it was the larger job.

On 2026-10-19 `73458eec` made `SameFloorTolerance`'s floor of 12 a real 12 in (305 mm) in a millimetre model, where it
had been 12 mm. Correctly: storeys 164 mm apart are one floor. So the fold kept one copy of their members, L9 and
L13..L24 came out empty, and the corpus gate read 31017-01 as LOST (227,331 -> 92,763 sq ft; 23 -> 3 shared storeys).
No step 149-168 knob restores it; the 09-25 tool reproduces the bank, the 10-19 tool the loss.

| 31017-01 against her model | bank (09-24) | 10-19 on | with step 169 |
|---|---:|---:|---:|
| shared storeys | 23 | 3 | 24 |
| her plate we read, sq ft (of hers on the shared storeys) | 227,331 of 347,987 | 92,763 of 180,919 | 251,047 of 411,376 |
| thickness agrees | 19 | 1 | 19 of 23 |
| her openings we have | 19 of 36 | 11 of 36 | 21 of 59 |

Her model stands L5..L24 at 120 in (3,048 mm) a storey. With the rule, ours stands them at 3,050.

## What the change is

Where an even share of the gap between two stated levels falls under half the set's storey height, the level above is
not the run's ceiling. The run stays where the walk up from the last stated level put it - the set's storey height a
floor - ASSUMED and listed under the ASSUMED header, as every plan-only storey is. The stated level keeps its stated
elevation; the e2k writer orders storeys by elevation, so the podium's C storeys fall between the tower's.

## What must NOT move

- A run whose share is half a storey or more: spaced evenly as before (`ARunBracketedByItsOwnBuildingsLevelsSaysSoAndDoesNotWarn`).
- Any set without the warning: its ladder is unchanged by construction.
- 31168-01 (C-ROOF between C-L9 and L10 at 1,125 mm) and 30993-01 (UPPER ROOF between ROOF and AMANITY at 89 mm): each
  roof moves to a storey over the level below it. Judged by the gate below and rendered.

## The measurement that decides it

    takeoff corpus-gate --jobs 31017-01,31168-01,30993-01 --bisect KOR_STEP169_OFF

1. Her plate area we read, thickness, openings - must not fall on any of the 3.
2. 31017-01 rendered against hers: the tower's typical storeys carry her plate.
3. The six-set gate (31168-01 is one of the six).

⛔ Run where the drawings are local. Never over the VPN.

## Judged, 2026-10-21

Run 6790c9d3 (`corpus-gate --jobs 31017-01,31168-01,30993-01 --bisect KOR_STEP169_OFF`, tool built 11:17 UTC, census reused locally):

| set | her plate we read | thickness agrees | her openings we have |
|---|---:|---:|---:|
| 31017-01 | 227,331 -> 251,047 of 411,376 (+23,716) | 19 -> 19 of 23 | 19 -> 21 of 59 |
| 30993-01 | 510,530 -> 510,530 (+0) | 35 -> 35 of 35 | 76 -> 76 of 154 |
| 31168-01 | not judged: READ A DIFFERENT STICK FILE than the bank, and no shared storey with columns | | |

Nothing fell on any of the 3. 31168-01 was judged by building it both ways and against her model instead:

- `model-diff` (169 off -> on): plates moved 0, columns lost 0 / gained 0, walls lost 1 / gained 1 - one 4,556 mm wall
  at (69,920, 86,393) moves L10 -> L11 because C-ROOF now stands between them; the C-ROOF columns now span two storeys
  of the stack. Nothing else in the file differs but storey heights.
- C-ROOF stands 2,945 mm over C-L9 (was 1,125). Her model: C-ROOF 116 in (2,946 mm) over C-LEVEL 9, between LEVEL 10
  and LEVEL 11 - the order and height ours now have.
- 30993-01's UPPER ROOF stands 3,455 mm over ROOF (was 89). Her model: UPPERROOF 242.5 in (6,160 mm) over ROOF, with a
  MECH storey between that the set's plans do not name. Closer, still short, and listed ASSUMED.

**Verdict: KEPT.** The fast suite passes 1,636 of 1,636.

Gate quirk seen on this run, OPEN: with `--jobs`, every banked set outside the list is reported LOST and the bisect
rebuilds all 55 of them, so the bisect lines for those sets say nothing about this rule.
