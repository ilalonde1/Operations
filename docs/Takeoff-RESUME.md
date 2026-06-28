# Takeoff — AUTHORITATIVE RESUME (read this FIRST after any compaction)

> If you are resuming this work: read THIS file + `Takeoff-Scorecard.md` + `Takeoff-Vector-Rebuild.md`
> before doing anything. Everything below is SETTLED. **Do NOT re-derive, re-prove, or relitigate it.**
> The repeated failure mode is wasting a session re-discovering decisions already made here.

## How I work this (the autonomy model — user-granted for the takeoff engine)
- **Autonomous**: I make the edits myself, `dotnet build` + `dotnet test` locally, **commit each working increment**, and keep THIS file + `Takeoff-Scorecard.md` current every increment. No Codex round-trip per change.
- **Adversarial Codex review** at the END of a multi-step feature (not mid-stream) — `codex exec -s read-only`.
- Branch: `develop`. Never publish; build only. Kill `takeoff.exe`/`dotnet` before rebuild if the DLL is locked.
- Synthesis runs are THROTTLED (~5-8 min for 74-77 pages) — run in background, don't idle-poll, verify the total when it lands.

## What this is
A structural takeoff engine that reads vector PDF drawing sets "like an estimator" → accurate concrete/
rebar quantities + analysis, with **anything unsure flagged ORANGE** (variable input). End-goal product:
an **app button → on-screen synopsis of unsure areas → export nice xlsx → (later) AI crucible to converse
about unknowns.** Must be GENERAL (any drawing) and live in the **Core engine**, callable from the WPF app —
NOT stranded in the CLI.

## SETTLED architecture (do not relitigate)
- **Synthesis-led 3-layer**: L1 deterministic exact extraction from vector text/geometry → L2 Claude
  synthesis for JUDGMENT/LOCATION only (classify sheet, locate plate) — **NEVER reads digits** → L3
  deterministic verification. Numbers ALWAYS come from exact vector text; vision only points at regions.
- The manual takeoff is the **ANSWER KEY, never an input**.
- All judgment lives in **Core** (`Kor.Operations.EngineeringTools.Core`), tested. The CLI
  (`TakeoffCli/Program.cs`, command `vector-takeoff`) is a thin/test harness + probes.

## THE BIG CORRECTION (this is why "83%" was wrong)
Coronation's QTO "Summary" Total of **24,495 cy INCLUDES a 4,287 cy mat footing** (row "Ftg", 85").
The pipeline EXCLUDES foundations. **Apples-to-apples suspended-slab target = 20,208 cy** (mat out) /
19,808 (also SOG out). Pipeline produces ~20,353 cy → **~100.7%**, BUT the total is ERROR-CANCELLING.

## Answer keys
- **PRIMARY = 31065 Revit** (5380 Heather St — a DIFFERENT building, so it doubles as not-a-one-off proof).
  Clean per-level slab volume, **net of openings**, no ETABS averaging, no mat-in-slab. Vector drawings, 74pp.
  - Revit floors:   `C:\Users\ilalonde\Desktop\Rory\_source-csv\31065-floors-{before,after}.csv` (m³/level, TS/TN)
  - walls/cols/ftgs: `...\_source-csv\31065-{walls,columns,foundations}-{before,after}.csv`
  - Drawings:       `C:\Users\ilalonde\Desktop\Structural Quantity Takeoff Demo\Inputs\31065 - AFTER (IFC 2026-03-06).pdf` (and BEFORE)
  - REAL takeoffs:  `C:\Users\ilalonde\Desktop\Rory\_archive\31065 - REAL *.xlsx`
  - Floor total (IFC/after) = 10,135.60 m³ over 140 floor elements.
- **ROBUSTNESS = Coronation 31044** (the hard/"weird" set — open-boundary podium, ETABS QTO).
  - Drawing: `scratchpad/coron_stick.pdf` (77pp). PNGs pre-rendered: `scratchpad/full/p-NN.png` (4620x3300 @110dpi).
  - QTO: `scratchpad/coron_qto.xlsx` (Summary tab = ETABS per-level slab; Total 24,495 incl. 4,287 mat).
  - Scale uniformly 1/8"=1'-0"; `mpp = PlanGeometry.MetresPerPixel("1/8\"=1'-0\"",110)`.

## Coronation per-level reality (the REAL remaining errors — they CANCEL in the total)
| Group | Ours cy | QTO cy | Δ | Root cause |
|---|---|---|---|---|
| L01 podium | ~301 | 2,059 | **−1,758** | poché LEAKS on open boundary; box right, enclosed only 9,044 of 30,400 gross |
| LM mezz | ~1,295 | 349 | **+946** | poché grabbed full plate for a ~9,723 partial mezz (open-to-below) |
| Roof L45–47 | ~49 | 442 | −393 | one small ROOF plate; L45 alone is 9,800 sqft |
| L14–15 | ~369 | 595 | −226 | drawing says 8", QTO says 12" → a QTO-vs-drawing **flag**, not a fix |
| Parkade P1–P6 | ~6,663 | 6,235 | +428 | gross-vs-net (ramp/shaft voids) |
| L44 | ~629 | 423 | +206 | cosmetic tiling label only; count is right |

**L01 is NOT cleanly crackable**: vector is shattered (5,474 subpaths, all 2-pt segments, NO closed slab
polygon — confirmed); raster poché leaks (69% of the box is exterior). Stronger sealing won't fix a boundary
that mostly isn't there. → These belong in ORANGE (flag + variable input), not brute-forced. That's the product.

## What is BUILT (Core, tested, committed)
- `SheetTitleReader` (level/zone by title-block position), `FloorMultiplier` (typical-floor tiling),
  `SlabThicknessReader` (field thk from "N\" SLAB" callout, modal), `DrawingDigest`/`VectorPageReader`,
  `ScheduleTakeoff`, `PlanEstimatePipeline`, `StructuralTakeoffReportGenerator` (xlsx).
- `SlabThicknessZoner` + `PlanGeometry.ThicknessZoneFractions` — multi-thickness Voronoi zoning.
  **GATED OFF** (`const bool tkApplyZoning = false` in Program.cs): the QTO models each level at a SINGLE
  thickness, so zoning OVER-counts typical floors ~12%. Capability kept (probe `vector-zones`, 9 tests).
  Do NOT re-enable unless a future answer key is itself zone-resolved.
- `PlateReliability` + `PlateReliabilityScorer` — the **orange-flag engine** (High/Med/Low + reasons from
  fill-ratio, fragmentation, degenerate box, thickness source, peer-area outlier). Tested, NOT yet wired.
- 221 Core tests green.

## NEXT STEPS (ordered — update statuses as you go)
1. **Wire reliability into the pipeline**: per plate compute fillRatio (one `MeasureEnclosedArea` pass over
   the located box) + peerAreaRatio (vs level-group / tower-band median) + ThicknessSource; attach a
   `PlateReliability` to each plate.
2. **Extract orchestration into Core** `SlabTakeoffEngine.RunAsync(pdf,pngDir,opts) → TakeoffResult`
   (plates + reliability + totals + flags). CLI `vector-takeoff` becomes a thin caller. **This is the
   "runs in the app engine, not the CLI" move the user keeps demanding.**
3. **Generalize `SheetTitleReader` for TS/TN zones** (31065 two-tower gap) — keep it position/regex-general,
   re-run Coronation to prove no regression (it broke once before by over-broadening — read Scorecard note).
4. **Run 31065 end-to-end** → first clean accuracy baseline vs the Revit floors (need to render its PDF to
   PNGs first, same as Coronation's `scratchpad/full`).
5. **Orange xlsx**: conditional orange fill + variable input cells + "ASSUMPTION — verify" notes on flagged plates.
6. On-screen synopsis model in `TakeoffResult` (drives the app panel). Then (later) the AI crucible layer.

## Commits so far (develop)
- `1f89bb1c` thickness-zoning capability (gated) + corrected slab benchmark
- `b7d6ed75` per-plate reliability/confidence model in Core (orange-flag engine)

## Run commands
- Slab takeoff: `takeoff vector-takeoff <pdf> <pngDir> <out.xlsx> [first] [last]`
- Probes: `vector-digest` (no AI), `vector-geom`, `vector-words`, `vector-plate`, `vector-zones`, `measure`.
