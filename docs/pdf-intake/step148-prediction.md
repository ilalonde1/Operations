# Step 148 — a storey with no drawing takes a floor from its nearest like-shaped neighbour

## ⛔ NOT BANKED. Measured 2026-09-25, rendered, and left off.

Turned on, built 31065-01 both ways and **rendered both**. Ten storeys each gained ONE plate of
~22,646 sq ft spanning the whole site — the podium footprint — while the right-hand tower, the one
that actually has no floor, still had none. About **226,000 sq ft of plate the engineer does not
have**, and the real hole untouched. Our percentage would have risen while the model got worse.

**The cause is an ORDER, not a threshold.** Donor selection scores a candidate against what STANDS
on the storey. 31065 is a two-tower site, so a storey's walls and columns span BOTH towers and the
site-wide podium plate resembles that extent perfectly. `DonorPlateLikenessMargin` cannot see that
the storey is two buildings.

This breaks the repo's own standing rule — *compose the site once, CUT AFTER*. The donor is being
picked before the building cut.

**What would make it right:** choose the donor per building, after the cut, so a tower storey can
only borrow from a tower storey. Until then the knob is opt-IN.

Evidence: `31065-before.png` / `31065-after.png`, rendered from
`Baselines/pdf-only-31065-01.e2k` and the six-set run's `out.e2k`.

---

**Knob:** `KOR_STEP148_ON=1` turns it ON (it is off by default).
**Setting:** `dxf.infer-missing-floors` (bool), answerable per job.

**Target set:** **32 of the 297 corpus sets** carry at least one storey with no floor — **130
unfloored storeys**, of which **112 had no drawing placed on them at all**. Four sets hold 85 of
the 130.

## The target set, as a number, before the change

Measured 2026-09-25 from `docs/etabs-handoff/corpus/gate-142-arm-on.csv` and its sheet ledger —
no corpus run; both artefacts already existed.

| | |
|---|---:|
| sets with at least one storey carrying no floor | **32** |
| unfloored storeys across them | **130** |
| …of which a PLACED drawing failed to produce a slab | 18 |
| …of which **no drawing was placed on the storey at all** | **112** |

Four sets hold 85 of the 130:

| job | storeys built | floored | short |
|---|---:|---:|---:|
| 31039-01 | 35 | 7 | 28 |
| 30864-01 | 41 | 20 | 21 |
| 30989-01 | 27 | 7 | 20 |
| 31005-01 | 24 | 8 | 16 |

A ladder of 35 storeys with 20 drawings is a typical floor drawn once. The storeys above it are
real — the elevations chain them — and nothing was ever drawn for them to place.

⚠ The ledger is from the gate-142 run. Steps 143, 144 and 145 have banked since, adding 239 storeys,
so the ladders are longer now and these counts are a floor, not a ceiling.

## What the change is

Nothing new is written. `E2kGeometryComposer` has carried this since before step 100: a storey with
no plate takes one from the storey whose own plate is closest IN SHAPE to what stands on it,
preferring the nearest elevation and the storey below on a tie, and the donor's openings come with
it. It writes its own warning naming every storey and its donor.

It is opt-in, and `PdfOnlyBuild.cs:379` — the stick-file route, the only route a drawing set takes —
hardcodes `InferMissingFloors = false`. Not a setting, not a knob: a literal.

This step makes it a rule with a key, on by default, marked as an assumption in the workbook.

Ian, 2026-09-25: *"make an educated guess on any of your questions … and build the model and mark
anything you did guess at as yellow — i.e. I guessed here — so if it's fucked up that's why.
Otherwise NOTHING will ever build."*

## What must NOT move

- **`30972-01`** — 20 of 20 storeys floored, 85% of her area. It has no unfloored storey, so it must
  be byte-identical. If it moves, the rule is reaching storeys it was not aimed at.
- **`30993-01`** — 39 of 40 floored. Exactly one storey may gain a floor.
- The six banked baselines change by design where they have unfloored storeys, and the two-arm
  differential is what says whether each change is right.

## The measurement that decides it

Two arms on the sets with her model, same denominator:

    takeoff corpus-gate --bisect KOR_STEP148_OFF

1. Her plate area we reproduce — must rise, and the sets in §1 are where.
2. `plates_beyond_sqft` — plate whose centroid lies outside the box of her columns on that storey.
   **This is the one that can condemn it.** A borrowed floor is the exact shape of a floor she drew
   somewhere else; if it lands on a storey with a different footprint it adds area that is not hers,
   and the headline percentage rises while the model gets worse.
3. The guard sets above.

## The known failure, already paid for once

The engineer's words, on the run where borrowing went wrong: *"on several levels (9, 3, mezz, 1) he
inverted slab and opening."* That was a different mechanism — a recovered outline read as a floor —
but the lesson holds: **a wrong floor looks exactly like a floor she drew and nothing about it asks
to be checked.** The donor rule already answers that twice over: it will not hand a fragment to a
storey that has its own plate, and `DonorPlateLikenessMargin` refuses a donor whose shape does not
resemble what stands on the storey. C-LEVEL 3 was handed LEVEL P1's site-wide parkade — six times
its own area — and that is what the shape test exists to stop.

What this step adds is the third answer: it is YELLOW in the workbook and says so in the model.
