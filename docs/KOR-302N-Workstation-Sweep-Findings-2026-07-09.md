# KOR-302N Workstation Sweep — Findings

**Machine:** KOR-302N (192.168.1.91) — Revit lead's primary workstation
**Developer:** Michael Li (profiles `mli` and `michael li.old`; ties back to **BMZ** / Bryson Markulin Zickmantel, KOR's pre-2021 name — desktop shortcut *"Michael @ Bryson Markulin Zickmantel Structural Eng"*)
**Swept:** July 9, 2026, read-only over the `c$` admin share
**Escrow taken:** `C:\Escrow\KOR-302N-2026-07-09` — 281 files, 36.3 MB (all deployed DLLs + all source found + scripts)

---

## Bottom line

The **deployed binaries are fully captured and the deployment mechanism is understood**, but the **source code is almost entirely missing**. Of ~100+ custom plugin DLLs in production, source for only **5 minor/recent tools** exists on this machine — and none of it is under version control. The bulk of KOR's Revit tooling (the rebar, quick-insert, visibility, ribbon, and view/sheet tools that the drafting team uses daily) has **no source code here at all**. That is the single most important thing to resolve before he leaves.

Separately: this is **local Revit desktop tooling, not BIM 360/APS cloud integration**. No Forge/APS app credentials surfaced on this machine. The "silent killer" risk from the continuity dossier (R1) does not appear to apply to what's on KOR-302N — the real risk here is R2 (unrebuildable source). *Caveat: BIM 360 integration could live elsewhere; this finding is scoped to this machine.*

---

## 1. The plugin hub — where the DLLs actually live

Every custom plugin resolves to one folder:

**`C:\ProgramData\2015_RevitCommands\`** — 174 files, ~34 MB, DLLs dating 2014 → 2026.

This folder is **shared on the network** as `\\KOR-302N\2015_RevitCommands` (and `\2015_RevitCommands_Txt` for config/data). The `.addin` manifests under `C:\ProgramData\Autodesk\Revit\Addins\<year>\` point here. So the deployment model is: **manifests per Revit year → DLLs in one shared ProgramData folder.** This is reproducible and now escrowed.

Config/data for the tools lives in `2015_RevitCommands_Txt`: rebar standards (`JBP_Reinforcing_*.csv`), column tables, per-city data files, QuickInsert content lists.

## 2. What the plugins are (deployed, by Revit year 2020–2026)

His custom add-ins, decoded from manifests and DLL names:

- **Core library:** `ML.dll` / `ML2016`–`ML2021` / `ML_Lib*` — Michael Li's shared library that the others depend on. Load-bearing.
- **Ribbons:** `X1_Ribbon`, `X2_Ribbon_2`, `X3_Ribbon_3`, `X4_PowerToolsRibbon`, `Ribbon_Conc_*`, `Ribbon2025` — the toolbar UI.
- **Rebar/structural:** `RebarTools_2017`…`_2027`, `RebarPlan`, `SelectOSteel`, `Fasteners`, `ReinfVisibility`, `OutlineReinfVisibility`.
- **Productivity:** `QuickInsert` (many variants), `QuickPick`, `NewTextTools`, `EditTexts`, `DrawDetailLines`, `Visibility*`, `UnGroupAll`, `SuperSwitch`, `SuperFilter`, `SuperTag`, `PowerTools`/`PowerTags`, `ProjectTools`, `ViewSheetTools`, `NamingTools`, `ElementRenamer`/`ElementRenumber`.
- **View/sheet:** `ViewsLayout`, `ViewNamePerSheet` (2017/2023/2025), `Element3DViewBox`, `ViewFocusSync`, `Focus3DView`, `CloseWindow`, `LegendTools`/`LegendCopy`.
- **File/version:** `RevitFileVersion`, `RevitFileUpgrader`, `MetImp` (metric-imperial), `OverrideDimensions`, `DimensionExplode`, `JBPElementID`, `FootingEdit`.
- **2026-active:** `DimensionExplode_2026`, `OverrideDimensions2026`, `RebarTools_2027`, `SuperSwitch2025`, `SelectOSteel2025` — he is **still actively building** (files dated as recent as July 1, 2026).

Plus non-C# tooling:
- **NONICA** (`C:\NONICA\`) — 12 Dynamo graphs (`.dyn`): wall finish by room, 3D view/section box, numberer, renamer, text-note-to-detail-item converters, untagged-item finder.
- **pyRevit** — installed (`C:\ProgramData\pyRevit`) with a custom extension `C:\SWTOOLS\MyTools.extension` (a "Window Export" pushbutton, `script.py`).
- **Third-party:** Xrev Freebies, Bluebeam Pushbutton, plus stock Autodesk add-ins (Collaborate, BatchPrint, eTransmit, Worksharing Monitor — not his).

## 3. Source code — the gap

**Found (all under `C:\Users\michael li.old\source\repos\`, none in Git, all with `.sln`):**

| Repo | Solution | Last modified |
|------|----------|---------------|
| BeamDisallowJoinInGroup | BeamDisallowJoinInGroup.sln | 2024-11-14 |
| RevitAddinTemplate.Multiversion1 | .sln | 2024-11-03 |
| RevitAddinTemplate.Multiversion2 | .sln | 2024-11-14 |
| TextAlignment | ConvertIMP2MET.sln | 2025-04-14 |
| TypicalDetails | TypicalDetails.sln | 2025-06-24 |

That is **it**. The current `mli\source\repos` folder is **empty**. There is no source on this machine for `ML.dll`, the ribbons, `RebarTools`, `QuickInsert`, `Visibility`, `PowerTools`, `ProjectTools`, `ViewSheetTools`, `SelectOSteel`, `NewTextTools`, or the dozens of other production DLLs. **Roughly 95% of the deployed tooling has no source here.**

**Where the missing source might be** (must ask / check):
1. A **Kingston USB drive (`D:`)** was connected **July 8, 2026** — the day before this sweep (recent-items shortcut `KINGSTON (D).lnk`).
2. A **708 MB OneDrive zip** (`OneDrive_1_2026-07-08.zip`) was downloaded to his Downloads on **July 8, 2026** (contents seen were Revit models, but the personal OneDrive itself is unswept).
3. His **personal OneDrive / Autodesk / GitHub-Gitee account** — not verified.
4. An **older machine or the pre-2021 BMZ environment** — much of this code predates KOR and may never have lived on KOR hardware.

The recent USB + OneDrive activity the day before is worth a direct, non-accusatory question: where is the master source tree, and is he taking a copy?

## 4. Build method (understood, from `RevitAddinTemplate.Multiversion2.csproj`)

His build strategy is documented by the template he works from — this is genuinely useful, because it's the pattern every plugin follows:

- **One project, per-Revit-year build configurations** `R2020`–`R2025`, each with its own `OutputPath`, `DefineConstants` (`R2020`…`R2025`), and `RevitVersion`.
- **Framework by year:** .NET Framework 4.7 (2020) → 4.8 (2021–2024) → **.NET 8 (`net8.0-windows`, 2025)**. This is the exact break Autodesk forced at Revit 2025; he's already across it.
- **References** `RevitAPI` / `RevitAPIUI` / `AdWindows` / `UIFramework` via `HintPath` to each local `Program Files\Autodesk\Revit <year>\` install, `Private=False`.
- **Post-build** `xcopy` of the `.dll` + `.addin` into `%AppData%\Autodesk\REVIT\Addins\<year>\` — i.e. he builds straight into the per-user add-ins folder to test, then publishes the DLL to the shared `2015_RevitCommands` hub.

**Implication for rebuild (Gate 1):** a KOR machine needs Visual Studio + the target Revit versions installed locally (for the API DLLs), then each plugin builds per-year off this pattern. The method is clear; the missing input is the source.

## 5. Access gaps hit during the sweep

- **Remote RPC/WMI/`schtasks` are blocked** (`RPC server unavailable` / `network path not found`). Could not enumerate **scheduled tasks or Windows services** on KOR-302N remotely — so any server-side/automation component can't be confirmed or ruled out from here. Needs a local check on the machine, or firewall opened.
- **No `D:` drive** present now (the Kingston USB is unplugged).
- Personal **OneDrive** contents not swept (out of scope for a `c$` sweep).

---

## Recommended immediate actions

1. **Ask him directly for the master source tree** — this is the whole ballgame. The 5 repos here are a fraction. Frame per the continuity dossier's request list, and get it into a KOR Git repo.
2. **Account for the Kingston USB and the OneDrive zip** from July 8 — verify whether source left the building, and whether they hold the missing repos.
3. **Preserve this machine** — do not wipe/reassign KOR-302N until Gate 1 (clean rebuild) passes. The `2015_RevitCommands` hub + `ML.dll` are the crown jewels and now escrowed, but the live machine is the reference environment.
4. **Local check on the machine** for scheduled tasks/services (blocked remotely), and confirm whether Visual Studio + which Revit versions are installed for the rebuild.
5. **Decompilation fallback is viable** — the DLLs are escrowed; `ML.dll` and the productivity tools will decompile cleanly with ILSpy if source never materializes. Insurance, not plan A.
