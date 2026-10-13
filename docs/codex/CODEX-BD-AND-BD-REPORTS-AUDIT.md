# Codex Deep Adversarial Audit — Business Development & BD Reports

## Why this exists

On 2026-10-13 a Leduc RFP closing at **2:00 p.m. MDT** was showing in the system as
closing at **8:00 a.m.** Every ingestion date parser had been told that an offset-less
timestamp was already UTC, so a published local wall clock was stored as though it were
UTC. **2,116 of 2,473 deadline-bearing opportunities were wrong** — most by 7 hours, some
by 8, 6 or 4 depending on the source's zone.

Nothing threw. No run failed. The integrity suite was green. The job that auto-expires
past-deadline opportunities had been firing up to 8 hours early for months, and two live
pursuits had been silently closed — one of them on the morning of the day it actually
closed. The only reason it surfaced is that Ian looked at a date and thought it felt wrong.

**That is the class this audit is about, in the two modules that the numbers are shown
in.** The ingestion-side instance is fixed and gated (see *Already established*). The
question now is what ELSE in `BusinessDevelopment` and `BdReports` produces a plausible,
confident, wrong answer — or quietly does nothing — without saying so.

These two modules are where BD work is actually seen and acted on: the dashboard a user
reads at a glance, the bazaar where seats are claimed, and the reports and briefs that go
to Ian and into client-facing material. A crash here costs nothing. **A number that is
wrong and looks authoritative gets acted on.**

**Your mindset: assume every figure shown to a user is wrong and prove how.** For every
finding give a concrete failure scenario — the exact rows, state or interleaving that
triggers it, and the resulting wrong output. A finding without a repro path is a question;
mark it as such.

**Do NOT fix anything. Produce the findings report only.** We review, then batch the fixes
with validation.

---

## Measured ground truth (do not re-derive; verify only if you doubt it)

Queried live against `KorOpportunitiesDb` on `KOR-APP01\SQLEXPRESS`, 2026-10-13:

| Measure | Value |
|---|---|
| `MajorProjectEnrichment` uniqueness | **UNIQUE (`MajorProjectsInventoryId`, `ProviderName`)** |
| `MajorProjectEnrichment` rows | **5,064** |
| …distinct projects they cover | **2,698** |
| …rows per project | **1.877 average, 3 maximum** |
| `ScoringProfile` rows | **0** |
| Opportunities carrying a deadline | **2,485** |

Providers writing enrichment rows: `ProjectBriefHoning` 2,469 · `ProjectBrief` 2,409 ·
`PrimeConsultantResearch` 181 · `ProponentResearch` 5.

⚠ **The enrichment table is therefore ONE-TO-MANY against a project.** `SqlBdReportService.cs`
reads `MajorProjectsInventory` in 18 places, joins `MajorProjectEnrichment` in 11, and uses
`COUNT(DISTINCT …)` in 2. That is a candidate population, **not a defect count** — a join
used only to filter, or with the provider pinned in a `WHERE`, is harmless. Establishing
which reported figures are actually inflated is a deliverable.

⚠ **`ScoringProfile` having 0 rows is a data state, not necessarily a defect.** Anything
weighting by a configured profile is weighting by nothing in production today. Where that
is a code path's only failure mode, say so and classify it as data.

Other facts you would otherwise waste time establishing:

- Deltek **`PRSummaryMain` has been frozen since 2026-02-17**, so any P&L or GL figure
  derived from it is understated. The upstream fix is not in these modules — the question
  is whether a report presents such a figure without saying it is stale.
- The **BC Major Projects Inventory feed was discontinued in 2026**, fetcher retired in
  migration 332. `MajorProjectsInventory` holds data no longer refreshed from that source.
- **`datetimeoffset` values in this database may now carry a non-zero offset** (e.g.
  `-07:00`), as of the 2026-10-13 fix. Comparison is by instant so filtering is unaffected,
  but code reading `.Date`, `.Hour`, or formatting without converting is reading LOCAL
  parts. `Opportunity.RfpReleaseDate` is deliberately derived from the published LOCAL date.
- `opportunities.Opportunities` has **no `Description`** and **no `PostedDateUtc`** column —
  both live on `OpportunityObservations`. It has `Name`, `BuyerName`, `RfpReleaseDate`,
  `SubmissionDeadlineUtc`, `Status`, `WonLostOutcome`, `RelevanceScore`, `RelevanceTier`,
  `DismissedAtUtc`.
- `Status` 1 = New, 7 = Lost. `WonLostOutcome` 3 = NoBid — what auto-expiry writes.
- The BD platform is a **radar**: for DISCOVERY it optimises recall over precision, so a
  deliberately wide, unfiltered list is working as designed. **That tolerance does not
  extend to a reported total.**

---

## Already established — do NOT re-report

1. **The ingestion timezone class is fixed and gated.** `PublishedClock` is now the only
   thing that converts a published timestamp; `DateTimeStyles.AssumeUniversal` is gone from
   the ingestion layer and a test fails if it returns. Migrations 339–342 corrected 2,116
   deadlines, 232 date-only deadlines, 2,150 posted dates and reopened 2 wrongly-expired
   opportunities. **Do not re-audit date parsing in `Kor.Opportunities.Data/Ingestion`.**
   It is listed here only because the two BD modules CONSUME these values, and a consumer
   that re-derives a local date, or formats without converting, is in scope.
2. **Entity identity and the destructive refresh were audited separately** in
   `CODEX-BD-ENTITY-IDENTITY-AND-REFRESH-AUDIT.md` (+ RESPONSE). Assume `CanonicalOrg`
   resolution is as designed. **Do not re-audit identity or merging** — ask only what these
   two modules DO with a canonical org, e.g. whether a report counts projects per architect
   in a way that would silently fuse two firms if one row held both.
3. A prior unverified pass by Claude suggested roughly **19 BD controls may no-op**. That
   number was never checked against the XAML. **It is a rumour, not a finding** — not a
   target, not a ceiling, and not a hypothesis to confirm. Enumerate what is actually there,
   and if every binding resolves, say so.
4. `SqlBdReportService.cs` contains **no reference to `WBS1`**, so the known Deltek
   `LEFT(WBS1,5)` fan-out trap does not apply there. Do not go looking for it.

---

## Scope — in tiers

### Tier 1 — reported numbers: anything a human reads as fact (audit EXHAUSTIVELY)

The question at every figure is: **what would have to be true for this number to be right,
and what happens when it isn't?**

- `Kor.Opportunities.Data/BdReports/SqlBdReportService.cs` (55 KB) — every query. Which
  aggregates reach `MajorProjectsInventory` through the one-to-many enrichment join without
  `DISTINCT`? Which `COUNT`/`SUM` counts rows when the reader will read it as things?
- `BdReportModels.cs`, `IBdReportService.cs`, `SectorReportDefinitionCatalog.cs`,
  `SectorReportDefinition.cs` — the contracts. Does a model's shape let a generator tell
  "zero" from "not retrieved"?
- `OnDemandHoningSynthesizer.cs`, `HoningResultParser.cs`, `PrimeConsultantParser.cs` —
  these turn stored research JSON back into fields. What does a malformed, partial or
  schema-drifted `ResultJson` produce? Silence, a default, or a wrong value?
- `Kor.Opportunities.Data/BdReports/Generators/` — all nine report generators plus
  `DocxBuilder.cs`, `HtmlPreviewBuilder.cs`, `BdVisualHtml.cs`, `BdReportDocument.cs`,
  `KorReportStyles.cs`, `KorReportLinks.cs`. **The specific thing to hunt: a section or
  figure that renders as complete when the data was empty, partial or truncated**, so the
  reader cannot distinguish "none found" from "not fetched".
- `Workspace/MillionsCurrencyConverter.cs` and the other converters
  (`SeatStatusBrushConverter`, `BarHeightConverter`, `PlaceholderBrushConverter`) — small
  files doing arithmetic and display on money and status. A converter that swallows a failed
  cast or returns an unexpected default is a silent wrong-display defect.

`Generators/SectorReportProseCatalog.cs` (49 KB) and `Generators/StrategicTargetCatalog.cs`
(14 KB) are **prose content, not logic** — skip them unless a Tier 1 finding points into them.

### Tier 2 — user actions: anything that can no-op or fail silently

- `Workspace/DashboardView.xaml` (63 KB) + `DashboardViewModel.cs` + `DashboardView.xaml.cs`,
  and `AdminView.xaml` + `AdminViewModel.cs` + `AdminView.xaml.cs`.
- `Workspace/BdWorkspaceWindow.xaml(.cs)`, `OpportunitiesHubView.xaml(.cs)`,
  `OpportunityHubSelection.cs`, `BazaarView.xaml(.cs)`, `BazaarViewModel.cs`,
  `BazaarOpportunitySelector.cs`.
- `Workspace/RelationshipsView*`, `OverwatchView*`, `OverwatchRowView.cs`, `AttributionView*`,
  `EventsView*`, `JobRunHistory*`, `ReassignDialog.*`, `DismissReasonDialog.*`,
  `StaffDirectory.cs`.
- `Reports/BdReportsWindow.xaml(.cs)` + `BdReportsViewModel.cs` (34 KB).

In WPF a `Command=` binding to a property that does not exist on the bound ViewModel is
**swallowed** — the control looks live, accepts the click, and does nothing. That is one of
five ways an action disappears; the others are a permanently-false `CanExecute`, a `catch`
that returns, an early return on an unmet precondition, and no binding at all. **For each
finding say which of the five it is, and whether a reasonable user would conclude the action
worked.**

The bazaar and the reassign/dismiss dialogs WRITE state that the rest of the firm sees. A
claim or reassignment that fails while the UI shows success outranks everything else in this
tier. Also worth being systematic about: can a command fire twice before the first
completes, does a long operation block the UI thread, and can selection go stale between
load and commit?

### Tier 3 — documents that leave the building

- `Briefs/BriefGenerator.cs` (58 KB) and `Briefs/BriefPdfGenerator.cs` (70 KB) — the two
  largest files in the module. What goes INTO a brief matters more than how it is drawn, so
  start with `BriefGenerator`. What happens when a section's data is missing entirely? Do
  page or section limits silently truncate? Are streams, fonts and documents disposed on the
  failure path as well as the success path?
- `Briefs/HtmlBriefPdfGenerator.cs`, `BriefsMakerWindow.xaml(.cs)`, `RegionBriefDialog.*`,
  `Workspace/PursuitBriefWindow.xaml`, `PursuitBriefWindow.xaml.cs`,
  `PursuitBriefWindow.Approach.cs`, `PursuitBriefViewModel.cs`, `PursuitBriefPdfExporter.cs`.
- `PursuitBriefWindow` is a partial class split across three files — a good place for two
  halves to disagree about state.
- `IBriefGenerator.cs` / `IBriefPdfGenerator.cs` — does either implementation actually honour
  its interface's contract? Where a comment or docstring disagrees with the code, **trust the
  code and report the comment as the defect.**

---

## Questions the report must answer

1. **Which reported figures are inflated by the enrichment fan-out?** Resolve each of the 11
   joins in `SqlBdReportService.cs`: does it feed an aggregate, and is that aggregate
   `DISTINCT`? Name the affected figures and the direction and size of the error. If none
   are affected, say so — that is a valid and valuable answer.
2. **Can a report or brief present an empty or partial result as complete?** Give the
   specific generator and the input that does it.
3. **Which user actions can fail or no-op without telling the user?** With the mechanism for
   each, ranked by whether a user would believe it worked. Include the count of `Command=`
   bindings that do not resolve, as **X of Y**.
4. **Is there any path where a bazaar claim, reassignment or dismissal is lost while the UI
   indicates success?** If yes, that is finding #1 regardless of anything else.
5. **Does any consumer of a timestamp re-derive a local date or format without converting?**
   The offsets changed on 2026-10-13; a reader that was accidentally correct before may be
   wrong now, or vice versa.
6. **What do the generators do with a stale-sourced figure** (MPI, `PRSummaryMain`)? Is any
   as-of date shown, anywhere?
7. **What else in these two modules decides what a user believes, that this scope list does
   not name?** The calibration from the 2026-08 estate audit was that absence claims were
   wrong 4 of 4 because they grepped for a phrase instead of a capability. **Look for the
   capability.** Correct this scope list where it is incomplete — that drift is itself a
   finding.

---

## Database access (you will need it — several questions are only settleable against data)

A developer Windows account has **no** rights on `KorOpportunitiesDb`; the app connects with
SQL auth. Get the connection string from the machine environment variable
`KOR_OPPORTUNITIES_OPPORTUNITIESDB` on **KOR-APP01**, read over the remote registry from
this workstation:

```powershell
$reg = [Microsoft.Win32.RegistryKey]::OpenRemoteBaseKey('LocalMachine','KOR-APP01')
$cs  = $reg.OpenSubKey('SYSTEM\CurrentControlSet\Control\Session Manager\Environment').GetValue('KOR_OPPORTUNITIES_OPPORTUNITIESDB')
```

Then connect with `System.Data.SqlClient` using `$cs`. **Never echo the string or the
password into output, a file, or this report.** Schema is `opportunities`.

Column names that are easy to get wrong: `CanonicalOrg.DisplayName` (not `Name`),
`Opportunities.Name` (not `Title`), `OpportunityObservations.RawJson`,
`MajorProjectEnrichment.MajorProjectsInventoryId` (not `MpiId`),
`OpportunitySourceMappings.[Key]` / `.ValueJson`.

⚠ Aggregating across a one-to-many join without `DISTINCT` is the exact defect class under
audit — do not reproduce it in your own verification queries. Count distinct projects, not
rows, and state which you counted.

## Constraints

- **Read-only.** No `dotnet build`, no `dotnet test` (the app tests hang headless). No DB
  writes, no migrations, no destructive operations, no service restarts, no deploys.
- SELECT-only queries are welcome and encouraged — a claim backed by a query beats a claim
  backed by reading code. State the query with the number.
- Do not read `bin/`, `obj/`, `TestResults/`, or any `.vsdx`.
- ASCII output. No fixes, no patches — findings only.
- **State what your audit covered and what it did not**, and name at least one same-class
  fault your pass would NOT have caught. A broad claim over a narrow check is worse than no
  check, because the next reader stops looking.

## Deliverable

Findings report at `docs/codex/CODEX-BD-AND-BD-REPORTS-AUDIT-RESPONSE.md`.

Rank findings by **silent-wrong-output risk first** — a defect that shows a confident wrong
number, or absorbs a click while looking successful, outranks one that throws. For each: the
failure scenario, the repro path, the affected row count if you can query it, and whether it
is live today.
