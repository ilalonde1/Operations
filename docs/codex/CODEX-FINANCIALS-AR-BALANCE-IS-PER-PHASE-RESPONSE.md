# Financials AR phase balances — response

Source edits completed. All gates below are **authored, not run**. No build, application or SQL execution, database access, publish, or deployment was performed. Existing unrelated workspace changes were left untouched.

## Site 1 — FinancialsService collections split

- Removed the `(WBS1, Invoice)` HashSet dedupe. Every matching phase now contributes to Total and, according to its own DueDate/InvoiceDate anchor, Aged90Plus. The comment records that AR balances are phase shares.
- Extracted `internal static AccumulateInCollectionsByWbs1` over plain row tuples; the reader supplies those tuples. Case membership, blank-key exclusion, USA FX, and the strict `> 90` aging threshold are retained.
- Gate authored, not run: `Kor.Operations.App/Kor.Transmittals.App.Tests/Financials/InCollectionsPhaseBalanceTests.cs` covers 84,000 + 77,962.50 = 161,962.50, phase-level aging, exclusion of unmatched invoice/project keys, USA FX, the 90-day boundary, credits, missing dates, and an empty case set.
- No code contradiction found at this site.

## Site 2 — CollectionsRepository open AR

- Changed `ARDeduped` to `SUM(InvBalanceSourceCurrency)` and retained `MAX(InvoiceDate)`.
- Gates authored, not run: `Kor.Operations.Mcp.Tests/Alerts/ArPhaseBalanceSqlTests.cs` checks the SUM and retained date MAX inside this invoice-grouped CTE. `Kor.Operations.App/Kor.Transmittals.App.Tests/Financials/ArBalanceAggregationSourceTests.cs` walks repository C# source, excluding bin, obj, and test projects, rejecting MAX arguments containing the balance column regardless of case or alias, including nested expressions. Its summary explicitly excludes C# deduplication and external `.sql` files from its coverage.
- **Contradiction requiring follow-up:** `InvoiceAmounts` does SUM LedgerAR, but first joins AR on WBS1, Invoice, and ClientID without phase keys. Multiple matching AR rows can therefore multiply each LedgerAR amount before aggregation. The brief's claim that original amount and balance now agree is not assured by this change. That join was left unchanged within the specified scope; these text gates cannot validate its live cardinality.

## Site 3 — ArAgingRule

- Changed the balance aggregate in `ARDeduped` to SUM; retained the date MAX, thresholds, and subsequent rollups. Updated the comment to explain phase shares.
- Gates authored, not run: the MCP CTE assertion and repository-wide MAX guard described above both cover this site.
- Brief wording discrepancy: gate 3 says "three rules' and the repository's SQL," but the named sites and source contain **two rules and one repository**, three CTEs total. The text gate covers all three named sites.

## Site 4 — WrittenOffInvoicePaidRule

- Changed the balance aggregate in `ARDeduped` to SUM and corrected the duplicate-row comment. The existing non-null, net-balance `<= 0` predicate and credit reporting now use the summed invoice balance; date MAX is retained.
- Gates authored, not run: the MCP CTE assertion and repository-wide MAX guard cover this site.
- No additional code contradiction found at this site.

## Site 5 — ArFinancialsService invoice detail

- Added pure `public static AggregateInvoicePhaseRows`, invoked after all detail-reader chunks. It applies each phase's USA FX before summing by `(WBS1, Invoice)`, takes the earliest non-null InvoiceDate and DueDate, recomputes DaysPastDue from DueDate falling back to InvoiceDate, and keeps the first row's remaining fields.
- Preserved the detail sort, SQL, chunking, project/firmwide aggregate calculations, and collections loop. Consolidation preserves the CAD-equivalent balances consumed by that loop.
- Gate authored, not run: `Kor.Operations.App/Kor.Transmittals.App.Tests/Financials/ArInvoicePhaseGroupingTests.cs` covers the 161,962.50 example, earliest dates, first-row identity, per-phase USA FX, both grouping keys, total and collections-subset conservation, credits, null dates, future-date aging, and empty input.
- No code contradiction found at this site. FX moved from reader-row construction into the pure grouping function so the authored FX gate exercises the production conversion path.

Whitespace validation: `git diff --check` is clean; the five new files also passed `git diff --no-index --check` against NUL. Live reconciliation against Deltek remains for the post-apply check described in the brief.
