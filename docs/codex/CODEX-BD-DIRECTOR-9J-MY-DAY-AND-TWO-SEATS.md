# BD Director 9j — My day and two seats

Bounded brief for Codex. Source edits only; nothing is built, run, applied or deployed by you.
Response goes beside this file as `CODEX-BD-DIRECTOR-9J-MY-DAY-AND-TWO-SEATS-RESPONSE.md` in the
9a–9i format.

## Why this exists

Ian runs business development and is not an engineer. The Director window measures everyone else;
this brief gives the signed-in person their own day: who to reach, why now, what KOR already has
with them, a drafted note, one click to log the touch, and on a pursuit they lead, the engineer to
pull in as technical lead. Agreed with Ian on 2026-10-14 as "Mine mode": a morning call sheet, two
seats on a pursuit (BD lead and technical lead), and his own row measured like everyone else's.

Searched before writing (CLAUDE.md rule 1). Everything this needs already exists, and nothing here
is new storage:

- **Mine** — `BdDirectorPursuitRow.IsMine(actor)` (9g) matches the signed-in UPN to the owner's
  `BdStaff.Mailbox` or `StaffKey`. CRM's `MineOnly` does the same.
- **Touches** — `ICrmActivityStore.AppendAsync` writes `opportunities.CrmActivities` (ActivityType
  2 Call, 3 Meeting, 4 EmailOut, 1 Note) against an engagement.
- **Warmth and history** — `IPursuitContextStore.GetWarmthAsync` (nightly email rollup:
  `LastTouchUtc`, `Emails90d`, `TopCorrespondent`); `DeltekClientContextService.LoadAsync` (KOR's
  projects, lifetime fee, contacts for a Deltek client), as the Accounts tab (9b) and the Pursuit
  Brief already use it.
- **Who at KOR knows the client** — `FinancialsService` (lines ~390–425) already reads each Deltek
  project's manager: `PR.ProjMgr` joined to `EMMain`. The Deltek ledger joins fan out; aggregate per
  project by `LEFT(WBS1,5)` first (`reference_deltek_ledger_and_fanout`).
- **Two seats** — `CrmEngagements.OwnerStaffId` is the BD lead and `AssignedStaffIds` (comma
  separated staff keys) holds the others. `SqlCrmEngagementStore.UpdateAsync` writes both under the
  row version. **No new column.**
- **Raising the ask** — the ledger's create-ask method on `IBdDirectorLedgerStore`, the one the
  Asks tab's "Ask…" button already uses.
- **Starting a relationship pursuit** — `SqlCrmEngagementStore.InsertAsync` (CRM's create path).
  Migration 252's `UX_CrmEngagements_BdRelationship` is unique on (buyer, owner, region) for
  `OpportunityId IS NULL`.

Measured on the live store 2026-10-15 (fixture facts; do not remeasure):

| Fact | Value |
|---|---|
| Live pursuits (Stage 1 or 3) | 101; 101 with a buyer; 100 with a region |
| Live pursuits with `AssignedStaffIds` set | 16 |
| Ian's live pursuits | owner of 1 (300 Shackan, no next action, no region); assigned on 1 (453 Vancouver Fraser Port Authority, owner `Omar`, next action due 2026-09-19) |
| `CrmActivities` | 114 all time; **0 in the last 30 days** |
| Live buyers with a warmth row / with any live signal / with a signal seen in 30 days | 62 / 70 / **1** |
| Active `BdStaff` rows | 70; BD owners appear twice (`Omar` and `omara@…`, `Islam` and `ishabana@…`); non-engineers present (accountant, `contact@`, a gmail address, two other domains) |
| Regions on live pursuits | `Vancouver/LowerMainland` 38 · `Alberta` 30 · `Okanagan-BcInterior` 17 · `USA` 10 · `Vancouver Island` 2 · `VancouverIsland` 2 · `Okanagan` 1 · none 1 |

What those numbers mean for the design: Ian's call sheet cannot come from his pursuits alone (he
holds two), and "why now" cannot lean on intel signals (one live buyer has a recent one). Next
actions, asks and touch age carry it; a signal is shown with its age, never presented as news.

## The decisions, so you build the one thing

- **No migration, no new table, no model call.** The drafted note is a template filled only from
  stored facts. Technical question-and-answer preparation needs writing, so it is NOT in this brief;
  it belongs to a later in-session export/import brief (CLAUDE.md rule 14).
- **One new tab, "My day"**, the tenth, for whoever is signed in. It is not gated to Ian.
- **Never a second live engagement for one buyer.** Starting a relationship pursuit on an account
  someone already tracks offers to join that pursuit as a second seat instead.
- **Region matching uses a key, never the raw string:** lower-case, spaces removed, the part before
  the first `-`. `Vancouver Island` and `VancouverIsland` match; `Okanagan` and `Okanagan-BcInterior`
  match.
- **Staff identity resolves through `BdStaff`**: a key or mailbox maps to the row's `Mailbox`; the
  signed-in UPN matches the mailbox. An `AssignedStaffIds` token that resolves to nothing is shown
  as written and never matched.

## Pieces

### Piece 1 — the call sheet read (Data)

`IBdDirectorStore.GetCallSheetAsync(string actorUpn, DateTimeOffset nowUtc, CancellationToken ct)`
→ `IReadOnlyList<CallSheetItem>`, SELECT-only, at most 12 rows, in this order:

1. **Pursuits** the actor owns or is assigned on (live), next action overdue, then due today, then
   due within 7 days, then no next action set. `Seat` is `BD lead` (owner) or `Second seat`.
2. **Asks** open and addressed to the actor (`BdAsk.ToStaffKey` resolving to the actor), by due date.
3. **Accounts**: up to 5 Deltek clients with the deepest KOR history and no live pursuit:
   `IBdDirectorStore.GetAccountsAsync` (9b, the Accounts tab's read), in its own order, filtered to
   `LivePursuits = 0`. Reuse it; do not write a second ranking.

Each `CallSheetItem` carries: kind, engagement id (null for an account), ask id, buyer org id and
name, Deltek client id, contact (the engagement's `ContactIntelPersonId` → `IntelPerson` name and
email; else the warmth row's `TopCorrespondent`), warmth (`LastTouchUtc`, `Emails90d`), the newest
live signal (subject, `LastSeenAtUtc`), the newest `CrmActivities.OccurredAtUtc`, next-action note
and due date, ask text and due date, and the pursuit's region.

### Piece 2 — who at KOR knows the client (Deltek)

Extend `DeltekClientIntelligence` with `KnownBy`: one entry per project manager across the client's
projects — name, email (`EMMain.EMail`; if the column is absent per `DeltekSchemaValidator`, null),
job count, latest open date — ordered by job count then recency. Projects are counted once each
(aggregate by `LEFT(WBS1,5)`). `DeltekProjectSummary` gains the manager's name. Existing bindings
keep their names.

### Piece 3 — the My day tab (App)

One row per call-sheet item:

- **Who** — buyer, and contact name and email when known.
- **Why now** — the first that applies: `Next action overdue since {BdDay}` · `Next action due
  {BdDay}` · `Ask due {BdDay}: {text}` · `No next action set` · `No touch recorded since {BdDay}` /
  `No touch recorded`. A signal adds `Last signal {BdDay}: {subject}`, with its date always shown.
- **KOR history** — `{n} projects · {lifetime fee} · latest {name} ({year})`, and `Known by
  {name} ({jobs} jobs)` for the top `KnownBy` entry. `Not linked to a Deltek client` when unlinked.
- **Drafted note** — the template below; a Copy button puts it on the clipboard.
- **Actions** — `Log call`, `Log email`, `Log meeting` (`AppendAsync`, ActivityType 2 / 4 / 3, the
  engagement id, subject `My day: {kind}`, actor as `CreatedBy`), each disabled for an account row.
  Account rows show `Start tracking` (Piece 4). The row refreshes after a write.

The template, every slot from a stored fact, and a sentence dropped whole when its slot is empty:
`Hi {contact first name},` · `{reason sentence from the next-action note or the ask text}` ·
`KOR has worked with {client} on {n} projects, most recently {latest project} ({year}).` ·
`{known-by name} led that work.` · `Would you have twenty minutes in the next two weeks?` It never
contains `{` or `}`, never names a project or person not in the row, and never says "recently"
about a signal older than 30 days.

### Piece 4 — two seats and starting a pursuit (App + existing stores)

- **Pull in an engineer**, on a pursuit row where the actor is BD lead and no second seat resolves
  to a `@korstructural.com` mailbox: up to three candidates, each with its reason. A candidate is an
  active `BdStaff` row with a `@korstructural.com` mailbox AND either a `KnownBy` entry for this
  client (email-matched to `Mailbox`) or a live pursuit in the same region key. Rank: `KnownBy` jobs
  descending, then same region, then fewest live pursuits held. Reason text: `Led {n} jobs for
  {client}` / `Holds {n} live pursuits in {region}`. Choosing one: (1) `UpdateAsync` appends the
  candidate's staff key to `AssignedStaffIds`; a row-version conflict reloads and says so; (2) the
  create-ask method raises `Technical lead on {pursuit}: {next action or "first conversation"}`,
  60 minutes, due in 7 days, `EngagementId` set, to that staff key. If (2) fails after (1), say so
  in the status line; never silently.
- **Start tracking**, on an account row: when any live engagement exists for that buyer, show
  `Tracked by {owner}` and offer `Join as second seat` (append the actor to that engagement's
  `AssignedStaffIds` through `UpdateAsync`). Otherwise `InsertAsync` a relationship pursuit: owner =
  the actor's `StaffKey`, Stage Drafting, the buyer, a region chosen from the distinct region keys in
  use, `ExternalSource = N'BdDirector.MyDay'`. A unique-index refusal is shown, not swallowed.

### Piece 5 — the second seat on the People pane

The People pane already has a row per owner key, Ian's included. Add one column, `Second seat`: live
pursuits where the person appears in `AssignedStaffIds` (resolved through `BdStaff`), measured the
same way for everyone, in the existing read.

### Piece 6 — gates

**Data (`BdDirectorRegressionTests`):** (1) `GetCallSheetAsync` is SELECT-only, parameterised, and
caps at 12; (2) the Deltek `KnownBy` query aggregates by `LEFT(WBS1,5)` before counting and joins
`EMMain` on `PR.ProjMgr`; (3) no new `CREATE TABLE`, no migration file, no API marker.

**App (`BdDirectorMyDayRegressionTests`, new):** (4) ordering: overdue pursuit, due-today pursuit,
open ask, no-next-action pursuit, account, from a fixture holding one of each; (5) the note template
drops a sentence whose slot is empty and never contains `{`; (6) a 45-day-old signal reads `Last
signal {date}` and the note does not say "recently"; (7) `Log call` calls `AppendAsync` once with
ActivityType 2 and the engagement id, and is disabled on an account row; (8) candidates: a
`@korstructural.com` PM with 3 jobs ranks above a same-region owner with none; an accountant row and
a gmail row are never candidates; `Vancouver Island` matches `VancouverIsland`; (9) choosing a
candidate makes one `UpdateAsync` and one create-ask call in that order, and a failed ask after a
saved update shows a message; (10) `Start tracking` on a tracked buyer never calls `InsertAsync`
and offers `Join as second seat`; (11) the People pane's `Second seat` counts the fixture's assigned
rows, resolving `Omar` and `omara@…` to one person; (12) the date ratchet stays zero (every date
through `BdDay`).

**Render (`BdScreensRenderTests`):** (13) `director-myday.png` from the fixture: one overdue
pursuit with a known-by engineer, one ask, one account with `Start tracking`; minimum total renders
rises to 15.

## Claude's run after apply (not yours)

Build App tests, Data tests and CLI; run the gate classes and the full Director filter; render and
read `director-myday.png` and `director-people.png`; install the App locally; open My day as Ian and
check his two pursuits and the five accounts against the live store.

## Read bound

About 140 KB:
- the 9b, 9g and 9i briefs and responses in `docs/codex/`
- `Kor.Opportunities.Data/Crm/IBdDirectorStore.cs`, `SqlBdDirectorStore.cs`, `IBdDirectorLedgerStore.cs`,
  `ICrmActivityStore.cs`, `SqlCrmActivityStore.cs`, `SqlPursuitContextStore.cs` (the interface and
  `GetWarmthAsync`), `SqlCrmEngagementStore.cs` (`InsertAsync`, `UpdateAsync`), `IBdStaffDirectory.cs`
- `Kor.Opportunities.Core/Models/CrmEngagement.cs`
- `Kor.Opportunities.Data/Schema/252_CrmEngagementBdRelationshipUniqueIndex.sql`, `276_BdStaffDirectory.sql`, `348_BdDirectorLedger.sql`
- `Kor.Operations.App/Crm/DeltekClientContextService.cs`; `Kor.Operations.App/Financials/FinancialsService.cs` lines 380–430;
  `Financials/DeltekSchemaValidator.cs`
- `Kor.Operations.App/BusinessDevelopment/Director/` (view model, rows, window XAML and code-behind, dialogs)
- `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdDirectorRegressionTests.cs`,
  `BdDirectorPendingBidsRegressionTests.cs` (the Mine fixture), `BdScreensRenderTests.cs` `RenderDirector`

Do not open the live database or Deltek. App projects with `UseWPF` have no implicit `System.IO`.

## Files

Creates: `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/BdDirectorMyDayRegressionTests.cs`; the RESPONSE.

Edits: `IBdDirectorStore.cs`, `SqlBdDirectorStore.cs`; `DeltekClientContextService.cs`; the Director view model, rows, window
XAML and code-behind (and a small region-choice dialog if no existing one fits); `BdDirectorRegressionTests.cs`,
`BdScreensRenderTests.cs`, and any existing Director test fixture the new reads require.

## Response

In the 9a–9i format: what changed per piece, the gates authored and not executed, what was not done
and why, and anything in the existing code that contradicted this brief. Do not build, test, run
tools, open the database or commit. `git diff --check` clean.
