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
| `ArFinancialsService` invoice detail (293–341) | one row **per phase**, phase not returned | **site A** |
| `FinancialsService` client rollup (969–976) | SUM per WBS1 | right |
| `ProjectAnalyticsService` (133–158) | SUM per WBS1 / period | right |
| `DeltekClientContextService` (637–655) | SUM per bucket | right |
| `DeltekLookupService` (61–66) | EXISTS a positive row | right for its purpose (no amount) |
| `ArAgingRule` (36–44) | MAX per invoice; collections tables only as a LEFT JOIN exclusion | **site B** |
| `FinancialsService.LoadInCollectionsByWbs1Sync` (760–850) | C# HashSet keeps one row per invoice | wrong, **out of scope** |
| `CollectionsRepository.GetOpenArByClientAsync` (120–124) | MAX per invoice | wrong, **out of scope** |
| `WrittenOffInvoicePaidRule` (31–37) | MAX per invoice | wrong, **out of scope** |
| `DeltekSchemaValidator`, `ExecutiveSummaryService` 329, `FinancialsService` 507, metric definitions, `ArTool` text, `ToolDefinitionDriftTests` | column lists, prose, docs | no amount computed; definitions already say SUM |

**Out of scope: the Collections module.** It is barely built. Measured 17 Oct in KorMcp: `Mcp.CollectionsCase` has 1 row
ever, a test opened 2026-05-09 09:09:30 and resolved 43 seconds later; there are 0 active cases; and
`Mcp.CollectionsCaseInvoice` has 1 row. The last three rows above only produce a number when cases exist. Do not touch them. They are recorded here so the fix is ready when that module
is built out.

## The two sites

A. **`Kor.Operations.Business/ArFinancialsService.cs`, invoice detail (293–351).** Accumulate `invoiceRows` per
   (WBS1, Invoice):
   - Balance is the sum of the phase rows' `bal * fx`.
   - InvoiceDate and DueDate are the earliest non-null, and DaysPastDue is recomputed from that anchor.
   - The other fields come from the first row. Keep the existing sort.

   Do it in C# after the reader, so the SQL and its chunking stay unchanged. Every total, and the in-collections loop
   at 90–100, must give the same numbers as before, because summing already-summed rows changes nothing. Extract the
   grouping as a pure `public static` function; `Kor.Operations.Business` declares no InternalsVisibleTo (searched its
   `*.cs` and `*.csproj`). Consumers:
   - `ExecutiveSummaryDeltekLoader.cs:342`, the AR invoice drilldown
   - `ArTool.cs:79`, `topInvoices`
   - `tools/MorningBrief`

   All three get one row per invoice and need no change.
B. **`Kor.Operations.Mcp/Alerts/Rules/Cash/ArAgingRule.cs` (about 28–60).** In `ARDeduped`,
   `MAX(InvBalanceSourceCurrency)` becomes `SUM(InvBalanceSourceCurrency)`. Keep `MAX(ClientID)` and
   `MAX(InvoiceDate)`. Rewrite the line-33 comment ("one row per WBS sub-phase per invoice — dedupe") to say the rows
   are phase shares and are summed per invoice. Leave the collections LEFT JOINs as they are.

Do not change any other reader, the metric definitions, or any number that already SUMs.

## Gates (authored, not run)

1. **The class, not the instances.** In App.Tests (`Financials/`), add a test that walks the repo's `*.cs` (skip
   `bin`, `obj` and test projects) and fails on any `MAX(` whose argument contains `InvBalanceSourceCurrency`. Make it
   case- and alias-insensitive, so `MAX(ar.InvBalanceSourceCurrency)` counts. It allow-lists exactly two files by
   path, `CollectionsRepository.cs` and `WrittenOffInvoicePaidRule.cs`, with the comment "Collections module, out of
   scope 2026-10-17; remove from this list when it is built out". It also asserts that both files still exist, so the
   allow-list cannot outlive them. Its summary must say it does not catch:
   - a dedupe done in C#, which is how `LoadInCollectionsByWbs1Sync` does it (out of scope, untested)
   - a dedupe in a `.sql` file outside the repo
2. Site A grouping: two phase rows of 00038658 give one row of 161,962.50; a USA row is FX-converted before summing;
   the sum over output rows equals the sum over input rows; the earliest date is kept.
3. Site B: in `Kor.Operations.Mcp.Tests/Alerts`, a text assertion that `ArAgingRule`'s SQL contains
   `SUM(InvBalanceSourceCurrency)` inside `ARDeduped` and no `MAX(InvBalanceSourceCurrency)`.

**None of these reads Deltek.** A wrong join or a wrong grain in the live SQL passes them all. After apply, Claude
compares the AR drilldown's sum, and the alert's per-client totals, against the ledger query behind the numbers above.

## Read bound (about 25 KB)

| File | Range |
|---|---|
| `Kor.Operations.Business/ArFinancialsService.cs` | 60–120, 280–355 |
| `Kor.Operations.Mcp/Alerts/Rules/Cash/ArAgingRule.cs` | 1–120 |
| `Kor.Operations.Mcp.Tests/Alerts/` | search `ArAgingRule`, read 40 lines around the hit |
| `Kor.Operations.App/Kor.Transmittals.App.Tests/Financials/` | list the folder; open one test file for conventions |

Do not open Deltek, live databases or APP01. Touch only the two files and the tests.
