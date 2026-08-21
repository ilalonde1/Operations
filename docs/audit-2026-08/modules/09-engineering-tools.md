# Module 09 — Engineering Tools (PDF→SAFE · Quantity Takeoff · Rebar Change · Structural Takeoff)

**Auditor run date:** 2026-08-20 · **Branch:** `develop` (clean; only `docs/audit-2026-08/` untracked)

---

## 1. What I searched

**Prior art first (CLAUDE.md rule 1).** Before running anything I looked for existing measurement
work: `docs/Takeoff-RESUME.md` (exists, read in full, 2026-07-03), `docs/Takeoff-Doctrine.md`
(2026-07-04), `docs/Takeoff-Scorecard.md` (2026-06-28), `docs/Takeoff-Vector-Rebuild.md`,
`docs/Takeoff-Estimate-Pipeline-Spec-2026-06-27.md`, `docs/quantity-takeoff/README.md` + 10 sample
CSVs, `docs/architecture/Kor.Operations.{QuantityTakeoff,StructuralQuantityTakeoff}.plan.md`,
`tools/` (no takeoff or PdfToSafe tooling there). Found a **shipped accuracy brief that already
answers the accuracy question** — `C:\Users\ilalonde\Desktop\Structural Quantity Takeoff
Demo\Management Demo 2026-07-04\Structural Quantity Takeoff - Results Brief.md` — read before
measuring anything myself.

**Code read:** `Kor.Operations.App/EngineeringTools/**` (49 .cs, 6 .xaml — count confirmed),
`Kor.Operations.EngineeringTools.TakeoffCli/{Program.cs 2,971 L, Vision.cs, PlanPdfRenderer.cs}`
(3,562 L total), `Kor.Operations.EngineeringTools.Core/*` (51 non-Dxf .cs — the takeoff engine
actually lives here, not in the App), `Kor.Operations.App/EngineeringTools.Tests/**` (28 .cs,
7,182 L), `Kor.Operations.EngineeringTools.Core.Tests/**` (48 .cs).

**Greps:** `anthropic|HttpClient|ANTHROPIC_API_KEY|claude-` across CLI+Core;
`SlabTakeoffEngine|IPlanVision|IPlanRaster` across the App (**zero hits** — see §5);
`TODO|FIXME|HACK|NotImplementedException|NotSupportedException` across all scope paths (**zero
hits**); multiline `catch…{}` empty-catch regex across scope (**zero hits**);
`"[a-z]:[\\/]|Users[\\/]|Desktop|Program Files"` across the test project (1 hit — §5);
`SqlConnection|SqlClient|Data Source=|password=` across scope (**zero hits** outside `Dxf/`);
`e2k|E2K|ETABS|Etabs` in `PdfToSafe/` (16 files); `QuantityTakeoffWindow|RebarChangeWindow|
EngineeringToolsWindow` across the App.

**Git dating (rubric rule 2):** `git log -1 --date=short -- <path>` for every scope path, plus
`git log --since=2026-07-03 --name-only -- …Core` to prove the 2026-08-15 Core commits are **all
`Dxf/`** — the takeoff engine's own last commit is `1a1689b6`, **2026-07-04**.

**Builds [RUN]:** `dotnet build` Debug on `TakeoffCli` (0 warn/0 err, 0.8 s),
`Core.Tests` (0/0), `EngineeringTools.Tests` (0 err; 2 NU1902 AngleSharp warnings inherited from
`Kor.Opportunities.Data`).

**Tests [RUN]** (targeted only, per rubric rule 4 — full suite never started):
- `Core.Tests --filter "…~Takeoff|~Rebar|~Slab|~Plan|~Sheet|~Schedule|~Plate|~Volume|~Floor|~Ifc|~Pdf|~Footing|~Building|~Grid"` → **392 passed, 0 failed, 4 m 23 s**.
- `EngineeringTools.Tests --filter "…~PdfToSafe"` → **409 passed, 0 failed, 454 ms**.
- `EngineeringTools.Tests --filter "…~AiTools"` → **2 passed, 0 failed**.

**End-to-end accuracy runs [RUN]** — the real 31065 drawing set, free deterministic mode, $0:
```
takeoff.exe vector-takeoff "…\Inputs\31065 - AFTER (IFC 2026-03-06).pdf" <pngDir> out.xlsx \
            12 51 "1:100" "…\Inputs\31065-storey-heights.json" --deterministic
```
and the same command over the **whole 73-page set** (no page range). Both exit 0.

**Answer key computed [QUERIED]:** summed the m³ column of
`C:\Users\ilalonde\Desktop\Rory\_source-csv\31065-{floors,walls,columns,foundations}-after.csv`.

**Environment probes [QUERIED]:** `[Type]::GetTypeFromProgID` for
`CSI.SAFE.API.ETABSObject`, `CSI.ETABS.API.ETABSObject`, `SAFEv1.Helper`, `ETABSv1.Helper`,
`CSI.SAP2000.API.SapObject` → **all five unregistered**; `C:\Program Files\Computers and
Structures` → **does not exist** on this machine. `pdftoppm` present (Poppler 25.07 via winget)
but **no longer needed** (§4). Demo-input folders listed on Desktop (`Rory\`, `Structural Quantity
Takeoff Demo\`, `CSI Ingestor\`).

---

## 2. What this module is

Four tools that turn a drawing set into engineering numbers. **PDF→SAFE (shipped in the app as
"Structural PDF Import")** reads a colour-marked vector PDF floor plan, extracts the drawn
polygons with PdfPig, classifies each colour as slab / wall / column / beam / opening, and writes
a ready-to-open analysis model — CSI SAFE `.f2k`, ETABS `.e2k` or AutoCAD `.dxf` — or pushes the
model straight into a running SAFE/ETABS/SAP2000 over the CSI OAPI. It saves a technician the
half-day of re-drawing a plan inside SAFE. **Structural Quantity Takeoff** is the app's second
card and has two modes: *Single-Issue* turns a Revit concrete schedule (CSV) into a per-floor
concrete + reinforcing + formwork workbook, and *Compare Two Issues* diffs the reinforcing
call-outs of two drawing issues (IFT vs IFC) into a change list plus a marked-up PDF with added
work in green and removed in red — the deliverable that settles "did the rebar go up?" with a
client. **Rebar Change** is not a separate tool any more; it is the second tab of that window.

The fourth and most differentiated capability does **not** live in the app at all. The **vector
takeoff engine** (`Kor.Operations.EngineeringTools.Core`, driven by the `takeoff` CLI) reads the
issued PDF *itself* — no Revit model, no CSV — and produces a whole-building concrete takeoff:
slab areas from the drawn structural grid cross-checked against raster poché, thicknesses from the
sheets' own `200 SLAB` / `8" SLAB` call-outs, footings from the foundation schedule crossed with
counted plan marks, walls and columns from grey fill × storey height. Its selling point is the
honesty model: every plate is **green (measured)**, **orange (assumed, with the reason printed)**,
or **named as a residual it refuses to price**. On a demo it prints a per-level table and a
synopsis like "35 of 54 plates clear; 19 need review" with a one-line reason each. That is a
credible thing to put in front of an architect's technical lead — provided you say plainly that it
runs from a command line today.

---

## 3. How you would demo it

**Prerequisites:** none of these need the KOR LAN, VPN, SQL, or any KOR service. All four run
fully offline on a laptop. The app is `Kor.Operations.App` → Home → **Engineering Tools** card
(`HomeWindow.xaml.cs:171`) → a two-card chooser [READ].

**A. Structural PDF Import (PDF→SAFE)** — app, ~2 min.
Open Engineering Tools → *Structural PDF Import* → **Select structural PDF**
(`PdfToSafeWindow.xaml.cs:77`) → the page renders, detected colours list on the left → set each
colour's type/thickness → **Export F2K / E2K / DXF**.
*Real input verified on disk:* `C:\Users\ilalonde\Desktop\CSI Ingestor\regent typ floor April
2026.pdf` (41,501 bytes), with the previously exported `…_SAFE.f2k` and `….dxf` beside it as the
expected result [QUERIED]. The identical file is the golden-snapshot fixture at
`Kor.Operations.App/EngineeringTools.Tests/PdfToSafe/Fixtures/regent_typ_floor.pdf`.
**This is the only PdfToSafe input I could locate anywhere on this machine, it is a single typical
floor, and it is dated 2026-04-14** — see §8 risk 2.
File export needs no CSI software. Opening the result *in SAFE on screen* needs SAFE installed and
licensed; live OAPI push additionally triggers a one-time UAC elevation to run `RegisterSAFE.exe`
(`CsiComRegistration.cs:33-60`) [READ].

**B. Structural Quantity Takeoff — Single-Issue** — app, ~1 min.
Engineering Tools → *Structural Quantity Takeoff* → fill WBS1/project → **Import CSV** → **Generate**
→ SaveFileDialog → the .xlsx opens in Excel automatically.
*Real input verified on disk:* `C:\Users\ilalonde\Desktop\Structural Quantity Takeoff Demo\Inputs\
30941 Lindley - concrete schedule.csv` — header `Level,Element,Variant,ConcreteVolume`, which
matches `StructuralTakeoffCsvImporter` exactly [QUERIED]. Expected output for comparison:
`…\Structural Quantity Takeoff Demo\30941 Lindley - Structural Quantity Takeoff (imperial,
per-floor).xlsx`.

**C. Structural Quantity Takeoff — Compare Two Issues (rebar change)** — app, ~2-4 min.
Same window, second tab → **Pick BEFORE** / **Pick AFTER** → **Generate change report** (xlsx) and
**Generate visual markup** (PDF).
*Real inputs verified on disk:* `…\Inputs\31065 - BEFORE (IFT Addendum 2025-10-07).pdf` (17.8 MB)
and `…\Inputs\31065 - AFTER (IFC 2026-03-06).pdf` (15.2 MB). Known-good outputs to sanity-check
against: `…\Structural Quantity Takeoff Demo\31065 - Rebar Takeoff & Change (IFT to IFC) FULL.xlsx`
and `…\31065 - Rebar Changes (visual markup, IFT to IFC).pdf` (2026-06-26).
Note both PDFs are 15-18 MB — allow for the read, and it OCRs any image-only page (flagged
"verify") before diffing.

**D. Vector takeoff from drawings (the differentiator)** — **command line only**, ~3-4 min.
```
takeoff.exe vector-takeoff "<set>.pdf" <pngDir> <out.xlsx> [first] [last] [scale] [heights.json]
            [--deterministic] [--fresh]
```
Verified working today on the 31065 set, both page-ranged and whole-set, exit 0, **0 API calls,
$0** [RUN]. It renders its own PNGs (bundled PDFium via Docnet), so the RESUME doc's `pdftoppm`
pre-step is no longer needed. It prints a per-page trace, a per-level volume table, an orange
synopsis, and a whole-building total, then writes the workbook.
**There is no button for this in the app.** If it is demoed, it is demoed in a terminal.

---

## 4. Completeness

| Capability | State | Evidence |
|---|---|---|
| PDF→SAFE: geometry extraction from vector PDF | `WORKING` | 409 tests green incl. a full golden-snapshot pipeline against a real PDF fixture [RUN] |
| PDF→SAFE: F2K / E2K / DXF file export | `WORKING` | golden snapshot `regent_typ_floor.expected.txt` (97 pts, 7 areas, 26 frames) reproduces [RUN]; matching `_SAFE.f2k` on disk [QUERIED] |
| PDF→SAFE: live OAPI push to SAFE/ETABS/SAP2000 | `UNKNOWN` | code + `VirtualSafeDriver` tests exist [RUN]; **no CSI product installed or COM-registered on this machine**, so the real driver has never been exercised here [QUERIED] |
| PDF→SAFE: AI assistant bar (classify colours, mutate state, trigger export) | `WORKING` (degrades silently when unconfigured) | `PdfToSafeWindow.AiWiring.cs:74-92`, `PdfGeometryAnalysisService.cs:38` [READ]; 2 AI-tool tests [RUN] |
| Structural Takeoff (app): CSV → per-floor concrete/rebar/formwork xlsx | `WORKING` | `StructuralTakeoffCsvImporter`/`Service`/`ReportGenerator` all covered in the 392 green Core tests [RUN]; sample CSV matches the contract [QUERIED] |
| Rebar change: two-issue call-out diff → xlsx | `WORKING` | `RebarChangeService` + `RebarGridPricer` + `RebarWeightEstimator` covered in the 392 [RUN]; shipped artifacts on disk [QUERIED] |
| Rebar change: on-drawing visual markup PDF | `WORKING` | `RebarOverlayGeneratorTests` green [RUN]; 2.3 MB marked-up PDF on disk [QUERIED] |
| Rebar change: OCR recovery of image-only sheets | `WORKING` | `PdfTextWithOcr.cs` (WinRT `Windows.Media.Ocr`, no external dep) [READ] — **not covered by any test** |
| Vector takeoff engine: whole-building measurement from PDF | `WORKING`, CLI-only | full 73-page run, exit 0, 54 plates, 19,545 cy [RUN] |
| Vector takeoff: `--deterministic` zero-AI mode | `WORKING` | "vision: DISABLED (--deterministic) - 0 API calls, $0" [RUN] |
| Vector takeoff: reproducibility | `WORKING` | pages 12-51 and the full 73-page set produce **byte-identical** totals (19,545 cy) [RUN] |
| Vector takeoff: orange-flag / residual model | `WORKING` | 19 of 54 plates flagged with printed reasons incl. 2 `[Critical] SLAB_TOO_THICK` [RUN] |
| Vector takeoff: column-schedule read on 31065 | `PARTIAL` | run prints "no readable column schedule - footprint fallback"; result is +135% vs the Revit key [RUN] — see §5 |
| Vector takeoff **in the WPF app** ("Generate takeoff" button) | `DEAD` (never built) | grep for `SlabTakeoffEngine|IPlanVision|IPlanRaster` across the whole App returns **zero hits** [RUN] |
| Standalone `QuantityTakeoffWindow` | `DEAD` (orphan) | DI-registered `AppModule.cs:199` with the comment "Superseded by StructuralQuantityTakeoffWindow"; no code path opens it [READ] |
| Standalone `RebarChangeWindow` | `DEAD` (orphan) | DI-registered `AppModule.cs:200`; **no reference anywhere else in the solution** [RUN] |
| App-side tests for QuantityTakeoff / RebarChange / StructuralTakeoff | `DEAD` | `EngineeringTools.Tests/QuantityTakeoff/` is an **empty folder**; all 28 test files are PdfToSafe (+1 AiTools) [QUERIED] |

**Marker count across all scope paths:** `TODO` 0 · `FIXME` 0 · `HACK` 0 ·
`NotImplementedException` 0 · `throw new NotSupportedException` 0 · empty `catch {}` 0 [RUN].
That is genuinely clean — the debt here is structural (§5), not littered.

---

## 5. What is broken or risky

**5.1 — The AI boundary: the strong claim holds, the absolute claim does not.** [READ + RUN]

The stated rule is "AI is banned from the measurement path". `IPlanVision.cs` states it precisely:
*"neither call ever reads a dimension — numbers always come from exact vector text and poché
geometry"*. **That specific claim is true and I could not falsify it.** Every thickness and every
dimension originates in exact vector text (`SlabThicknessReader`, `SheetScaleReader`) or in pixel
measurement; `SlabTakeoffEngine.cs:820-822` states the invariant explicitly and the missing-thickness
recovery at 2c is deliberately deterministic-only.

But **AI does influence priced quantities in three flagged places**, and MVE's technical lead will
find them if he reads the trace:
- `SlabTakeoffEngine.cs:678-706` — on a plate with no readable grid, the AI returns a bounding box
  and the poché is measured **inside that box**. The AI does not read the area, but it chooses the
  region whose area is priced. The RESUME records the failure mode in the owner's own words:
  *"3 SOUTH measured 1,973 one run, 10,530 the next"* on no-grid plates.
- `SlabTakeoffEngine.cs:782-806` — `THK_SPLIT_AI_ONLY`: when the deterministic Voronoi split has no
  in-box anchor, the **vision apportionment's area percentages become the priced effective depth**.
- `SlabTakeoffEngine.cs:707-714` — `AREA_ESTIMATED_PEERS`: when the AI cannot locate at all, the
  area is substituted from neighbouring levels.

All three raise an orange flag naming themselves, all three are visible in the workbook, and
`--deterministic` removes them entirely (proven: 0 calls, $0, and the whole building still prices).
**The honest sentence for the room is: "no number the AI produces is ever used as a dimension; on
a handful of unreadable plates it points at a region, and every one of those is flagged orange —
and there is a mode where it never runs at all."** Do not say "AI never touches the measurement."

**5.2 — PdfToSafe has the opposite AI posture, and nobody has reconciled the two.** [READ]
`PdfToSafeAiTools` includes `SetSlabThicknessAtIndex`, `SetColumnSectionAtIndex`,
`SetLineSectionAtIndex`, `SetColorProperties` and `ExportF2k/E2k/Dxf` — an LLM can set a slab
thickness and then export the model, with no orange-flag equivalent. The system prompt
(`PdfToSafeWindow.AiWiring.cs:31-67`) even hardcodes classification thresholds ("both sides ≤
1500 mm AND aspect ≤ 2.5 → Column") as prose *inside a prompt* rather than as code. If both tools
are demoed in one session, "our AI never touches numbers" and "ask the AI to set the slab to
250 mm" are five minutes apart.

**5.3 — Column volume is 2.35× the Revit answer key.** [RUN]
`columns (gray-fill) 2,502 cy (no readable column schedule - footprint fallback)` vs the Revit
key's 814 m³ = 1,065 cy. The tool falls back to grey-fill footprint × storey height because it
cannot read 31065's column schedule. It is not silently wrong — the fallback is printed — but it
is a large error in a headline number and it currently **cancels** the slab under-count (§7).

**5.4 — The takeoff engine is stranded outside the product.** [RUN]
Zero references to `SlabTakeoffEngine`, `IPlanVision` or `IPlanRaster` anywhere in
`Kor.Operations.App`. The RESUME's stated goal — *"Must be GENERAL and live in the Core engine,
callable from the WPF app — NOT stranded in the CLI"* — and its NEXT STEP #2 are **not done**.
The Core-side refactor *is* done (`SlabTakeoffEngine.RunAsync` exists and is host-agnostic); what
is missing is the app-side `IPlanVision`/`IPlanRaster` pair and a button. That is a bounded piece
of work, not a rewrite.

**5.5 — Two independent E2K writers.** [READ]
`Kor.Operations.App/EngineeringTools/PdfToSafe/EtabsE2kExporter.cs` (351 L, string-builder,
internal, App-layer) writes `.e2k` for PdfToSafe. `Kor.Operations.EngineeringTools.Core/Dxf/
E2kDocument.cs` (681 L) + `E2kGeometryComposer.cs` (764 L) write `.e2k` for the DXF→ETABS
generator (another agent's module). **They share no code, no model, and no tests.** Same file
format, two implementations, in the same solution. Not a demo blocker; a real maintenance liability
and an obvious question if anyone opens both.

**5.6 — `takeoff.exe` with no arguments prints the wrong usage line.** [RUN]
Running the binary bare prints `Usage: takeoff <before.csv> <after.csv> <out-basepath> …` — the
oldest CSV-diff command. There are **~35 subcommands** dispatched by a flat chain of
`args[0].Equals(...)` down 2,971 lines of `Program.cs`, with no help, no list, and no version. A
mistyped command in front of MVE prints a usage string for a feature you are not demoing. This
same `Program.cs` also hosts `dxf-to-etabs`, `corpus-read`, `e2k-compare` and `dxf-import-rules` —
**the CLI is shared with the DXF→ETABS module**, so a change on either side ships in one binary.

**5.7 — Minor / for the record.**
- `EngineeringTools.Tests/PdfToSafe/FirmDefaultsEdgeCaseTests.cs:211` — the one hardcoded absolute
  path the cross-cutting scan found: `@"C:\Program Files\Computers and Structures\SAFE 22\SAFE.exe"`.
  **Assessed benign** — it is a *string value* round-tripped through a settings-persistence
  assertion, never touched on the filesystem. The test passes on this machine, which has no such
  path [RUN]. No fix needed; do not spend demo time on it.
- `Vision.cs:20` — `HttpClient` timeout 3 min, and `Vision.cs:165` honours Anthropic `Retry-After`
  with backoff. Timeouts are bounded. `PdfGeometryAnalysisService.cs:33` — 90 s. Both fine.
- `Vision.cs:23-24` — API key from `KOR_ANTHROPIC_KEY` or `ANTHROPIC_API_KEY` env var only. **No
  key is embedded anywhere in scope** — no connection strings, no credentials, no SQL [RUN].
- `Program.cs:576` correctly refuses to start rather than half-running when the key is absent.
- App error handling is uniformly `MessageBox.Show($"Could not …: {ex.Message}")` — safe, but a raw
  exception message in a dialog is what MVE will see if a PDF misbehaves.

---

## 6. Dependencies

| Dependency | Needed by | Reachable off the KOR LAN? |
|---|---|---|
| **Nothing on the KOR network** | all four tools | ✅ — no SQL, no Deltek, no Graph, no SharePoint, no share, no MCP service in any scope path [RUN] |
| Anthropic API (`api.anthropic.com/v1/messages`, `claude-sonnet-4-6`) via `KOR_ANTHROPIC_KEY` | vector takeoff phase 2 (optional — `--deterministic` skips it); PdfToSafe AI bar (optional) | ✅ needs internet only. **Key is set on this machine** [QUERIED] |
| **CSI SAFE** (licensed) | opening the exported `.f2k` on screen; live OAPI push | ❌ **not installed on this machine**; must be on the demo laptop [QUERIED] |
| **CSI ETABS** (licensed) | opening exported `.e2k` | ❌ not installed here [QUERIED] |
| CSI SAP2000 (licensed) | `Sap2000ApiExporter` path | ❌ not installed here [QUERIED] |
| Revit | **not needed at demo time** — the answer-key CSVs and the schedule CSV are already exported to disk | n/a |
| Bluebeam | **not needed at demo time** — only to *author* a new marked-up PDF for PdfToSafe; a marked-up sample already exists | n/a |
| Excel | opening every generated .xlsx (the code calls `Process.Start` with `UseShellExecute`) | ✅ local |
| Windows 10 build 19041+ (WinRT `Windows.Media.Ocr`, `Windows.Data.Pdf`) | rebar-change OCR fallback; PdfToSafe page render | ✅ local; **the tools will not run on a non-Windows or older-Windows machine** |
| PdfPig, ClosedXML, DocumentFormat.OpenXml, Docnet.Core (PDFium), SixLabors.ImageSharp | everywhere | ✅ all NuGet, bundled |
| Poppler `pdftoppm` | **no longer required** — `PlanPdfRenderer.cs` renders with bundled PDFium. The RESUME's render step is stale [RUN] | n/a |

---

## 7. Test reality

**Two test projects, and the split matters.**

`Kor.Operations.App/EngineeringTools.Tests` — 28 .cs / 7,182 L / **~333 `[Fact]`+`[Theory]`
attributes → 411 executed tests**, all green in **456 ms** [RUN]. Coverage is genuinely good and
**not theatre**: `VirtualPipelineTests` drives the real 41 KB PDF through extraction → classification
→ export against a `VirtualSafeDriver`, and asserts a full golden snapshot (97 points, 7 areas
incl. 2 openings, 26 frames, materials, loads, restraints). `ExportValidator`, `PolygonProcessor`
(45 facts), `GeometryFilter`, `WallOpeningDetector` and both section parsers are all real. But the
project is **100% PdfToSafe** — its `QuantityTakeoff/` folder is empty and there is not one test
for the Structural Takeoff window, the rebar-change window, or `PdfTextWithOcr`.

`Kor.Operations.EngineeringTools.Core.Tests` — where the takeoff and rebar logic is actually
tested. My filtered run: **392 passed, 0 failed, 4 m 23 s** [RUN]. This covers
`StructuralTakeoffService`, `StructuralTakeoffCsvImporter`, `RebarChangeService`,
`RebarGridPricer`, `RebarWeightEstimator`, `RebarOverlayGenerator`, `SlabThicknessReader/Zoner`,
`SheetTitleReader`, `SheetScaleReader`, `StructuralGridReader`, `SlabAreaReconciler`,
`PlateReliabilityScorer`, `PlanGeometry`, `PlanVisionParser` (canned JSON, no spend),
`ScheduleGridReader`, `FootingScheduleReader`, `IfcQuantityTakeoff`, `VolumeCalculator`. The
RESUME's "221/236 Core tests" is stale low — it is far larger now.

**What is now untested — the answer to the dating question.** App code last moved **2026-06-26**,
App tests last moved **2026-05-15** — a 6-week gap. That gap is exactly the Phase 5-6 consolidation
commit `f5454794` that created `StructuralQuantityTakeoffWindow`, i.e. **the entire consolidated
window that the demo will use has zero App-side tests**. The mitigation is real, though: every
piece of logic it calls (`StructuralTakeoffService`, `RebarChangeService`, `RebarWeightEstimator`,
`RebarOverlayGenerator`, report generators) *is* covered in Core.Tests, which moved as recently as
2026-07-24. What is untested is the **wiring**: CSV→dialog→service→xlsx→`Process.Start`, the
metric/imperial radio, the `withWeight` branch at `StructuralQuantityTakeoffWindow.xaml.cs:196`,
and `PdfTextWithOcr`. Those are UI-thread paths a headless test cannot easily reach — which is why
§9 recommends a rehearsal, not a test.

The CLI (`TakeoffCli`, last commit 2026-08-15) has **no tests of its own** by design — it is a thin
host over Core, plus ~35 probe commands.

### The accuracy question — measured, with real numbers and dates

**Accuracy HAS been measured end to end.** There are two records and they do not fully agree.

**(a) The shipped record — `Structural Quantity Takeoff - Results Brief.md`, 2026-07-04** [DOC].
Validated against three projects with independent answer keys: 31065 Heather St vs the full Revit
model → **−15% whole building** (columns −4%, walls −12%, typical floors ±5%); 31044 Coronation vs
the estimator's QTO → **slab +2.8%** (−1.1% in free mode); 30941 Lyndley vs the Revit concrete
schedule → **matched-level slab −1%** (18,845 vs 19,002 cy). Backed by `docs/Takeoff-Doctrine.md`
(2026-07-04), which is a serious document: four gates, a **fitted-parameter register** naming the
one calibrated constant (`SlabAreaReconciler.DefaultNetFactor = 0.92`), and an explicit
anti-overfit rule. This is stronger governance than most commercial takeoff tools publish.

**(b) What I measured today, 2026-08-20, on the same set** [RUN]. Free deterministic mode, 0 API
calls, whole 73-page set; answer key = the 31065 IFC Revit CSVs summed by me [QUERIED]:

| Category | Engine (2026-08-20) | Revit 31065 IFC key | Delta |
|---|---|---|---|
| Slab incl. mats | 11,003 cy | floors 10,135.7 m³ = 13,257 cy | **−17.0%** |
| Walls | 4,164 cy | 3,375.0 m³ = 4,414 cy | −5.7% |
| Columns | 2,502 cy | 814.0 m³ = 1,065 cy | **+135%** |
| Foundations | 1,875 cy | 1,741.0 m³ = 2,277 cy | −17.7% |
| **Whole building** | **19,545 cy** | **21,013 cy** | **−7.0%** |

**Read this carefully before quoting it.** Three caveats, all of which an engineer will raise:
1. **The total is error-cancelling.** A −2,254 cy slab shortfall is partly masked by a +1,437 cy
   column over-count. −7.0% overall is *not* −7% accuracy.
2. **The answer key is itself imperfect.** 107 of 322 column rows and 168 of 315 wall rows in the
   Revit export are `0 m³` — unmodelled or placeholder families. The column comparison in
   particular cannot be trusted to 1%.
3. **My run does not reproduce the brief's category numbers.** The brief says columns −4%; today
   the engine prints *"no readable column schedule — footprint fallback"* and lands at +135%. I
   could not establish whether that is a regression after `1a1689b6`, a different invocation, or
   the answer-key zeros. I did **not** run the paid vision mode to find out — there is no cached
   `.vision-cache` for this set any more, so it would have spent real money without sign-off. The
   command that would settle it, at roughly **$2**, is the same one minus `--deterministic`.

**The genuinely strong findings, both mine [RUN]:** the engine is **exactly reproducible** —
pages 12-51 and the full 73-page set both total 19,545 cy — and it prices a whole 40-storey
building for **$0 with zero AI calls**. Those two facts are worth more in the room than any single
percentage.

---

## 8. Demo risk (ranked)

1. **"Can I click it?" — the best tool has no button.** The vector takeoff engine is the thing MVE
   would actually be impressed by, and it runs in a terminal. Demoing a console window after
   showing a polished WPF app reads as "prototype", and invites *"so this isn't in the product
   yet?"* — to which the honest answer is no.
2. **PdfToSafe has exactly one known-good input, and it is one floor from April.** `regent typ
   floor April 2026.pdf` is 41 KB, a single typical floor. If MVE hands over one of their own
   marked-up sheets — the natural ask for a tool whose input is *"a Bluebeam-marked-up PDF"* — it
   has never been tried. **Do not invite a live file.**
3. **The column number is 2.35× the answer key.** If anyone opens the whole-building total and
   asks for the breakdown, columns are visibly wrong. The tool does say why on the same line
   ("no readable column schedule — footprint fallback"), which turns a failure into a limitation
   — but only if the presenter reads it out first.
4. **Two contradictory AI stories in one session.** "AI never touches the numbers" (takeoff) and
   "ask the AI to set the slab thickness and export" (PdfToSafe) are both true of this module.
   Decide which one is being told, and do not demo both tools' AI in the same sitting.
5. **No SAFE on the demo machine = no payoff shot.** Exporting an `.f2k` to a folder is
   anticlimactic. The moment that lands is the model open in SAFE. That needs a licensed SAFE
   install on whatever laptop is used, plus a first-run UAC prompt for COM registration — which is
   itself an awkward thing to hit live.
6. **`takeoff.exe` bare prints the wrong usage.** One typo and the screen shows a usage line for a
   feature you are not demoing, out of a 2,971-line command file with no help.
7. **Two 15-18 MB PDFs on the rebar compare.** It reads both fully, OCRs any image-only page, then
   diffs. It is not instant, and there is no progress bar beyond a wait cursor and a status label —
   dead air of unknown length in front of an audience.
8. **Orange everywhere, honestly.** 19 of 54 plates flagged, two of them `[Critical]
   SLAB_TOO_THICK`. This is the product working as designed and it is a genuinely good story — but
   only if framed *before* the workbook is opened. Unframed, a wall of orange reads as broken.
9. **Two dead menu entries.** `QuantityTakeoffWindow` and `RebarChangeWindow` are still
   DI-registered. Nothing opens them, so nothing will break — but if anyone inspects the app's
   surface they are visible leftovers.

---

## 9. To-do register

| # | Item | Size | Tag | Why it matters |
|---|---|---|---|---|
| 1 | Write the one-page **demo script** for these four tools: exact files (§3 names them all), exact order, and the two sentences that frame the orange flags and the column fallback *before* the workbook opens | S | `BEFORE-DEMO` | Every top risk in §8 is a framing failure, not a code failure. This is the single highest-value hour in this module. |
| 2 | **Rehearse all four end to end on the actual demo laptop**, from the app, with the §3 files — including the 15-18 MB rebar compare, timed | S | `BEFORE-DEMO` | The consolidated window has zero App-side tests (§7). A rehearsal is the only thing that covers the wiring. Also gets the real wall-clock number for risk 7. |
| 3 | Decide and write down the **AI story**, and pick which tool's AI is shown | S | `BEFORE-DEMO` | §5.1/§5.2. The claim that makes this trustworthy to an engineer must be stated precisely, once, and not contradicted twenty minutes later. |
| 4 | Confirm **SAFE is installed and COM-registered** on the demo laptop, and pre-run the registration UAC prompt once | S | `BEFORE-DEMO` | Risk 5. Zero CSI products on this machine [QUERIED]. Without this the PDF→SAFE demo ends at a file on disk. |
| 5 | Either prepare a **second marked-up PDF** for PdfToSafe, or agree not to accept a live file from MVE | S | `BEFORE-DEMO` | Risk 2. One 41 KB single-floor sample is a thin base for the most visual tool in the set. |
| 6 | Give `takeoff.exe` a **help/usage list** of its real commands | S | `BEFORE-DEMO` | Risk 6. Twenty minutes; removes an entire class of on-screen embarrassment. |
| 7 | Re-run the 31065 set **with vision** (~$2, needs spend sign-off) and reconcile against the 2026-07-04 brief's category numbers | S | `SOON` | §7(b) caveat 3. The shipped brief is the firm's public accuracy claim; it currently does not reproduce in free mode. Know before someone else checks. |
| 8 | Fix or explicitly scope the **column-schedule read** on 31065 | M | `SOON` | §5.3. +135% on a headline category. |
| 9 | Wire `SlabTakeoffEngine` into the app: app-side `IPlanVision`/`IPlanRaster` + a "Generate takeoff" button bound to `SlabTakeoffResult.Synopsis` | L | `SOON` | §5.4 / risk 1. The Core work is done and host-agnostic; this is the step that turns the differentiator into a product. |
| 10 | Refresh `docs/Takeoff-RESUME.md` and `docs/Takeoff-Scorecard.md`, or mark them superseded | S | `SOON` | Both are stale (§ below). The RESUME is explicitly labelled "read this FIRST" and will mislead the next session. |
| 11 | App-side tests for the consolidated window: CSV→xlsx round trip, the `withWeight` branch, `PdfTextWithOcr` on a known image-only page | M | `SOON` | §7. Closes the 6-week code/test gap on the exact window the demo uses. |
| 12 | Unify the two E2K writers behind `Core/Dxf/E2kDocument` | L | `LATER` | §5.5. Pure maintenance debt; invisible at demo. |
| 13 | Delete the two orphan windows and their DI registrations | S | `LATER` | §5.4 / risk 9. Cosmetic. |
| 14 | Split `Program.cs` (2,971 L, ~35 commands, shared with the DXF module) into per-command files | L | `LATER` | §5.6. Two modules ship in one binary from one file. |

---

## Contradictions with existing documents and memory

- **STALE-DOC — `docs/Takeoff-RESUME.md`** (2026-07-03; engine code moved to 2026-07-04). Labelled
  "AUTHORITATIVE… read this FIRST", but: (a) its NEXT-STEP #2 "extract orchestration into Core
  `SlabTakeoffEngine.RunAsync`" is **done**; (b) its "Render command — `pdftoppm -png -r 110`"
  pre-step is **obsolete** — `PlanPdfRenderer.cs` renders with bundled PDFium and the CLI does it
  itself [RUN]; (c) its "221/236 Core tests" is far below the current count [RUN]; (d) it does not
  mention `--deterministic`, `--fresh`, the `.vision-cache`, the storey-heights JSON input, or that
  the engine now prices walls, columns, footings and mats — it describes a slab-only tool; (e) all
  the scratchpad paths it cites (`scratchpad/coron_stick.pdf`, `scratchpad/full/`,
  `scratchpad/31065_full/`) **no longer exist** [QUERIED].
- **STALE-DOC — `docs/Takeoff-Scorecard.md`** (2026-06-28). Cited by the RESUME as required
  reading; predates the whole-building pricing work and the three-set bench.
- **STALE-DOC — `docs/architecture/Kor.Operations.QuantityTakeoff.plan.md`** — superseded in its
  own successor's header; the UX split it describes no longer exists.
- **CONTRADICTS the shipped brief** — `Structural Quantity Takeoff - Results Brief.md` (2026-07-04)
  claims columns −4% on 31065; today's free-mode run reports "no readable column schedule" and
  +135% [RUN]. Unresolved; see to-do 7.
- **Memory `project_takeoff_engine_state.md` / `project_structural_takeoff_build.md`** — "Read
  docs/Takeoff-RESUME.md FIRST" should be amended: read it, then verify, per the above. The
  memory's other two claims **check out**: "31065 Revit = answer key" ✅ (the CSVs exist and I used
  them) and "QTO never input" ✅ (`SlabTakeoffEngine` reads only the PDF; the doctrine states it and
  nothing in the code reads a QTO).
- **Memory `project_rebar_change_tool.md` — "Rebar delta v4 (spot-check pending)"**: partially
  superseded. A **v5** artifact exists — `31065 - Change Ledger + Grid DeltaAs (IFT-Add to IFC)
  v5.xlsx`, 2026-07-24 — and `RebarExtentsTests.cs` moved the same week [QUERIED]. The spot-check
  is still not recorded anywhere I could find.
- **Memory `project_pdftosafe_*` — "batch smoke pending"**: **confirmed still pending**. No batch
  smoke harness exists in `tools/`, in the test project, or anywhere in the repo [RUN]. The
  golden-snapshot `VirtualPipelineTests` is single-file coverage, not batch.
