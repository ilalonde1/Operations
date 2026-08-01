# KOR Revit Toolset — Current-State Accounting & Rebuild Statement of Work

**Prepared:** 2026-07-13 · Ian Lalonde, Operations & IT
**For:** Drafting team working session + management sign-off
**Purpose:** A full accounting of the custom Revit tools KOR currently runs, what can be retired, a framework to capture what drafters actually need, and a Statement of Work to rebuild the toolset as a lean, maintainable, version-agnostic suite KOR owns.

---

## 1. Executive summary

KOR's custom Revit tooling was written and maintained by one person, who has departed and removed the source code. We have preserved the **compiled tools from every machine** and recovered a portion of the real source. From that we've established the true shape of what we run:

- The tools ship as **195 DLL files**, but those collapse to **78 distinct tools** — the rest are **duplicate copies built once per Revit year** (e.g., "QuickInsert" exists in **21** near-identical versions).
- On a current drafter's machine, only about **22 tools are actually loaded** in Revit. **The large majority of the 195 files are dead** — abandoned or superseded builds nobody loads.
- **Almost all of the tools are obfuscated** — deliberately scrambled by the author so the code can't be read back. We can *run* them; we largely can't *read or edit* them.
- We hold **clean, editable source for ~30 of them** (7 recovered outright + ~23 that weren't obfuscated), **including the core framework** every tool is built on.

**The opportunity:** rather than nurse 195 brittle, locked files that break every Revit release, we rebuild the tools that matter as **one clean, modern codebase KOR fully owns** — consolidated, version-resilient, source-controlled, and documented. This document accounts for what exists, proposes what to cut, gives the drafting team a structured way to say what they actually use and what's missing, and lays out the rebuild as a phased SOW with a proof-of-concept first step.

**No drafting disruption:** every existing tool keeps running throughout. This is additive.

---

## 2. Part A — What we currently have

### 2.1 The headline numbers
| Measure | Count | Meaning |
|---|---|---|
| DLL files deployed | 195 | The raw file count across machines |
| Distinct tools | **78** | Once per-Revit-year duplicates are collapsed |
| Actively **loaded** on a current machine | **~22** | What a drafter's Revit actually loads today |
| Readable / editable source available | **~30 tools** | 7 recovered + ~23 un-obfuscated (incl. the core library) |
| Obfuscated (locked, source not readable) | **169 DLLs** | Run-only; deliberately scrambled with "Obfuscar" |

### 2.2 The currently-loaded toolset (Revit 2026, a live machine)
These are the ~22 tools a drafter's Revit loads today — the real working set:

| Area | Tools loaded today |
|---|---|
| **Ribbon / menus** | X1–X4 custom ribbons + PowerTools ribbon (the toolbar the team sees) |
| **Rebar / reinforcing** | RebarMatcher, RebarInfoEditing, TagRebarAll (rebar tagging), RebarParameterEditor |
| **Text / annotation** | NewTextTools |
| **Insert / pick** | QuickInsert, QuickPick |
| **Visibility / filter** | VisibilityAMES, SuperFilter |
| **View / sheet** | ViewsLayout, ViewNamePerSheet, Element3DViewBox, CloseWindow |
| **Detailing** | DrawDetailLines |
| **File / version** | RevitFileVersion, RevitFileUpgrader |
| **Utility** | RevitTools, UnGroupAll (ProjectTools), ClimateDataEditing |

### 2.3 The full tool universe (78 distinct tools by category)
Everything that exists, grouped by function. Bold = confirmed in the active loaded set.

- **Ribbon / UI:** Ribbon, Ribbon_Conc, PowerToolsRibbon
- **Rebar / reinforcing:** RebarTools, RebarPlan, ReinfPowerTag, ReinfPowerExtent, ReinfVisibility, OutlineReinfVisibility, RebarParameterEditor, RebarMatcher, TagRebarAll
- **Steel:** SelectOSteel, SteelShape, Fasteners
- **Text / annotation:** NewTextTools, EditTexts, TextNoteTools, TextNoteAlignment, CADTextTools, ExplodeMultiLineTextNote, SuperTag, PowerTags, Numbers, TagModel
- **Insert / pick:** QuickInsert, QuickPick
- **Visibility / filter:** Visibility, VisibilityPS, SuperFilter, xxxFilters, OutlineVisibility
- **View / sheet management:** ViewsLayout, ViewSheetTools, ViewNamePerSheet, ViewNameUpdater, ViewRenumber, ViewFocusSync, OpenViews, Element3DViewBox, DuplicateView, CloseWindow, SectionNumberReplace, LegendTools, LegendCopy
- **Element / naming:** ElementRenamer, ElementRenumber, NamingTools, ColumnSymbolX, JBPElementID
- **Dimensions / lines:** OverrideDimensions, ChangeLines, DrawDetailLines
- **Structural edits:** StructuralColumnUpdater, ChangeBeamType, WallBeamTools, WallTools, UnGroupAll/ProjectTools
- **Units:** MetImp, ConvertIMP2MET, ConvertSystem2Met
- **File / version:** RevitFileVersion, RevitFileUpgrader
- **General suites:** PowerTools, RevitTools, NewTools, ToolsExtension, SuperSwitch, ReplaceDetailFamily, ClimateDataEditor, Video
- **Core framework (not tools):** ML, ML_Lib, **RvtLib** (the base library every tool inherits — recovered, readable)
- **Retire on sight:** SecuredUser (a private license/user gate — not needed)

### 2.4 Source status — what we can rebuild from what
| Bucket | Count | What it means for the rebuild |
|---|---|---|
| **Recovered real source** | 7 tools | His actual code, buildable now (SteelShape, OpenViews, ConvertIMP2MET, BeamDisallowJoinInGroup, TypicalDetails, VRP_DeleteRef, + templates) |
| **Clean-decompiled** | ~23 tools incl. **RvtLib core** | Readable C#; usable as reference and starting point |
| **Obfuscated (locked)** | 169 DLLs | Source unreadable — rebuild from **observed behavior** + config data (the DLL is the spec) |

**Key point:** even for the locked tools, the running DLL tells us exactly what the tool does, and we hold their data files (rebar standards, city data, insert lists). We do **not** need the lost source to rebuild — the binaries are the specification.

---

## 3. Part B — What we don't need (the cut list)

Retiring dead weight is most of the win. Proposed cuts, for confirmation in the meeting:

1. **All redundant year-variants.** One "QuickInsert," not 21; one "Ribbon," not 11; one "Visibility," not 8. The rebuild targets *all* Revit years from a single codebase — the per-year copies disappear entirely. (**~195 files → one codebase.**)
2. **Abandoned / never-loaded tools.** Of 78 distinct tools, only ~22 are loaded today. The ~56 that no drafter's Revit loads are candidates to drop unless the team flags one as "we still need that."
3. **Tools superseded by native Revit.** Some date to 2014–2018; Revit has since added equivalents (some view, naming, and text functions). We'll check each against current Revit before rebuilding.
4. **Non-KOR / retire-on-sight:** `SecuredUser` (a private license gate), any downloaded third-party trials, and duplicate "PowerTools/NewTools/ToolsExtension" suites that overlap.
5. **Third-party add-ins stay as-is** (RevitLookup, Bluebeam, Xrev, Autodesk tools) — not ours to rebuild.

**Net:** a realistic rebuild target is **~20–40 tools**, not 200.

---

## 4. Part C — What they NEED that they don't have (drafter worksheet)

This is the part the meeting fills in. For each tool area below, we want three things from the team:

**For every tool:** (a) *Use it daily / sometimes / never?* (b) *Works well / annoying / broken?* (c) *What would make it better?*

**Then, the gaps — the most valuable question:** *What do you do by hand, repeatedly, that a tool should do?* Seed prompts:
- Rebar: placement, tagging, scheduling, bending schedules — what's still manual?
- Sheets/views: set-up, renumbering, batch export — pain points?
- Annotation & dimensions: repetitive cleanup?
- Model QA: checks you run manually every submission?
- Concrete volumes / quantities: current process gaps?
- Anything you saw at another firm or in a paid add-on that you wish you had?

> A one-page checklist version of this (tool list with tick-boxes + a "gaps" section) accompanies this document for the meeting.

---

## 5. Part D — The rebuild: Statement of Work

### 5.1 Objective
Replace the departed developer's 195 brittle, obfuscated, per-year DLLs with **one lean, modern, maintainable Revit add-in suite that KOR fully owns and controls**, covering the tools the team actually uses, engineered so a new Revit release is a **~1-day update, not a rewrite**.

### 5.2 Design principles ("intelligent and better")
- **Consolidate:** one codebase, a handful of modules — not 200 files.
- **Version-agnostic:** a single project builds for all supported Revit years; API differences isolated so a new release doesn't break the suite.
- **Clean layered architecture:**
  1. **Agnostic core** — pure logic (geometry, units, tolerances, data): never changes when Revit does.
  2. **Revit-API shim** — the *only* code that touches version-specific API; fix a Revit change here once.
  3. **Commands** — each tool, thin, calling core + shim.
  4. **Data-driven ribbon** — one menu definition.
- **Owned & maintainable:** KOR source control (Git), automated build per Revit year, **no obfuscation**, documented, tested — anyone competent can maintain it.
- **Behavior-verified:** each rebuilt tool is run side-by-side against the old DLL to confirm identical behavior before cutover.

### 5.3 Honest constraints
- Revit **≤2024 runs on .NET Framework 4.8; 2025+ on .NET 8** — an unavoidable two-runtime split, handled by multi-targeting from one codebase.
- "Version-agnostic" means *low-effort to update*, not *never touched* — the API will change; we make absorbing it cheap.
- This is a **real engineering project** (see effort), not a recovery script.

### 5.4 Phased plan
| Phase | Work | Outcome |
|---|---|---|
| **0 — Scope** | Drafter usage map (this meeting) → final tool list | A finite, agreed build list (~20–40 tools) |
| **1 — Proof of Concept** | Stand up the clean architecture: agnostic core + Revit shim + **one real tool**, multi-targeted, building for all target Revit years, in KOR Git with automated build | Proof the model works end-to-end; a template every tool follows |
| **2 — Core & framework** | Rebuild the core library (from the recovered `RvtLib` source) + the ribbon | The foundation all tools plug into |
| **3 — Tool migration** | Rebuild tools in priority order: source-in-hand first (7 recovered + decompiled), then reimplement the locked ones from behavior + data | Tools ship in waves, verified against the old DLLs |
| **4 — Cutover & retire** | Deploy the new suite alongside, migrate machine-by-machine, retire the 195 old DLLs | One clean suite in production, old sprawl gone |

### 5.5 Proof-of-Concept definition (the immediate, concrete first build)
Deliver a working skeleton that proves the whole approach before committing to the full port:
- The **agnostic core + Revit-API shim** layers, established.
- **One real, useful tool** rebuilt clean inside it (candidate: a well-understood one we have source for, e.g. a text/units or view tool).
- **Multi-targeted** so one build produces the add-in for your target Revit years (e.g., 2024 + 2025 + 2026).
- Committed to a **KOR Git repository** with an **automated build**.
- A short **"how to add a tool" guide** so the pattern is repeatable.

This PoC is small, low-risk, and gives management something concrete to green-light the full effort against.

### 5.6 Deliverables
- This accounting + the drafter worksheet (done).
- The agreed tool scope (from the meeting).
- The PoC skeleton + build (Phase 1).
- The rebuilt suite, in KOR Git, documented, with an automated per-Revit-year build (Phases 2–4).
- Preserved assets already secured in `P:\Recovery` (source, DLLs, families, templates, standards).

### 5.7 Resourcing & effort (honest)
- Needs a **competent Revit / C# developer** (in-house hire, contractor, or partner). This is the real decision — the tooling can't maintain itself, which is exactly the position we're in now.
- **Rough scale:** PoC ~1–2 weeks; core + first useful wave ~4–8 weeks; full ~20–40-tool suite a few months, scope-dependent. Firm numbers after the scope meeting.
- **Interim:** existing tools keep running; no urgency-driven risk.

### 5.8 Risks & mitigations
| Risk | Mitigation |
|---|---|
| Locked tools hard to reimplement exactly | Behavior + data files as spec; verify side-by-side; prioritize by actual usage |
| Rebuild under-scoped / scope creep | Usage map fixes the list; PoC-first proves effort before commitment |
| "It works today, why bother" | Every Revit release currently risks breaking 195 unmaintainable files with no author — this removes that standing risk |
| Losing the assets we recovered | Already backed up to `P:\Recovery`; source under Git going forward |

---

## 6. Recommended next steps
1. **Run the drafter meeting** with the worksheet — lock the tool scope (keep / cut / need).
2. **Green-light the Proof of Concept** (Phase 1) — small, concrete, de-risks the whole project.
3. **Decide the resource** — who builds it (hire / contractor / partner).
4. I proceed to build the PoC skeleton on the agreed scope for sign-off.

*Everything referenced here — the recovered source, the compiled tools, families, templates, and standards — is preserved and organized under `P:\Recovery`.*
