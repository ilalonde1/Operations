# Takeoff Scorecard — objective state vs the QTO benchmark

> Updated every iteration. This is the honest "are we there yet." Benchmark = Coronation 31044-01
> manual QTO: **38,705 cy** (Slab 24,495 / Wall 8,339 / Column 2,353 / Foundation ~3,518); rebar
> 8,910,454 lb. The QTO is the ANSWER KEY, never an input.

## As of 2026-06-27 (synthesis-led rebuild)

**Architecture: VALIDATED.** Synthesis-led (Layer 1 exact extraction → Layer 2 Claude estimator-
synthesis over exact data → Layer 3 verification). The riskiest assumption — that Claude can produce
accurate, flagged takeoff facts from the exact vector digest (no image, no OCR) — is proven on 2 pages.

| Layer | Status | Evidence |
|---|---|---|
| L1 — drawing digest (exact facts) | **DONE**, general, tested | `vector-digest`; plan pages ~200 lines/16 regions, schedule p61 22 wall bands; 170 Core tests |
| L1 — wall schedule reconstruction | **DONE**, tested | `ScheduleGridReader`: W1-W5 6/12/30/32/36/42", W4 steps 30→36→42" |
| L2 — page synthesis | **VALIDATED (2 pages)** | `vector-synth`: p21→level P4/slab 10"/PC1-4+SWA-C extracted+flagged; p61→schedule, W-marks match |
| L3 — verification + scorecard | TODO | this file is the scaffold |

**Current end-to-end takeoff total: NOT YET PRODUCED.** Honest — the architecture is validated but the
full assembly isn't wired. Do not report a % until the end-to-end run exists.

## Open problems (in priority order)
1. **Slab footprint AREA from geometry** — the hard one. The largest closed vector region is NOT the
   floor slab outline (footprint is many wall segments, not one polygon). Options: (a) targeted Claude
   vision on the rendered page to bound the slab region + exact vector scale; (b) reconstruct the
   outline by unioning wall lines; (c) reuse the old pixel-poché area (stable ~17-19k sqft/floor) with
   vector thickness. Decide via the vertical slice — get SOME area, measure, improve.
2. **Multi-page assembly** — run L2 across all relevant pages, dedupe floors, sum per level (reuse the
   existing BuildingRollup reconciliation ideas).
3. **Wall key-plan lengths** — from native geometry (polyline lengths) to price the wall bands.
4. **Column schedule** — same deterministic pattern as walls, OR via synthesis (already reads PC1-4).
5. **xlsx** — feed the assembled model into the EXISTING ClosedXML exporter.

## Next concrete step
Wire the vertical slice: digest all pages → synthesize each → assemble per-level concrete (slab area via
the chosen option #1 + wall bands + columns) → total → write it here vs QTO → then tighten the biggest gap.
