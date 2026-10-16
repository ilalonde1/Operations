# BD Director 9j — My day and two seats

Implemented as source edits on 2026-10-15. **Nothing was built, tested, rendered,
installed, applied to a database, deployed or committed.** Neither the Opportunities
database nor Deltek was opened. No application, CLI, database tool or gate was run.
Source inspection and whitespace checks only; `git diff --check` is clean, including
separate checks of the two new files. Compilation and runtime behavior remain unverified.

**No migration, new table or model call.** No email is sent. Drafts use stored facts
and an ordinary template. Technical question-and-answer preparation remains out of scope.

Prior art: the existing Director reads, ledger, activity and pursuit stores, staff
directory, Deltek client context and financial manager join, schema validator,
relationship/contact/staff/ledger schemas, Director UI, and regression/render fixtures.
The 9b/9g/9i briefs and responses supplied the surrounding contracts. The three
untracked insurance PDFs and vocabulary file were left untouched.

## Applied changes

- **Piece 1 — call sheet:** Added `CallSheetItem` and `GetCallSheetAsync(actorUpn,
  nowUtc, ct)`. Typed SQL parameters supply the actor, local-calendar boundaries,
  five account IDs and the fixed 12-row limit. Actor ownership, assigned tokens and
  ask recipients resolve through `BdStaff`. Reads include the contact, email warmth,
  latest nonretired signal, latest logged touch, action/ask dates and text, region
  and seat. Accounts reuse `GetAccountsAsync` in its existing order, select at most
  five with no live pursuit, and recheck that condition in the final SELECT. The
  complete account ranking is read so an already-tracked prefix cannot hide the
  deepest untracked clients. No second account ranking was introduced.
- **Piece 2 — Deltek:** Added `KnownBy` and optional `ManagerName` on project summaries
  without renaming existing bindings. Client jobs are reduced by `LEFT(WBS1,5)`;
  one master row per job is selected deterministically by latest open date, then
  WBS1/manager. Counts are aggregated before the manager join. Results sort by job
  count and recency. An optional-column probe in `DeltekSchemaValidator` permits
  `EMMain.EMail` to be absent; email is then null. A failed KnownBy section uses the
  existing degraded-history indication.
- **Piece 3 — My day:** Added the tenth tab for the signed-in mailbox, with buyer,
  contact, reason, explicitly dated signal, history, KnownBy, drafted note and Copy.
  All displayed day values bind through `BdDay`; project years are numeric years.
  Missing template slots drop their entire sentence, and literal braces are removed
  from inserted facts. Signals never enter the drafted note. Call/email/meeting append
  exactly one activity of type 2/4/3, subject `My day: Call/Email/Meeting`, with actor
  and pursuit ID. Accounts cannot log; an ask without a pursuit also cannot log.
  Successful writes refresh My day and People. Refresh failure retains a message
  that the write completed. Deltek loads once per distinct client in the bounded
  sheet, using the existing service cache; the Accounts selection path is unchanged.
- **Piece 4 — two seats:** Candidate eligibility requires active company staff plus
  client-manager or same-region ownership evidence. Mailbox aliases collapse to one
  person. Ranking is jobs, same-region presence, then fewest live pursuits held;
  at most three candidates appear. Unknown assigned tokens remain visible literally.
  Choosing a candidate rereads the pursuit and directory, rechecks eligibility,
  appends the staff key under the current row version, then calls the ledger directly
  for a 60-minute technical-lead ask due seven days later. A row-version conflict
  reloads with an explicit message. A failed ask after a saved seat is reported as
  a partial success and directs the user to Asks. The existing 500-character ask
  limit is honored; assigned lists exceeding 500 characters reject before writing.
  Start tracking rereads all live buyers first, offers `Tracked by …` / `Join as
  second seat` when found, and otherwise requires a normalized region key in use.
  Creation uses the actor's staff key, Drafting and `BdDirector.MyDay`. A narrow
  guard in the existing store holds the buyer range within the insert transaction
  to prevent two My day creates racing across owners/regions. Duplicate-index
  refusals are surfaced rather than returning an existing row silently. Join
  preserves existing assigned tokens and uses the reread row version.
- **Piece 5 — People:** Added the `Second seat` column to the existing aggregate and
  row binding. SQL counts each live assigned pursuit once with `EXISTS`, including
  key/mailbox aliases. The population remains people who own live pursuits.
- **Piece 6 — integration:** Registered the existing staff-directory store, supplied
  activity/directory dependencies to all Director fixtures, updated ten-tab
  expectations and the concrete Knowledge test store's interface implementation.
  No region dialog was needed: account cards use an inline region selector.

## Gates authored, not executed

| Gate source | Coverage |
|---|---|
| Existing `BdDirectorRegressionTests` (App project; Data source contracts) | SELECT-only call sheet, typed parameters, cap 12, shared account ranking, contact/signal/activity joins; five-character job reduction before manager counts and staff join; optional email; existing storage/API-marker checks; transaction guard and explicit duplicate refusal. |
| New `BdDirectorMyDayRegressionTests` | Required ordering plus within-seven-days ordering/cap; missing template sentences/braces; dated old signal; all three activity types and disabled account logging; candidate evidence, alias collapse and region normalization; ordered seat/ask writes and partial failure; row-version conflict; tracked buyer join without insert; new pursuit fields and duplicate refusal; assigned alias counts and People binding; date ratchet. |
| Existing Director fixture gates | Ten tab names/order, constructor dependencies and empty default reads. |
| `BdScreensRenderTests` | Added `director-myday.png` with overdue pursuit/KnownBy, ask and Start tracking account; dated-signal/reason/button assertions. People fixture includes the second-seat column, Ian's supplied count of one and an explicitly illustrative Omar count of two. Minimum total renders rises from 14 to 15. |

The new behavior fixture uses a non-Ian actor and illustrative clients/people. Source
gates do not establish actual SQL cardinality, permissions, query plans or concurrency
behavior. The no-migration source gate checks feature-named migration files; the
change-list inspection additionally confirms that no schema file was added or edited.
No pre-fix failure, post-fix pass or visual evidence is claimed. No PNG was generated.

## Contradictions and implementation decisions

1. Piece 1 puts missing-next-action pursuits before asks, while gate 4 explicitly
   requires the reverse. Implemented the gate's order: overdue, due today, within
   seven days, open asks, missing next action, accounts. Overdue/today use local
   calendar days, consistent with the date presentation.
2. `BdDirectorPursuitRow.IsMine` does not exist in the starting source. `IsMine` is on
   `BdDirectorPendingBidRow`. My day therefore has explicit directory-based identity
   resolution; unresolved tokens never become an actor match.
3. `DeltekSchemaValidator` previously only reported missing required columns. It had
   no optional-column availability operation. Added one without making email a
   required financial-schema column.
4. `SqlCrmEngagementStore.InsertAsync` previously returned an existing natural-key
   pursuit both before insertion and after a unique-index error. My day now bypasses
   those silent-return paths and adds the buyer-wide live check in the transaction.
   Existing callers retain their prior behavior. This guards the My day creation
   path; it does not retrofit every other pursuit writer with a new global invariant.
5. The staff directory existed in Data but was not registered in the App. Added its
   singleton registration beside the existing Director store.
6. The general Asks action mirrors the ask into next-action fields with another
   pursuit update. My day's required one-update-then-one-ask sequence calls the ledger
   directly, preserving the pursuit's existing next action.
7. The older Accounts brief prohibited loading Deltek for every list row. The new
   brief requires history on each of at most 12 call-sheet rows; those bounded reads
   are confined to My day and do not change Accounts selection behavior.

## Files and handoff

Two files created: `BdDirectorMyDayRegressionTests.cs` and this response. Seventeen
existing files edited. Supporting edits beyond the brief's explicit list are the
optional schema probe, the existing pursuit insert guard, App directory registration,
and the Knowledge fake-store interface implementation. No schema files changed.

Claude's follow-up remains the affected-project Debug builds (App/Data tests and CLI,
plus the changed Knowledge test project), named gates/full Director filter, render
and visual review of `director-myday.png` and `director-people.png`, local installation,
and Ian's two pursuits/five-account live comparison. These were deliberately not
performed here because 9j reserves execution for the follow-up run. No commit was made.
