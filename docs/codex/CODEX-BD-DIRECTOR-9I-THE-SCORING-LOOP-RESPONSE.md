# BD Director 9i — the scoring loop

Implemented as source edits on 2026-10-15. **Nothing was built, run, applied to a
database or deployed.** No application, CLI verb, test, gate, render, service action
or commit was performed. The live database, Books folder and `.claims.json` files
were not opened. Source inspection and whitespace checks only: `git diff --check`
is clean, including separate no-index whitespace checks of the two new files.
No compilation, pre-fix failure, post-fix pass, SQL execution or visual proof is claimed.

**No new table was created. Nothing retires a claim without a user's action.
No model call exists in the scoring-loop change.** The existing local meaning
search remains the export's retrieval mechanism; import still constructs no embedder.

Prior art searched/read: the 9f and 9g briefs and responses, schema directory and
migrations 348/350/352, Director brief and ledger contracts/stores, claim review
contracts/store, Knowledge brief files/search/store contract, CLI brief verbs,
Director view model/rows/XAML/dialogs, existing regression fixtures and render harness.
These already supply transactional import, immutable decisions with outcome scoring,
citation snapshots, user-driven retirement and shared date presentation. Those paths
are extended. The three unrelated untracked insurance PDFs and vocabulary file remain
untouched.

## Applied changes

- **Piece 1 — migration 356:** Guarded replacement of `CK_BdDecision_Kind` adds
  `Brief`. The drop checks the current definition; the add checks for absence.
  Separate guarded batches add nullable `BriefRowId`, its `NO ACTION` foreign key
  to `DirectorBrief(Id)`, and its index. No data statements, grants or new tables.
- **Piece 2 — import:** Sections gain `expected` and optional `reviewAfterDays`.
  Recommendations require a nonblank, single-line expected outcome of at most
  500 characters; supplied review offsets must be 7–90. After inserting the five
  brief rows and capturing their IDs, the existing transaction inserts one Brief
  decision per nonblank recommendation. Headline/rationale are bounded with SQL
  `LEFT`, the importing actor is recorded, and pursuit/account IDs are null.
  Duplicate skips precede writes; forced imports append new rows and decisions.
  The store gains exactly one INSERT statement. Latest/history reads join the
  associated decision ID without changing which brief versions are selected.
- **Piece 3 — export:** Every section includes `priorCalls`, carrying its linked
  Brief decisions from `[weekOf - 28 days, weekOf)`, newest first, with expected
  outcomes, scores, notes and review dates. `ListPriorCallsAsync` performs one
  parameterized SELECT-only read per section. CLI output adds each section's
  prior-call count beside the claims count.
- **Piece 4 — claims:** `ListClaimVerdictsAsync` joins decisions to saved citations
  through `OPENJSON` and groups distinct decision/claim pairs. It returns all four
  outcome counts and ordered Wrong decision IDs. Knowledge rows display the verdict
  and flag `Wrong >= 2 && Right == 0` in red. The existing Retire action pre-fills
  `Cited by {n} brief recommendations scored wrong: decisions {ids}` in the existing
  note dialog, then uses the existing `RetireAsync` path after the user submits it.
  Export loads verdicts once and injects them into claim search, which subtracts
  `min(0.15, 0.05 * max(0, Wrong - Right))` before ranking.
- **Piece 5 — window:** One SELECT-only aggregate supplies the exact track-record
  summary and section/book breakdown above the decisions grid. Each decision counts
  once globally and once per cited book, even with several citations from that book.
  This week resolves its joined decision ID against the loaded ledger, displays the
  outcome and, while Pending, the review date through `BdDay`. Load/refresh read the
  track record and verdicts; tab changes do not. Scoring continues through
  `ScoreDecisionAsync` and refreshes decisions, verdicts and brief status.

## Gates authored, not executed

- **Knowledge `DirectorBriefTests`:** Missing/blank/overlong/multiline expected
  outcomes reject before reaching the store, including an actually omitted JSON
  property. Review-offset boundaries and Pacific review dates cross both daylight
  saving transitions. Export round-trips populated and empty prior-call arrays.
  Fake vectors/verdicts prove equal-score reordering, the 0.15 penalty cap, net
  Wrong-minus-Right behavior and no penalty when Right equals or exceeds Wrong.
- **`BdDirectorRegressionTests`:** Migration 356's guards, batches, constraint,
  column/FK/index counts and absence of data statements; exactly three brief-store
  INSERTs plus the existing acknowledgement UPDATE; decision writes within the
  existing transaction after duplicate detection; SELECT-only prior-call, verdict
  and track-record queries, citation JSON paths and duplicate suppression. The
  existing ledger write-boundary gate remains unchanged: three INSERTs, three
  UPDATEs, and scoring changes only its existing outcome fields.
- **Ledger/Knowledge/Market App gates:** Six scored calls (three Right, two Wrong,
  one Unclear) and two overdue Pending calls supply both summary lines. Gates cover
  one read per load/refresh, no tab read handlers, the two-Wrong flag and both reason
  IDs, the one-Wrong/one-Right negative control, no retirement on load, actor/reason
  forwarding, and Pending-to-Right refresh after scoring. Existing date ratchets remain.
- **`BdScreensRenderTests`:** Updated the fixture for `director-asks.png`,
  `director-knowledge.png` and `director-week.png`. Added rendered text/flag/date
  assertions, existing note-dialog prefill inspection, and read-count checks after
  tab selection. The total minimum remains fourteen. No PNG was regenerated here.

## Fixture details and limits

The fixtures are illustrative. SQL source gates do not prove SQL Server syntax,
permissions, query plans, transaction rollback or concurrent-import behavior. Fake
aggregates do not verify actual database counts. Rendering and layout still require
the specified follow-up run. No historical outcomes or eight-week scoring history
were fabricated.

Review dates use 16:00 Pacific on the target calendar day, then convert to UTC.
For the 2026-10-19 week, the default is **2026-11-16 at 16:00 Pacific**, stored as
2026-11-17T00:00:00Z. Forced imports remain independent historical calls and therefore
participate in verdicts and track records. Retirement preserves the existing
200-character reason limit; a prefill exceeding that limit must be edited by the user.

## Files and handoff

Two new files: migration 356 and this response. Twenty-three existing files edited.
Shared section validation and `PriorCall` live in Data to preserve the existing
Knowledge-to-Data dependency; the Knowledge validation entry point delegates to that
shared shape gate. Three necessary supporting edits beyond the listed files are
`BdDirectorPersonRow.cs` for presentation properties, `NoteDialog.xaml.cs` for optional
prefill, and the Pending bids test fixture for the new full-window reads.

Claude owns the affected-project Debug builds for App tests, Knowledge tests and CLI,
the named gates and their pre-fix negative controls, rendering and reading the three
tabs, and local App installation. Ian applies migration 356 in his next SSMS sitting.
Then re-export 2026-10-19, add `expected` to the five authored sections, force-import,
confirm five new Brief decisions with November 16 Pacific review dates, and record
the actual counts in memory. No commit was made.
