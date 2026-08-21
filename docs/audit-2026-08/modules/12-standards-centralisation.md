# Module 12 — Standards centralisation: the detail estate, the Revit palette, and the process change

**Audited 2026-08-21.** Spans three repos (`Operations`, `KOR.RevitTools`, `KOR.Drafter`), two
databases, and one file share. Audited as **one chain**, which is the thing no other module did.
Evidence tiers: `RUN` executed · `QUERIED` live state read · `READ` source only · `DOC` a document says so.

Module 11 covers RevitTools/Drafter as products. This module covers the *standards chain* only and
does not repeat it. Where a finding is 11's, it is cited, not re-derived.

---

## 1. What I searched

**Read first, as instructed**
- `Operations/docs/KOR-StandardDetails-Governance-Review-2026-08-06.md` (in full), then verified each
  of its three headline numbers live (§2 below). Code it describes last changed **2026-05-07**
  (`git log -1 --date=short -- Kor.Operations.App/StandardDetails/*`) — the doc is **newer than the
  code, so it is current**, unusually for this repo.
- `Operations/CODEX-STDDETAILS-REVIEW-PROMPT.txt` (the target picture: *"One cockpit, two registers,
  one gatekeeper"*).
- `Operations/docs/audit-2026-08/modules/11-revit-drafter.md` (in full, to avoid duplication).

**SQL, SELECT only**
- `KorTransmittals` on `KOR-APP01\SQLEXPRESS` as `transmittals_app` — credential taken from the app's
  own `Kor.Operations.App/App.config:162`, not from the prompt file. Windows auth was tried first and
  **failed** (`Login failed for user 'kor\ilalonde'`) for both `KorTransmittals` and `KorStandards`,
  so there is no Windows-auth path. Row counts on Documents / DocumentVersions / FileBlobs /
  ApprovalRecords / PublicationRecords / AuditEvents / DocumentGroups; status distribution; full
  contents of ApprovalRecords, PublicationRecords, DocumentGroups; `MIN/MAX(EventUtc)`;
  `sys.columns` on AuditEvents and Documents; the 12 documents joined to their blob storage paths.
- `KorStandards` as `standards_reader` (same credential module 11 used, published in
  `KOR.RevitTools/PALETTE-README.md:20`) — `COUNT/SUM/COUNT(DISTINCT)` on `detail.vw_PaletteCatalog`;
  distribution by `Confidence`, `Discipline`, `SizeToken`; `COUNT(*)` on `detail.vw_DetailPlaceable`;
  `sys.columns` on the view. `SELECT` on `detail.Detail` and `detail.Component` → **permission
  denied** (login is correctly scoped to the two views).

**File share (targeted `Test-Path` / single-level `[System.IO.Directory]` calls, never `-Recurse` over SMB)**
- `\\Kor-fs01\Drafting\Document Details\` — all 12 blob paths; folder ACL; `cmd /c dir`.
- `\\Kor-fs01\Drafting\_QUARANTINE-app-docs-2026-08-03\` — `dir /s /b`, file sizes and mtimes.
- `\\Kor-fs01\Drafting\KOR-Deploy\` — `content\`, `current\<year>\`, `version.txt`.
- `\\KOR-302N\C$\KOR.Drafter\tasks\template\work\` and `\\KOR-302N\C$\ProgramData\KOR\kor-tools.json`.

**Builds / tests / scans I ran**
- `dotnet build Kor.Operations.App -c Debug` → **0 errors, 2 NU1902 warnings, 3.1 s** `[RUN]`
- `dotnet test tests/KOR.RevitTools.Core.Tests -c Debug --filter FullyQualifiedName~DetailsCatalog`
  → **4 passed, 0 failed, 14 ms** `[RUN]`
- Byte-scan of the **deployed** `\\Kor-fs01\Drafting\KOR-Deploy\current\{2025,2026}\KOR.RevitTools.dll`
  (both 330,664 B, stamped 2026-07-30 10:31) for the literals `vw_PaletteCatalog`, `DetailsPalette`,
  `detailsPalette` → **all three absent** `[RUN]`
- `git branch -a -v`, `git rev-list --left-right --count main...feature/details-palette`,
  `git show main:…/ToolCatalog.cs | grep -c DetailsPalette` in `KOR.RevitTools`.

**Greps**
- `grep -rn "KorStandards|vw_PaletteCatalog|detail\.Detail|detail\.Component|KOR-D-|DetailNumber|SheetSize|Variant"`
  over `Kor.Operations.App/`, `Kor.Operations.Data/`, `Kor.Operations.Services/` → **zero hits**.
- `grep -rn "TODO|FIXME|HACK|NotImplementedException|NotSupportedException"` and
  `grep -rnE "catch\s*(\([^)]*\))?\s*\{\s*\}"` over `Kor.Operations.App/StandardDetails/` → **zero and zero**.
- `grep -rniE "detail ?sheet|DetailSheet"` over all of `KOR.RevitTools` → **zero hits**.
  **⚠ This grep produced a false negative and I initially drew a wrong conclusion from it.** The
  capability exists as a whole **Sheets** ribbon panel; it is simply never called "detail sheet"
  anywhere in the code, because each command is named for its verb (`CreateSheetsFromList`,
  `PlaceViewsOnSheet`, `RenumberViewsOnSheet`, `CopyDetailToViews`). **Methodological note for future
  readers: a phrase-grep proves a phrase is absent, never that a capability is.** The correct method,
  used for the assessment now in §5.7, is to read `Framework/ToolCatalog.cs` for the category list and
  then read the command implementations in `Tools/<Category>/`. Corrected 2026-08-21 after the owner
  identified the error.
- `grep -rni "michael|mli\b|quick command"` over `KOR.RevitTools` (→ `docs/addin-inventory.md`,
  `build/Remove-LegacyTools.ps1`, `build/install-loader.ps1`, `config/kor-tools.json`).
- Migration headers in `KOR.Drafter/db/`: 004, 005, 005b, 008, 011, 013, 015, 016, 018, 020, 021,
  023, 025, 027; row counts of the `VALUES` blocks in the four adoption migrations.

**Delegated (read-only sub-search)** — the `KOR.Drafter/standards/` corpus, `RULINGS.md`, the
conformance run history, `process-record/` (48 briefs, 85 reports, 17 evidence CSVs), and a hunt for
any drafter-adoption evidence. Its findings are marked `[DOC]` or `[QUERIED]` inline in §5.

---

## 2. What this module is

**The claim.** KOR's standard details had drifted: the same detail existed under many names in many
models, nothing declared what was canonical, and the Revit tooling that served drafters belonged to a
BIM developer who has left. Over July–August 2026 the firm built the pieces of a fix: a **census** of
every detail name across the model fleet (7,489 names → 468 genuinely reusable), a **register** in
SQL that mints an immutable number for each canonical detail (`KOR-D-00001`…`KOR-D-00612`), a
**component canon** of the `.rfa` families a detail may be built from, a **repeatable conformance
test** with an eight-check scoreboard, a **governance app** in the Operations desktop suite with
draft→submit→approve→publish and PDF watermarking, and a **Revit palette** that would let a drafter
search that register and drop a standard detail into the job model.

**The reality.** Those are six real pieces and they are **not connected to each other.** The register
is live and populated. The app governs twelve unrelated booklet PDFs and contains not one line of
code that has ever heard of `KorStandards`. The palette is written, tested and good-looking, but it
lives on an unmerged branch, is absent from the deployed fleet payload, is switched off on every
machine, points at a template file that is not at the path it names, and would show an empty list
even if all of that were fixed — because the promotion event that marks a detail placeable does not
exist in any code, so all 612 details are still `unverified`. What a user can actually see and do
today is: open a WPF window listing twelve PDFs in four groups, and click Open — which fails, because
the files were moved off their recorded paths on 2026-08-03 and never relinked.

---

## 3. How you would demo it

**Honest answer: three static screens and a story. No end-to-end click path exists.**

*Screen 1 — the register (works, and is the best asset here).* From any LAN/VPN machine:
`sqlcmd -S "KOR-APP01\SQLEXPRESS" -U standards_reader -P … -d KorStandards -Q "SELECT TOP 30 DetailNumber, Title, Discipline, SizeToken FROM detail.vw_PaletteCatalog ORDER BY DetailNumber"`.
612 numbered details, four disciplines, real sheet-size tokens. `[RUN]` This is genuinely impressive
and takes ten seconds. Pipe it into a grid rather than showing a console.

*Screen 2 — the census.* `standards/census/DETAIL-CENSUS.csv`, 7,489 rows, columns
`DetailName,Kind,ModelsUsing,RawSpellingVariants`, dated 2026-07-31 `[QUERIED]`. The line that lands
with architects is in `standards/README.md:31`: **7,489 detail names → 468 true standards (used in
10+ models) vs 4,974 one-offs.** That is a measured statement of exactly the problem MVE has.

*Screen 3 — the governance app.* Operations app → Home → **Standard Details** tile (visible only to
`SecurityGroup.StandardDetails.Members`, `App.config:125` — three people). The window has a group
tree (CAD / Concrete / Revit / Wood Frame), a records grid, a revisions grid, and
Submit/Approve/Reject/Publish buttons that light up per status. **Do not click Open in the demo:**
all 12 records fail with "File not found in storage" (§5.1). Requires KOR LAN.

*Screen 4 — the Sheets panel, and this is the one thing here you can actually click.* KOR Tools ribbon
→ **Sheets**. On Autodesk's *Snowdon Towers* sample model (module 11's playbook deliberately uses it
so nothing depends on KOR's template): **Create Sheets** from `S-101=Typical Details; S-102=Sections`
→ **Views to Sheet**, pick several drafting views, watch them auto-arrange in a grid inside the title
block → **Align Viewports** → **Renumber Views**, clicking the viewports in the order you want them
numbered → **Name Views by Sheet**. Requires **only a machine with Revit** — no VPN, no SQL, no KOR
share. This is the strongest demo asset in the module and it survives being run at MVE's office
(§5.7) `[READ]`.

**What cannot be demoed:** the Revit palette (§5.2 — five independent blockers) and therefore anything
that starts from the central register. The sheet builder works but is fed by hand, not by the
catalogue. If someone asks "show me a drafter pulling a standard detail out of the register", there is
nothing to show.

---

## 4. Completeness

| Capability | State | Evidence |
|---|---|---|
| **Detail register** — 612 canonical `KOR-D-#####` numbers, immutable, discipline-filed | `WORKING` | `QUERIED` — 1,079 palette rows / **612 distinct DetailNumber**; disciplines Concrete 288, Wood Frame 148, General 132, Steel 31, null 13 |
| **Sheet-size variants in KorStandards** | `WORKING` | `QUERIED` — `SizeToken`: D 153, D LOW RISE 102, 1E LOW RISE 81, E 68, E1 28, null 647 |
| **Component canon** (`detail.Component`) | `PARTIAL` | `READ` — 331 loaded by `005b` (its own guard asserts `<> 331` → abort), +17 `008`, +7 `011`, +17 `015`, +7 `016` = **~379 rows**; net after D3 merge/retire **not verifiable — `standards_reader` has no SELECT on `detail.Component`** |
| **Conformance scoreboard**, 8 checks, versioned runs | `WORKING` as a discipline | `READ`/`DOC` — 12 of 14 runs recorded as migrations; run #14 `027_RecordEightGreen.sql:26-33` records C1–C8 all `pass`, 2026-08-06. **Cannot be verified live** (no grant on `conformance.*`) |
| **Governance app**: records, versions, blobs, SHA-256, status workflow, approvals, publication, audit, group tree | `WORKING` (machinery) | `RUN` (builds clean) + `QUERIED` (12/12/12 rows, 4 groups, 43 audit events) |
| **PDF watermark-on-open** for non-published revisions | `WORKING` | `READ` — `StatusWatermarkRenderer.cs:63` tiles rotated translucent red text over every imported page; non-PDF gets a status-tagged copy + warning (`:44`) |
| **The 12 governed masters actually open** | `DEAD` | `QUERIED` — all 12 storage paths `Test-Path=False`; source folder is **empty** (§5.1) |
| **App ↔ KorStandards link** (the gatekeeper's promotion) | `DEAD` — never built | `RUN` — grep for `KorStandards`/`detail.*`/`DetailNumber`/`SheetSize`/`Variant` across the whole Operations app: **zero hits** |
| **Revit details palette** | `STUBBED` in practice | `RUN` — absent from `main`; absent from the deployed DLL; `detailsPalette` config absent on every machine checked; template path missing; 0 placeable |
| **Custom detail-sheet creation** (the Sheets panel: create, place-in-grid, align, distribute, renumber detail numbers, name-by-sheet, batch PDF) | `WORKING` — 8 of 11 commands, 3 `PARTIAL` | `READ` (implementations, not tooltips) + `RUN` (7 `GridPacker`/`ViewNameComposer` tests pass). **Corrected — see §5.7.** Caveat: it is not fed by the central catalogue |
| **Michael Li replacement** (ribbon toolset) | `WORKING` | `DOC`+`QUERIED` — see §5.4 |
| **Drafter adoption of any of it** | `UNKNOWN`, and no instrument exists to know | see §5.5 |

**Marker counts, `Kor.Operations.App/StandardDetails/` (11 files, ~118 KB):**
`TODO` / `FIXME` / `HACK` / `NotImplementedException` / `NotSupportedException` — **0**.
Empty catch blocks — **0** (13 `catch` keywords, every one logs or surfaces a message). This is a
clean module by marker count; its defects are of shape and wiring, not of sloppiness. The only debris
is `Kor.Operations.App/StandardDetails/_test.txt`, six bytes containing `test`, committed in the
initial commit `74272b2e` (2026-03-11) and never removed.

---

## 5. What is broken or risky

### 1. Every one of the 12 governed masters is unopenable. The files were moved and never relinked. `[QUERIED]`

This resolves the governance review's open CAUTION ("could not perform SHA recheck — this could be
missing files, a share issue, or path visibility"). It is none of those. It is a move.

- All 12 `FileBlobs.StoragePath` values point under `\\Kor-fs01\Drafting\Document Details\<title> (ID n)\v1\<guid>.pdf`.
- `cmd /c dir "\\Kor-fs01\Drafting\Document Details"` → **`0 File(s)`, `2 Dir(s)`** (i.e. `.` and `..`
  only). Folder mtime **2026-08-03 21:24**. My account holds `Modify` on it (`Get-Acl`), so this is
  not an ACL artefact.
- `\\Kor-fs01\Drafting\_QUARANTINE-app-docs-2026-08-03\` contains **11 of the 12** blobs, folder
  structure intact, original mtimes preserved (2025-04-26 → 2026-02-24).
- **The twelfth is gone.** `cc81b796cf36453fa4dfe30789610bd9.pdf` — DocumentId 36, *KOR SHEARWALL &
  COLUMN PRESENTATION STANDARDS* — is not in the quarantine folder and not under `Document Details`.

Consequence in the app: `StandardDetailsFileStore.cs:100` returns `FileMissing`, and
`StandardDetailsWindow.Logic.cs:412` puts up **"File not found in storage."** for all twelve.
There is no relink or restore UI — the governance review already established that recovery is a
DB/file-server operator task (`:153`). A `[BEFORE-DEMO]` `UPDATE dbo.FileBlobs SET StoragePath = REPLACE(StoragePath, '\Document Details\', '\_QUARANTINE-app-docs-2026-08-03\')`
would fix 11 of 12 in one statement — **but this audit is read-only; the owner must run it.**

### 2. The Revit palette is blocked four independent ways, any one of which alone kills it. `[RUN]/[QUERIED]`

Module 11 named the symptom (`0 placeable`). Here is the cause chain, and all four links are broken:

| # | Blocker | Evidence |
|---|---|---|
| a | **Not in the product.** The palette exists only on `feature/details-palette` (`c8cdde9`, `9687ad8`, both 2026-08-06). `git rev-list --left-right --count main...feature/details-palette` = `0 2`. `git show main:…/ToolCatalog.cs \| grep -c DetailsPalette` = **0** | `RUN` |
| b | **Not deployed.** The fleet payload at `\\Kor-fs01\Drafting\KOR-Deploy\current\{2025,2026}\KOR.RevitTools.dll` is stamped **2026-07-30**, five days before the palette was written. Byte-scan for `vw_PaletteCatalog`, `DetailsPalette`, `detailsPalette`: **all absent** | `RUN` |
| c | **Switched off everywhere.** `DetailsPaletteRegistration` (`ToolCatalog.cs:33`) only adds the button when `%PROGRAMDATA%\KOR\kor-tools.json` has a `detailsPalette` section. This workstation: file absent. **KOR-302N** (the designated pilot machine): file present, 85,883 B, mtime 2026-07-28, `detailsPalette` **not in it**. The pilot has never been run | `QUERIED` |
| d | **Nothing is placeable, by construction.** `db/023_StandardsReader.sql:33` defines `IsPlaceable = CASE WHEN Confidence IN ('content-verified','human-confirmed') AND VariantsDiverge = 0 THEN 1 ELSE 0 END`. Live: **1,079 of 1,079 rows are `Confidence='unverified'`** — a single group, no exceptions. `018_MintDetailNumbers.sql:4` says this is deliberate: *"ALL Confidence='unverified' — the placeable gate holds everything until verification campaigns promote entries."* **No verification campaign has run, and no code anywhere writes `human-confirmed`.** The promotion event is the missing keystone of the whole design | `QUERIED` + `READ` |

**And a fifth, if the other four were fixed:** `DetailsPaletteCommand.cs:42` requires the template at
`options.TemplatePath`. `PALETTE-README.md:20` documents that path as
`\\Kor-fs01\Drafting\KOR-Deploy\content\Kor_Structural_Standards_Template_R25.rvt`. That path
**does not exist** — `KOR-Deploy\content\` holds exactly two folders (`bolt-thumbs`,
`fastener-thumbs`) and **zero files** `[QUERIED]`. The only copy of the template is
`\\KOR-302N\C$\KOR.Drafter\tasks\template\work\Kor_Structural_Standards_Template_R25.rvt`
(129,552,384 B, 2026-08-06 19:30), on one workstation, alongside nine `_BEFORE-*` snapshots. The
command would show *"The details template is not available"* and cancel.

**What it would take to make the palette usable** (in dependency order, none of it started):
publish the template to the deploy share (S); merge and deploy the branch (S); add the
`detailsPalette` section to the pilot machine's config (S); build a promotion path that writes
`Confidence='human-confirmed'` — the whole of §5.3 (L). Until the last one exists, the honest
interim demo is `showUnverified: true`, which shows all 612 with an "unverified" badge
(`KorDetailsPaletteForm.cs:189, 202, 211`).

### 3. "One cockpit, two registers, one gatekeeper" is currently zero cockpits, zero registers and no gate. `[RUN]`

The Codex prompt states the intent verbatim. Measured against it:

- **Register 1 (COMPONENTS, ~379 `.rfa` families):** not surfaced anywhere in the app. Not even
  readable — `standards_reader` has no grant on `detail.Component`, and no `detail.vw_ComponentRegister`
  view exists. Cannot be built today without a DBA action.
- **Register 2 (DETAILS, 612):** not surfaced anywhere in the app.
- **The gatekeeper's approval as the promotion event:** does not exist. `DecideAsync`
  (`StandardDetailsRepository.cs:496-527`) updates status, inserts an `ApprovalRecords` row with a
  **hardcoded** comment (`:522`), inserts an audit event — and stops. No outbox, no cross-database
  write, no `DetailHistory`. The governance review's recommendation (its Part 2C) has not been started.
- **The identity mismatch is total, not partial.** The 12 governed records are *booklets* — "KOR
  DETAILS - CONCRETE", "REFERENCE LIBRARY - STANDARD HATCHING OR SHADING PATTERNS", "SIZE E - GENERAL
  NOTES AND TYPICAL DETAILS". A KorStandards row is *one drafting view*, e.g. `KOR-D-00021 CONCRETE
  BASEMENT WALL DETAIL 01`. These are not the same kind of object at different scales; they are a
  PDF-package register and a Revit-view register that happen to share the word "detail". **Confirmed,
  not merely implied: the app↔KorStandards link does not exist at all.**

Schema confirms the variant gap is still open: `Documents` columns are
`DocumentId, DocumentUid, Title, Description, CreatedByUserId, CreatedUtc, UpdatedByUserId, UpdatedUtc, RowVersion, DocumentGroupId`
— **no `DetailNumber`** `[QUERIED]`. Nothing from the governance review's GO list has been built in
the 15 days since it was written.

### 4. The module has not been used since 2026-03-12. `[QUERIED]`

- All 12 `Documents` were created in a **59-minute window on 2026-02-26** (`MIN/MAX(CreatedUtc)` =
  04:12:12 → 05:12:02).
- **All 12 live `DocumentVersions` are Status 0 (Draft). `SUM(IsCurrentOfficial)` = 0.** Nothing has
  ever been published and left published.
- `ApprovalRecords` holds **one** row and `PublicationRecords` **one** row, both dated 2026-02-26,
  both pointing at `DocumentVersionId = 83`, which no longer exists as a live version. Both comments
  are the hardcoded strings. Both `DecidedByUserId`/`ActedByUserId` are the same synthetic GUID
  `1FDE84A8-…` — `CreateStableUserGuid(Environment.UserName)`, not an enterprise identity.
- `AuditEvents`: 43 rows, `MIN(EventUtc)` 2026-02-24 01:47, **`MAX(EventUtc)` 2026-03-12 18:14**.
  The last eight events target `DocumentVersion` ids 95, 96, 97 — none of which are live rows.

This is a developer's test dataset that was never handed to the firm, not a governance system in
service. Five months of silence is the single most important context for any claim made about it.

### 5. The process change: real artefact discipline, no measurable process adoption. `[DOC]/[QUERIED]`

The prompt asked not to skip this and not to manufacture a narrative. Both halves need saying.

**What is genuinely there, dated and numeric:**
- `standards/RULINGS.md` — a ruling register created 2026-08-01, **~29 rows** across three ID series
  (G1–G9 governing, E1–E11 engineer content, D1–D5 assistant-made), each carrying a decider and a
  source. Named deciders: Jim DesRoches, Rory Beirne, Simon Szarkiewicz, Kevin Wurmlinger, Ian
  Lalonde. Dated rows span **2026-07-31 → 2026-08-02**. The governing ruling is G1: *"Revit is master.
  CAD is generated output from it. One version. | Jim DesRoches | 2026-07-31"*. `standards/README.md:19`
  makes the register binding: content in the corpus but not in the register is *"CARRIED, not
  RATIFIED, and must never be presented to anyone as an approved standard."*
- The census: **7,489 detail names → 468 true standards (10+ models) vs 4,974 one-offs**, measured
  2026-07-31.
- 1,858 CAD-vs-Revit divergent lines classified; a markup lexicon built from **3,172 human-marked
  PDFs** across seven engineers.
- The conformance scoreboard, `db/004_CreateDetailAndConformance.sql:444-459` + `006_RunConformance.sql`:
  eight checks with `not-implemented` as a first-class status (so a check cannot pass by being
  unwritten), **14 runs**, ending 8/8 green on 2026-08-06.
- `process-record/`: 48 executed briefs, 85 reports, 17 evidence CSVs, 2026-07-27 → 2026-08-06.
- One head-to-head: `process-record/exam/31202-01/EXAM-SCORECARD.md` — machine vs production drafter
  on job 31202-01, markup by Jim DesRoches, graded 2026-07-29, **PASS, 12/12 markup items**.
- One explicit before/after: `KOR.Drafter/docs/DOCUMENTATION-BRIEF.md:56-68`, e.g. *"Detail identity |
  before: none anywhere | after: KOR-D-00001..00612, DB + in-model"*; *"Third-party identity | before:
  GairWilliamson on 17/17 sheets + Saved By: ML | after: zero contaminants (14-term sweep)"*.

**What is not there — and this is the honest answer to "did the process change":**
- **No adoption instrument of any kind.** No count of drafters using KOR Tools, no telemetry, no
  usage log, no training-attendance record, no announcement to staff, no install count. Searched both
  repos for `rollout|adoption|telemetry|training|announce`; the only training document is
  `KOR.Drafter/docs/TRAINING-CURRICULUM.md`, subtitled *"Virtual Drafter — Training Curriculum"* —
  drills for the machine, not for humans.
- `\\Kor-fs01\Drafting\KOR-Deploy\version.txt` **does not exist**, though
  `KOR.RevitTools/docs/OPERATIONS-RUNBOOK.md:139` treats it as the deployment stamp `[QUERIED]`.
- **The production template was deliberately not touched.** `standards/README.md:43-44`: *"The server
  template (`00 Templates\Wood_Framing\...R25.rvt`) is untouched and stays untouched until the gate
  lifts."* The 294-of-299 detail load landed in a **work copy** on 302N. No document shows ratified
  content reaching a production template or an issued drawing.
- **The one behavioural change contemplated was abandoned.** `RULINGS.md:129` records Kevin
  Wurmlinger's row: *"Will not action a process request. Asking costs goodwill for nothing"* — and
  closes *"the largest single automation win — 'write TAG C4 not C4' across ~51,000 marks — is CLOSED
  as a behavioural fix."*
- `KOR.Drafter/docs/PLAN-2026-08-04.md:48` and `DOCUMENTATION-BRIEF.md:68` both put drafter delivery
  in the future tense: *"palette built, gated, awaiting pilot."*

**Plain statement:** the *artefact* estate was measurably centralised — that is well evidenced and
dated. The *process* — what a drafter does differently on Monday — has no evidence of having changed,
because nothing was shipped to drafters and nothing measures them. Say it that way if asked. The
`RULINGS.md` register is the strongest process artefact and it is two days of decisions from three
weeks ago.

### 6. Michael Li replacement: genuinely KOR-owned, with named residue. `[DOC]+[QUERIED]`

- **What his tooling did:** an obfuscated, source-less estate of ~195 loose DLLs — the X-series
  ribbons (`X1_Ribbon`…`X4_PowerToolsRibbon`), `Z0`–`Z5` utilities including `Z5_RebarMatcher` and
  `Z2_ViewNamePerSheet`, `NewTextTools`, `DrawDetailLines`, `QuickInsert/QuickInsert9/QuickPick`,
  `UnGroupAll`, `VisibilityAMES`, `CloseWindow` (`docs/addin-inventory.md:63`).
- **What replaced it:** `KOR.RevitTools` — one source-controlled codebase, 137 tools, Revit 2020–2027,
  with named successors to his tools (`RenameTypesCommand`, `UngroupAllCommand`,
  `ViewNameBySheetCommand`, `QuickInsertCommand`, `RebarMatchTagCommands`). Removal is scripted and
  **reversible**: `build/Remove-LegacyTools.ps1:41` matches `Michael\s*Li|ML\s*Xpress|MLI\b|BuildIT`
  and backs up before disabling; `Restore-LegacyTools.ps1` reverses it; `install-loader.ps1:148`
  wires it into install.
- **Status of the old estate:** *"none are active"* — every Michael-Li manifest across every year
  folder 2020–2026 carries `.disabled`; the `Revision Cloud Manager` bundle that embeds his
  `ML_Lib2014/2016` DLLs is disabled at both levels (`addin-inventory.md:109-116`, audited 2026-07-14).
- **What is still dependent on him — and it is the *content*, not the code.** `config/kor-tools.json`
  ships **at least 10 `MLI-`-prefixed families** as live palette entries, e.g.
  `:1036 "\\\\Kor-fs01\\Drafting\\009_REVIT CUSTOMED\\Detail Components\\--MLI-PL W-BOLTS.rfa"`,
  `--MLI-CLOUD`, `MLI-PL W- ADJUSTABLE BOLTS`, `MLI-PL W-BOLTS NELSON`, `MLI-Dbl Nut 2 Ends-Side`,
  `MLI-SLAB-STEP.bmp`. `ToolCatalog.cs:194` labels a whole ribbon group
  *"Callouts + wood plan (from Michael's Quick Commands sheets)"*, and
  `config/kor-tools.sample.json:9` still says a palette group is *"pending confirmation of Michael's
  exact curation from Lindsay's Q1 screenshots."* Two orphan artefacts remain named in the inventory:
  `DimensionExplode2026.dll` (active manifest, assembly missing, placeholder AddInId, spoofed
  `VendorId=ADSK` — *"very likely Michael Li's"*) and his `michael li.old` profile on KOR-302N.
- **Verdict:** the *code* is fully KOR-owned and the continuity story is strong — module 11 proved the
  add-in builds for R23 and R25 on a machine with no Revit installed. The *detail-component library*
  is still substantially his authorship, carried forward by filename. That is normal and defensible,
  but it is the true answer to "is anything still dependent on him."

### 7. Custom detail-sheet creation: the **Sheets panel works**. The catalogue half is what is missing. `[READ]+[RUN]`

**Retraction.** An earlier draft of this audit marked this capability `DEAD` on the strength of a
phrase-grep that found no occurrence of "detail sheet". That was wrong and the owner corrected it.
The capability is a whole ribbon panel; it is named for its verbs, not for its noun. I have now read
the implementations — not the `ToolCatalog.cs` tooltip strings, which are marketing text — for the
**Sheets** and **Details** categories only. (The ribbon has 18 categories and 137 tools; module 11
owns the catalog as a whole and this module does not re-audit it.)

| Command | file:line | State | Note from reading the implementation |
|---|---|---|---|
| **Create Sheets** `CreateSheetsFromListCommand` | `Tools/Sheets/SheetCommands.cs:77` | `WORKING` | Title-block picker, `S-101=Foundation Plan; S-102=…` list. On a duplicate or invalid number it **deletes the sheet it just created** (`:127-133`) so no orphan auto-numbered sheet is left behind. Reports the created count |
| **Views to Sheet** `PlaceViewsOnSheetCommand` | `Tools/Sheets/PlaceViewsOnSheetCommand.cs:19` | `WORKING` — the best-built tool in the module | Excludes views already placed (a view can live on only one sheet); creates the viewports first and measures their **true** footprints rather than guessing; derives the usable region from the title block's bounding box inset 10 mm; **starts the grid below any pre-existing viewports** so it never overlaps existing sheet content (`:99-107`); packs via `GridPacker`, which lives in `KOR.RevitTools.Core` and is unit-tested; names every skipped view in the result dialog |
| **Renumber Views** `RenumberViewsOnSheetCommand` | `Tools/Sheets/SheetCommands.cs:154` | `WORKING` — notably careful | Click-order pick loop, then a **three-pass** assignment: park every picked viewport on `KRTMP<i>` so finals cannot collide with a picked sibling; assign finals; for anything still stuck, restore its original number or take the lowest free one — so **no viewport is ever left holding a temp value**. Guards `step == 0`, which would have duplicated numbers. Reports the blocked count separately |
| **Copy Detail to Views** `CopyDetailToViewsCommand` | `Tools/Annotation/DetailingCommands.cs:21` | `WORKING` | Filters the selection down to elements **owned by the active view** before copying — model elements cannot be copied view→view and would throw; restricts targets to the same `ViewType`; disambiguates duplicate view names by element id; reports views *and* elements copied |
| **Name Views by Sheet** `ViewNameBySheetCommand` | `Tools/ViewNaming/ViewNameBySheetCommand.cs:20` | `WORKING` | Composes `SheetNo_DetailNo_Title` via the tested `ViewNameComposer`. Seeds its uniqueness set from **every** view name because Revit enforces uniqueness across the whole view namespace — otherwise `MakeUnique` hands back a name Revit rejects and the view is silently skipped. **This is a direct Michael-Li replacement:** its docstring states it is an on-demand rebuild of his `Z2_ViewNamePerSheet`, *"the same result as the legacy live IUpdater, but run when the drafter chooses and with no background hooks"* |
| **Align / Distribute Viewports** | `Tools/Sheets/SheetComposeCommands.cs:20, :108` | `WORKING` | Positions by box centre and outline Min/Max; six alignment targets; requires ≥2 selected viewports |
| **Legend to Sheets** `CopyLegendToSheetsCommand` | `Tools/Views/LegendRenumberCommands.cs` | `WORKING` | Multi-select sheets by `Number - Name`; one transaction; reports placed count |
| **Renumber Sheets** `BatchRenumberSheetsCommand` | `Tools/Sheets/SheetCommands.cs:20` | `WORKING` | Find/replace + prefix over selected or all sheets |
| **Duplicate Sheet** `DuplicateSheetWithViewsCommand` | `Tools/Sheets/SheetCommands.cs:241` | `PARTIAL` | The logic is right — duplicates with detailing, places legends as-is (correct: a legend may sit on many sheets), re-places `ScheduleSheetInstance`s while skipping the titleblock revision schedule. **But** the catches at `:277` and `:293` swallow a failed viewport or schedule and the command still reports `Created N duplicated sheet(s)`. A copy that lost content reads as a clean success. Module 11 §5.8 named this; it stands |
| **Set Revision on Sheets** `SetRevisionOnSheetsCommand` | `Tools/Sheets/RevisionCommands.cs:63` | `PARTIAL` | Per module 11 §5.8, `:121` lets a sheet that silently did not receive its revision disappear into a batch total. Drawing-issuance risk — the highest-consequence defect in the panel |
| **Batch PDF** (sheets → `Number - Name.pdf`) | `Tools/Data/DataCommands.cs:42` | `PARTIAL` | Module 11 §5.8: `catch { return "(could not write file)"; }` and the command still returns `Result.Succeeded` |
| **KOR Details** `DetailsPaletteCommand` | `Tools/Details/DetailsPaletteCommand.cs` | `STUBBED` in practice | The only command in the **Details** category. Opens the standards template in the background, finds the source `ViewDrafting` by name, copies its view-owned elements into a new drafting view in the job model, sets `View Prefix` to the `KOR-D-#####`, one undoable transaction, rolls back and closes without saving on failure. Well written, and blocked five ways — §5.2 |

**Test coverage for the composition logic:** `dotnet test --filter "FullyQualifiedName~GridPacker|FullyQualifiedName~ViewName"` → **7 passed, 0 failed, 7 ms** `[RUN]`. `GridPacker.Pack` is tested for wrapping and for the narrow-usable-width case; `ViewNameComposer` for compose, prefix-omission, illegal-character stripping and `MakeUnique`. The Revit-side commands themselves remain untested, as module 11 established for all 137.

**The correct conclusion, which is a sharper finding than the wrong one it replaces.** A drafter can
build a custom detail sheet today, entirely with KOR-owned tools: create the sheet from a list, place
the chosen detail views on it auto-arranged in a grid, align and distribute them, renumber the detail
numbers by click order, rename the views to match, and batch-export to PDF. **That half works.** What
does not work is the *first* hop — pulling those details out of the central 612-detail register,
which is the `KOR Details` palette and is blocked five ways (§5.2). So the sentence to say out loud
is: *"the sheet-composition tooling is real and good; it is not yet fed by the central catalogue."*
The gap in "standards centralisation" is not the sheet builder — it is everything upstream of it.

### 8. Credentials in the clear (noted per instruction, not re-derived)

`CODEX-STDDETAILS-REVIEW-PROMPT.txt:5,7` carries two live plaintext passwords (`transmittals_app`,
`standards_reader`) in a tracked file at the repo root. Both are also live elsewhere:
`Kor.Operations.App/App.config:162` (and `:170` for `opportunities_app`) and
`KOR.RevitTools/PALETTE-README.md:20`. I used the App.config copy, and only for SELECTs. The
`standards_reader` grant is genuinely narrow — my `SELECT` on `detail.Detail` and `detail.Component`
was refused. `transmittals_app` is not narrow; it is the application login.

---

## 6. Dependencies

| Dependency | Needed by | Reachable off the KOR LAN? |
|---|---|---|
| `KorTransmittals` on `KOR-APP01\SQLEXPRESS` | The whole governance app | **LAN/VPN only.** SQL auth (`transmittals_app`); Windows auth for `kor\ilalonde` **fails** `[RUN]` |
| `KorStandards` on `KOR-APP01\SQLEXPRESS`, schema `detail` | Register, palette, conformance | **LAN/VPN only.** Windows auth fails; `standards_reader` reads two views only |
| `\\Kor-fs01\Drafting\Document Details\` | Blob storage for the 12 masters | **LAN/VPN only.** Currently **empty** — see §5.1 |
| `\\Kor-fs01\Drafting\KOR-Deploy\` | Add-in payload, loader, would-be template home | **LAN/VPN only.** `content\` holds no files |
| **KOR-302N** | Sole copy of `Kor_Structural_Standards_Template_R25.rvt`; sole pilot machine | **LAN/VPN only.** Single point of failure for the entire detail template |
| Autodesk Revit 2020–2027, licensed desktop | Palette, sheet tools | Must be installed on the demo machine |
| PdfSharp | `StatusWatermarkRenderer` | In-process |
| `System.Data.SqlClient` 4.9.0 (deprecated, deliberate) | `SqlDetailsPaletteReader.cs:1` | n/a |

No Graph, no SharePoint, no Deltek, no AI provider, no HTTP service. **Nothing in this module works
from MVE's office without a VPN**, and the two screens worth showing (register, census) are a SQL
query and a CSV — both of which can be pre-exported to static artefacts and shown offline. Do that.

---

## 7. Test reality

**`Kor.Operations.App/StandardDetails/` — zero tests.** There is no test project covering it; the app
tests hang headless and are `--filter`-only per `AGENTS.md`. The module builds clean `[RUN]` and that
is the entirety of the automated assurance on the approval workflow, the optimistic-concurrency
guards, the one-current-official filtered index, the delete transaction, and the watermark renderer.
Its compensating control is the 2026-08-06 governance review, which is a good adversarial read — and
which, notably, **could not tell that the 12 blobs had been moved** three days before it ran, because
it treated `Test-Path=False` as ambiguous rather than chasing it. That is exactly the "verify the
artifact, not your reading of it" failure the repo's own working rules warn about.

**`KOR.RevitTools.Core.Tests` — 4 DetailsCatalog tests, all passing in 14 ms** `[RUN]` (of module 11's
79). They cover `DetailsPaletteCatalog.FromRows`: grouping by detail number, variant de-duplication,
the placeable roll-up, and the filter. That is the right unit to test and it is correct. **What it
cannot test** is everything that is actually broken: the SQL read, the config load, the template open,
the Revit copy-paste, the transaction rollback. `DetailsCatalogTests.cs:14` carries a dummy
`Password=secret` connection string that nonetheless confirms the real server/instance/database/user.

**Blunt summary:** coverage is not theatre here — it is honest and small. But 4 passing tests on a
LINQ grouping is not evidence that a palette works, and no one should present it as such.

---

## 8. Demo risk

Ranked by likelihood × damage in front of MVE's technical lead.

1. **Claiming "we centralised our standard details" and then opening the app.** Twelve booklet PDFs,
   all Draft, none published, and every Open button fails. The gap between the sentence and the screen
   is the whole risk of this module. `[QUERIED]`
2. **"Show me the palette."** Four blockers deep (§5.2); on the deployed build the button does not
   exist at all. If someone installs the branch to demo it, it opens empty or dies on the missing
   template. `[RUN]`
3. **"Can it build a detail sheet?"** The answer is **yes** — and this was the one item this audit
   initially got wrong (§5.7). The risk is now the opposite of what I first wrote: not that the
   capability is missing, but that it gets **oversold as catalogue-driven** when it is not. If it is
   shown, show it as sheet composition and say the palette feed is the next hop. Two commands in the
   panel are `PARTIAL` and both fail silently — do not demo **Duplicate Sheet** or **Set Revision on
   Sheets**. `[READ]`
4. **"How many details do you have?"** Safe answers: 612 canonical numbers, 1,079 view occurrences,
   ~379 components, 7,489 names censused, 468 true standards. **Unsafe:** any statement that these are
   approved, published, verified, or available to drafters. All 612 are `unverified`. `[QUERIED]`
5. **"Who approves a standard?"** One approval record exists, from 2026-02-26, with a hardcoded
   comment, attributed to a SHA of a Windows username. Do not open ApprovalRecords on screen.
6. **"How do your drafters use it?"** There is no adoption evidence and no instrument that could
   produce any. An architecture firm will ask this — it is *their* hardest problem too. Prepare an
   honest answer rather than improvising one. `[DOC]`
7. **Looks-unfinished risk:** `_test.txt` containing `test` sitting in the module folder; a repo-root
   `CODEX-STDDETAILS-REVIEW-PROMPT.txt` with two live passwords in it; `KOR-Deploy\content\` empty.
8. **The MVE mirror-image risk.** If KOR pitches detail-library governance as a capability and MVE
   asks to see it running, KOR is describing MVE's own unsolved problem while not having solved it
   either. That is recoverable if framed as *"here is how we measured ours"* — the census and the
   scoreboard are real and are the right things to show — and unrecoverable if framed as a product.

---

## 9. To-do register

| Item | Size | Tag | Why it matters |
|---|---|---|---|
| Relink the 11 quarantined blobs: `UPDATE dbo.FileBlobs SET StoragePath = REPLACE(StoragePath,'\Document Details\','\_QUARANTINE-app-docs-2026-08-03\')` — **owner runs it, verify by clicking Open** | S | `BEFORE-DEMO` | Turns 12 dead records into 11 working ones. Highest value-per-minute item in the module |
| Locate or re-issue `cc81b796…pdf` (ID 36, KOR SHEARWALL & COLUMN PRESENTATION STANDARDS), or delete the record | S | `BEFORE-DEMO` | The twelfth master is genuinely lost; a broken row on screen invites the wrong question |
| **Demo the Sheets panel** on Autodesk's *Snowdon Towers* sample (per module 11's playbook, no KOR template needed): Create Sheets from a two-entry list → Views to Sheet (auto-grid) → Align → Renumber Views by click order → Name Views by Sheet. Rehearse it once; avoid **Duplicate Sheet** and **Set Revision on Sheets** (both `PARTIAL`, §5.7) | S | `BEFORE-DEMO` | This is the one genuinely clickable, visually satisfying thing in the whole module. It needs only Revit — **no VPN, no SQL, no KOR share** — so it survives a demo at MVE's office. Frame it honestly: sheet composition works, it is not yet fed by the central catalogue |
| Do **not** enable the palette on any demo machine; keep the `detailsPalette` config section absent | S | `BEFORE-DEMO` | Absence is invisible; an empty or crashing palette is not |
| Pre-export the register (612 rows) and census headline to a static grid/PDF for offline showing | S | `BEFORE-DEMO` | The two genuinely impressive screens, made VPN-proof |
| Delete `Kor.Operations.App/StandardDetails/_test.txt` | S | `BEFORE-DEMO` | Six bytes of debris in a folder someone may open |
| Scrub the two live passwords from `CODEX-STDDETAILS-REVIEW-PROMPT.txt` and rotate `standards_reader` before `feature/details-palette` merges | M | `SOON` | Post-merge the `PALETTE-README.md` credential is in `main`'s history permanently |
| Publish `Kor_Structural_Standards_Template_R25.rvt` to `KOR-Deploy\content\` | S | `SOON` | Removes KOR-302N as the single point of failure for the entire detail library — a real business risk independent of the demo |
| Merge `feature/details-palette` and deploy, keeping the dormant switch off | M | `SOON` | Gets the palette into the product so a pilot is possible at all |
| Run one verification campaign: promote a **pilot set of ~20 details** to `content-verified` so `IsPlaceable=1` is non-zero | M | `SOON` | The single change that turns the palette from empty to demonstrable |
| Add `detail.vw_ComponentRegister` + a `standards_reader` grant | M | `SOON` | Register 1 cannot be built at all today; blocks the governance review's Part 2D |
| Add `Documents.DetailNumber` + unique filtered index + picker UI (governance review Part 2B) | M | `SOON` | The first hop of the app↔KorStandards link |
| `DocumentVariants` table and variant-scoped versioning/current-official (Part 2A) | L | `SOON` | 432 palette rows carry a `SizeToken`; the app cannot express one detail in five sheet sizes |
| Promotion outbox: approval → `Confidence='human-confirmed'` + `DetailHistory` (Part 2C) | L | `LATER` | The keystone of "one gatekeeper". Meaningless until B and A land |
| Replace `CreateStableUserGuid(Environment.UserName)` with real identity | L | `LATER` | Approvals are not governance-grade until an approver is a person, not a hash |
| Stand up *any* adoption measure — install count, `version.txt`, a usage ping | M | `LATER` | Today the question "did drafting change?" is structurally unanswerable |
| Decide and record the Michael-Li content position: keep `MLI-` family names or rename | M | `LATER` | The code is KOR-owned; the library is still his by filename |

---

## 10. Verdict

**Demo-able with care: one live click-path (the Sheets panel) plus two static screens — but not as a
working chain.** The
underlying asset is real and genuinely strong: 612 immutable detail numbers, 1,079 occurrences,
~379 canonical components, a 7,489-name census that resolves to 468 true standards, ~29 recorded
rulings with named deciders, and an eight-check conformance scoreboard that went green on 2026-08-06.
For an architecture firm with its own detail-library problem, the *measurement* is the impressive
part and it will land. The **Sheets panel is the second real asset** — eleven commands, eight
`WORKING` and carefully built (a three-pass renumber that cannot strand a viewport; a grid packer
that measures real footprints and refuses to overlap existing content), and it needs nothing but
Revit, so it demos anywhere. But the chain from the register to a drafter's screen is broken at every
hop *upstream* of that panel: the app has never heard of KorStandards (zero code references,
confirmed by grep), the palette is unmerged, undeployed, unconfigured, template-less and gated to
zero placeable rows, and the twelve documents the app *does* govern have been unopenable since their
files were moved on 2026-08-03 — with one of the twelve lost outright. The module has had no activity
since 2026-03-12. **A drafter can compose a detail sheet today; they cannot get a standard detail out
of the register to put on it.**

The single most important thing to fix before the demo is the **blob relink** — one `UPDATE`, run by
the owner, verified by clicking Open — because it converts the one screen that will actually be
clicked from a guaranteed error dialog into a working governance walkthrough. The single most
important thing to fix after it is the **promotion event**: until approval in the app writes
`Confidence='human-confirmed'` into KorStandards, the palette is architecturally incapable of ever
showing a detail, and "one cockpit, two registers, one gatekeeper" remains a sentence rather than a
system.
