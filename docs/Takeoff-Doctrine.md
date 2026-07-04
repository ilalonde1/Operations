# Takeoff Doctrine — set-agnostic accuracy, enforced

The takeoff tool's goal is to be **agnostically accurate**: every number comes from a drafting
convention any structural set obeys, never from a rule fitted to one building or one answer key.
This document is the gate every accuracy change must pass. It exists because the difference between
*honing the app* and *reverse-engineering a drawing* is a process, not an intention.

## The four gates (all must pass before a change ships)

1. **Convention, not coincidence.** The change must be expressible as a sentence about how drawings
   are made, with no building name in it. ("A gridline label sits inside a drawn circle." "A strip
   footing runs continuously under its wall — the schedule says BOTTOM CONT.") If the sentence needs
   "on 31065" to be true, it does not ship.
2. **Three-set validation, $0.** Every change re-runs all benchmark sets (`--deterministic` and/or
   `.vision-cache` replay — no API spend) before commit. A change that helps one set and hurts
   another is not a fix; it is a hypothesis that failed. Current bench: 31065 (metric, two-tower,
   Revit key), 31044 Coronation (imperial, transposed grid, QTO key), 30941 Lindley (imperial,
   37-storey, per-element key). New sets with answer keys join the bench; none leave.
3. **Assumptions carry flags.** Anything inferred rather than read prints an orange flag naming its
   evidence (SCALE_ASSUMED, LEVEL_FROM_SHEET_NUMBER, THK_SPLIT_DISAGREE, PERIMETER_WALL_EST…). A
   silent assumption is a defect even when the number is right.
4. **Falsification is a valid result.** A lever that the evidence rejects is closed and documented
   (ApplyZoning zoning, wall gray-tone calibration) so it cannot be re-proposed. Tuning until the
   benchmark number moves is the failure mode this gate exists to prevent.

## Fitted parameters register (the honest exceptions)

Everything fitted to an answer key is listed here. The list should shrink, never silently grow.

| Parameter | Value | Origin | Path off the register |
|---|---|---|---|
| `SlabAreaReconciler.DefaultNetFactor` | 0.92 | calibrated vs Revit key | derive per-set from the set's own clean typical floors (poché/grid ratio) |
| Rebar density profiles (`BC-moderate` …) | table | calibrated, by design | stays — user-selectable, documented as calibrated |

## Rabbit-hole test

Work on a single set is legitimate exactly as long as its output is one of:
- a **convention** that passes gate 1–2, or
- a **flagged inference** (gate 3), or
- a **closed lever** (gate 4), or
- an honest **residual** ("not priced, quantify by hand", named in the output).

The moment a proposed change is none of these — a constant nudged to close one building's delta, a
special case keyed to a sheet — the work stops and the delta is documented as residual instead.

## What "accurate" means for a drawings-only tool

Plans + schedules state most of the concrete; some volume (transfer build-ups, section-only
thickenings) is structurally NOT on the plans. The tool's contract is therefore: measure everything
the drawings state (typicals to ±5%), flag everything it assumes, and NAME everything it cannot
price. A flagged, named residual is a correct answer; a silently absorbed one is not.
