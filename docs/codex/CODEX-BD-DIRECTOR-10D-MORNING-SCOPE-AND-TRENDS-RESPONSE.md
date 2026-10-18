# BD Director 10d — response

Source edits completed in `tools/MorningBrief/MorningFacts.cs` and `MorningBriefTests.cs`. All gates below are **authored, not run**. No build, application, tests, database access, environment-value lookup, publish, or deployment was performed. Existing unrelated workspace changes were left untouched.

## Part 1 — each figure states its scope

KPI names now append the label derived directly from `kpi.Scope`: `firmwide`, `all active projects (N)`, or `scope not stated by the engine`. Alert names append the last label because `ExecutiveAlert` has no scope field. No title lookup is used.

The coverage fact supplies the exact section scope `Executive Summary engine; project-scoped figures cover all active projects (N)`. `MorningPromptComposer` already renders `SectionScope`, so it needed no edit. Existing 10c assertions were updated for the new header, fact names, and coverage fields.

Gate authored, not run: `Executive_scope_labels_follow_fields_and_the_header_describes_mixed_scopes` gives AR Outstanding a Scoped value, Backlog a Firmwide value, and Utilization a None value. It checks all three labels, the alert label, and the exact section-scope/header text. Its summary explicitly excludes live engine-scope correctness, store connections, table contents, and screen sparklines.

No code contradiction was found: KPI and trend records expose Scope; alerts do not. The Financials engine and its WIP visibility were not changed.

## Part 2 — print trends and use the configured portfolio store

Added `OptionalConnection`, reading the machine environment first and the process environment second, returning null when neither supplies a value. The existing required `Connection` lookup delegates to it and retains its missing-value exception for existing callers.

`GatherCooAsync` uses the optional lookup for `KOR_FILESYNC_KORTRANSMITTALSDB`. A resolved value is passed directly into `SqlFinancialPortfolioSnapshotStore`, and `existingTrend: null` lets the engine load its history. An unset variable selects the placeholder store and empty trend array, passing `Portfolio trend not read: KOR_FILESYNC_KORTRANSMITTALSDB unset.` into `ExecutiveFacts`. No connection value is added to output.

Confirmed by source inspection: the engine loads snapshots from twelve weeks ago only when its trend argument is null, and `TrendLoaded` is false for an empty array. The fallback therefore avoids a store read and surfaces the unset-variable note. A failed or empty real store instead surfaces `Portfolio trend unavailable.` through the engine's flag. No code contradiction was found.

`ExecutiveFacts` now emits coverage, KPIs, trends, then alerts, preserving engine order within each group. Coverage includes TrendLoaded and MaxPostedPeriod (`yyyy-MM` or `unknown`), and counts three source-load flags. Each trend uses its own Scope, preserves its value/status text, and prints non-null series in the supplied order with `N0` values and the explicit warning that period labels are not carried by the engine. Drilldowns take the first ten payer rows, otherwise delivery-risk rows, retaining the full denominator. Payer rows use the requested amount/earned/invoiced/AR format; delivery-risk rows reuse the existing formatter. Data-unavailable trends expose their status as `CouldNotRead`.

Gates authored, not run:

- `Executive_trends_print_series_drilldowns_and_unavailable_coverage_in_engine_order` covers the 12-payer-row fixture with `10 of 12`, series formatting/order, payer-row precedence and formatting, delivery-risk rows/count series, unavailable reasons, supplied and default coverage notes, posted-period formatting, and group order.
- `Coo_trend_store_uses_optional_connection_with_an_empty_fallback` checks the configured and fallback call paths as source text, including lookup precedence and the absence of the throwing lookup for this variable. It does not read environment values.

Both summaries exclude actual database access, connection/table validation, and screen-sparkline parity. Live confirmation of twelve Delivery Risk points and matching Revenue/Billings headlines remains for the post-apply check described in the brief.

Whitespace validation: `git diff --check` is clean. The new response file also passed `git diff --no-index --check` against NUL.
