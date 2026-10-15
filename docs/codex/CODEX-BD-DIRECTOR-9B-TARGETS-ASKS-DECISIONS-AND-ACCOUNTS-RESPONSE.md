# BD Director 9b — targets, asks, decisions and accounts

Applied 2026-10-14. **Source edits and regression sources only; nothing was built,
run, rendered or applied to a database. Migration 348 is unapplied.** No tests,
application/model calls, database tools, deployments, emails or service actions
were run. Source searches, source review and whitespace checks were performed.
`git diff --check` passed; new files were also checked without staging them.
No executed pre-fix failure, post-fix pass or visual verification is claimed.
No commit was made.

Prior art: reviewed the bounded 9a implementation/response, existing typed SQL
write pattern, pursuit record/update contract, staff picker, selected-client Deltek
contract, dossier route, Dashboard, DI and render/regression references. Searched
the migration/brief locations for an existing 348 and followed migration 274 and
the schema README. Accepted the supplied production measurements without
remeasuring them. Read the Dashboard source at `dcfe00a9`: its count of
`yyyy-MM-dd` / `MMM d, yyyy` literals was **zero**.

## Applied changes

- **Piece 1 — migration:** Added idempotent, GO-batched migration 348 with the three
  guarded tables, required checks/defaults, non-cascading foreign keys and the
  separately guarded ask-recipient/status index. It contains no database selection,
  destructive DDL or data moves. The allowed decision value `Drop` remains a string
  literal; the regression gate distinguishes it from a DROP statement.
- **Piece 2 — ledger:** Added the records, metric lists and ledger interface/store.
  All parameters are typed. Inserts return the generated ID; the three mutation
  paths return `Task`. Targets can only be retired, asks can only be closed while
  Open, and decisions can only have their four outcome fields scored. Original
  wording has no edit/delete API. Lists use the specified filters and ordering.
- **Piece 3 — read store:** Kept the 9a store SELECT-only. Target actuals execute one
  batch with coverage first and one scalar result per key, using five typed
  parameters per key. Firm/region/person scopes, case-insensitive scope keys,
  snapshot metrics, end-exclusive period metrics and the Deltek exclusion for
  pursuits opened follow the brief. Accounts read active Deltek clients ordered by
  project count, with live pursuits, warmth and open asks. The last-project column
  is read with `GetDateTime` at ordinal six.
- **Piece 4 — window/dialogs:** Added the five tabs in the specified order, keeping
  the existing People/Funnel captions, bindings and content. Added the pursuit Ask
  button, target actuals/coverage, account list and selected-account summary, and
  ask/decision grids and actions. All six caption constants use `x:Static`. Dates
  use the shared formatter/converter; fees use the current-culture `C0` format.
  Added the four dialogs, field limits, bounded minutes, quarter/default dates and
  local-midnight-to-UTC conversion. Person presets resolve legacy owner spellings
  to mailboxes. Writes use the actor already resolved by the window.
  Selected-account reads cancel their predecessor and discard stale responses;
  loading the account list does not call Deltek. Ask creation saves the ledger row
  first, then mirrors the fresh pursuit record's next-action fields through the
  existing store. A failed mirror is reported as a saved ask with a failed mirror,
  preserving the ask and avoiding a misleading retry instruction. Target actions
  reload targets only. Superseded ask-filter reads do not abort the rest of a
  window refresh; partial refresh failures report that not all figures refreshed.
- **Piece 5 — Dashboard:** Added the optional ledger constructor argument, actor-
  filtered open asks and the collapsed-when-empty “Asked of you” section above
  Priority Actions. Done/Can't open the optional-note dialog, close as Done/Declined
  and remove the row. Notes respect the 500-character closure limit. Both existing
  Dashboard data-loading paths load asks. Duplicate close clicks and stale reads
  are guarded; the existing AI context was not expanded with ask contents.
- **Piece 6 — wiring:** Registered the ledger singleton beside the existing read
  store. The Director view model receives the ledger and existing Deltek service.
  Dialogs are constructed with their runtime arguments as Piece 6 requests; no
  extra dialog DI registrations were added. Updated the 9a fixture constructors
  without changing their gates. Home visibility and the date-ratchet ceiling were
  left intact.
- **Piece 7 — harness/gates:** Authored the new ledger regression class with scope,
  exclusions and same-class misses in its summary. The render harness now selects
  and lays out each tab, writing `director-number.png`, `director-people.png`,
  `director-funnel.png`, `director-accounts.png` and `director-asks.png` at 1680×980.
  It no longer writes `director.png`; the minimum count is ten. The shared render
  helper releases its adorner host between tab renders. No PNG was produced or
  inspected in this session.

## Gates authored, not executed

| Gate | Expected versus actual checks authored |
|---|---|
| `Migration348_IsAdditiveIdempotentAndBatched` | File, guards/batches/settings, allowed CHECK values, minutes/period checks, index and absence of destructive/data statements or cascades. |
| `LedgerStore_WritesOnlyItsThreeTables_AndNeverDeletes` | Three inserts and three updates target only ledger tables; permitted SET lists, Open-only ask closure and typed parameters. |
| `ReadStore_StaysSelectOnly_AndReadsTheAccountPopulation` | No write keywords, active Deltek-client population/order, open asks and matching datetime2 column/reader ordinal. |
| `TargetActuals_UseOneParameterizedBatch_AndMetricSpecificPeriods` | One reader execution, five numbered parameter families, period/live-stage branches, source exclusion and case-insensitive scope comparison. |
| `Decisions_AreImmutableExceptTheirOutcome` | Reflection finds the scoring method and no update/edit/delete/remove methods. |
| `Ask_OnAPursuit_MirrorsToTheNextAction` | Linked/unlinked cases: one ledger creation, one correctly attributed mirror for a linked pursuit, preserved other fields, and no mirror for a free ask. |
| `Ask_MirrorFailureKeepsTheSavedRecord_AndSaysSo` | Ledger creation remains successful when the pursuit read fails; status explicitly explains the failed mirror. |
| `Targets_ActualsComeFromTheReadStore` | Full keys, returned actuals, fee coverage and safe “Unknown metric” display. |
| `Accounts_ReadDeltekForTheSelectedAccountOnly` | No Deltek calls on window load, one call per selected client and the null-result sentence. |
| `Accounts_IgnoreAStaleSelectionResponse` | Previous token is cancelled and a late first response cannot replace the second selection. |
| `Asks_FilterChangeDuringRefresh_KeepsNewestRowsAndFinishesRefresh` | New filter cancels the old read, retains the new rows and lets the window finish loading decisions. |
| `Dashboard_ShowsOpenAsksForTheSignedInPerson` | Exact actor key, rows, Done/note/actor forwarding, removal and optional-ledger behavior. |
| `Window_HasTheFiveTabs_InOrder_AndPlainWords` | Five ordered headers, six static captions, visible literals across all Director files and ComboBox selection/placeholders. |
| `Director_StillContributesZeroInlineDates` | Zero formats in all Director sources and the unchanged Dashboard baseline of zero at `dcfe00a9`. |
| Existing 9a class and render harness | Updated mocks, preserved existing gates, five tab outputs and measured account-fixture totals. |

## Fixture discrepancies and limits

The account fixture uses the supplied 15 IDs, names, kinds, project counts, live
pursuit counts and touch dates verbatim. Every row marks the count as records.
The harness asserts **15 accounts, four with a live pursuit, three touched within
90 days of 2026-10-14**. Client identifiers, last-project dates, open-ask counts,
three asks, two decisions and the selected Deltek fee/AR summary are explicitly
illustrative layout data. The two example targets are 120 live pursuits and 100
accounts with a live pursuit; their actuals are the supplied **101 and 77**, and
coverage is **101 live / zero with a fee**. These target settings are examples,
not production ledger records.

As requested, the 9a People/Funnel fixture is unchanged. Its stage split and
medians are layout examples, so its Submitted examples do not represent the 9b
measurement of zero Submitted pursuits. The inherited illustrative per-person
action splits, deadline/title/date examples, source fees and zero-win second
source placeholder retain the limitations recorded in the 9a response. The UI
does not synthesize that placeholder in production.

The brief says “five caption constants” but specifies six (Number, People, Funnel,
Accounts, Asks, Decisions); all six are implemented and gated. Its “Edits (10)”
list names twelve files; this change edits those twelve and creates the thirteen
listed files, including this response.

The actuals command has an explicit 420-key ceiling for its five-parameters-per-key
design and SQL Server's 2,100-parameter limit; exceeding it fails rather than
silently dropping targets. The accounts pane requests the top 1,000 clients, which
includes the supplied current population of 569. Reads across stores are not a
single database snapshot. Ask creation and the next-action mirror are separate
writes; a mirror failure does not roll back the ledger record. Closing an ask
retains the mirror, as specified by the closure-only API.

SQL source gates cannot prove that SQL Server accepts the DDL, permissions,
cardinality or query performance. Mocks cannot prove WPF binding/layout, dialog
interaction, deployment or live identity behavior. Literal scans cannot find
developer wording supplied through data or composed strings. No pre-fix negative
control, post-fix execution, render or database verification has been performed.

Claude owns the changed-project Debug build, filtered regression execution and
negative controls, migration 348 apply/idempotence check with sqlcmd, five-tab
render/PNG review and local deployment. Ian has no execution step in this handoff.

## Files touched

New (13):

- `Kor.Opportunities.Data/Schema/348_BdDirectorLedger.sql`
- `Kor.Opportunities.Data/Crm/IBdDirectorLedgerStore.cs`
- `Kor.Opportunities.Data/Crm/SqlBdDirectorLedgerStore.cs`
- `Kor.Operations.App/BusinessDevelopment/Director/TargetDialog.xaml`
- `Kor.Operations.App/BusinessDevelopment/Director/TargetDialog.xaml.cs`
- `Kor.Operations.App/BusinessDevelopment/Director/AskDialog.xaml`
- `Kor.Operations.App/BusinessDevelopment/Director/AskDialog.xaml.cs`
- `Kor.Operations.App/BusinessDevelopment/Director/DecisionDialog.xaml`
- `Kor.Operations.App/BusinessDevelopment/Director/DecisionDialog.xaml.cs`
- `Kor.Operations.App/BusinessDevelopment/Director/NoteDialog.xaml`
- `Kor.Operations.App/BusinessDevelopment/Director/NoteDialog.xaml.cs`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdDirectorLedgerRegressionTests.cs`
- `docs/codex/CODEX-BD-DIRECTOR-9B-TARGETS-ASKS-DECISIONS-AND-ACCOUNTS-RESPONSE.md`

Edited (12):

- `Kor.Opportunities.Data/Crm/IBdDirectorStore.cs`
- `Kor.Opportunities.Data/Crm/SqlBdDirectorStore.cs`
- `Kor.Operations.App/BusinessDevelopment/Director/BdDirectorPersonRow.cs`
- `Kor.Operations.App/BusinessDevelopment/Director/BdDirectorViewModel.cs`
- `Kor.Operations.App/BusinessDevelopment/Director/BdDirectorWindow.xaml`
- `Kor.Operations.App/BusinessDevelopment/Director/BdDirectorWindow.xaml.cs`
- `Kor.Operations.App/BusinessDevelopment/Workspace/DashboardViewModel.cs`
- `Kor.Operations.App/BusinessDevelopment/Workspace/DashboardView.xaml`
- `Kor.Operations.App/BusinessDevelopment/Workspace/DashboardView.xaml.cs`
- `Kor.Operations.App/CompositionModules/OpportunitiesModule.cs`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdDirectorRegressionTests.cs`
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdScreensRenderTests.cs`

The seven files already untracked when 9b started, including its brief, were preserved.
