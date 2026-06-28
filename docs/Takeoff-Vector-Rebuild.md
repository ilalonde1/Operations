# Structural Takeoff — Vector Rebuild (AUTHORITATIVE RESUME DOC)

> **If you are a fresh / compacted Claude session: READ THIS FIRST, then `git log --oneline -15`,
> then run the probes below. Do NOT re-derive the architecture from scratch. Do NOT relitigate the
> decisions here. They were settled with Ian (KOR) over multiple painful sessions. Trust this doc +
> the git history + the code. Ground every claim in the actual files, never in summary memory.**

## The one cardinal rule
The **manual QTO is the ANSWER KEY, never an input.** Measure from the drawing. Never tune a
thickness/area to hit the QTO total — that is circular and destroys generality. Coronation
`31044-01` manual QTO (benchmark only): 38,705 cy total — Slab 24,495 / Wall 8,339 / Column 2,353 /
Foundation ~3,518; rebar 8,910,454 lb.

## THE GOAL (full product vision — read this)
Read a structural drawing set **like an estimator** (plans + schedules + sections + general notes,
cross-referenced) and produce (1) **accurate quantity takeoffs** (concrete by element+level, rebar)
and (2) **analysis** (the reasoning, assumptions, flags). General across any set (manual QTO is the
benchmark, never an input). **End state: an AI/MCP "crucible" layered on top** so users ask natural-
language questions about the estimate ("why is wall concrete higher at P6?", "what did you assume for
the transfer slab?"). => The output MUST be a **structured, traceable estimate model** (every quantity
carries provenance: sheet/schedule/assumption + confidence/flag), because that model is what MCP
queries. Build for explainable+queryable from the start — do NOT build a number-only pipeline.

## THE WINNING ARCHITECTURE — synthesis-led, 3 layers (Ian chose this 2026-06-27; DO NOT revert to per-element deterministic parsers — that long road is why we never finish)
- **Layer 1 — Deterministic extraction (exact, NO AI):** raw facts from the vector — text lines per
  sheet (`VectorPageReader.ReadTextLines`), geometry primitives + areas/lengths, structured schedules
  where regular (`ScheduleGridReader`). Numbers ORIGINATE here, so they're trustworthy.
- **Layer 2 — AI synthesis (Claude over Layer 1, the estimator's judgment):** classify sheets,
  associate geometry to elements, cross-reference schedule↔plan↔section↔notes, read sections for
  transfer-slab/foundation thickness, apply notes defaults, FLAG assumptions. Targeted vision only on
  ambiguous crops. Reasons over EXACT data — NOT pixels (that was the old failure). Emits the
  structured estimate model with provenance+confidence. (sonnet, KOR_ANTHROPIC_KEY.)
- **Layer 3 — Deterministic verification:** recompute totals from the model, internal consistency,
  compare to QTO scorecard. Catches AI drift.
Numbers trace to Layer 1; AI assembles+flags; Layer 3 verifies; missing drawing data is FLAGGED never
faked. The structured model IS the artifact the MCP crucible (next product step) queries.

## DONE v1 (the finite finish line — stop the never-finishing)
End-to-end: Coronation vector PDF -> Layer1 extraction -> ONE Claude synthesis pass -> structured
estimate model -> the nicely-formatted ClosedXML **xlsx** (REUSE the existing exporter) -> total within
honest tolerance of QTO 38,705, gaps flagged not faked, runs clean on Onyx. Proven by
`docs/Takeoff-Scorecard.md` (updated every iteration: total vs QTO, per-element gap, flags).
DISCIPLINE: vertical slice FIRST (get a full rough end-to-end run + measure), THEN tighten the biggest
gap and re-measure. Never perfect one parser in isolation.

## Autonomy mandate (granted by Ian 2026-06-27)
Build this autonomously: implement -> codex-verify substantive logic -> build/test -> commit each
increment -> keep this doc + the scorecard current. Ian verifies via `git log` + the scorecard, not by
driving each step. Be economical with synthesis calls.

## The architecture decision (DO NOT RELITIGATE)
1. **All KOR drawings are VECTOR PDFs** (stickfiles from Revit/AutoCAD). The schedule text,
   callouts, dimensions, and the wall/slab/column geometry are all EXACT machine-readable data.
2. **Read the native vector — never rasterize + OCR.** The old `vision-estimate` path rasterized the
   PDF and used an Anthropic vision model to OCR numbers that already exist as exact text. THAT is the
   "whack-a-mole": ±5% run-to-run noise + endless per-drawing patching. We are replacing it.
3. **Read it like an estimator:** pull exact text + schedules + geometry, then synthesize across the
   sheet set (plan ↔ schedule ↔ section). Deterministic where possible; AI only for genuine judgment
   (classification, association, gap-flagging) — never for reading digits.
4. **DO NOT reuse the PdfToSafe app.** PdfToSafe is a *different app*: it reads the engineer's Bluebeam
   *markup annotations* and intentionally **discards the base drawing** (`GeometryFilterService`
   defaults `annotationsOnly=true`). On a native stickfile its extractor returns ~empty. Wrong tool.
   The takeoff has its **own** vector reader in `Kor.Operations.EngineeringTools.Core`. Zero PdfToSafe
   dependency. (Codex independently NO-GO'd reusing PdfToSafe, 2026-06-27.)
5. **NO per-drawing special-casing, ever.** If a value doesn't extract, fix the extractor *generally*
   or *flag* it. A Coronation- or Onyx-specific patch is the signal the architecture is wrong.
6. **Cleanup discipline:** every commit that adds a vector reader DELETES the raster/OCR code it
   replaces, in the same commit. No two pipelines side by side. Final dead-code sweep before "done".

## What is BUILT (durable — in git, proven on `coron_stick.pdf`)
- `Core/VectorPageReader.cs` (commit `e6f8c089`): reads a page's native content via PdfPig — every
  word with its bbox + every vector subpath with bbox/closed/filled. No raster, no OCR.
- Word grouping (`cfa860b2`): uses `NearestNeighbourWordExtractor` so CAD glyphs group into real
  tokens (`WALL`, `LEVEL`, `30"`, `MPa`) instead of single letters.
- `Core/ScheduleGridReader.cs` (`5397eb66` + `290caa0b`): reconstructs the shear-wall schedule grid —
  `ReadLevelLadder` (ordered unique levels top→bottom) + `ReadThicknessCells` (thickness on the tight
  baseline left of each `WALL` token, tagged to its level) + `ReadWallBands` (bind each cell to its
  nearest mark column W1–W5, fill down into `ScheduleTakeoff.WallBand` records). Codex-reviewed (GO,
  twice). 5 unit tests in `ScheduleGridReaderTests.cs` (170 Core tests green). Proven on p-61: ladder
  `L20…P7`; 22 thickness cells, exact `6/12/30/32/36/42"`; **22 wall bands** — W1=30"/W2=12"/W3=32"/
  W5=6" throughout, **W4 steps 30"(L15-L10)→36"(L9-P5)→42"(P6-P7)**, matching the drawing.
- CLI probes (additive, in `TakeoffCli/Program.cs`): `vector-dump <pdf> <page> [textfilter]` and
  `vector-sched <pdf> <page>` (prints ladder + thickness cells + wall bands). The OLD `vision-estimate`
  mode still exists and still works — it is the live path until the vector path replaces it.

## What is NEXT (the schedule → takeoff path)
DONE: native reader, word grouping, schedule grid, mark binding, fill-down bands → `WallBand` records.
1. **Integration (next):** in `vision-estimate`, read the schedule page from the source PDF and call
   `ScheduleGridReader.ReadWallBands` instead of `PlanVisionClient.ReadWallScheduleJsonAsync`. The
   config currently feeds PNGs; add the source PDF path so the vector reader can run. Feed the bands
   into the EXISTING `ScheduleTakeoff.ComputeWall` (which also needs key-plan lengths — see below).
2. **THEN delete** the vision schedule-reader it replaces (`ReadWallScheduleJsonAsync` in `Vision.cs`)
   — first dead-code removal (replace-and-delete in the same commit).
3. Wall key-plan **length** from native geometry (replaces the noisy vision median-of-3) — measure the
   core-wall polyline lengths from `VectorPageReader` paths.
4. Then: column schedule (same pattern as walls), slab footprint from native geometry, then the
   holistic estimator-synthesis pass. Slab transfer-thickness / un-called-out foundations stay
   FLAGGED, never invented.

## How to resume after a compaction (do this, in order)
1. Read this doc.
2. `git log --oneline -15` — the commits ARE the truth.
3. Build: `dotnet build Kor.Operations.EngineeringTools.TakeoffCli/...csproj -c Debug` (kill stray
   `takeoff`/`dotnet` hosts that lock `Core.dll` first).
4. Prove the state with your own eyes — sample drawing is at
   `…/scratchpad/coron_stick.pdf` (the Coronation stickfile, 77 pages, vector):
   `takeoff vector-sched <coron_stick.pdf> 61`  → should print the ladder + 22 thickness cells.
5. Continue with "What is NEXT". Verify-by-running. Codex-review the substantive logic before commit.

## Working agreements with Ian (KOR)
- Build → verify by running → commit each increment. Surface doubt; never overclaim.
- Verify in REALITY (run it), not just build/analyzers. Honesty over optimism.
- The honest current accuracy of the OLD vision path was ~74% of QTO with honest flags — a WORKING
  ingestor+estimator, not broken. The vector rebuild is to make it accurate, not to rescue it.
