# Step 182 — a tie between printed thicknesses is broken by the ground each stands for

## Written 2026-10-25, on 31065's L1 thickness (Ian's go on "the L1 thickness zones"), before the gate below has judged it.

**Knob:** `KOR_STEP182_OFF=1` keeps a plate whose printed thicknesses tie on count at the default, as before
(`StructuralPlanClassifier.PriceSlabsByTheCalloutsInsideThem`, step 118).

**Target set:** 2 plates in the six banked sets (31130-01 P2 east, 31138-01 L22 mech), both now the engineer's thickness;
1 more (31168-01 B-L39) takes 12", which is also the default, so its model does not change. Its witness, 31065-01 L1 north,
does NOT move - see "Measured" below. Reach on the 57-project corpus NOT yet measured.

## Measured, 2026-10-25 (six sets, on and off, frozen build cli-c182; Halifax byte-identical)

| Plate | Before | After | The engineer's model |
|---|---|---|---|
| 31130-01 P2 east, 27,329 sq ft | 12" default | 10" (10" nearest 77%) | 10" field (`Slab10`, 49,264 sq ft) |
| 31138-01 L22 mech, 5,290 sq ft | 12" default | 10" (10" nearest 61%) | an 8" plate with a 10" plate over 3,731 sq ft of it (71%) |
| 31168-01 B-L39 roof, 9,475 sq ft | 12" default | 12" (12" nearest 59%) | no B-L39 in the yardstick (and it is this tool's own output, see 0db1608d) |
| 31065-01 L1 north, 19,352 sq ft | 12" default | 12" default, refused | 900 mm field with 300/500 mm and transfer zones |

31130 P2 was looked at (call-outs and their cells drawn over the slab linework): the 10" call-outs stand over the field,
the three 12" call-outs together at the east edge by the ramp.

**The witness stands down, and that is right under the rule as written.** On 31065 L1 north 900 is nearest 46% of the
plate, 300 19%, 600 19%, 500 15%. Looked at on the drawing (S2.04.1.1, p24): this plate is not one thickness with a few odd
call-outs - it is four zones, 900 over the west half, 500 north-centre, 600 east, 300 strips, divided by dashed STEP lines
the drawing draws ("900 SLAB + STEP", "600 SLAB + STEP" stand across them). One thickness for the whole plate is wrong at
any choice; 900 overstates the east half by 300-400 mm. The 50% line was written before the measurement and is not moved
to make the witness pass. The refusal now says the shares, so the engineer sees where each thickness stands.

The fix for the witness is the zones themselves: split the plate along the drawn step lines. In the exported DXF they are
on the reader's catch-all BEAM layer, with the hatching, stirrup-zone boxes, dimensions and symbols - reading them out is
its own step.

## The witness

31065's L1 is a transfer floor. The north plate (19,352 sq ft) prints 300, 500, 600 and 900 SLAB with no outline between
them; step 118 takes the thickness printed most, and on a tie keeps the 12-in default and asks. Looked at (S2.04.1.1, p24):
"900 SLAB" stands in the open field; the hatched bands are stirrup patterns, not thicknesses. The engineer's L1 carries a
900 mm field under 37,540 sq ft of its 57,900, with 300 and 500 mm areas and 1,040-1,945 mm transfer zones. Ours: 12 in.

## What the change is

On a tie, each point of the plate (a 60 x 60 sample of its box) goes to the call-out nearest it; the thickness whose
call-outs stand for more than half the plate is the plate's, ASSUMED and flagged with every share. Under half, the tie is
still the engineer's question.

Not this step: splitting the plate into the zones the drawing draws (step lines). That is the plate's real shape and a
step of its own.

## What must NOT move

- A plate with one thickness, or one printed most: step 118 decides it as before.
- Halifax (its DXFs carry no call-outs): byte-identical.

## The measurement that decides it

1. 31065-01 L1: the north plate's thickness, against the engineer's L1.
2. The six banked sets, on and off.
3. `takeoff corpus-gate --bisect KOR_STEP182_OFF` on the corpus, judged on thickness.

⛔ Run where the drawings are local. Never over the VPN.
