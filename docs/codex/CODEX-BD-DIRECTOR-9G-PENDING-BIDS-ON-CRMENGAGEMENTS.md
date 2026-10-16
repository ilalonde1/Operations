# BD Director 9g — Pending bids, on the pursuit the platform already has

Bounded brief for Codex. Source edits only; nothing is built, run, applied or deployed by you.
Response goes beside this file as `CODEX-BD-DIRECTOR-9G-PENDING-BIDS-ON-CRMENGAGEMENTS-RESPONSE.md`
in the 9a–9f format.

## Why this exists

Ian's decisions of 2026-10-15 (`docs/KOR-BD-Opportunity-to-Proposal-Handoff-2026-10-15.md`,
section 1, settled): the BD Brain owns the pursuit lifecycle Opportunity → Pending → Submitted →
Won/Lost as platform state set in the app; no retrofit to the `M:\Project Opportunities` folders; a
folder path is at most a pasted link; every pursuit is assigned to a person; a Pending tab filters
by assignee. The Leduc RFP 2026-RFP-IP-061 was submitted that day under Islam Shabana.

The hand-off's section 2 asked for a new pursuit record. Searched before writing (CLAUDE.md rule 1),
and it exists:

- `opportunities.CrmEngagements` (`Schema/03_Crm.sql`, `48_…`, `267_…`): `Stage` is
  `CrmEngagementStage` Drafting=1 / Submitted=3 / Won=6 / Lost=7, `OwnerStaffId` is the assignee
  (a `BdStaff.StaffKey`), `NextActionDueUtc`/`NextActionNote`, `WonLostOutcome`/`OutcomeReason`.
- `opportunities.CrmEngagementStageHistory` (267) is written by `SqlCrmEngagementStore` on every
  insert and stage change with `ByStaffId`; `OpportunityAssignmentLog` records owner changes.
- `SqlPursuitGrabStore.GrabAsync(opportunityId, staffUpn)` turns an opportunity into a pursuit:
  Opportunities.Status → Pursuing, engagement at Drafting, history, assignment log.
- `SqlCrmEngagementStore.UpdateAsync` on a stage change also updates `Opportunities.Status`
  (line 342), so Submitted is already two-sided.
- The RFP number is `Opportunities.OpportunityKey`; the due date is
  `Opportunities.SubmissionDeadlineUtc`, edited in `OpportunityEntryDialog`. One deadline, one truth.
- The CRM screen already has a "Mine" filter (`CrmViewModel.MineOnly`, owner = signed-in actor).

Measured on the live store, 2026-10-15 17:20: **100 of the 101 live engagements have
`OpportunityId` NULL.** They are BD-tracking pursuits from migration 48 (ExternalSource NULL 75,
BdMeeting 23, EmailThread 2), not bids; one engagement was grabbed from an opportunity. So "0 at
Submitted" and "100 with no deadline" were never about bids, and a Pending tab that showed all 101
would be wrong. **A bid is an engagement with an `OpportunityId`.** Leduc exists as opportunity
39322 (`BIDSTEND-2026-RFP-IP-061`, Status New, no engagement) and again as 39332
(`APCALLBU-AB-2026-06377`, the same RFP from the Alberta Purchasing Connection) — a cross-source
duplicate for the ingestion dedup to settle later, not here; 39322 is the one to use.

So 9g adds what is actually missing, on the existing rows, and nothing else: the word Pending,
two fields on the pursuit, a Pending bids tab in the Director window, and the Submitted action from
that tab.

## The decisions, so you build the one thing

- **No new pursuit table, no second lifecycle, no second deadline.** Everything reads and writes
  `CrmEngagements`, `CrmEngagementStageHistory` and `Opportunities` through the existing stores.
- **Pending is the display name of `CrmEngagementStage.Drafting`.** The enum value does not
  change; the word "Drafting" disappears from the CRM and Director screens.
- **A bid is `OpportunityId IS NOT NULL`.** The Pending bids tab shows bids at Drafting or
  Submitted and says, in one line, how many relationship pursuits without an RFP it is not showing.
- **The tab is called "Pending bids"**, not "Pending": the Asks & decisions ledger already uses
  the word Pending for an undecided review, and a tab named Pending beside it would read as that.
- **Submitted at** is the latest `CrmEngagementStageHistory` row at Stage 3, not a new column.
- **Due** is `Opportunities.SubmissionDeadlineUtc`, shown local with a countdown and edited
  only through the existing `OpportunityEntryDialog` opened from the row.
- **Nothing scrapes a folder.** `FolderLink` holds a pasted path or URL, displayed as a link.
- No model call anywhere (CLAUDE.md rule 14).

## Pieces

### Piece 1 — migration `Kor.Opportunities.Data/Schema/353_PursuitSubmission.sql`

Guarded by `COL_LENGTH`, its own batch each, no data statements, no grants:
`ALTER TABLE opportunities.CrmEngagements ADD SubmittedFileName NVARCHAR(260) NULL;` and
`ALTER TABLE opportunities.CrmEngagements ADD FolderLink NVARCHAR(1000) NULL;`.

### Piece 2 — model and engagement store

- `Kor.Opportunities.Core/Models/CrmEngagement.cs`: `string? SubmittedFileName`, `string? FolderLink`.
- `SqlCrmEngagementStore`: the two columns in the select list, `InsertAsync` and `UpdateAsync`.
  `UpdateAsync` keeps its existing behaviour (stage history, `Opportunities.Status`, ClosedAtUtc).
- `Kor.Opportunities.Core/Models/CrmEngagement.cs` (or beside it): `CrmStageNames.Display(stage)`
  → "Pending" for Drafting, "Submitted", "Won", "Lost". `CrmEngagementRowView` and every Director
  and CRM binding that shows a stage name use it.

### Piece 3 — the Director store read

`IBdDirectorStore.ListPendingBidsAsync()` → `PendingBidRow(long EngagementId, long OpportunityId,
string RfpNumber, string Project, string Client, string? OwnerStaffId, string? OwnerDisplayName,
string? OwnerMailbox, DateTimeOffset? DueUtc, CrmEngagementStage Stage, DateTimeOffset? SubmittedAtUtc,
string? SubmittedFileName, string? FolderLink, DateTimeOffset? NextActionDueUtc)` and
`CountRelationshipPursuitsAsync()` → `int`. SELECT only, as every `SqlBdDirectorStore` read is:
engagements with `OpportunityId IS NOT NULL AND Stage IN (1, 3)` joined to `Opportunities`
(`OpportunityKey`, `Name`, `BuyerName`, `SubmissionDeadlineUtc`), `BdStaff` on `StaffKey =
OwnerStaffId` for the display name and mailbox (the People pane's identity rule), and the latest
history row at Stage 3 for `SubmittedAtUtc`. Order by `DueUtc` ascending, nulls last. The
relationship count is `OpportunityId IS NULL AND Stage IN (1, 3)`.

### Piece 4 — the Pending bids tab (tab 9) and its two actions

- Caption constant `PendingBidsCaption` =
  `Bids in flight: each pursuit that came from an RFP, who holds it, when it is due, and whether it went in. Pursuits without an RFP behind them are relationship work and stay on People and Accounts.`
- One line above the grid: `{pending} pending · {submitted} submitted · by assignee: {Name n, Name n, …} · due this week {n}, next week {n}, later {n} · {relationship} relationship pursuits not shown`,
  weeks by the local calendar week of `DueUtc` through `BdDay`.
- Grid, one row per bid, sorted by due: Client · Project · RFP · Assignee · Due (local date and
  time via `BdDay`) · Time left (the Dashboard's deadline formatter the Market tab already reuses)
  · Stage (`CrmStageNames.Display`) · Submitted (`BdDay`, blank when Pending) · File · Link
  (a hyperlink that opens the pasted path or URL with the shell; no validation beyond non-empty).
  Row text red when `DueUtc` is within 24 hours or past and the stage is Pending, the Market tab's
  colour rule.
- "Mine" checkbox: assignee mailbox or StaffKey equals the signed-in actor, the CRM `MineOnly`
  rule; off by default for the Director.
- **Mark submitted…** opens `SubmittedDialog` (beside `TargetDialog`, `AskDialog`, `DecisionDialog`,
  `NoteDialog`): file name (required, ≤ 260), folder link (optional, ≤ 1000), a note that the
  submitted time is now. OK → `ICrmEngagementStore.UpdateAsync(engagement with { Stage =
  Submitted, SubmittedFileName, FolderLink }, actor)`, then reload. The store writes the history
  row and the opportunity status; the dialog writes nothing itself.
- **Assign…** reuses the Director's existing reassign dialog and path, unchanged.
- **Edit opportunity…** opens the existing `OpportunityEntryDialog` for the row's opportunity, so
  the due date is edited where it lives, then reload.
- `BdDirectorViewModel` reads bids and the relationship count on load and refresh, never on tab
  change; the constructor gains `ICrmEngagementStore engagements` (eleven parameters; update both
  fixtures and the harness).

### Piece 5 — gates

**App, new `BdDirectorPendingBidsRegressionTests.cs`:** (1) nine tabs in order, the caption by
`x:Static`, plain words, no "Drafting" literal in any Director or CRM C#/XAML file; (2) the tab
calls `ListPendingBidsAsync` and `CountRelationshipPursuitsAsync` once on load and never on tab
change; (3) a fixture with two Pending bids (one due in 6 hours, one in 3 days), one Submitted bid
and three relationship pursuits renders two red rows at most one, the submitted row with its time,
the relationship count 3 and no relationship row (the negative control); (4) Mine keeps only rows
whose assignee matches the actor by mailbox or StaffKey; (5) `SubmittedDialog` OK calls
`UpdateAsync` once with Stage Submitted and both fields, and Cancel calls nothing; (6) the date
ratchet still zero. Render `director-pending.png`; minimum total fourteen.

**Data, extend `BdDirectorRegressionTests`:** (7) `ListPendingBidsAsync` SQL is SELECT-only, carries
`OpportunityId IS NOT NULL`, joins `BdStaff` on `StaffKey`, and takes `SubmittedAtUtc` from the
history table; (8) migration 353 is two guarded column adds and nothing else; (9) the engagement
store's write boundary extends by exactly the two columns.

### Piece 6 — the hand-off document

Add one paragraph under section 2 of `docs/KOR-BD-Opportunity-to-Proposal-Handoff-2026-10-15.md`
headed "Built on, 2026-10-15": the pursuit record is `CrmEngagements`; what 9g added; the bid test
`OpportunityId IS NOT NULL`; the duplicate opportunity rows 39322 / 39332 noted for ingestion.
Do not rewrite sections 1 or 2.

## Claude's run after apply (not yours)

Build App tests, Data (via the App tests) and CLI; run the gate classes; apply 353 (Ian in SSMS);
render and read `director-pending.png`; install the App locally. Then Leduc, in the app: grab
opportunity 39322 for ishabana@korstructural.com, Mark submitted with the PDF's file name and the
folder link; confirm the row shows Submitted with today's time and the Director's Submitted count
reads 1.

## Read bound

About 150 KB:
- `docs/KOR-BD-Opportunity-to-Proposal-Handoff-2026-10-15.md` sections 1–2; `docs/codex/CODEX-BD-DIRECTOR-9F-…` brief and response
- `Kor.Opportunities.Core/Models/CrmEngagement.cs`, `Opportunity.cs`, `OpportunityEnums.cs`
- `Kor.Opportunities.Data/Schema/03_Crm.sql`, `48_CrmEngagementBdTracking.sql`, `267_PursuitLifecycleFoundation.sql`, `276_BdStaffDirectory.sql`, `352_DirectorBrief.sql`
- `Kor.Opportunities.Data/Crm/ICrmEngagementStore.cs`, `SqlCrmEngagementStore.cs`, `SqlPursuitGrabStore.cs`, `IBdDirectorStore.cs`, `SqlBdDirectorStore.cs`
- `Kor.Operations.App/Crm/CrmEngagementRowView.cs`, `CrmViewModel.cs` lines 100–200 and 730–760, `CrmView.xaml`
- `Kor.Operations.App/Opportunities/OpportunityEntryDialog.xaml.cs` (constructor and OK path only)
- `Kor.Operations.App/BusinessDevelopment/Director/` (all), `CompositionModules/OpportunitiesModule.cs` lines 140–150 and 260–280
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdDirectorRegressionTests.cs`, `BdDirectorMarketAndBriefRegressionTests.cs`, `BdScreensRenderTests.cs` `RenderDirector`
- the Market tab's deadline formatter and colour rule, wherever `BdDirectorViewModel` reuses them

Do not open the live database. App projects with `UseWPF` have no implicit `System.IO`.

## Files

Creates: `Kor.Opportunities.Data/Schema/353_PursuitSubmission.sql`;
`Kor.Operations.App/BusinessDevelopment/Director/SubmittedDialog.xaml` + `.xaml.cs`;
`Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdDirectorPendingBidsRegressionTests.cs`; the RESPONSE.

Edits: `Kor.Opportunities.Core/Models/CrmEngagement.cs`; `Kor.Opportunities.Data/Crm/SqlCrmEngagementStore.cs`,
`IBdDirectorStore.cs`, `SqlBdDirectorStore.cs`; `Kor.Operations.App/Crm/CrmEngagementRowView.cs` (and any CRM
binding that showed "Drafting"); `BdDirectorWindow.xaml` + `.xaml.cs`, `BdDirectorViewModel.cs`;
`OpportunitiesModule.cs` if the engagement store is not already registered for the window; the Director
test fixtures, `BdDirectorRegressionTests.cs`, `BdScreensRenderTests.cs`;
`docs/KOR-BD-Opportunity-to-Proposal-Handoff-2026-10-15.md`.

## Response

The 9a–9f format. State plainly that nothing was built, run, applied or deployed, that no new
table or lifecycle was created, and that no model call exists in the change. `git diff --check`
clean. Do not commit.
