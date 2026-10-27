# Step 185 — a sheet's legend frame is not a floor

## Written 2026-10-27 ~02:00 PDT, after measuring and looking, before the code replay.

**Knob:** `KOR_STEP185_OFF=1` keeps every legend frame as a floor, as before. Rule row: `dxf.legend-frame-is-not-a-floor`
(1 = on; migration 123, KOR.Drafter 0975701, applied by operations-ee 2026-10-27 ~02:05).

**Target set:** 20 of 56 corpus gate sets carry a legend frame written as a floor (35 plates), and all 20 have the
engineer's model (every gate set does); the other 279-set corpus is not measured for it.

**Ian's plan-page item (4b):** "legend boxes read as floors". Recorded 10-26 as: 20 of 56 gate sets have a storey whose plate
misses 80%+ of its members (35 storeys); operations-ee's hint, legend boxes hold schedule text; checked 10-26 on 31162: our DXF
carries no text in the box, but it holds 41 BEAM lines + a wall sample + a partition sample - a line-type key.

## The population at develop (c03a0e07), not the stale one

The 35-storey list predates step 184. Re-measured on the 57-set corpus gate run tonight (run fff31290, 79 min, built at
c03a0e07 + the answer-import test change, which touches no geometry), `scratchpad offplate_root.py` over the build reports:
**21 storeys on 14 of 56 sets** have 80%+ of their members beyond every plate. Every one was rendered (`takeoff model-render
--storey`) and looked at:

- **12 storeys on 8 sets are a legend frame read as the floor** - 31053 L2, 31066 L1 L3 ROOF, 31085 L3 L4, 31093 L2, 31108 L3,
  31158 L1 L2, 31171 L3. A plain rectangle at the sheet's foot or corner, beyond the building, holding the key's one sample
  stroke (read as a wall) and nothing else.
- **9 are other faults, not this class**: a floor read in part - a core or a wing only (30864 L2-L4, 31083 L4, 31087 ROOF,
  80064 L2); a plate off its walls (30838 L36 L37); edge boxes crossed by the building's walls (31183 P1); no plate at all
  (31017 L6). Left as they are; not this step.

The off-plate share is the wrong instrument for the class: a legend frame also stands on storeys whose real floor WAS read
(31085 L3, 31108 L3, 31158 L1, 31076 L1-L2, 31129 L1). So the check looks for the frame itself.

## The sentence and the check (rule 11)

**A bare rectangle drawn beyond the building - sheet furniture: a sheet's key or legend frame, or a box drawn on the sheet,
holding no column and at most one sample stroke - is closed by the slab pass and written as a storey's floor.**

WIDENED 2026-10-27 ~03:10, before the commit (operations-ee's condition on migration 124: look at the takes at the
threshold's edge). The sentence first said "a sheet's key or legend frame". 30990-01 L1/L1A's 530 sq ft take is not one: on
S2.04.1 (PDF page 26, "...Stickfile (KW comments)") it is the frame of a 3D stair sketch a reviewer pasted east of grid 8b at
line D, read on the slab MARKUP layer as a double-lined box (443 inside 530). It is not a floor, and taking it out is right,
for a reason the first sentence did not name. Operations-ee (Ian's proxy): keep 500 sq ft; restate the rule - migration 125
retires 123's row and restates it with this wording. 30986-02 L2's 585 sq ft strip, the other edge case, is not taken by the
build (the e2k check counted it; the in-build test, with the storey's members from every sheet, does not).

`tools/CorpusGateBisect/legend_box_plates.py` over the gate's 56 models: a FLOOR plate that is a rectangle of 500+ sq ft, with
no column and no opening in it, at most one wall midpoint, lying wholly beyond the box of the storey's other members.
**RED at c03a0e07: 35 plates on 20 of 56 models**, exit 1. Two passes to get there, both from looking:

- first cut, no size floor and no opening test: 68 plates on 28 models - 2-100 sq ft pads and symbols nobody has looked at,
  and 31053 L17, a whole floor read square beside its walls (a placement fault) holding its stair opening. Openings are written
  `AREA "KO.." AREA n`, which the first parse missed;
- the frame repeats across jobs at one size - 7,065-7,068 sq ft on 31066, 31076, 31162; 1,171 on 31129, 31158, 31171; 6,828-6,845
  on 31093 and 31108 - the office's sheet template. Seen as frames in the renders: 31076, 31117, 31162, 31129, 31039, 30986-02
  (the tall title strip), 31048 (the lower frame), 31098, 31003, 40112, 30990, 31143, 30864 ROOF (the lower frame).
  NOT confirmed by eye: 30986-02 L2's 585 sq ft strip (beside the building's west edge in the render; small).

## Replay 1 (gate 8acd14bc, 2026-10-27, "at most one stroke") — CONTRADICTED; the rule narrowed (migration 126)

Against run fff31290: 38 of 56 sets byte-identical, 18 changed; 47 rectangles taken in-build on 18 sets, every take not looked
at before looked at on its drawing. **One was a real roof: 40112-01 ROOF, 2,203 sq ft - the UPPER STEEL ROOF DECK on S2.03 ROOF
PLANS** (gridlines F-K by 7B-10A, deck outline, SB beams, SC1 HSS columns, ~62 x 37 ft), bare only because the reader read none
of its steel. The 32 one-stroke takes were all legend frames or 31183 P1's schedule-table box. So operations-ee (Ian's proxy)
chose (a): only a rectangle holding EXACTLY ONE stroke, the key's sample, is taken; a bare one is left as read (migration 126
restated 125; 124's 500 stays). The e2k check's limits this replay exposed are in its own header.

**Finding recorded for its own fix (not this step): 40112-01 S2.03 - the reader reads none of the upper roof's steel (SB beams,
SC1 HSS 12x12 columns); the ROOF storey's deck carries no member.**

## Replay 2 (gate f98c7cf2, 2026-10-27 03:54-05:12 PDT, "exactly one stroke", migration 126) — the replay that banks it

Compared exactly against the step-off gate fff31290 and replay 1 (8acd14bc) by scratchpad diff126.py: per set, the model must
equal the off model minus each one-stroke frame replay 1 took - plates and columns exactly, walls bounded (none added; each lost
wall inside a taken frame, at most one per frame - a wall carries its TOP storey's name, so a sample drawn on L2's sheet is
written on L3, and some samples are already dropped downstream).

- The build's takes equal replay 1's one-stroke takes on every set (32; none extra, none missing).
- **54 of 56 sets PASS.** 40112-01 is byte-identical to off: the S2.03 steel roof deck is back. 30990-01 byte-identical to off
  (the reviewer's sketch box stays, as before). The threshold-boundary sets 30864, 31168, 31048 byte-identical to off and to
  replay 1; every set neither replay touched byte-identical to both.
- **31066-01 (explained, not wrong):** L2 and ROOF still carry one legend-frame plate, at exactly the taken frame's place:
  building 2's sheets (S2.15, S2.20) draw the same frame with NO stroke; it had been deduplicated against building 1's, and with
  that one taken it is written. No worse than before the step; L1 and L3 are clean.
- **31158-01 (explained):** L2/L3 lost the two samples; L4-L6's samples (their frames not taken) are rewritten 1 mm off as their
  stack recomposes - same strokes, same place, all inside the frame's box.
- **The gate's judge: 31117-01 newly LOST** against the bank (openings "hers we have" 7 -> 4). A false credit going: its L2/L3
  "plate" was the 7,215 sq ft frame, beyond her footprint, yet it counted toward "we carry the floor", so 3 of her openings were
  credited as "a void we carry no plate over". Her plate figure is unchanged (5,426 of 16,615 sq ft); plates beyond her
  footprint fell by exactly the frame (18,012 -> 10,797).

The comparison is tools/CorpusGateBisect/step185_replay_diff.py (its header states the wall-pairing limit).

**Follow-up recorded, not fixed (operations-ee): a no-stroke copy of the template frame survives as a plate - 31066-01's
building 2 sheets S2.15 (L2) and S2.20 (ROOF) - a remaining instance of the class.** FIXED 2026-10-27, below.

## The same frame drawn again without its stroke (2026-10-27, migration 130)

Building 2's key stroke on 31066 was the same wall as building 1's and was modelled once, so building 2's copy of the frame
holds no stroke - no frame by the one-stroke test - and was written once building 1's copy was taken. A plate on the same storey
at the very rectangle of a frame taken there (every corner within the reach, 6 in; the area within 1%) is that frame and is taken
too. Check tools/CorpusGateBisect/legend_frame_twins.py: RED on f98c7cf2, 2 twins (31066 L2, ROOF) of 56 sets. Test
TheSameLegendFrameDrawnAgainOnAnotherSheetWithoutItsStrokeGoesToo: red without the rule, green with it; a bare rectangle
elsewhere stays. Gate gate57-twin against f98c7cf2: 55 of 56 byte-identical; 31066 changed by exactly its 2 twin plates; the
check GREEN (0). Migration 130 restates 126's row to say so (rehearsed in a rolled-back transaction, applied by operations-ee).

## Predicted

- e2k CHANGES only on the 20 sets the check names; the other 36 build byte-identical to run fff31290.
- On those 20, the plates the check names leave, and with each the one sample stroke it framed (a wall). Nothing else moves.
- The check goes GREEN (0 plates) on the 57 built with step 185 on, and stays at 35 with `KOR_STEP185_OFF=1`.
- Her plate area moves DOWN on the sets where the frame was being counted inside her footprint (the frame is never on her
  plan), never up. A set that LOSES plate area she has must be looked at before anything lands.
- Halifax 1898f6b4: unchanged unless its sheets carry the frame (to be measured, not assumed).

COVERS / DOES NOT: as the check's header - a frame inside the building's box, a frame that is not a rectangle, a key holding
two or more strokes read as walls (31048 L4's upper frame) are left as read.
