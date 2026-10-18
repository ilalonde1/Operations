# BD Director 10c — response

Source edits completed. The five gates in `MorningBriefTests.cs` are **authored, not executed**. No build, application, test, SQL, model call, outside service, publish, or deployment was run. No earlier response was opened. The prerequisite AR grouping implementation is present in the tree. Existing unrelated workspace changes were left untouched.

## Part 1 — MorningBrief references the App

`MorningBrief.csproj` now targets `net8.0-windows10.0.19041.0`, enables WPF, restores the `System.IO` implicit using, and references `Kor.Operations.App`, following `BdBriefSmoke`. Existing references remain.

Gate authored, not executed: `Coo_source_uses_the_executive_engine_and_references_the_App` checks those project settings and the absence of a MorningBrief reference in the App project. This is a source gate, not a build or constructor/configuration check. No contradictory reference was found.

## Part 2 — COO section consumes the Executive Summary

`GatherCooAsync` now loads one `FinancialsService` snapshot with `watchlistOnly: false`, then supplies that snapshot, its utilization rows, and an empty trend array to `ExecutiveSummaryService`. The former direct AR, WIP, firm-utilization, and project-analytics reads and composer-defined financial calculations are removed. No project filters were added. Employee analytics and accepted Operations cards remain independent reads.

Confirmed in source: `ExecutiveSummaryService.GetExecutiveSummaryAsync` calls `LoadSnapshotsAsync` only inside `if (trend == null)`. The supplied empty array is non-null, so this path does not call the trend store. `SqlFinancialPortfolioSnapshotStore` also stores the explicitly supplied placeholder string without resolving configuration in its constructor. No collections provider is supplied and no collections text is added.

The pure `ExecutiveFacts` formatter emits each KPI's title, value, inline text, status, and up to ten drilldown rows, plus each alert's title, message, and drilldown. It preserves engine order and the selected list's full count. Money uses `N0`; dates use `yyyy-MM-dd`. Coverage includes SnapshotLoaded, DeltekLoaded, loader failures, and schema drift. The COO section header receives `all active projects (N)` from `snap.Rows.Count` via the coverage fact's section-scope metadata.

Code details that qualify the brief: some engine results carry multiple non-null drilldown lists, rather than one. The formatter selects invoice detail before project AR and cash accounts before cash history, including explicitly empty lists. The synthetic gate covers both preferences and all eleven current row types. Also, `ExecutiveAlert` has Title and Message, not the KPI's ValueText/InlineSubText/StatusMessage fields; those existing alert fields are printed directly.

Gates authored, not executed:

- `Coo_source_uses_the_executive_engine_and_references_the_App` rejects the four direct service constructions and `ChargeType`, and checks the requested snapshot/engine arguments.
- `Executive_facts_preserve_every_KPI_alert_order_count_and_unavailable_reason` covers every current KPI/alert row shape, both ordered top-ten invoice lists with `10 of 12` coverage, the COO scope header, unavailable reasons, and coverage messages. The engine Utilization KPI receives no partial-week annotation.

These gates do not establish live scope, constructor/configuration behavior, or financial correctness; the live Executive Summary comparison remains outstanding.

## Part 3 — invoice presentation and partial timesheet weeks

Invoice rows are printed exactly at the grain supplied by the engine, without grouping, sorting, filtering, or recomputing balances or ages. No due-date clause is printed. A missing DueDate labels the supplied age as `days since invoice`; an actual DueDate retains `days past due` wording.

Added pure `EmployeeWeekFacts` presentation logic. It counts distinct employee IDs per Sunday week through the current week, compares each count against the median of up to eight preceding observed weeks, and treats ties as complete. It emits one completeness fact with partial-week lines and calls `EmployeeFacts` for the last complete week, naming that week. A week without a prior baseline is marked unknown; unavailable history does not produce invented outliers. This logic does not alter the engine's Utilization KPI.

Gates authored, not executed:

- `Executive_invoice_rows_keep_the_engine_grain_and_label_invoice_age` preserves two supplied rows sharing an invoice number on different projects and checks date-only invoice text, no due-date clause, and `days since invoice`.
- `Employee_partial_weeks_are_named_and_outliers_use_September_27` uses the seven specified counts and hours. It expects `partial: week 2026-10-04, 19 of 24 people`, `partial: week 2026-10-11, 11 of 24 people`, and outliers from 2026-09-27. It also covers case-insensitive distinct IDs and empty input.

No contradiction to the prerequisite invoice grouping was found. These presentation gates do not validate live invoice grain, timesheets, snapshot scope, configuration, or the engine's amounts and utilization ratio.

## Part 4 — BD fixes from the parked run

Read `git diff 'stash@{0}^1' 'stash@{0}' -- tools/MorningBrief/MorningFacts.cs` and manually reused only the named BD changes: pursuit SQL and organisation-name fallback through CanonicalOrg, `Pursuit`, `PursuitFacts`, `QuietDays`, `Describe`, and `ActionFacts` with its call site. The stash was not popped, applied, dropped, or otherwise changed.

Stale pursuits rank by whole days quiet, then proposed fee, then ID. Their text includes `N days quiet` and date-only last-touch text. Empty titles fall back to the organisation, then `(untitled)`. Open-action due dates also use date-only formatting.

Gate authored, not executed: `Pursuits_rank_by_days_quiet_and_BD_dates_and_title_fallbacks_are_plain` checks age ranking ahead of fee, fee tie-breaks, both title fallbacks, date-only pursuit/action output, and closed-action exclusion. The existing exact-fourteen-day/top-ten denominator gate remains. The new gate's summary excludes live SQL joins, scope, constructor/configuration behavior, and engine correctness. No contradiction was found in the selected BD hunks.

Whitespace validation: `git diff --check` is clean. The new response file also passed `git diff --no-index --check` against NUL. Live composer/Executive Summary comparison remains for the post-apply check described in the brief.
