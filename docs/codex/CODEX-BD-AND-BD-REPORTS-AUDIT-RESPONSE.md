# Business Development and BD Reports audit response

Audit date: 2026-10-13. Source: working tree in `C:\VIsual Studio Projects\Operations`.
Live evidence: SELECT-only queries against the configured Opportunities database on
KOR-APP01, starting at approximately 14:55 Pacific; Q4 recorded
`2026-10-13T15:03:02.7833812-07:00`. These are separate reads, not a transactionally
consistent snapshot. Counts can change after this audit.

This is a findings-only report. No application code was changed, no build or test
was run, no generator or view-time synthesizer was invoked, and no database write,
migration, service action, or deployment was performed. In particular, invoking
`GetSectorPursuitsAsync` would not have been read-only: it can persist synthesis.
Credentials were read into memory and were not printed or written.

Prior-art search covered existing BD audit documents, `tools/BdReportBuilders`,
BD integrity/graph tools, and the adjacent KOR.Drafter database-tools location.
The existing report builders informed the lineage review; no new audit executable
or parallel implementation was created.

## Answers to the seven required questions

1. **None of the 11 enrichment joins inflates figures through provider fan-out.**
   Each pins one provider. Given the supplied unique key, the multiplier is 1,
   not 1.877 or 3. The complete join inventory is below. There are other counting
   defects, notably counting affiliations as people and historical work as active
   pipeline.
2. **Yes, partial results can look complete.** Live examples include 167 uncosted
   projects in a 270-project pursuit pool, strategic-target counts capped at 12,
   clipped recommendation text, and lower-case verdicts counted as honed but
   omitted from sector tables. Export and retrieval-failure cases add further
   conditional paths.
3. **0 of 4 `Command=` bindings is unresolved by static source tracing.** All
   four belong to Admin. The no-op defects are inside handlers or missing save
   plumbing, rather than 19 missing commands. See the action inventory below.
4. **A successful dismissal can be bypassed by a stale Bazaar claim.** This is
   finding 1. Both writes commit; the removal's business effect is defeated.
   I did not find a demonstrated claim/reassign SQL rollback that is translated
   into success. Reassignment has expected-owner/closed-state guards. A failed
   board reload after a committed reassignment can nevertheless be hidden by
   its success message.
5. **Yes, timestamp consumers use different calendar-date policies.** Briefs
   format the stored offset's date; Dashboard converts to workstation local
   time. There are 61 stored deadlines with a different Pacific calendar date,
   including 17 future deadlines. None of the 384 nonzero-offset deadlines had
   that difference in this snapshot. This is a consumer inconsistency, not a
   re-report of the fixed ingestion defect.
6. **Source freshness is not consistently disclosed.** Generated-at timestamps
   are common; they do not date the underlying MPI or billing data. The Deltek
   billed column reaches frozen `PRSummaryMain` without a freeze/as-of warning.
   Some intelligence sections do correctly show aged/stale as-of annotations,
   person last-seen dates, and linked-org refresh dates. "There are no as-of
   dates anywhere" would be false.
7. **The scope needs the producers and entry-point wiring, not just renderers.**
   `SqlBriefDataStore`, the pursuit mutation stores, `SqlPursuitBriefStore`,
   `SqlBdDashboardStore`, `SqlPrimePipelineStore`, `OpportunityEntryDialog`,
   `DeltekClientContextService`, and DI registration materially decide the
   output. The report service also contains three visual reports and action
   rollups beyond the nine named document generators. These were followed
   where relevant; the coverage limits below prevent treating this as an estate
   audit of every dependency.

## Ranked findings

"Live" below means the triggering data was observed, not that the UI/export was
executed. "Conditional" means a concrete code path and triggering state were
established, but the interleaving, outage, or rendered artifact was not exercised.

### 1. High - a stale claim succeeds after the same opportunity was dismissed

Evidence: `Kor.Opportunities.Data/Crm/SqlPursuitGrabStore.cs:100` (`GrabSql`),
`Kor.Opportunities.Data/MajorProjects/SqlPursuitLifecycleStore.cs:173`, and
`Kor.Operations.App/BusinessDevelopment/Workspace/BazaarViewModel.cs:249`.

Repro: A and B load the same New/unowned opportunity. A selects Not for us and
commits its dismissal. A sees "Removed ... not for us". The dismissal leaves
Status=1 and OwnerStaffId=NULL. B grabs the stale row. `GrabSql` tests only Id,
Status=1, and OwnerStaffId=NULL; it does not test DismissedAtUtc. B's transaction
changes the opportunity to Pursuing, assigns an owner, creates a Drafting CRM
engagement and assignment history, and returns the new engagement ID as success.
The dismissal fields remain set. The row is now simultaneously removed and claimed.

This is a missing commit-time lifecycle precondition, not a swallowed exception
or a missing Command binding. Both users reasonably believe their conflicting
actions worked. The reverse ordering is guarded: dismissal after a completed
grab fails its New/unowned test. MPI ownership also checks dismissal; the missing
guard is specifically the opportunity grab path.

Live exposure: **87 dismissed New/unowned opportunities satisfy the grab's
current guard**. **0 dismissed opportunities had a CRM engagement** at the read.
Q1 establishes both. The race is code-proven, not an observed historical incident.
Do not describe this as a transaction failing to commit or as evidence that an
actual reassignment was lost.

### 2. High - Dashboard's opportunity Save closes successfully and discards edits

Evidence: `Workspace/DashboardView.xaml.cs:242`, under
`Kor.Operations.App/BusinessDevelopment`; also
`Kor.Operations.App/Opportunities/OpportunityEntryDialog.xaml.cs:22,243,377`.

Repro: open a cached opportunity by double-clicking a Dashboard RFP/deadline row,
change a valid editable field, and press Save. Dashboard constructs the ordinary
editable `OpportunityEntryDialog`, calls `ShowDialog()`, and ignores both its
return value and `Result`. The dialog constructs the draft and sets
`Result=draft; DialogResult=true`; its caller is responsible for persistence.
The normal Opportunities view consumes that result and calls its update path;
Dashboard does not. Reopening the record retrieves the original values.

Mechanism: missing save-result hookup, the "no binding at all" category at the
persistence boundary. The Save control itself is wired. The comment calling this
a "Read-only view" and saying it matches the normal double-click flow is false.
Conditional on an edit through this entry point; affected attempted-edit count
cannot be reconstructed from the database because nothing was written.

### 3. High - on-demand synthesis writes verdicts the report renderer cannot classify

Evidence: `Kor.Opportunities.Data/BdReports/OnDemandHoningSynthesizer.cs:296-355`,
`BdReportModels.cs:17-48`, `HoningResultParser.cs:66`,
`Generators/SectorReportGenerator.cs:46-52`, and `SqlBdReportService.cs:53-85`.

The synthesizer lowercases accepted verdicts. C# verdict constants, `Rank`,
`IsUrgent`, sector bucket comparisons, and the summary switch use uppercase
ordinal comparisons. SQL's database collation is
`SQL_Latin1_General_CP1_CI_AS`, so its pursuit filters still accept lower-case
values. The parser preserves case.

Repro: synthesis returns `PURSUE`; persistence writes `pursue`. A sector report
containing that project increments Projects honed, but puts it into none of the
urgent/pursue/monitor/discover/dead/duplicate sections. A lower-case `pursue` whose
angle says URGENT also loses the C# urgency flag. SQL grouping is case-insensitive
while its C# switch is not, so a grouped lower-case representative can additionally
misclassify an entire summary group. The exact summary error depends on the
returned group representative; it is not valid to assert it always equals 88.

Live: **88 active rows**, **87 `pursue` and 1 `monitor`**, carry lower-case
verdicts; all 88 have `Notes` beginning `on-demand synthesis` (Q1/Q2). These are
affected when included by a sector filter, not 88 disjoint sector-report totals.

A second deterministic failure is in the same persistence boundary: the regex
`^[a-z][a-z0-9-]{1,30}$` rejects the underscore in the explicitly requested
`PURSUE_URGENT`. `PersistSynthesisAsync` returns without writing, while its caller
still increments `updated`. Conversely, an unknown token such as `banana` passes.
There is also no check that a returned positive ID belongs to the requested batch.
The rejected-urgent/foreign-ID cases are conditional; no model call was made.

### 4. High - Briefs Maker can export the previous selection under the new ID

Evidence: `Kor.Operations.App/BusinessDevelopment/Briefs/BriefsMakerWindow.xaml.cs`,
`ProjectPicker_ProjectSelected`, `PersonPicker_PersonSelected`,
`UpdateGenerateButtonsEnabled`, and `GenerateAsync` (project branch at line 449).

Repro: fully load project A, then select B while B's data query is slow. The handler
sets `_selectedProjectId=B` immediately, but leaves `_selectedProject=A` and the
Generate buttons enabled. Press Generate before B completes. It renders A's
captured data into `KOR-Project-Brief-B-...`. The person branch has the same split
between latest ID and previous data. The completion-time stale-request check does
not protect this interval. If B's load throws, the old selection also survives
after the error popup.

Mechanism: stale state used by an otherwise working handler, outside the five
click-disappearance categories. A file is produced and opened, making success
plausible. Conditional, with no historical export count available. The picker
preview can still show A, but that does not reconcile B's selected ID/filename.

### 5. High - Admin can display a saved Enabled value that was never sent to SQL

Evidence: `Workspace/AdminView.xaml:117-120`,
`Workspace/AdminViewModel.cs:178-223,470`.

Repro: toggle a job from false to true, holding the first update pending; toggle it
back before completion. Two-way binding immediately changes `row.IsEnabled` back
to false. The second command returns at `_adminWriteBusy`, without writing,
reverting, or reporting that the change was ignored. The first update already
captured true as its argument, but after awaiting it formats success using the
row's now-false value: "Enabled = False". Database=true, checkbox=false,
success text=false. A Run Now on one job can likewise suppress another job's toggle
because the busy flag is shared. The commands' CanExecute defaults to true.

Mechanism: early return on an unmet precondition. The UI itself represents the
unsaved state as accepted. Conditional timing defect; no database write was made
to reproduce it. This outranks an ignored duplicate Run Now, which does not itself
alter a durable-looking checkbox value.

### 6. High - failed relationship retrieval becomes an authoritative absence claim

Evidence: `Workspace/PursuitBriefViewModel.cs:101-122,145-168,226-295`,
`Workspace/PursuitBriefWindow.xaml.cs` snapshot construction, and
`Workspace/PursuitBriefPdfExporter.cs` rendering of that snapshot.

Repro: load a project whose owner has real Deltek history, but have the Deltek read
throw. `LoadOwnerDeltekAsync` catches and clears its displays. The getters then
say "No prior KOR work on record with this owner" and "No KOR contacts on record
at this owner". Equivalent catches affect owner pursuit history and architect
contacts. Outer loading still reports "Loaded pursuit brief ...". Exporting the
snapshot carries the false absence claims into the PDF. Missing linkage and
genuine empty results share these placeholders as well.

Mechanism: catch that returns a default, followed by normal success status.
Conditional outage/linkage case; no failure injection was performed and no live
count of false absences is claimed. Gray placeholder styling is not an
"unavailable" explanation.

Related entry-point inconsistency: Relationships explicitly enriches an org brief
with Deltek and supplies unavailable/no-connection notes
(`Workspace/RelationshipsView.xaml.cs:169-204,230`). Briefs Maker calls the raw
`IBriefDataStore` directly. That store returns `Deltek:null`
(`Kor.Opportunities.Data/Briefs/SqlBriefDataStore.cs:791`), and DI registers that
raw store (`CompositionModules/OpportunitiesModule.cs:97`). Thus the same linked
org exported from Briefs Maker silently lacks the financial section and the
explanation available through Relationships. This is an existing reachable code
path, not a claim that the app lacks a Deltek capability.

### 7. High - unknown structural seats become "open", and research contacts become "we know them"

Evidence: `Kor.Opportunities.Data/BdReports/Generators/BdVisualHtml.cs:62,95,115-122`
and `SqlBdReportService.cs:935-972,1009-1020,1037-1075`.

Repro A: supply an attack-card row with no resolved structural engineer. JavaScript
`se(c)` prints "open seat". No actual seat-status field is in that card decision.
Unknown and open are different facts. **245 of the 270 live pursuit-card rows
have no resolved SE** (Q3); those 245 also had no text SE and no Locked/Preferred
seat flag. This is 245 unsupported open-seat assertions, not proof that 245 seats
are actually occupied.

Repro B: add one active public-research affiliation for a firm's employee, with
no KOR contact or prior work. The graph/treemap's affiliation count makes the firm
green and labels it "we know people"/"we know them". Attack cards similarly call
an architect warm on the people count. That SQL asks whether an affiliation exists,
not whether KOR knows the person; some versions do not even exclude retired
people or former affiliations. No identity merge is involved. The exact number
of falsely warm organizations was not measured, because absence of a relationship
cannot be proved merely by absence of one database edge.

### 8. High - org briefs label contracted fees as billed and hide billing-source staleness

Evidence: `Briefs/BriefGenerator.cs:1081-1092`,
`Briefs/BriefPdfGenerator.cs:1142-1202`, `Briefs/HtmlBriefPdfGenerator.cs:475-493`,
`Kor.Operations.App/Crm/DeltekClientContextService.cs:309-405`, and Relationships'
`BuildDeltekSection`.

Repro: a client has PR.Fee=100,000 and actual billed=20,000. `LifetimeFee` is
derived from `SUM(PR.Fee)` (with the existing FX treatment). DOCX and the QuestPDF
fallback label that 100,000 "Lifetime billed". The normal HTML PDF correctly
labels it "Lifetime fee". Thus the PDF fallback can change the meaning of the
same amount without disclosing a change of renderer. This is a semantic error,
not enrichment fan-out; the supplied values imply an 80,000 overstatement in
this concrete example. No live client balances were independently reconciled.

Separately, recent-project `FeeBilled` comes from `PRSummaryMain`. The brief says
"Billed" without disclosing the supplied 2026-02-17 freeze. A successful stale
query does not set `DegradedSections`. On a project billed since that date, the
column understates current billing by the missing post-freeze amount. This source
condition is live per the audit's ground truth; the dollar shortfall was not
re-derived. The known freeze does not justify calling every AR or fee field stale:
they have different sources.

### 9. High - pipeline totals silently value unknown-cost projects at zero

Evidence: `SqlBdReportService.cs:98-105,289-304,964-966,1011-1013`,
`Generators/CallSheetReportGenerator.cs:44-48`, and
`Generators/PursuitDossierReportGenerator.cs:44-49`.

Repro: a pursuit list contains one known 10M project and one unknown-cost project.
The report displays Pipeline (CAD)=10M alongside two pursuits, without identifying
it as the known-cost subtotal or disclosing cost coverage. All-unknown inputs
produce zero in the COALESCE-based totals. Heat uses the same zero substitution;
an uncosted opportunity contributes no value to that prioritization channel.

Live: **270 pursuit projects, 167 unknown costs, 103 known costs; displayed known
subtotal CAD 24,267,907,744** (Q2). Only 38.1% of projects are costed. The missing
amount cannot be quantified by treating the 167 nulls as actual zero values.
This is a completeness/presentation defect; the sum of known amounts is not
arithmetically wrong. Sector and executive totals use broader verdict populations,
so the 270-project amount is not asserted to be their displayed amount.

### 10. High - completed portfolio work is reported as active project pipeline

Evidence: `SqlBdReportService.cs:314-389` and
`Generators/ArchitectFrequencyReportGenerator.cs:29-54,60,103`.

The ranking unions nonretired MPI architect links with nonretired architect-role
IntelWork, deduplicates names, then labels the result Active projects and
Pipeline. Neither branch establishes a live pursuit window. The renderer goes
further: more linked active projects means more team slots KOR can pursue.

Repro: keep an MPI row active in the inventory but set Stage=`DELIVERED (2019)`.
Its architect still receives one active project and its cost in pipeline. This is
not an objection to keeping historical work in a discovery radar or credentials
list; it is an objection to the reported meaning.

Live: **21 nonretired architect-linked MPI rows have Stage starting DELIVERED or
equal to Opened**, with raw known cost **34,374,191** before ranking/name
deduplication (Q3). Examples include MPI 8288 (Simone Little Italy, delivered
2024), 8289 (800 Broadway, delivered 2025), and 8304 (820 Olive, delivered 2019).
Do not interpret that raw sum as the exact net overstatement across the top-20
ranking. There are also 1,633 architect-role IntelWork rows with a numeric year
before 2026 (Q2); a past year alone does not prove completion, so those are not
1,633 independently proved false-active projects.

### 11. High - "People" counts affiliations, with live inflation in the top-20 report

Evidence: `SqlBdReportService.cs:348-352` (architects), `439-461` (competitors),
and the correlated architect counts in `GetPursuitDossiersAsync`.

Repro: one live person has two nonretired affiliation rows at the same canonical
org, for example a role change. `COUNT(*)` reports two People. It should not be
read as two contacts. Former roles can also contribute because IsCurrent is not
required; the competitor query does not require a live person row.

Live: the exact architect ranking CTE followed by the affiliation comparison
finds **13 of the displayed top 20 architects** with inflated People counts (Q5).
Examples: Arcadis IBI Group **25 vs 21 distinct people**; Chris Dikeakos **12 vs
8**; Diamond Schmitt **28 vs 19**; GBL **12 vs 8**. Across active orgs with live
people, **273 org groups have 491 excess affiliation rows** (Q2). The latter is
an exposure population, not an additive report-wide contact overstatement.
This finding concerns counting capability, not entity resolution or merging.

### 12. High - region-brief "live" and "forward" sections use incompatible eligibility rules

Evidence: `Kor.Opportunities.Data/Briefs/SqlBriefDataStore.cs:504-529,2211-2251`,
`Briefs/BriefPdfGenerator.cs:865-870`, and the equivalent DOCX/HTML sections.

Repro A: a New prime RFP is dismissed. Region counting and list queries still
include it because they require Status=1/IsPrimeConsultantRfp/province, with no
dismissal check. They also lack an explicit future-deadline check. This reads as
"Live prime RFPs", not a broad discovery pool. Live BC: **28 counted, including
5 dismissed**, so only 23 remain after adding that single eligibility rule (Q4).
AB counted 26 with none dismissed. No expired rows were in these groups at the
read; expiry is a conditional additional exposure.

Repro B: forward headline counts only ProjectStage CapitalPlan/FacilityRenewal;
the top-five forward detail query includes every nonretired MPI in the geography.
A costly on-hold/completed/proposed project can occupy the list while contributing
nothing to that headline. Live BC's headline was **230**, while four of its five
displayed rows did not satisfy that headline's stage predicate. The list included
MPI **8136, NaiKun Wind Power Project, Stage/ProjectStage=On hold**, under
"Forward pipeline (planned / funded)". AB headline=54; two of its five details
were ProjectStage=Planned, also outside the headline predicate. Q4 lists exact IDs.

### 13. High - "verified-current" can mean only that two narrow input streams were quiet

Evidence: `OnDemandHoningSynthesizer.cs:117-122,160-228,355` and
`SqlBdReportService.cs:160-175`; source-date contracts in `BdReportModels.cs`.

Repro: a stale urgent project says its submission window closes on a date that has
now passed. No new IntelProjectAction update or title-matching OpportunityObservation
has arrived. Classification becomes Quiet and `TouchVerifiedAsync` stamps the
honing LastRefreshAtUtc now without reassessing its verdict. A new
IntelProjectSignal, changed MPI stage, or other change outside those two inputs
also does not make it Due. Repeated quiet touches can perpetuate the verdict and
refresh date even when the discontinued source cannot supply confirming evidence.

The code explicitly equates nothing observed with "the verdict is still true".
That is stronger than the observations justify. A due synthesis API failure,
missing API key, or exhausted budget can also leave cached rows, without the
EnsureResult/deferred information being conveyed in the report model. The sector
service's catch keeps serving cached data.

Conditional current-truth error; no touch or synthesis was executed during the
audit and no exact current Quiet population is claimed. More generally, all MPI
report paths can show a new generated-at date without exposing the discontinued
feed's as-of date. A honing timestamp, where available, is a different provenance
dimension. Intel as-of annotations elsewhere do not remedy this MPI omission.

### 14. Medium-high - capped data and clipped reasoning are presented as complete

Evidence: `SqlBdReportService.cs:537-583,834-868`,
`Reports/BdReportsViewModel.cs:523`,
`Generators/StrategicRelationshipsReportGenerator.cs:49-73`, and the `Safe`
helpers in Sector, CallSheet, ExecSummary, PrimeConsultant, PursuitDossier, and
AwardPrograms generators.

Live count example: Strategic Relationships requests at most 12 matches per
target, then prints `Projects.Count` under "Live pipeline matches". There are
**21 Graham matches and 24 EllisDon matches** under the exact single-term query
semantics (Q6). The summary therefore shows **12 instead of 21** and **12 instead
of 24**, understating by 9 (42.9%) and 12 (50%). The detail section correctly says
sample; that qualification does not accompany the summary count. The query also
does not restrict verdict to live categories; Q6 found no DEAD/DUPLICATE among
these two current match sets, so that additional exposure remains conditional.

Live text example: Sector uses caps of 400 characters for pursue reasoning and
110 for monitor reasoning, then returns `value[..max]` without an ellipsis or
"abridged" notice. **24 uppercase PURSUE rows have angles over 400 characters;
8 uppercase PURSUE_URGENT rows have angles over 600** (Q4). The 24 are candidates
for the pursue table because angle-derived urgency can move some to the urgent
section. Do not confuse these source counts with an exact per-sector clipping
count. Across the relevant corpus, long text is real, not a synthetic edge case.

Concrete harmful input: an otherwise plausible opening paragraph fills the cap;
the next sentence says "Do not approach yet; structural scope is already awarded".
The exported table omits that qualification and looks finished. CallSheet,
PursuitDossier, PrimeConsultant and ExecSummary similarly cap actionable prose;
the action-rollup service already clips recommendations to 300 characters before
the UI receives them. Project risks/signals/actions and sector-brief lists also
have TOP limits in `SqlBriefDataStore` without a total/cutoff contract. Some table
headings explicitly state top-N, which is better and is not itself a defect.

### 15. Medium-high - visual DOCX export saves one viewport, not the full cards report

Evidence: `Reports/BdReportsWindow.xaml.cs:282-313,371-376` and
`Kor.Opportunities.Data/BdReports/Generators/DocxBuilder.cs` (`RenderImage`).

Repro: open Attack Cards with enough rows to scroll, leave the preview at the top,
and save DOCX. The visual branch calls WebView2 `CapturePreviewAsync`, passes the
control's ActualWidth/ActualHeight to RenderImage, and writes one image. Cards
below the viewport cannot be included by that operation. The report/header can
say 270 pursuits while the document contains only the visible subset, and the
UI reports Saved without a cutoff notice. Scrolling changes which subset exports.

Live data exceeds an ordinary viewport (270 cards); the specific lost row count
depends on window size and scroll position. Conditional rendered manifestation:
no WebView/Word export was run. This finding is about DOCX; the PDF path uses
PrintToPdfAsync, so the same clipping claim is not automatically transferable.

### 16. Medium-high - generated prose asserts verification and urgency independent of its inputs

Evidence: `Generators/ExecSummaryReportGenerator.cs:49,106-107,128,146`,
`Generators/SectorReportGenerator.cs:67`, and
`Briefs/BriefGenerator.cs:45-55` / `BriefPdfGenerator.cs:359-365`.

Repro A: pass an empty headline and empty lists to ExecSummary. It still says every
PURSUE has named decision-makers, an identified procurement model and a warm-intro
path, and that chasing locked work is "structurally eliminated". It still emits
fixed amounts such as Graham's 3.4B and the 850M school program, while claiming no
statement is asserted without underlying rows. Those amounts are constants, not
results of the supplied live queries. Sector summary narrative likewise renders
without reconciling its statements to the supplied rows.

Repro B: obtain a brief for an old/closed/non-prime opportunity by ID.
`SqlBriefDataStore.GetOpportunityBriefAsync` filters only Id; its model does not
carry status or the prime-RFP boolean. Word/PDF still declare "Live in active
procurement", that the sector/geography match, and that the structural seat has
not been set. A deadline can visibly be in the past while that assurance remains.

These are unconditional text paths, reachable today; the number of historical
documents containing a false individual statement is unknown. Static strategic
guidance can be useful, but it is not verified by a successful database read.

### 17. Medium - deadlines have inconsistent dates and rounded countdowns

Evidence: `Briefs/BriefGenerator.cs:1242-1247`,
`Briefs/BriefPdfGenerator.cs:223,1477-1483`, and
`Workspace/DashboardViewModel.cs:350,375,388-389`.

Briefs format a DateTimeOffset directly, taking its stored local date, without
displaying a time zone or submission time. Dashboard converts to LocalDateTime.
Repro: the same instant near midnight in different stored offsets can produce a
different brief date while Dashboard remains stable. Live example **opportunity
12403** has stored offset 0, brief date **2031-01-01**, Pacific date
**2030-12-31**. Q4 finds **61** calendar discrepancies, **17 future**. Q3 found
**384 nonzero-offset deadlines and zero Pacific-date discrepancies among those
384**; do not claim the October correction newly broke these 61 rows.

There is no universally correct zone inferable from these consumers alone: buyer
local and viewer local dates can both be intentional. The defect is the
undisclosed, inconsistent policy for a field read as the closing date. The audit
does not challenge the deliberately local RfpReleaseDate.

Countdown repro: at 36 hours remaining, `Math.Round(TotalDays)` can show two days
in the brief while Dashboard's floor shows one; a recently expired deadline can
round to zero rather than a clearly expired value. These are duration/calendar
presentation issues; comparisons by instant remain correct. Bazaar's
`UtcDateTime.Date` is an explicit UTC deduplication key, not a forgotten conversion.

### 18. Medium - project briefs can describe KOR as KOR's competitor

Evidence: `Briefs/BriefGenerator.cs:408,1044-1066` and
`Briefs/BriefPdfGenerator.cs:586,673-696` (also reused by HTML PDF).

Repro: a project's structural summary names KOR. Every structural-role block
unconditionally prints "KOR's competitor on this pursuit" and recommends
`KOR Structural Ltd. vs KOR`. It never checks Kind/ID to recognize KOR's own seat.
Attack Cards does have that capability (`seIsKor`); the brief diverges.

Live: **MPI 2237, 5740 Cambie Condominiums**, is in the pursuit pool with resolved
SE **KOR Structural Ltd.** (Q5). Opening its project brief supplies the trigger.
No document was generated in this audit.

### 19. Medium - malformed/partial prime research counts as completed research

Evidence: `PrimeConsultantParser.cs:18,35-87`,
`SqlBdReportService.cs:590-630`, and
`Generators/PrimeConsultantReportGenerator.cs:25-41`.

Repro: a PrimeConsultantResearch row contains valid JSON `{}` or a schema-drifted
`primeConsultant` object without `knownToKor`. The service admits every non-null
ResultJson. Parser fallback loses the distinction between parsing failure,
unassessed relationship and a known negative: Empty has KnownToKor=false. The
report counts the row in Projects researched and, without a firm, Prime unknown
after research. With a firm but missing relationship it excludes it from the
known-to-KOR group without exposing an unknown status. String `"true"` is also
read as false. A numeric confidence of zero is deliberately treated as unassessed;
that documented choice is not independently called a bug.

Conditional: the live read found **148 active non-null prime-research rows, zero
missing firmName and zero missing knownToKor** (Q2). This does not establish that
all types/content are correct, but it does not support claiming a live missing-
field incident. HoningResultParser similarly returns Empty on malformed JSON;
it correctly avoids JSON_VALUE's 4,000-character prose limit and disposes the
JsonDocument. SQL JSON_VALUE over genuinely invalid JSON can throw on other
report paths; it is not universally a silent fallback.

### 20. Medium - Admin labels an in-flight ingestion run "failed"

Evidence: `Workspace/AdminViewModel.cs:125-132` (`ToRunRow`).

Repro: insert/start a normal run with Success=0 and EndedAtUtc=NULL, then refresh
Admin before it completes. ToRunRow maps `run.Success ? "ok" : "failed"`, without
considering EndedAtUtc. The dashboard can therefore induce intervention against
a healthy running ingestion. This is a status-presentation defect, not proof
of an ingestion failure.

Live at the audit read: **0 in-flight unsuccessful rows among the latest 50
ingestion runs** (Q1). The next in-flight run is the trigger; no job was started
for this audit. Job-history/status paths are not all identical, so this is scoped
to the Admin recent-run mapping.

## Enrichment join resolution: all 11 sites

All line numbers in this table refer to
`Kor.Opportunities.Data/BdReports/SqlBdReportService.cs`.

| Join site | Provider restriction | Aggregate reached | DISTINCT? | Fan-out result |
|---|---|---|---|---|
| 56, sector summary | ON ProjectBriefHoning | COUNT(*) by verdict | No | One row per project; no inflation |
| 106, sector freshness/value | ON ProjectBriefHoning | SUM freshness flags and project costs | No | One row per project; no inflation |
| 149, sector detail | ON ProjectBriefHoning | No SQL aggregate; generator counts/buckets rows | No | One hydrated row per project |
| 266, call-sheet pool | ON ProjectBriefHoning | No SQL aggregate; generator counts/sums rows | No | One row per project |
| 302, executive headline | ON ProjectBriefHoning | COUNT(*) and conditional SUMs | No | One row per project |
| 563, strategic keyword matches | ON ProjectBriefHoning | No SQL aggregate; capped list later counted | No | No enrichment multiplication; cap error is finding 14 |
| 598, prime consultants | ON PrimeConsultantResearch | No SQL aggregate; researched/identified counts in generator | No | One research row per project |
| 659, pursuit dossiers | ON ProjectBriefHoning | Correlated org counts; generator totals | No for correlated counts | No provider fan-out; affiliation counting is finding 11 |
| 716, dossier signal attachment | ON ProjectBriefHoning | No aggregate; joins matching IntelSignal records | No | Project-to-signal multiplicity is intentional; attached list capped at four |
| 946, shared AttackSpineCte | ON ProjectBriefHoning | Graph/treemap pursuit counts, weighted cost; graph edge weights | HotOrg DISTINCT org/project; three downstream COUNT(DISTINCT MpiId) | No provider or repeated-role inflation within an org |
| 1050, attack cards | ON ProjectBriefHoning | Correlated affiliation counts; card count | No | One card per project |

The source now has three textual COUNT(DISTINCT ho/a.MpiId) occurrences in these
visual queries (lines 964, 991, 1011), rather than the brief's two. That search
count is not the proof of correctness; the unique provider-pinned relationship
is. A single pursuit intentionally contributes value to each participating org
in the graph, so summing all org tiles is not a distinct firm-wide pipeline total.
The graph describes heat as pursuit cost times urgency, rather than currency alone.

Other service queries were traced too: sector signals use org-ID membership
subqueries (not multiplicative MPI joins); architect ranking and project samples
use row-number name deduplication; competitor footprint uses a name UNION and
separate samples; award matching uses OUTER APPLY TOP 1, so it cannot multiply
the award-program rows; action totals and the top-open sample are separate
queries. The write-only report audit logger was read but not called. No WBS1
fan-out investigation was performed in this service.

## UI and generator coverage

The XML/source inventory covered **16 XAML files**, **4 Command attributes**, and
**143 named event attributes** from Click, MouseDoubleClick, SelectionChanged,
RequestNavigate, Checked, Unchecked, TextChanged, Loaded, Unloaded, Expanded and
Collapsed. **143 of 143 named handlers were found** in their associated partial
code files. This is a static name/route check, not proof that WPF resolves every
runtime DataContext or template NameScope.

| Command binding | Static resolution |
|---|---|
| AdminView.xaml:119, DataContext.ToggleEnabledCommand via AdminViewRoot | AdminViewModel property initialized; root DataContext assigned |
| AdminView.xaml:133, RunNowCommand via ancestor DataGrid | AdminViewModel property initialized |
| AdminView.xaml:139, ViewHistoryCommand via ancestor DataGrid | AdminViewModel property initialized |
| AdminView.xaml:145, OpenReportCommand via ancestor DataGrid | AdminViewModel property initialized |

No permanently-false CanExecute was found for these four. The command class
defaults to true. Static searches did not establish 19 inert controls.

| Action area | Result of handler/store tracing |
|---|---|
| Dashboard navigation/briefs/refresh | Wired handlers; editable-opportunity Save is discarded (2) |
| Bazaar grab/dismiss | SQL transactions and outcome handling exist; repeat grab returns Ignored, not false success; dismissal race in (1) |
| Overwatch reassign | Expected-owner, duplicate-target and closed-stage outcomes are surfaced; busy returns Ignored. A committed reassign followed by LoadAsync catching a reload error has its error status overwritten by Reassigned; the write succeeded but the board may still show the old owner. Conditional display failure, not a lost write |
| MPI own/release/dismiss/restore | Lifecycle calls and affected-row outcomes exist; own checks dismissal; no phantom-success rollback established |
| Reassign/Dismiss dialogs | Selection/reason and cancellation paths present; dialog close itself is not confused with store commit |
| Admin | Four bindings resolve; busy guard drops enabled-state changes (5); recent-run status defect (20) |
| Relationships/Events/Attribution/Job history | Event handlers and load/error paths exist; Deltek brief capability confirmed in Relationships |
| Briefs Maker/Region dialog | Generate writes and errors are handled; picker/export race in (4); region data semantics in (12) |
| PursuitBrief partial class | Load, lifecycle, snapshot export and approach-generation paths traced; load-failure defaults in (6). Approach regeneration consumes the loaded Brief snapshot rather than reloading all underlying sources, so its "live" framing cannot guarantee changes made since window load |
| Reports window | Generation/export/link handlers present; visual DOCX cutoff in (15). `KorReportLinks` accepts kind `opp` but navigation dispatch handles mpi/org/person; an opp link is canceled then ignored. No current nine-generator caller creating an opp link was found, so this is a latent contract gap, not a counted live dead button |

The last two conditional observations are not inflated into additional proved
live incidents. Missing bindings, permanently false CanExecute, catches, early
returns and missing save plumbing were all considered; stale-state errors also
exist outside those five categories.

| Document generator | Main audit result / completeness boundary |
|---|---|
| Sector | Lower-case rows omitted; never-honed rows intentionally excluded from report body; prose caps and unvalidated summary narrative; optional null signals become empty |
| ExecSummary | Distinct headline protected from provider fan-out; sector overlap explicitly disclosed; static verification claims and unknown-cost coverage remain wrong |
| CallSheet | Explicit top-three/top-N portions are intentional; capped prose and unknown-cost total are not disclosed as such; uses the shared ordinal urgency classifier |
| ArchitectFrequency | Historical work called active; affiliation People inflation; KOR-aligned comes from a hardcoded name list and is disclosed as "per memory", not a current CRM relationship query |
| CompetitorIntel | Project footprint deduplication exists; people count is affiliation count, and action count includes nonretired actions regardless of open/closed status; it is not labeled Open actions like the architect column |
| StrategicRelationships | Detail correctly says sample; summary incorrectly turns capped list length into total; prose/catalog contacts are static |
| PrimeConsultant | Unknown/default parser state counted as researched; known relationship is a nonnullable bool; explicit top tables plus unmarked prose caps |
| PursuitDossier | Full-pool KPI separate from selected drill-down; unknown-cost subtotal and research-versus-relationship semantics; text and attached-signal limits |
| AwardPrograms | Date-only deadline correctly remains date-only; no enrichment fan-out; input cap 80 and detail cap 12, heuristic suggested project match, shortened labels. A suggested match is not proof of eligibility; no live false award count was established |

Also read: BdReportModels, IBdReportService, sector definitions/catalog,
OnDemandHoningSynthesizer, both parsers, BdReportDocument, DocxBuilder,
HtmlPreviewBuilder, BdVisualHtml, KorReportStyles and KorReportLinks. The prose
catalogs were only inspected where static assertions or matching logic required
it; they were not treated as executable eligibility rules.

All six methods in each brief interface are implemented: opportunity, project,
person, sector, region, organization. Comments in BriefGenerator and
BriefPdfGenerator still say "three" shapes; the code has six. HTML PDF implements
the same six and falls back to QuestPDF, including the semantic difference in (8).
There is no general missing-interface-method finding. DOCX uses scoped
WordprocessingDocument instances; parser JsonDocuments and screenshot streams
are scoped. Headless Edge has process disposal, timeout termination and scratch
cleanup in a finally block; its HTML write precedes that inner cleanup scope, so
a failure while writing the temporary HTML can leave the scratch directory.
No broad font/stream leak was demonstrated, and native renderer behavior was not
exercised. Successful file creation is not proof of complete pagination.

The four converters were checked against their use sites. MillionsCurrencyConverter
expects the supplied double-in-millions and multiplies once by 1,000,000; no live
factor-of-a-million defect was found. BarHeightConverter expects doubles for both
values. Both return empty/zero on unexpected binding types, an explicit residual
runtime-binding risk rather than a proved current mismatch. SeatStatusBrushConverter
uses neutral for unknown status; PlaceholderBrushConverter changes color only.
Neither makes the substantive assertions identified in (6) and (7).

ScoringProfile's supplied zero-row state is **not reported as a defect**.
`SqlPursuitBriefStore` currently supplies FitScore=null and ThePlay=null directly;
the view shows placeholders. That is not evidence of a broken weighted score or
of a silently displayed numeric zero. No profile-dependent scoring claim was
proved in these report paths.

## Reproducible SELECT evidence

These queries count source projects only after pinning the unique provider.
Affiliation queries deliberately compare rows with distinct people. They contain
no credentials and perform no writes. Observed results are stated above; Q1/Q2
were run before Q4-Q6 and should not be expected to remain identical indefinitely.

### Q1 - verdict spellings, lifecycle exposure, running status

```sql
SELECT COALESCE(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'),
                JSON_VALUE(e.ResultJson,'$.verdict'))
       COLLATE Latin1_General_100_BIN2 AS Verdict, COUNT(*) AS Projects
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.MajorProjectEnrichment e
 ON e.MajorProjectsInventoryId=m.Id AND e.ProviderName=N'ProjectBriefHoning'
WHERE m.RetiredAtUtc IS NULL
GROUP BY COALESCE(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'),
                  JSON_VALUE(e.ResultJson,'$.verdict'))
         COLLATE Latin1_General_100_BIN2;
-- DEAD 1; DISCOVER 297; MONITOR 803; PURSUE 131; PURSUE_URGENT 52;
-- monitor 1; pursue 87.

SELECT DATABASEPROPERTYEX(DB_NAME(),'Collation') AS Collation;
SELECT COUNT(*) AS DismissedNewUnowned
FROM opportunities.Opportunities
WHERE Status=1 AND OwnerStaffId IS NULL AND DismissedAtUtc IS NOT NULL;
-- 87
SELECT COUNT(*) AS DismissedWithEngagement
FROM opportunities.Opportunities o
WHERE o.DismissedAtUtc IS NOT NULL AND EXISTS
 (SELECT 1 FROM opportunities.CrmEngagements e WHERE e.OpportunityId=o.Id);
-- 0
SELECT COUNT(*) AS RunningShownFailed
FROM (SELECT TOP 50 Success,EndedAtUtc FROM opportunities.IngestionRuns
      ORDER BY StartedAtUtc DESC) r
WHERE EndedAtUtc IS NULL AND Success=0;
-- 0
```

### Q2 - source populations and monetary coverage

```sql
SELECT COUNT(*) AS LowercaseHoned,
 SUM(CASE WHEN e.Notes LIKE N'on-demand synthesis%' THEN 1 ELSE 0 END)
 AS MarkedOnDemand
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.MajorProjectEnrichment e
 ON e.MajorProjectsInventoryId=m.Id AND e.ProviderName=N'ProjectBriefHoning'
WHERE m.RetiredAtUtc IS NULL
 AND COALESCE(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'),
              JSON_VALUE(e.ResultJson,'$.verdict'))
     COLLATE Latin1_General_100_BIN2 IN ('pursue','monitor','dead','discover');
-- 88 / 88

SELECT COUNT(*) AS PursuitPool,
 SUM(CASE WHEN m.EstimatedCostCad IS NULL THEN 1 ELSE 0 END) AS UnknownCost,
 SUM(COALESCE(m.EstimatedCostCad,0)) AS ReportCost
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.MajorProjectEnrichment e
 ON e.MajorProjectsInventoryId=m.Id AND e.ProviderName=N'ProjectBriefHoning'
WHERE m.RetiredAtUtc IS NULL
 AND COALESCE(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'),
              JSON_VALUE(e.ResultJson,'$.verdict')) IN ('PURSUE','PURSUE_URGENT');
-- 270 / 167 / 24267907744

SELECT COUNT(*) AS ResearchedRows,
 SUM(CASE WHEN JSON_VALUE(e.ResultJson,'$.primeConsultant.firmName') IS NULL
          THEN 1 ELSE 0 END) AS NoFirm,
 SUM(CASE WHEN JSON_VALUE(e.ResultJson,'$.primeConsultant.knownToKor') IS NULL
          THEN 1 ELSE 0 END) AS UnknownRelationship
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.MajorProjectEnrichment e
 ON e.MajorProjectsInventoryId=m.Id AND e.ProviderName=N'PrimeConsultantResearch'
WHERE m.RetiredAtUtc IS NULL AND e.ResultJson IS NOT NULL;
-- 148 / 0 / 0

SELECT COUNT(*) AS GroupsWithRepeatedPeople, SUM(n-d) AS ExcessPeopleRows
FROM (
 SELECT pa.CanonicalOrgId,COUNT(*) n,COUNT(DISTINCT pa.IntelPersonId) d
 FROM opportunities.IntelPersonAffiliation pa
 JOIN opportunities.IntelPerson p ON p.Id=pa.IntelPersonId
  AND p.RetiredAtUtc IS NULL
 JOIN opportunities.CanonicalOrg co ON co.Id=pa.CanonicalOrgId
  AND co.RetiredAtUtc IS NULL
 WHERE pa.RetiredAtUtc IS NULL GROUP BY pa.CanonicalOrgId
 HAVING COUNT(*)>COUNT(DISTINCT pa.IntelPersonId)
) x;
-- 273 / 491

SELECT COUNT(*) AS PastArchitectPortfolioRows
FROM opportunities.IntelWork iw
WHERE iw.RetiredAtUtc IS NULL
 AND LOWER(ISNULL(iw.Role,'')) LIKE '%architect%'
 AND TRY_CONVERT(int,iw.YearApprox)<2026;
-- 1633; not proof that every such project is completed.
```

### Q3 - structural unknowns, delivered rows, offsets

```sql
SELECT COUNT(*) AS CardsWithNoResolvedSe,
 SUM(CASE WHEN NULLIF(LTRIM(RTRIM(m.StructuralEngineerName)),'') IS NOT NULL
          THEN 1 ELSE 0 END) AS HasTextSe,
 SUM(CASE WHEN m.SeatStatus IN ('Locked','Preferred') THEN 1 ELSE 0 END)
 AS LockedOrPreferred
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.MajorProjectEnrichment e
 ON e.MajorProjectsInventoryId=m.Id AND e.ProviderName=N'ProjectBriefHoning'
LEFT JOIN opportunities.CanonicalOrg se
 ON se.Id=m.StructuralEngineerCanonicalOrgId AND se.RetiredAtUtc IS NULL
WHERE m.RetiredAtUtc IS NULL AND se.Id IS NULL
 AND COALESCE(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'),
              JSON_VALUE(e.ResultJson,'$.verdict')) IN ('PURSUE','PURSUE_URGENT');
-- 245 / 0 / 0

SELECT COUNT(*) AS OldDeliveredArchitectMpis,SUM(EstimatedCostCad) AS IncludedCost
FROM opportunities.MajorProjectsInventory
WHERE RetiredAtUtc IS NULL AND ArchitectCanonicalOrgId IS NOT NULL
 AND (Stage LIKE 'DELIVERED%' OR Stage='Opened');
-- 21 / 34374191, before the report's name deduplication.
SELECT TOP 5 m.Id,m.ProjectName,m.Stage,m.EstimatedCostCad,co.DisplayName
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.CanonicalOrg co ON co.Id=m.ArchitectCanonicalOrgId
 AND co.RetiredAtUtc IS NULL
WHERE m.RetiredAtUtc IS NULL AND (m.Stage LIKE 'DELIVERED%' OR m.Stage='Opened')
ORDER BY m.Id;

SELECT COUNT(*) AS NonzeroOffsets,
 SUM(CASE WHEN CONVERT(date,SubmissionDeadlineUtc)<>
  CONVERT(date,SubmissionDeadlineUtc AT TIME ZONE 'Pacific Standard Time')
  THEN 1 ELSE 0 END) AS DateDiffersPacific
FROM opportunities.Opportunities
WHERE SubmissionDeadlineUtc IS NOT NULL
 AND DATEPART(TZOFFSET,SubmissionDeadlineUtc)<>0;
-- 384 / 0
```

### Q4 - region rules, calendar dates, and long prose

```sql
SELECT SYSDATETIMEOFFSET() AS ObservedAt;
SELECT ProjectProvince,COUNT(*) AS ReportLivePrimeRfps,
 SUM(CASE WHEN DismissedAtUtc IS NOT NULL THEN 1 ELSE 0 END) AS Dismissed,
 SUM(CASE WHEN SubmissionDeadlineUtc<SYSDATETIMEOFFSET() THEN 1 ELSE 0 END)
 AS Expired
FROM opportunities.Opportunities
WHERE Status=1 AND IsPrimeConsultantRfp=1 GROUP BY ProjectProvince;
-- BC 28/5/0; AB 26/0/0; NULL province 15/3/0.

SELECT Id,Name,DATEPART(TZOFFSET,SubmissionDeadlineUtc) AS StoredOffsetMinutes,
 CONVERT(char(10),CONVERT(date,SubmissionDeadlineUtc),23) AS BriefDate,
 CONVERT(char(10),CONVERT(date,SubmissionDeadlineUtc
  AT TIME ZONE 'Pacific Standard Time'),23) AS DashboardPacificDate
FROM opportunities.Opportunities WHERE Id=12403;
SELECT COUNT(*) AS DifferentCalendarDate,
 SUM(CASE WHEN SubmissionDeadlineUtc>SYSDATETIMEOFFSET() THEN 1 ELSE 0 END)
 AS FutureDifferentCalendarDate
FROM opportunities.Opportunities
WHERE SubmissionDeadlineUtc IS NOT NULL
 AND CONVERT(date,SubmissionDeadlineUtc)<>
     CONVERT(date,SubmissionDeadlineUtc AT TIME ZONE 'Pacific Standard Time');
-- 61 / 17

WITH R AS (
 SELECT Province,Id,ProjectName,Stage,ProjectStage,EstimatedCostCad,
 ROW_NUMBER() OVER(PARTITION BY Province ORDER BY EstimatedCostCad DESC) rn
 FROM opportunities.MajorProjectsInventory WHERE RetiredAtUtc IS NULL
)
SELECT Province,Id,ProjectName,Stage,ProjectStage FROM R
WHERE rn<=5 AND Province IN ('BC','AB') ORDER BY Province,rn;
-- BC IDs 3059,7981,1162,8158,8136; AB 6541,6977,6655,7069,7046.
SELECT Province,COUNT(*) AS ForwardHeadline
FROM opportunities.MajorProjectsInventory
WHERE RetiredAtUtc IS NULL AND ProjectStage IN ('CapitalPlan','FacilityRenewal')
 AND Province IN ('BC','AB') GROUP BY Province;
-- BC 230; AB 54.

WITH P AS (
 SELECT m.Id,COALESCE(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'),
                     JSON_VALUE(e.ResultJson,'$.verdict')) AS Verdict,
        COALESCE(hp.korAngle,rt.korAngle) AS Angle
 FROM opportunities.MajorProjectsInventory m
 JOIN opportunities.MajorProjectEnrichment e
  ON e.MajorProjectsInventoryId=m.Id AND e.ProviderName=N'ProjectBriefHoning'
 OUTER APPLY OPENJSON(e.ResultJson,'$.honingPass')
  WITH(korAngle nvarchar(max) '$.korAngle') hp
 OUTER APPLY OPENJSON(e.ResultJson) WITH(korAngle nvarchar(max) '$.korAngle') rt
 WHERE m.RetiredAtUtc IS NULL
)
SELECT Verdict COLLATE Latin1_General_100_BIN2 AS Verdict,COUNT(*) AS RowsOver400,
 SUM(CASE WHEN LEN(Angle)>600 THEN 1 ELSE 0 END) AS RowsOver600,
 MAX(LEN(Angle)) AS Longest
FROM P WHERE LEN(Angle)>400
GROUP BY Verdict COLLATE Latin1_General_100_BIN2;
-- PURSUE 24/12/1025; PURSUE_URGENT 19/8/919; pursue 6/1/1361;
-- MONITOR 118/61/1266; DISCOVER 47/19/1430; DEAD 1/0/600.
```

### Q5 - exact top-20 architect membership and the KOR seat example

```sql
WITH A AS (
 SELECT m.ArchitectCanonicalOrgId AS OrgId,m.EstimatedCostCad AS Cost,0 AS SrcRank,
 LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(m.ProjectName)),
 N' ',N''),N'-',N''),N'.',N''),N',',N''),N'''',N'')) AS DedupKey
 FROM opportunities.MajorProjectsInventory m
 WHERE m.RetiredAtUtc IS NULL AND m.ArchitectCanonicalOrgId IS NOT NULL
 UNION ALL
 SELECT iw.CanonicalOrgId,iw.EstimatedValueCad,1,
 COALESCE(NULLIF(LTRIM(RTRIM(iw.NormalizedProjectName)),N''),
 LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(iw.ProjectName)),
 N' ',N''),N'-',N''),N'.',N''),N',',N''),N'''',N'')))
 FROM opportunities.IntelWork iw WHERE iw.RetiredAtUtc IS NULL
 AND LOWER(ISNULL(iw.Role,N'')) LIKE N'%architect%'
), D AS (
 SELECT *,ROW_NUMBER() OVER(PARTITION BY OrgId,DedupKey ORDER BY SrcRank) AS rn
 FROM A WHERE DedupKey IS NOT NULL AND DedupKey<>N''
), R AS (
 SELECT TOP(20) co.Id,co.DisplayName,COUNT(*) AS ReportProjects,
 SUM(d.Cost) AS ReportCost
 FROM D d JOIN opportunities.CanonicalOrg co ON co.Id=d.OrgId
 AND co.RetiredAtUtc IS NULL WHERE d.rn=1 GROUP BY co.Id,co.DisplayName
 ORDER BY COUNT(*) DESC,COALESCE(SUM(d.Cost),0) DESC,co.DisplayName
)
SELECT r.Id,r.DisplayName,r.ReportProjects,p.AffiliationRows,p.DistinctPeople
FROM R r CROSS APPLY (
 SELECT COUNT(*) AffiliationRows,COUNT(DISTINCT pa.IntelPersonId) DistinctPeople
 FROM opportunities.IntelPersonAffiliation pa
 JOIN opportunities.IntelPerson ip ON ip.Id=pa.IntelPersonId
 AND ip.RetiredAtUtc IS NULL
 WHERE pa.CanonicalOrgId=r.Id AND pa.RetiredAtUtc IS NULL
) p WHERE p.AffiliationRows>p.DistinctPeople ORDER BY r.ReportProjects DESC;
-- 13 rows; example results in finding 11.

SELECT m.Id,m.ProjectName,se.DisplayName
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.CanonicalOrg se ON se.Id=m.StructuralEngineerCanonicalOrgId
 AND se.RetiredAtUtc IS NULL AND se.Kind='KorStructural'
JOIN opportunities.MajorProjectEnrichment e
 ON e.MajorProjectsInventoryId=m.Id AND e.ProviderName='ProjectBriefHoning'
WHERE m.RetiredAtUtc IS NULL
 AND COALESCE(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'),
              JSON_VALUE(e.ResultJson,'$.verdict')) IN ('PURSUE','PURSUE_URGENT');
-- MPI 2237, 5740 Cambie Condominiums, KOR Structural Ltd.
```

### Q6 - target counts before the TOP 12 cap

```sql
SELECT 'Graham' AS Target,COUNT(*) AS MatchingProjects,
 SUM(CASE WHEN COALESCE(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'),
                       JSON_VALUE(e.ResultJson,'$.verdict'))
          IN ('DEAD','DUPLICATE') THEN 1 ELSE 0 END) AS DeadOrDuplicate
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.MajorProjectEnrichment e
 ON e.MajorProjectsInventoryId=m.Id AND e.ProviderName='ProjectBriefHoning'
WHERE m.RetiredAtUtc IS NULL AND e.ResultJson LIKE N'%Graham%'
UNION ALL
SELECT 'EllisDon',COUNT(*),
 SUM(CASE WHEN COALESCE(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'),
                       JSON_VALUE(e.ResultJson,'$.verdict'))
          IN ('DEAD','DUPLICATE') THEN 1 ELSE 0 END)
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.MajorProjectEnrichment e
 ON e.MajorProjectsInventoryId=m.Id AND e.ProviderName='ProjectBriefHoning'
WHERE m.RetiredAtUtc IS NULL AND e.ResultJson LIKE N'%EllisDon%';
-- Graham 21/0; EllisDon 24/0.
```

## Limits and faults this pass would not catch

This audit traced every query in SqlBdReportService, the nine generators and their
listed contracts/renderers, the specified workspace/report actions and partial
classes, and the six-shape brief generation paths. Dependency reads were targeted
to data meaning, write guards, provenance, and missing-data behavior. The brief
data store and other CRM/Deltek/Intel dependencies were not exhaustively audited
as independent subsystems. Stored facts were not externally fact-checked.

There was no interactive WPF binding trace, click execution, model request,
fault injection, concurrency replay, generated Word/PDF inspection, build, or
test. No historical log reconstruction establishes that a conditional race
already occurred. No invalid dates were reparsed, no entity identities were
re-resolved, and no source-freeze or enrichment uniqueness baseline was needlessly
re-derived. Excluded build/output directories and .vsdx files were not read.

Same-class faults this pass would **not** catch include a runtime WPF NameScope
failure despite the right command property existing, a missing font/renderer
layout causing a final-page qualification to be clipped, and a valid-looking
research fact that is already wrong in its source JSON. All can still produce
confident wrong output. The static event/command count is not a usability or
end-to-end correctness gate.

No finding has been labeled "verified" under the estate audit's pre-fix failing-
gate rule. These are source/data findings with specified repro paths, ready for
review and subsequent fixes with regression gates, as requested.
