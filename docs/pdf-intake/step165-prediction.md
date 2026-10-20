# Step 165 — the engineer's yellow is structure arriving from below

## ⚠ WRITTEN AFTER THE CODE, 2026-10-19. Not banked until the gates below have judged it.

**Knob:** `KOR_STEP165_OFF=1` writes yellow on the plain `-ASSUMED` layer and raises it like any member
(`DxfExporter.DeclaredSuffix`, `E2kGeometryComposer.ArrivesFromBelow`).

**Setting:** `dxf.pdf.markup-structure-colours` entry `FFFF00=WALL-BELOW?` (step 152's table).

**Target set:** 1 of 1 witness sets measured (143 E 3rd Hotel), every yellow mark on its 8 plan pages looked at: L36's
yellow columns sit on the typical floor's with nothing over them (L37 and the roof step back 1.5 m); its burgundy
columns are exactly the six that continue to the roof; the roof's yellow are those six arriving; L1's yellow perimeter is
the parkade wall, its burgundy spine the wall going up. 63 yellow members read once where a member is already written on
the storey (L1 16, L2 10, L36 14, L4 3, ROOF 6, UPPER ROOF 15). Only layers named `-BELOW` move: 0 of the 6 banked sets
carry one, so byte-identical is expected.

## The fault

Declared WALL, yellow was raised a storey by the member-rises rule: four columns stood from L36 to L37 under nothing, and
L1's core was written twice, once as a 44" wall.

## What the change is

- A "<TYPE>-BELOW" declaration writes on a `-BELOW` layer. The composer keeps a member on such a layer on its plan's own
  storey - the rule a column read from dashed linework already follows (`FromBelow`: "dashed is below and carries the
  slab, solid is above").
- The below members are written in a second phase, after every other wall and column; phase 0 is the order and result
  as before. A below wall whose length is half covered by walls already written on its storey (parallel within 5
  degrees, axes within the two half-thicknesses, the pieces taken together), or a below column within the larger side of
  one written, is that member drawn again and is not written. The report counts them by storey.

## What it does NOT do

- A below member never gives way to another below member.
- Nothing is judged against a prediction of what will be written.

## The measurement that decides it

    the six-set gate (local mirror)
    takeoff corpus-gate --bisect KOR_STEP165_OFF

⛔ Run where the drawings are local. Never over the VPN.
