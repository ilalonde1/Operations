# Stickfile → Takeoff: the `estimate` pipeline

**Goal.** Feed the tool a structural stickfile PDF and get back the same orange-celled
concrete + rebar takeoff xlsx the app already produces from a Revit schedule — but with
**no Revit export, no manual measurement.** The drawing is the only input. Success is
measured as *how close the output lands to a manual takeoff*, because the manual takeoff
is the thing we are replacing. Validated 2026-06-27 against Coronation Heights Tower 1
(stickfile vs the engineer's prelim QTO): typical tower plate **9,597 sq.ft measured vs
9,800 in the QTO (−2.1%)**, scale and thickness read off the sheet, core void auto-excluded.

---

## Why this is tractable (what the Coronation run proved)

1. **KOR already draws the answer.** The framing sheets are titled *"… PLAN — CONCRETE
   OUTLINE"*. The heavy black boundary *is* the quantity boundary — it is drawn for takeoff.
   The flood-fill target is explicit, not inferred.
2. **The building is ~12–16 unique plates, not 57 floors.** Towers repeat. One sheet is
   *"LEVEL 16 & 17-28 PLAN"* — one plate stands in for 12 storeys. Measure the unique
   plates, multiply by storey counts from the level schedule.
3. **Scale is deterministic.** The title block states `1/8" = 1'-0"`, the page is full-size
   30×42 ARCH-E (2160×3024 pt), so at render DPI *d*: `ft_per_px = (8 ft) / (d px/inch)`.
   No grid-measuring needed; the grid is a cross-check, not the primary scale.
4. **Thickness is labeled** on the plate (`8" SLAB`).
5. **The core void falls out for free.** Elevator/stair cores are drawn hatched (dark); the
   interior-light flood skips them, so the measured area excludes the void without any
   special handling. This is why the measured area matched the *net* QTO number.

---

## Architecture — two layers

### Layer 1 — Geometry engine (VALIDATED, deterministic)
Pure measurement from a rasterized plan region. No AI. This is the IP that saves the hours.

- **`MeasurePlateArea(png, cropBox, darkThreshold, metersPerPx) → area_m2`**
  1. Threshold to a dark mask (luminance < ~110): keeps the heavy concrete outline, drops
     thin gray grid lines.
  2. Dilate the dark mask 1 px to seal hairline gaps in the outline.
  3. BFS flood-fill the **exterior** over *light* pixels, seeded from the **entire crop
     border** (not one corner — grid lines partition the margin into compartments; a
     single seed gets trapped, the full-border seed does not).
  4. Area = light pixels the exterior flood never reached (= enclosed slab, core void
     already excluded because it is dark-hatched).
  5. `area_m2 = interiorPx · metersPerPx²`.
  - *Gotcha (learned the hard way):* do **not** try to take "largest interior connected
    component of NOT-exterior" — dark grid lines bridge the slab interior out to the border,
    so the whole plate gets tagged border-touching and dropped. Count interior *light*; it
    is the robust estimator and is conservative by ~the interior-dark text area (≈2%).
- **`MeasureFootprint(png, cropBox, fillColor±tol, metersPerPx) → area_m2`** — gray-fill
  pixel area for wall/column footprints (walls and columns are gray-filled on the concrete
  outline). Footprint × storey height = wall/column concrete.
- **`MeasureMatZones(png, cropBox, …)`** — foundation: flood-fill each mat zone, × its
  labeled thickness.
- **`ScaleFromTitleBlock(pageText) → metersPerPx`** — parse `1/8"=1'-0"`, `3/16"=1'-0"`,
  etc.; combine with page size + render DPI. Fallback: grid-bubble spacing if scale absent.

### Layer 2 — Orchestration + vision (the build)
Drives Layer 1 across a whole stickfile. This is where Claude vision does what pixels can't.

- **`ClassifySheet(png, pageText) → {kind, level(s), plateBoxes[]}`** — vision reads the
  title block: is this a framing/concrete-outline plan, a foundation plan, a schedule, a
  detail? Which storey(s) does it cover? Where are the plate(s) on the sheet (2-up sheets
  are common)? Text alone is too noisy — the sheet-number spine (`S2.10`…`S2.17` = framing)
  plus a vision read of the title is reliable.
- **`ReadThickness(png, plateBox) → inches`** — vision reads the `8" SLAB` callout(s);
  flags multiple thicknesses on one plate (drop panels, transfer slabs).
- **`ReadLevelSchedule(stickfile) → [{level, storeyHeight, count, usesPlate}]`** — the
  storey list + heights, off the drawing's own level schedule (NOT the QTO). This is what
  turns "unique plate" into "× N storeys."
- **`CalibrationProfile`** — per-project rebar ratios (lb/cy) per element, the orange
  variable cells. Seeded by region preset, overridable. Coronation (BC, moderate seismic)
  measured: **slab 199, wall 375, column 385**. San Diego (Lindley, high seismic) runs
  ~2× on walls/columns. The engineer sets one number per element; the tool does the rest.
- **Confidence + two-method cross-check.** Where the grid spacing is dimensioned, measure
  scale both from the title block and the grid; agree → high confidence, disagree → flag.
  Plate area sanity-checked against the storey footprint envelope.

---

## Output
Identical to the app's single-issue takeoff (`StructuralTakeoffReportGenerator.BuildXlsx`):
one row per element/level, concrete volume, rebar weight, **orange calibration cells** for
the ratios and any per-plate overrides, a confidence column, and the same cover/branding.
The estimator opens it, adjusts the orange cells, done — minutes instead of hours.

## Pipeline flow
```
stickfile.pdf
  └─ pdfinfo → page size; pdftoppm → per-sheet PNG @110dpi; pdftotext → title-block text
       └─ Layer2.ClassifySheet  ─────────────► {framing | foundation | schedule | detail}
            framing/foundation sheets:
              ├─ Layer2.ScaleFromTitleBlock → metersPerPx
              ├─ Layer2.ReadThickness       → in
              ├─ Layer1.MeasurePlateArea / MeasureFootprint / MeasureMatZones → m²
              └─ × thickness → concrete m³ per plate
       └─ Layer2.ReadLevelSchedule → storey counts × per-plate volumes → element concrete
       └─ CalibrationProfile ratios × concrete → rebar
       └─ StructuralTakeoffReportGenerator.BuildXlsx → estimate.xlsx
```

## Build order (Codex)
1. **Layer 1 into Core** as `Kor.Operations.EngineeringTools.Core.PlanGeometry` — port the
   validated flood-fill (`MeasurePlateArea`) + footprint + mat-zone, with unit tests against
   the Coronation typical plate (expect 9,597 sq.ft ±3% from the bundled crop).
2. **CLI mode** `takeoff estimate <stickfile.pdf> <out.xlsx> [profile]` wiring Layer 1 +
   a thin Layer-2 stub (manual crop/scale/thickness config) → emits the app xlsx. Proves
   the wiring end-to-end before vision.
3. **Layer 2 vision** via the app's Anthropic API path (same as PdfToSafe vision-fusion):
   ClassifySheet / ReadThickness / ReadLevelSchedule, with the two-method scale cross-check.
4. **CalibrationProfile** presets (BC-moderate, SD-high-seismic) + orange-cell round-trip.

## Non-circularity rule
Concrete is **measured from the drawing**. Storey counts/heights/thicknesses are **read from
the drawing**. The only human input is the rebar ratio per element (one number, calibratable).
Nothing is lifted from the QTO — the QTO is the score sheet, never an input.

---

## Implementation status — 2026-06-27

**Built and tested (deterministic backbone):**
- `Kor.Operations.EngineeringTools.Core/PlanGeometry.cs` — Layer-1 engine: `MeasureEnclosedArea`
  (flood-fill plate area), `MeasureGrayFootprint` (wall/column footprint), `MetresPerPixel`
  (title-block scale parser). Pure pixel buffers, no image dependency. **Parity-verified** against
  the Coronation typical plate: 9,597 sq.ft slab and 355 sq.ft gray footprint, bit-for-bit with the
  field measurement.
- `PlanReconciler.cs` — the diligence engine + `PlanProfile` (regional rebar norms: BC-moderate
  199/375/385, SD-high-seismic 290/500/700). Flags `SLAB_TOO_THICK` (the L01 transfer-slab case),
  non-positive area/thickness, scale disagreement, out-of-band ratios; returns confidence + reasons.
- `PlanEstimatePipeline.cs` — `MeasuredPlate` → priced concrete → `StructuralTakeoffInput[]`, reusing
  the existing `StructuralTakeoffService` + report generator (the orange-celled xlsx).
- CLI: `takeoff measure …` (geometry probe) and `takeoff estimate <config.json> <out.xlsx>`
  (full run → xlsx + diligence report). ImageSharp decode. **Additive** — existing `single` /
  `overlay` / `rebar` / diff modes untouched and regression-verified.
- 34 new unit tests (98 total in Core.Tests, all green).

End-to-end on Coronation: tower band reproduces the manual QTO to **−2.0%**; the tool **itself**
flags the L01 transfer slab as needing thickness confirmation — the diligence loop, in code.

**Built and tested (Vision Layer 2 — auto-perception):**
- `PlanVision.cs` — `SheetReading` / `PlateReading` + `PlanVisionParser` (defensive JSON → typed
  reading: unknown enums degrade, boxes clamp/order, non-object root → empty, a missing box is
  *degenerate* not whole-sheet so the caller skips it).
- `TakeoffCli/Vision.cs` — `PlanVisionClient`: the firm's Anthropic path (`KOR_ANTHROPIC_KEY`,
  `claude-sonnet-4-6`, forced `report_sheet` tool for guaranteed JSON, retry on 429/5xx,
  `max_tokens` truncation guard). Classifies the sheet and locates each concrete-outline plate
  (level, count, element, thickness, normalized box).
- `PlanGeometry.MeasureEnclosedRegions` + **`MeasureEnclosedClusters`** — segments a crop into
  enclosed regions, then union-find-merges a plate's grid bays back into one cluster (gap scales
  with render DPI). The largest cluster *inside the vision box* is the plate: a clipped neighbour
  contributes only a partial fragment (loses to the full target), boxed annotations are sub-threshold
  strays, and a comparable second in-box region is **flagged for human verification**, never silently
  summed. Proven on a crop spanning two side-by-side plates: returns one plate (9,531 sq.ft / 1.80M px),
  not the 19,310-sqft sum.
- **Walls + columns, automatic.** `PlanGeometry.MeasureGrayComponents` segments the solid-gray fill
  (the calibrated ~208–223 tone) into blobs; `ClassifyVertical` splits column (compact) from wall
  (elongated ≥4 or footprint >25 sq.ft) — validated on Coronation (columns elong 1.0–2.6, walls
  5.6–36.8, clean gap at 4). `vision-estimate` derives, for each slab plate, the co-located vertical
  concrete (footprint × storey-height × floor-count) confined to the plate's vision box, with
  per-sheet centroid **de-duplication** so a column shared by two overlapping boxes is counted once,
  and a skip when the slab pass already distrusted the box. Storey height comes from the config
  (`storeyHeightIn`, per-page overridable). Measured tower vertical footprint 319 sq.ft/floor.
- **Transfer-slab diligence (the other half of the L01 lesson).** `IsTransferProneLevel` +
  `TRANSFER_LEVEL_THK`: a thin plan callout (≤24") on a transfer/podium/mat/lowest-level plate is
  flagged for thickness confirmation, so a built-up transfer that reads 12" on the plan can't sail
  under the SLAB_TOO_THICK wire. (Reading the exact depth off the section detail is the next step;
  the flag guarantees it can't pass silently meanwhile.)
- CLI: `takeoff vision-estimate <pages.json> <out.xlsx>` reads every sheet with Claude, measures slab
  area (cluster geometry) + walls + columns, prices + reconciles through the same pipeline/xlsx.
  `takeoff graycomp …` is the gray-tone/shape diagnostic used to calibrate the thresholds from
  evidence. Walls/columns remain available in deterministic `estimate` mode too.
- 28 new unit tests (126 total in Core.Tests, all green). Three adversarial audits run and closed
  (over-measure, scale-confirmation, wrong-plate/double-count, glyph-chaining, cross-plate vertical
  double-count, untrusted-box vertical, batch-fatal paths, transfer-level under-read).

End-to-end on Coronation, **fully autonomous from the raw PNGs**: both copies of the tower plate read
9,520 / 9,513 sq.ft (consistent to 0.07%, ~−3% vs the manual QTO) with zero human input; the L1 mezz
plate's box ambiguity is surfaced as a verify-flag rather than guessed. Known hard edge: a built-up
transfer slab whose plan callout reads a thin nominal thickness (the L01 case) needs the *section*
read, not just the plan — consistent with the standing "thickness is the swing" finding.

**Whole-building run (all 77 stickfile sheets, autonomous).** The vision layer classifies every sheet
and processes only the framing/foundation plans. Two structural realities of a real set surfaced and
are now handled:
- **Cross-sheet duplication.** A floor is drawn on several sheets (multi-issue reprints, formwork vs
  reinforcing copies, enlarged partials). A building-wide signature de-dup (kind + the sorted set of a
  sheet's level labels; first sheet wins) prevents the 3–4× multiply-count that a naive 77-sheet sum
  produces. The prompt now also takes level/count from the plan TITLE, never a schedule's level range
  (a 'LEVEL P7–L1 SCHEDULE' header was being read as a 7-level count on a footings plan).
- **Foundations are not slab×thickness.** A FOUNDATIONS/FOOTINGS plan is footings (F/SF marks), deep
  core footings (84"/96"/144"), pile caps and SOG — schedule-driven, no single slab thickness. The
  prompt now returns null thickness there (so it flags THK_UNRESOLVED rather than priced 0), and the
  footing/core-mat VOLUME is the next module (read the footing schedule + measure the hatched core-mat
  regions × their labelled depths). Suspended parkade slabs (which DO have a thickness) are captured.

So: per-sheet measurement (slab area, walls, columns, thickness, count) is solid and the superstructure
rolls up de-duplicated; the foundation footing/core-mat concrete is a known, flagged gap, not a silent
zero or a guess.

**Deferred refinement:** surface the vision measurement-ambiguity flag inside the xlsx diligence
report (today it is a clear console `~` warning); plumbing it through `MeasuredPlate` → pipeline →
report is the clean next step.
