# Step 177 — a ring inside a hole already cut is part of that hole

## Written 2026-10-24, looking at 31138's L10 with steps 175 and 176 on, before the gate below has judged it.

(The number was first used the same night for a rule refusing openings with under nine tenths of their ground over the
floor. That rule was measured, found to take one of her openings - she cuts small shafts on the slab edge - and removed
before it was committed; see step175-prediction.md. This is a different rule.)

**Knob:** `KOR_STEP177_OFF=1` cuts every ring whose centre is in a floor, whatever hole it already stands in
(`StructuralPlanClassifier.SplitSlabsAndOpenings`).

**Target set:** 1 witness set, 31138-01 (every storey's elevator shaft). Reach on the corpus NOT yet known; any ring drawn
inside an opening the same sheet cuts moves. The gate below measures it and this line is corrected with the figure.

## The witness

The six banked sets with steps 175-176 off and on: 31138 gained two openings on every storey, none of them hers (88 of
her 108 matched both ways). Rendered L10, off / on / hers: the shaft is one opening in hers and in the build without step
175; with it, the shaft's two cabs - each drawn closed inside it - were cut as two more (KO127 52,743..58,176 by
49,796..51,877 mm; KO128 and KO129 its two halves, exactly). The same pattern adds openings on 31130 (+46), 31065 (+57),
31202 (+53) and 31170-arch (+57).

## What the change is

Rings are taken largest first, so a hole is cut before what is drawn inside it. A ring whose centre lies in an opening
already cut AND that runs along that opening's own edge for at least `dxf.ring-on-plate-edge-fraction` of its length
(KorStandards, 0.5 - the share that already says a ring on a plate's edge is that edge) is not cut again, and is flagged.

First cut, any ring with its centre in a hole: on the six banked sets it took 31170-arch from 640 openings to 464 and
her openings matched from 32 to 20 of 37 - its real shafts stand inside larger rings it cuts, sharing little or none of
their edge - and 31065 from 57 to 56. Narrowed to the rings that tile the hole.

## What must NOT move

- Two holes side by side (step 119's two cabs drawn as two rings sharing an edge, with no shaft outline round them).
- Plates: this decides only whether a ring is a second opening.
- Halifax and the six banked sets: checked on and off before commit.

## The measurement that decides it

1. The six banked sets, 175-177 on against all three off: openings back to the pre-175 count, or within the rings that
   are genuinely new holes; her openings matched not lower.
2. Halifax e2k byte-identical.
3. `takeoff corpus-gate --bisect KOR_STEP177_OFF` on the corpus.

⛔ Run where the drawings are local. Never over the VPN.

## Measured, 2026-10-25 (six banked sets, 175-177 against all three off; builds of 31065 and 31202 looked at)

With `dxf.nested-ring-tile-share` 0.15, against all three steps off, judged against her models: no set loses plate or
any opening of hers. 31170-arch 32 -> 33 of her 37 openings; 31065 plate +112 sq ft; 31202 75 openings (77 off - the
core slot's two ends no longer cut over the slot); 31138 +2 openings (L1 KO43 inside a shaft opening the build already
cut twice - harmless; P1 KO28 beside the parkade ramp, where our P1 already cuts several openings hers does not - the
over-cutting at P1 predates these steps). At 0.25 31202's slot ends (24.6% of it) were cut again: three cuts.

31065's L1 KO54 (2.3 x 2.4 m) is not cut with this step; it is the cab at the west end of a 7 x 2.4 m shaft that the
build without the step cut THREE times (KO50 and KO51 the same box twice, and KO54). Its "match" to her A15 (a 255 x
350 mm sleeve 1.35 m away) was distance, not the opening.
