# Financials — an invoice's AR balance is the SUM of its phase rows, not the MAX

Bounded brief for Codex. Source edits only; nothing is built, run, applied or deployed by you.
Response goes beside this file as `CODEX-FINANCIALS-AR-BALANCE-IS-PER-PHASE-RESPONSE.md`: one section per site (what
changed, the gate authored and not run, anything in the code that contradicted this brief). `git diff --check` clean.

## The fact, measured 17 Oct against Deltek (read-only)

Deltek `AR` holds one row per (WBS1, WBS2, WBS3, Invoice). Each row's `InvBalanceSourceCurrency` is **that phase's
share** of the invoice. It is not the invoice total repeated.

- 66 of 69 AR rows on multi-phase open invoices equal the `LedgerAR` net of their own (WBS1, WBS2, WBS3, Invoice).
  The other 3 rows total 1,050.
- Invoice 00038658 (31103-01): 84,000.00 on 2.SD and 77,962.50 on 3.DD. LedgerAR `IN` is -84,000.00 and -77,962.50
  on the same phases. The invoice is 161,962.50.
- Across 367 open invoices: SUM per invoice matches the ledger on 358; MAX per invoice matches on 326.
  Firmwide SUM is 2,169,365.16 and MAX is 1,992,683.47, so **MAX understates open AR by 176,681.69**.

## Every reader, classified (all 44 references in 14 `*.cs` files read, 17 Oct)

| Reader | What it does | Verdict |
|---|---|---|
| `ArFinancialsService` firmwide totals (130–143) and project rows (201–224) | SUM per bucket / WBS1 | right |
| `ArFinancialsService` in-collections split (90–100) | sums every matching invoice row | right |
| `ArFinancialsService` invoice detail (293–341) | one row **per phase**, phase not returned | **site 5** |
| `FinancialsService` client rollup (969–976) | SUM per WBS1 | right |
| `FinancialsService.LoadInCollectionsByWbs1Sync` (760–850) | C# HashSet keeps one row per invoice | **site 1** |
| `ProjectAnalyticsService` (133–158) | SUM per WBS1 / period | right |
| `DeltekClientContextService` (637–655) | SUM per bucket | right |
| `DeltekLookupService` (61–66) | EXISTS a positive row | right for its purpose (no amount) |
| `CollectionsRepository.GetOpenArByClientAsync` (120–124) | MAX per invoice | **site 2** |
| `ArAgingRule` (36–44) | MAX per invoice | **site 3** |
| `WrittenOffInvoicePaidRule` (31–37) | MAX per invoice | **site 4** |
| `DeltekSchemaValidator`, `ExecutiveSummaryService` 329, `FinancialsService` 507, metric definitions, `ArTool` text, `ToolDefinitionDriftTests` | column lists, prose, docs | no amount computed; definitions already say SUM |

So today the Executive Summary's collections split and the Clients view's split disagree for any in-case invoice that
spans phases.

## The five sites

1. **`Kor.Operations.App/Financials/FinancialsService.cs`, `LoadInCollectionsByWbs1Sync` (about 760–850).** The `seen`
   HashSet keeps only the first AR row per (WBS1, Invoice). Remove the dedupe so every phase row of an in-case invoice
   adds to the WBS1 total and to Aged90Plus. Rewrite the comment above it (about 795–800) to state the measured fact.
   Extract the per-row accumulation as an `internal static` function over plain row tuples so it can be tested.
   `Kor.Operations.App/AssemblyInfo.cs:14` already grants InternalsVisibleTo to `Kor.Operations.App.Tests`.
2. **`Kor.Operations.Mcp/Collections/CollectionsRepository.cs`, `GetOpenArByClientAsync` (about 108–175).** In
   `ARDeduped`, `MAX(InvBalanceSourceCurrency)` becomes `SUM(InvBalanceSourceCurrency)`. Keep `MAX(InvoiceDate)`.
   `InvoiceAmounts` already SUMs `LedgerAR` across phases, so the balance and the original amount now agree.
3. **`Kor.Operations.Mcp/Alerts/Rules/Cash/ArAgingRule.cs` (about 28–60).** Same change in `ARDeduped`. Rewrite the
   line-33 comment ("one row per WBS sub-phase per invoice — dedupe") to say the rows are phase shares and are
   summed per invoice.
4. **`Kor.Operations.Mcp/Alerts/Rules/Legal/WrittenOffInvoicePaidRule.cs` (all, 5 KB).** Same change in `ARDeduped`,
   and fix the comment near line 23. The rule then fires when the invoice's net balance is ≤ 0, and the credit it
   reports is the invoice's net, not one phase's.

5. **`Kor.Operations.Business/ArFinancialsService.cs`, invoice detail (293–351).** Accumulate `invoiceRows` per
   (WBS1, Invoice): Balance is the sum of the phase rows' `bal * fx`, InvoiceDate and DueDate are the earliest non-null,
   DaysPastDue is recomputed from that anchor, and the other fields come from the first row. Keep the existing sort.
   Do it in C# after the reader, so the SQL and its chunking stay unchanged. Every total, and the in-collections loop at
   90–100, must give the same numbers as before, because summing already-summed rows changes nothing. Consumers:
   `ExecutiveSummaryDeltekLoader.cs:342` (AR invoice drilldown), `ArTool.cs:79` (`topInvoices`), and
   `tools/MorningBrief`. They get one row per invoice and need no change.

Do not change any other reader, the metric definitions, or any number that already SUMs.

## Gates (authored, not run)

1. **The class, not the instances.** In App.Tests (`Financials/`), add a test that walks the repo's `*.cs` (skip
   `bin`, `obj` and test projects) and fails on any `MAX(` whose argument contains `InvBalanceSourceCurrency`, case- and
   alias-insensitive (`MAX(ar.InvBalanceSourceCurrency)` counts). Its summary must say it does not catch a dedupe done in
   C# (as site 1 was) or in a `.sql` file outside the repo. Gate 2 covers site 1.
2. The extracted accumulator from site 1: two phase rows of one in-case invoice (84,000 and 77,962.50, one older than
   90 days) give a Total of 161,962.50 and the right Aged90Plus. A row whose invoice is not in the case set adds
   nothing. A USA bucket applies the FX rate.
3. In `Kor.Operations.Mcp.Tests/Alerts`, a text assertion that the three rules' and the repository's SQL contain
   `SUM(InvBalanceSourceCurrency)` inside `ARDeduped`.
4. Site 5, with the grouping extracted as a pure `public static` function. `Kor.Operations.Business` declares no
   InternalsVisibleTo (searched its `*.cs` and `*.csproj`). Two phase rows of
   00038658 give one row of 161,962.50; a USA row is FX-converted before summing; and the sum over output rows equals the
   sum over input rows.

**None of these reads Deltek.** A wrong join or a wrong grain in the live SQL passes them all. Claude checks after apply
by comparing the in-collections and alert totals against the ledger query that produced the numbers above.

## Read bound (about 45 KB)

| File | Range |
|---|---|
| `Kor.Operations.App/Financials/FinancialsService.cs` | 755–850 |
| `Kor.Operations.Business/ArFinancialsService.cs` | 60–120, 280–355 |
| `Kor.Operations.Mcp/Collections/CollectionsRepository.cs` | 100–180 |
| `Kor.Operations.Mcp/Alerts/Rules/Cash/ArAgingRule.cs` | 1–100 |
| `Kor.Operations.Mcp/Alerts/Rules/Legal/WrittenOffInvoicePaidRule.cs` | all |
| `Kor.Operations.Mcp.Tests/Alerts/` | search `ArAgingRule`, read 40 lines around the hit |
| `Kor.Operations.App/Kor.Transmittals.App.Tests/Financials/` | list the folder; open one test file for conventions |

Do not open Deltek, live databases or APP01. Touch only the five files and the tests.
