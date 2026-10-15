# Codex — The BD Director window: the shell, the people pane and the funnel pane

**DO NOT BUILD. DO NOT RUN TESTS. DO NOT RUN ANY TOOL AGAINST A DATABASE.** Make the edits, add
the regression gates as source, and ping when applied. Claude builds, runs the filtered test
classes, renders the new screen and reads the PNG, and deploys the local install. Ian runs nothing.

## Why this exists

Ian manages business development. The BD workspace (Dashboard, Opportunities, Future Projects,
My Pursuits, All Pursuits, Relationships, Scorecard, Market History, Events, Engagements) is the
regional engineers' tool. Nothing in the app is *his*: a view above theirs that measures what each
person carries and what has moved, and lets him act on it. This brief builds that window with the
two panes that can be filled from tables that exist today. Targets, asks, decisions, a "Mine"
mode and anything that calls a model are later briefs and are out of scope here.

Measured on the live database and in the source on 2026-10-14 at commit `19853324`. Use these as
given; do not remeasure, do not quantify beyond them. They are also the harness fixture (Piece 6).

| Fact | Value |
|---|---|
| Live pursuits (`opportunities.CrmEngagements`, `Stage` Drafting=1 or Submitted=3) | 101, 101 of 101 with an `OwnerStaffId` |
| Owner keys and their live counts | `Omar` 25 · `Conor` 22 · `Islam` 20 · `Jim` 18 · `John` 9 · `Rory` 4 · `jbryson@korstructural.com` 2 · `ilalonde@korstructural.com` 1 |
| `NextActionDueUtc` in the past / NULL / in the future | 24 / 74 / 3 |
| `UpdatedAtUtc` older than 14 days | 101 of 101 (newest is 2026-09-03 18:02 UTC on every owner: a sweep) |
| `opportunities.CrmActivities` on live pursuits: ever / last 30 d / last 90 d | 114 / 0 / 0 (whole table 114 rows, first 2024-11-22, last 2026-06-19) |
| `opportunities.CrmEngagementStageHistory` rows in the last 90 days | 0 |
| `opportunities.OpportunityAssignmentLog` rows in the last 90 days | 0 |
| Outcomes (Stage 6 or 7) on rows whose `ExternalSource` is not `Deltek.%` | 0 |
| Live pursuits whose buyer has `CrmBuyerEmailWarmth.LastTouchUtc` within 90 d / ever | 25 / 38 of 101 |
| `opportunities.BdUiOpens` last 30 d by `Surface` | `Bazaar` 6 · `Attribution` 2 · `Pursuits` 2 · `Overwatch` 1 (these are telemetry keys, not screen names) |
| `opportunities.BdStaff` | one row per *spelling*: `Omar` (Source `deltek:firstname-match`) and `omara@korstructural.com` (Source `deltek:emmain`) both carry `DisplayName` "Omar Alcazar Pastrana"; `Mailbox` is the resolved address, NULL = unrouted |
| User-visible "Bazaar" | 1 of 1: `Kor.Operations.App/BusinessDevelopment/Workspace/OpportunitiesHubView.xaml` line 50, a ToolTip. The C# literals are a doc comment, a code comment and the telemetry key `RecordOpen("Bazaar", …)`, which stays |
| Inline date-format ratchet (`BdPresentationRegressionTests.InlineDateFormats_OnBdScreens_OnlyGoDown`) | ceiling 64; scans `Crm`, `BusinessDevelopment/Workspace`, `Opportunities` recursively. A new folder outside those is invisible to it |

Line numbers below are as of `19853324`.

## The decisions, so you build the one thing

- **Name.** Window title and Home tile: **BD Director**. Folder
  `Kor.Operations.App/BusinessDevelopment/Director/`, namespace
  `Kor.Operations.App.BusinessDevelopment.Director`. Telemetry surface key `"BdDirector"`.
- **Who sees it.** A role, not a hard-coded address. `KnownRoles.BdDirector = "BdDirector"`
  (`Kor.Operations.App/Services/KnownRoles.cs`), App.config key
  `SecurityGroup.BdDirector.Members` = `ilalonde@korstructural.com` (beside line 152's
  `SecurityGroup.BusinessDevelopment.Members`), checked with
  `SecurityGroupAccess.IsUserInGroup(KnownRoles.BdDirector, userIdentity)` exactly as
  `canSeeBd` is at `HomeWindow.xaml.cs` ~line 280. Do **not** copy the `canSeeBriefing` string
  compare used for the COO Card.
- **A person, not a spelling.** The people pane groups by *person*: the `BdStaff.Mailbox` when
  present, else the `StaffKey`. All keys sharing that identity count together, so `Omar`'s
  pursuits and `omara@korstructural.com`'s screen opens land on one row named by `DisplayName`.
- **Reads only, plus one existing write.** Reassign goes through
  `IPursuitOverwatchStore.ReassignAsync` and the existing `ReassignDialog`. Nothing else writes.
  There is no "release" store method in the codebase today; do not invent one.
- **Nothing recomputed.** Stage counts, win rate and backfill segmentation come from
  `CrmAnalyticsService.Compute` (`Kor.Operations.App/Crm/CrmAnalyticsService.cs` line 52) over
  `ICrmEngagementStore.ListAsync`. Source and owner wins come from
  `IBdAttributionStore.GetSummaryAsync`. The per-pursuit drill list is
  `IPursuitOverwatchStore.ListAsync` filtered in the view model. The new store adds only what no
  store exposes: the per-person aggregates and three funnel facts (below).

## Piece 1 — the window, the tile, the gate

- `Kor.Operations.App/BusinessDevelopment/Director/BdDirectorWindow.xaml` + `.xaml.cs`,
  `BdDirectorViewModel.cs`, `BdDirectorPersonRow.cs` (view rows). Pattern:
  `Kor.Operations.App/Views/CooCardWindow.xaml.cs` (constructor takes the view model, sets
  `DataContext`, loads on `Loaded`). Title "BD Director". Header line:
  `{Live} live pursuits · {People} people · as of {Day}` where the day goes through the
  `BdDay` converter, declared in this window's resources exactly as
  `BusinessDevelopment/Workspace/DashboardView.xaml` line 21 declares it.
- `Loaded` records the open: `AppServices.Get<IBdUiOpenStore>().RecordOpen("BdDirector", actor)`,
  actor resolved the way `OverwatchView.xaml.cs` lines 204-210 resolve it. Lift `ResolveActor`
  into a small shared static if you need it twice; do not paste it.
- Home tile: a `BdDirectorCard` button in `HomeWindow.xaml` modelled on `CooCardCard` (line 153),
  text "BD Director" and one plain sentence: "Who carries what, what has moved, and what has not."
  `OpenBdDirector_Click` mirrors `OpenCooCard_Click` (`HomeWindow.xaml.cs` line 220).
  `HomeTileVisibilityState` gains a `BdDirector` slot, `Collapsed` in `ForSecurityLookupFailure`;
  `ApplyCardSecurity` sets it from the role; the state-apply block (~line 318) and the ordered
  card list in `RebuildHomeCardsLayout` (place it right after `CooCardCard`) both carry it.
  `HomeTileVisibilityStateTests.cs` is updated for the new slot, never weakened.
- DI in `Kor.Operations.App/CompositionModules/OpportunitiesModule.cs`: the store beside line
  257 (`AddSingleton<IBdDirectorStore>(_ => new SqlBdDirectorStore(options.OpportunitiesDb))`),
  the view model and window as transients beside lines 145-146.

## Piece 2 — the store: what nothing exposes today

`Kor.Opportunities.Data/Crm/IBdDirectorStore.cs` + `SqlBdDirectorStore.cs`. Constructor takes the
connection string like `SqlBdUiOpenStore`. Two methods, both read-only, both over the tables named
in the facts table.

**`Task<IReadOnlyList<BdDirectorPerson>> GetPeopleAsync(CancellationToken ct)`** — one row per
person with at least one live pursuit:

| Field | Definition |
|---|---|
| `PersonKey` | `COALESCE(BdStaff.Mailbox, OwnerStaffId)`, the grouping identity |
| `DisplayName` | `BdStaff.DisplayName`, else the `OwnerStaffId` as stored |
| `OwnerKeys` | every `StaffKey` that resolves to this person (the drill filter uses it) |
| `Live` | live pursuits owned under any of those keys |
| `OverdueNextAction` | of those, `NextActionDueUtc < SYSUTCDATETIME()` |
| `NoNextAction` | of those, `NextActionDueUtc IS NULL` |
| `Untouched14d` | of those, `UpdatedAtUtc < now − 14 d` |
| `Activities30`, `Activities90` | `CrmActivities` rows on those pursuits by `OccurredAtUtc` |
| `StageMoves90` | `CrmEngagementStageHistory` rows on those pursuits by `EnteredAtUtc` |
| `BuyersMailed90` | of those pursuits, buyer org's `CrmBuyerEmailWarmth.LastTouchUtc` within 90 d |
| `Opens30` | `BdUiOpens` rows whose `ByStaffId` is any of this person's keys, last 30 d |

Join `BdStaff` on `StaffKey = OwnerStaffId` (CI collation, already on the column). The warmth
join is on `CrmEngagements.BuyerCanonicalOrgId = CrmBuyerEmailWarmth.CanonicalOrgId`. The
`SqlBdUiOpenStore` summary says the table is "never [read] by any UI"; append one sentence
there: read by the BD Director window since brief 9a, as counts per person only.

**`Task<BdDirectorFunnel> GetFunnelAsync(CancellationToken ct)`** — three facts the analytics
snapshot does not carry:

| Field | Definition |
|---|---|
| `MedianDaysInStage` per live stage | median of `now − COALESCE(latest StageHistory.EnteredAtUtc for the current stage, OpenedAtUtc)` over live pursuits in that stage. `PERCENTILE_CONT(0.5)` is fine |
| `UndecidedPastDeadline` | live pursuits whose opportunity's `SubmissionDeadlineUtc` is more than 30 days ago: rows of engagement id, project name, buyer (`COALESCE(o.BuyerName, co.DisplayName)` as the board does), owner key, deadline |
| `UndecidedNoDeadline` | count of live pursuits with no opportunity or a NULL deadline, so the first number is not read as the whole |
| `LostWithCompetitor`, `LostWithReason`, `LostTotal` | over Stage 7 rows whose `ExternalSource` is not `Deltek.CustomProposal`: `LostToCanonicalOrgId IS NOT NULL`, `OutcomeReason IS NOT NULL` |

## Piece 3 — the people pane

Caption, verbatim: **"Everyone who owns a live pursuit. A live pursuit is Drafting or Submitted.
Counts are for that person's pursuits; opens are the BD screens they opened."**

Grid columns, in this order and with these headers: `Person` · `Live pursuits` ·
`Overdue next action` · `No next action` · `Untouched 14 days` · `Activity, 30 days` ·
`Activity, 90 days` · `Buyers emailed, 90 days` · `Opened the tools, 30 days`. Sorted by live
pursuits descending. No target column, no rank, no colour grading: the numbers are the
statement.

Selecting a person fills the list beneath: their live pursuits from `IPursuitOverwatchStore.ListAsync`
filtered to `OwnerKeys`, columns `Pursuit` · `Buyer` · `Stage` · `Last activity` · `Opened` with
dates through `BdDateDisplay`/`BdDay`, and two buttons per row: **Reassign…** (the existing
`ReassignDialog`, people from `StaffDirectory.LoadOptionsAsync(IProposalStaffStore)` as
`OverwatchView.LoadDirectoryAsync` line 152 does, then
`IPursuitOverwatchStore.ReassignAsync(engagementId, opportunityId, fromOwner, toOwner, byStaff,
reason, ct)`; after `Reassigned` reload both panes) and **Open** (resolve
`BdWorkspaceWindow` from DI, it is transient at `OpportunitiesModule.cs` line 157, call
`NavigateToPursuit(engagementId)`, `Show()`). The `ReassignOutcome` cases other than
`Reassigned` show the same plain sentences `OverwatchView` shows; read them there, do not
write new ones.

## Piece 4 — the funnel pane

Caption, verbatim: **"Live pursuits by stage, how long they have sat there, and what has been
decided. Deltek history is counted separately and never added to the live numbers."**

- A stage table from `CrmAnalyticsService.Compute`: `Stage` · `Pursuits` · `Days in stage,
  median` (from the store). Stages named by the enum's words, Drafting and Submitted.
- One line of outcomes: `Won {n} · Lost {n} · win rate {r} on decided live pursuits · {k} Deltek
  wins kept separate`, from the snapshot's `Won`, `Lost`, `WinRate`, `BackfillWins`. With no
  decided pursuits the line reads `No live pursuit has been decided yet.` Beneath it:
  `Of the lost: {LostWithCompetitor} name the competitor, {LostWithReason} give a reason.`
- **Undecided, past deadline.** Caption: "Submitted or drafting, deadline more than 30 days
  gone, no outcome recorded. {UndecidedNoDeadline} more have no deadline on file." Columns
  `Pursuit` · `Buyer` · `Owner` · `Deadline` · **Open**. Open goes to the pursuit, where the
  existing outcome dialog records the result. This is the backfill worklist; build no form here.
- **Where wins came from**: the Scorecard's source rows (`Source` · `Wins` · `Won fee`) from
  `IBdAttributionStore.GetSummaryAsync`, same numbers as the Scorecard page by construction.

## Piece 5 — clarity, inherited from brief 8

- Every date on the window goes through `BdDateDisplay` or the `BdDay` converter. No
  `yyyy-MM-dd` or `MMM d, yyyy` literal anywhere under `BusinessDevelopment/Director/`.
- Add `"BusinessDevelopment/Director"` to the ratchet's folder list in
  `InlineDateFormats_OnBdScreens_OnlyGoDown` and leave the ceiling at 64. The new folder must
  contribute zero.
- No developer words on the window: not "Overwatch", "Bazaar", "engagement", "CRM", "canonical",
  "UPN", "staff id". Say pursuit, buyer, person, owner.
- Any ComboBox you add carries `controls:Placeholder.Text` or a selection, per
  `BdCrmClarityRegressionTests.Pursuits_EveryInlineInputHasAPlaceholder`'s rule.
- The one visible "Bazaar": `OpportunitiesHubView.xaml` line 50 ToolTip becomes
  `Available, unclaimed opportunities: the same eligibility rules as the To grab checkbox.`

## Piece 6 — the harness and the gates

- `BdScreensRenderTests` gains `RenderDirector(dir)` writing `director.png` at 1680×980, added to
  the list at lines 77-80. Fixture = the facts table: eight people rows with exactly the counts
  measured (Omar 25/5/19/25/0/0/0/3/0 … through Ian 1/0/1/1/0/0/0/0/0; take overdue and
  no-next-action from the `OwnerStaffId` table above, activity and moves 0, buyers-mailed from
  the 25-of-101 row set: Omar 3, Conor 3, Islam 3, Jim 8, John 7, Rory 0, John Bryson 1, Ian 0;
  opens: give John Bryson 2 and Ian 6 so the column renders non-zero), a funnel with Drafting
  and Submitted counts summing to 101, `Won 0 Lost 0`, three undecided rows and two source rows.
  Mock every store (`Moq`), as the other render methods do; `RenderView` wraps in an
  `AdornerDecorator` already.
- New class `BdDirectorRegressionTests.cs` beside the others. Each gate EXPECTED vs ACTUAL, and
  the class summary states what it covers, what it does not, and one same-class fault it would
  miss (CLAUDE.md rule 11). Gates:
  1. `HomeWindow.xaml.cs` source checks `KnownRoles.BdDirector`; `HomeTileVisibilityState` has
     the slot and the failure state collapses it; `KnownRoles` has the constant; `App.config`
     has the key with Ian's address.
  2. The window's XAML and view model contain none of the developer words listed in Piece 5,
     and contain both captions verbatim.
  3. `SqlBdDirectorStore` SQL text contains `Stage IN (1, 3)`, the `BdStaff` join on `StaffKey`,
     `COALESCE(` on `Mailbox`, the `CrmBuyerEmailWarmth` join, the `BdUiOpens` read, and the
     `Deltek.CustomProposal` exclusion on the lost counts.
  4. `OpportunitiesHubView.xaml` contains no "Bazaar".
  5. The ratchet's folder list contains `BusinessDevelopment/Director`.
  6. The people pane groups by person: a view-model unit test feeds two `BdDirectorPerson` rows
     with the same `PersonKey` and different keys and asserts one row whose `Live` is the sum.
     (If you make the store return one row per person already, test the SQL text for the
     `GROUP BY` on the coalesced key instead, and say which you did.)
- Existing gates this brief touches (`HomeTileVisibilityStateTests`, the ratchet) are rewritten
  to the new behaviour, never deleted or loosened.

## What every piece must come with

- `using System.IO;` in any file that reads files; the UseWPF projects lack the implicit using.
  Guard nullable derefs in tests. These two are the repo's recurring Codex misses.
- Internals of the App are visible to the test project; Moq 4.20, xUnit 2.9.
- `git diff --check` clean. One response file beside this brief, named
  `CODEX-BD-DIRECTOR-9A-THE-WINDOW-AND-TWO-PANES-RESPONSE.md`, in the shape of brief 8's
  response: what was done per piece, gates authored, limits, files touched. Say plainly that
  nothing was built or run.

## Out of scope — do not do these

- Targets, asks, decisions, the "Mine" mode, the morning call sheet, anything that calls a model,
  any new table or migration, any email.
- A "release to the pool" action. No store method exists; it is a later brief.
- Changing `PursuitOverwatchRow`, `OverwatchView`, `OverwatchViewModel`, the Scorecard, or
  `CrmAnalyticsService` beyond reading them.
- Renaming the `Bazaar*` types or the `"Bazaar"` telemetry key.
- The other 64 inline date sites and the 42 ComboBoxes on other screens.

## The bound — files you will read (about 320 KB)

```
Kor.Operations.App/HomeWindow.xaml                                                     26 KB
Kor.Operations.App/HomeWindow.xaml.cs                                                  17 KB
Kor.Operations.App/HomeTileVisibilityState.cs                                           1 KB
Kor.Operations.App/Services/KnownRoles.cs                                               1 KB
Kor.Operations.App/Services/SecurityGroupAccess.cs                                      2 KB
Kor.Operations.App/App.config                                                          (lines 118-153 only)
Kor.Operations.App/Views/CooCardWindow.xaml                                            13 KB
Kor.Operations.App/Views/CooCardWindow.xaml.cs                                          1 KB
Kor.Operations.App/Views/CooCardViewModel.cs                                            5 KB
Kor.Operations.App/CompositionModules/OpportunitiesModule.cs                           22 KB
Kor.Operations.App/BusinessDevelopment/BdDateDisplay.cs                                 1 KB
Kor.Operations.App/Converters/BdDayConverter.cs                                         1 KB
Kor.Operations.App/Controls/Placeholder.cs                                              7 KB
Kor.Operations.App/BusinessDevelopment/Workspace/BdWorkspaceWindow.xaml                 9 KB
Kor.Operations.App/BusinessDevelopment/Workspace/BdWorkspaceWindow.xaml.cs             15 KB
Kor.Operations.App/BusinessDevelopment/Workspace/OverwatchView.xaml                     7 KB
Kor.Operations.App/BusinessDevelopment/Workspace/OverwatchView.xaml.cs                  9 KB
Kor.Operations.App/BusinessDevelopment/Workspace/OverwatchViewModel.cs                  8 KB
Kor.Operations.App/BusinessDevelopment/Workspace/OverwatchRowView.cs                    4 KB
Kor.Operations.App/BusinessDevelopment/Workspace/ReassignDialog.xaml                    2 KB
Kor.Operations.App/BusinessDevelopment/Workspace/ReassignDialog.xaml.cs                 3 KB
Kor.Operations.App/BusinessDevelopment/Workspace/AttributionView.xaml                   9 KB
Kor.Operations.App/BusinessDevelopment/Workspace/AttributionViewModel.cs                6 KB
Kor.Operations.App/BusinessDevelopment/Workspace/DashboardView.xaml                    (line 21 only)
Kor.Operations.App/BusinessDevelopment/Workspace/OpportunitiesHubView.xaml              4 KB
Kor.Operations.App/Crm/CrmAnalyticsService.cs                                           7 KB
Kor.Opportunities.Core/Models/CrmEngagement.cs                                          6 KB
Kor.Opportunities.Data/Crm/ICrmEngagementStore.cs                                       1 KB
Kor.Opportunities.Data/Crm/SqlPursuitOverwatchStore.cs                                 12 KB
Kor.Opportunities.Data/Crm/SqlBdAttributionStore.cs                                     6 KB
Kor.Opportunities.Data/Crm/SqlBdUiOpenStore.cs                                          2 KB
Kor.Opportunities.Data/Crm/IBdStaffDirectory.cs                                         2 KB
Kor.Opportunities.Data/Schema/03_Crm.sql                                                6 KB
Kor.Opportunities.Data/Schema/267_PursuitLifecycleFoundation.sql                        6 KB
Kor.Opportunities.Data/Schema/274_CrmEnrichment.sql                                     3 KB
Kor.Opportunities.Data/Schema/276_BdStaffDirectory.sql                                  3 KB
Kor.Operations.App/Kor.Transmittals.App.Tests/HomeTileVisibilityStateTests.cs           2 KB
Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdScreensRenderTests.cs            21 KB
Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdCrmClarityRegressionTests.cs     10 KB
Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdPresentationRegressionTests.cs   20 KB
```

## Files you will create

```
Kor.Operations.App/BusinessDevelopment/Director/BdDirectorWindow.xaml
Kor.Operations.App/BusinessDevelopment/Director/BdDirectorWindow.xaml.cs
Kor.Operations.App/BusinessDevelopment/Director/BdDirectorViewModel.cs
Kor.Operations.App/BusinessDevelopment/Director/BdDirectorPersonRow.cs
Kor.Opportunities.Data/Crm/IBdDirectorStore.cs
Kor.Opportunities.Data/Crm/SqlBdDirectorStore.cs
Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdDirectorRegressionTests.cs
docs/codex/CODEX-BD-DIRECTOR-9A-THE-WINDOW-AND-TWO-PANES-RESPONSE.md
```
