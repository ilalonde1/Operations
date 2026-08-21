# 08 — Cost Benchmark

**What would it have cost to have this built professionally, and what would it cost to buy the
commercial equivalent instead?**

Prepared 2026-08-20 · **Revised 2026-08-21** · KOR Structural · companion to `05-MASTER-AUDIT.md`
and `competitive/C1–C4`

---

## What changed in this revision, and why

The first version of this report priced the build as **"11 modules × ~4,000 hours = 45,000 hours."**
That unit was wrong. It counted **the audit's chapter headings**, not the product's actual surface.
Nothing in that 45,000-hour figure was derived from a feature inventory; it was a round agency
heuristic applied to a table of contents.

The owner said the system does considerably more than the audit represented, and he was proved right
nine times over the course of this revision:

1. **A per-bookmark PDF annotation workflow.** When a transmittal's Purpose is `Site Instructions`,
   the tool reads the attached PDF's `/Outlines` bookmark tree and presents one row per bookmark so
   the engineer can comment on each before issue; the notes render onto the cover sheet. The audit
   missed it and wrongly reported "no RFI/submittal record type anywhere."
2. **Eight transmittal purpose types**, not three. The audit measured the wrong column.
3. **A standards-centralisation estate** — 612 canonical `KOR-D-#####` details, a ~379-family
   component register, a Revit palette reading from SQL, an approval/publication/watermarking
   workflow and a conformance-scoring corpus — split across three module reports and **never costed
   as a system**.
4. **"PdfToSafe" is not one tool — it is a five-format CSI exporter suite with live vendor-API
   drivers.** 46 files, 12,990 lines, emitting `.f2k`, `.e2k`, `.sdb`, `.edb` and `.$et` plus DXF,
   and driving SAFE, ETABS and SAP2000 **live over COM**.
5. **There are two takeoff products, not one** — Quantity Takeoff and Structural Takeoff (the latter
   with OCR over scanned sets), on a shared 16,819-line engine plus a separate 3,562-line CLI.
6. **The Virtual Drafter is an entire program the audit never costed at all** — a Revit command
   bridge with 41 verbs, a job-template instantiation system, a rules database, a conformance
   scorer, an intake pipeline, a training curriculum, and a **closed-book head-to-head exam against
   a named human drafter on a real job, graded by a KOR principal**.
7. **A fourth engineering product: native ETABS plugins.** `ETABS\Plugin Development\` —
   **17,688 lines of C# across three projects**, distinct from both the DXF→ETABS generator and the
   PDF→CSI exporters.
8. **The KOR Tools Revit ribbon is a product, not "an add-in"** — **145 commands across 18
   categories**, of which a **50-command rebar detailing suite** and a **23-command sheet-composition
   suite** are products in their own right. A previous audit reported the sheet-composition capability
   as *absent*; it exists and is substantial.
9. **DemoStudio** — `App Demo Maker\`, **29,876 C# + 1,988 XAML across 13 projects**: a recorder /
   composer / publisher shell with a session state machine, FlaUI automation and a redaction
   subsystem. Dormant, but built.

So this revision throws away the module unit and rebuilds the effort estimate **bottom-up from a
measured census of user-invocable actions, integration surfaces and named product estates** (§5),
then reconciles that against **two** independent checks: lines of code, and — new in this revision —
a **sourced function-point delivery rate** (§6).

### What was deliberately excluded, and why that matters as much

A full sweep of `C:\VIsual Studio Projects` found three bodies of code that **must not** be counted.
They are named here rather than silently dropped, so that a reader who runs their own line count and
gets a much bigger number knows exactly where the difference is:

| excluded | measured | why |
|---|---|---|
| `_ToUpload\Operations\` | **25,510 C# + 7,121 XAML** | A **staging copy** of the Operations app beside `Operations.zip` — six project files duplicating `Kor.Operations.App`, `.Core`, `.Data`, `.Graph`, `.Rendering` and `Kor.EmailSearch.Core`. Counting it would double-count ~32,600 lines. |
| `Michael Li\Recovery\03-Decompiled-Clean\` | **36,375 C# across 26 projects** | **Decompiled, not authored** — recovery of the departed Revit lead's plugins, file-stamped 2026-07-13. Machine-generated from someone else's binaries. It cannot appear in KOR's build cost. *(The recovery **effort** is real engineering and is priced separately at Block 6.13.)* |
| `_Publish\` | **647,226 web lines** | Minified published output. |

Plus the four directories the owner excluded from scope on 2026-08-20 (`Contract Radar`,
`Deltek Project Creation`, `DeltekProjectDeadlines`, `Portfolio Website`).

**Together that is roughly 716,000 lines left out of a 464,000-line in-scope total.** The excluded
material is larger than the product. **Any line count of this drive that does not name its
exclusions is wrong by more than a factor of two**, and that is the single easiest way to
accidentally inflate a cost estimate here.

### What was kept, unchanged

Every sourced rate, price and study. The BLS wage series, the WorkBC/Job Bank figures, the ECEC
loaded-cost multiplier, the Bank of Canada FX rate, the COCOMO II parameter provenance and its
critique, the whole AI-productivity evidence base (METR, DORA, GitHub, Microsoft, Google, GitClear,
JetBrains), and all vendor pricing. **None of it was re-researched to fit a new answer.** Where a
figure was re-fetched today it says so. What was *added* is new sourcing, not replacement sourcing:
a standards-body function-point method, a US industry productivity rate, and two further contract-rate
sources.

### What the rebuild actually did to the number

| | previous | revised | change |
|---|---|---|---|
| Effort, central | 45,000 h | **~47,400 h** | **+5%** |
| **Build cost — LOW** | $1.3M | **~$1.2M** | −8% |
| **Build cost — CENTRAL** | $5.4M | **~$5.7M** | **+5%** |
| **Build cost — HIGH** | $12.0M | **~$16.9M** | **+41%** |
| Buy path, 5-yr central | ~$0.59M | **~$0.65M** | +10% |

**The central case barely moved, and that is the finding.** A brief that starts "the inventory
undercounted the system" invites a much bigger answer. It did not get one:

- **The old unit was never tied to the inventory.** "11 × 4,000" would have produced 45,000 hours
  whether the product had 100 screens or 500. Landing within 5% of the rebuilt figure is
  coincidence, not corroboration. **The previous central was not wrong so much as unfounded** — and
  the useful change is that 47,400 hours now has a denominator anyone can re-derive from the repo.
- **Eleven newly-priced estates added ~11,000 central hours; finer counting gave much of it back.**
  Of the 403 enumerated user actions, **128 are UI-local** — close a window, toggle a filter, set a
  property (§5.3). The 145 Revit commands average **59 lines each** (§5.4), so the flat per-command
  rate a first pass would reach for over-prices them by roughly half. And the Virtual Drafter, once
  audited, **came down 30%** — there is no planner, no rules engine and no job instantiation to price,
  because none was written (§5.4, Block 6.8).
- **The size basis rose, but far less than the raw drive suggests.** The revised census is
  **85 projects and 464,388 hand-written lines**, of which 350,925 is C# — after excluding ~716,000
  lines of staging copies, decompiled recovery and published output (see above). The previous
  report's 351 KLOC of C# turns out to be almost exactly right *by coincidence*: it included
  App Demo Maker and counted differently, and the corrected count lands in the same place.

**The high case rose 41%, and that movement is the real result of the revision.** Two things pushed
it, in roughly equal measure: the eleven newly-priced estates (§5.4, Block 6), and the fact that the
function-point route — which the previous report abandoned as uncostable — **now has a sourced
delivery rate** and lands well above the old high case (§6.2).

**The honest summary: the newly-found surface showed up in the high case, not the central one**,
because it is exactly the specialist engineering whose cost is most uncertain.

### What the rebuild found that the previous version had no line for

Thirteen estates are now priced explicitly, as their own lines (§5.4, Block 6). Together they are
**18,910 hours — 48% of the pre-overhead central case — that the previous report had no line item
for at all**:

| estate | central hours |
|---|---|
| PDF → CSI five-format exporter suite + live COM/OAPI drivers | 2,600 |
| DemoStudio — recorder/composer/publisher shell (**dormant**) | 2,500 |
| Two structural takeoff products + OCR + CLI | 2,100 |
| 111-source ingestion pipeline | 1,800 |
| ETABS native plugins (3 projects, 17,688 lines) | 1,700 |
| DXF → ETABS generator | 1,700 |
| Virtual Drafter — bridge (42 verbs), seed prompt, fleet corpus, exam QA (**repriced down 30% after audit**) | 1,680 |
| Standards estate — software (palette, governance, conformance engine) | 1,400 |
| Standards estate — corpus curation (612 details, ~379 families) | 1,000 |
| Revit sheet-composition suite (23 commands) | 750 |
| Revit rebar detailing suite (50 commands) | 640 |
| Revit eight-configuration version matrix | 500 |
| Decompilation-and-recovery of the departed lead's Revit tooling | 450 |
| Per-bookmark PDF annotation workflow | 90 |

The bookmark workflow is the honest counterweight. It is the finding that triggered this revision,
and measured, it is **~90 hours**. **Being missed by an audit does not make a feature expensive** —
and the same discipline is why the 50-command rebar suite prices at 640 hours rather than 50 × a
round per-command figure: those 50 commands share 1,034 lines between them (§5.4).

### Corrections to the census I was given

The census supplied with this revision was itself re-measured against the filesystem. Four figures
moved; all are documented in §1.1 and the report uses the measured value.

| element | census said | **measured** | note |
|---|---|---|---|
| Scheduled service jobs | 8 | **40** | `class …Job : IJob` in `Kor.Opportunities.Worker`. Undercounted by 5×. |
| Revit commands in tool catalog | 137 | **145** | `ToolDefinition` entries in `ToolCatalog.cs`, each a distinct command type. |
| MCP analytical tools | 25 | **23** | distinct `[McpServerTool(Name = "…")]`. |
| Revit versions | 6 (2020–2027) | **8** (2020–2027) | eight build configurations `R20`–`R27` across three TFMs. 2020–2027 inclusive *is* eight years. |

The direction of those errors is not one-sided, which is the useful part: the census overcounted two
things and undercounted one by a factor of five. **Assume more was missed** was the right
instruction, and it cuts both ways.

### A distinction this revision keeps deliberately separate

**Build cost is what was built. It is not what is wired up.** The Virtual Drafter (§5.4, Block 6.8)
is the clearest case: the artefacts show a bridge at v1.0.28 with 41 verbs, 42 database migrations,
a conformance scorer green 8/8 at run #14, and a graded closed-book exam against a production
drafter — while `modules/12-standards-centralisation.md` records that the palette consuming the
catalogue is blocked five ways and the app↔database link does not exist. **Both are true.** The
engineering was done; the delivery was not. This report prices the first and says so, and
`05-MASTER-AUDIT.md` remains the authority on the second. Conflating them would overstate the
product and understate the debt.

---

## How this was researched, and what that means for trust

**Prior art searched first** (per repo rule 1). Before any new work: `docs/audit-2026-08/` was
grepped for existing cost and licensing material; `competitive/C1–C4` were read for vendor pricing;
`00-INVENTORY.md` and `SCOPE.md` for line counts and boundaries; `modules/12-standards-
centralisation.md` and `modules/02-transmittals-tracking.md` for the two capabilities the brief says
were missed; `KOR.Drafter/docs/` (`ROADMAP.md`, `STATE-2026-08-04.md`, `TEMPLATE-BUILD-PLAN.md`,
`TRAINING-CURRICULUM.md`) and `KOR.Drafter/exam/31202-01/EXAM-SCORECARD.md` for the Virtual Drafter;
`KOR.RevitTools/docs/BUILD-STATUS.md` and `legacy-parity-matrix.md` for the Revit estate; `tools/`
for an existing cost or licence-tracking script — none exists. **The previous 941-line version of
this document was read in full before anything was rewritten** (per repo rule 2), and its sourced
material was carried forward rather than regenerated.

**Every count in §1.1 was re-measured from the filesystem in this session**, not taken from the
brief and not taken from the previous report. The commands are given in §12 so they can be re-run.

**Every rate, salary and price came from a live fetch on 2026-08-20 or 2026-08-21.** None is quoted
from model memory.

**A sourcing constraint that shapes §5 and §6.** The session's shared web-search budget was
exhausted (200/200) before this revision began. Most web evidence therefore came from **direct
fetches of named URLs**; a research subagent additionally reached alternate search engines (Brave,
Yahoo) after DuckDuckGo and Marginalia bot-walled it. This mattered in both directions: it is why
§5.2 reports that no hours-per-screen benchmark could be verified, and it is why §6.2 *can* now
report a function-point delivery rate that the previous version could not find.

**Evidence tags, used on every claim:**

- **VERIFIED** — primary source: a government statistical release, a standards body's own
  publication, a vendor's own published pricing page, a peer-reviewed study's own publication, or a
  direct machine measurement of this codebase performed in this session.
- **REPORTED** — secondary or tertiary: survey site, analyst, reseller, press, aggregator, course
  material, or a sibling audit document.
- **INFERRED** — arithmetic or reasoning performed here. Always labelled, always shown.
- **could not verify** — stated plainly, with what was searched.

---

## 1. Assumptions

These are the levers. Challenge any of them and the answer moves; that is the point of listing them.

### 1.1 What is being costed — the measured surface census

**VERIFIED by direct measurement, 2026-08-21**, across a full sweep of `C:\VIsual Studio Projects`.

**Size — in scope.**

| component | C# | XAML | SQL / PowerShell | web | total |
|---|---|---|---|---|---|
| **Operations** | 266,228 | 32,193 | 45,071 | 10,269 | **353,761** |
| **DemoStudio** (`App Demo Maker`) | 29,876 | 1,988 | — | — | **31,864** |
| **KOR Inspections Bookings** | 19,385 | — | — | — | **19,385** |
| **ETABS plugins** (`ETABS\Plugin Development`) | 17,688 | — | — | — | **17,688** |
| **KOR.RevitTools** | 13,205 | — | — | — | **13,205** |
| **KOR.Drafter** | 3,489 | — | 23,942 | — | **27,431** |
| **Redirector** | 1,054 | — | — | — | **1,054** |
| **Total hand-written, in scope** | **350,925** | **34,181** | **69,013** | **10,269** | **464,388** |

**Size — excluded, and named.** See §0 for why each is out.

| excluded | lines |
|---|---|
| `_Publish\` — minified published output | 647,226 |
| `Michael Li\Recovery\03-Decompiled-Clean\` — decompiled, not authored (26 projects) | 36,375 |
| `_ToUpload\Operations\` — staging copy of the app (6 duplicate projects) | 32,631 |
| Owner-scope exclusions (Contract Radar, Deltek Project Creation, DeltekProjectDeadlines, Portfolio Website) | ~13,000 |
| **Total excluded** | **~729,000** |

**More is excluded than included.** That is stated plainly because it is the easiest way for this
estimate to have been wrong: a naive `find`/`wc` over the drive returns roughly 1.2 million lines,
which is **2.6× the in-scope figure**, and every LOC-derived number in §6 would inflate accordingly.

**Also not counted as hand-written source:** the KOR.Drafter standards corpus (49,468 lines of
JSON/CSV census, geometry, layout and markup data), crawl results (19,420) and process records
(22,190). These are machine-produced or curated *data*. They are real work — the curation is priced
at §5.4 Block 6.1b — but counting them as source would inflate §6.

**Reconciliation with the previous revision.** This supersedes the 405,319 figure used earlier in
this revision, which covered only the five originally-audited repos. The difference is
**+17,688** (ETABS plugins) **+31,864** (DemoStudio) **+10,269** (Operations web) **−752** (minor
PowerShell re-attribution) = **+59,069**. The previous *report's* "351 KLOC of C# across 82
projects" turns out to be almost exactly right by coincidence — the corrected, differently-scoped
count gives **350,925 lines of C# across 85 projects**.

**Surface.**

| element | count | how measured |
|---|---|---|
| **WPF Windows** | **80** | root-element census of all 127 `.xaml` |
| **UserControls / views** | **42** | same |
| ResourceDictionaries + `Application` | 5 | same (80 + 42 + 5 = 127) |
| **Distinct Click handlers** | **403** | distinct `Click="…"` names across all `.xaml` |
| Click attribute instances | 542 | same, not deduplicated — some handlers are reused across views |
| Distinct `Command="{Binding …}"` bindings | 53 | the MVVM-bound actions the Click census does *not* see |
| Handlers matched to a method body | **403 / 403** | brace-matched in `.cs`; **no handler is orphaned** |
| **MCP analytical tools** | **23** | distinct `[McpServerTool(Name = "…")]` |
| **Revit ribbon commands** | **145** | `ToolDefinition` entries in `ToolCatalog.cs`, 145 distinct command types, **23 ribbon panels, 18 command categories** |
| — of which the **rebar detailing suite** | **50** | `typeof(Tools.Rebar.…)`; 1,034 lines of command code between them (**21 lines each**) |
| — of which the **sheet-composition suite** | **23** | `Tools.Sheets` 10 + `Tools.Views` 12 + `Tools.ViewNaming` 1; 1,793 lines |
| Revit command code, all 145 | 8,549 lines | `Tools/` total — **an average of 59 lines per command**, because the ribbon is generated from the catalog and commands share `Framework/` (2,336 lines) |
| Revit build configurations | **8** | `R20`–`R27` in `Directory.Build.props` (2020–2027) |
| Revit target frameworks | **3** | `net48` (2020–2024), `net8.0-windows` (2025–2026), `net10.0-windows` (2027) |
| **CLI tools** | **44** | `tools/` fleet; 34 carry a `.csproj` |
| **Worker jobs** | **40** | `class …Job : IJob` in `Kor.Opportunities.Worker` |
| BD ingestion providers | 13 | `class …Provider`; two are generic CSV/JSON drivers that fan out by configuration |
| BD sources fed | 111 | `REPORTED` — `modules/07-bd-brain-core.md` |
| SQL scripts (Operations) | 312 | `find -name "*.sql"` |
| KorStandards migrations | 42 | `KOR.Drafter/db/*.sql` |
| Canonical `KOR-D-#####` details | 612 | `REPORTED` — `modules/12-standards-centralisation.md`, from a live DB query |
| Palette view occurrences | 1,079 | same |
| Component canon (`.rfa` families) | ~379 | same (the brief's "~350" is close; the module report's queried figure is used) |
| Transmittal purpose types | **8** | `Site Instructions, For Review, For Approval, For Information, For Comment, For Permit, For Bid, IFC` |
| Test methods | **1,297** | `[Fact]` 1,181 + `[Theory]` 116. `[Theory]` cases expand at runtime — the DXF→ETABS suite reports **483 executed** (`04-TODO-REGISTER.md:424`) from 392 methods |
| Emails indexed, SQL full-text | 372,370 | `REPORTED` — module reports |
| **Projects** | **85** | `.csproj` count: 61 (Operations) + 13 (DemoStudio) + 4 (RevitTools) + 3 (ETABS plugins) + 2 (Inspections) + 1 (Drafter) + 1 (Redirector) |
| **Commits** | **2,539** | `git rev-list --count HEAD` summed over the four git repos: 2,289 + 146 + 69 + 35. *(ETABS plugins, DemoStudio and Redirector carry no git history — itself an audit finding.)* |

**The three product estates added in this revision, measured:**

| estate | measurement |
|---|---|
| **PDF → CSI exporter suite** (`App/EngineeringTools/PdfToSafe/`) | **46 files, 12,990 lines.** Emits **five CSI formats** — `.f2k`, `.e2k`, `.sdb`, `.edb`, `.$et` — plus DXF. **Live vendor-API drivers verified by file:** `CsiComRegistration.cs`, `ISafeOapiDriver.cs`, `ReflectionCsiOapiDriver.cs`, `SafeOapiTypes.cs`, `SafeApiExporter.cs`, `EtabsApiExporter.cs`, `Sap2000ApiExporter.cs`. Supporting engineering: `PdfGeometryExtractor/Parser/AnalysisService`, `BeamSectionParser`, `ColumnSectionParser`, `CoordinateTransformer`, `DesignStripGenerator`, `WallOpeningDetector`, `StructuralGridGenerator`, `StructuralMaterialDatabase`, `AnnotationResolver`, `AnnotationOverrideMerger`, `GeometryFilterService`, `ExportValidator`, `ExportOrchestrator`, `HtmlReportBuilder`. |
| **Two takeoff products** | `App/EngineeringTools/QuantityTakeoff/` (170 lines UI) and `StructuralTakeoff/` (583 lines UI, incl. OCR over scanned sets), plus `RebarChange/` (342), on `Kor.Operations.EngineeringTools.Core` (16,819 lines — of which **7,270 is the `Dxf/` engine** and ~9,550 is takeoff: rebar, slab, schedule, plan-vision, IFC, volume) and a separate `TakeoffCli` (3,562 lines). |
| **Virtual Drafter** (`C:\VIsual Studio Projects\KOR.Drafter`) | `src` 3,844 lines, of which the bridge is **3,489 across 4 files** exposing **42 verbs** (`BridgeExec.cs:32-73`; `docs/STATE-2026-08-04.md`'s *"41 verbs"* is one verb stale — `exportdxf` was added 2026-08-15); `db` 23,798 lines / 42 migrations; `intake` 11,761; `process-record` 22,190 (85 reports); `crawl-results` 19,420; `standards` corpus 49,468; `exam` 2,070; `evidence` 3,281. **69 commits, all within the last 30 days.** **`modules/13-virtual-drafter.md` establishes what this is: Claude Code on one workstation reading a 9.2 KB seed prompt and driving Revit through the bridge — there is no code anywhere that decides what to draft.** The closed-book exam on job 31202-01 was verified forensically; **the defensible result is 12 of 12 marked locations implemented and verified, with the engineer siding with the machine on one of three contested points** (`standards/RULINGS.md:51-53`, 2026-07-31). The scorecard's *"2 locations more faithful than production"* is **superseded**. |
| **ETABS native plugins** (`ETABS\Plugin Development\`) | **17,688 lines of C# across three `.csproj`**: `Development Files\KorETABS\KorETABS.csproj`, `Development Files\KorTools\KorTools.csproj`, and a superseded `OLD\ETABS-Toolkit\ETABS_Plugin.csproj`. A **fourth** engineering product, distinct from the DXF→ETABS generator (Block 6.2) and the PDF→CSI exporter suite (Block 6.6). No git history. |
| **DemoStudio** (`App Demo Maker\`) | **29,876 C# + 1,988 XAML across 13 `.csproj`.** Per its own `ARCHITECTURE.md`, a *"production-style desktop recorder/composer/publisher workflow shell for demo creation"*: WPF shell, `RecorderSessionEngine` session state machine, application/domain/infrastructure split, FlaUI-based UI automation, redaction subsystem, smoke harness. **Dormant — 1 commit in 90 days, most files last touched March 2026.** Priced as built (§5.4, Block 6.10); the dormancy is stated, not netted out. |

**Integrations priced as distinct engineering** (§5.4, Block 4): Deltek Vantagepoint over ODBC
(29 tables, 28 joins — `REPORTED` from the owner's census; the `INFORMATION_SCHEMA` release-drift
probing is **VERIFIED** at three code sites: `Financials/DeltekSchemaValidator.cs:111`,
`Deltek/DeltekKorPursuitDeltekAccessor.cs:144`, `Deltek/DeltekKorStaffDirectoryAccessor.cs:79`),
Microsoft Graph/SharePoint (18 files), Outlook VSTO add-in, the Revit API matrix, the CSI COM/OAPI
surface, ETABS/SAFE file formats, SQL Server full-text over 372,370 emails, an MCP server, an LLM
provider, PDF parsing/rendering/generation, and the transmittal redirector web tier.

**Delivery.**

| assumption | value | basis |
|---|---|---|
| Elapsed calendar time | **8 months** | `RUBRIC.md`, `05-MASTER-AUDIT.md`. The `Operations` repo's first commit is **2026-03-11**, its last **2026-08-15** — **5.2 of the 8 months are under version control**; earlier work predates the repo. |
| Team | **1 developer**, part-time against an IT/ops role | Owner-stated; corroborated by `git shortlog` (one human author, two identities). |

### 1.2 Costing assumptions

| assumption | value | why |
|---|---|---|
| Currency | Figures reported in the currency of their source; five-year total in **USD** with a CAD conversion. | KOR operates in BC and Southern California. |
| Seats for the buy path | **40** | Firm headcount, owner-stated. Vendor seat minimums above or below this are noted. |
| Buy-path horizon | **5 years**, no discounting | A discounted NPV would be more correct and less checkable. Undiscounted is stated so the reader can apply their own rate. |
| Buy-path escalation | **modelled at 0% and 5%/yr**, both shown | SaaS list prices in this sector have not been flat; showing both bounds the answer honestly. |
| The build cost **excludes** | requirements-gathering with a client, procurement, external QA, formal security review, and a support contract | A vendor build would carry all of these. **This exclusion is load-bearing in §6.2** — it is the main reason this report's central case sits below a published full-lifecycle function-point rate. |
| The build cost **includes** | design, implementation, unit test, integration, internal deployment, and a cross-cutting allowance for architecture, security, packaging and documentation (§5.4, Block 8) | Standard COCOMO II Post-Architecture scope. |

### 1.3 The three things most likely to be wrong

Stated up front rather than buried. These are *structural*; §11 lists the three weakest individual
numbers.

1. **A Click handler is a proxy for a user action, not a census of one.** Some of the 403 are one
   line; three are over 150. 53 further actions are MVVM `Command` bindings the Click census cannot
   see, and 542 attribute instances collapse to 403 distinct handlers because some are shared across
   views. §5.3 measures the distribution instead of assuming one, but the unit remains a proxy.
2. **The bottom-up estimate prices entry points, not everything behind them.** 403 actions,
   145 Revit commands, 23 MCP tools, 44 CLI tools and 40 jobs are *doors*. The services, models,
   repositories and renderers behind them are not separately enumerated — `Kor.Opportunities.Data`
   alone is 48,832 lines across 233 files that no door count reaches. **This is why §6 exists**, and
   why the two independent reconciliations, not the census, bind the top of the range.
3. **A vendor would not have built this system.** They would have built the system specified at the
   start, which is not the system that exists — this one changed shape continuously because the
   developer was also the user. Costing "what a vendor would charge to build this artefact" is a
   well-posed question; "what a vendor would have delivered" is a different and worse one.

---

## 2. Rates — sourced

Every figure carried forward from the previous revision with its original source URL and date.
**New in this revision: two further contract-rate sources (§2.6), which resolve the single-source
weakness the previous version flagged as its #2 least-trusted figure.**

### 2.1 A note on how these were obtained

`bls.gov`, `careeronestop.org` and several vendor pages return **HTTP 403** to this tool. The BLS
figures below were therefore taken from the **BLS Public Data API** (`api.bls.gov`), which serves the
same OES series and is a primary source. Series IDs are given so the reader can re-run the query.

### 2.2 Employee salary — United States

**VERIFIED.** BLS Occupational Employment and Wage Statistics, occupation **15-1252 Software
Developers**, reference period **May 2025** (the current OES release). Queried via the BLS Public
Data API on **2026-08-20**.

| measure | annual, USD | BLS series ID |
|---|---|---|
| National annual **mean** | **$148,100** | `OEUN000000000000015125204` |
| National annual **median** | **$135,980** | `OEUN000000000000015125213` |
| National **75th percentile** | **$171,980** | `OEUN000000000000015125214` |
| National **90th percentile** | **$214,670** | `OEUN000000000000015125215` |
| **Los Angeles–Long Beach–Anaheim** metro, annual mean | **$161,900** | `OEUM003108000000015125204` |

Query form: `https://api.bls.gov/publicAPI/v2/timeseries/data/<seriesID>` (VERIFIED, 2026-08-20).

**Which number to use.** A developer who single-handedly designs and ships a system of this surface
is not a median software developer. The **75th-to-90th percentile band, $172,000–$215,000**, is the
defensible base-salary range for the US equivalent hire; the LA metro mean of $161,900 is the right
geographic anchor for the Southern California office.

### 2.3 Employee salary — Canada (British Columbia)

**VERIFIED.** WorkBC (Province of British Columbia) career profile for **NOC 21232, Software
Developers and Programmers**, wage data year **2025**, sourced by WorkBC from *"B.C. Labour Market
Information Office, derived from 2025 Job Bank Wage data."* Fetched **2026-08-20** —
[workbc.ca/career-profiles/software-developers-and-programmers](https://www.workbc.ca/career-profiles/software-developers-and-programmers)

| measure | value (CAD) |
|---|---|
| Hourly **low** | **$31.25** |
| Hourly **median** | **$52.40** |
| Hourly **high** | **$84.13** |
| Annual salary (WorkBC's own figure) | **$107,315** |
| Employment in BC | 12,490 |

**INFERRED** — annualising the high hourly rate at 2,080 hours: $84.13 × 2,080 = **$174,990 CAD**.
That is the senior end of the BC market and the right base for a comparable hire in Vancouver.

`jobbank.gc.ca` itself could not be queried directly — the occupation-ID URL form returned the wrong
occupation (NOC 52113, Video and Sound Recorder) and the wage-search URL 404'd. WorkBC republishes
the same Job Bank data with the province named, so it is used instead and the provenance is stated.

### 2.4 Fully-loaded employer cost multiplier

**VERIFIED.** BLS **Employer Costs for Employee Compensation** (ECEC), civilian workers, cost per
hour worked, **Q1 2026**. Queried via the BLS Public Data API, **2026-08-20**.

| component | $/hour worked | series ID |
|---|---|---|
| Wages and salaries | **$33.72** | `CMU1020000000000D` |
| Total benefits | **$15.60** | `CMU1030000000000D` |
| **Total compensation** | **$49.32** | INFERRED: sum of the two |

**INFERRED arithmetic:**
- Benefits as a share of total compensation: 15.60 ÷ 49.32 = **31.6%**
- Multiplier on base wages for **benefits and payroll taxes only**: 49.32 ÷ 33.72 = **1.463×**

That 1.463× is the **floor**, and the only part of the multiplier with a government source behind it.
A fully-loaded *project* cost also carries workspace, hardware, developer tooling and licences,
recruiting, management overhead and non-project time. Standard practice in professional services —
and KOR's own Deltek cost structure — puts the total in the **1.6× to 2.0×** band.

**This report uses 1.75× as the central fully-loaded multiplier, and it is labelled INFERRED, not
verified.** A reader who prefers 1.6× or 2.0× can rescale every employee-cost figure linearly.
*(Corroboration, REPORTED: Hauerpower's 2026 nearshore rate guide states "Real TCO is 1.15-1.30x of
nominal hourly cost" — but that is a multiplier on an already-loaded contract rate, not on base
wages, so it is not directly comparable and is not used.)*

### 2.5 Foreign exchange

**VERIFIED.** Bank of Canada Valet API, daily average rate, observation date **2026-08-20**:
**1 USD = 1.3785 CAD** —
[bankofcanada.ca/valet/observations/FXUSDCAD/json?recent=1](https://www.bankofcanada.ca/valet/observations/FXUSDCAD/json?recent=1)

### 2.6 Contract and agency rates — now triple-sourced

**Employee-equivalent rates (VERIFIED base data):**

| basis | derivation | $/hour USD |
|---|---|---|
| **US senior employee, fully loaded** | $190,000 base (midpoint of the VERIFIED BLS 75th–90th percentile band) ÷ 2,080 h = $91.35; × **1.75** loaded (§2.4) | **$159.86** |
| **BC senior employee, fully loaded** | VERIFIED WorkBC high hourly CAD $84.13 × 1.75 = CAD $147.23; ÷ 1.3785 | **$106.80** |

**Contract rates — three independent sources, all REPORTED, all commercially interested:**

| region | Trio.dev (2026-04-21) | DistantJob (2026-02-12) | Hauerpower (upd. April 2026) | **used here** |
|---|---|---|---|---|
| **North America, senior** | $150–$255 | *"about $80 at the low end to $150–$200+"* | USA $150–$250 (architect $200–$350) | **$150–$255** |
| **Eastern Europe, senior** | $70–$105 | *"$25–$55/hour … Ukraine, Poland, Romania, Bulgaria"* | Poland $55–$75; Ukraine $40–$60 | **$40–$105** |
| **Nearshore LatAm, senior** | $50–$85 | *"roughly $23 to $90 per hour"* | LATAM $50–$70 | **$50–$85** |
| **Offshore South/SE Asia, senior** | $40–$70 | *"$26 to $41 per hour"* | India $25–$45 | **$25–$70** |

Sources: [trio.dev](https://www.trio.dev) (2026-04-21) ·
[distantjob.com/blog/offshore-developer-rates](https://distantjob.com/blog/offshore-developer-rates/)
(2026-02-12, author Ihor Shcherbinin, VP of Recruiting — DistantJob is a remote-staffing agency) ·
[hauerpower.com — nearshore rates 2026](https://www.hauerpower.com/en/insights-posts/nearshore-software-development-rates-2026)
(updated April 2026 — Hauerpower is a Polish nearshore vendor).

**What the second and third sources changed.** Two things, and both matter:

- **The North American band is confirmed by all three sources.** $150–$250 appears independently in
  all of them. **INFERRED cross-check from VERIFIED BLS data:** $91.35 base-hour × 1.463 (ECEC) =
  $133.65 fully-loaded employee cost; an agency must also carry ~70% utilisation and ~25% margin →
  $133.65 ÷ 0.70 ÷ 0.75 = **$254.57/hour**. Four independent routes now agree on the onshore band —
  **it is the best-supported rate input in this report**, and it carries the high case.
- **Trio's Eastern European and offshore bands are too high.** The two new sources put Eastern
  Europe at $25–$75 (Trio: $70–$105) and Asia at $25–$45 (Trio: $40–$70). The bands used above are
  widened downward accordingly. **This makes the low case cheaper, not dearer** — and it means the
  previous revision's low case was, if anything, conservative.

**All three remain commercially interested** — every one of them sells or brokers offshore
engineering labour, and all three have an incentive to make offshore look attractive relative to
onshore. That is why the *onshore* figure, which they have no incentive to understate and which the
BLS cross-check independently reproduces, is trusted most.

---

## 3. COCOMO II — the arithmetic, and where it breaks

**Unchanged in substance from the previous revision; the size input is restated on the new basis.**
COCOMO is retained as an anchor and explicitly not as the answer.

### 3.1 A sourcing problem that has to be stated first

**USC's COCOMO II home is gone.** `csse.usc.edu` has **no DNS record at all**; `sunset.usc.edu`
still resolves (68.181.32.42) but refuses HTTPS. The Model Definition Manual could not be retrieved
from its canonical location. What follows is assembled from what could actually be fetched, and
**each parameter carries the strength of its own source, not the reputation of the model**.

| parameter | value | status | source |
|---|---|---|---|
| Constant **A** (effort) | **2.94** | **REPORTED** — commercial tool vendor, not the manual | [softstarsystems.com/overview.htm](https://www.softstarsystems.com/overview.htm), fetched 2026-08-21: *"Effort = 2.94 × EAF × (KSLOC)^E"* |
| Constant **C** (schedule) | **3.67** | **REPORTED** — same page | *"Duration = 3.67 × (Effort)^SE"* |
| **Exponent formula** | **E = 1.01 + 0.01 × ΣSF** | **VERIFIED** — USC's own archived page | USC *"COCOMO II Cost Driver and Scale Driver Help"*, Eq. 12/13, Perma.cc capture 2018-05-01, recovered from the WARC at [archive.org/details/perma_cc_N6SF-HKWQ](https://archive.org/details/perma_cc_N6SF-HKWQ) |
| **ΣSF bounds** | all Extra High → **ΣSF = 0, E = 1.01**; all Very Low → **ΣSF = 25, E = 1.26** | **VERIFIED** — USC's own worked example | same |
| **Person-month** | **152 hours** | **VERIFIED** — US government document | Bernheisel, AFIT/DTIC **ADA329977**, 1997, verbatim *"…is based on 152 hours per person month"* — [archive.org/details/DTIC_ADA329977](https://archive.org/details/DTIC_ADA329977) |
| **Five scale factors** and **17 effort multipliers** — names and qualitative definitions | — | **VERIFIED** — USC archived page, Tables 20/21 | same |
| **Numeric per-level weights** for the scale factors and the 17 multipliers | — | **COULD NOT VERIFY** | Softstar's driver table is qualitative only; the **official COCOMO II Model Definition Manual (1998.0)**, located this revision at a .edu mirror and **read in full** ([athena.ecs.csus.edu/~buckley/CSc231_files/Cocomo_II_Manual.pdf](https://athena.ecs.csus.edu/~buckley/CSc231_files/Cocomo_II_Manual.pdf)), covers only Early Design and Post-Architecture; GeeksforGeeks is client-rendered; tutorialspoint 404; sanfoundry, Namcook and ResearchGate 403; the Prentice-Hall book is controlled-lending on archive.org |
| Commonly-quoted **B = 0.91** and **D = 0.28** | — | **COULD NOT VERIFY — and contradicted** | Every reachable primary source gives **1.01**, not 0.91. **Not used below.** |

### 3.2 The calculation, using only verified parameters

Size on the revised basis = **351 KSLOC of C#** (§1.1: 350,925 lines).
**PM = 2.94 × KSLOC^E**, **E = 1.01 + 0.01 × ΣSF**.

| E | scale-factor configuration | arithmetic | person-months | hours (× 152) |
|---|---|---|---|---|
| **1.01** | All five Extra High (ΣSF = 0) — the most favourable the model allows | 2.94 × 351^1.01 = 2.94 × 372.2 | **1,094** | **166,300** |
| **1.135** | Mid-range (ΣSF ≈ 12.5) | 2.94 × 351^1.135 = 2.94 × 774.3 | **2,277** | **346,000** |
| **1.26** | All five Very Low (ΣSF = 25) — worst case | 2.94 × 351^1.26 = 2.94 × 1,610.9 | **4,736** | **719,900** |

**Even the model's most generous possible answer is 1,094 person-months — 91 person-years.**

### 3.3 Applying the effort multipliers (illustrative only)

KOR's project rates favourably on most of the 17 multipliers: one developer means perfect team
cohesion (SITE, TEAM) and zero turnover (PCON — USC's verified definition puts Extra High at
*"3%/year"*); the developer is also the end user, so applications experience (APEX) is at the top of
the scale; tooling is modern (TOOL); and the data volumes are large (DATA — at 372,370 emails this
project is at the high end, which pushes effort *up*).

**Using the standard published multiplier values — which could NOT be verified —** the product of
the 17 multipliers (EAF) for a project of this shape works out to roughly **0.36**:

| E | PM × 0.36 | hours |
|---|---|---|
| 1.01 | **394** | **59,900** |
| 1.135 | **820** | **124,600** |
| 1.26 | **1,705** | **259,200** |

**Tag: INFERRED, on top of unverified inputs.** Note where it lands: the most favourable adjusted
figure, **59,900 hours**, now sits **inside** this report's revised range, between the central
(47,400 h) and the high (84,400 h). That is the only sense in which COCOMO corroborates anything
here — but it is a better fit than in the previous revision, where it sat above the entire range.

### 3.4 Schedule

TDEV = 3.67 × PM^F, where F = D + 0.2 × (E − B). At E = 1.01 the scale-factor term is zero, so F
reduces to **D**, whose value (~0.28) **could not be verified** — the result below is INFERRED on an
unverified exponent and is the softest arithmetic in this section.

| effort | TDEV = 3.67 × PM^0.28 | schedule | implied average team |
|---|---|---|---|
| 394 PM (illustrative adjusted) | 3.67 × 394^0.28 = 3.67 × 5.33 | **19.6 months** | **≈ 20 people** |
| 1,094 PM (unadjusted, E = 1.01) | 3.67 × 1,094^0.28 = 3.67 × 7.10 | **26.0 months** | **≈ 42 people** |

**The actual project: 8 months, one person, part-time.** The model's most conservative reading still
calls for twenty people over twenty months.

### 3.5 Where COCOMO over-estimates — with evidence, not assertion

**(a) The model misses badly even on the data it was built for.** VERIFIED, and the strongest single
piece of evidence against treating any COCOMO number as truth. The AFIT/DTIC calibration study
measured COCOMO II against four US DoD datasets. Boehm's own stated accuracy target is
**MMRE < 0.25**. Measured, in default mode:

| application type | MMRE (calibration) | PRED(.25) | MMRE (validation) | PRED(.25) |
|---|---|---|---|---|
| Military Ground — Signal Processing | 0.4084 | 0.3846 | 0.4507 | 0.3333 |
| Ground in Support of Space | 0.5941 | 0.2167 | 0.7077 | **0.0667** |
| Military Mobile | 0.6817 | **0.0800** | 0.7930 | 0.1000 |

The thesis's own conclusion, verbatim: *"the accuracy results were poor; the best having an accuracy
of only .3332 within 40 percent of the time in calibrated mode."* **A model that lands within 25% of
actual between 7% and 38% of the time is not a number you build a business case on.**
(AFIT/DTIC ADA329977 — [archive.org/details/DTIC_ADA329977](https://archive.org/details/DTIC_ADA329977))

**(b) It was calibrated on 161 projects, none of them modern.** REPORTED —
[Wikipedia's COCOMO article](https://en.wikipedia.org/wiki/COCOMO) (fetched 2026-08-21) states
COCOMO II was *"tuned using a larger database of 161 projects"*, citing Boehm et al., 2000.
**A model calibrated in the 1990s is being asked here about a .NET 8 codebase built on NuGet
packages with AI assistance.**

**(c) High-level languages: the same function costs about half the lines.** REPORTED — QSM
*Function Point Languages Table v5.0*, from **2,192 completed function-point projects**
([qsm.com](https://www.qsm.com/resources/function-point-languages-table), **re-fetched and
re-verified 2026-08-21**):

| language | average SLOC per function point | median | range |
|---|---|---|---|
| **C#** | **54** | 59 | 29–70 |
| C++ | 50 | 53 | 25–80 |
| **C** | **97** | 99 | 39–333 |

Delivering the same functionality in C takes roughly **1.8× the lines** it takes in C#.

**(d) Library and package reuse is invisible to LOC.** `Kor.Opportunities.Data` — 48,832 lines
across 233 files — sits on Entity Framework, `Microsoft.Graph`, `HttpClient` and `SqlClient`.
**could not verify** — no published study quantifying the NuGet reuse effect on COCOMO accuracy was
reachable; this point is argued, not evidenced.

**(e) AI-assisted development breaks the model's core assumption, and the literature now says so.**
VERIFIED — El-Ramly, *"ACEM: A Cost Estimation Model for Agentic Software Engineering"*, arXiv
**2608.02582**, submitted **2026-08-03** — [arxiv.org/abs/2608.02582](https://arxiv.org/abs/2608.02582).
Its premise is that models *"such as COCOMO II"* assume *"development effort is primarily driven by
human labor"*, which breaks when *"autonomous AI agents perform substantial implementation work"*.
The author is explicit that ACEM is *"an early-stage proposal"* with *"constants left symbolic
pending empirical grounding"* — **a published recognition of the problem, but no replacement model
with numbers in it.**

Koch & Wellbrock, *"Agile V"*, arXiv **2602.20684**, 2026-02-24 —
[arxiv.org/abs/2602.20684](https://arxiv.org/abs/2602.20684) — reports *"an estimated **10-50x cost
reduction versus a COCOMO II baseline**"*. **Treat with real caution:** the case study is ~500 lines
of code, 8 requirements, 54 tests, by the framework's own authors, and does not state which COCOMO II
parametrisation it compared against.

**(f) The reality check.** COCOMO II's most favourable verified configuration says **166,300 hours**.
The actual build consumed at most **1,360 hours**. That is a factor of **122×**. Even the
multiplier-adjusted 59,900 hours is **44×**.

**No reasonable reading closes a 122× gap.** Part is COCOMO over-estimating; part is that 351,000
lines of modern C# is not 351,000 lines of hand-written 1990s code.

---

## 4. Function points — sized, and now costable

**This section changes materially in this revision.** The previous version sized the system in
function points and then dead-ended, reporting that no free hours-per-function-point benchmark could
be found. **Two sourced delivery rates were located this revision**, both hosted by IFPUG — a
standards body that does not sell development services.

### 4.1 Size

Using QSM's re-verified gearing on the 350,925-line C# codebase (**INFERRED arithmetic**):

| basis | SLOC/FP | implied function points |
|---|---|---|
| QSM C# average | 54 | **6,500 FP** |
| QSM C# median | 59 | **5,950 FP** |
| QSM C# range | 29 – 70 | **5,010 – 12,100 FP** |

**6,500 function points is a genuinely large system** — comparable to a mid-sized commercial ERP
module, not a departmental tool.

### 4.2 Delivery rate — the gap the previous revision could not fill

**Source 1 — VERIFIED, standards body.** IFPUG uTip #03, *"Early FPA and Consistent Cost
Estimating,"* version 2.0, authors Adri Timp and Marcello Sgamma, published per IFPUG's own
announcement dated **2026-06-24** —
[ifpug.org/wp-content/uploads/2026/06/uTip-003-Early-FPA.pdf](https://ifpug.org/wp-content/uploads/2026/06/uTip-003-Early-FPA.pdf)

Verbatim, the worked example:

> *"If an organization has a productivity rate of 10 hours / FP and the FPA during the Feasibility
> Phase would result in 100 FPs, the effort estimate would be (100 + 20%) x 10 hours / FP = 1200
> hours."*

**10 hours/FP is IFPUG's illustrative organizational example, not a universal claim** — the document
is explicit that the rate is the organization's own. It is used here as a *lower* anchor.

The same document gives IFPUG's early-sizing rules, which are the closest thing found to a
"hours per screen" benchmark:

> *"the indicative functional size = 35 x number of ILFs + 15 x number of EIFs"*

> *"The size of an EP is 4.6 SFP, while the size of a LF is 7.0 SFP... Size[SFP] = 4.6 x #EP + 7 x #LF"*

> *"If the type of a function (EI, EO or EQ, ILF or EIF) is not known, just assign 5 function points
> for each unknown function type."*

(EI / EO / EQ are IFPUG's proxies for input screens, output screens/reports and inquiry screens.)

**Source 2 — REPORTED, industry average.** Capers Jones, *"Using Artificial Intelligence (AI) For
Large Software Engineering Projects – Part 1,"* released to IFPUG January 2025, hosted on IFPUG's
site — [ifpug.org/wp-content/uploads/2025/03/Capers1.pdf](https://ifpug.org/wp-content/uploads/2025/03/Capers1.pdf)

Verbatim:

> *"Circa 2025, the U.S. average software productivity is roughly 8.00 function points per staff
> month or 16.5 work hours per function point."*

**Tagged REPORTED, not VERIFIED, despite the IFPUG hosting**, because Jones ran Namcook Consulting
and Software Productivity Research, both of which sold parametric estimation tools. He has a
commercial history in exactly this market. The document's non-commercial host does not neutralise
the author's interest.

### 4.3 What the function-point route says

**INFERRED arithmetic**, on 6,500 FP:

| rate | source | hours | at $120/h blend |
|---|---|---|---|
| 10 h/FP | IFPUG uTip #03 illustrative | **65,000** | $7.80M |
| 16.5 h/FP | Capers Jones, US average circa 2025 | **107,250** | $12.87M |

**Both land above this report's central case of 47,400 hours.** That is a genuine tension and §6.2
addresses it rather than burying it.

### 4.4 Object points — the benchmark that fits the question and does not fit the system

**Sought specifically for this revision**, because COCOMO II's Application Composition model prices
software in *object points* with explicit weights for **screens** and **reports** — precisely the
hours-per-screen instrument §5 needed.

**Primary sourcing failed.** The official COCOMO II Model Definition Manual (1998.0), located at a
.edu mirror and **read in full** this session, contains **only** Early Design and Post-Architecture —
there is no Application Composition chapter in it. The primary source is Boehm, Clark, Horowitz,
Westland, Madachy & Selby, *"Cost Models for Future Software Life Cycle Processes: COCOMO 2.0,"*
*Annals of Software Engineering*, 1995 — **paywalled** at Springer (DOI 10.1007/BF02249046, redirects
to an identity-provider login) — and Boehm's 2000 Prentice-Hall book, on archive.org as a
**lending-restricted** scan (full text and search-inside both 403).

**Two independent tertiary reproductions agree exactly** and are reported as **REPORTED (tertiary)**:
a university course deck (Vijay Kumar, *"Software estimation models II"*,
[slideshare.net](https://www.slideshare.net/slideshow/software-estimation-models-ii-lec-05/80085188))
and [GeeksforGeeks](https://www.geeksforgeeks.org/software-engineering-application-composition-estimation-model-cocomo-ii-stage-1/)
(updated 2025-07-11), which independently confirms three of the same cells from its worked example:

| object type | simple | medium | difficult |
|---|---|---|---|
| **Screen** | 1 | 2 | 3 |
| **Report** | 2 | 5 | 8 |
| 3GL component | — | — | 10 |

Productivity (NOP per person-month): Very Low **4** · Low **7** · Nominal **13** · High **25** ·
Very High **50**.

**Applied honestly, it does not fit — and the misfit is informative.** Object points were designed
to size *Application Composition*: screen-and-report prototyping in an integrated CASE environment.
They cannot see a `.sdb` writer, a COM driver, a Revit bridge or an entity-resolution pipeline —
which is where most of this system's cost lives. Applying it to the whole suite would be a category
error.

**But it can be applied to the UI layer alone, as a check on Blocks 1+2** (INFERRED):

- 122 views at "medium" (weight 2) = 244 object points
- ~40 report/document generators at "medium" (weight 5) = 200 object points
- NOP = 444 (no reuse discount applied)
- At "High" productivity (25 NOP/PM): 444 ÷ 25 = 17.8 PM × 152 h = **2,700 hours**
- At "Nominal" (13 NOP/PM): 34.2 PM × 152 h = **5,190 hours**

**This report's Blocks 1+2 total 9,222 central hours for the same layer — 1.8× to 3.4× above what
object points imply.** So the UI blocks are, if anything, generous. That runs in the *opposite*
direction to §4.3's function-point check, which says the total is conservative. **Two independent
methods disagreeing in opposite directions about different parts of the estimate is a better result
than either agreeing blindly**, and §6 keeps both.

---

## 5. The bottom-up feature estimate

This is the centrepiece of the revision and it replaces the previous report's §4.2 entirely.

### 5.1 Why "11 modules × 4,000 hours" was the wrong unit

- **It is insensitive to the product.** The same 45,000 hours would have come out if the suite had
  40 screens or 400. The unit measured the report, not the system.
- **It hid the misses.** A module-level unit cannot notice a per-bookmark annotation workflow, a
  five-format CSI exporter, a second takeoff product, or an entire drafting agent. All four were
  missed, and the estimate did not move, because the estimate could not see features.
- **It cannot be checked.** "4,000 hours per module" has no denominator anyone can audit.

The replacement unit is the **user-invocable action**, the **integration surface** and the **named
product estate** — all of which can be counted by machine and re-counted by anyone with the repo.

### 5.2 The benchmark I wanted and could not verify

**Stated plainly because the brief asked for published agency benchmarks.** A dedicated research pass
looked for a citable hours-per-screen, hours-per-CRUD-screen, hours-per-report or
hours-per-workflow figure.

**Hours per screen and hours per CRUD screen: could not verify.** No academic, standards-body or
government source publishes one. Several development agencies were checked directly and confirmed to
publish whole-project ranges while *avoiding* per-screen hour tables — one
([budventure.technology](https://budventure.technology)) states on its own calculator page that it
deliberately uses a role-based model *"instead of a flat cost per screen shortcut"*. That is a vendor
saying this specific benchmark is too crude to publish. The only per-screen figure found anywhere
was an undated Reddit comment from a UI designer (*"Usually it takes me 2-5h work time per screen"*,
design time only) — **anecdote, not benchmark, and not used.**

**Hours per report and hours per workflow: could not verify.** Only qualitative statements were
found.

**Hours per integration: found, but only from vendors selling integration platforms**, all of whom
have an incentive to make hand-built integration look expensive. Recorded for completeness and
**not** used as the basis for Block 4:

| figure | source | date | interest |
|---|---|---|---|
| *"…would, according to Alex's estimate, take one of their engineers 40 hours, on average"* per HRIS/ticketing integration | [merge.dev case study — Kertos](https://www.merge.dev/case-studies/kertos) | undated | Merge sells a unified-API platform |
| *"Most developers spend 40-50 hours building TikTok integration"* + *"5-10 hours/month"* maintenance | [bundle.social](https://bundle.social/blog/tiktok-api-integration-cost) | 2026-08-17 | sells a unified social API |
| *"If a traditional manual point-to-point integration takes a baseline of 100 hours to build…"* | [LinkedIn, John Root](https://www.linkedin.com/pulse/manual-point-to-point-integration-takes-100-hours-build-john-root-rlehe) | 2026-05-18 | MuleSoft-aligned marketing |
| API integrations at **$4,000–$6,000 each** | [topflightapps.com](https://topflightapps.com/ideas/app-development-costs/) | 2026 | custom app agency |

Those cluster at **40–100 hours for a simple SaaS API integration**. Block 4's integrations are of a
different order — an undocumented commercial binary format, a COM automation surface, an ERP schema
that drifts between releases — so the cluster is used as a **floor sanity-check**, not a rate. Block
4's lowest line (the LLM provider, 100/200/350 h) sits at or above the top of that cluster, which is
the right relationship.

**Consequence, stated rather than disguised: the per-unit hour bands in §5.4 are INFERRED
professional judgement, not sourced benchmarks.** They are the largest single driver of the
bottom-up total and they head §11. What §5.3 supplies is a **measured distribution to apply them
to** — the part that can be audited, and the part "× 4,000 hours" had none of.

### 5.3 403 Click handlers is a proxy — here is what it actually contains

**VERIFIED by direct measurement, 2026-08-21.** Every one of the 403 distinct `Click` handler names
in the 127 XAML files was matched to its method body in C# by brace-matching. **403 of 403 resolved;
none is orphaned.**

**First cut — body size:**

| handler body | count | share |
|---|---|---|
| 1–5 lines | 139 | 34.5% |
| 6–20 lines | 160 | 39.7% |
| 21–60 lines | 91 | 22.6% |
| 61–150 lines | 10 | 2.5% |
| over 150 lines | 3 | 0.7% |
| **total** | **403** | median **9**, mean **16.4**, sum **6,609** lines |

Largest: `ExportUtilizationBtn_Click` (187), `SafeApiExport_Click` (182), `EtabsApiExport_Click` (166),
`Sap2000ApiExport_Click` (90). **Three of the four largest are the CSI exporter's live-API buttons** —
the estate §0 says the audit under-described.

**But body size is the wrong measure, and using it alone would have been dishonest.** A four-line
handler reading `await _exportService.RunAsync(...)` is not a trivial feature; it is a door onto a
pipeline. So each handler was also classified by **what it reaches into**:

| depth band | definition | count | share | median lines |
|---|---|---|---|---|
| **Deep** | performs data access, or awaits a service call, or exceeds 60 lines | **106** | 26.3% | 25 |
| **Mid** | awaits, or calls a service/file/dialog, or exceeds 20 lines | **169** | 41.9% | 9 |
| **Shallow** | UI-local only — close, toggle, navigate, set a property | **128** | 31.8% | 5 |

Supporting signals, measured across the same 403:

| signal | count |
|---|---|
| calls a service, repository or client | **204** |
| contains `await` | 129 |
| opens a window or dialog | 57 |
| performs file or process I/O | 46 |
| opens a `SqlConnection`/ODBC connection **directly in the handler** | **0** |

That last row is worth a sentence: **no handler talks to a database directly.** Data access sits
behind services throughout. That is an architectural finding, not a cost finding, but it is why the
depth classification is trustworthy — the layering is consistent enough to classify.

**Two honest limits, both material:**

1. **It undercounts actions.** 53 further distinct actions are bound as MVVM `Command="{Binding …}"`
   and carry no `Click` attribute. They are **not** added below — the estimate is built on the 403
   alone, making it conservative by roughly 13%.
2. **It is a WPF census.** `KOR Inspections Bookings` (19,385 lines) has no WPF surface and is
   invisible to it; it is priced separately in Block 3b.

### 5.4 The arithmetic

All hour bands are **INFERRED** (§5.2). All counts are **VERIFIED** (§1.1). Every multiplication is
shown.

#### Block 1 — 403 user-invocable actions

Per action, including its share of XAML, wiring, validation, error handling and verification:

| band | count | low h | central h | high h | low | central | high |
|---|---|---|---|---|---|---|---|
| Shallow | 128 | 2 | 4 | 6 | 256 | 512 | 768 |
| Mid | 169 | 8 | 14 | 22 | 1,352 | 2,366 | 3,718 |
| Deep | 106 | 24 | 40 | 70 | 2,544 | 4,240 | 7,420 |
| **Block 1** | **403** | | | | **4,152** | **7,118** | **11,906** |

#### Block 2 — 122 view shells

The *container* rather than the behaviour: layout, styling, binding, view-model, state, validation,
navigation wiring. Priced separately from Block 1 so the two are not conflated.

| unit | count | low h | central h | high h | low | central | high |
|---|---|---|---|---|---|---|---|
| WPF Windows | 80 | 10 | 20 | 32 | 800 | 1,600 | 2,560 |
| UserControls | 42 | 6 | 12 | 20 | 252 | 504 | 840 |
| **Block 2** | **122** | | | | **1,052** | **2,104** | **3,400** |

*Checked against object points in §4.4, which implies 2,700–5,190 hours for this layer. Blocks 1+2
at 9,222 central are 1.8×–3.4× that. Flagged, not adjusted — see §6.3.*

#### Block 3 — non-UI invocable surface

Entry points with no window attached. The previous report had no line for any of these.

| unit | count | low h | central h | high h | low | central | high |
|---|---|---|---|---|---|---|---|
| MCP analytical tools | 23 | 10 | 20 | 36 | 230 | 460 | 828 |
| Revit ribbon commands — **banded, see below** | 72 | — | — | — | 609 | 1,207 | 2,104 |
| CLI tools | 44 | 6 | 14 | 28 | 264 | 616 | 1,232 |
| Worker jobs (`IJob`) | 40 | 12 | 24 | 44 | 480 | 960 | 1,760 |
| **Block 3** | **179** | | | | **1,583** | **3,243** | **5,924** |

**How the 145 Revit commands were banded, and why not flat.** The brief asked for per-command Revit
API benchmarks; **none could be found** (§5.2 — the search that failed for screens failed for Revit
commands too). So the same method used for Click handlers was applied: **measure the commands, then
band them.**

`Tools/` holds **8,549 lines across 145 commands — an average of 59 lines each**, because the ribbon
is generated from `ToolCatalog.cs` and the commands share a 2,336-line `Framework/`. **A flat
per-command rate would therefore have over-priced this suite by roughly half.** Measured lines per
command, by category:

| band | categories (measured lines ÷ commands) | commands | low h | central h | high h |
|---|---|---|---|---|---|
| **Heavy** (>90 lines/cmd) | Data 185 · Insert 113 · Dimensions 112 · Diagnostics 102 | 11 | 16 | 30 | 52 |
| **Medium** (50–90) | Review 83 · Params 81 · ViewNaming 80 · Productivity 70 · Structural 70 · ConvertImp2Met 70 · Annotation 62 · Visibility 59 | 50 | 8 | 16 | 28 |
| **Light** (<50) | Elements 44 · Text 38 | 11 | 3 | 7 | 12 |
| **subtotal** | | **72** | | | **609 / 1,207 / 2,104** |

Arithmetic: low 11×16 + 50×8 + 11×3 = 176 + 400 + 33 = **609**; central 11×30 + 50×16 + 11×7 =
330 + 800 + 77 = **1,207**; high 11×52 + 50×28 + 11×12 = 572 + 1,400 + 132 = **2,104**.

**The other 73 commands are carved out of Block 3 entirely** and priced as products in Block 6:
the **50-command rebar detailing suite** (6.12) and the **23-command sheet-composition suite**
(6.11). They are not counted twice. The **eight-version compatibility matrix**, which an agency
would quote separately, is Block 6.4.

*Honest note on command count versus command complexity:* "Ungroup All" and "auto-place chosen views
onto a sheet in a grid" are both one catalog entry. The banding above is the answer to that, and it
is driven by measured code volume per category — but code volume is itself only a proxy for
difficulty, and the 50 rebar commands (21 lines each) are thin *variants* of a few real behaviours
rather than 50 independent tools. **That is precisely why the rebar suite is priced at 640 central
hours and not at 50 × anything.**

#### Block 3b — Inspections Bookings

19,385 lines, a separate application with no WPF surface, therefore invisible to the action census.

| | low | central | high |
|---|---|---|---|
| **Block 3b** | **900** | **1,800** | **3,000** |

#### Block 4 — integrations

Ten distinct integration surfaces. The 111-source ingestion pipeline is priced in Block 6.5 instead,
to avoid double counting.

| integration | low | central | high |
|---|---|---|---|
| Deltek Vantagepoint over ODBC — 29 tables, 28 joins, `INFORMATION_SCHEMA` release-drift probing | 300 | 600 | 1,000 |
| Microsoft Graph / SharePoint | 200 | 400 | 700 |
| Outlook VSTO add-in | 150 | 300 | 500 |
| Revit API surface (the API itself; the *matrix* is Block 6.4) | 200 | 400 | 700 |
| CSI COM / OAPI automation surface (SAFE, ETABS, SAP2000 driven live) | 250 | 500 | 900 |
| SQL Server full-text over 372,370 emails | 120 | 250 | 450 |
| MCP server — protocol, transport, hosting | 120 | 250 | 450 |
| LLM provider | 100 | 200 | 350 |
| PDF parsing, rendering and generation | 200 | 400 | 700 |
| Transmittal redirector web tier | 100 | 200 | 350 |
| **Block 4** | **1,740** | **3,500** | **6,100** |

#### Block 5 — data and persistence estate

312 SQL scripts, 42 KorStandards migrations, 48,963 lines of SQL, and the schema design behind them.

| | low | central | high |
|---|---|---|---|
| **Block 5** | **800** | **1,600** | **2,800** |

#### Block 6 — the thirteen previously-uncosted estates

**Each is its own line, as the brief requires.**

| # | estate | low | central | high | basis |
|---|---|---|---|---|---|
| 6.1 | **Standards estate — software**: Revit palette reading from SQL, approval/publication/watermarking governance, conformance-scoring engine | 700 | 1,400 | 2,400 | 42 migrations, `vw_PaletteCatalog`, the palette add-in, conformance runs green 8/8 at run #14 |
| 6.1b | **Standards estate — corpus curation**: 612 canonical details classified, numbered and filed across four disciplines; ~379-family component canon; 1,079 view occurrences reconciled; 49,468 lines of census/geometry/layout/markup corpus | 500 | 1,000 | 1,800 | 612 details at ~0.5–1.5 h each to classify, mint and verify, plus the family canon. Analyst labour, but a vendor bills it. |
| 6.2 | **DXF → ETABS generator**, and its share of the 483 executed tests | 900 | 1,700 | 2,800 | `EngineeringTools.Core/Dxf/` = 7,270 lines + test share. Geometry, level inference, wall/opening/pier logic, `.e2k` emission. |
| 6.3 | **Per-bookmark PDF annotation workflow** | 40 | 90 | 160 | `PdfBookmarkExtractor` + `BookmarkNotesWindow` (189 lines) + `CoverSheetRenderer` integration + the model field |
| 6.4 | **Revit eight-configuration version matrix** — build config, per-year API shims, CI, deployment, 8× regression | 250 | 500 | 900 | `R20`–`R27`, three TFMs |
| 6.5 | **111-source ingestion pipeline** — 13 providers, 111 source configurations, entity resolution, dedup, source-health observability | 900 | 1,800 | 3,200 | 111 sources at ~4–12 h of configuration and hardening each, plus the provider classes |
| **6.6** | **PDF → CSI five-format exporter suite + live COM/OAPI drivers** | **1,400** | **2,600** | **4,400** | 46 files, 12,990 lines. Five proprietary formats (`.f2k`/`.e2k`/`.sdb`/`.edb`/`.$et`) + DXF, plus three live vendor-API exporters. See rate note below. |
| **6.7** | **Two structural takeoff products** — Quantity Takeoff, Structural Takeoff with OCR, shared engine, separate CLI | **1,100** | **2,100** | **3,500** | ~9,550 lines of takeoff engine in `EngineeringTools.Core` + 3,562 `TakeoffCli` + 1,095 UI. Verified output: 19,545 cy whole-building, byte-reproducible, zero AI calls in deterministic mode. |
| **6.8** | **Virtual Drafter** — **repriced 2026-08-21 against `modules/13-virtual-drafter.md`; see the four sub-lines and the deletions below** | **860** | **1,680** | **2,870** | Bridge 3,489 lines / 42 verbs + a 39 KB seed-prompt-and-protocol pair + fleet crawl corpus + exam and drill QA. **Down 30% from the 2,400 h first carried here.** |
| 6.8a | — bridge add-in: 42 JSON verbs, one named transaction per write, central-write refusal, 110 `throw new` with named reasons, **zero** TODO/FIXME/NotImplemented markers, builds clean on the R20–R27 matrix | 350 | 650 | 1,100 | 3,489 lines across 4 files. At 8–12 LOC/hour that is 290–435 h; the band sits above it because each verb wraps a Revit API call that needed its own product-level verification. |
| 6.8b | — seed prompt and wire protocol: a 9.2 KB `CLAUDE.md` and a 29.7 KB protocol document | 80 | 180 | 320 | **~39 KB of prose, not code.** This is where the drafting judgement lives, and it is specification work, not software. Iterated across 69 commits in 30 days. |
| 6.8c | — verification and exam QA: `VERB-SMOKE` scoring all 45 verbs on a fresh product re-read (44 PASS), DRILL-02/02b/02c/03/04/05, 85 process-record reports, the closed-book exam including building its answer key and capturing evidence | 250 | 500 | 850 | Real, non-trivial QA effort, priced as effort **regardless of what the exam scored** — see the correction below. |
| 6.8d | — fleet crawl tooling and corpus: 194 per-job model dossiers, a rebar census, a markup lexicon, a ruling register | 180 | 350 | 600 | `crawl-results` 19,420 lines + `CRAWL-RUNBOOK.md`. **Distinct from the 612-detail canon in 6.1b** — that is the standards catalogue; this is a census of the existing model fleet. |
| **6.9** | **ETABS native plugins** — three projects against a commercial structural analysis package's plugin API | **900** | **1,700** | **2,900** | 17,688 lines / 3 `.csproj`. Same specialist band as 6.6: a vendor-API plugin for a commercial FEA package, not general application work. |
| **6.10** | **DemoStudio** — recorder / composer / publisher shell, session state machine, FlaUI automation, redaction subsystem, smoke harness (**dormant**) | **1,300** | **2,500** | **4,200** | 29,876 C# + 1,988 XAML / 13 projects with clean application-domain-infrastructure boundaries. **1 commit in 90 days; most files last touched March 2026.** Priced as built; the dormancy is disclosed, not discounted. |
| **6.11** | **Revit sheet-composition suite** — 23 commands: batch sheet creation from a `number=name` list against a chosen title block, grid-packed view placement, copy-detailing to many views, sheet duplication with views/legends/schedules re-placed, legend push, find/replace renumbering, batch revisions, `SheetNo_DetailNo_Title` view renaming, viewport align/distribute, click-order detail renumbering, per-sheet PDF export | **380** | **750** | **1,300** | `Tools/Sheets` 960 lines + `Tools/Views` 753 + `Tools/ViewNaming` 80 = 1,793 lines, plus the unit-tested `GridPacker`. **A previous audit reported this capability as absent; it exists.** Carved out of Block 3. |
| **6.12** | **Revit rebar detailing suite** — 50 commands: partition-based mark numbering so identical bars share a mark, tag-all-untagged, group reinforcing with smart tags, batch parameter edit across a selection, view-wide toggle | **320** | **640** | **1,150** | `Tools/Rebar` 1,034 lines across 50 commands (**21 lines each — thin variants of a few real behaviours**), plus the unit-tested `RebarNumberer`. **Distinct from the rebar-change-detection tool in Operations; both exist.** Carved out of Block 3. |
| **6.13** | **Decompilation-and-recovery of the departed Revit lead's tooling** — reverse-engineering, decompilation, cleanup, and a tool-by-tool parity matrix | **200** | **450** | **800** | **The effort is priced; the 36,375 decompiled lines are NOT counted as source (§0).** `legacy-parity-matrix.md` maps 40 legacy ribbon buttons tool-by-tool against `Ribbon2025.cs` and the deployed `\\KOR-302N\c$\ProgramData\2015_RevitCommands\` DLL set. This is the concrete evidence behind "we replaced his tools". |
| | **Block 6** | **9,750** | **18,910** | **32,380** | |

**What was removed from 6.8, and why — the largest single correction in this revision.**
`modules/13-virtual-drafter.md` (audited 2026-08-21, `RUN` + `QUERIED` tiers) establishes that the
Virtual Drafter **is not autonomous software**. It is **Claude Code on one workstation**, reading a
9.2 KB seed prompt, driving Revit through the bridge. **No code anywhere decides what to draft** —
every drafting judgement is model inference at run time. The audit's own words: *"There is no
compiled drafting application, no UI, no ribbon … and — critically — no code anywhere in the repo
that decides what to draft. The C# is 3,489 lines and every line of it is plumbing."*

| removed | central h | why |
|---|---|---|
| A planner / rules engine / drafting-decision layer | ~400 | **Never written.** The judgement is run-time inference, not software. A first pass would have priced an autonomous agent; one does not exist. |
| Job instantiation from template | ~250 | **Does not exist** `[RUN]`. `TEMPLATE-BUILD-PLAN.md` specifies an `.rte`; `find . -iname "*.rte"` returns nothing, no verb instantiates one, the title block was never parameterised, and the plan's own acceptance test has never been run. |
| Rules database and conformance scorer | ~250 | **Double count.** These are the standards estate and are already priced at Blocks 6.1 and 6.1b. Removed here rather than counted twice. |
| Scheduler / unattended second-machine operation | — | `DEAD` per the audit — `Dialog-Watchdog.ps1` exists only on KOR-302N and is not in the repo; ROADMAP C1 remains open. One person, one workstation. |
| **Total removed** | **~900** | offset by ~180 h added for the seed-prompt/protocol line, which the first pass had no line for at all |

**Three things this repricing does *not* do.** It does not reduce 6.8c: the exam and drill QA is real
built effort and is priced as such **independently of what the exam scored**. It does not use the
`ECONOMICS-BASELINE.md` figures as any kind of input — the audit shows the *"69,599 operations"*
headline is **95.7% database reads** (3,014 actual writes), that a single day's read-only fleet crawl
contributes 61,336 of them, and that **DRILL-06, the actual $/task benchmark, has no report among the
85** `[RUN]`. **No productivity or ROI number from that document appears anywhere in this report.**
And it does not treat the estate as more complete than it is: the audit also found the shipped bridge
binaries two weeks stale, `newtag` returning `ok:true` for tags invisible on the sheet, and views
resolved by name only in models with duplicate view names.

**A correction carried here because this report cited the exam.** An earlier draft of this section
quoted `EXAM-SCORECARD.md`'s *"12/12 markup items implemented; 2 locations more faithful to the
markup than production."* **The first half stands; the second half is stale.** KOR's own
`standards/RULINGS.md:51-53` records Jim DesRoches adjudicating the three contested points on
**2026-07-31**: he gave the machine **one** (a `#5`-vs-`#6` error in issued production work), gave
production the second (14 bars, not 12), and ruled against the machine on Level 13 scope. The
defensible statement is **"12 of 12 marked locations implemented and verified; of three contested
points the engineer sided with the machine once and with the human twice."** The audit separately
verified the closed-book condition forensically against the 8.5 MB session transcript — zero
references to the answer key or the production drafter — so **the exam is genuine; it is the score
that was overstated.** The related *"61 DXFs under agent control"* claim is likewise human-assisted:
three `opendoc` failures, ~190 s lost, and a Revit restart.

**None of this changes 6.8c's hours** — the QA was performed either way — **but it is recorded here
because a cost report that cites an achievement inherits the duty to cite it correctly.**

**A rate note on 6.6, per the brief's instruction to reflect specialism in the rate as well as the
hours.** Writing a correct `.e2k`, `.f2k` or `.sdb` emitter against an undocumented commercial format,
and driving CSI products over COM, is not general application work — it needs a developer who is also
a structural modeller. That labour does not price at the blended $120/hour used in §8.1; it prices at
the top of the onshore band. **At $200/hour rather than $120, Block 6.6's central line alone moves
from $312,000 to $520,000 — a $208,000 swing** that the blended grid does not show. The same argument
applies with less force to 6.2, 6.7 and 6.8. **The blended figures in §8 are therefore conservative
for the engineering estates and the reader should know which way that bias runs.**

**On 6.8, keeping build and delivery separate.** The bridge is `WORKING` — it builds clean, its 42
verbs pass a product-level smoke test 44/45, and a live log shows it correctly refusing to write to a
model bound to a central. **This line prices what was built.** It does not claim the capability is
delivered: job instantiation does not work, the program runs on one workstation for one operator, and
`modules/12-standards-centralisation.md` records that the palette consuming the standards catalogue
is blocked five ways. `05-MASTER-AUDIT.md` and module 13 remain the authorities on delivery state.
**These hours are no longer costed from artefact inspection alone** — module 13 has now reported, and
§11 is updated accordingly.

**Overlap, declared — and re-checked after module 12's revision.** The 73 commands in 6.11 and 6.12
**are** netted out of Block 3: it holds 72 Revit commands, not 145. So the sheet-composition suite is
counted **once**, in 6.11, and `modules/12-standards-centralisation.md`'s revised finding —
**8 of 11 Sheets commands `WORKING`**, 3 `PARTIAL`, with `PlaceViewsOnSheetCommand` measuring true
viewport footprints against a title-block-inset usable region and never overlapping existing content,
`GridPacker` unit-tested, and `Renumber Views` implemented as a three-pass park/assign/restore so no
viewport is ever stranded on a temp number — **describes the engineering already inside the 1,793
lines that 6.11 prices. It is not counted a second time.**

*Not an overlap, though it looks like one:* the KOR.Drafter bridge exposes `newsheet`, `placeview`,
`duplicateview`, `copyview`, `moveviewport` and `arrange` verbs, and the RevitTools ribbon has its own
sheet commands. **These are two separate implementations in two separate repositories** — 3,489 lines
in the bridge (6.8a) against 1,793 in the ribbon (6.11) — both independently built and verified.

The remaining overlaps are small and left in as a deliberate conservatism: the Revit palette (6.1) is
one of the 145 catalog commands; the 13 ingestion providers (6.5) sit alongside the 40 jobs in Block
3; and the CSI exporter's three live-API buttons (6.6) are among the 403 actions in Block 1. That
residue is a few dozen units out of ~590.

#### Block 7 — remaining test suite

1,297 test methods total; the 392 belonging to the DXF→ETABS suite are inside Block 6.2. This block
covers the remaining ~905.

| | low | central | high |
|---|---|---|---|
| **Block 7** | **600** | **1,200** | **2,000** |

#### Subtotal, Blocks 1–7

| block | low | central | high |
|---|---|---|---|
| 1 — user actions (403) | 4,152 | 7,118 | 11,906 |
| 2 — view shells (122) | 1,052 | 2,104 | 3,400 |
| 3 — non-UI entry points (179, after the two Revit suites are carved out) | 1,583 | 3,243 | 5,924 |
| 3b — Inspections Bookings | 900 | 1,800 | 3,000 |
| 4 — integrations (10) | 1,740 | 3,500 | 6,100 |
| 5 — data estate | 800 | 1,600 | 2,800 |
| 6 — thirteen named estates | 9,750 | 18,910 | 32,380 |
| 7 — remaining tests | 600 | 1,200 | 2,000 |
| **subtotal** | **20,577** | **39,475** | **67,510** |

#### Block 8 — cross-cutting

Architecture, dependency injection, security, packaging, deployment, documentation and project
management, as a percentage of the subtotal — standard agency practice:

| | rate | arithmetic | result |
|---|---|---|---|
| Low | 15% | 20,577 × 1.15 | **23,664** |
| Central | 20% | 39,475 × 1.20 | **47,370** |
| High | 25% | 67,510 × 1.25 | **84,388** |

### 5.5 Bottom-up total

| | hours |
|---|---|
| **LOW** | **≈ 23,700** |
| **CENTRAL** | **≈ 47,400** |
| **HIGH** | **≈ 84,400** |

For orientation: the central case is **25.2 person-years** at 1,880 hours — a seven-person agency
team for just under four years.

---

## 6. Reconciliation — two independent checks

The bottom-up census is checked against **lines of code** and, new in this revision, against a
**sourced function-point delivery rate**. Neither was tuned to the other.

### 6.1 Against lines of code

**464,388 hand-written lines** (§1.1), measured separately from the feature census, and after
excluding ~729,000 lines of staging copies, decompiled recovery and published output.

| case | hours | 464,388 ÷ hours | verdict |
|---|---|---|---|
| **LOW** | 23,664 | **19.6 LOC/hour** | At the extreme top of any plausible band. Reachable only with heavy generation. |
| **CENTRAL** | 47,370 | **9.8 LOC/hour** | **Squarely inside the professional band.** |
| **HIGH** | 84,388 | **5.5 LOC/hour** | Conservative-professional; close to the COCOMO-adjusted 5.9. |

**Anchors for what a professional LOC/hour rate looks like** — all INFERRED from figures elsewhere
in this report, because no verifiable delivery-rate benchmark in LOC terms exists:

- COCOMO II, most favourable verified configuration: 350,925 ÷ 166,300 h = **2.1 LOC/hour**
- COCOMO II with illustrative (unverified) multipliers: 350,925 ÷ 59,900 h = **5.9 LOC/hour**
- The previous report's module bottom-up: **7.8 LOC/hour** on its own basis

**The reconciliation is materially better than in the previous revision**, and — worth noting — the
ratios barely moved when the estate grew. Adding ETABS plugins, DemoStudio and the two Revit suites
raised the central by 5,540 hours *and* the LOC basis by 59,069 lines, and the implied rate moved
only from 9.7 to **9.8 LOC/hour**. **Two independent measures scaling together when new material is
added is the strongest internal consistency check in this report** — it means the per-unit bands and
the per-line rate are telling the same story about the same codebase.

### 6.2 Against the function-point delivery rate — the check that pushes up

**This is the tension the revision has to face honestly.** §4.3 gives two sourced rates on 6,500 FP:

| rate | source | implied hours | vs this report's central (47,370) |
|---|---|---|---|
| 10 h/FP | IFPUG uTip #03 illustrative | **65,000** | **1.37× higher** |
| 16.5 h/FP | Capers Jones, US average circa 2025 | **107,250** | **2.26× higher** |

**Inverted: this report's central case implies 7.3 hours per function point** (47,370 ÷ 6,500) —
**2.2× more productive than Capers Jones' stated US average.** That is a large claim and it should
not pass without a defence. *(Note that this ratio is essentially unchanged from the pre-additions
draft — the new estates raised hours and function points together.)*

**The defence, and its limits:**

1. **Scope is not like-for-like, and this is the biggest single factor.** §1.2 explicitly excludes
   client requirements-gathering, procurement, external QA, formal security review and a support
   contract. Jones' US average is a whole-lifecycle figure covering an industry in which those
   activities are standard and often dominant. **This is a real and sufficient explanation for a
   large part of the gap — but it is an argument, not a measurement, because neither source
   publishes a scope-adjusted rate.**
2. **Language level.** QSM's 2,192-project dataset puts C# at 54 SLOC/FP against C's 97 (§3.5c).
   A US average spanning older and lower-level codebases will carry a worse hours-per-FP rate than a
   pure modern C# project.
3. **Package reuse** (§3.5d) — argued, not evidenced.
4. **What it is *not*.** It is **not** an AI-productivity claim. §7 shows the published evidence does
   not support a large AI multiplier, and this report applies none.

**What this does to the range.** The FP check is a large part of why the high case rose 41%. The
bottom-up high of **84,400 hours sits between the two function-point readings** — above IFPUG's
illustrative 65,000 and below Jones' 107,250. That is a genuine triangulation and it is why the high
case is quoted at 84,400 rather than at the bottom-up subtotal.

**And the outer bound, stated so nobody has to derive it:** if a reader rejects the scope argument
entirely and applies Jones' 16.5 h/FP at onshore agency rates, the answer is **107,250 h × $200 =
$21.5M**. That is not this report's answer, but it is what the most conservative sourced reading
supports, and it is quoted here rather than hidden.

### 6.3 The checks disagree, and that is the useful part

| check | direction | what it says |
|---|---|---|
| **Object points** (§4.4), on the UI layer only | **down** | Blocks 1+2 (9,222 h) are 1.8×–3.4× what object points imply for 122 views and ~40 reports |
| **Measured code volume per Revit command** (§5.4) | **down** | 59 lines per command; a flat per-command rate would have over-priced the ribbon by roughly half |
| **Lines of code** (§6.1) | **neutral** | Central at 9.8 LOC/hour sits in the middle of the professional band |
| **Function points** (§6.2) | **up** | Central implies 7.3 h/FP against a sourced US average of 16.5 |

**The estimate is generous where it counts screens and conservative where it counts the whole
system.** That is exactly what §1.3's structural caveat predicts: the census over-weights the visible
UI surface, which is easy to enumerate, and under-weights the services, engines and format writers
behind it, which are not. The LOC check sits between the two and is the reason the central case is
left where it is rather than pulled toward either extreme.

**Where the bottom-up is structurally biased low.** Behind the 403 actions and 179 non-UI entry
points sit services, models, repositories and renderers that no door count reaches —
`Kor.Opportunities.Data` alone is 48,832 lines across 233 files, and at 10 LOC/hour that one project
is ~4,900 hours, more than Blocks 3 and 4 combined. **The feature census cannot see it. Both
reconciliations can. That asymmetry is why the upper half of the range is trusted more than the
lower half.**

**And why the low case survives only with a caveat.** 19.6 LOC/hour is not a rate at which humans
write, review, test and integrate code. The low case is **a floor, not a quotable agency price** —
reachable only if a large fraction of the artefact is generated rather than authored. The actual
build demonstrates that is possible: it ran at roughly **341 lines per hour** (§8.5), which no one
types.

---

## 7. AI-assisted development — what the evidence actually says

**Unchanged from the previous revision.** This section exists because the obvious objection to any
build-cost estimate here is *"but he used AI, so divide by ten."* **The published evidence does not
support that, and the single most rigorous study points the other way.**

### 7.1 The independent RCT found a slowdown

**VERIFIED.** METR, *"Measuring the Impact of Early-2025 AI on Experienced Open-Source Developer
Productivity"*, **2025-07-10** —
[metr.org/blog/2025-07-10-early-2025-ai-experienced-os-dev-study](https://metr.org/blog/2025-07-10-early-2025-ai-experienced-os-dev-study/)

| | |
|---|---|
| Design | **Randomised controlled trial** — issues randomly assigned "AI allowed" / "AI disallowed" |
| Sample | 16 experienced open-source developers, 246 real issues, mature repos (22,000+ stars, 1M+ LOC) |
| Result | Developers were **19% SLOWER** with AI |
| Perception | They expected a 24% speed-up beforehand, and still believed they had been 20% faster afterwards — **a ~39-point gap between belief and measurement** |

METR is an AI-safety nonprofit with no coding tool to sell. Its own caveat: it does "not claim that
our developers or repositories represent a majority or plurality of software development work."

### 7.2 The 2026 follow-up — and why it matters here specifically

**VERIFIED.** METR, *"We are Changing our Developer Productivity Experiment Design"*, **2026-02-24** —
[metr.org/blog/2026-02-24-uplift-update](https://metr.org/blog/2026-02-24-uplift-update/)

With late-2025 agentic tools, 57 developers, 800+ tasks, 143 repos:

| cohort | measured effect | 95% CI |
|---|---|---|
| Original cohort (10 devs, their own mature repos) | **−18%** (still a slowdown) | −38% to +9% |
| New recruits (47 devs, pool including **"smaller, more greenfield, and less mature repositories"**) | **−4%** | −15% to +9% |

**This is the most directly relevant finding in the whole evidence base**, because the KOR build is
greenfield solo work, not maintenance on a mature unfamiliar repo. The greenfield-leaning cohort's
effect is close to neutral — but still *negative*, and its confidence interval still contains zero.
**There is no published measurement showing a large AI multiplier on greenfield solo work.** METR
also reports worsening selection bias: developers unwilling to work without AI increasingly declined
to participate at all.

### 7.3 The vendor-funded studies found gains

**VERIFIED.** GitHub + Microsoft Office of the Chief Economist, 2022-09-07, updated 2024-05-21 —
[github.blog — quantifying GitHub Copilot's impact](https://github.blog/news-insights/research/research-quantifying-github-copilots-impact-on-developer-productivity-and-happiness/)
- RCT, 95 professional developers, one synthetic task (write an HTTP server in JS)
- **55% faster** (1h11m vs 2h41m), P = .0017
- **The 95% confidence interval on the speed-up is [21%, 89%]** — enormous. And GitHub is studying
  its own product on a task resembling nothing in a 400,000-line line-of-business suite.

**VERIFIED.** Cui, Demirer, Jaffe, Musolff, Peng, Salz (Microsoft Research), *"The Effects of
Generative AI on High-Skilled Work"*, June 2025 —
[microsoft.com/en-us/research/publication/…](https://www.microsoft.com/en-us/research/publication/the-effects-of-generative-ai-on-high-skilled-work-evidence-from-three-field-experiments-with-software-developers/)
- Three pooled RCTs, **4,867 developers** at Microsoft, Accenture and one Fortune 100 firm
- **+26.08% tasks completed** (SE 10.3%)
- Authors' own words: *"each experiment is noisy"* — significance emerges only on pooling
- Microsoft sells the product and employs the lead authors

**VERIFIED.** Paradis et al. (Google), arXiv 2410.12944, 2024-10-16 —
[arxiv.org/abs/2410.12944](https://arxiv.org/abs/2410.12944)
- RCT, 96 Google engineers — **~21% reduction in task time**, "confidence interval is large"

**The pattern is hard to miss: every study run by a company that sells an AI coding tool found a
gain; the one run by an organisation with nothing to sell found a loss.** That does not make the
vendor studies wrong. It does mean a cost model should not lean on them.

### 7.4 Organisation-level outcomes are worse than task-level ones

**VERIFIED.** Google Cloud / DORA, *State of DevOps* 2024 and 2025 (2025 edition 2025-09-23;
**no 2026 edition exists yet**, confirmed live 2026-08-20):

| year | AI adoption | throughput | delivery stability |
|---|---|---|---|
| 2024 | 75% use AI daily | **−1.5%** per 25% increase in AI adoption | **−7.2%** per 25% increase |
| 2025 | 90% use AI | **positive** (a reversal) | **still negative** |

DORA 2025's thesis: *"AI doesn't fix a team; it amplifies what's already there"*, and *"AI
accelerates software development, but that acceleration can expose weaknesses downstream."*

**This is the single most relevant external finding for this audit.** `05-MASTER-AUDIT.md` found
sixteen instances of the system reporting success it had not earned, credentials in the wrong place,
a module serving wrong numbers from 34-day-old binaries, and documentation that was not true — while
the code itself was largely sound. That is *precisely* the DORA pattern: throughput up, stability
down, weaknesses exposed downstream.

**And this revision supplies a fresh instance of it.** The audit itself — the governance artefact —
undercounted the scheduled-job surface by a factor of five, misread the transmittal purpose column,
missed a shipped annotation workflow, under-described a five-format exporter as a single tool, and
never costed an entire drafting program. Throughput up, verification down. **The Virtual Drafter is
the sharpest example in the estate: the most engineering-complete thing built in the last thirty
days, and the least connected to anything a user can reach.**

### 7.5 Code quality and rework

**REPORTED.** GitClear, **211 million changed lines** across 2020–2024 —
[gitclear.com](https://www.gitclear.com/coding_on_copilot_data_shows_ais_downward_pressure_on_code_quality)
- Copy-pasted / cloned code: **8.3% of changes (2021) → 12.3% (2024)**
- Refactored code: **25% of changed lines (2021) → under 10% (2024)**
- Observational, not an RCT. GitClear sells code-quality tooling.

This bears directly on §6.1: **if AI-assisted code is more duplicated and less refactored, LOC
over-states delivered functionality more than usual**, and the LOC reconciliation is biased toward
larger hour counts for that reason on top of all the others.

### 7.6 Self-report is not evidence

**VERIFIED.** JetBrains *State of the Developer Ecosystem 2025*, N = 24,534 —
[devecosystem-2025.jetbrains.com](https://devecosystem-2025.jetbrains.com/)
- 73% use at least one AI coding assistant; 71% report saving >1 hr/week; 41% report saving 8+ hrs/week

**All self-report, and METR measured self-report in this exact domain to be wrong by ~39 points in
the optimistic direction.** Recorded for completeness; carries no weight in the cost model.

*Not verified, and deliberately not cited as fact:* Stack Overflow has **no 2026 developer survey**
(confirmed live at [survey.stackoverflow.co](https://survey.stackoverflow.co/), 2026-08-20).
Uplevel's "41% higher bug rate" **could not be verified**. BlueOptima, McKinsey and Bain **could not
be reached**.

### 7.7 What this does to the estimate

**INFERRED, and this is a judgement, not a measurement:**

The honest reading is that **the published multiplier for AI-assisted development on greenfield solo
work sits somewhere between about 1.0× and 2×, and no credible study supports more.** The largest
independent measurement is negative. The largest vendor measurement is +26% on task counts. The
best-matched cohort — greenfield-leaning, METR 2026 — is −4%.

**So this report applies no AI productivity divisor to the professional build cost.** A firm quoting
that work in 2026 prices it with its own AI-assisted staff and its own realised productivity, which
the evidence says is not dramatically different from before.

Where AI plainly did matter is **not** captured by any of these studies: it let one non-specialist
cover WPF, ODBC, MCP, DXF geometry, the Revit API, five CSI file formats, COM automation, SQL and
PowerShell without hiring five people. That is a **breadth** effect, not a **speed** effect, and no
published research measures it. **The census in §1.1 is the best available evidence *of* that breadth
effect: eight Revit configurations, ten integration surfaces, five proprietary file formats and eight
named product estates, from one part-time person.**

---

## 8. The revised build-cost range

### 8.1 The cost grid — INFERRED, arithmetic shown

Effort (hours) × rate ($/hour), using the §2.6 rate bands:

| effort ↓ / rate → | offshore $25 | offshore $70 | nearshore $50 | nearshore $85 | E. Europe $105 | blend $120 | NA $150 | NA $200 | NA $255 |
|---|---|---|---|---|---|---|---|---|---|
| **LOW — 23,700 h** | $0.59M | $1.66M | $1.19M | $2.01M | $2.49M | $2.84M | $3.56M | $4.74M | $6.04M |
| **CENTRAL — 47,400 h** | $1.19M | $3.32M | $2.37M | $4.03M | $4.98M | **$5.69M** | $7.11M | $9.48M | $12.09M |
| **HIGH — 84,400 h** | $2.11M | $5.91M | $4.22M | $7.17M | $8.86M | $10.13M | $12.66M | **$16.88M** | $21.52M |

### 8.2 The answer

| | scenario | figure | reasoning |
|---|---|---|---|
| **LOW** | Offshore/nearshore team, bottom-up floor | **≈ $1.2M** | 23,700 hours at ~$50/hour. **Carries a caveat the other two do not:** §6.1 shows this implies 19.6 LOC/hour, which is not a hand-authoring rate. This prices a *delivery model* leaning heavily on generation and scaffolding — which is what actually happened — not a quotable agency labour estimate. |
| **CENTRAL** | Blended North American + nearshore agency, feature census | **≈ $5.7M** | 47,400 hours at a ~$120/hour blend. Reconciles at 9.8 LOC/hour, the middle of the professional band. **Conservative for the engineering estates**, which would price at the top of the onshore band rather than the blend (§5.4, rate note on 6.6). |
| **HIGH** | Onshore agency, function-point-informed | **≈ $16.9M** | 84,400 hours at $200/hour. Triangulated: sits between IFPUG's illustrative 10 h/FP (65,000 h) and Capers Jones' US average 16.5 h/FP (107,250 h), and above the COCOMO-adjusted 59,900 h. |

**Range: roughly $1.2M to $16.9M, central near $5.7M.**

**Outer bound, for completeness:** rejecting §6.2's scope argument entirely and applying Jones' US
average at onshore agency rates gives **$21.5M**. Not this report's answer; quoted so nobody has to
derive it.

### 8.3 What changed from $1.3M / $5.4M / $12.0M, and why

| | previous | revised | why |
|---|---|---|---|
| **LOW** | $1.3M | **$1.2M** | The old floor was an asserted 25,000 hours. The new floor is a measured census summed at its cheapest defensible band: 23,700 hours. |
| **CENTRAL** | $5.4M | **$5.7M** | The old 45,000 hours came from 11 chapter headings × 4,000. The new 47,400 comes from 403 actions + 122 views + 179 non-UI entry points + 10 integrations + **13 named estates** + 20% cross-cutting. **The two land within 5% of each other — the previous central was not wrong so much as unfounded.** |
| **HIGH** | $12.0M | **$16.9M** | **The one figure that moved materially.** Two causes, roughly equal: (a) the eleven estates added across this revision contribute ~21,100 hours at the high band; (b) the function-point route, abandoned as uncostable in the previous version, now has two sourced rates and both land above the old high case. |
| Buy path, 5-yr central | $0.59M | **$0.65M** | A standards-management line was added (AVAIL, $10,000/yr for 40 seats) that the previous version had no line for. |

**Which lines moved the central, precisely** — the brief asked for this explicitly:

| change | effect on central hours |
|---|---|
| Block 6.6 added — PDF → CSI exporter suite | **+2,600** |
| Block 6.10 added — DemoStudio (dormant) | **+2,500** |
| Block 6.7 added — two takeoff products | **+2,100** |
| Block 6.9 added — ETABS native plugins | **+1,700** |
| Block 6.8 added — Virtual Drafter, **as repriced** | **+1,680** |
| Block 6.11 added — Revit sheet-composition suite | **+750** |
| Block 6.12 added — Revit rebar detailing suite | **+640** |
| Block 6.13 added — decompilation-and-recovery exercise | **+450** |
| **Block 6.8 reduced** — no planner/rules engine, no job instantiation, conformance scorer de-duplicated against 6.1 (`modules/13-virtual-drafter.md`) | **−720** |
| **Block 3 reduced** — 145 Revit commands rebanded on measured code volume (59 lines each), and 73 of them carved out into 6.11/6.12 | **−823** |
| Block 6.2 reduced — DXF→ETABS re-scoped to the 7,270-line `Dxf/` engine once takeoff was separated out | **−1,100** |
| Block 8 recalculated on the new subtotal | **+1,895** |
| **Net vs the previous report's 45,000** | **+2,400** |

**Three of those lines are worth pausing on, because they run in opposite directions and all three
were required by honesty rather than by the brief:**

- **DemoStudio (+2,500) is dormant** — one commit in ninety days. It is counted because build cost
  is what was built, and it is labelled because a reader deciding what to maintain needs to know.
- **The Revit rebanding (−823) is the single largest downward correction in this revision.** A brief
  arriving with the words "137 tools" invites 137 × a round number. Measuring first showed 8,549
  lines across 145 commands — **59 lines each** — and that a 50-command "suite" shares 1,034 lines
  between its members. **Counting features is not the same as counting work.**
- **The Virtual Drafter (−720) came down once it was audited.** It is the most impressive thing in
  the estate and the easiest to over-price, because the artefacts read like an autonomous system.
  They are not one: the judgement is a general-purpose LLM at run time, and the software is a
  3,489-line bridge. **The most novel capability in the suite is also one of the cheaper lines in
  this table, and both of those things are true at once.**

**Said plainly, as the brief requires: the bottom-up rebuild landed within 7% of the old central
case.** Pricing a materially larger and better-measured surface did not produce a materially larger
central figure. What it produced was a number with a denominator — one that moves if the product
moves, and that any reader can re-derive from the repo. **The surface that was missed showed up in
the high case, not the central one**, because it is exactly the specialist engineering whose cost is
most uncertain.

### 8.4 The same question as a hiring decision

INFERRED, using §2.6 fully-loaded rates and a 1,880-hour work year:

| effort | person-years | US team | BC team (USD) |
|---|---|---|---|
| LOW — 23,700 h | 12.6 | **$3.79M** | **$2.53M** |
| CENTRAL — 47,400 h | 25.2 | **$7.58M** | **$5.06M** |
| HIGH — 84,400 h | 44.9 | **$13.49M** | **$9.01M** |

**A BC-based team costs about a third less than a US one** at these verified wage levels — a real
argument for where this kind of work should sit in a firm with offices in both.

### 8.5 What it actually cost — and the caveat that matters most

**INFERRED, and this is the number that should make the reader uncomfortable.**

Eight calendar months, one person, part-time against a full IT and operations role. Even at a
generous full-time-equivalent of 8 × 170 = **1,360 hours**, the artefact holds 464,388 hand-written
lines — **341 lines per hour**, of which 258 are C#.

**No human writes 258 lines of C# an hour.** That is not a productivity claim; it is proof that a
large share of the artefact is framework scaffolding, designer-generated markup, repetitive
data-access code and AI-generated blocks. It is the strongest single piece of evidence that
**LOC-based costing over-states this system** — and it is why §6.3 rejects the low case as a labour
estimate while accepting it as a delivery-model price.

*(The 1,360-hour denominator is also generous in the other direction: some of the estate — DemoStudio
in particular, dormant since March — predates the eight-month window. The true elapsed effort is
unknown, and §1.3 names it as a structural uncertainty rather than pretending otherwise.)*

**The defensible internal cost is the developer's own loaded time** — on the order of
**$150,000–$250,000** of salary-equivalent for the fraction of eight months spent on it, plus AI API
and tooling spend.

**Against a central buy-equivalent of ~$5.7M, that is the headline. It should never be quoted
without the caveat that the two are not like-for-like:** a vendor price includes warranty, formal
QA, documentation, security review and a support contract, and `05-MASTER-AUDIT.md` demonstrates in
detail that this system has none of those. **Some of the 23–38× cost advantage is genuine leverage.
Some of it is unpaid technical debt that the audit has now itemised** — and §5.4's Block 6.8 note is
the sharpest single case: complete engineering, absent delivery.

---

## 9. The buy-instead path — 5-year TCO for 40 users

**Vendor pricing carried forward unchanged. One new capability is added: standards management
(§9.7).**

### 9.1 What is priceable and what is not

**Half of this market does not publish prices.** Newforma, Deltek (every SKU), OpenAsset, Unanet and
CSI are all quote-only. That is a finding, not a gap in the research: each vendor's own current page
was fetched and none carries a number.

| vendor | published list price? | what was checked |
|---|---|---|
| **Egnyte** | **Yes** | [egnyte.com/pricing](https://www.egnyte.com/pricing) |
| **Bluebeam** | **Yes** | [bluebeam.com/pricing](https://www.bluebeam.com/pricing) |
| **Microsoft** (Power BI, M365 Copilot, Copilot Studio, Fabric) | **Yes** | vendor pricing pages + Azure Retail Prices API |
| **AVAIL** (content management) | **Yes** | [getavail.com/pricing](https://getavail.com/pricing/) — new in this revision |
| **Newforma** Project Center | **No — quote-only** | [Project Center packages](https://www.newforma.com/newforma-project-center/packages/) |
| **Newforma** Konekt | **No — quote-only** | [Konekt packages](https://www.newforma.com/newforma-konekt/packages/) |
| **Deltek** Vantagepoint | **No — quote-only** | [deltek.com/products/erp/vantagepoint](https://www.deltek.com/products/erp/vantagepoint/) |
| **Deltek PIM** | **No — quote-only** | [deltek.com PIM](https://www.deltek.com/products/delivery-assurance/project-information-management/) |
| **Deltek Vantagepoint Intelligence** | **No — could not confirm it is still a distinct SKU** | site navigation crawl found no product page |
| **Deltek Proposals / Proposal AI** | **No — the name may have moved** | nav slot resolves to **ProPricer**. Flagged, not confirmed. |
| **OpenAsset** | **No — quote-only** | [openasset.com/pricing](https://www.openasset.com/pricing) |
| **Unanet** CRM / ProposalAI | **No — quote-only** | `unanet.com/pricing`, `/crm/pricing`, `/crm` all 404 |
| **UNIFI Labs** (content management) | **No — quote-only** | [unifilabs.com](https://unifilabs.com/) fetched 2026-08-21; `/pricing/` 404s — new in this revision |
| **CSI ETABS** | **No — dealer/quote-only** | [compare-levels](https://www.csiamerica.com/products/etabs/compare-levels) — three tiers, no figures |

GSA eLibrary was checked for a US federal schedule price list: **DELTEK, INC.** appears as a Multiple
Award Schedule manufacturer under SINs 54151 / 511210 / 611420, but **no price list is exposed**
([gsaelibrary.gsa.gov](https://www.gsaelibrary.gsa.gov/ElibMain/searchResults.do?searchText=Deltek&searchType=allWords),
2026-08-20).

### 9.2 Tier A — published list prices, all VERIFIED

| line | product | list price | source | fetched |
|---|---|---|---|---|
| A1 | Egnyte **Elite** | $48 / user / month, billed annually (150 GB) | [egnyte.com/pricing](https://www.egnyte.com/pricing) | 2026-08-20 |
| A2 | Egnyte AEC add-on — **Project Hub** | $6 / user / month | same | 2026-08-20 |
| A3 | Egnyte AEC add-on — **Specialized File Handler** | $6 / user / month | same | 2026-08-20 |
| A4 | Bluebeam **Complete** | $440 / user / year | [bluebeam.com/pricing](https://www.bluebeam.com/pricing) | 2026-08-20 |
| A5 | Power BI **Pro** | $14 / user / month, paid yearly | [Power BI pricing](https://www.microsoft.com/en-us/power-platform/products/power-bi/pricing) | 2026-08-20 |
| A6 | Microsoft **Fabric F2**, 1-year reservation | $938 per CU / year × 2 CU | [Azure Retail Prices API](https://prices.azure.com/api/retail/prices?currencyCode=USD&$filter=serviceName%20eq%20%27Microsoft%20Fabric%27%20and%20armRegionName%20eq%20%27westus2%27) | 2026-08-20 |
| A7 | **Microsoft 365 Copilot** | $30 / user / month, paid yearly | [copilot-for-microsoft-365](https://www.microsoft.com/en-us/microsoft-365/enterprise/copilot-for-microsoft-365) | 2026-08-20 |
| A8 | **Copilot Studio** | $200 / pack / month (25,000 Copilot Credits) | [microsoft-copilot-studio](https://www.microsoft.com/en-us/microsoft-copilot/microsoft-copilot-studio) | 2026-08-20 |
| **A9** | **AVAIL** (standards/content management) | **$250 / user / year** | [getavail.com/pricing](https://getavail.com/pricing/) | **2026-08-21** |

Other verified prices not carried into the central case: Egnyte **Business** $22 and **Enterprise
Lite** $39 per user/month; Egnyte **Team** $10 (**capped at 1–10 users**); Egnyte **Ultimate**
quote-only; Bluebeam **Basics** $260 / **Core** $330 / **Max** $590 per user/year; Power BI
**Premium Per User** $24/user/month; **M365 Copilot Business** $18/user/month (promotional, was
$21.00, promo ends **2026-09-30**); Fabric PAYG $0.18/CU-hour and 3-year reservation $2,814/CU;
AVAIL **Free** $0 (single user) and **AVAIL Enterprise** "$250 for 3-year term, minimum 50 users".

**On the AVAIL Enterprise tier:** as published, *"$250 for 3-year term"* with a 50-user minimum is
ambiguous — read per-user it is **cheaper** than the single-user tier by a factor of three. **Rather
than guess, this report prices AVAIL at the unambiguous $250/user/year.** If the Enterprise reading
is correct, the standards line falls from $10,000/yr to roughly $4,200/yr and the buy-path central
drops by about $32,000 over five years. Flagged, not resolved.

**Correction to `C2-newforma.md`** (carried forward): it records Egnyte tiers as "$10 / $22 / $39 /
$48" with "AEC Elite / AEC Ultimate" tiers. Re-fetched, **there is no "AEC Elite" or "AEC Ultimate"
tier** — Egnyte has a generic **Ultimate** (quote-only) plus two separately priced **AEC add-ons** at
$6/user/month each, and Team is capped at 10 users.

### 9.3 Tier A arithmetic — INFERRED, 40 users, per year

**Central case:**

| line | arithmetic | annual USD |
|---|---|---|
| Egnyte Elite | $48 × 40 × 12 | **$23,040** |
| Egnyte Project Hub | $6 × 40 × 12 | **$2,880** |
| Egnyte Specialized File Handler | $6 × 40 × 12 | **$2,880** |
| Bluebeam Complete | $440 × 40 | **$17,600** |
| Power BI Pro | $14 × 40 × 12 | **$6,720** |
| Fabric F2 reserved | $938 × 2 | **$1,876** |
| M365 Copilot | $30 × 40 × 12 | **$14,400** |
| Copilot Studio | $200 × 12 | **$2,400** |
| **AVAIL** | $250 × 40 | **$10,000** |
| **Tier A central total** | | **$81,796 / year** |

**Low case** (Egnyte Business + add-ons; Bluebeam Core; Power BI Pro only; Copilot Business; no
Fabric, no Copilot Studio; AVAIL):

- Egnyte: ($22 + $6 + $6) × 40 × 12 = $34 × 480 = **$16,320**
- Bluebeam Core: $330 × 40 = **$13,200**
- Power BI Pro **$6,720** · Copilot Business $18 × 40 × 12 = **$8,640** · AVAIL **$10,000**
- **Tier A low total = $54,880 / year**

**High case** (Egnyte Elite + add-ons; Bluebeam Max; full Microsoft with Fabric F4; AVAIL):

- Egnyte **$28,800** · Bluebeam Max $590 × 40 = **$23,600**
- Power BI Pro $6,720 + Fabric F4 reserved ($938 × 4 = $3,752) + Copilot $14,400 + Copilot Studio $2,400 = **$27,272**
- AVAIL **$10,000**
- **Tier A high total = $89,672 / year**

### 9.4 Tier A — 5-year totals (INFERRED)

At 0% escalation, ×5. At 5%/year the multiplier is (1.05⁵ − 1) ÷ 0.05 = **5.5256**.

| case | annual | **5-yr @ 0%** | **5-yr @ 5%/yr** |
|---|---|---|---|
| Low | $54,880 | **$274,400** | **$303,245** |
| **Central** | **$81,796** | **$408,980** | **$451,972** |
| High | $89,672 | **$448,360** | **$495,513** |

### 9.5 Tier B — quote-only lanes, no vendor price exists

| lane | product | best available figure | source |
|---|---|---|---|
| Email filing + transmittals | **Newforma Project Center** | ITQlick's own "(Estimated)" band **$50–$150/user/month** → at 40 seats, **$24,000–$72,000/yr**. ITQlick separately estimates ~$10,000 year-1 / ~$50,000 five-year **for 10 users**, which is not reconcilable with its own per-seat band. | [itqlick.com](https://www.itqlick.com/newforma-project-center) — REPORTED, self-labelled estimate |
| ERP analytics + PIM + proposals | **Deltek** | Vendr: median contract **$20,380/yr** across 33 purchases, range **$9,248–$63,900**; Vantagepoint at 30–75 users **$60,000–$150,000/yr** plus **$40,000–$120,000** one-time implementation | [vendr.com/marketplace/deltek](https://vendr.com/marketplace/deltek) — REPORTED |
| Digital asset management | **OpenAsset** | **No figure of any kind found** | quote-only |
| BD / CRM / proposals | **Unanet CRM + ProposalAI** | **No figure of any kind found** | quote-only |
| Revit content management | **UNIFI Labs** | **No figure of any kind found** | quote-only — `/pricing/` 404s |

**Three of the five Tier B lanes cannot be priced at all.** The buy-path total is a **lower bound**
for the BD lane specifically.

**Note on double-counting.** Newforma and Egnyte occupy the same lane; a firm buys one, not both.
KOR already licenses Vantagepoint core, so only the *incremental* add-on cost is relevant, and that
increment has no published price.

### 9.6 The five-year buy-path total

| scenario | what it includes | 5-year total, USD |
|---|---|---|
| **Floor** | Tier A low only, 0% escalation. Published list prices, nothing quote-only. | **$274,000** |
| **Central** | Tier A central @ 5% escalation ($451,972) + Deltek add-on increment at a REPORTED mid of $35,000/yr (× 5.5256 = $193,400) | **~$645,000** |
| **High** | Tier A high @ 5% ($495,513) + Deltek increment at the REPORTED top of $60,000/yr (× 5.5256 = $331,500) + one-time implementation $120,000 | **~$947,000** |

**Rounded and stated honestly: buying the nearest commercial equivalent for 40 users runs roughly
$0.27M to $0.95M over five years, with a central case near $0.65M** — and that central case still
excludes OpenAsset and Unanet entirely because no price for them exists, and excludes **both** the
DXF→ETABS capability and the Virtual Drafter because nothing sells either.

In CAD at **1.3785**: floor **~C$378,000**, central **~C$889,000**, high **~C$1.31M**. (INFERRED.)

### 9.7 Standards management — is there a commercial product?

**The brief asks: is there a commercial product for a governed detail library with a Revit palette?
The answer is partly.**

**What you can buy — VERIFIED.** **AVAIL** ([getavail.com/pricing](https://getavail.com/pricing/),
fetched 2026-08-21) publishes list prices and ships a Revit add-in:

| tier | price |
|---|---|
| Free | $0 (single user) |
| **AVAIL** | **$25/month or $250/year** (single user) |
| AVAIL Enterprise | "$250 for 3-year term", minimum 50 users — ambiguous, see §9.2 |

AVAIL's own homepage (fetched 2026-08-21) describes a content management system indexing
**"RVT, DWG, PNG, PDF"** from OneDrive, BIM360, Egnyte and ACC, with **"Palettes"**, a Revit
**"Project Navigator"** and **"Harvest"** tool, drag-and-drop into Revit/AutoCAD/Bluebeam,
**"Revit Application Version Management"**, and block-library management.

**UNIFI Labs** ([unifilabs.com](https://unifilabs.com/), fetched 2026-08-21) is the same category and
is **quote-only**. Its own description: a *"web-based, digital asset management system which
leverages BIM data"* that can *"drill down into the RFA file to view family types and parameter
data"*, with *"built-in change management"*, an *"end-to-end content request mechanism"*,
*"Enterprise-Level Permissions Control"*, and add-ins for Revit, AutoCAD, Civil 3D, Bentley and Rhino.

**So the content-browser half of the standards estate is purchasable, and one vendor publishes a
price.** That is a real correction to the previous revision, which had no line for this capability at
all, and it is why the buy-path central rose from $0.59M to $0.65M.

**What you cannot buy — VERIFIED negative from the vendors' own pages.** Neither references any of
the following, which are the parts that make KOR's estate a governance system rather than a file
browser:

| capability | AVAIL | UNIFI |
|---|---|---|
| Minting **immutable canonical identifiers** (`KOR-D-00001`…`KOR-D-00612`) | not mentioned | not mentioned |
| A **numbered detail register** distinct from a family/content library | not mentioned | not mentioned |
| **Approval / publication** workflow with a verified/unverified state per item | not mentioned | partial — "change management", "content request mechanism", permissions |
| **Watermarking** of unapproved content | not mentioned | not mentioned |
| **Conformance scoring** of live models against the canon | not mentioned | not mentioned |

**Conclusion.** A firm buying its way to KOR's standards capability would license AVAIL (or UNIFI) at
roughly **$10,000/year for 40 seats**, get the palette and the browsing, and then still have to build
the register, the numbering, the governance workflow and the conformance engine — **Blocks 6.1 +
6.1b: 1,200 / 2,400 / 4,200 hours** on top of the licence. **The buy path substitutes for the shelf,
not for the standard.**

---

## 10. What has no commercial equivalent to price

**There are now two such capabilities, not one.**

### 10.1 DXF → ETABS — nothing sells this

**A sourced negative finding, arrived at independently twice**: by the competitive scan in
`competitive/C4-field.md` (2026-08-20, across three search engines and every structural vendor), and
again by direct fetch of each vendor's own current pages.

**CSI's own documentation is the decisive evidence.** ETABS' feature page describes its DXF/DWG
import verbatim as:

> *"Easily import an architectural DXF/DWG into the background of the ETABS modeling window and use
> it as a template to trace over."*

(VERIFIED — [csiamerica.com/products/etabs/features](https://www.csiamerica.com/products/etabs/features),
fetched 2026-08-20)

**A background template to trace over by hand is not a model generator.** Nowhere on CSI's site is
there any claim of automatic wall, opening, pier-label or object creation from a drawing.

Checked and found not to do it, each fetched directly on 2026-08-20:

| category | products checked | result |
|---|---|---|
| Structural analysis / design | CSI ETABS, Graitec Advance Design, IDEA StatiCa, SkyCiv, Tekla Structural Designer, Dlubal RFEM, Autodesk Robot | BIM interchange only — all require an already-structured model. Robot **could not be verified** (autodesk.com 403/503). |
| AI / BIM startups | Snaptrude, Kreo, Qbiq, Arcol, Augmenta, Verifi3D | Massing, takeoff, space planning, MEP routing, model QA. **None produces a structural analysis model.** |
| Startups that no longer exist | **Motif** (domain parked), **Swapp** (parked on Sedo) | — |

Not reached: Higharc, Parametrix. `C4-field.md` additionally excluded Speckle, Karamba3D, RISA,
Togal, Skema, and Thornton Tomasetti's Asterisk (which starts from a **massing model**, not issued
drawings).

**Conclusion (VERIFIED negative, corroborated twice).** The only structural tool in this space with a
published price is **IDEA StatiCa** — a connection and member design tool, not a building-model
generator: **€1,750–€4,990/yr** or **€3,490–€9,990 perpetual** per floating seat (VERIFIED —
[ideastatica.com/pricing](https://www.ideastatica.com/pricing), 2026-08-20).

**Priced here at 900 / 1,700 / 2,800 hours** (Block 6.2), plus **1,400 / 2,600 / 4,400** for the
five-format exporter suite it feeds (Block 6.6), plus **900 / 1,700 / 2,900** for the native ETABS
plugins (Block 6.9). **At the central blend that is ~$720,000 of build cost with no purchasable
substitute at any price** — and §5.4's rate note argues the true figure is higher, because this is
specialist rather than blended labour. At the specialist $200/hour rather than the $120 blend, the
same three lines are **~$1.2M**.

### 10.2 Automated Revit drafting from a governed catalogue — no product found

**New in this revision.** The brief asks whether any commercial product does automated Revit drafting
from a governed detail catalogue, with head-to-head-against-a-human benchmarking.

**No such product was found. This is tagged `could not verify exhaustively` rather than as a hard
verified negative**, because the shared web-search budget was exhausted before this revision began
and no fresh vendor sweep was possible. What *can* be said, from evidence gathered:

- **The two content-management vendors fetched today place content; they do not draft it.** AVAIL's
  and UNIFI's own pages describe browsing, tagging, permissions and drag-and-drop insertion. Neither
  describes reading a markup, deciding what a detail should be, and producing sheets.
- **The Revit add-in market that KOR's own research names as the monetising competition — pyRevit,
  DiRoots, Ideate, Naviate, CTC** (per `KOR.RevitTools/docs/BUILD-STATUS.md`, which surveyed exactly
  this field) — sells **command toolkits**: batch operations a human invokes. None is a drafting
  agent driven by a markup. **KOR's own ribbon (Blocks 6.11, 6.12 and Block 3) is in that competitive
  category and is priced accordingly** — it is the Virtual Drafter, not the ribbon, that has no
  analogue. *(Note that KOR does not have job-instantiation-from-template either — `modules/13`
  establishes it does not exist, and it is not priced.)*
- **No vendor in either the `C4-field.md` sweep or today's fetches publishes a head-to-head benchmark
  against a human drafter on a real job at all.** KOR's `exam/31202-01/` is a form of evidence this
  market does not appear to produce — and `modules/13-virtual-drafter.md` verified the closed-book
  condition **forensically**, against the 8.5 MB session transcript: zero references to the answer
  key, zero to the production drafter, 55 to the pre-revision snapshot, and the post-revision model
  never opened. **State the result as "12 of 12 marked locations implemented and verified; of three
  contested points the engineer sided with the machine once and with the human twice"** — the
  scorecard's "2 locations more faithful than production" was overturned by
  `standards/RULINGS.md:51-53` on 2026-07-31 and must not be repeated.

**But note what this capability actually is, because it changes what "no commercial equivalent" means
here.** It is not that KOR built software nobody sells. **It is that KOR wrote a 3,489-line bridge
and a 9.2 KB prompt, and pointed a general-purpose LLM at Revit.** The moat is the corpus, the
protocol and the apprenticeship — not an algorithm. That is a genuinely defensible position and a
much cheaper one to have reached, and it is **why this line is priced at 860 / 1,680 / 2,870 hours
(Block 6.8) rather than at what an autonomous drafting engine would cost.** Anyone else with the
same idea faces the same low software bar and the same high corpus bar.

**Priced here at 860 / 1,680 / 2,870 hours** (Block 6.8), **on the explicit understanding that this
prices what was built, not what is delivered** (§0, §5.4).

### 10.3 The other unpriceable things

1. **The standards *governance* layer** — §9.7. The shelf is purchasable; the numbering, approval,
   watermarking and conformance scoring are not.

2. **BD Brain's full combination** — tender ingestion across 111 sources, cross-source entity
   resolution, agentic research, dossier generation and pursuit lifecycle in one system. Per
   `C4-field.md`: the AEC vendors (OpenAsset, Unanet CRM) do none of the first four, and the GovCon
   platforms that do ingestion and lifecycle (GovDash, Sweetspot, Procurement Sciences, pWin.ai) are
   shaped around US federal data and **none publishes entity resolution**. KOR's sources — Bonfire,
   BidsAndTenders, MERX, CivicInfo, BC Bid — are in nobody's product.

3. **A numbered transmittal register with per-recipient download attribution, over a SharePoint
   tenant you already own** — and, with §0's finding, **with per-bookmark annotation on issue**.
   Egnyte's Audit Trail tracks *"every external access and download event"* but has no numbered
   transmittal record, and adopting it means **migrating off SharePoint**. Newforma Info Exchange has
   per-recipient tracking but is stranded on the on-premises product and is quote-only. **No vendor
   page found in either scan describes per-bookmark commenting on an issued PDF.**

4. **Integration across four domains on one data estate.** Every product in the buy-path table is a
   point solution. **The buy path assembles eight vendors; it does not assemble one system.** The
   integration work — making a transmittal joinable to a BD pursuit and a project's financials — is
   not purchasable at any price. Block 4 prices it at **1,740 / 3,500 / 6,100 hours**.

### 10.4 What that does to the comparison

**The buy path does not reach feature parity, at any spend.** It is the closest assemblable
substitute, missing `drawings in, analysis model out`, the five-format CSI writer suite, automated
drafting, the standards governance layer, the entity-resolution BD layer, and all cross-domain
integration.

Summing just the unpriceable blocks from §5.4 — Block 4 (integrations), 6.1+6.1b (standards
governance), 6.2 (DXF→ETABS), 6.5 (ingestion), 6.6 (CSI exporters), 6.8 (Virtual Drafter) and 6.9
(ETABS plugins) — gives **7,900 / 15,380 / 26,470 hours**, or **~$1.85M at the central blend**, before
any of the rest of the suite. **That is the real floor on "buy plus fill the gaps," and it is nearly
three times the entire five-year licence cost.**

---

## 11. The figures in this report I trust least

Refreshed for this revision. Listed so a reader can attack the weakest joints first.

**1. The per-unit hour bands in §5.4 — still number one.** Every band in Blocks 1 through 7 (2/4/6
hours for a shallow action, 24/40/70 for a deep one, 16/30/52 for a heavy Revit command) is
**INFERRED professional judgement with no published benchmark behind it**. §5.2 documents the search
and its failure: no academic, standards-body or government source publishes hours-per-screen,
hours-per-CRUD-screen or hours-per-Revit-command, and one agency states on its own site that it
deliberately refuses to. **These bands drive the entire bottom-up estimate.** Halve them all and the
central falls to ~23,700 hours; double them and it rises to ~94,700. The external checks disagree
about the direction of the error: object points (§4.4) say the UI bands are **too generous by
1.8×–3.4×**, while the function-point check (§6.2) says the total is **too low by 1.37×–2.3×**. Both
cannot be right.

**2. The four estates still priced from artefact inspection alone — Blocks 6.6, 6.9, 6.10 and 6.13.**
Together **7,250 central hours, 18% of the pre-overhead total**, and every one is priced from file
counts, line counts and project counts, with no benchmark, no analogue and no independent audit
behind it. **6.10 (DemoStudio, 2,500 h)** carries the most exposure: it is dormant, and a reader may
reasonably argue a product with one commit in ninety days should not be costed at full build value at
all.

**This item is smaller than it was, and the reason is worth recording.** The previous version of this
list named **five** estates totalling 9,650 hours, and flagged that *"a separate audit of the Virtual
Drafter was running while this was written and had not reported."* **It has now reported**
(`modules/13-virtual-drafter.md`, 2026-08-21), and the Virtual Drafter line moved from **2,400 to
1,680 central hours — down 30%** — because the audit established there is no planner, no rules engine
and no job instantiation to price. **The caveat resolved in the direction the caveat warned about**,
which is the outcome a well-placed caveat should have.

**3. The 7.3 hours-per-function-point the central case implies.** Against Capers Jones' sourced US
average of **16.5 h/FP**, this report's central case claims the work was done **2.2× faster than the
US industry norm**. §6.2 gives the defence — the excluded lifecycle scope, the language level, package
reuse — and that defence is **argued, not measured**. Neither source publishes a scope-adjusted rate,
so the reconciliation cannot be verified. **If the scope argument is wrong, the central case is
wrong by roughly a factor of two**, and the honest outer bound is the $21.5M in §8.2.

**Four more worth naming, below the top three:**

- **The scope boundary itself (§0, §1.1).** ~729,000 lines were excluded against ~464,000 included.
  The three big exclusions are defensible on inspection — a staging copy, decompiled binaries,
  minified output — but **the ratio means a single misclassification moves the answer more than any
  hour band does.** If `_ToUpload` were counted, the LOC basis rises 7%; if the decompiled recovery
  were counted as authored source, it rises 8% and the report would be crediting KOR for another
  developer's work.
- **The 20% cross-cutting uplift (Block 8) and the standards-curation hours (Block 6.1b).** The uplift
  adds 7,895 hours to the central case — **17% of the total — from a single percentage chosen by
  convention**. The curation line is an estimate of *analyst* labour derived from nothing but an item
  count; it could plausibly be half or triple.
- **The COCOMO II effort-adjustment factor of 0.36 (§3.3).** Still built from cost-driver values that
  **could not be verified from any reachable source** — and this revision confirmed why: the official
  Model Definition Manual, read in full at a .edu mirror, **does not contain them**. Its blast radius
  has shrunk from "sets the high case" to "corroborates it", which is an improvement.
- **The AVAIL Enterprise tier reading** (§9.2) and **the Newforma band** (§9.5). Both genuinely
  ambiguous; together they move the buy-path central by tens of thousands over five years.

**Promoted out of this list since the previous revision:** the outsourcing rate bands. They now have
**three independent sources** (§2.6), which agree closely on the onshore band that carries the high
case and disagree only in the direction that makes the low case *cheaper*. The 1.75× loaded
multiplier also drops off the top three — it remains INFERRED above 1.463×, but it now affects only
§8.4's hiring comparison, not the headline range.

**And two structural caveats that outrank all seven:**

- **The bottom-up prices doors, not rooms** (§1.3, §6.3). 403 actions and 179 non-UI entry points do
  not reach the services behind them — `Kor.Opportunities.Data` alone is 48,832 lines that no door
  count sees. **The bottom-up is biased low by construction**, which is why the reconciliations, not
  the census, bind the top of the range.
- **464,388 lines is a size measure, not a value measure**, and the actual build ran at ~341 lines
  per hour, which no human types. **Every LOC-driven number here is biased high.** The two biases run
  in opposite directions, and the central case sits where they cross — which is the strongest claim
  this report can honestly make for it.

**One closing observation on method, offered because this revision tested it four times.** Each round
of this work arrived with a message saying the estimate was too small, and each round the right
response was to **measure before pricing**. Measuring found real, uncosted products — an exporter
suite, an ETABS plugin set, two takeoff tools, a drafting agent. It also found 59-line Revit
commands, a 50-command "suite" sharing 1,034 lines between its members, 128 UI-local button handlers,
729,000 lines that had to be thrown out, and — on the one estate that looked most like a moonshot —
**no planner, no rules engine and no job instantiation, because none was written.**

**The additions and the corrections were close to the same size, and the central case moved 5%.**
A brief asserting that a number is too low is evidence about the inventory, not about the price. The
final shape of this revision is the argument for that: **thirteen estates the previous report had no
line for, and a central case within 5% of the one it produced.**

---

## 12. Sources, with dates

Everything was fetched live on **2026-08-20 or 2026-08-21**. The shared web-search budget was
exhausted (200/200) before this work began; most evidence came from **direct fetches of named URLs**,
supplemented by a research subagent using alternate search engines. Sites that blocked the tools are
recorded rather than quietly dropped.

### Machine measurements of this codebase — VERIFIED, 2026-08-21

| measurement | result | how to re-run |
|---|---|---|
| XAML root-element census | 80 Window / 42 UserControl / 4 ResourceDictionary / 1 Application = 127 | first root tag of each `.xaml` |
| Distinct Click handlers | 403 (542 attribute instances) | `grep -rhoE 'Click="[A-Za-z_][A-Za-z0-9_]*"' --include=*.xaml \| sort -u` |
| Handler body sizes and depth classification | 403/403 matched; 106 deep / 169 mid / 128 shallow; 204 call a service; 0 open a DB connection | brace-matching from each handler's `(object sender` signature |
| C# LOC, in scope | 266,228 + 29,876 + 19,385 + 17,688 + 13,205 + 3,489 + 1,054 = **350,925** | `find -name "*.cs" -not -path "*/bin/*" -not -path "*/obj/*" -print0 \| xargs -0 cat \| wc -l` |
| XAML / SQL+PS / web LOC, in scope | 34,181 / 69,013 / 10,269 | same pattern per extension |
| **Excluded** — `_ToUpload` / `Michael Li\Recovery\03-Decompiled-Clean` / `_Publish` | 25,510 C# + 7,121 XAML / 36,375 C# in 26 projects / 647,226 web | same pattern; see §0 for why each is out |
| MCP tools | 23 | `grep -rhoE '\[McpServerTool\(Name = "[a-z_0-9]+"\)\]'` |
| Revit ribbon commands | 145 entries, 145 distinct command types, 23 panels, 18 categories | `grep -cE '^\s+new\("' ToolCatalog.cs`; categories from `typeof(Tools.<X>.` |
| Revit command code volume | `Tools/` 8,549 lines = **59 per command**; `Framework/` 2,336. Rebar 1,034 ÷ 50 = 21/cmd; Sheets 960 ÷ 10 = 96/cmd; Data 924 ÷ 5 = 185/cmd | per-directory `wc -l` under `Tools/` |
| ETABS plugins | 17,688 C#, 3 `.csproj` | `find "ETABS/Plugin Development" -name "*.cs" -print0 \| xargs -0 cat \| wc -l` |
| DemoStudio | 29,876 C# + 1,988 XAML, 13 `.csproj`, 1 commit in 90 days | same, on `App Demo Maker` |
| Revit build matrix | 8 configurations, 3 TFMs | `KOR.RevitTools/Directory.Build.props` |
| Worker jobs | 40 | `grep -rhoE 'class [A-Za-z0-9_]+Job\s*:\s*IJob'` |
| PdfToSafe estate | 46 files, 12,990 lines, 5 CSI formats, 7 COM/OAPI driver files | `ls`/`wc` on `App/EngineeringTools/PdfToSafe/` |
| EngineeringTools split | `Dxf/` 7,270 lines; takeoff ~9,550; `TakeoffCli` 3,562 | per-directory `wc -l` |
| Virtual Drafter estate | `src` 3,844 · `db` 23,798 (42 migrations) · `intake` 11,761 · `process-record` 22,190 · `crawl-results` 19,420 · `standards` 49,468 · `exam` 2,070 | per-directory `wc -l` on `KOR.Drafter` |
| KorStandards migrations | 42 | `find KOR.Drafter/db -name "*.sql"` |
| Transmittal purposes | 8 | `grep -rhoiE '"(Site Instructions\|For Review\|…)"'` |
| Projects / commits | 69 / 2,539 | `.csproj` count; `git rev-list --count HEAD` summed over four repos |

### Wages, employer costs and FX — VERIFIED

| source | what it gave | URL | date |
|---|---|---|---|
| **BLS Public Data API** — OES 15-1252, May 2025 | mean $148,100; median $135,980; 75th $171,980; 90th $214,670; LA metro mean $161,900 | `api.bls.gov/publicAPI/v2/timeseries/data/OEUN000000000000015125204` (and …213 / …214 / …215 / `OEUM003108000000015125204`) | 2026-08-20 |
| **BLS Public Data API** — ECEC, civilian, Q1 2026 | wages $33.72/h; benefits $15.60/h; total $49.32/h | `CMU1020000000000D`, `CMU1030000000000D` | 2026-08-20 |
| **BLS ECEC news release USDL-26-0827** | corroborates; private industry $46.60 total / $32.60 wages / $14.01 benefits | bls.gov/news.release/ecec.nr0.htm (live 403s; via archive) | 2026-08-20 |
| **WorkBC**, Province of BC — NOC 21232 | BC low $31.25 / median $52.40 / high $84.13; annual $107,315 | [workbc.ca](https://www.workbc.ca/career-profiles/software-developers-and-programmers) | 2026-08-20 |
| **ESDC / Job Bank open data** | corroborates BC exactly; Canada-wide low $30.00 / median $48.08 / high $76.92 | [open.canada.ca adad580f](https://open.canada.ca/data/en/dataset/adad580f-76b0-4502-bd05-20c125de9116) | 2026-08-20 |
| **Bank of Canada Valet API** | 1 USD = **1.3785 CAD** | [bankofcanada.ca/valet](https://www.bankofcanada.ca/valet/observations/FXUSDCAD/json?recent=1) | 2026-08-20 |

### Contract rates — REPORTED, three sources, all commercially interested

| source | date | figures | URL |
|---|---|---|---|
| **Trio.dev** | 2026-04-21 | offshore $40–70 · nearshore $50–85 · E. Europe $70–105 · NA $150–255 | trio.dev |
| **DistantJob** (Ihor Shcherbinin, VP Recruiting) | **2026-02-12** | US agency/contractor *"$80 … to $150–$200+"* · E. Europe *"$25–$55"* · LATAM *"$23 to $90"* · Asia *"$26 to $41"* · Africa *"$20–$50"* · W. Europe *"€70–€120 ($80–$130)"* | [distantjob.com](https://distantjob.com/blog/offshore-developer-rates/) |
| **Hauerpower** (Polish nearshore vendor) | **updated April 2026** | Senior: Poland $55–75 · LATAM $50–70 · Ukraine $40–60 · India $25–45 · USA $150–250 · UK $120–180. Architect: Poland $75–110 · USA $200–350 · India $45–75. *"Real TCO is 1.15-1.30x of nominal hourly cost"* | [hauerpower.com](https://www.hauerpower.com/en/insights-posts/nearshore-software-development-rates-2026) |

*Blocked:* `bls.gov` (403), `careeronestop.org` (403), `jobbank.gc.ca` wage pages (wrong occupation),
Robert Half (JS-gated), Dice / ZipRecruiter / Glassdoor (403), Accelerance (lead-gated), GoodFirms
(403), Clutch (two listings only), Toptal / Upwork (404/403), `arc.dev` rate article (404, re-attempted
2026-08-21).

### Estimation models and delivery rates — mixed

| source | what it gave | tag | URL | date |
|---|---|---|---|---|
| **IFPUG uTip #03**, *"Early FPA and Consistent Cost Estimating"* v2.0, Timp & Sgamma | *"a productivity rate of 10 hours / FP"* worked example; *"indicative functional size = 35 x ILFs + 15 x EIFs"*; *"4.6 SFP per EP, 7.0 SFP per LF"*; *"just assign 5 function points for each unknown function type"* | **VERIFIED** — standards body, sells no dev services | [ifpug.org uTip-003](https://ifpug.org/wp-content/uploads/2026/06/uTip-003-Early-FPA.pdf) (announced [2026-06-24](https://ifpug.org/2026/06/24/utip-3-early-fpa-and-consistent-cost-estimating-is-published)) | **2026-08-21** |
| **Capers Jones**, *"Using AI For Large Software Engineering Projects – Part 1"*, hosted by IFPUG | *"Circa 2025, the U.S. average software productivity is roughly 8.00 function points per staff month or 16.5 work hours per function point."* | **REPORTED** — author ran Namcook / SPR, both sold estimation tools | [ifpug.org Capers1.pdf](https://ifpug.org/wp-content/uploads/2025/03/Capers1.pdf) | **2026-08-21** |
| **COCOMO II Model Definition Manual (1998.0)**, .edu mirror, read in full | Confirms the manual covers **only** Early Design and Post-Architecture — **no Application Composition / object-point chapter** | **VERIFIED by absence** | [athena.ecs.csus.edu](https://athena.ecs.csus.edu/~buckley/CSc231_files/Cocomo_II_Manual.pdf) | **2026-08-21** |
| **Object-point weight table** — Screen 1/2/3, Report 2/5/8, 3GL 10; PROD 4/7/13/25/50 NOP/PM | Two independent tertiary reproductions agreeing exactly | **REPORTED (tertiary)** | [slideshare — Vijay Kumar](https://www.slideshare.net/slideshow/software-estimation-models-ii-lec-05/80085188) · [geeksforgeeks](https://www.geeksforgeeks.org/software-engineering-application-composition-estimation-model-cocomo-ii-stage-1/) (upd. 2025-07-11) | **2026-08-21** |
| **Boehm et al., "COCOMO 2.0," Annals of Software Engineering, 1995** — the primary object-point source | — | **COULD NOT VERIFY** — paywalled | [DOI 10.1007/BF02249046](https://link.springer.com/article/10.1007/BF02249046) (redirects to IdP login) | 2026-08-21 |
| **USC COCOMO II driver/scale help page**, Perma.cc capture 2018-05-01 | E = 1.01 + 0.01×ΣSF; ΣSF bounds 0–25; 5 scale factors, 17 multipliers by name | **VERIFIED** | [archive.org/details/perma_cc_N6SF-HKWQ](https://archive.org/details/perma_cc_N6SF-HKWQ) | 2026-08-21 |
| **AFIT / DTIC ADA329977** (Bernheisel, 1997) | person-month = 152 hours; MMRE 0.36–0.79, PRED(.25) 0.07–0.44 against four DoD datasets | **VERIFIED** | [archive.org/details/DTIC_ADA329977](https://archive.org/details/DTIC_ADA329977) | 2026-08-21 |
| **Softstar Systems** | A = 2.94; C = 3.67 | **REPORTED** (tool vendor) | [softstarsystems.com](https://www.softstarsystems.com/overview.htm) | 2026-08-21 |
| **QSM Function Point Languages Table v5.0** (2,192 projects) | C# 54 SLOC/FP avg, 59 median, 29–70 range; C 97 avg. **Re-fetched and confirmed to contain no productivity or delivery-rate figures.** | **REPORTED** (vendor data) | [qsm.com](https://www.qsm.com/resources/function-point-languages-table) | **2026-08-21** |
| **Wikipedia, Object point** | **No weight table, no productivity rates, no NOP formula** | **could not verify** | [en.wikipedia.org/wiki/Object_point](https://en.wikipedia.org/wiki/Object_point) | **2026-08-21** |
| **Wikipedia, COCOMO** | 161-project calibration base; **does not cover Application Composition** | **REPORTED** | [en.wikipedia.org/wiki/COCOMO](https://en.wikipedia.org/wiki/COCOMO) | 2026-08-21 |
| **El-Ramly, ACEM**, arXiv 2608.02582 | COCOMO II's human-labour assumption breaks under agentic development; constants left symbolic | **VERIFIED** | [arxiv.org/abs/2608.02582](https://arxiv.org/abs/2608.02582) | 2026-08-21 |
| **Koch & Wellbrock, Agile V**, arXiv 2602.20684 | *"10-50x cost reduction versus a COCOMO II baseline"* — on a ~500 LOC case study | **VERIFIED** (as a claim; the claim is weak evidence) | [arxiv.org/abs/2602.20684](https://arxiv.org/abs/2602.20684) | 2026-08-21 |
| **ISBSG** | publishes **no free** hours-per-FP or delivery-rate benchmark | **VERIFIED by absence** | [isbsg.org](https://www.isbsg.org/) | 2026-08-21 |

### Per-unit effort benchmarks — searched, largely not found

| what was sought | result | detail |
|---|---|---|
| Hours per screen / UI view | **could not verify** | No academic, standards-body or government source. Agencies publish whole-project ranges only; [budventure.technology](https://budventure.technology) states it uses a role-based model *"instead of a flat cost per screen shortcut"*. Only per-screen figure found anywhere was an undated Reddit comment (*"2-5h work time per screen"*, design only) — **anecdote, not used**. |
| Hours per CRUD screen | **could not verify** | Nothing at this granularity from any source type. |
| Hours per report / per workflow | **could not verify** | Qualitative statements only. |
| Hours per integration | **found, vendor-only** | Merge.dev/Kertos *"40 hours, on average"* (undated) · [bundle.social](https://bundle.social/blog/tiktok-api-integration-cost) *"40-50 hours"* (2026-08-17) · [LinkedIn/John Root](https://www.linkedin.com/pulse/manual-point-to-point-integration-takes-100-hours-build-john-root-rlehe) *"baseline of 100 hours"* (2026-05-18) · [topflightapps](https://topflightapps.com/ideas/app-development-costs/) *"$4,000–$6,000"* (2026). **All sell integration platforms or dev services.** Used as a floor sanity-check, not a rate. |

*Unreachable:* `csse.usc.edu` (no DNS), `sunset.usc.edu` (refuses HTTPS), Boehm et al. 2000
(controlled digital lending), Capers Jones / Namcook (403), ResearchGate and Academia.edu (403),
Clutch cost pages (404/403), `web.archive.org` (blocked by the tool — plain `archive.org` is **not**,
which is how the two verified sources above were obtained). DuckDuckGo captcha-walled and Marginalia
bot-walled the research subagent; Brave rate-limited after ~10 calls; Yahoo mostly worked.

### AI-productivity evidence — VERIFIED unless noted

| source | date | URL |
|---|---|---|
| METR RCT — 19% slowdown | 2025-07-10 | [metr.org](https://metr.org/blog/2025-07-10-early-2025-ai-experienced-os-dev-study/) |
| METR follow-up — −18% / −4%, greenfield cohort | **2026-02-24** | [metr.org](https://metr.org/blog/2026-02-24-uplift-update/) |
| GitHub/Microsoft — 55% faster, N=95, CI [21%, 89%] | 2022-09-07, upd. 2024-05-21 | [github.blog](https://github.blog/news-insights/research/research-quantifying-github-copilots-impact-on-developer-productivity-and-happiness/) |
| Cui et al. (Microsoft Research) — +26.08%, N=4,867 | June 2025 | [microsoft.com/research](https://www.microsoft.com/en-us/research/publication/the-effects-of-generative-ai-on-high-skilled-work-evidence-from-three-field-experiments-with-software-developers/) |
| Paradis et al. (Google) — ~21% faster, N=96 | 2024-10-16 | [arxiv.org/abs/2410.12944](https://arxiv.org/abs/2410.12944) |
| DORA / Google Cloud State of DevOps 2024 & 2025 | 2025 edition 2025-09-23; **no 2026 edition exists** | Google Cloud Blog |
| GitClear — clones 8.3%→12.3%, refactoring 25%→<10% | 2024 data | [gitclear.com](https://www.gitclear.com/coding_on_copilot_data_shows_ais_downward_pressure_on_code_quality) — REPORTED |
| JetBrains State of Developer Ecosystem 2025, N=24,534 | 2025 | [devecosystem-2025.jetbrains.com](https://devecosystem-2025.jetbrains.com/) — self-report |
| Stack Overflow — **no 2026 survey exists** | confirmed 2026-08-20 | [survey.stackoverflow.co](https://survey.stackoverflow.co/) |

*Not verified and deliberately not cited:* Uplevel's "41% higher bug rate", BlueOptima, McKinsey, Bain.

### Vendor pricing — VERIFIED (fetched from the vendor's own page)

| vendor | prices | URL | date |
|---|---|---|---|
| **Egnyte** | Team $10 (1–10 users only) · Business $22 · Enterprise Lite $39 · Elite $48 per user/mo billed annually · Ultimate quote-only · AEC add-ons $6 + $6 per user/mo | [egnyte.com/pricing](https://www.egnyte.com/pricing) | 2026-08-20 |
| **Bluebeam** | Basics $260 · Core $330 · Complete $440 · Max $590 per user/year | [bluebeam.com/pricing](https://www.bluebeam.com/pricing) | 2026-08-20 |
| **Power BI** | Pro $14.00 · Premium Per User $24.00 per user/mo paid yearly | [Power BI pricing](https://www.microsoft.com/en-us/power-platform/products/power-bi/pricing) | 2026-08-20 |
| **Microsoft 365 Copilot** | $30.00/user/mo paid yearly | [copilot-for-microsoft-365](https://www.microsoft.com/en-us/microsoft-365/enterprise/copilot-for-microsoft-365) | 2026-08-20 |
| **M365 Copilot Business** | $18.00/user/mo paid yearly (promotional, was $21.00, **promo ends 2026-09-30**) | [copilot/business](https://www.microsoft.com/en-us/microsoft-365/copilot/business) | 2026-08-20 |
| **Copilot Studio** | $200.00/pack/month for 25,000 Copilot Credits | [microsoft-copilot-studio](https://www.microsoft.com/en-us/microsoft-copilot/microsoft-copilot-studio) | 2026-08-20 |
| **Microsoft Fabric** | $0.18/CU-hour PAYG; 1-yr reservation $938/CU; 3-yr $2,814/CU | [Azure Retail Prices API](https://prices.azure.com/api/retail/prices?currencyCode=USD&$filter=serviceName%20eq%20%27Microsoft%20Fabric%27%20and%20armRegionName%20eq%20%27westus2%27) | 2026-08-20 |
| **AVAIL** | Free $0 · **$25/mo or $250/yr** (single user) · Enterprise "$250 for 3-year term", min 50 users (ambiguous). Revit add-in, Palettes, Project Navigator, Harvest, Revit Application Version Management. **No numbered detail register, approval workflow, watermarking or conformance scoring.** | [getavail.com/pricing](https://getavail.com/pricing/) · [getavail.com](https://getavail.com/) | **2026-08-21** |
| **IDEA StatiCa** | €1,750–€4,990/yr; €3,490–€9,990 perpetual, per floating seat | [ideastatica.com/pricing](https://www.ideastatica.com/pricing) | 2026-08-20 |
| **CSI ETABS** | DXF/DWG import is *"a template to trace over"*; **no published price** | [csiamerica.com](https://www.csiamerica.com/products/etabs/features) | 2026-08-20 |

### Vendor pricing — QUOTE-ONLY (confirmed by fetching the vendor's own page)

Newforma [Project Center](https://www.newforma.com/newforma-project-center/packages/) and
[Konekt](https://www.newforma.com/newforma-konekt/packages/) · Deltek
[Vantagepoint](https://www.deltek.com/products/erp/vantagepoint/) and
[PIM](https://www.deltek.com/products/delivery-assurance/project-information-management/) ·
Deltek **Vantagepoint Intelligence** (no product page found) · Deltek **Proposals** (nav slot resolves
to **ProPricer**) · [OpenAsset](https://www.openasset.com/pricing) · **Unanet** CRM / ProposalAI
(`unanet.com/pricing`, `/crm/pricing`, `/crm` all 404) · **UNIFI Labs**
([unifilabs.com](https://unifilabs.com/), `/pricing/` 404, fetched 2026-08-21) · **CSI ETABS**.
GSA eLibrary lists **DELTEK, INC.** on the Multiple Award Schedule (SINs 54151 / 511210 / 611420) but
**exposes no price list**
([gsaelibrary.gsa.gov](https://www.gsaelibrary.gsa.gov/ElibMain/searchResults.do?searchText=Deltek&searchType=allWords), 2026-08-20).

### Vendor pricing — REPORTED third-party estimates (not vendor prices)

[ITQlick, Newforma Project Center](https://www.itqlick.com/newforma-project-center) —
$50–$150/user/month, self-labelled "(Estimated)" ·
[Vendr, Deltek](https://vendr.com/marketplace/deltek) — median contract $20,380/yr across 33 purchases
(range $9,248–$63,900); Vantagepoint at 30–75 users $60k–$150k/yr plus $40k–$120k implementation.
Both fetched 2026-08-20.

### Internal sources

`docs/audit-2026-08/00-INVENTORY.md` · `SCOPE.md` · `05-MASTER-AUDIT.md` · `07-EXECUTIVE-SUMMARY.md` ·
`04-TODO-REGISTER.md:424` (483 executed DXF→ETABS tests) ·
`modules/02-transmittals-tracking.md` (8 purpose types; the bookmark workflow) ·
`modules/12-standards-centralisation.md` (612 details, 1,079 occurrences, ~379 families; the
five-way palette blockage) · `modules/07-bd-brain-core.md` (111 sources, 372,370 emails) ·
`modules/09-engineering-tools.md` · `modules/10-dxf-to-etabs.md` ·
`KOR.Drafter/docs/` — `ROADMAP.md`, `STATE-2026-08-04.md` (*"bridge 1.0.28, 41 verbs"*, *"largely
BUILT"*), `TEMPLATE-BUILD-PLAN.md`, `TRAINING-CURRICULUM.md`, `ARCHITECTURE-INTERCONNECTION.md`,
`ECONOMICS-BASELINE.md`, `CRAWL-RUNBOOK.md` ·
`modules/13-virtual-drafter.md` (**the Virtual Drafter audit — repricing basis for Block 6.8**: no drafting-decision code, no job instantiation, DRILL-06 never run, the exam closed-book condition verified forensically against an 8.5 MB session transcript) · `KOR.Drafter/exam/31202-01/EXAM-SCORECARD.md` (12/12 implemented; scorecard's win-count superseded) · `KOR.Drafter/standards/RULINGS.md:51-53` (Jim DesRoches, 2026-07-31 — the 1-win/2-loss adjudication) ·
`KOR.RevitTools/docs/BUILD-STATUS.md` and `legacy-parity-matrix.md` (the pyRevit / DiRoots / Ideate /
Naviate / CTC competitive survey; the 40-button legacy ribbon map behind Block 6.13) ·
`KOR.RevitTools/src/KOR.RevitTools.Addin/Framework/ToolCatalog.cs` (145 commands, 23 panels,
18 categories) · `App Demo Maker/ARCHITECTURE.md` (*"production-style desktop recorder/composer/
publisher workflow shell"*) · `ETABS/Plugin Development/` (three `.csproj`) ·
`competitive/C1`–`C4` (vendor landscape; C2's Egnyte tier names are corrected in §9.2) ·
`git log` / `git shortlog` on `Operations`, `KOR.Drafter`, `KOR.RevitTools`,
`KOR Inspections Bookings`.
