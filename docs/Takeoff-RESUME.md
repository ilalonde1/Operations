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
- `f1fb9a26` this RESUME doc
- `01590d56` #22 SheetTitleReader reads TOWER NORTH/SOUTH zones (31065 two-tower)
- `b1867fbf` #20 wire reliability into pipeline + on-screen synopsis (232 Core tests)
- `c928275b` METRIC thickness: read "200 SLAB" (mm) not just imperial inch marks (236 Core tests)

## HOPPER BUILD (2026-06-28, user said "build the hopper") — IN PROGRESS
The multi-signal cascade. Built as tested Core increments (all judgment in Core; CLI thin):
- **Inc 1 DONE** `1ce1c2b1` `StructuralGridReader`/`GridFrame` — the AREA ANCHOR (grid-bubble envelope,
  tower id, MultiPlan flag). 8 tests. `vector-signals` calls it (single source of truth).
- **Inc 2 DONE** `a5edd8e3` `SlabAreaReconciler` — grid ⟷ poché converge → `AreaConsensus` + PlanFlags
  (GridConfirmed / AREA_POCHE_LOW/HIGH / GridOnly / PocheOnly / Unresolved). netFactor 0.92 (calibratable). 8 tests.
- **Inc 3 DONE** `81bfa2cb` wired into `SlabTakeoffEngine`: per plate reads grid + reconciles; grid is primary
  area, poché confirms/flags; flags ride `MeasuredPlate.ExtraFlags`→PlanCheck. Same effective scale both signals.
- **Inc 4 DONE** `98ae78dd` `SlabThicknessZoner` reads metric "200/450/900 SLAB"; CLI `vector-takeoff` gains
  `[scale]` arg (use "1:100" for 31065 — imperial default is ~1:96, ~8% area low).
- **Inc 5 TODO** grounded-AI adjudication hook: when AreaBasis=Unresolved or callouts garbled (podium/P3),
  focus a READ-ONLY grounded AI on that spot (read grid dims/confirm boundary) → resolve OR honest orange.
  HARD RULE (Ian): never hallucinate; genuine unknown stays orange.
- **Inc 6 TODO** multi-plan split (typical band drawn as 2 plans), podium handling; re-run 31065 vs Revit.

### VERIFY RUN DONE (2026-06-28) — hopper 12,688 cy vs Revit 13,257 = 95.7% (BUT error-cancelling — read carefully)
`run_hopper.txt`/`hopper-31065.xlsx`, pages 12-51 @ 1:100. The 95.7% is NOT real accuracy — it is L1 podium
UNDER (−1,953) cancelling L6-18 south OVER (+2,880). The HONEST win: **every materially-wrong plate
self-flagged ORANGE; the GREEN plates are accurate** (2 NORTH 372.7cy vs Revit L2-N 358cy = 96%). Per-floor:
| Floor | Revit | Hopper | Status |
| P3 | 640 | 0 | EXCLUDED no-thk, flagged |
| P2 | 1027 | 696 | 🟠 TRANSFER_THK |
| P1 | 791 | 564 | 🟠 TRANSFER_THK |
| L1 | 3109 | 1156 | 🟠 POCHE_LOW×2  **−1953** |
| L2 | 882 | 690 | 2N🟢 / 2S🟠 |
| L3 | 618 | 588 | 3N🟢 |
| L4 | 673 | 588 | 4S🟢 |
| L5 | 598 | 532 | 5N🟢 |
| L6-18 | 4277 | 7157 | 🟠 POCHE_LOW  **+2880** |
| L19 | 503 | 717 | 🟠×2 |
| ROOF | 132 | 0 | out of page range |
**#1 fixable defect (Inc 5/6 target):** `18 SOUTH` took grid env 16,979 sqft (grid `9..13×B..F` = podium width,
MISSED the tower setback) while sibling `18 NORTH` correctly caught it (`2..5×B..F`=5,319). Poché (6,656) was
CLOSER to truth and got overruled. Rule to add: on a TYPICAL tower plate, when grid≫poché AND a sibling tower's
setback envelope is far smaller, distrust the grid (it grabbed podium-width bubbles) → adjudicate / use poché.

## SIGNAL-EVIDENCE SWEEP (2026-06-28) — proof before building the "hopper" (user-directed)
User reframed the architecture: a CASCADE ("hopper down the pegs") of INDEPENDENT area signals that
converge → green, diverge → grounded-AI adjudication on that one spot → honest ORANGE if truly unknown
(NEVER hallucinate). Before building, proved each signal with the `vector-signals` probe (Program.cs, no AI),
on clean-tower(p30)/podium(p12)/parkade(p16,p14,p20)/south-fail(p40), metric 1:100:
- **Vector polygon (shoelace): DEAD** — segment soup, largest closed path ≤5,208 sqft (columns), no slab outline.
- **Stroked envelope: USELESS** — catches the sheet frame (53,103 sqft on every sheet).
- **Filled-region sum: PARTIAL** — meaningful only on podium (32,172 sqft, slab drawn as fill), tiny on towers.
- **Raster poché (in AI box): WORKS on dense sheets** (tower 12,998≈Revit), FAILS on sparse concrete-outline
  (south 449). Reinforcing-sibling fallback (tower-zone fix 6a8795ab) recovers most failures.
- **GRID-BUBBLE ENVELOPE: STRONG (the winner)** — estimator's own method. Margin-banded detector reads clean
  sequences: p30 north [1-8]×[A-F], p40 south [9-13], p12 podium [1-13]×[A-F]. Calibrates: L3 north env 16,408
  ×0.78 net ×7.87" = 311cy ≈ Revit 309. RECOVERS THE PODIUM poché can't (20-32k sqft vs 4-7k fragment).
  Bubbles also identify the tower for free (N=1-8, S=9-13). Noisy on parkade (stray "1", partial letters) +
  south-letter band needs widening — refinements, not failures.
- **Thickness zoning (450/900 drop bands): FEASIBLE** — all 22 SLAB callouts located on the transfer sheet;
  needs the same metric fix the thickness reader got (SlabThicknessZoner reads "900 SLAB" wrong) + a valid plate.
PROPOSED HOPPER: area = grid-envelope×net ⟷ poché(in box) converge; tower id from bubbles; thickness =
metric callout + zoning; diverge/noisy → grounded-AI adjudication → orange if unknown. Probe: `vector-signals`.

### Grid-detector PROOF rounds (2026-06-28, deeper)
Net factor CORRECTED: grid envelope (N+S) ≈ true Revit area on typical floors (L3 1.02, L4 0.93); ≈1.0, NOT
0.78 (earlier north-only error). Transfer L2 grid/area=0.71 ⇒ the +40% is the 450/900 drop bands (zoning).
Detector hardened in `vector-signals`: digits = margin row → dominant-height → median-gap trim; letters =
dominant-height (NO column lock, L-shaped plates split A–D/E–F). Results: N towers all [1-8]×[A-F]=19,854;
S towers [9-13]×[A-F]=11,460; podium [1-13]×[A-F×2]=24,185; parkade P1/P3 clean=19,854/20,893 (P2 one stray
"4" → minor trim tune). STABLE per tower (poché varied 6k-13k on same floors; grid identical).
CONFIRMED by image (p36 S2.10.1): typical tower 6-18 is a REAL SETBACK (grids 2-5×B-F, smaller than podium
1-8×A-F) AND that sheet carries TWO plans (odd 7-17 / even 6-18) side-by-side → grid spans both (doubled
[2,3,4,5,2,3,4,5]) → needs MULTI-PLAN SPLIT (detectable: repeated label run + big X-gap). Open detector work:
multi-plan split + P2 stray-trim; both addressable, not fundamental. Net signal verdict: GRID IS THE ANCHOR.

### THICKNESS/ZONING proof (2026-06-28) — last signal
Slab-callout distribution (mm×n, paired number↔SLAB in `vector-signals`): L3 typical 200×2/250×1 (single
field, no zoning needed ✓); L2-S transfer 200×4/900×3/450×1 (the DROP BANDS — the +40% the single field
misses, detectable+quantifiable ✓); P1 250×9/200×1, P2 250×4/600×1/150×1 (field + thick zone ✓); L1 podium
& P3 = — (pairing FAILS on dense/garbled text → AI-adjudication/orange). Verdict: field thickness proven;
drop-band zoning FEASIBLE (bands detectable); podium/P3 callout extraction is the hard residual.

### FULL SIGNAL VERDICT (all proven, 2026-06-28)
AREA: polygon DEAD · stroked-env useless(frame) · filled-sum partial(podium) · poché works-dense/fails-sparse/noisy
· **GRID-ENVELOPE = anchor** (stable, ≈true area ±2-7%, clean on single-plan sheets). THICKNESS: field-modal
proven · drop-band zoning feasible · podium/P3 dense-text = AI. IDENTITY: grid bubbles give tower+level.
RESIDUALS (all characterized → AI-adjudication/orange, none fundamental): multi-plan split, podium/P3 callout
+ podium area, parkade P2 stray-trim. READY to build the hopper on this evidence (pending Ian's go).

## 31065 BASELINE (2026-06-28, #23 verified) — 9,748 cy vs Revit 13,257 = 73.5%
Up from 46% on the metric-thickness fix alone. 18 plates, 13 clear / 5 review. The remaining 26.5% is
NOT silent error — it is 3 levels the engine FLAGGED or DROPPED, exactly as the orange model should:
- **"Level 1" podium DROPPED** (no thickness) + **"1" measured but AREA_SMALL (47% of peers)** — the SAME
  physical level split across two title forms ("Level 1" vs "1") that don't pool/reconcile. Revit L1 = 3,109 cy
  (the big podium), badly under-counted. ROOT: title normalization. **This is the #1 next fix.**
- **"Level 5" DROPPED** (vs "5"/"5 NORTH"/"5 SOUTH") — same title-normalization split.
- **"P3" DROPPED** (15,694 sqft, no thickness/sibling) — P3 parkade callout not read. Revit P3 = 640 cy lost.
  ROOT: P3 sheet's pooled text had no qualifying SLAB callout (maybe SOG wording); inspect its digest.
- TS/TN two-tower tiling WORKS: 18 NORTH and 18 SOUTH each tile 6-18 (13 floors) as separate plates.
- Metric thickness CONFIRMED across the set: tower 7.87" (200mm), parkade 9.84" (250mm).
NEXT (ordered): (1) title normalization ("Level N"→"N", tower-suffix base) so split levels pool/reconcile
+ stop dropping L1/L5; (2) P3 callout/SOG read; then re-run for a new baseline. Then #24 orange xlsx.

## NEXT-SESSION STATE (2026-06-28 autonomous run — read before building)
- **Metric thickness FIX shipped** (`c928275b`): `SlabThicknessReader` now reads metric "200 SLAB" (mm,
  100-600 → ÷25.4 in) alongside imperial. The 31065 trap was "5. SLABS TO BE CAMBERED" matching as 5" and
  overriding synthesis for the whole tower. Pools separate cleanly (inch mark blocks metric; digit-SLAB
  adjacency blocks imperial note-numbering); ≥2 metric ≥ imperial count ⇒ metric wins.
- **#21 Core engine DONE** (`a39fe533`): `SlabTakeoffEngine.RunAsync(SlabTakeoffRequest, IPlanVision,
  IPlanRaster) → SlabTakeoffResult` in Core — the host (CLI today, WPF next) supplies the 2 AI calls
  (`IPlanVision`) + raster I/O (`IPlanRaster`); CLI is a thin caller via `CliPlanVision`/`CliPlanRaster`.
  Returns data (plates, xlsx bytes, totals, Notes trace, Synopsis). 236 tests + 1-page smoke reproduce.
- **Throttle fix DONE** (`30e01eaa`): vision retry honors Anthropic `Retry-After` + exp backoff/jitter, 8 tries.
- The WPF app can now call `SlabTakeoffEngine.RunAsync` directly — that is the "Generate takeoff" button's
  engine. It still needs app-side `IPlanVision`/`IPlanRaster` impls (reuse the firm's Anthropic path + an
  ImageSharp/WPF decoder) and the on-screen synopsis panel binds to `SlabTakeoffResult.Synopsis`.

## DONE this session
- **#20 reliability WIRED** (commit b1867fbf): synopsis works; signal is cluster fill-of-own-extent
  (NOT enclosed/box) + AREA_COMPLEX_LEVEL (podium/roof/mezz by level type, since L01/ROOF measure
  locally-clean yet are ~3x off). Coronation: 5 clear / 20 review — honest. Total holds 20,400 cy.
- **#22 TS/TN** (commit 01590d56): TOWER-label zone read; Coronation zero-regression. KNOWN GAP:
  31065 reinforcing sheets lack the label -> blank zone -> won't dedup vs framing sibling -> would
  double-count. FOLLOW-ON: sheet-block tower inference (a labelled sheet sets its S2.NN block's tower;
  unlabelled siblings inherit). Sheet numbers DO encode tower (N=S2.0x, S=S2.1x) but uneven + OCR-garbled.

## NOW (#23 in progress)
31065 run launched (`run_31065.txt`, xlsx `31065-takeoff.xlsx`). Compare per-level to the Revit answer key
(`scratchpad/31065-revit-targets.txt`, TOTAL 13,257 cy; L1 podium 3,109; typical L6-18 = 329 cy each;
P1-P3 parkade). Watch for: the TS/TN double-count (reinforcing sheets), per-tower tiling, podium/roof flags.

## In flight (this session, autonomous run) — verify then commit
- **#20 reliability WIRED**: reworked `PlateReliabilityScorer` to emit `PlanFlag`s into the EXISTING
  `PlanReconciler`/`PlanCheck` diligence system (NOT a parallel confidence). `MeasuredPlate` gained
  measurement diagnostics (FillRatio, ClusterCount, ThicknessSource, DegenerateBox, PeerAreaRatio);
  `PlanEstimatePipeline.Run` merges pricing + measurement flags via `PlanCheck.From`. CLI computes
  fillRatio (`MeasureEnclosedArea`) + peer ratio + thickness source and prints a SYNOPSIS of plates
  needing review. 222 Core tests. (Verifying via Coronation run `run_reliability.txt`.)
- **#22 TS/TN zones DONE**: `SheetTitleReader.TitleBlockTowerZone` reads a "TOWER NORTH/SOUTH" label
  from the title block (31065's two-tower form — title line has no half). STRICT (title-region +
  title-size + same baseline) to avoid the stray-north-arrow regression. 224 Core tests.
  NOTE: 31065 still needs **per-tower tiling** (two towers share level numbers — TileTowerCounts would
  collide them); that's part of the 31065 run (#23), not the title reader.
- **#23 prep**: 31065 PNGs render via `pdftoppm -png -r 110 "<31065 AFTER>.pdf" <dir>/p` ->
  `scratchpad/31065_full/p-NN.png`. Revit floor answer key = `31065-floors-after.csv` (m³/level, TS/TN).

## Render command (PDF -> PNG for poché)
`pdftoppm -png -r 110 "<set>.pdf" <outdir>/p`  (Poppler; produces p-NN.png at 110 dpi to match the pipeline)

## Run commands
- Slab takeoff: `takeoff vector-takeoff <pdf> <pngDir> <out.xlsx> [first] [last]`
- Probes: `vector-digest` (no AI), `vector-geom`, `vector-words`, `vector-plate`, `vector-zones`, `measure`.
