# Takeoff Scorecard — objective state vs the QTO benchmark

> Updated every iteration. This is the honest "are we there yet." Benchmark = Coronation 31044-01
> manual QTO: **38,705 cy** (Slab 24,495 / Wall 8,339 / Column 2,353 / Foundation ~3,518); rebar
> 8,910,454 lb. The QTO is the ANSWER KEY, never an input.

## As of 2026-06-28 (END-TO-END RUN PRODUCED)

**Architecture: VALIDATED + WIRED END-TO-END.** `vector-takeoff <pdf> <pngDir> <out.xlsx>` runs the
whole synthesis-led pipeline on all 77 pages and writes the existing ClosedXML workbook.

**FIRST FULL TOTAL (Coronation, slab element only):** **12,951 cu.yd slab vs QTO slab 24,495 = ~53%**
(28 plates; rebar 2,577,295 lb). Walls/columns/foundations NOT yet wired, so this is the SLAB bucket
only — compare to 24,495, not the full 38,705. Honest baseline, gaps legible (below), nothing faked.

| Layer | Status | Evidence |
|---|---|---|
| L1 — drawing digest (exact facts) | **DONE**, general, tested | `vector-digest`; 175 Core tests |
| L1 — wall schedule reconstruction | **DONE**, tested | `ScheduleGridReader`: W1-W5 6/12/30/32/36/42" |
| L1 — canonical sheet title (level, zone) | **DONE**, tested | `SheetTitleReader`; dedupes halves, reconciles thickness |
| L2 — classify + locate-plate + poche | **DONE, run on 77 pp** | `vector-takeoff`; per-page slab box+level+thickness -> measured area |
| Assembly -> xlsx | **DONE** | existing PlanEstimatePipeline -> StructuralTakeoffReportGenerator |
| L3 — verification + scorecard | partial | this file; per-run total vs QTO. Internal-consistency checks TODO |

What the full run proved works: classify (skips cover/notes/detail/schedule), focused plate-locator +
pixel-poche area, **N-way dedup** by canonical (level, zone) — P1 North's 3 sheets -> 1; P7-P2 each
N/S framing+reinforcing -> one slab/half — and **thickness reconciliation** across match-line halves
(P7 South inherited 12" from North). Foundations with no sibling thickness were FLAGGED + excluded,
not faked.

## Gap analysis from the full run (priority order — tighten biggest first)
1. **Typical-floor MULTIPLIER missing — the dominant UNDER-count.** Tower plans parse as levels
   `1,3,13,15,28,38,43,44`; each "typical" sheet stands for a BAND of physical floors (e.g. 3->12) but
   is counted once (`MeasuredPlate.Count=1`). Building runs to L44+ROOF; ~8 sheets cover ~40 floors.
   FIX: read the floor range/count (title "LEVELS x-y" or synthesis) -> set `Count`. This is `Count`'s
   whole purpose. **NEXT.**
2. **P1 Mezz OVER-counted ~5x.** Title `LEVEL P1 MEZZ PLAN` / `…MEZZ. PLAN - SOUTH` doesn't match the
   reader (MEZZ between level and PLAN; "." punctuation), so 5 sheets fell back to varying synth
   free-text -> no dedup -> phantom plates that partially MASK gap #1.
3. **`SheetTitleReader` generality.** Misses `FOUNDATIONS PLAN`, `MEZZ`, `PLAN - SOUTH` punctuation.
   Fixing -> foundations get canonical identity (N/S reconcile thickness) + mezz dedups. (Foundation mat
   is also still typed `Slab`/wrong rebar density — separate element handling TODO.)
4. **Gross vs net poche.** `MeasureEnclosedClusters` returns enclosed LIGHT area inside the box — likely
   GROSS (openings/shafts/ramps not deducted) vs the QTO's NET concrete -> over-measures area per plate.
   Quantify once #1-3 are fixed (don't chase it while bigger errors dominate).
5. **Walls / Columns / Foundations elements** — not yet wired (schedules exist via `ScheduleGridReader`;
   need key-plan lengths for walls, column counts, mat areas). Needed to compare to full QTO 38,705.

## Next concrete step
Implement the typical-floor multiplier (#1): extract floor range/count per tower plan, apply via
`MeasuredPlate.Count`, re-run full, re-measure here. Then fix `SheetTitleReader` generality (#2/#3),
re-run. Then adversarial Codex review (prompt staged) -> fix -> iterate. Then wall/column/foundation
elements toward the full 38,705.
