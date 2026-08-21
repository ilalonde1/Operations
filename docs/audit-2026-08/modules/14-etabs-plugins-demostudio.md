# Module audit — Two products the audit never saw

**PART A** — ETABS native plugins (`C:\VIsual Studio Projects\ETABS\Plugin Development\`)
**PART B** — DemoStudio (`C:\VIsual Studio Projects\App Demo Maker\`)

Audited 2026-08-21. Two independent products, two independent verdicts. Read them separately.

**Headline correction to the task brief.** Both halves were mis-scoped going in:

- Part A was framed as "3 projects, 17,688 lines, establish whether the OLD one is superseded". The
  three projects on this workstation are **one generation behind production**. The live plugin is an
  unversioned copy-fork on `\\Kor-fs01\Library`, rebuilt 2026-03-30 for ETABS 23, plus a **fourth,
  newer plugin** (`ETABS Reaction Tool`, 2026-08-05) whose source is not on this machine at all. The
  ETABS plugins are **not dead** — they are the most-used engineering software KOR has, and they are
  the *second half* of the DXF→ETABS story audited in module 10.
- Part B was framed as "1 commit in 90 days, most files March 2026 — is it worth anything?" It
  builds clean, starts, records, composes, publishes and zips a shareable package — **I did all of
  that end to end today** — and the reason it stopped is visible in the dates, not in a defect.

---
---

# PART A — ETABS native plugins

## A1. What I searched

**Prior art check first** (CLAUDE.md rule 1). Grepped the Operations repo for
`KorETABS|ETABS_Plugin|DemoStudio|App Demo Maker` across `*.md *.cs *.ps1 *.csproj` — three hits, all
inventory/scope lines, no prior analysis. Read `docs/audit-2026-08/SCOPE.md` (line 15 tiers both of
these "Tier 3 — inventory line only") and `00-INVENTORY.md:134-151`. Read
`docs/audit-2026-08/modules/10-dxf-to-etabs.md` in full before writing a word about overlap, and
`09-engineering-tools.md` headings. Nothing existed on either product.

**Local tree.** `find` over `ETABS/Plugin Development` (maxdepth 4). Read all `.csproj` files;
`KorTools/{cPlugin,fMain,Setup,Export,Cracking,PiersAndSpandrels,Process,RawData}.cs`;
`KorETABS/{ApiLayer,QuickDefine,QuickAssign,CrackingSet,PierLabelSet,MassProp,Units,Excel}.cs` heads;
`OLD/ETABS-Toolkit/ETABS_Plugin/{cPlugin,EtabsConnector,Form1.*}.cs`.

**Git.** `git log`, `git status`, `git remote -v` in all three repos.

**Greps.** `TODO|FIXME|HACK|XXX`; `NotImplementedException|NotSupportedException`; empty-catch regex
(`catch\s*(\([^)]*\))?\s*\{\s*\}`, via a Python walker, not shell); `C:\\|L:\\|\\\\Kor|@"[A-Z]:`;
`Convert.ToDouble(tb|Convert.ToInt32(tb`; `cracking|CrackingValues` across `Operations` **and**
`KOR.Drafter`; `PierLabel|PropName|"W"|"C"|"S"` across `Operations/…/Dxf/`.

**Machine state (`[RUN]`).** `Get-PSDrive`, `net use`, `Get-ChildItem 'C:\Program Files\Computers and
Structures'`, `Get-ChildItem HKCU:\Software\Computers and Structures`.

**Share (filtered, read-only, never recursive-broad).** `net view \\Kor-fs01`; `Test-Path` probe of
six shares for `03 Programs`; then depth-limited `Get-ChildItem` inside
`\\Kor-fs01\Library\03 Programs\CSI America (SAFE, ETABS, SAP2000, etc.)\ETABS - Standalone\Plugin
Development\` only. Read `Install Kor Tools ETABS 22.bat`, `Install Kor Tools ETABS 23.bat`,
`_install_helper.ps1`, `READ ME .txt`, `ETABS_Reaction_Tool\HOW TO INSTALL.txt`.

**Builds (`[RUN]`).** Copied sources to a scratchpad (never wrote to the share or the project tree),
repointed `HintPath`s at local copies of `ETABSv1.dll` / `KorETABS.dll`, and ran `dotnet build`:
- `KorETABS.csproj` (net472, as committed) — **fails, cannot be built on this machine at all**
- `KorETABS23.csproj` (live) — **0 errors, 9 warnings**
- `KorTools23.csproj` (live) — **0 errors, 1,224 warnings**
- `KorTools.csproj` (as committed in git) — **fails, two separate causes**

**Not done.** ETABS is not installed on this workstation, so no plugin was loaded or exercised inside
ETABS. Every behavioural claim below about what a command *does to a model* is `[READ]`.

## A2. What this module is

An ETABS model is not finished when the geometry is in it. Somebody still has to define the concrete
materials and the wall/slab/column sections to KOR's naming convention, set the seismic and wind load
combinations for the right Rd/Ro, build displacement-modified spectrum cases, assign stiffness
("cracking") modifiers to every shell and frame according to KOR's standard table for that load case
and model type, put a pier label on every wall and a spandrel label on every coupling beam so the
results come out as pier forces rather than a mesh of shells, and finally pull the result tables into
Excel. On a tower that is thousands of individual assignments, all of it mechanical, none of it
judgement. **Kor Tools is the plugin that does that half.** It installs into ETABS's Tools menu and
opens a tabbed window that sits on top of the live model, driving it through the CSI API
(`ETABSv1.dll`) — so it operates on whatever the engineer already has open, rather than producing a
file.

This matters more than its folder location suggests: it is the **complementary half of the DXF→ETABS
generator** in module 10. That tool deliberately stops at geometry — "no loads, diaphragms, stiffness
modifiers, section properties or design" — and Kor Tools is precisely loads, stiffness modifiers,
section properties and labels. Nobody scoping this audit connected the two. Kor Tools is also the
oldest continuously-used software KOR has: first commit **2020-11-16** by Adrian Crowder, carried
forward by Jeremy Atkinson and "achan", and rebuilt for each new ETABS release — 21 (2024-05), 22
(2024-07), 23 (**2026-03-30**). `[READ]` on function, `[QUERIED]` on dates and deployment.

`KorETABS` and `KorTools` are one product in two layers, not two products. `KorETABS.dll` is the
headless library: the API wrapper (`ApiLayer.cs`), unit conversion (`Units.cs`, 24 KB — ETABS's
"database units" are a genuine trap), and the domain operations (`QuickDefine`, `QuickAssign`,
`CrackingSet`, `PierLabelSet`, `MassProp`). `KorTools.dll` is the WinForms plugin that ETABS actually
loads; it references `KorETABS.dll` and contains only UI (`fMain.Designer.cs` alone is 188 KB).
Neither supersedes the other and neither runs without the other `[READ]` — `KorTools23.csproj`
`<Reference Include="KorETABS">`.

## A3. How you would demo it

**Prerequisite that decides everything: the demo machine must have ETABS 23 installed and licensed.**
This workstation does not — `C:\Program Files\Computers and Structures` does not exist and
`HKCU:\Software\Computers and Structures` is absent `[RUN]`. A CSI licence is per-seat and not
something to arrange in the demo window.

Given a machine with ETABS 23, the path is:

1. Map `L:` to `\\Kor-fs01\Library` — **the installer hard-requires the `L:` drive letter**, not the
   UNC path (`Install Kor Tools ETABS 23.bat`, `SOURCE=L:\03 Programs\…`) `[READ]`. This workstation
   has no `L:` mapped `[RUN]`, so the installer as shipped would fail here.
2. Launch ETABS 23 once and close it (the installer refuses if `%LOCALAPPDATA%\Computers and
   Structures\ETABS 23` does not yet exist).
3. Run `Install Kor Tools ETABS 23.bat` — copies the plugin to
   `%LOCALAPPDATA%\…\ETABS 23\Plugins\KorTools\` and appends `PluginN=…KorTools.dll` under
   `[Plugins]` in `ETABS.INI` via `_install_helper.ps1` `[READ]`.
4. Open a real KOR model, then **Tools → Kor Tools**.
5. On screen: a tabbed always-on-top window — **Setup · Quick Define · Cracking · Piers and
   Spandrels · Process · Raw Data · Export**.

The demo-safe click path is: **Setup** → build a storey list from "N levels at h, N parking at h" and
press Create; **Quick Define** → create a concrete material and W/S/C sections to KOR's prefixes;
**Cracking** → pick load case + model type, watch it assign stiffness modifiers to every selected
shell and frame from KOR's standard table; **Piers and Spandrels** → assign pier labels across a
selection; **Export** → tick "Story Drifts", "Pier Forces", "Modal Periods and Frequencies" and write
a real `.xlsx`.

**Do not click the Process or Raw Data tabs.** They pop `MessageBox.Show("Coming in Phase 2.")` —
still, in the 2026-03-30 production build (`fMain.cs:238-241`, both the 22 and 23 sources) `[READ]`.

**Better demo idea:** run the module-10 DXF→ETABS generator to produce the `.e2k`, import it, then
open Kor Tools and finish the model. That is the whole story — drawings in, analysable model out —
and it uses two tools KOR already ships.

## A4. Completeness

Feature inventory taken from the **live ETABS 23 fork** (the deployed code), not the local copies.

| Capability | State | Evidence |
|---|---|---|
| Load into ETABS Tools menu (`cPlugin.Main` / `Info`) | WORKING | `[READ]` `cPlugin.cs`; `[QUERIED]` deployed `KorTools.dll` 2026-03-30 |
| Setup → create storey list (levels / parking / EMR) | WORKING | `[READ]` `Setup.cs:66` → `QuickDefine.DefineStories` |
| Setup → seismic RSA combos from Rd/Ro | WORKING | `[READ]` `Setup.cs:98` → `QuickDefine.SetupCombosEQ` |
| Setup → wind combos | WORKING | `[READ]` `Setup.cs:118` → `SetupCombosWind` |
| Setup → displacement-modified spectrum cases | WORKING | `[READ]` `Setup.cs:138` |
| Setup → convert diaphragm mass to lumped masses | PARTIAL | `[READ]` `Setup.cs:152`; requires a solved model, guarded; `MassProp.cs:159/174` carry two unfinished `TODO`s on beam-depth rotation and floor association |
| Setup → assign material to walls/cols/floors/beams | WORKING | `[READ]` `Setup.cs:179` → `QuickAssign.AssignMaterial` |
| Setup → "Add Pier Labels" to columns | PARTIAL | `[READ]` `Setup.cs:199-217` — see A5, it iterates **all** frame objects, not columns |
| Quick Define → concrete material / slab / wall / column sections | WORKING | `[READ]` `Define.cs:62/145/188/277` (23 fork) |
| Cracking → auto-crack whole model | WORKING | `[READ]` `Cracking.cs:799` |
| Cracking → assign shell / frame modifiers by load+model type | WORKING | `[READ]` `Cracking.cs:837/892` |
| Cracking → named cracking sets save/load/delete/release/clear | WORKING | `[READ]` `Cracking.cs:943-995` |
| Cracking → KOR standard cracking table | WORKING but see A5 | `[READ]` 675-line hardcoded XML literal, `Cracking.cs:19-693` |
| Piers and Spandrels → assign selected / next / next-sub / clear | WORKING | `[READ]` `PiersAndSpandrels.cs:50-146` |
| Piers and Spandrels → named label sets | WORKING | `[READ]` `PiersAndSpandrels.cs:239-279` |
| **Process** (load-case scaling / run automation, ~2,241 lines) | **DEAD (unreachable)** | `[READ]` `fMain.cs:238-241` blocks tab index 4 |
| **Raw Data** (76 lines + grid) | **DEAD (unreachable)** | `[READ]` same block, tab index 5 |
| Export → result tables to `.xlsx` | WORKING | `[READ]` `Export.cs:61`, seven preselected tables |
| Build the *committed* `KorTools` source | **BROKEN** | `[RUN]` two failures, see A5 |
| Build the *committed* `KorETABS` (net472) source | **BROKEN on this machine** | `[RUN]` MSB4803, no .NET Framework MSBuild installed |
| Build the *live* ETABS 23 fork | WORKING | `[RUN]` 0 errors both projects |

**Debt markers, whole tree (KorETABS + KorTools + OLD/ETABS_Plugin):**
`TODO` **15** (9 in the live product, 6 in `OLD/`), `FIXME` 0, `HACK` 0,
`NotImplementedException` **1** — `OLD/ETABS-Toolkit/ETABS_Plugin/EtabsConnector.cs:165` only,
`NotSupportedException` 0, empty catch blocks **0**. `[RUN]` greps.

Significant `TODO`s: `KorETABS/Process.cs:927` *"This whole process is rough coded, will need to
revisit"*; `Process.cs:971` *"Temporary check for gravity loads (LinStatic), should use more robust
solution"*; `QuickDefine.cs:725` *"Adjust parameters for 30 modes instead of default 12. Can't do this
through API currently"* — that last one is a live modelling limitation, not a code smell.

`OLD/ETABS-Toolkit` — **genuinely superseded.** Five independent signals `[READ]`/`[QUERIED]`:
its `cPlugin.Info` announces "ETABS Multi-tool / Created by Jeremy Atkinson (c) 2020"; its tabs are
About · Cracking · Define · Excel Export · Pier Names · Prelim — the same feature set Kor Tools now
implements; it references `C:\Program Files\Computers and Structures\ETABS 18\ETABSv1.dll`; its last
commit is 2021-02-05 *"snapshot before moving to server"*; and no build of it exists in any of the
`Kor Tools 21/22/23` deployment folders. **One caveat:** it had a **Prelim** tab and Excel
template/directory management that Kor Tools never picked up. If anyone still wants those, they exist
only there.

## A5. What is broken or risky

1. **The version-controlled source no longer builds; the shipping source is not version-controlled.**
   `[RUN]` Building the committed `KorTools` working tree fails twice: first `MSB3577` (two resources
   resolve to `KorTools.fMain.resources` — stray `Setup.resx` and `QuickDefine.resx` map onto the same
   partial class as `fMain.resx`), and with those removed, 30+ `CS0103` errors because
   `KorTools/QuickDefine.cs` is **staged as deleted** and took every "Quick Define" tab handler
   (`bDefineSlabCreate_Click`, `CreateNewWallName`, `RefreshConcMaterials`, …) with it. That deletion
   has sat uncommitted since 2025-03-05 `[QUERIED]` `git status`. Meanwhile the code that actually
   ships lives in `Development Files\KorETABS23\` and `KorTools23\` on `\\Kor-fs01\Library` — **no
   `.git` directory in either** `[QUERIED]`. There is no history, no diff and no blame for the version
   of KOR's most-used engineering tool that is on every engineer's machine.

2. **`Release` builds write straight into the production plugin folder.** `[READ]`
   `KorTools23.csproj` / `KorETABS23.csproj`: `<OutputPath>L:\…\KOR Tools 23\</OutputPath>` under
   `Configuration|Platform == Release|AnyCPU`. Pressing Ctrl+Shift+B in the wrong configuration
   overwrites what the whole firm loads on next ETABS launch. No staging, no CI, no versioning.

3. **A KOR engineering standard is a 675-line string literal.** `[READ]` `Cracking.cs:19-693` —
   `public static string xmlContent = @"<CrackingValues>…"`. These are the firm's standard stiffness
   modifiers per load case and model type. `grep -ril "cracking|CrackingValues"` across `Operations`
   and `KOR.Drafter` returns **one** unrelated hit `[RUN]` — none of this is in `KorStandards`. It is
   also duplicated: `KorStandardCrackingValues.xml` (21,944 bytes, 2021-10-28) still sits beside the
   `Kor Tools` and `Kor Tools Phase 1` deployments. **Cross-reference module 12.**

4. **The naming convention is hardcoded in two products that do not know about each other.**
   `[READ]` `KorTools/app.config` sets `DefineWallPrefix=W`, `DefineColumnPrefix=C`,
   `DefineSlabPrefix=S`. `Operations/…/Dxf/E2kGeometryComposer.cs:379/442/492` emits `NextName("W"…)`,
   `NextName("C"…)`, `NextName("S"…)`. Pier labelling likewise: `E2kGeometryComposer.cs:392`
   (`AssignPierLabels`, a `KorStandards` rule `dxf.assign-pier-labels`) versus `Setup.cs:199` and the
   whole of `PiersAndSpandrels.cs` / `KorETABS/PierLabelSet.cs`. Same convention, two sources of
   truth, one of them a DB rule and the other an XML config on a share.

5. **"Add Pier Labels" labels every frame, not every column.** `[READ]` `Setup.cs:199-217`: the
   comment says "Get column names" but the call is `SAPMODEL.FrameObj.GetNameList(...)`, which returns
   **all** frame objects — beams and braces included — and every one is then given a pier label. There
   is also no null guard: if the model has no frames, `columnNames` stays `null` and the `foreach`
   throws inside ETABS's process. Unverified against a live model — I could not run ETABS. The check
   is: open a model with beams, click the button, and look at the beams' pier assignments.

6. **Effectively no error handling.** `[RUN]` **2** `catch` blocks in ~14,400 lines of live code.
   `KorETABS/QuickDefine.cs:478` shows `ex.Message` in a MessageBox; `Cracking.cs:704` catches
   `FileNotFoundException` around `KorCrackingValuesXML.LoadXml(xmlContent)` — a **string** overload
   that cannot throw it, a leftover from when the XML was a file. Every other API failure propagates
   straight into ETABS's own process, where the user loses unsaved model work.

7. **94 unguarded `Convert.ToDouble(textBox.Text)` / `Convert.ToInt32(...)` calls.** `[RUN]` grep over
   `KorTools`. Example `Setup.cs:105-108`, the Rd/Ro inputs. A blank or mistyped box throws a
   `FormatException` with no catch anywhere above it — see (6).

8. **1,224 build warnings** in the live `KorTools23` build `[RUN]`, most of them `CA1416`
   platform-compatibility on WinForms calls from a `net8.0-windows` library.

9. **`_install_helper.ps1` contains a nonsense predicate that works by accident.** `[READ]`
   `$_ -eq $null -eq $false` parses as `($_ -eq $null) -eq $false`, i.e. "not null", which happens to
   be the intent. It will confuse the next person who touches it.

## A6. Dependencies

| Dependency | Needed for | Off the KOR LAN? |
|---|---|---|
| **ETABS 23, installed and licensed** | everything — this is an in-process plugin | Per-seat CSI licence. **Absent on this workstation** `[RUN]` |
| `ETABSv1.dll` (CSI API, 353,320 bytes, 2026-02-23) | compile + run | Ships in the plugin folder |
| `\\Kor-fs01\Library\03 Programs\…` **mapped as `L:`** | install, and both `HintPath`s | **VPN required; the drive letter is hard-coded** |
| Microsoft Excel (COM interop) | the Export tab | Local Office install |
| `.NET 8 Desktop Runtime` (ETABS 22/23 builds) | run | Local |
| **.NET Framework MSBuild / Visual Studio** | rebuilding the net472 ETABS-21 generation | **Not installed on this workstation** `[RUN]` MSB4803 |
| SQL / `KorStandards` | — | **None.** Zero database dependency `[RUN]` grep |

No network calls, no AI, no SQL. Once installed, the plugin runs entirely offline — which makes it the
*most* portable engineering tool in the whole suite, provided ETABS is on the machine.

## A7. Test reality

**There are no tests.** No test project, no test file, no assertion framework anywhere in the three
repos or in the ETABS 23 fork `[RUN]`. 14,400 lines of code that mutate live structural models —
stiffness modifiers, load combinations, pier labels — with zero automated verification. That is not
coverage theatre; it is the absence of the stage.

This is defensible-ish for API glue that is impossible to test without ETABS, but `KorETABS/Units.cs`
(24 KB of unit conversion), `QuickDefine.CreateStoryLists`, and the cracking-table lookup are all pure
functions that could be tested today with no ETABS present at all.

## A8. Demo risk

1. **No ETABS on the demo machine = no demo.** Highest risk by far, and not fixable with code. `[RUN]`
2. **Clicking Process or Raw Data pops "Coming in Phase 2."** in front of MVE's technical lead, on
   software that has been in production for five years. Two visible tabs, ~2,241 lines behind them.
   `[READ]`
3. **A `FormatException` from an empty text box takes down the dialog** (and possibly disturbs the
   host ETABS session) with no handler anywhere. Any stray keystroke on the Setup tab. `[RUN]`/`[READ]`
4. **"Where is this in source control?"** is a fair question and the honest answer is "it is a folder
   on a file share". Do not volunteer it; have the answer ready. `[QUERIED]`
5. The window is `TopMost` by default `[READ]` `fMain.cs` + `app.config` — it will float over any
   screen-share content and over ETABS itself. Uncheck it before presenting.
6. `cPlugin.Info` reports **"Kor Tools / Version 0.9 / Created on December 3, 2020 / Developed by
   Adrian Crowder"** `[READ]`. If anyone opens Tools → Add/Show Plugins on screen, the firm's flagship
   engineering tool is labelled a five-year-old 0.9 by a name MVE will not recognise.

## A9. To-do register

| Item | Size | Tag | Why |
|---|---|---|---|
| Decide whether ETABS is on the demo machine at all; if not, cut Part A from the run sheet | S | `BEFORE-DEMO` | Everything else is moot |
| Hide the Process and Raw Data tabs instead of blocking them | S | `BEFORE-DEMO` | Removes the only guaranteed "unfinished" moment |
| Update `cPlugin.Info` to a current version string and "KOR Structural" | S | `BEFORE-DEMO` | One dialog, five-year-old credit |
| Rehearse the Setup → Quick Define → Cracking → Piers → Export path once on a real model | M | `BEFORE-DEMO` | Nothing here has ever been rehearsed by the person demoing it |
| Get `KorETABS23` / `KorTools23` into git and delete the 22 fork from this workstation | M | `SOON` | The shipping source has no history; the local copy is broken and misleading |
| Change `Release` `OutputPath` off `L:\…\KOR Tools 23\` to a staging folder | S | `SOON` | One stray build overwrites the firm's plugin |
| Fix the committed `KorTools` tree (restore `QuickDefine.cs`, drop stray `.resx`) or delete it | S | `SOON` | A repo that does not build is worse than no repo |
| Move the cracking standard out of `Cracking.cs:19-693` into `KorStandards` | L | `SOON` | Same standard, three copies, none authoritative — module 12 |
| Reconcile W/C/S prefixes and pier labelling between Kor Tools and DXF→ETABS | M | `SOON` | Two hardcoded copies of one convention |
| Wrap the tab handlers in try/catch and switch the 94 `Convert.To*` to `TryParse` | M | `SOON` | An exception here escapes into ETABS |
| Investigate whether "Add Pier Labels" is labelling beams (`Setup.cs:207`) | S | `SOON` | Potential wrong engineering output, not just a crash |
| Add unit tests for `Units.cs`, `CreateStoryLists`, cracking lookup | M | `LATER` | Testable today without ETABS |
| Find the source of `ETABS Reaction Tool` (2026-08-05) and bring it under the same roof | M | `LATER` | A shipped plugin with no locatable source |
| Decide the fate of the `Prelim` tab in `OLD/ETABS-Toolkit` | S | `LATER` | Only feature not carried forward |

## A10. Verdict — PART A

**Demo-able with care, and only on a machine with ETABS 23 licensed.** This is the opposite of what
the brief assumed: not an abandoned earlier attempt but continuously-maintained, firm-wide production
tooling, rebuilt five months ago for the current ETABS and used daily — and it is the missing second
half of the DXF→ETABS story that module 10 tells only half of. Paired, they are the strongest
engineering narrative KOR has for MVE: drawings in, geometry generated, materials/cracking/combos/pier
labels applied, results out to Excel. The engineering is sound; the software practice around it is
not — the shipping source is an unversioned folder on a file share, `Release` builds overwrite
production, there are two `catch` blocks in 14,400 lines, and there are no tests at all. **The single
most important thing to fix before the demo** is trivially small: hide the two tabs that say "Coming
in Phase 2." The single most important thing to fix after it is to get `KorETABS23`/`KorTools23` into
git before the only copy of KOR's most-used engineering tool is a share folder someone can delete.

---
---

# PART B — DemoStudio

## B1. What I searched

**Prior art / given artifacts first** (CLAUDE.md rules 1 and 2). `00-INVENTORY.md:134-151` (13
projects, per-project line counts and last-modified dates) and `SCOPE.md:15` read before running
anything. Read `ARCHITECTURE.md`, `REMEDIATION.md` (all 430 lines), `docs/*.md`, `.github/workflows/`,
`global.json`, `Directory.Build.props`, both `.sln` files.

**Git.** `git log` (44 commits), `git branch -a` (7 branches), `git show --stat 439a6ec`,
`git log -- REMEDIATION.md`, `git log -- architecture/`, `git remote -v`, `git rev-parse` on all four
tracked refs. Also `git log --reverse` on the **Operations** repo, to date the handover.

**Code read.** `DesktopCompositionRoot.cs` (all 220 registration lines), `FfmpegExecutableResolver.cs`,
`DesktopRecorderOptionsNormalizer.cs`, `FfmpegCaptureOptionsValidator.cs`, `LocalFileStorage.cs`,
`StubRedactionProcessor.cs`, `IRedactionProcessor.cs`, `RedactionRule.cs`,
`DesktopAiNarrationService.cs`, `DesktopPublishPackageService.cs`, `AutomationOptions.cs`,
`appsettings.json`, `MainWindow.xaml` (1,559 lines), `FfmpegVideoCaptureServiceRegressionTests.cs`.

**Greps.** `redact` (case-insensitive, `.cs` and `.xaml`); `IRedactionProcessor|RedactionRequest`;
`IDemoRunPipeline`; `UseSqlite|UseSqlServer|UseInMemory`; `Data Source|Server=`; `TODO|FIXME|HACK`;
`NotImplementedException|NotSupportedException`; `class Stub`; empty-catch regex via a Python walker.

**Commands run (`[RUN]`).**
- `dotnet build src/DemoStudio.Desktop.App/DemoStudio.Desktop.App.csproj -c Debug` → **success, 0
  warnings, 0 errors, 13.66 s**
- `dotnet run --project src/DemoStudio.Desktop.Smoke -c Debug -- --iterations 1 --seconds 4 --mode
  Desktop --output <scratchpad>` → **passed**, then `ffprobe` on the produced file
- `dotnet test tests/DemoStudio.Desktop.Core.Tests` → **3 passed**
- `dotnet test tests/DemoStudio.Desktop.App.Tests --filter
  "FullyQualifiedName!~InfrastructureConcurrencyTranslation"` → **72 run, 71 passed, 1 failed**
- **Launched `DemoStudio.Desktop.App.exe` twice**, inspected both windows via UI Automation, captured
  the main window with `PrintWindow`, and **drove a full session through UIA**: Record Demo → Finish
  Recording → Export Tutorial. Then read the produced package and `runtime-20260821.log`, stopped the
  process and confirmed no orphan `ffmpeg`.
- `ffprobe` on `demo.mp4` and `thumbnail.jpg`; `ffmpeg -version`; `Get-CimInstance Win32_VideoController`

**Could not verify.** Whether the GitHub Actions CI on `ilalonde1/Demo-Studio` is red — `gh` is not
installed here `[RUN]`. The check is `gh run list -R ilalonde1/Demo-Studio -L 5`, or open
`https://github.com/ilalonde1/Demo-Studio/actions`. Given the failing test below and that `main` was
pushed on 2026-08-01, expect red.

## B2. What this module is

DemoStudio is a screen-recording studio built around one workflow: point it at a window or the whole
desktop, hit **Record Demo**, walk through whatever you want to show, hit **Finish Recording**, tidy
the clips, and hit **Export Tutorial** to get a packaged `.zip` containing the video, a poster frame,
a metadata file, a ready-to-paste share blurb and an interactive HTML walkthrough. Under the covers it
is a `RecorderSessionEngine` state machine driving `ffmpeg` `gdigrab` through a queued process
launcher, with a floating always-on-top HUD, global hotkeys (`Ctrl+Shift+1/2/3`), a pause-to-split
clip model, a presenter mode that puts the controls on one monitor and the target on the other, a
watchdog that stops a runaway capture, crash-safe session drafts, and an optional OpenAI
text-to-speech pass that turns typed clip scripts into voiceover.

What is on screen is genuinely good. The main window opens with a "Create your first demo in four
steps" quick start, a **Recorder Studio** header, live `State / Clips / Duration` chips, a four-stage
progress strip (Choose Target · Record Flow · Curate Clips · Export Tutorial), a **Record** tab with a
window picker ("Found 7 capturable windows"), a **Review + Export** tab with a per-clip grid for
banner text and narration script, and a status bar reading **"Ready to record — Readiness check
passed."** It does not look like a side project. `[RUN]` — screenshot captured via `PrintWindow`.

## B3. How you would demo it

You would not demo DemoStudio to MVE. It is an internal tool with no relevance to structural
engineering, and putting it on screen invites "why did an engineering firm build a screencast app?"
Its value here is entirely as a **production tool for the MVE demo itself**, used off-camera.

To use it, on a machine with `ffmpeg` on `PATH`:

1. **Fix `appsettings.json` first** — set `DesktopRecorder:Capture:FfmpegPath` to `"ffmpeg"` (or the
   real path). As committed it says `C:\Program Files\WinGet\Links\ffmpeg.exe`, **which does not exist
   even on the developer's own workstation**, and the app refuses to start with a modal titled
   *"Startup Blocked — DemoStudio cannot start due to critical environment issues: FFmpeg executable
   is not resolvable"* `[RUN]`. The one-line workaround that I used and verified is the environment
   variable `DesktopRecorder__Capture__FfmpegPath=ffmpeg`.
2. `dotnet build src/DemoStudio.Desktop.App -c Debug` then run
   `artifacts\bin\DemoStudio.Desktop.App\Debug\net8.0-windows\DemoStudio.Desktop.App.exe`.
3. Record Type → **Desktop** (safer than Window, see B5), or pick the target window.
4. **Record Demo** → walk the workflow → **Finish Recording** → **Export Tutorial**.
5. Output lands in `%LOCALAPPDATA%\DemoStudio\RecorderDesktop\publish\publish-<id>-latest.zip`.

I ran exactly this today. The session recorded 26.3 s, one clip, and produced a 1920×1200 h264
`demo.mp4`, a valid 1920×1200 `thumbnail.jpg`, `tutorial.html` + `assets/style.css`, `tutorial.json`,
`metadata.json`, `share-copy.txt`, `README.txt` and a 551 KB zip `[RUN]`.

## B4. Completeness

| Capability | State | Evidence |
|---|---|---|
| Build (Debug, `DemoStudio.Desktop.App`) | WORKING | `[RUN]` 0 warnings, 0 errors, 13.66 s |
| App starts as committed | **BROKEN** | `[RUN]` "Startup Blocked" modal — bad `FfmpegPath` |
| App starts with `FfmpegPath` corrected | WORKING | `[RUN]` window "DemoStudio Recorder Desktop", "Readiness check passed" |
| Target window enumeration / picker | WORKING | `[RUN]` "Found 7 capturable windows" |
| Desktop capture (start → stop → mp4) | WORKING | `[RUN]` 26.3 s, 1920×1200 h264, verified with `ffprobe` |
| Window capture, single monitor | UNKNOWN | Not exercised; default mode is `Window` |
| Window capture, second monitor at negative origin | **PARTIAL/BROKEN** | `[RUN]` the one failing test is exactly this |
| Clip model (pause to split, curation grid) | WORKING | `[RUN]` `Clips: 1`, grid present |
| Compose → publish package + zip | WORKING (artifacts) | `[RUN]` all 8 files + zip produced |
| Publish reported as **succeeded to the user** | **BROKEN** | `[RUN]` cross-thread `InvalidOperationException`; log says "Publish package creation failed" while the package exists |
| Thumbnail generation | PARTIAL | `[RUN]` `ffmpeg-thumbnail-primary` **and** `-retry` both exit −22; a third fallback produced a valid jpg |
| Interactive HTML tutorial with steps | **STUBBED for screen recordings** | `[RUN]` `"Browser interaction log not found" → Generated 0 narration steps → Tutorial exported … with 0 steps`; `tutorial.json` = `{"steps": []}` |
| Browser (WebView2) interaction recorder → real steps | UNKNOWN | `[READ]` `DesktopBrowserInteractionRecorder`; only path that populates steps; not exercised |
| AI voiceover (OpenAI TTS) | PARTIAL | `[READ]` needs `OPENAI_API_KEY`; degrades to a clear message; not exercised (spend) |
| Microphone narration recording | UNKNOWN | `[READ]` UI present, not exercised |
| Session history / draft recovery (atomic writes) | WORKING | `[RUN]` `session-history.json`, `session-draft.json` written; `[READ]` `DesktopAtomicJsonFile` |
| Watchdog / preflight / dependency health | WORKING | `[RUN]` health probe runs `ffmpeg -version` every 30 s in the log |
| **FlaUI desktop automation** | **DISABLED** | `[READ]` `AutomationOptions.DesktopEngine = "Stub"` and **no `Automation` section in `appsettings.json`** → `StubDesktopAutomationEngine` is what gets registered |
| **Redaction** | **STUBBED — see B5** | `[READ]` `StubRedactionProcessor` is the only implementation and the only registration |
| EF Core / SQL Server persistence layer | **DEAD** | `[READ]` `DemoStudioDbContext`, 2 migrations, repositories — **no DbContext is registered in `DesktopCompositionRoot`** |
| Smoke / soak harness | WORKING | `[RUN]` `passed=1 failed=0 ffmpegLeftover=False` |

**Debt markers:** `TODO` / `FIXME` / `HACK` — **0** across `src` and `tests` `[RUN]`.
`NotImplementedException` — **0**. `NotSupportedException` thrown — **1**, `DurationToWidthConverter.cs:32`
(a normal one-way `IValueConverter`). Empty catch blocks — **14** `[RUN]`, notably
`DesktopAiNarrationService.cs:63,66`, `DesktopClipNarrationService.cs:49,52`,
`DesktopCaptureMediaCoordinator.cs:315`, `DesktopSmokeCheckService.cs:117`,
`MainWindowViewModel.CaptureMedia.cs:63`, `RecorderHudWindow.xaml.cs:121`. Four `Stub*` classes, three
of which are the registered production implementation.

## B5. What is broken or risky

1. **The committed configuration prevents the app from starting.** `[RUN]`
   `src/DemoStudio.Desktop.App/appsettings.json` → `"FfmpegPath": "C:\\Program Files\\WinGet\\Links\\ffmpeg.exe"`.
   That path does not exist on this machine; `ffmpeg` is at
   `…\WinGet\Packages\yt-dlp.FFmpeg_…\bin\ffmpeg.exe` and is on `PATH`. `FfmpegExecutableResolver`
   only falls back to `PATH` when the configured value is a **bare name** with no separator — a rooted
   path that does not exist returns `false` with no fallback. Result: modal *"Startup Blocked"*, and
   `ERROR AppStartup Startup health gate failed` in the log. **One-word fix**: `"ffmpeg"`.

2. **Publish reports failure after succeeding.** `[RUN]`
   `runtime-20260821.log`: `ERROR PublishWorkflowViewModel Publish package creation failed.
   System.InvalidOperationException: The calling thread cannot access this object because a different
   thread owns it` — thrown from `RelayCommand.RaiseCanExecuteChanged` (`RelayCommand.cs:96`) via
   `CommandStateCoordinator.cs:25` ← `MainWindowViewModel.cs:1403/1361/1598` ←
   `ProductionWorkspaceViewModel.set_LastTutorialHtmlPath` (`ProductionWorkspaceViewModel.cs:249`) ←
   `PublishWorkflowViewModel.CreatePublishPackageAsync`. A background continuation sets a bound
   property without marshalling to the dispatcher. The package and the zip **were written
   correctly**; only the completion signal blew up. It fires a second time as
   `ERROR Runtime UI command failed` from `WorkflowStateViewModel.cs:52/149`. This is precisely what
   `REMEDIATION.md` §1.2 and §2 were written to prevent.

3. **The one failing test is the multi-monitor window-capture path — and it was added in the last
   commit.** `[RUN]` `FfmpegVideoCaptureServiceRegressionTests.StartAsync_ClampsWindowBoundsToDesktop_BeforeBuildingGdigrabOffsets`
   fails: expected `-video_size 1921x1040` in the `gdigrab` argument list, not present. The fixture is
   a window at `(-1921,-1)` on a desktop spanning `(-1920,0,3840,1080)` — a second monitor to the left
   of the primary. `git show --stat 439a6ec` shows this file was **created** in the 2026-08-01 commit.
   Someone was mid-fix on exactly this and stopped. Since `CaptureMode` defaults to `"Window"` and the
   app advertises a two-monitor Presenter View, this is the most likely way a real recording goes
   wrong.

4. **Redaction redacts nothing, and says it succeeded.** `[READ]` `StubRedactionProcessor.cs` is the
   only `IRedactionProcessor` in the solution and the only registration
   (`DesktopCompositionRoot.cs:149`). Its entire behaviour is
   `File.Copy(request.RawVideoPath, request.RedactedVideoPath, true)` followed by
   `new RedactionResult(true, …)` and a log line per rule: `REDACTION_RULE|Name=…|Match=…|Replacement=…`.
   It returns **`Succeeded: true`** having copied the unredacted video to the path named "redacted".
   That is worse than not having it: it is a false assurance. There is no redaction UI anywhere
   (`grep -i redact` over all `.xaml` → **zero hits**), and the only caller is
   `DemoRunPipeline.cs:381`, which the recorder UI does not drive. `RedactionRule` is a text
   match-and-replace record (`MatchExpression`, `ReplacementText`) — a design for redacting *text*,
   with no pixel-region, OCR or blur concept anywhere in the codebase. **There is nothing here to
   turn on.**

5. **FlaUI automation is not actually wired on.** `[READ]` `AutomationOptions.DesktopEngine` defaults
   to `"Stub"`; `appsettings.json` has no `Automation` section; `DesktopCompositionRoot.cs:151-176`
   therefore registers `StubDesktopAutomationEngine` and `StubDesktopInspectionService`. `ARCHITECTURE.md`
   presents FlaUI as a live subsystem. It is code that is present and unreachable by default.

6. **An entire EF Core / SQL Server layer is dead.** `[READ]` `DemoStudioDbContext`, two migrations,
   `DemoRunRepository`, `RedactionRuleConfiguration`, and a design-time factory hardcoding
   `Server=(localdb)\MSSQLLocalDB;Database=DemoStudio;Trusted_Connection=True`
   (`DemoStudioDesignTimeDbContextFactory.cs:11`). No `DbContext` is registered in the desktop
   composition root, so none of it runs. `REMEDIATION.md` §7 flagged this as "decide whether
   strategic or redundant" and the decision was never made.

7. **The tutorial export is empty for screen recordings.** `[RUN]` `tutorial.json` = `{"title": "…",
   "steps": []}` and `tutorial.html` ships `const steps = [];`. The log is explicit: `Browser
   interaction log not found at …\browser-interactions\stage-browser-events.jsonl` → `Generated 0
   narration steps from 0 demo steps` → `Tutorial exported … with 0 steps`. Steps only come from the
   WebView2 browser recorder, so the interactive walkthrough half of the product is inert for desktop
   and window capture — which is the only mode useful for demoing WPF apps.

8. **Thumbnail generation fails twice before succeeding.** `[RUN]` `ffmpeg-thumbnail-primary` and
   `ffmpeg-thumbnail-retry` both exit −22 (`Nothing was written into output file`) against ffmpeg
   `N-124279` (2026-04-30). A third path produced a valid jpg. Fragile against ffmpeg version drift.

9. **Storage sandbox is stricter than `REMEDIATION.md` implies but not as designed.** `[READ]`
   `LocalFileStorage.ResolvePath` still *accepts* rooted paths (§1.1 said reject them) but then
   enforces `candidate.StartsWith(_basePathBoundary)` and throws otherwise. Escape is prevented; the
   contract split the plan asked for was never made.

10. **The repo is a personal GitHub account.** `[QUERIED]` `origin = git@github.com:ilalonde1/Demo-Studio.git`.
    Not KOR-owned. Same for the ETABS plugin repos (`github.com/jchatkinson/…`, a former colleague's
    account).

## B6. Dependencies

| Dependency | Needed for | Off the KOR LAN? |
|---|---|---|
| `ffmpeg` (+ `ffprobe`) on the machine | all capture, compose, thumbnails | **Local only — no network.** Currently pointed at a path that does not exist |
| .NET 8 SDK/runtime (`global.json` pins `8.0.418`, `rollForward: latestPatch`) | build/run | Local; `8.0.422` installed satisfies it `[RUN]` |
| WPF / Windows 10+ | the shell | Local |
| WebView2 runtime | browser interaction recorder only | Local |
| **OpenAI API** (`api.openai.com`, `OPENAI_API_KEY`) | AI voiceover only | **Internet + a paid key.** Optional; degrades with a clear message `[READ]` |
| SQL Server LocalDB | **nothing at runtime** — design-time EF only | Dead path |
| `\\Kor-fs01`, Deltek, Graph, SharePoint, `KorStandards`, MCP | — | **None** |

**This is the least network-coupled product in the entire suite.** It runs on a laptop in MVE's
office with the Wi-Fi off, provided `ffmpeg` is present. That is exactly the property a fallback
demo needs.

## B7. Test reality

| Project | Tests | Result |
|---|---|---|
| `DemoStudio.Desktop.Core.Tests` | 3 | **3 passed** `[RUN]` |
| `DemoStudio.Desktop.App.Tests` | 73 (72 run, `InfrastructureConcurrencyTranslation` filtered out — needs LocalDB) | **71 passed, 1 failed** `[RUN]` |

Both ran headless without hanging, contrary to the Operations-repo rule about WPF app tests — this is
a different solution and its tests are ViewModel-level, not window-level.

The coverage is better-aimed than most suites in this audit: `ProcessExecutionSafetyTests`,
`FfmpegOperationQueueTests`, `CaptureRuntimeResilienceTests`, `Phase1StabilityTests`,
`ReliabilityWorkflowTests`, `RuntimePathResolverDeterminismTests`, a `ReleaseGate/` folder with
`Gate=ReleaseConfidence` traits, and `UiGuidanceExperienceTests` asserting on the onboarding copy. Zero
`TODO`s in the whole codebase.

But it is aimed at the *reliability plumbing*, not at the product's promise. **Three tests** cover the
`RecorderSessionEngine` state machine, the thing the architecture doc calls the core. **Nothing**
covers redaction (there is nothing to cover). **Nothing** covers the publish package contents, which
is how the cross-thread bug in B5(2) survives a green-ish suite. And the one red test is the one that
matters most for actually recording something.

## B8. Demo risk

DemoStudio is not going on screen, so these are risks to **using it to make the fallback video**,
ranked:

1. **It does not start as committed.** One-line config fix, but if someone tries it cold under time
   pressure the first thing they see is "Startup Blocked". `[RUN]`
2. **Window capture on a second monitor is provably wrong.** Record in **Desktop** mode, on a single
   monitor, with Presenter View off. `[RUN]`
3. **Publish looks like it failed when it worked.** Whoever uses it will believe the export broke and
   will not think to go look in `%LOCALAPPDATA%\DemoStudio\RecorderDesktop\publish\`. `[RUN]`
4. **The "interactive tutorial" is an empty shell** for anything that is not a browser recording. If
   someone plans a deliverable around it they will get a styled page with no content. `[RUN]`
5. **Redaction cannot be relied on for anything.** If anyone assumes the recorder will scrub client
   names from a KOR-app walkthrough, they will ship an unredacted video with a file named
   "redacted". `[READ]` — this is the one finding here with a real downside beyond the demo.
6. Session titles are raw GUIDs — `"Demo 5cba7c2336bb45a38e835148efb0bfaf"` in `README.txt`,
   `metadata.json`, `share-copy.txt` and the tutorial `<h1>`. Anything shared externally needs the
   title set by hand. `[RUN]`
7. The dependency-health probe launches `ffmpeg -version` every 30 seconds for the app's whole
   lifetime `[RUN]` — harmless, but it is a process spawn every 30 s during a recording.

## B9. To-do register

| Item | Size | Tag | Why |
|---|---|---|---|
| Set `appsettings.json` `FfmpegPath` to `"ffmpeg"` | S | `BEFORE-DEMO` | Only if DemoStudio is used to produce the fallback video; without it the app will not start |
| Record in **Desktop** mode on one monitor; do not use Window mode or Presenter View | S | `BEFORE-DEMO` | Avoids the one known-broken capture path |
| Tell whoever records that "Publish failed" is a lie and where the zip actually lands | S | `BEFORE-DEMO` | Prevents a false abort under time pressure |
| Do **not** rely on redaction; blur/cut manually or record against sanitised data | S | `BEFORE-DEMO` | The stub reports success on unredacted video |
| Fix the cross-thread `RaiseCanExecuteChanged` (dispatcher-marshal `LastTutorialHtmlPath` and the workflow-state setters) | S | `SOON` | Real bug, small fix, kills two ERROR paths |
| Fix or delete `StartAsync_ClampsWindowBoundsToDesktop…` and the clamping it tests | M | `SOON` | The suite is red and the feature is broken |
| Make redaction real or **delete `IRedactionProcessor`, `RedactionRule` and the stub** | L | `SOON` | A stub that returns `Succeeded: true` is a liability; deletion is the honest cheap option |
| Delete or activate the EF/SQL Server layer (`REMEDIATION.md` §7 decision, never made) | M | `SOON` | ~4,500 lines of Infrastructure that nothing calls |
| Turn FlaUI on in `appsettings.json` or stop claiming it in `ARCHITECTURE.md` | S | `SOON` | Doc asserts a subsystem the config disables |
| Give sessions a user-settable title instead of a GUID | S | `SOON` | Every published artifact is stamped with a hex blob |
| Move the repo under a KOR-owned GitHub org | S | `LATER` | Currently `ilalonde1/Demo-Studio` |
| Add a publish-package contents test | S | `LATER` | Would have caught B5(2) |
| Make thumbnail generation robust to ffmpeg version drift | S | `LATER` | Primary and retry both fail today |

## B10. Verdict — PART B

**Keep it off the screen, but keep it on the bench.** DemoStudio is not abandoned and it is not
broken — it is **parked at a working milestone**. The Operations repo's first commit is 2026-03-11 and
DemoStudio's last real work is 2026-03-14 `[QUERIED]`: attention moved to the product that mattered,
three days after the suite got its own repo. The single commit since is a 2026-08-01 work-in-progress
sweep that fixed FFmpeg crop settings, added two test files and left one of them red. It builds clean
in 13.66 s with zero warnings, it starts, and today it recorded, composed, published and zipped a
complete demo package — I drove the whole flow through the real UI and verified every artifact with
`ffprobe` `[RUN]`. Two defects stand between it and a polished fallback video, and both are hours of
work, not weeks: a one-word config fix so it starts, and a dispatcher marshal so publish stops
reporting a failure it did not have. **The single most important thing to fix** is the cross-thread
exception in `PublishWorkflowViewModel` — everything else in the pipeline already works. The one thing
that must not be believed about it is the redaction subsystem: it is a `File.Copy` that returns
success, there is no UI for it, and no amount of configuration turns it on.

---

## Cross-cutting notes for the orchestrator

- **STALE-DOC — `docs/audit-2026-08/SCOPE.md:15`.** Tiers `App Demo Maker` as "inventory line only".
  It is a working, buildable, runnable recorder that produced a complete demo package today `[RUN]`.
- **STALE-DOC — `docs/audit-2026-08/00-INVENTORY.md:134-151`.** Shows several DemoStudio projects last
  modified 2026-08-01. Those are checkout timestamps from the WIP sweep; the code dates from
  2026-03-14 `[QUERIED]` `git log`.
- **STALE-DOC — `App Demo Maker/ARCHITECTURE.md` (2026-03-08).** Describes FlaUI automation as a live
  subsystem; `AutomationOptions.DesktopEngine` defaults to `"Stub"` and `appsettings.json` never
  overrides it, so the stub engines are what the composition root registers `[READ]`.
- **STALE-DOC — `App Demo Maker/REMEDIATION.md`.** Presented in the brief as a current statement of
  what is wrong. `git log -- REMEDIATION.md` shows it was authored 2026-03-14 in commit `3e038e4`
  ("Phase 2: composition root") and **never touched since** `[QUERIED]`; its 2026-08-01 mtime is the
  checkout. Scored against the code: §1.1 storage sandboxing **partly done** (boundary enforced,
  contract split not made); §1.2 shutdown coordinator **not done** (no such type exists); §1.3 atomic
  persistence **done** (`DesktopAtomicJsonFile`, used by session history, draft recovery and launch
  profiles); §3 composition root **done**; §4 `IProcessLauncher` **done**; §6 correlated logging
  **done** (visible in `runtime-20260821.log`); §7 dead-code decision **not made**; §8 security
  hardening **partly**. Roughly half, all of it in the March burst.
- **The audit's scope missed a shipped product.** `ETABS Reaction Tool` — a self-contained ETABS 23
  plugin (17.9 MB viewer + a 10 KB launcher DLL, updated **2026-08-05**) deployed under
  `\\Kor-fs01\Library\03 Programs\CSI America (SAFE, ETABS, SAP2000, etc.)\ETABS - Standalone\Plugin
  Development\KOR Tools 23\ETABS_Reaction_Tool\` `[QUERIED]`. It plots column and wall reactions in
  plan by storey, colours them by magnitude, plots a support over full height, and exports to Excel or
  PDF `[DOC]` — its own `HOW TO INSTALL.txt`. **Its source is nowhere under `C:\VIsual Studio
  Projects`** `[RUN]` (`find -maxdepth 3 -iname "*reaction*"` → nothing). Check:
  `Get-ChildItem '\\Kor-fs01\Library\03 Programs\CSI America (SAFE, ETABS, SAP2000, etc.)\ETABS - Standalone\Plugin Development\KOR Tools 23\ETABS_Reaction_Tool' -Recurse -Depth 2`.
- **Module 10 and Part A are two halves of one story.** DXF→ETABS generates geometry and stops
  deliberately at "no loads, diaphragms, stiffness modifiers, section properties"; Kor Tools does
  exactly and only that. They also duplicate two conventions — the `W`/`C`/`S` naming prefixes
  (`E2kGeometryComposer.cs:379/442/492` vs `KorTools/app.config`) and pier labelling
  (`E2kGeometryComposer.cs:392` vs `Setup.cs:199` / `PiersAndSpandrels.cs`).
- **Module 12 (standards centralisation) is missing a standard.** KOR's cracking-modifier table lives
  as a 675-line hardcoded XML string at `Cracking.cs:19-693` in an unversioned fork on a file share,
  plus a stale duplicate `KorStandardCrackingValues.xml` beside two older deployments. It is not in
  `KorStandards` — `grep -ril "cracking" Operations KOR.Drafter` returns one unrelated hit `[RUN]`.
