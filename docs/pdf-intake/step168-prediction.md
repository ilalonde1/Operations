# Step 168 — an opening the page found is cut in whatever floor holds it

## ⚠ WRITTEN AFTER THE CODE, 2026-10-20. Not banked until the gates below have judged it.

**Knob:** `KOR_STEP168_OFF=1` carries the openings as slab rings again (`PdfOnlyBuild`), drops the void exemption from
the column rule (`E2kGeometryComposer`) and the drawn-order rule for concave openings.

**Target set:** 1 of 1 witness sets measured (143 E 3rd Hotel): B1's OPEN TO BELOW room (1,017 sq ft, a column standing
in it) cut where it was floored; L37's stair B cut where v5 floored it; every other storey's openings unchanged (41
storeys diffed). Only pages a markup-only read takes across (step 153) carry openings this way: 0 pages of the 6 banked
sets. The drawn-order rule is the composer's and can move any set's concave openings.

## The fault, three ways

1. Carried as slab rings, a void the size of a room was read as a floor and cleared as a parkade's interior (step 78).
2. The composer refuses a hole with a column in it (step 107, right 98.6% of the time - X-boxes round columns). A void its
   own words name is the other 1.4%.
3. The composer orders an opening's points by angle round its centre. That walks a convex outline and scrambles a concave
   one into a star of the same points - the B1 room zigzagged through the stair.

## What the change is

- `ExtractedGeometry.CarriedOpenings` / `CarriedVoids` written on `KOR_C_OPENING-CARRIED` / `KOR_C_OPENING-VOID`
  (`DxfExporter`, both in `NonMemberLayers`); the classifier takes them out of every other reading and cuts each ring
  whose centre lies in a final floor, under half of it, not already cut there.
- A void found by its words (step 164) is traced over the room's square lines only - the X's own strokes are not walls.
- An opening on the VOID layer is exempt from the column rule.
- Where angle-sorting changes an opening's area by more than a hundredth, its drawn order is kept (if it does not cross
  itself). Convex openings are written exactly as before.

## The measurement that decides it

    the six-set gate (local mirror)
    takeoff corpus-gate --bisect KOR_STEP168_OFF   (needs the engineers' models exported on KOR-210)

⛔ Run where the drawings are local. Never over the VPN.
