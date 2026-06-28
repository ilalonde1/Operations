# Takeoff Scorecard — objective state vs the QTO benchmark

> Updated every iteration. This is the honest "are we there yet." Benchmark = Coronation 31044-01
> manual QTO: **38,705 cy** (Slab 24,495 / Wall 8,339 / Column 2,353 / Foundation ~3,518); rebar
> 8,910,454 lb. The QTO is the ANSWER KEY, never an input.

## As of 2026-06-28 (SLAB ACCURACY PASS — now trustworthy, not error-cancelling)

**Architecture: VALIDATED + WIRED END-TO-END.** `vector-takeoff <pdf> <pngDir> <out.xlsx>` runs the
whole synthesis-led pipeline on all 77 pages and writes the existing ClosedXML workbook. SLAB element
only (walls/columns/foundations not yet wired), so compare to QTO slab 24,495, not the full 38,705.

**Slab total progression (each step VERIFIED on real pages, not estimated):**

| Step | Slab cy | % of 24,495 | What changed |
|---|---|---|---|
| Raw end-to-end | 12,951 | 53% | first full run |
| + typical-floor multiplier | 18,186 | 74% | but ERROR-CANCELLING (see below) |
| + title-block-by-position, tiling, deterministic thickness | 18,547 | 75.7% | **trustworthy** — the cancelling errors fixed |
| + degenerate-box guard | **20,353** | **83%** | CONFIRMED; guard fired on 2 degenerate boxes (38, 43 — both 789 sqft -> median) |

The jump from 74% to 75.7% is small in number but a QUALITATIVE change: the old 74% was a coincidental
balance of a ~5x mezzanine over-count, gross area, and missing floor bands. Those are now FIXED, so the
~76% is real and STABLE, not a sum of canceling errors. The thickness wobble (P7 read 12"/4"/8" across
runs) is gone — thickness is read deterministically from the drawing's "10\" SLAB" callouts.

What landed this pass (all committed, 203 Core tests green):
- **`SheetTitleReader` reads by POSITION** (title-block region), not a page-wide line scan. Level from
  the largest "PLAN" baseline on the right edge; match-line half from the sheet-number suffix
  ("S2.02-N"). Fixed the mezzanine (5 sheets -> 2 halves) with ZERO false positives on 77 pages. The
  earlier regex-broadening that regressed Level-1 is obsolete — position read is the right tool.
- **`FloorMultiplier.TileTowerCounts`** — typical plans tile the tower contiguously (boundaries = rep
  levels), so the floors no band names (16, 29-37, 39-41) are no longer orphaned. Each multiplied plate
  is FLAGGED as an inferred stack.
- **`SlabThicknessReader`** — field thickness from the exact "N\" SLAB" callout (modal, skips SOG /
  columns / slab-bands). PRIMARY; synthesis is fallback only where no callout exists.
- **Degenerate-box guard** — a tower plate < 40% of the tower median area is a bad locate; substitute the
  median, FLAGGED (one bad box on a 10-floor typical plan was costing ~1,600 cy).

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

## Remaining gaps to close the slab (priority order)
The mezzanine / tiling / thickness / degenerate-box items above are now DONE. The slab is at a
CONFIRMED, trustworthy **83% (20,353 cy)**; the rest of the gap to 24,495 is, in order:
1. **Slab bands / drop panels / thickenings** — the "24\" SLAB" / thickened strips along column lines
   add real concrete the QTO counts but we don't (we measure field slab area x field thickness only).
   Likely the single biggest remaining chunk (~10-15% of slab volume). Read the thickening callouts +
   their footprints and add as extra volume (FLAG where the footprint must be inferred).
2. **Gross vs net poche** — `MeasureEnclosedClusters` returns gross enclosed area (shafts/stairs/
   elevator cores/ramp voids not deducted) -> over-measures, partially OFFSETTING #1. Net it out so the
   two real errors stop hiding each other.
3. **Residual area variance** — the locate box still varies run-to-run a little (thickness no longer
   does). The degenerate-box guard catches gross failures; consider median-of-N or a vector-area
   cross-check for the rest.
4. **Per-page scale from `ScaleNote`** (Codex #2) — Coronation is uniformly 1/8"=1'-0" so no accuracy
   impact here; parse ScaleNote for other firms/scales. Generality only.

## Then: the other elements (toward the full 38,705)
5. **Foundations** — type mats as `TakeoffElementType.Foundation` (own rebar density), give them a
   canonical identity, read mat thickness. Currently FLAGGED + excluded (correct, but missing volume).
6. **Walls / Columns** — schedules exist (`ScheduleGridReader` / synthesis reads PC1-4); need key-plan
   wall lengths + column counts to price them.

## Next concrete step
Slab bands / thickenings (#1): read the thickening callouts and footprints, add the extra volume with
provenance, re-run, re-measure here. Then net out gross-vs-net (#2). Then start foundations (#5).
