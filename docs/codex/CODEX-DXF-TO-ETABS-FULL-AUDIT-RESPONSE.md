# VERIFIED 2026-08-31 — Ian's box, against the files published 28 Aug 19:48/19:50

Every finding below was checked against the shipped `31168-FROM-DRAWINGS.e2k`,
`31168-TOWERS-FROM-DRAWINGS.e2k`, their reports and their `-QUESTIONS.xlsx`. No ETABS source has
changed since those files were published (`git diff e03677aa..HEAD` over `Dxf/` is empty).

| # | Finding | Verdict |
|---|---|---|
| 1 | Workbook asks about a storey the file does not have | **CONFIRMED — CRITICAL, and it carries a second fault** |
| 2 | Sheet table counts tower sheets as placed | **CONFIRMED** |
| 3 | Floor-coverage warning measured pre-cut | **NOT DEMONSTRATED** — flag never fires on this job |
| 4 | Joints not recounted after the cut | **CONFIRMED — and understated; the fault is in the file** |
| 5 | Sheet member counts captured pre-filter | **CONFIRMED** |
| — | Untried input: rules-DB mismatch via env var | **REFUTED** |

**1 — CONFIRMED.** `31168-QUESTIONS.xlsx` row **S7**, status `NEEDS YOU`, reads: *"IS THIS A FLOOR,
AND HOW THICK? … **B-LEVEL 28**: 1,298 sq ft at (3273, 2327)."* The building-C file carries no
`A-`/`B-` storey at all (`grep -c '^  STORY  *"[AB]-'` → 0; its storeys are C-ROOF, C-LEVEL 9…3,
LEVEL 2, LEVEL 1 MEZZ, LEVEL 1, LEVEL P1/P2/P3, Base). The citation is exact:
`ModelQuestionnaire.cs:618` takes `sheet.Stories.FirstOrDefault()` off the pre-cut ledger, and the
questionnaire contains **no reference** to `NotesAboutStoreysThisModelHas` — the 27 Aug guard built
for precisely this does not cover this path.

**The second fault on the same workbook, which the audit did not name.** Rows **F2** and **J1** both
tell her `LEVEL 1 MEZZ` has no plate — F2: *"these are left without a plate rather than given an
invented one: LEVEL 1 MEZZ. They need a slab edge drawn… There is no closed outline on any slab
layer of these storeys."* J1: *"These storeys carry walls and columns and no slab, so they have no
diaphragm until you add one."* The file gives `LEVEL 1 MEZZ` **three plates** — `KF5`, `KF6`, `KF7`,
2,754 + 2,330 + 1,095 sq ft — her own three mezzanine slabs, closed on 28 Aug. J1 further states
*"Closure tolerance was tested at 6, 12 and 18 inches on this job and the result did not change"*;
the slab that closed it was found at a flood-fill bridge of 126 in. This is the `FloorGaps`
coverage-versus-existence defect (`E2kDocument.cs:973`) reaching the workbook with fabricated
supporting prose, on the one storey the engineer has corrected us on twice.

**2 — CONFIRMED.** The building-C report's placed-sheet table carries `A-LEVEL 28`…`32`,
`B-LEVEL 30`…`35` and `S2.21.1_2_A-LEVEL 27` with nonzero walls/columns/slabs (e.g. `B-LEVEL 33` at
28/73/2), in a file with zero `A-`/`B-` storeys.

**3 — NOT DEMONSTRATED.** The string exists at `E2kGeometryComposer.cs:1647` (cited `:1641`, six
lines off, same block) but occurs **zero times in both shipped reports** — `thinlyFloored` is empty,
and `IsMezzanineStorey` skips immediately above it at `:1639`. The pre-cut reasoning may hold on
another job; there is no evidence for it in these artifacts. Codex hedged `PLAUSIBLE`; correctly.

**4 — CONFIRMED, and the consequence is worse than stated.** Both reports print `Joints : 1835`, and
both files really do contain 1835 `POINT` definitions — so the number is not a stale print. The
building-C file **references only 760 of them from any geometry: 1,075 orphan joint definitions**,
the site composition's point table left behind by the cut. The site file is 1835/1835, zero orphans.
The defect is therefore in the `.e2k` the engineer opens, not only in the report describing it.

**5 — CONFIRMED.** `S2.22.1_1_LEVEL 33 PLAN - BLDG A` reports **49 columns** in the sheet table;
`A-LEVEL 33` in the file has **24**. That is the known grid-bubble sheet — 32 ten-inch polygon
circles in the perimeter band, part of the 96 declined set-wide — counted in the row as though they
filled the model.

**Untried input — REFUTED.** `PlanRulesFor(null)` returns built-in options without consulting the
environment (`JobPublisher.cs:154`), and `RuleSettings.LoadRequired` — the only path that reads
`KOR_ENGINEERINGTOOLS_STANDARDSDB` — is reached only when `RequireRuleSettings` is true, which
`JobPublisher.cs:110` sets from the same null check. Both paths use built-ins when the connection is
null and KorStandards when it is not. No mismatch in either direction.

**"What the report is supposed to be" — adopted.** Every unqualified count in a `*-report.txt` must
be true of the `.e2k` beside it after every cut, rename, merge, dedup and object removal; anything
that is a drawing fact or a composition-time decision must say so.

---

# CODEX RESPONSE AS RECEIVED

### The building-C workbook can ask about a tower-B floor the building-C file does not have
SEVERITY  CRITICAL
WHERE     Kor.Operations.EngineeringTools.Core/Dxf/ModelQuestionnaire.cs:618
WHAT      S7 is built from `report.Sheets`, and those sheet outcomes are the pre-cut placement ledger. It takes `sheet.Stories.FirstOrDefault()` from that uncut sheet, without checking `report.PlatesByStorey` or the finished file's storey list.
TRIGGER   `31168-FROM-DRAWINGS.e2k`, built with `TowerOnly = C`: the run composes all 31168 sheets, cuts to building C afterwards, then writes the workbook from the uncut sheet flags.
CONSEQUENCE  The engineer can receive a building-C questions workbook with an open question about a storey absent from `31168-FROM-DRAWINGS.e2k`. That is not a noisy report line; it is an answer request for the wrong building.
VERIFY    Run `python docs/etabs-handoff/read_questions.py <folder>\31168-QUESTIONS.xlsx` and then grep the building-C file for the named storey. I expect the S7 question to name `B-LEVEL 28` (or another `A-`/`B-` tower storey), and `Select-String 'STORY "B-LEVEL 28"' <folder>\31168-FROM-DRAWINGS.e2k` to return no match.

### The building-C report's sheet table treats pre-cut tower sheets as placed in this model
SEVERITY  MATERIAL
WHERE     Kor.Operations.EngineeringTools.Core/Dxf/DxfToEtabsService.cs:2221
WHAT      The report divides sheet rows by `s.Stories.Count > 0`. `s.Stories` is captured before `KeepOnlyTower`, `KeepStoreysUpTo`, `DropStoreys`, storey renames, and object cuts; unlike `summary.Flags`, it never passes through `NotesAboutStoreysThisModelHas`.
TRIGGER   `31168-FROM-DRAWINGS-report.txt`: `TowerOnly = C` cuts A/B tower storeys from the saved file, but the sheet outcomes still describe the whole-site composition.
CONSEQUENCE  The report section headed as placed sheets can list `BLDG A`, `BLDG B`, or `WEST` tower drawings with nonzero storey/member counts in the building-C deliverable. A reader checking "which drawings filled this model" is looking at drawings whose storeys are not in the model.
VERIFY    In `31168-FROM-DRAWINGS-report.txt`, grep the main sheet table for `BLDG A|BLDG B|WEST` before the `Read but not placed on any storey in this model` heading. I expect nonzero rows. In `31168-FROM-DRAWINGS.e2k`, grep `^\s+STORY\s+"[AB]-` and expect no matches.

### The floor-coverage warning is measured before the building cut and only name-filtered after it
SEVERITY  MATERIAL
WHERE     Kor.Operations.EngineeringTools.Core/Dxf/E2kGeometryComposer.cs:1641
WHAT      `Floor does not reach the structure...` is computed inside `Compose`, before the one-building cut removes foreign plates and members from shared floors. The later pass only rewrites or drops storey names, so the count and percentages remain composition-time values.
TRIGGER   31168 has shared parkade/podium floors and then a `TowerOnly = C` object cut at `DxfToEtabsService.cs:1432`; those cuts change the structure and floor extents that the warning claims to measure.
CONSEQUENCE  The building-C report can state a floor coverage percentage that is true of the whole-site composition, not the saved building-C file. That affects J5 in the workbook, which can mark the issue as a known defect or an engineer question from the wrong measurement.
VERIFY    `Select-String 'Floor does not reach the structure' <folder>\31168-FROM-DRAWINGS-report.txt,<folder>\31168-TOWERS-FROM-DRAWINGS-report.txt`. I expect the line, including count and percentages, to be byte-identical or otherwise not proportional to the different saved files. If the owner recomputes coverage from the two `.e2k` files, I expect the building-C value not to equal the printed one. PLAUSIBLE on the exact printed percentages.

### The top-line Joints count is not recounted after the cut
SEVERITY  MATERIAL
WHERE     Kor.Operations.EngineeringTools.Core/Dxf/DxfToEtabsService.cs:1640
WHAT      The post-cut recount updates walls, columns, floors, and storeys, but leaves `Summary.Points` from the whole-site composition. `DropObjects` and `DropObjectsWithNoAssign` also leave point coordinates behind, so the number printed as `Joints` is not the active joints used by kept generated members.
TRIGGER   `31168-FROM-DRAWINGS.e2k`, where the building-C cut removes other-building objects after all generated points have already been appended.
CONSEQUENCE  The report's first block can give the engineer a site-sized joint count beside a 13-storey building model. It is a count of leftover generated point definitions, not a count of joints participating in the structure she is checking.
VERIFY    Compare the `Joints        : N` line in both reports. I expect the building-C report's `N` to match, or be close to, the site report. Then count unique `KP...` names referenced by kept `KW`, `KC`, `KF`, `KS`, and `KO` connectivities in `31168-FROM-DRAWINGS.e2k`; I expect that referenced-joint count to be materially lower than the printed `N`.

### The report's sheet member counts are captured before late "not modelled" filters run
SEVERITY  MATERIAL
WHERE     Kor.Operations.EngineeringTools.Core/Dxf/DxfToEtabsService.cs:946
WHAT      `SheetOutcome` stores `geometry.Walls.Count`, `geometry.Columns.Count`, and `geometry.Slabs.Count` before the later drawing-set circle-family pass removes polygon circles from `geometry.Columns` at `DxfToEtabsService.cs:1035`, and before whole-floor sheets give up walls and columns at `DxfToEtabsService.cs:1307`.
TRIGGER   31168's grid bubbles: the code comment at `DxfToEtabsService.cs:1006` says some sheets draw 10-inch polygon circles on column layers that are later declined as columns.
CONSEQUENCE  A sheet row can show columns in the report even when those column-like shapes were explicitly not modelled. The warning says a count and diameters, but the sheet table still presents the pre-decision counts as if they filled the model.
VERIFY    In the shipped reports, grep for `circle(s) on a column layer were not modelled`; I expect a nonzero count. Then sum the `Cols` column in the report's sheet table and compare it with `docs/etabs-handoff/members_by_storey.py` or a direct `LINE "KC"` count from the `.e2k`; I expect the sheet-table sum to exceed the saved-model column count by at least the declined-circle count, plus any whole-sheet standdown/duplicate removals.

### The one ship-blocker
The blocker is the first finding: an open workbook question for a storey absent from the building-C file. A false report line is bad; an answer request for the wrong building is worse because it asks the engineer to spend judgement on an artifact she cannot inspect in the model she opened.

### The untried input that breaks next
A production `takeoff publish-job ... --land` run with `KOR_ENGINEERINGTOOLS_STANDARDSDB` set in the environment but without an explicit `--rules-db` argument. `JobPublisher.Run` uses the environment while computing reach through `PlanRulesFor`, but passes `RequireRuleSettings = request.RuleSettingsConnection is not null` into `DxfToEtabsService.Run` at `Kor.Operations.EngineeringTools.Core/Dxf/JobPublisher.cs:110`. With a null argument, the model generation path uses built-in rules while the split-planning path used KorStandards. That is a concrete mismatch between the storey split and the geometry build.

### What the report is supposed to be
The report must describe the saved file beside it. A drawing ledger can exist inside it, but every line needs to say whether it is a drawing fact, a composition-time decision, or a saved-file fact. The default meaning of an unqualified count in `*-FROM-DRAWINGS-report.txt` should be: this is true of the adjacent `.e2k` after every cut, rename, merge, dedup, and object removal.
