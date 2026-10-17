# BD Director 10a — response

## Piece 1 — moves cite cards

Added `HourMove.Cards`, an empty-by-default list serialized as `cards` with the existing hour JSON options. Missing `cards` remains valid. Parsing rejects nonpositive or repeated ids within a move. The CLI loads the hour role's cards and refuses proposed, rejected, retired, missing, or wrong-role citations before importing, reporting the move rank and card id.

Searched the existing hour parser, `tools/BdKnowledgeIngest` import/option handling, doctrine store, and doctrine regression tests before editing. Reused their import and validation paths; no new CLI verb or option. Earlier response files were not opened.

## Piece 2 — computed track records

Added `DoctrineTrackRecord.cs` with the specified positional record and pure `Compute` function. Each cited move counts as one suggestion; results split into Done, NotToday, NotDoing, and Undecided. Outcomes count distinct linked pursuit ids per card. Won also counts as advanced; Lost and Archived do not. `PursuitsArchived` is an additional init property because decision 3 requires a separate archived count but the brief's positional record omitted it.

The function's summary and displayed rule make the date assumptions explicit: the next 30 UTC calendar dates after the hour date, including day 30; starting stage is the latest history entry on or before the hour date, defaulting to Pending when absent. Reentering the starting stage is not advancement. Suggestions sort by date, hour id, then rank. NoEffect considers advances on Done pursuit moves only. The three flags use the exact requested thresholds; nothing retires automatically.

`IBdDoctrineStore.ListRecordsAsync` reads role-matching hours/results and history for linked pursuits, then delegates to `Compute`. There is no new table or persisted score. The rhythm store needed no edit because the brief permits this read on the doctrine store. Malformed stored hours fail the read rather than silently disappearing from the counts.

## Piece 3 — required closing reasons

Added the shared `PursuitCloseReason` helper, using `NoteDialog` with a required single line, maximum 2000 characters. Cancelling prevents the stage change/save. The view model also validates closing reasons before persistence. The existing optional Lost-detail prompt remains; skipping it preserves the required reason, and a supplied replacement must also be one line.

Close-path search covered `CrmEngagementStage.Won/Lost/Archived`, `ChangeStageAsync`, stage assignments, engagement-store callers, and lifecycle-store callers throughout App source. The paths found are:

- **Pursuits/CRM stage menu:** `CrmView.StageMenuItem_Click` → shared reason dialog → `CrmViewModel.ChangeStageAsync` → store update.
- **Pursuits/CRM engagement editor:** `CrmEngagementDialog.SaveButton_Click` → shared reason dialog → `CrmView.SaveEditedEngagementAsync` → `CrmViewModel.SaveEngagementAsync` → store update/insert.
- **Director Pursuits links/double-click:** `OpenPursuitLine_Click` / `PursuitLine_DoubleClick` → pursuit deep link → `BdWorkspaceWindow.NavigateToPursuit` → the same CRM screen and its two close paths.

Contrary to the brief's implication, the Director has no separate Won/Lost/Archived mutation. Its direct stage writes create Pending pursuits or submit them; the fee-proposal launcher also submits. The other lifecycle actions found own/release/dismiss/restore MPI projects, rather than changing CRM engagement stages.

`SqlCrmEngagementStore.UpdateAsync` now retains `OutcomeReason` for stage 8. It also stamps/preserves `ClosedAtUtc` for stage 8: the existing code cleared that date, which would exclude newly archived pursuits from the synthesis's 90-day closed list. Reopening clears both. Existing archived rows were not backfilled.

## Piece 4 — records and decisions on Doctrine

Accepted cards show the requested record line, or “Not suggested yet,” plus undecided/won/lost/archived counts. “Needs a look” appears above Accepted for flagged accepted cards, with plain-language flags and both requested buttons.

Retire confirms first, then calls `RetireCardAsync` with actor `Ian`. The SQL update is guarded by accepted status and returns false for other statuses. Ask Claude to amend opens an editable, prefilled question containing the card id and record, then uses the existing local prompt launcher/report flow. Full card facts and the calculation rule accompany the question. Refreshing after a move result or card decision updates the record and citations.

## Piece 5 — cards on Today

Moves show “From the doctrine: …” under the principle. Accepted and retired citations resolve; proposed/rejected cards do not appear. Retired titles are struck through and marked “retired.” Clicking a citation selects Doctrine and brings a focused card panel into view, retaining its historical record even after retirement.

## Piece 6 — synthesis reads KOR's record

Added `Records` and `Closed` to `BdSynthesisInput`, and `ClosedPursuit`. Both the CLI and App gather fresh records and pursuits closed within the last 90 days before creating a synthesis prompt.

“What KOR's own record shows” includes flagged cards first, the record table, and dated Won/Lost/Archived pursuits with companies and reasons. Missing reasons are labelled. The prompt requests amendments or retirement recommendations, and permits experience-derived cards only when two or more outcomes share a reason, citing pursuit ids. Retirement recommendations go in the answer for Ian to decide; they introduce no automatic retirement/report command.

The old synthesis rules required every recommendation and citation to come from books. Those rules and the report example now explicitly allow the requested outcome evidence. Duplicate detection is unchanged. No model call or outside-service integration was introduced.

## Piece 7 — authored gates and verification limits

Authored, **not executed**:

- `DoctrineTrackRecordTests` in the non-WPF Data test project: mixed results, untouched cards, day 0/1/10/30/31/40 boundaries, starting-stage comparison, separate outcomes, repeated-link deduplication, each flag at its threshold and one short, ordering, Done-only NoEffect behavior, old JSON, and invalid card ids.
- `DoctrineTrackRecordStoreTests` in the same file: actual store retirement/refusal behavior and decision actor, archived reason/date persistence, inclusion in the closed-pursuit read, and reopening. These are explicitly skipped unless `KOR_DOCTRINE_TEST_DB` names a migrated disposable test database; they do not fall back to the application's connection.
- `BdDoctrineRegressionTests`: citation acceptance/role errors and CLI gate-before-write wiring; required reason/cancellation for all three closing stages; source checks for both close paths; card flags, retirement and retired navigation; synthesis fixtures with two shared reasons and 90-day boundaries. The existing CLI allowed-options test remains. CLI wiring checks inspect source rather than launching the executable.
- `BdScreensRenderTests`: one flagged card and two recorded accepted cards on Doctrine; accepted/retired citations on Today, strike-through assertions, and citation navigation. The existing fixture will produce `director-today.png` and `director-doctrine.png`; no PNG was produced or inspected in this pass.
- Updated `BdDirectorRegressionTests`' existing close-date SQL assertion to include Archived.

Read back the edits and checked the diff. `git diff --check` is clean. Builds, test runs, pre-fix failure checks, application/CLI execution, database/share/Deltek access, installation, imports, deployment, and commits were not performed, as required by this brief. Gates therefore remain authored and unverified. Historical hours without citations contribute no card suggestions; archived rows without existing close dates remain outside the dated closed-pursuit list until separately repaired. Unrelated changes present at session start were left untouched.
