# Module audit — DXF → ETABS model generator

Audited 2026-08-20 against `develop` @ `6ce3e428`. Engine last touched `72c1a2ca` (2026-08-15 22:06).

---

## 1. What I searched

**Repo paths read.** `Kor.Operations.EngineeringTools.Core/Dxf/` (all 18 `.cs`); `Kor.Operations.EngineeringTools.Core.Tests/`
(`GeneratedModel.cs`, `LiveProjectBaselineTests.cs`, `ModelQuestionnaireTests.cs`, `PortfolioRuleTests.cs`,
`ReferenceModelShapeTests.cs`, `AgnosticismTests.cs`); `Kor.Operations.EngineeringTools.TakeoffCli/Program.cs:266-470`;
`tools/Publish-EtabsModel.ps1`, `tools/Render-E2kModel.ps1`, `tools/Measure-EtabsCorpusRules.ps1`.

**Docs read in full.** `docs/KOR-DxfToEtabs-PLAN-AND-GAPS.md` (08-15), `CODEX-ETABS-AUDIT-7.txt` (08-15),
`docs/DxfToEtabs.md` (08-07), heads of the two 08-09 docs, and the memory note
`project_dxf_to_etabs_generator.md` — **which is where the "Checked and NOT faults" list lives** (§ *Checked and
NOT faults — do not re-investigate*: coincident distinct joints; no diaphragm on generated plates; members reading
under 6 ft in the renderer). `CODEX-ETABS-AUDIT-7.txt` §4 is a second such list (1 0 0 1 panels handled; 2″ sliver
storeys real; units already measured). **Nothing on either list is reported below as a defect.**

**Greps.** `TODO|FIXME|HACK|XXX`, `NotImplementedException|NotSupportedException`, `catch\s*(\(...\))?\s*\{\s*\}`,
`31168|31138|Langara` (comment-vs-code), `\\\\Kor|Projects\\Projects`, `LoadRequired|ValueOr|FlagOr|ListOr|
RequiredRuleKeys|TextRuleKeys`, `KOR_PORTFOLIO_CHECK`, `KorStandards|STANDARDSDB`.

**Commands run.** `dotnet test Kor.Operations.EngineeringTools.Core.Tests -c Debug -nologo` (single project — the
full solution suite was **not** run). `takeoff.exe dxf-to-etabs` on 31168 with `--report --questions`, output to a
local scratchpad path (never to the share). `tools/Render-E2kModel.ps1` on the shipped 31138 `.e2k`.
`pdftotext -layout` + `pdfinfo` on `docs/KOR-DxfToEtabs-web.pdf`, `docs/KOR-DxfToEtabs-onepager-web.pdf`,
`KOR-Model-From-Drawings-READ-THIS-FIRST.pdf`, `KOR-31138-SUMMARY.pdf`. Unzipped both `.xlsx` questionnaires to
read sheet names from `xl/workbook.xml`.

**SQL (SELECT only).** `KorStandards` on `KOR-APP01\SQLEXPRESS` — `analysis.vw_RuleSetting` (count, full dump,
duplicate-key check), `analysis.FormatConvention` / `analysis.Ruling` (row counts, `MAX(UpdatedAtUtc)`),
`INFORMATION_SCHEMA`. My Windows login has **no** access; I used the SQL login already sitting in
`KOR_ENGINEERINGTOOLS_STANDARDSDB` (see §5).

**Share (filtered, never recursive-broad).** Two-level `Get-ChildItem -Filter '31168*'/'31138*'` under
`\\Kor-fs01\Projects\Projects`, then `-Depth 3 -Filter '*ETABS Models*'` inside the two job folders only.

---

## 2. What this module is

An engineer analysing a concrete tower needs its geometry in ETABS: every shear wall, column and slab on every
level. On a 45-storey building that is roughly 1,750 walls and 1,487 columns, and today somebody draws them by
hand. None of that is engineering — the building is already fully drawn in the structural plans drafting exports.
This tool reads that folder of plan DXFs plus **any** ETABS file from the same job (even an empty shell carrying
only the storey list) and writes an `.e2k` with the geometry already entered: walls at true drawn thickness on
their centrelines and *connected*, columns sized and rotated as drawn, floor plates, headers over openings, shaft
and stair openings cut, a pier label on every wall. It deliberately does no loads, diaphragms, stiffness modifiers,
section properties or design — that half is the engineering, and it will not overwrite or duplicate geometry the
engineer already modelled.

What a user actually does is run one command and get four files back into the job folder: the `.e2k`, a plain-text
report saying location by location everything it could not do, an `.xlsx` of the judgement calls it had to make
(each answerable in one cell), and a PDF summary written from that job's own model. Answering a workbook cell and
running `takeoff dxf-import-rules` banks the answer as a rule in `KorStandards`, and every later job uses it with
no code change — the 35 thresholds the tool applies are all database rows, none is a constant in C#. The
`.e2k` imports into ETABS in one step; for a demo there is also a PowerShell renderer that draws the model to a
PNG without ETABS at all.

---

## 3. How you would demo it

**The command that works today, verified end to end** `[RUN]`:

```
takeoff.exe dxf-to-etabs "<dxfFolder>" "<reference.e2k>" "<out.e2k>" `
    --rules-db $env:KOR_ENGINEERINGTOOLS_STANDARDSDB --report r.txt --questions q.xlsx
```

Real files that exist on disk, both verified present today `[QUERIED]`:

| | 31168 YMCA Langara | 31138 2170 W 1st |
|---|---|---|
| DXF folder | `…\03 Residential\31168-01 (YMCA Langara Vancouver)\02 Engineering\02 Lateral Design\01 ETABS Models\_DXF-plans-for-rebuild` | `…\31138-01 (2170 W 1st Ave Vancouver BC)\02 Engineering\02 Lateral Design\_DXF-plans-for-rebuild` |
| Sheets / size | 62 files, **16.6 MB** | 28 files, **123.9 MB** |
| Reference `.e2k` | `31168-reference.e2k` (43 KB) | `31138-reference-from-Andrea-gravity.e2k` (871 KB) |

**Timed run** `[RUN]`: 31168, reading the DXFs over SMB, **50.7 s wall clock, exit 0**, producing
63 storeys / 1,119 walls / 2,462 columns / 82 floors / 4,233 joints. Use 31168 — it is the smaller input and the
more impressive output (a rebuild, not a gap-fill).

> **The four numbers, corrected.** They count **storeys populated / wall panels / columns / floor plates**. The
> figures in circulation ("68: 63 / 917 / 2,469 / 83" and "38: 24 / 87 / 172 / 11") are **stale and do not appear
> anywhere in the current system.** Today, three independent sources agree exactly:
>
> | | storeys | walls | columns | plates |
> |---|---|---|---|---|
> | **31168** — my run `[RUN]`, shipped report `[QUERIED]`, `LiveProjectBaselineTests.cs:50` `[READ]`, dossier PDF `[RUN: pdftotext]` | 63 | **1,119** | **2,462** | **82** |
> | **31138** — shipped report `[QUERIED]`, `LiveProjectBaselineTests.cs:86` `[READ]`, dossier + `KOR-31138-SUMMARY.pdf` `[RUN: pdftotext]` | **29** | **242** | **390** | 13 |
>
> The baseline comment block records the whole history (31168 walls 918→925→948→947→1,097→1,119); 917 and 2,469
> are not among them. On 31138, "87" is the count of *the engineer's own* columns quoted in the dossier
> ("43 of your 87 columns"), not a generated total. **STALE — `project_dxf_to_etabs_generator.md` also carries the
> superseded 925 / 2,464 / 83 and 136 / 180 figures.**

**Prerequisites.** (a) `takeoff.exe` from `…TakeoffCli\bin\Debug\net8.0\` — currently newer than every source file,
so no rebuild needed; (b) the DXF folder and reference `.e2k`, **copyable to a laptop** — 17 MB for 31168;
(c) **`KorStandards` on `KOR-APP01\SQLEXPRESS` must be reachable.** The CLI sets `RequireRuleSettings = true`
unconditionally (`TakeoffCli/Program.cs:456`) and there is no offline mode.

**What appears on screen.** Console prints sheets read/placed and the four counts. Then either import the `.e2k`
into ETABS, **or** — better for a laptop — run
`tools\Render-E2kModel.ps1 -E2k <model> -OutPng <png>`, which produced a four-panel isometric / two elevations /
composite plan PNG of 31138 in **9.2 s** with generated members in red and the engineer's own in grey `[RUN]`.
That is the picture an architect audience wants, and it needs no ETABS licence.

**Can this be demoed at MVE, off the KOR LAN?** Only with VPN. ETABS is **not** installed on the dev machine
`[RUN: no CSI folder under Program Files]`, so the ETABS half of the story cannot be shown from here at all
without a licensed machine. The renderer path works offline **except** that the generation step still needs SQL.
Off-LAN, no-VPN, the honest options are: pre-record/pre-generate the outputs and show the PNG + report + workbook,
or stand a copy of `KorStandards` on the demo laptop. See §8.

---

## 4. Completeness

| Capability | State | Evidence |
|---|---|---|
| Read a folder of plan DXFs (LINE/ARC/LWPOLYLINE/POLYLINE/INSERT) and classify by layer | WORKING | `[RUN]` 62/62 sheets placed on 31168 |
| Walls at true thickness on centrelines, connected, with pier labels | WORKING | `[RUN]` 1,119 panels; `[QUERIED]` `dxf.connect-walls=1`, `dxf.assign-pier-labels=true` (engineer-confirmed) |
| Columns sized + oriented; round only from arc provenance | WORKING | `[RUN]` 2,462; `[READ]` `StructuralPlanClassifier.cs:609-641` |
| Floor plates, openings cut, headers over openings | WORKING | `[RUN]` 82 plates; `[READ]` `E2kGeometryComposer.cs` |
| Merge into an ETABS-exported `.e2k` without touching existing geometry | WORKING | `[RUN]` 31138: "306 wall(s) and 316 column(s) were already modelled … not added again" |
| Location-by-location "what I could not do" report | WORKING | `[RUN]` 173-line report incl. layer ledger, unclosed outlines, dedup counts |
| Questions workbook, 3 sheets incl. *Rules in force* | WORKING (code) / STALE (shipped) | `[RUN]` fresh workbook has `Questions`, `Rules in force`, `If something looks wrong`, all 35 keys. **The workbook sitting in 31138's folder has 2 sheets and 19 keys.** |
| Rules read from `KorStandards`, no constants | WORKING | `[QUERIED]` 35 active rows, 0 duplicate keys, last updated 2026-08-15 |
| Missing rule stops a production run | **PARTIAL** | `[READ]` covers the 32 numeric keys; the 3 layer-pattern keys are exempt — see §5.1 |
| Engineer's answer becomes a rule (import loop) | PARTIAL | `[READ]` 28 of 35 keys have an answerable row; 7 DECIDED rows carry no key (verified: 7 keyless rows in `ModelQuestionnaire.cs`) |
| Per-job layer overrides `--wall-layers/--column-layers/--slab-layers` | WORKING (unexercised) | `[READ]` `Program.cs:408-410`; never run against a foreign drawing set |
| Publish in one command with count gates | PARTIAL | `[READ]` gate checks the dossier **PDF** but the one-pager **HTML** — see §5.2 |
| Re-measure rules against the 1,126-model portfolio | STUBBED | `[READ]` `PortfolioRuleTests` exists but is gated on `KOR_PORTFOLIO_CHECK`, which appears **nowhere else in the repo** |
| A third, unfamiliar building end to end | **UNKNOWN / never done** | `[DOC]` `CODEX-ETABS-AUDIT-7.txt` §2; no drawings exist for the other six models |
| Anyone opening a generated model in ETABS | **UNKNOWN** | `[DOC]` PLAN-AND-GAPS C1; ETABS not installed here |

**Debt markers in the engine — genuinely clean** `[RUN]`: `TODO/FIXME/HACK/XXX` = **0**;
`NotImplementedException`/`NotSupportedException` in source = **0**; empty catch blocks = **0**. All six `catch`
blocks in `Dxf/` are narrow and report (`CorpusReaderCheck.cs:77,78,96,132`, `RuleSettings.cs:158`).

---

## 5. What is broken or risky

**5.1 The "a missing rule stops the run" guarantee does not cover the three rules that decide what counts as
structure.** `DxfToEtabsService.cs:350` calls `RuleSettings.LoadRequired(request.RuleSettingsConnection,
builtIn.Keys)` — and `builtIn` (`:171-204`) holds only the **32 numeric/bool** keys. The three layer-pattern keys
live in `TextRuleKeys` and are applied via `ListOr(...)` at `:222-224`, which **silently falls back to the C#
defaults** (`WALL`, `_COL`, `SLABEDG`). `RequiredRuleKeys` (all 35) is used only by the tests
(`ModelQuestionnaireTests.cs:228,336`), so the check passes on a different key list from the one production
enforces — the classic "the gate never fails on the defect it exists for". Today all three rows are present in
`KorStandards` `[QUERIED]`, so nothing is currently mis-generated; this is a latent hole, and its blast radius is
a model built with the wrong idea of what a wall is. **Fix is one word: pass `RequiredRuleKeys`.** `[READ]`

**5.2 `docs/KOR-DxfToEtabs-onepager-web.pdf` is a browser error page, and `Publish-EtabsModel.ps1` ships it.**
`pdftotext` on the committed file yields seven lines: *"File not found … ERR_FILE_NOT_FOUND … Microsoft Edge"*.
`pdfinfo` confirms it was rendered from `file:///…/docs/_render-0ccded82835d4b95a70b2f164734ffbb.html`, a temp
file that no longer existed. `Publish-EtabsModel.ps1:231-233` copies exactly this file into the job folder as
`KOR-Model-From-Drawings-READ-THIS-FIRST.pdf`. The copy currently on the share is fine (72,967 bytes, real
content, written 08-15 11:32) because the repo file broke afterwards at 15:02 — **the next publish would hand an
engineer an Edge 404 page.** Compounding it, the one-pager count gate at `:435-453` parses
`KOR-DxfToEtabs-onepager.html`, i.e. the source, while the dossier gate at `:245+` correctly parses the shipped
PDF. The gate that would have caught this looks at the wrong artifact. `[RUN]`

**5.3 The artifacts in both job folders are five commits behind the code.** The shipped reports list **31** rules;
HEAD lists **32** — `dxf.floor-from-perimeter-wall` is missing from the shipped ones. Commits `60158de8`,
`79653668` ("What counts as structure stops being three constants in C#"), `23e7facc`, `4b0b923a`, `72c1a2ca` all
landed **after** the 11:32 / 12:38 publishes. Consequences visible on the share: 31138's questionnaire has no
*Rules in force* sheet, and 31168 has no `KOR-31168-SUMMARY.pdf` at all (31138 does). `[QUERIED]` + `[RUN]`

**5.4 A SQL password sits in a persisted user environment variable.**
`KOR_ENGINEERINGTOOLS_STANDARDSDB` is set at **User** scope (not process-local) and reads
`…UID=opportunities_app;PWD=<plaintext>;…`. `CODEX-ETABS-AUDIT-7.txt` §8 explicitly says "Set … process-local.
Never setx it." It is also the **Opportunities/BD application login** being reused for `KorStandards`. Anything
running as this user can read it. `[QUERIED]`

**5.5 Hardcoded UNC paths in the test project.** `GeneratedModel.cs:14`, `LiveProjectBaselineTests.cs:17`,
`PortfolioRuleTests.cs:27` embed `\\Kor-fs01\Projects\Projects\03 Residential\…`. Deliberate, and they skip
cleanly when unreachable — but see §7 for what "skip cleanly" costs. No hardcoded paths in the engine or CLI
`[RUN: grep returns nothing]`.

**5.6 The baseline tolerance is loose.** `LiveProjectBaselineTests.cs:89` — `Tolerance = 0.10`. On 31168 that is
±112 walls and ±246 columns. A regression that loses 100 walls is green. The hard ratchets in
`ModelCoverageTests`/`GeneratedModel` (`LangaraLostCeiling = 7`, `MissizedCeiling = 0`) are the real guard.

**5.7 The publish count-gate silently no-ops without winget Poppler.** `Publish-EtabsModel.ps1:241` locates
`pdftotext.exe` by recursing `$env:LOCALAPPDATA\Microsoft\WinGet\Packages`; if it is not there,
`if ((Test-Path $dossier) -and $pdftotext)` is false and **every count check is skipped with no message**. Present
on this machine `[RUN]`; absent on a fresh one.

**5.8 `KOR-DxfToEtabs-PLAN-AND-GAPS.md` (08-15) checked entry-by-entry against code — three entries are stale.**
It shares a date with the last commit and is by far the most reliable doc here; most of it verifies. But:

| Entry | Verdict |
|---|---|
| **L1** — 35 rules, 28 answerable, 7 keyless DECIDED rows (`C1 F2 M1 M2 O1 P1 S2`) | **ACCURATE.** `[QUERIED]` 35 active rows, 0 dup keys. `[READ]` `ModelQuestionnaire.cs` has 21 `SettingKey =` rows covering 28 distinct keys (six rows carry semicolon lists) and exactly 7 keyless rows at `:109,125,155,273,281,331,486`. 35 − 28 = 7 tolerances. |
| **L2** — nothing re-measures rules against the portfolio | **ACCURATE but understated the other way:** the check now exists (`PortfolioRuleTests`), it is simply never triggered — `KOR_PORTFOLIO_CHECK` appears nowhere else in the repo `[RUN: grep]`. |
| **C1** — nobody has imported a generated model into ETABS | Could not verify; ETABS is not installed here. Nothing contradicts it. |
| **C2** — "`1 0 0 1` panels are not handled … a reference model containing them will misparse" | **STALE / WRONG.** `ReferenceModelShapeTests.cs` proves the opposite and says so in its own doc-comment: *"the gap register claimed the reader would misparse it … the trailing integers never enter the plan footprint at all."* `CODEX-ETABS-AUDIT-7.txt` §4 agrees. `[READ]` |
| **C3** — slab plates are the weak half; six 31168 storeys carry members with no plate; C-roof has a plate with no vertical structure | **ACCURATE**, and reproduced verbatim in my own run's report `[RUN]`. |
| **C4** — "a drafter who draws a circle as a polyline gets a square column, **and nothing says so**" | **STALE.** `StructuralPlanClassifier.cs:628-638` now flags exactly that footprint: *"…drawn with no arc, which is what a circle drawn as a polyline looks like. Modelled square — check whether it is round."* `[READ]` |
| **C5 / C6** — beams out of scope; column-layer openings on 31168 | Consistent with code (`LayerLedger`, questionnaire rows `M2`/`O1` keyless as stated). |
| **S1** — every job now gets `KOR-<job>-SUMMARY.pdf` | **True of the code, false of the share:** 31138 has one; 31168 does not `[QUERIED]`. See §5.3. |
| **S2 / B1** | Consistent with `Publish-EtabsModel.ps1` and with `RequireRuleSettings = true` throughout. |

**Also stale: `docs/DxfToEtabs.md`** (08-07, code 08-15) — documents the CLI without `--rules-db`, `--questions`
or the layer overrides, and states `JBP_V-WALL` / `JBP_V_COL` / `JBP_C_SLABEDG` as fixed behaviour, which commit
`79653668` explicitly undid.

**What I did *not* flag, because it is on the owner's NOT-faults lists**: coincident distinct joints; no diaphragm
on generated plates; members reading under 6 ft in the renderer (the renderer now prints `under 6ft: 0` itself);
`1 0 0 1` skewed panels (handled, `ReferenceModelShapeTests.cs`); 2″ sliver storeys in site models; unit
declaration across the corpus.

---

## 6. Dependencies

| System | Needed for | Off-LAN from MVE? |
|---|---|---|
| **`KorStandards` on `KOR-APP01\SQLEXPRESS`**, schema `analysis`, view `vw_RuleSetting` | **Every generation run and every test run.** Hard requirement, by design — no fallback | **No — VPN required.** `Microsoft.Data.SqlClient`, default connect timeout, `CommandTimeout = 15` (`RuleSettings.cs:111`) |
| `\\Kor-fs01\Projects\Projects` | The DXF drawings and the reference `.e2k` | **No — but inputs are copyable**; 17 MB for 31168 |
| ETABS (licensed desktop) | Opening the deliverable | Not installed on the dev machine; MVE will not have it |
| Poppler `pdftotext` (winget) | The publish document gate only | Local |
| .NET 8 runtime | `takeoff.exe` | Local (FS01 has none — self-contained publish needed there) |
| PowerShell + System.Drawing | `Render-E2kModel.ps1` | **Local — fully offline** |
| SQL login `opportunities_app` | Reading the rules | See §5.4 |

No Graph, no SharePoint, no Deltek, no AI provider, no HTTP service. The dependency surface is small and the only
blocking one is SQL.

---

## 7. Test reality

Project: `Kor.Operations.EngineeringTools.Core.Tests` — 47 `.cs`, **8,645 lines**, against a Core project of 69
`.cs` / **16,819 lines**, of which the DXF→ETABS engine proper is **18 files / 7,270 lines** `[RUN: wc]`. *(The
task brief's "69 .cs, 16,819 LOC" describes the whole Core project, not the `Dxf` folder.)* Statically:
**354 `[Fact]` + 38 `[Theory]` with 89 `[InlineData]` rows**.

**I ran it** `[RUN]` — `dotnet test Kor.Operations.EngineeringTools.Core.Tests -c Debug -nologo`, single project,
share reachable, `KorStandards` reachable:

```
Failed!  - Failed: 3, Passed: 480, Skipped: 0, Total: 483, Duration: 13 m 22 s
```

**The suite is RED at HEAD, and the reported figure of ~432 tests is wrong — it is 483.** All three failures are
in `ModelQuestionnaireTests`, and all three are the *tests* being stale, not the product:

- `TheFrontPageCarriesOnlyWhatSheHasToRead` — `ModelQuestionnaireTests.cs:603`,
  *"There isn't a worksheet named 'Rules in force'"*. The sheet is only written when rules were loaded
  (`ModelQuestionnaire.cs:517`, `if (report.RulesApplied.Count == 0) return;`) and the test builds a report with
  none.
- `QuestionsWorkbookCarriesHiddenRuleMetadata` — `:32`, expects `corner-limbs-vs-stocky-pier` on the *Questions*
  sheet; commit `72c1a2ca` marked it `ForTheRecord = true` (`ModelQuestionnaire.cs:125`) and moved it off the
  front page.
- `ALayerNameAnswerImportsAsTextRatherThanBeingSearchedForDigits` — `:551`, same cause for the
  `dxf.wall-layer-patterns` row (`ModelQuestionnaire.cs:441-444`).

`ModelQuestionnaireTests.cs` was *edited* in `72c1a2ca` and left failing, which corroborates
`CODEX-ETABS-AUDIT-7.txt` §3: the suite has not been run at this commit. Nothing here indicates a broken
generator — a fresh 31168 run produced the correct three-sheet workbook with all 35 keys `[RUN]`.

**"Skipped: 0" is misleading.** These tests do not use xUnit `Skip`; they return early and *pass* when the share
or DB is missing. See below.

**What is actually covered.** `DxfToEtabsTests` (49) and `PlanGeometryTests` (25) are real unit tests on small
shapes. The valuable ones are the audit tests: `ModelCoverageTests` reconciles the model against **raw DXF
segments in both directions** plus a no-empty-storey-between-populated check; `ModelIntegrityTests` catches
doubling; `GeneratedModel` carries hard ratchets (`LangaraLostCeiling = 7`, `WestFirstLostCeiling = 26`,
`MissizedCeiling = 0` both jobs). That is the four-fault-class apparatus and it is genuinely good.

**Where coverage is theatre.** Every test that touches a real building — `LiveProjectBaselineTests`,
`ModelCoverageTests`, `PortfolioRuleTests` — returns `null` and **passes silently** when the share is unreachable
(`GeneratedModel.cs:65`, `LiveProjectBaselineTests.cs:93`). Off the LAN the suite is green while proving nothing
about either building. `PortfolioRuleTests` additionally needs `KOR_PORTFOLIO_CHECK=1`, a variable set by nothing
in the repo — so the portfolio measurement that corrected the 36″ wall cap and the 96″ column cap runs only when
a human remembers. And the whole project requires `KorStandards` reachable (`RequireRuleSettings = true`
everywhere), so a green run on a disconnected machine is not achievable at all — B1 in PLAN-AND-GAPS is accurate.

---

## 8. Demo risk

Ranked, worst first.

1. **A live run needs VPN to `KOR-APP01`.** There is no `--offline` and no built-in fallback; `RequireRuleSettings`
   is hardcoded `true` in the CLI. If the VPN is flaky at MVE, the headline demo dies with a SQL error. `[READ]`
2. **Someone hands MVE the one-pager and it is an Edge "File not found" page.** §5.2. It is a committed, tracked
   deliverable, and the publish script copies it into job folders. `[RUN]`
3. **"Can it read *our* drawings?" — almost certainly not without being told the layer names.** KOR's defaults are
   `WALL`, `_COL`, `SLABEDG`. `AgnosticismTests.cs:16-22` documents the exact trap against a US National CAD
   Standard set: `WALL` matches `S-CONC-WALL-NEW` by luck, `_COL` misses `S-COLS`, `SLABEDG` misses
   `S-SLAB-EDGE` — "the model comes back with walls and no columns or floors, which looks like a building rather
   than like a failure." `--wall-layers/--column-layers/--slab-layers` exist but have **never been run against a
   foreign set**. Do not invite an on-the-spot test with an MVE DXF. `[READ]`
4. **"Has an engineer opened one of these in ETABS?"** The honest answer is no (PLAN-AND-GAPS C1), and ETABS is
   not even installed on the dev machine. If the technical lead asks, the fallback is the renderer PNG — which is
   good, but it is our renderer, not ETABS. `[RUN]`
5. **"How many buildings has it done?" — two, and both were used to tune it.** `CODEX-ETABS-AUDIT-7.txt` calls
   this the single largest gap. Six other engineer-authored models read cleanly but have no drawings.
6. **Opening the job folder mid-demo shows stale artifacts** — 31138's workbook missing its *Rules in force*
   sheet, 31168 missing its summary PDF, both reports listing 31 rules against 32 today. §5.3. `[RUN]`
7. **Looks-unfinished:** `docs/DxfToEtabs.md` (08-07) still documents the CLI without `--rules-db`, `--questions`
   or the layer overrides, and presents `JBP_V-WALL`/`JBP_V_COL`/`JBP_C_SLABEDG` as fixed constants — the exact
   thing the 08-15 work removed. Anyone reading the repo docs gets a week-old picture.
8. **A round column drawn as a `CIRCLE` entity is not read at all** (`DxfPlanReader.cs:38` lists `CIRCLE` among
   unsupported). It *is* reported, so it is honest — but on an unfamiliar drawing set it is a visible miss.

---

## 9. To-do register

| Item | Size | Tag | Why it matters |
|---|---|---|---|
| Re-render `docs/KOR-DxfToEtabs-onepager-web.pdf` and verify with `pdftotext`, not by opening it | S | `BEFORE-DEMO` | The current file is a browser 404 page and the publish script ships it |
| Point the one-pager count gate at the shipped PDF, as the dossier gate already does (`Publish-EtabsModel.ps1:435`) | S | `BEFORE-DEMO` | Otherwise the same class of defect recurs unseen |
| Decide and rehearse the off-LAN story: VPN, or pre-generated outputs + renderer PNG | S | `BEFORE-DEMO` | A live run at MVE is impossible without SQL reachability |
| Re-publish 31168 and 31138 from HEAD (owner runs it — it writes to the share) | S | `BEFORE-DEMO` | Job folders are 5 commits stale; 31168 has no summary PDF |
| Pass `RequiredRuleKeys` instead of `builtIn.Keys` at `DxfToEtabsService.cs:350` | S | `BEFORE-DEMO` | Closes the one hole in the "no fallback value" guarantee, and it is a one-line answer to the obvious question |
| Fix the three failing `ModelQuestionnaireTests` left red by `72c1a2ca` | S | `BEFORE-DEMO` | "All tests green" is currently not true; see §7 |
| Rehearse the exact 31168 command + renderer on the demo machine, timed | S | `BEFORE-DEMO` | 50.7 s + 9.2 s is a good number; know it cold |
| Move the standards connection string off a persisted user env var / stop reusing `opportunities_app` | M | `SOON` | Plaintext SQL password readable by anything running as the user |
| Put one unfamiliar building through end to end (needs drawings, and the Revit link-hiding step) | L | `SOON` | The largest real gap; two buildings are not evidence about a third |
| Have an engineer import a generated `.e2k` into ETABS and sign off | M | `SOON` | The largest untested surface (PLAN-AND-GAPS C1) |
| Schedule `Measure-EtabsCorpusRules.ps1` / `KOR_PORTFOLIO_CHECK` on FS01 | M | `SOON` | Nothing currently notices a rule drifting from the portfolio (L2) |
| Make the seven keyless DECIDED workbook rows (`C1 F2 M1 M2 O1 P1 S2`) learnable | L | `LATER` | Answering them today changes nothing in the next model |
| Tighten `LiveProjectBaselineTests` tolerance below 10 %, or delete it in favour of the ratchets | S | `LATER` | ±112 walls on 31168 is not a regression test |
| Make the publish gate fail loudly when `pdftotext` is absent | S | `LATER` | Silent no-op on any machine without winget Poppler |
| Read `CIRCLE` entities, or say in the report that a circle-drawn column was skipped by name | M | `LATER` | Matters only for foreign drawing sets |

---

## 10. Verdict

**Demo-able with care, and it is the strongest engineering artifact in the suite.** The engine is genuinely clean
— zero TODOs, zero `NotImplementedException`, zero empty catches, no hardcoded job numbers or paths outside
comments, 35 rules in a database rather than constants, and a report that volunteers what it could not do rather
than hiding it. A full 63-storey run reproduces its recorded baseline exactly in **50.7 seconds**, and the
renderer turns the result into a four-panel drawing in **9.2 seconds** without ETABS. Show 31168, pre-generated or
live over VPN, and finish on the render and the "what I could not do" report — the honesty is the selling point.

The single most important thing to fix is the **off-LAN dependency decision**: `RequireRuleSettings` is hardcoded
`true`, so with no VPN there is no run, and everything else in the demo hangs off that. Right behind it, re-render
the one-pager PDF — it is currently an Edge error page committed to the repo and wired into the publish path. Keep
two questions off the table by pre-empting them honestly: nobody has opened one of these in ETABS yet, and it has
only ever built the two buildings it was developed against.
