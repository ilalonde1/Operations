# BD Director 9g — pending bids on the existing pursuit

Applied as source edits on 2026-10-15. **Nothing was built, run, applied to a database
or deployed.** No application, CLI, test, gate, render, service action or commit was
performed. The live database and opportunity folders were not opened. Source
inspection and whitespace checks only: `git diff --check` is clean, with separate
no-index whitespace checks for the new files. No pre-fix failure, post-fix pass,
SQL execution or visual proof is claimed.

**No new table, lifecycle or deadline was created. No model call exists anywhere
in this change.** Pending is the display name of the existing enum value 1. The
submission timestamp is read from stage history, and the deadline remains on the
opportunity. A pasted folder link is opened by the shell; nothing inspects folders.

Prior art searched/read: the handoff's settled decisions, 9f brief and response,
the Director and CRM screens and fixtures, engagement/grab/reassignment stores,
Director read contracts, schema directory and lifecycle/staff definitions, the
opportunity editor, shared BD date presentation, Dashboard deadline formatter,
and the existing render harness. These already provide the pursuit lifecycle,
assignment dialog, opportunity editor, stage history and countdown; those paths
are reused. Unrelated NetworkOps work, PDFs, the supplied brief and vocabulary
artifact were not edited.

## Applied changes

- **Piece 1 — migration 353:** Exactly two nullable column additions, each with
  its own `COL_LENGTH` guard and batch: `SubmittedFileName NVARCHAR(260)` and
  `FolderLink NVARCHAR(1000)`. No data statements or grants.
- **Piece 2 — model and engagement store:** Added both properties to the model,
  SELECT projection, INSERT, UPDATE, parameters and returned row images. Existing
  ordinals remain stable; the two fields occupy 30–31 and the previous-stage output
  moves to 32. Concurrency, transactional history, close-out handling, provenance
  restrictions and the buyer-name projection are retained. `CrmStageNames.Display`
  supplies the four screen names. CRM rows, chips, context/status/export wording,
  stage menu and editor, plus Director pursuit/funnel rows, use the shared labels.
- **Piece 3 — Director reads:** Added `PendingBidRow`, `ListPendingBidsAsync` and
  `CountRelationshipPursuitsAsync`. Bids require an opportunity and stage 1 or 3;
  the read joins opportunity details and `BdStaff.StaffKey`, takes the latest
  Stage 3 history timestamp, and sorts deadlines ascending with nulls last.
  Relationship work is counted separately with the inverse opportunity predicate.
  Both queries are SELECT-only.
- **Piece 4 — Pending bids:** Added the ninth tab, exact static caption, summary,
  all ten columns, local date/time through `BdDay`, shared countdown, Pending-only
  red urgency, shell link, and Mine off by default. Mine matches either mailbox
  or stored staff key, ignoring case. Both reads happen on load/refresh, with no
  tab-change reads. Mark submitted validates file/link lengths, reads the current
  pursuit and saves its concurrency token through the engagement store, then
  reloads. Already-submitted or closed pursuits are refused. Assign uses the
  existing reassignment dialog and guarded store path. Edit opportunity uses the
  existing editor/store and reloads. Cancel makes no submission write.
- **Piece 6 — handoff:** Appended the requested “Built on, 2026-10-15” paragraph
  beneath section 2, preserving the existing decisions and wording. It identifies
  `CrmEngagements`, the bid predicate and duplicate Leduc rows 39322 / 39332, with
  39322 designated for the subsequent app workflow.

Source findings required small adjustments to the brief's assumptions:

- The Director already receives `ICrmEngagementStore` as its third constructor
  argument and DI already registers it. That dependency is reused; the constructor
  remains at ten parameters, rather than injecting the same service twice. Existing
  full-load fixtures now supply the two new reads. The concrete Knowledge test
  store also implements the expanded interface.
- The existing engagement store synchronized opportunity status only for Won/Lost,
  not Submitted. Its existing transaction now explicitly maps all four stages:
  Pending → Pursuing (4), Submitted → Submitted (5), Won → Won (6), Lost → Lost (7).
  This avoids writing engagement stage 3 into the opportunity's different enum and
  keeps a reopened Pending pursuit aligned as well. No additional write statement
  or opportunity column was added.
- `BdDay` previously showed only a date. Its optional `Time` parameter now uses
  the existing `BdDateDisplay.DayAndTime`; other consumers keep their date-only
  behavior. Calendar-week grouping lives in the shared date helper. The existing
  Market deadline projection supplies countdown formatting but no urgency property;
  the bid row implements the specified Pending-and-within-24-hours-or-past rule.
- The Number caption no longer asserts that no pursuit has reached Submitted.
  It still describes the existing target options without a fixed claim about data.

## Gates authored, not executed

- **`BdDirectorPendingBidsRegressionTests`:** Nine-tab order, exact caption and
  columns, stage display without changing enum values, no Drafting screen literal,
  one read per load/refresh, no Mine/tab reads, bid/relationship negative control,
  mailbox/key identity, urgency boundaries, shared countdown, local calendar weeks,
  date/time conversion and the zero-date-format ratchet. Submission checks cover
  both fields and actor, a freshly fetched row/token, cancellation, field limits,
  and refusal to restamp or reopen a moved bid. Assignment and opportunity edit
  checks exercise the existing stores and reload paths.
- **`BdScreensRenderTests`:** Adds `director-pending.png` from a coherent fixture
  with six pursuits owned by two people: two Pending bids due in six hours and three
  days, one Submitted bid, and three relationship pursuits. It asserts three bid
  rows, exactly one red row, the Submitted timestamp in the rendered cell, blank
  Pending timestamps and the relationship count of three. Selecting all nine tabs
  still reads each new query only once. Minimum total renders rises to fourteen.
  The older eight Director screenshots retain their existing fixture. The harness
  also invokes the new regression class's modal OK/Cancel helper on its themed STA
  thread, through the production window action, checking one update versus none.
- **`BdDirectorRegressionTests`:** SELECT-only bid/count SQL, population predicates,
  staff identity join, history timestamp and deadline ordering; exact migration 353
  batches; exact INSERT/UPDATE column sets; output/reader ordinals; retained history,
  concurrency and close-out clauses; unchanged write-statement/table boundary; and
  the actual stage-to-opportunity-status mapping.
- **Existing gates/fixtures:** Director, ledger, Knowledge and Market tab-order
  expectations now include the ninth tab. Full-load mocks provide the new reads.
  The Knowledge brief fixture has the two required interface methods without
  adding pending bids to the brief-export workflow.

## Fixture details and limits

The summary's assignee and due-week breakdowns count Pending bids; Submitted has
its separate count. Mine narrows the visible bid counts and breakdowns, while the
relationship exclusion count remains global. Weeks use the workstation's local
calendar and locale's first day of week. Unknown deadlines and deadlines before
the current week do not inflate this-week/next-week/later buckets; those bids stay
visible, and expired Pending bids are red. Countdown/urgency refresh with the
loaded snapshot, matching the existing Market refresh behavior.

The six-pursuit fixture is illustrative, not evidence about Leduc or live counts.
Its fake store and SQL source checks do not prove query execution, permissions,
transaction rollback, actual history timestamps or database cardinalities.
Assignment retains the existing owned-pursuit path; a row absent from that board
prompts a refresh rather than inventing a new assignment mechanism. Shell opening
and visual layout have not been exercised. The modal interaction checks are part
of the render harness, not the non-render test run.

## Files and handoff

Five new artifacts: migration 353, SubmittedDialog XAML/code-behind, the new App
regression class and this response. Existing edits cover Core/Data, Director/CRM,
the shared date helper/converter, affected fixtures/render harness and the handoff.
The Knowledge test interface implementation is the only change outside those
listed App/Core/Data/document areas.

Claude owns the affected-project Debug builds, filtered gates and pre-fix negative
controls, CLI build, rendering and reading `director-pending.png`, and local App
installation. Include the Knowledge tests in the build check because their concrete
Director-store fixture implements the expanded interface. Ian applies migration
353 in SSMS. Then, in the app, grab opportunity 39322 for
`ishabana@korstructural.com`, mark it submitted using the actual PDF file name and
folder link, and confirm today's submitted time and a Submitted count of one.
No commit was made.
