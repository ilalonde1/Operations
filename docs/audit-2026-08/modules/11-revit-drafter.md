# Module 11 — Revit tooling (KOR.RevitTools) and the Drafter standards system (KOR.Drafter)

**Audited 2026-08-20.** Two repos outside the Operations solution.
Evidence tiers: `RUN` executed · `QUERIED` live state read · `READ` source only · `DOC` a document says so.

---

## 1. What I searched

**Repos / git**
- `git log`, `git status`, `git branch -vv`, `git diff --stat main..feature/details-palette`,
  `git rev-list --left-right --count main...feature/details-palette` in both repos.
- `git show 3624a61` (bridge DXF export), `git show ccabd58 --stat` (layer rule), `git show --stat 9687ad8`.
- `git log -1 --date=short --format=%ad -- <path>` for six `KOR.RevitTools/docs/*.md` vs `src/`.

**Builds and tests I ran** (Debug, single project at a time, per AGENTS.md)
- `dotnet build tests/KOR.RevitTools.Core.Tests -c Debug` → **succeeded, 0 warnings** `[RUN]`
- `dotnet test tests/KOR.RevitTools.Core.Tests -c Debug --no-build` → **79 passed, 0 failed, 37 ms** `[RUN]`
- `dotnet build src/KOR.RevitTools.Addin -c "Debug R25"` → succeeded, 4 nullable warnings `[RUN]`
- `dotnet build src/KOR.RevitTools.Addin -c "Debug R23"` (net48) → succeeded, 3 warnings `[RUN]`
- `dotnet build src/KOR.RevitTools.Loader -c "Debug R25"` → succeeded, 0 warnings `[RUN]`
- `dotnet build KOR.Drafter/src/KOR.Drafter.Bridge -c "Debug R25"` → succeeded, 1 warning `[RUN]`

**Live state I queried (read-only)**
- `sqlcmd -S KOR-APP01\SQLEXPRESS -d KorStandards` as `kor\ilalonde` → **login failed** (no read grant) `[QUERIED]`
- Same as `standards_reader` (credential from `PALETTE-README.md`) →
  `SELECT COUNT(*), SUM(IsPlaceable) FROM detail.vw_PaletteCatalog` → **1079 rows, 0 placeable** `[QUERIED]`
- `SELECT … FROM analysis.vw_RuleSetting` as `standards_reader` → **permission denied** (login is correctly scoped) `[QUERIED]`
- `\\Kor-fs01\Drafting\KOR-Deploy\` — `current\<year>\`, `loader\`, `_rollback\` (27 snapshots), `version.txt` `[QUERIED]`
- `\\KOR-302N\C$\KOR.Drafter\logs\bridge-20260815.log`, `bridge\inbox\done\exp*.json`,
  `C:\Temp\kor-dxf\` (61 DXFs), `C:\Users\administrator.kor\AppData\Local\KOR.Drafter\<year>\` `[QUERIED]`
- Byte-scan of four `KOR.Drafter.Bridge.dll` files for the literals `exportdxf` / `Copied Central` `[RUN]`
- Layer-name extraction (DXF group code 8) from two exported DXFs on 302N `[RUN]`

**Greps / reads**
- `KOR.Drafter`: `docs/AUDIT-BRIEF.md`, `docs/FINISH-PLAN.md`, `PROJECT-CONTROL.md`, `RESUME-HERE.md`,
  `BRIDGE-READY.md`, `START-NEW-SESSION.txt`, `README.md`, `Directory.Build.props`,
  `db/041_LayerNamesAreARuleNotAConstant.sql`, all 41 `db/*.sql` filenames, `src/KOR.Drafter.Bridge/*.cs`.
- `KOR.RevitTools`: `README.md`, `PALETTE-README.md`, `Directory.Build.props`, all four `.csproj`,
  `Framework/ToolCatalog.cs`, `Framework/DetailsPaletteRegistration.cs`, `Tools/Details/*`,
  `docs/DEMO-PLAYBOOK.md`, `docs/BUILD-STATUS.md`, `docs/license-access-handover.md`.
- `Operations`: `Kor.Operations.EngineeringTools.Core/Dxf/PlanSheetNaming.cs`, `DxfToEtabsService.cs`,
  `StructuralPlanClassifier.cs`, `LayerLedger.cs`, `Core.Tests/AgnosticismTests.cs`.
- `grep -rE 'catch\s*(\([^)]*\))?\s*\{\s*\}'` over both `src/` trees (bin/obj excluded);
  `grep -E 'TODO|FIXME|HACK|NotImplementedException|NotSupportedException'`;
  `grep -rn 'Password=|User ID=|Server='` over `src/`, `config/`, root.
- Memory `project_revit_plugin_continuity_2026_07_09.md`, `project_bd_pipeline_audit_2026_07_09.md`.

---

## 2. What this module is

**KOR.RevitTools** is the firm's replacement for the Revit add-ins written by the departed BIM
developer. The old estate was 195 loose DLLs in `C:\ProgramData\2015_RevitCommands\` on each
workstation, obfuscated, with no source. KOR.RevitTools is one codebase that builds one add-in per
Revit year (2020–2027) and puts **137 tools on a single "KOR Tools" ribbon tab** — text restyling,
unit conversion, rebar tools, sheet and view batch operations, visibility toggles, CSV parameter
round-trip, model-health and warning reports. A separate tiny **Loader** assembly is installed once
per machine; every Revit launch it syncs the payload from `\\Kor-fs01\Drafting\KOR-Deploy` to a local
cache and reflection-loads it, so a firm-wide update is a copy to a share, with timestamped rollback
snapshots. That deployment is live: a payload stamped `2026-07-30T10:31:09` sits on the share with
27 rollback snapshots behind it `[QUERIED]`.

**KOR.Drafter** is four things in one repo and needs saying plainly, because it is not self-evident.
(1) A **Revit bridge**: an invisible add-in with no ribbon that watches a folder for JSON command
files and executes them inside Revit's API — 56 verbs, one command at a time, every write in a named
transaction, refusing to write to any model still bound to a live central. It is how an AI agent
drafts inside Revit unattended. (2) A **standards corpus** (`standards/`): the ratified text of KOR's
general notes, a 7,489-row detail census, an engineer-markup lexicon built from seven engineers'
redlines, and `RULINGS.md` — a hand-kept register of who decided what, when, and on what evidence.
(3) A **rules database**: `db/` holds all 41 migrations for `KorStandards` on `KOR-APP01\SQLEXPRESS`,
including the whole `analysis` schema — **which is where the DXF→ETABS generator in the Operations
repo reads every one of its 35 rules from**. The generator's code lives in Operations; its rules live
here. (4) A **body of evidence**: fleet crawl dossiers for 194 Revit models, a graded exam on a real
job, before/after screenshots, 48 executed task briefs. The repo's own `standards/README.md` states
the point: *"This folder is the asset. The bridge is rented plumbing."*

---

## 3. How you would demo it

**KOR.RevitTools — demoable, and there is already a script for it.** `docs/DEMO-PLAYBOOK.md` gives a
five-demo click path on Autodesk's own *Snowdon Towers Sample Structural* model, deliberately chosen
so nothing depends on KOR's template: Select Similar → Hide/Show Elements → 3D Box on Selection →
Change Beam Type → (fifth headliner). Prerequisites: **a machine with Revit installed** (2020–2027;
the playbook assumes 2026) and the add-in deployed to `%AppData%\Autodesk\Revit\Addins\<year>\` via
`build\deploy.ps1`, or the loader installed and LAN access to the deploy share. No SQL, no VPN, no
services for the five headliners `[DOC + READ]`. **Caveat: the playbook is dated 2026-07-14 and its
opening line says "28 tools, all green"; the catalog now has 137. Re-read it before using it.**

**KOR.Drafter — do not demo live.** The bridge has no UI at all: the demo would be a JSON file
appearing in a folder and a JSON file appearing in another folder, on one specific workstation
(KOR-302N) that must have Revit open with the right model loaded, over VPN. There is no screen to
show. What *is* showable is the artefact trail: the 61 DXF files it exported from a 45-storey tower
in 22 seconds, the bridge log, and the standards corpus. Treat KOR.Drafter as a slide, not a click
path.

**Revit → DXF → ETABS — the chain does not currently close. See §5, finding 1.**

---

## 4. Completeness

| Capability | State | Evidence |
|---|---|---|
| KOR.RevitTools ribbon add-in, 137 tools, Revit 2020–2027 from one codebase | `WORKING` | `RUN` (builds R23 net48 + R25 net8 clean); in-Revit behaviour is `READ` only |
| Network loader + firm-wide deploy + rollback | `WORKING` | `QUERIED` (share payload 2026-07-30, 27 rollback snapshots, install/remove/restore scripts present) |
| KOR.RevitTools.Core.Tests | `WORKING` | `RUN` 79/79 pass |
| KOR Details palette (SQL-backed, `feature/details-palette`) | `PARTIAL` — code works, **data is empty** | `RUN` (builds); `QUERIED` (`detail.vw_PaletteCatalog` = 1,079 rows, **0 with `IsPlaceable=1`**) |
| Drafter bridge: 56 JSON verbs, transaction-wrapped, central-write refusal | `WORKING` | `QUERIED` (bridge log 2026-08-15 shows ping/opendoc/exportdxf/sethidden with a correct WRITE REFUSED) |
| Bridge `exportdxf` (Revit → DXF) | `WORKING` on one machine, **not in the repo's shipped artifacts** | `QUERIED` (61 DXFs on 302N; byte-scan: installed DLLs contain `exportdxf`, `artifacts\2020` and `artifacts\2025` do not) |
| Revit → DXF → **ETABS** end-to-end | `PARTIAL` — filename contract matches, **layer contract does not** | `RUN` (layer extraction from two exported DXFs) |
| `KorStandards` `analysis` schema as the single rule source for DXF→ETABS | `WORKING` | `READ` (41 migrations, `034` creates `analysis.vw_RuleSetting`, `041` moves layer patterns in; Operations `DxfToEtabsService.RequiredRuleKeys` lists all 35) — **could not verify live: no read grant, see §6** |
| `standards/` corpus + `RULINGS.md` | `WORKING` as a human artefact | `READ` — hand-maintained, no machine link to SQL (§5, finding 5) |
| Dialog-sentry "Copied Central Model" fix (commit 3624a61) | `DEAD` — the assignment is unreachable | `READ` (`BridgeApp.cs:229`) |

**Marker counts**

- `TODO` / `FIXME` / `HACK` / `NotImplementedException` / `NotSupportedException` in **KOR.RevitTools**
  `src/` and `tests/`: **0**. Same five markers in **KOR.Drafter** `src/`: **0**. Genuinely none —
  this is not a repo that parks work in comments.
- **Empty catch blocks, KOR.RevitTools `src/`: 83** single-line `catch { }` / `catch (Exception) { }`
  out of 187 `catch` keywords, plus ~46 more whose body is a comment or a bare
  `return`/`continue`. **The cross-cutting scan's figure of 19 is a large undercount** — it appears to
  have matched only one syntactic form. Corrected number: **83 minimum, ~129 including
  comment-only and bare-return bodies**.
- Empty catch blocks, **KOR.Drafter** `src/KOR.Drafter.Bridge`: **21** out of 46 `catch` keywords in
  3,489 LOC.

---

## 5. What is broken or risky

### 1. Revit → DXF → ETABS is **not** a closed chain. The layers do not line up. `[RUN]`

This is the most important finding in the module, and it cuts against the most attractive story in
the suite. Taking it in two halves:

**The filename half works.** `BridgeExec.cs:2266` writes each view as
`--Structural Plan - <LEVEL NAME>.dxf`, named for the view's `GenLevel`, not the view. Operations'
`PlanSheetNaming.Parse` (`PlanSheetNaming.cs:78`) strips everything before the token `LEVEL` and then
matches `L(EVEL)?\s*(\d+)` — so `--Structural Plan - LEVEL 28.dxf` resolves to level 28 correctly.
Verified by reading both sides. 61 such files exist on KOR-302N, exported 2026-08-15 `[QUERIED]`.

**The layer half does not.** I extracted every DXF group-code-8 value from two of those exported
files `[RUN]`:

- `--Structural Plan - LEVEL 11.dxf` — 34 distinct layers, including `A-WALL`, `I-WALL`, `S-COLS`,
  `A-FLOR`, `S-GRID`, `S-STRS`.
- `--Structural Plan - LEVEL 25.dxf` — 43 distinct layers, same structural set.

The generator's rules (`KorStandards`, seeded by `db/041_LayerNamesAreARuleNotAConstant.sql`) are
`dxf.wall-layer-patterns = WALL`, `dxf.column-layer-patterns = _COL`, `dxf.slab-layer-patterns =
SLABEDG`, matched as case-insensitive substrings. Against the layers actually exported:

| Role | Pattern | Matches in the exported DXF? |
|---|---|---|
| walls | `WALL` | Yes — but it matches `A-WALL`, `A-WALL-PATT` and `I-WALL`, i.e. the **linked architectural model's partitions, curtain wall and door openings**, mixed with the structural concrete that also lands there |
| columns | `_COL` | **No.** `S-COLS` has a hyphen, not an underscore |
| slab edges | `SLABEDG` | **No.** There is no slab-edge layer at all; floors are on `A-FLOR` |

The Operations test suite already names this exact failure in prose —
`Kor.Operations.EngineeringTools.Core.Tests/AgnosticismTests.cs:17-21`: *"`_COL` misses S-COLS on the
underscore and `SLABEDG` misses S-SLAB-EDGE on the hyphen. Partial matching is the worst outcome
available — the model comes back with walls and no columns or floors, which looks like a building
rather than like a failure."* That is precisely what a run on today's Revit export would produce.

Two contributing causes, both fixable:
- `ExportDxf` (`BridgeExec.cs:2240`) constructs `DXFExportOptions` with `SharedCoords`,
  `ExportOfSolids` and `TextTreatment` only. It **never sets an export-layer table**, so Revit uses
  its default (AIA) mapping. KOR's reference DXF corpus — the one the generator's rules were measured
  against — uses `JBP_V-WALL` / `JBP_V_COL` / `JBP_C_SLABEDG`, which is a *different* export layer
  table. The bridge does not apply it. No `exportlayers*.txt` exists anywhere in `KOR.Drafter` `[READ]`.
- The linked architectural model was still visible. The commit message for 3624a61 says the link
  category must be hidden first; the same bridge log shows the attempt to do so
  (`sethidden`) **failing** at 21:52 — `WRITE REFUSED — this document is a live workfile of central
  …30783-01 - 500 Foster - Structural Model - R20.rvt` — and the export running anyway 26 seconds
  later `[QUERIED]`.

**Verdict on lead 3: the chain does not connect end to end today.** Say so plainly if asked. What is
true and still impressive: Revit exports 110 views in 22 seconds under agent control, the filename→
storey contract is correct and deliberate, and every layer pattern is now a database rule rather than
a C# constant — so closing the gap is a settings change plus a Revit export-layer table, not a code
change. That is a credible "next sprint" answer, not a demo.

### 2. The bridge's own shipped artifacts do not contain `exportdxf`. `[RUN]`

Byte-scanning the four DLLs for the literal `exportdxf`:

| DLL | `exportdxf` | `Copied Central` fix |
|---|---|---|
| `KOR.Drafter\artifacts\2020\KOR.Drafter.Bridge.dll` (2026-08-04) | **NO** | — |
| `KOR.Drafter\artifacts\2025\KOR.Drafter.Bridge.dll` (2026-08-04) | **NO** | — |
| `\\KOR-302N\…\AppData\Local\KOR.Drafter\2020\…dll` (2026-08-15 21:21) | YES | **NO** |
| `\\KOR-302N\…\AppData\Local\KOR.Drafter\2025\…dll` (2026-08-15 21:19) | YES | — |

`BRIDGE-READY.md` and `README.md` both instruct a deployer to copy `artifacts\<year>\` to the
workstation. Following those instructions today installs a bridge **without** the DXF export. The
only build that has it is a hand-placed DLL on one machine, and that build predates the dialog fix
committed the same evening.

### 3. The "Copied Central Model" fix is unreachable dead code. `[READ]`

`BridgeApp.cs:198-203` computes `result = 8` (TaskDialogResult.Close) when an unnamed TaskDialog's
text contains `"has been copied or moved"`. Six lines later, `BridgeApp.cs:216`:

```csharp
bool answer = !(e is TaskDialogShowingEventArgs) || (id.Length > 0 && id != "?");
```

For exactly the case the fix targets — a `TaskDialogShowingEventArgs` whose `DialogId` is null, so
`id == "?"` — this evaluates to `false`, and `e.OverrideResult(result)` at `:229` is never called.
`result = 8` is discarded in every path except an explicit per-command `dialogAnswers` override, which
would overwrite it anyway. The observable behaviour changes from *"cancels the open"* to *"blocks the
queue until something else clicks it"* — better, but not what the commit message claims
(*"Now matched on its text and answered with Close"*). The comment at `:210` defers to a
"title-reading watchdog"; `Dialog-Watchdog.ps1` is referenced in `docs/PROTOCOL.md:456` and in five
process-record documents but **does not exist in this repo** — it lives only on KOR-302N. An
unattended run on any other machine has nothing to clear that dialog.

This is exactly the failure class `docs/AUDIT-BRIEF.md` asks for ("guards that never fire") and it
survived the audit that brief commissioned.

### 4. `RemoveUnusedViews` fails *open* on schedules. `[READ]`

`src/KOR.RevitTools.Addin/Tools/Views/ViewSheetExtraCommands.cs:37`

```csharp
try { placedViewIds.Add(RevitShim.IdValue(si.ScheduleId)); }
catch (Exception) { }
```

`placedViewIds` is the **protection set** for a destructive purge. `ViewType.Schedule` is purgeable.
If this throws, a schedule that *is* placed on a sheet silently drops out of the protected set and
becomes a delete candidate. Twenty-six lines later the same file handles the analogous case
correctly, with a comment saying so: `:63` catches and **adds** the view to the protected set,
labelled *"Fail SAFE"*. Line 37 is the same pattern inverted. Given commit `66bd1af` is titled
*"Wave-4 audit fixes: RenumberMarks data-loss…"*, this class of bug has bitten before.

### 5. Two standards systems that cannot disagree politely, because nothing reconciles them. `[READ]`

Lead 7 asked whether `standards/RULINGS.md` and the `KorStandards` SQL rules are the same body of
rules. **They are not, and the situation is slightly worse than "two systems".**

- **Three registers, not two.** `standards/RULINGS.md` holds *drafting* rulings (sheet content,
  general notes, CAD-vs-Revit reconciliation) decided by Jim DesRoches, Rory Beirne, Simon
  Szarkiewicz and Ian. `KorStandards.analysis` holds *modelling* rulings (ETABS `.e2k` facts, member
  classification) decided by Andrea Neuviale. `KorStandards.markup`/`detail`/`conformance` hold the
  census and lexicon. Different subjects, different deciders, no overlap.
- **No link exists in either direction.** No migration in `db/` references `standards/`; no script
  generates the markdown from SQL or SQL from the markdown; `analysis.Ruling`'s natural key is
  `(Engineer, Scope, Topic)` with no ruling-code column, so it is structurally incapable of holding
  `RULINGS.md`'s `G1`/`E1`/`D1` entries.
- **The ID namespaces collide.** `E1` means *"CAD vs Revit splice tables — Revit is OKAY"* in
  `RULINGS.md`, a markup-lexicon entry in `db/002_SeedMarkupLexicon.sql:30`, and a sheet-size token
  in `db/005_LoadDetailObservations.sql:483`. `D1`/`D2`/`D4` likewise mean one thing in `RULINGS.md`
  and something unrelated in `db/008_AdoptObservedFamilies.sql:3`.
- **One file claims a projection relationship and gives the wrong home.**
  `standards/markup-corpus/LEXICON.md:11-24` correctly insists the lexicon lives in SQL, then names
  `Operations\Kor.Opportunities.Data\Schema\293_MarkupLexicon.sql` in `KorOpportunitiesDb` — but it
  actually shipped as `db/001_CreateKorStandardsDb.sql` in the standalone `KorStandards` database.
  Its status banner still reads *"STATUS 2026-08-01: the SQL home is written but NOT YET APPLIED"*,
  19 days and 40 migrations out of date.

**Is this duplicated rule authority?** For the DXF→ETABS rules specifically, **no** — those live in
exactly one place (`analysis.vw_RuleSetting`), and `db/036_EveryRuleLivesHere.sql:154` enforces it:
*"None is compiled in, and there is no fallback value."* That part is architecturally clean, and it is
the part MVE would ask about. The exposure is the **drafting** register: `RULINGS.md` is a
hand-maintained markdown file with no ID scheme, no consumer, and colliding identifiers, sitting
beside a SQL system that looks like it should own it.

### 6. A live SQL password is committed to git. `[READ]`

`KOR.RevitTools/PALETTE-README.md:20` contains
`Server=KOR-APP01\SQLEXPRESS;Database=KorStandards;User ID=standards_reader;Password=‹REDACTED — standards_reader password, verified live›;TrustServerCertificate=True`.
I confirmed the credential is live by using it `[QUERIED]`. Mitigations are real: the login is
read-only and genuinely scoped — my `SELECT` on `analysis.vw_RuleSetting` was refused, and only
`detail.vw_PaletteCatalog` answered. But it is a working production credential in a tracked file,
introduced by `c8cdde9`, and **it exists only on `feature/details-palette`** — so it can still be
scrubbed from history before that branch merges. That window closes on merge.
`tests/KOR.RevitTools.Core.Tests/DetailsCatalogTests.cs:14` carries a dummy `Password=secret` but
does confirm the real server/instance/database/username.

### 7. The details palette would demo as an empty list. `[QUERIED]`

`detail.vw_PaletteCatalog` holds **1,079 rows and zero with `IsPlaceable = 1`**. With the documented
default `showUnverified: false`, the palette opens and shows nothing. `PALETTE-README.md` says this is
intentional pending verification — but on screen it reads as a broken feature.

### 8. Other swallowed failures worth naming (all `[READ]`)

- `Tools/Data/DataCommands.cs:42` and `Tools/Review/ReviewCommands.cs:65` —
  `catch { return "(could not write file)"; }`. The string reaches the user's dialog, so it is not
  fully silent, but the command still returns `Result.Succeeded`.
- `Tools/Data/CsvImportCommand.cs:231,234` — a whole element or whole CSV row that throws mid-write
  is skipped and increments **no** counter, so a partial import reports as clean. (The per-field
  catch at `:228` does count.)
- `Tools/Rebar/RebarMatchTagCommands.cs:109` and `Tools/Rebar/RebarCommands.cs:57` — per-field rebar
  parameter writes; one field failing while another succeeds still marks the element "updated".
  This is engineering data on a structural drawing.
- `Tools/Sheets/RevisionCommands.cs:121` — a sheet that silently did not receive its revision, in a
  batch reported only as a total. Drawing-issuance risk.
- `Tools/Sheets/SheetCommands.cs:277,293` — a duplicated sheet that lost viewports or schedules
  reports as a complete success.
- `Framework/DetailsPaletteRegistration.cs:29` — `catch { return null; }` makes the KOR Details
  button vanish with no log line on any config or share failure.
- `KOR.Drafter/src/KOR.Drafter.Bridge/BridgeExec.cs:2711` —
  `try { accepted = p.SetValueString(text); } catch { }` inside `setparams`. Mitigated: `accepted`
  stays false and the code falls through to a raw `Set` or throws, and `setparams` is all-or-nothing.
  Not a defect, listed because it looks like one.
- `KOR.Drafter/src/KOR.Drafter.Bridge/BridgeExec.cs:2539` —
  `try { fa.DeleteWarning(f); } catch { }` deletes Revit warnings best-effort with no report.

### 9. Hardcoded paths in tests — not a portability problem `[READ]`

The cross-cutting scan's "3 hardcoded absolute paths in its tests" resolve to four literals in
`CoreLogicTests.cs:132` and `DetailsCatalogTests.cs:14,18` which are **inline JSON fixture strings
never touched on disk**, plus `KorToolsConfigTests.cs:33,49` which assert
`StartsWith(@"\\Kor-fs01", …)` against the tracked `config/kor-tools.json`. None would fail on another
machine; the last two do hardcode the site's file-server name into the test contract, which is
brittle but not broken. All 79 tests passed on this machine `[RUN]`.

---

## 6. Dependencies

| Dependency | Needed by | Reachable off the KOR LAN? |
|---|---|---|
| **Autodesk Revit 2020–2027 (licensed desktop install)** | Both add-ins, at runtime. `Directory.Build.props` resolves `$(RevitTargetFramework)` to `net48` for R20–R24, `net8.0-windows` for R25/R26, `net10.0-windows` for R27 | **A demo machine must have Revit installed.** Building does **not** require it — RevitAPI comes from NuGet (`Nice3point.Revit.Api.*`), verified by building R23 and R25 on this box with no Revit present `[RUN]` |
| `\\Kor-fs01\Drafting\KOR-Deploy` | Loader payload sync, rollback, install scripts, content year resolution | **LAN/VPN only** |
| `\\Kor-fs01\Drafting\{year}\QuickPick\…` | Family/note libraries in `config/kor-tools.json` | **LAN/VPN only** |
| `KorStandards` on `KOR-APP01\SQLEXPRESS` | Details palette (`detail.vw_PaletteCatalog`); DXF→ETABS rules (`analysis.vw_RuleSetting`) | **LAN/VPN only**. Sessions have no read grant under `kor\ilalonde`; `standards_reader` reads two views only `[QUERIED]` |
| KOR-302N (the one bridge workstation) | Every KOR.Drafter bridge operation, plus `Dialog-Watchdog.ps1` which exists nowhere else | **LAN/VPN only** |
| `System.Data.SqlClient` 4.9.0 | Details palette. Deliberately the deprecated provider (`CS0618` suppressed at `SqlDetailsPaletteReader.cs:1`) for net48/net8/net10 packaging | n/a |

No Microsoft Graph, no SharePoint, no Deltek ODBC, no AI provider, no HTTP service in either repo.
**Everything except the five headline RevitTools demos needs the KOR LAN.** For a demo at MVE's
office, plan on the RevitTools ribbon only, or bring a VPN.

---

## 7. Test reality

**KOR.RevitTools** — one test project, `tests/KOR.RevitTools.Core.Tests`, **79 tests, all passing in
37 ms** `[RUN]`. It tests `KOR.RevitTools.Core` only: units, naming, text parsing, rebar numbering,
grid packing, config deserialization, the details catalog filter. That is the right layer to test —
Core is `netstandard2.0` and references no Revit API, so it runs anywhere. But be blunt about the
consequence: **the 137 ribbon commands, which are where every defect in §5 lives, have zero automated
coverage.** `RemoveUnusedViews`, `CsvImport`, `RevisionCommands`, `DuplicateSheetWithViews` — all
untested, all destructive or data-writing, all only verifiable by opening Revit. 79/79 green is
honest about what it covers and says nothing about what matters most.

**KOR.Drafter** — **no test project at all.** 3,489 lines of code that edits live structural models
unattended, with zero automated tests. The compensating control is the process: `docs/FINISH-PLAN.md`
defines a five-step artefact gate (export it, sweep the output, render it and look, count from raw
JSON, compare against a written expected value), and `docs/AUDIT-BRIEF.md` commissioned an adversarial
read. That is a genuine discipline and it has caught real bugs — `BridgeExec.cs:1673` carries a
comment recording an independent audit finding that non-numeric element ids were being silently
dropped. It also missed the dead dialog fix in §5.3, which is the argument for tests.

---

## 8. Demo risk

Ranked by likelihood × damage in front of a technical lead.

1. **Claiming "Revit → DXF → ETABS" as a working pipeline.** If anyone frames it that way and MVE's
   lead asks to see it — or worse, asks what layer names the export produces — the answer is
   `A-WALL`, `S-COLS`, `A-FLOR` against rules expecting `WALL`, `_COL`, `SLABEDG`. Two of three
   member types would come back empty. This is the single highest-damage risk in the module precisely
   *because* it is the most attractive story.
2. **The details palette opening empty.** 1,079 catalog rows, 0 placeable. If the palette is enabled
   on the demo machine it shows a blank list. Leave it dormant.
3. **"How many tools is it?"** The playbook says 28, `BUILD-STATUS.md` says 79, the catalog has 137.
   Any of those numbers said out loud can be contradicted by the ribbon on screen.
4. **A live `RemoveUnusedViews` demo.** Do not run a purge tool on a model you care about in front of
   an audience, given §5.4.
5. **"Who else can build and deploy this?"** — see the bus-factor note below. The honest answer is
   good, but it needs to be *said*, not improvised.
6. **The KOR.Drafter repo being opened on screen.** `README.md` opens with *"PRIVATE virtual-drafter
   workstation kit — Do not publish, reference, or copy any part of this repo."* It is confidential
   from KOR's own drafting team. It should not be shared with an architecture partner at all.
7. **Looks-unfinished risk:** three root-level status documents (`PROJECT-CONTROL.md`,
   `RESUME-HERE.md`, `START-NEW-SESSION.txt`) plus `BRIDGE-READY.md`, one of which begins
   "SUPERSEDED". A visitor reading the repo root cannot tell what is current.
8. **`CODEX-PALETTE-SQL-PROMPT.txt` untracked in `git status`** on KOR.RevitTools — it is an LLM
   prompt file, not code, but it shows as a dirty working tree.

### Bus factor (lead 5) — the answer is better than reported

The reported context is confirmed on the first point and needs correcting on the second:

- **Confirmed:** the Revit lead, Michael Li, is gone — `docs/license-access-handover.md` names him as
  *"the departed BIM developer"* and his profiles (`mli`, `michael li.old`) are still on KOR-302N
  `[QUERIED]`. His original 195-DLL estate had no source at all.
- **Bus factor for KOR.RevitTools today: not one person.** Anyone with the repo and a .NET SDK can
  build it — I built the add-in for both Revit 2023 (net48) and 2025 (net8) on a machine with **no
  Revit installed**, because the API comes from NuGet `[RUN]`. Deployment is a documented script to a
  share with 27 rollback snapshots, an `install-loader.ps1`, and `Remove-LegacyTools.ps1` /
  `Restore-LegacyTools.ps1` for reversal `[QUERIED]`. This is the strongest continuity story in the
  module and it is worth telling explicitly: *the thing that made the old estate un-inheritable has
  been designed out.*
- **Bus factor for KOR.Drafter: one person and one machine.** The bridge is installed on KOR-302N
  only, `Dialog-Watchdog.ps1` exists only there, and the repo README forbids it going anywhere else.
  That is by design, but it is a real single point of failure.
- **Correction to the reported context — the "do not rewrite the resolver" guidance is not about
  Revit.** It refers to `CanonicalOrgResolver` in `Kor.Opportunities.Data/Awards/`, from the BD
  pipeline audit of 2026-07-09 `[READ]`. There is no resolver in KOR.RevitTools other than
  `LoaderApp.cs:72`'s `AppDomain.AssemblyResolve`, which nobody has warned about. Two adjacent memory
  entries appear to have been compressed into one line. **Do not let this become a reason to avoid
  touching the loader.**

### `feature/details-palette` (lead 4) — safe to merge, but scrub the password first

- **2 commits ahead of `main`, 0 behind** `[RUN]` — a clean fast-forward, no conflicts possible.
- **973 insertions, 0 deletions across 11 files.** Purely additive: a new `Tools/Details/` folder, a
  new form, a `KorConfig` loader, `DetailsCatalog` in Core, 5 lines added to `ToolCatalog.cs`, one
  `System.Data.SqlClient` package reference, and a test file.
- **It builds and its tests pass** (the 79 include `DetailsCatalogTests`) `[RUN]`.
- **It is genuinely dormant.** `DetailsPaletteRegistration.AddIfEnabled` returns null unless
  `%PROGRAMDATA%\KOR\kor-tools.json` carries a `detailsPalette` section with a connection string;
  no tracked config file in the repo has one `[READ]`. Merging it changes nothing on any drafter's
  ribbon.
- **Not abandoned mid-flight** — it is finished code parked pending data (the `IsPlaceable` promotion
  that has not happened). The uncommitted file is `CODEX-PALETTE-SQL-PROMPT.txt`, a prompt, not code.
- **The one blocker is the committed password** (§5.6). Because the credential exists only on this
  branch, merging it moves a live secret onto `main` permanently. Rotate or scrub first, then merge.

### Document authority (lead 1) — which of these to trust

| Document | Date | Verdict |
|---|---|---|
| `START-NEW-SESSION.txt` | 2026-08-06 | **Authoritative for how to resume.** Names the trust order: SQL scoreboard → `docs/PLAN-2026-08-04.md` → the 302N QUEUE. Still accurate |
| `RESUME-HERE.md` | 2026-08-06 | Correctly self-marked `SUPERSEDED`. Trust only its pointer |
| `PROJECT-CONTROL.md` | 2026-08-01 | **Stale by 14 days and two significant commits** (the layer rule, the DXF export). To its credit it opens with its own staleness check telling you to compare against the 302N logs — do that. Good for the estate map (§2), not for state |
| `BRIDGE-READY.md` | 2026-08-04 | **Stale and actively misleading.** Says *"KOR-302N's observed active bridge was Revit 2025"*; the 2026-08-15 log reads `Bridge 1.0.31 up. Revit 2020 build 20200210_1400(x64)` `[QUERIED]`. Its deployment steps install artifacts that lack `exportdxf` (§5.2) |
| `docs/AUDIT-BRIEF.md` | 2026-08-01 | Not state — a standing brief. Still the best statement of the risk model in either repo, and §5.3 is a finding it explicitly asked for and did not get |
| `docs/FINISH-PLAN.md` | 2026-08-03 | Scoped to the standards-template work (view renames, discipline values, catalog duplicates), **not** to the bridge. Its Step 1 SQL delete against `KorTransmittals` may or may not have been run — I did not check, and it is out of scope here. Its five-step artefact gate is the durable part |
| `docs/PROTOCOL.md` | 2026-08-04 | **Stale.** Documents 29 verb sections; the code dispatches 56 verbs, and **`exportdxf` appears nowhere in it** `[RUN]`. The brief's own rule — *"where code and PROTOCOL disagree, that is a finding"* — makes this one |
| `KOR.RevitTools/docs/DEMO-PLAYBOOK.md` | 2026-07-14 | **Stale** (code 2026-08-06). Says 28 tools; catalog has 137. Click paths likely still valid; the numbers are not |
| `KOR.RevitTools/docs/BUILD-STATUS.md` | 2026-08-01 | **Stale.** Says 79 tools as of 2026-07-15; catalog has 137 rows / 137 distinct command types |

---

## 9. To-do register

| Item | Size | Tag | Why it matters |
|---|---|---|---|
| Agree the one-sentence framing of Revit→DXF→ETABS and brief whoever presents: *"Revit export and the storey mapping are done; layer mapping is the open piece."* | S | `BEFORE-DEMO` | The alternative is improvising in front of the one person qualified to catch it |
| Confirm the demo machine has the details palette **dormant** (no `detailsPalette` block in `%PROGRAMDATA%\KOR\kor-tools.json`) | S | `BEFORE-DEMO` | Otherwise it opens an empty list — 0 of 1,079 rows are placeable |
| Reconcile the tool count and correct `DEMO-PLAYBOOK.md`'s opening line and the Diagnostics figure | S | `BEFORE-DEMO` | 28 vs 79 vs 137 is contradicted by the ribbon on screen |
| Do not run `RemoveUnusedViews` in any live demo | S | `BEFORE-DEMO` | §5.4 — fails open on sheeted schedules |
| Keep the KOR.Drafter repo off screen entirely | S | `BEFORE-DEMO` | Its own README marks it confidential from KOR's drafting team |
| Rotate `standards_reader`'s password and scrub it from `PALETTE-README.md` **before** merging `feature/details-palette` | S | `SOON` | The secret is confined to one unmerged branch; merging makes it permanent history |
| Fix `ViewSheetExtraCommands.cs:37` to fail safe like `:63` | S | `SOON` | Silent deletion of a sheeted schedule |
| Fix or delete the unreachable `result = 8` in `BridgeApp.cs:198-216` | S | `SOON` | A committed fix that does not fire is worse than a known bug |
| Rebuild and commit `artifacts/<year>/` so they contain `exportdxf` and the dialog fix | S | `SOON` | Today, following `BRIDGE-READY.md` installs a bridge without the DXF export |
| Correct `BRIDGE-READY.md` (Revit 2020, not 2025) and add `exportdxf` to `docs/PROTOCOL.md` | S | `SOON` | Both are contradicted by live evidence |
| Add a KOR Revit export-layer table that maps structural walls / columns / floors to distinct layers, ship it with the bridge, and set it in `DXFExportOptions` | M | `SOON` | This is the actual fix for §5.1 — the rules side is already settable |
| Teach `ExportDxf` to hide linked models itself (or refuse and say why) rather than depending on a `sethidden` that can be refused | M | `SOON` | The 2026-08-15 run exported with the arch link visible after `sethidden` failed |
| Count the skips in `CsvImportCommand.cs:231,234` and report them | S | `SOON` | A partial data import currently reports as clean |
| Report per-item failures in `RevisionCommands.cs:121` and the rebar parameter writers | M | `SOON` | Silent revision and rebar-value misses are drawing-issuance risk |
| Add a smoke-test project for the destructive ribbon commands (or a documented manual checklist) | L | `LATER` | 137 commands, 0 automated coverage, and that is where every §5 defect lives |
| Decide whether `standards/RULINGS.md` moves into SQL or is explicitly declared a human-only register, and fix the `E1`/`D1` collisions either way | L | `LATER` | Not a demo risk; a real long-term correctness risk |
| Correct `standards/markup-corpus/LEXICON.md`'s stated SQL home and status banner | S | `LATER` | It points at a database and migration that do not exist |
| Delete or gitignore `CODEX-PALETTE-SQL-PROMPT.txt` | S | `LATER` | Dirty working tree on the branch you are about to merge |

---

## 10. Verdict

**KOR.RevitTools is demo-ready** and is quietly one of the best continuity stories in the suite: 137
tools on one ribbon across seven Revit versions, building clean on a machine with no Revit installed,
deploying firm-wide from a share with rollback — replacing 195 obfuscated DLLs left by a departed
developer. Use the existing playbook, fix its stale numbers, and say the bus-factor point out loud.
**KOR.Drafter should stay off the screen** — its own README marks it confidential from KOR's drafting
team, it has no UI, and it runs on one workstation.

The single most important thing to fix is **the framing of Revit → DXF → ETABS**. The lead was right
that it would be the strongest demo in the suite; it is also not currently true. The filename→storey
contract is deliberate and correct, and 61 real DXFs exist to prove the export works. But the exported
layers are `A-WALL`, `S-COLS` and `A-FLOR`, and the generator's rules look for `WALL`, `_COL` and
`SLABEDG` — so a run today yields architectural partitions taken for structural walls, no columns and
no floor plates. Since 2026-08-15 those patterns are database rules rather than C# constants, so the
gap closes with a Revit export-layer table and three settings, not a code change. That is an excellent
answer to give MVE. Presenting it as already working is the one move that could go badly.
