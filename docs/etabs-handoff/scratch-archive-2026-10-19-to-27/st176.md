# Step 176 — the smaller of two readings of one floor goes, by the ground they share

## Written 2026-10-24, with Ian's go-ahead (the 31032 slab outline, "Go on you go's"), before the gate below has judged it.

**Knob:** `KOR_STEP176_OFF=1` judges a plate from another sheet by its vertices alone, as before
(`DxfToEtabsService`, the storey's plates, step 103).

**Target set:** 1 witness set, 31032-01 (L1, with step 175). Reach on the corpus NOT yet known; any storey where a part
plan's plate lies mostly over the overall plan's floor but fewer than nine of its ten corners do moves. The gate below
measures it and this line is corrected with the figure.

## The witness

With step 175 on, 31032's L1 fell from about 11,600 to 2,143 sq ft against her 12,840. Two rules decide whether two
readings of a storey are one floor, and they disagreed:

- Step 103 (`DxfToEtabsService`) takes out the SMALLER plate from another sheet - but only when nine of its ten vertices
  stand inside the larger. The stair partial plan S2.03.1_3 is drawn 2.6 m off the overall plan's grid (L1's columns
  matched hers at a median 50 mm; with the part plan's members in, 2,634 mm), and its 2,143 sq ft plate failed that test.
- Step 113b (`E2kGeometryComposer`) measures the ground two plates share (nine tenths), which caught it - but keeps the
  FIRST reading written, which was the part plan's. The 11,610 sq ft floor was dropped: "lies inside one already written".

## What the change is

Step 103 also takes out the smaller plate when nine tenths of its GROUND lies inside a larger plate from another sheet -
113b's measure (`LoopGeometry.GroundShared`, moved there unchanged), step 103's choice: the larger is the floor.

## What must NOT move

- Any storey where the two rules already agreed: there 113b dropped the second reading, and the second was the smaller.
- Halifax (31180-01) and the six banked sets: e2k identical with the step on and off, checked before commit.

## The measurement that decides it

1. 31032-01 L1, with step 175 on: 176 on and off.
2. Halifax e2k and the six banked sets, on and off.
3. `takeoff corpus-gate --bisect KOR_STEP176_OFF` on the corpus.

⛔ Run where the drawings are local. Never over the VPN.
