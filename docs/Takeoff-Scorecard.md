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

## Adversarial Codex review (done 2026-06-28) — outcome
Ran `codex exec -s read-only` over `SheetTitleReader` / `DrawingDigest` / the `vector-takeoff` handler.
ADOPTED (safe, general, no regression — commit c3d6ec91):
- **Dedup-after-success**: a (level,zone) key is marked counted only after a real measurement, so a
  failed/empty/penthouse first sheet no longer skips the level's framing sheet as a dup (under-count).
- **Multiplier on L-prefixed levels** too (`L13`), not just bare numbers.
- **Per-page digest isolation**: one malformed page emits an empty digest; the run continues.

KEY LEARNING — title parsing is the limiting factor, and MORE REGEX MADE IT WORSE. Broadening
`SheetTitleReader` (MEZZ modifier, zone-after-separator `PLAN - NORTH`, ordinals) was tried and REVERTED:
on this drawing's collision-heavy text it (a) mis-keyed the Level-1 framing sheet from a note that
referenced "P1 MEZZ", and (b) picked up a stray north-arrow `NORTH`, breaking Level-1 dedup. Scanning
all page text for a title pattern is fundamentally noisy. **The real fix (Codex #4) is to read the title
from the title-block REGION** (bottom-right by geometry/font/position via `VectorPageReader` bboxes),
not by scanning every line. That single change unlocks mezzanine zones, foundation identity, and
general zone-after-separator WITHOUT the false positives. This is the next structural investment.

## Remaining gaps (priority order)
1. **Title-block-region reader** (replaces line-scan) — unlocks: P1 Mezz dedup (currently ~5x
   over-count, masking under-counts), foundation identity, general firm title conventions, reliable
   match-line zones. Highest leverage; do before chasing percentages.
2. **Floor-band coverage holes** — levels 16 and 29-41 fall between captured bands (named-after-top vs
   named-after-bottom inconsistency) -> a few floors counted x1. Quantify + close after #1.
3. **Degenerate locate box** — occasional ~789 sqft plate (synthesis returns a tiny box); add a
   plausibility check vs sibling-plate median and re-locate/flag. Reduces run-to-run variance.
4. **Per-page scale from `ScaleNote`** (Codex #2) — Coronation is uniformly 1/8"=1'-0" so no accuracy
   impact here, but hardcoding breaks other firms/scales. Parse the (glyph-jumbled) ScaleNote, fall back
   to default. Generality only.
5. **Foundation element + gross-vs-net** (Codex #3/#7) — type mats as `Foundation` (own rebar density);
   poché currently measures gross enclosed area (openings/shafts not deducted -> over-measure).
6. **Walls / Columns** — schedules exist (`ScheduleGridReader`); need key-plan lengths + column counts
   to reach the full QTO 38,705.

## Next concrete step
Build the title-block-region reader (#1): use `VectorPageReader` word bboxes to read the sheet title
from the title-block zone instead of scanning all lines, then re-enable mezzanine/zone/foundation
identity on top of it with tests, re-run full, re-measure here.
