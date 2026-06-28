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
- `Core/ScheduleGridReader.cs` (`5397eb66`): reconstructs the shear-wall schedule grid —
  `ReadLevelLadder` (ordered unique levels top→bottom) + `ReadThicknessCells` (thickness on the tight
  baseline left of each `WALL` token, tagged to its level). Codex-reviewed (GO). Proven on p-61:
  ladder `L20…P7`, 22 thickness cells, exact `6/12/30/32/36/42"`, column x=1294 reads 30→36→42"
  top-to-bottom (real per-level thickening).
- CLI probes (additive, in `TakeoffCli/Program.cs`): `vector-dump <pdf> <page> [textfilter]` and
  `vector-sched <pdf> <page>`. The OLD `vision-estimate` mode still exists and still works — it is the
  live path until the vector path replaces it.

## What is NEXT (the schedule → takeoff path)
1. Bind each thickness column (x) to its mark name (W1–W5) — find the mark header row, nearest-x.
2. Form level **bands** (each thickness runs from its level down to the next thickness for that mark).
3. Emit `ScheduleTakeoff.WallBand` records → feed the EXISTING `ScheduleTakeoff` math.
4. **THEN delete** the vision schedule-reader it replaces (`ReadWallScheduleJsonAsync` etc. in
   `Vision.cs`) — first dead-code removal.
5. Then: column schedule (same pattern), slab footprint from native geometry, then the holistic
   estimator-synthesis pass. Slab transfer-thickness / un-called-out foundations stay FLAGGED, never
   invented.

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
