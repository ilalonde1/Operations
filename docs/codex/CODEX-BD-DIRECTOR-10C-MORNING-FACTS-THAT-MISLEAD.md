# BD Director 10c — morning facts that mislead

Bounded brief for Codex. Source edits only; nothing is built, run, applied or deployed by you.
Response goes beside this file as `CODEX-BD-DIRECTOR-10C-MORNING-FACTS-THAT-MISLEAD-RESPONSE.md`: plain markdown,
one section per fix (what changed, the gate authored and not executed, anything in the code that contradicted this
brief). Do not open earlier responses. `git diff --check` clean.

## Why this exists

10b's composer (`tools/MorningBrief`) was run live on 16 Oct for 19 Oct. Every source read, and the prompt is honest
about coverage, but five facts would lead the morning session to wrong advice. The values below are what the live run
printed; the fixes are to the composer only. **Rule 14 still holds: no model call, no outside service.**

## The five fixes, all in `tools/MorningBrief/MorningFacts.cs` unless named

1. **WIP counts non-project codes.** Firm unbilled WIP read 3,202,465 of which 2,938,252.65 was `XICRV-CA Intercompany
   Revenue CAD` and 239,106.89 `9OHVA-00 Overhead Variance`; both topped "Top unbilled WIP projects". Exclude rows that
   are not regular client projects from the top list AND recompute the firm line from the remaining rows (state it as
   "client projects, X of Y WIP rows"). Find how the codebase already tells a regular project from overhead/intercompany
   (search `ChargeType` and the WIP/AR services; Deltek `PR.ChargeType` is `R` for regular) and reuse it; if the WIP row
   carries no such field, add it to the WIP row from the query that already builds it, defaulting existing callers to
   unchanged behaviour. Do not invent a code-prefix rule unless no field exists — and if so, say so in the response.
2. **Over-budget is ranked by ratio.** Top entry was `60054-22`: 109.5 hours / 3.14 budget = 34.92×, while `30862-CA
   Union & Ash` at 2,162 hours over sat fifth; "253 of 9960 projects qualify" counts every project ever. Restrict to
   active projects (find the status field the project rows already carry or the at-risk tool uses), rank by overage hours
   descending, keep the 1.35 factor as the entry condition, and print both overage and ratio. Header says "X of Y active
   projects".
3. **Partial timesheet weeks read as complete.** Measured in Deltek on 16 Oct (`tkDetail`, Sunday weeks, hours / distinct
   people): 08-30 927.5/24, 09-06 952.5/24, 09-13 987.0/24, 09-20 928.0/24, 09-27 944.5/24, **10-04 632.5/19, 10-11
   292.5/11**. The composer reported 10-11 as firm utilization and took its 11 people as "last week". A week is complete
   when its distinct-people count reaches the median of the prior eight weeks (inclusive of ties). Firm utilization: the
   last four COMPLETE weeks; list any newer partial week on one line as "partial: N of M people, not used". People
   outliers: from the last complete week, and say which week that is. Do the counting in the composer from rows it
   already loads where possible (`EmployeeAnalyticsService.LoadEmployeeWeeklyUtilizationSync` returns per-person weeks).
4. **Overdue invoices.** Lines printed `(due )` — the due date is empty for every row; and one invoice appears as several
   lines (00038658 twice, 00039126 twice). Group by invoice number (sum balances, keep the oldest days past due, keep
   project/client/PM), rank groups by balance, and omit the due-date clause when the source has none (find why it is
   empty: if the AR row never carries a due date, drop the clause rather than adding a query).
5. **BD stale list ranks by fee nobody records.** 0 of 110 live pursuits have a proposed fee, so "top 10 by proposed fee"
   is arbitrary. Rank stale pursuits by days since last touch (oldest first), fee as tie-break only, and print
   `N days quiet` and the date only (`yyyy-MM-dd`), not a full timestamp. Where the title is empty, use the organisation
   name the pursuit row can reach; if none, `(untitled)`. Same date-only format for action due dates.

## Gates (authored in `MorningBriefTests`, not run)

Fixture tests on the pure functions (extract them as `public static` where needed, as `PursuitFacts` and `EmployeeFacts`
already are): (1) WIP top list and firm line exclude non-regular rows, with X of Y; (2) over-budget ranks by overage among
active rows only; (3) with the seven week counts above as fixture, 10-04 and 10-11 are partial, the four complete weeks are
09-06..09-27 and outliers come from 09-27; (4) duplicate invoice lines group into one with summed balance, no `(due )`;
(5) stale pursuits order by quiet days, date-only output, empty title falls back.
**Each gate's summary says what it does not cover:** none of them reads Deltek, so a wrong table or field choice in the
SQL passes; Claude checks that by running the composer live after apply.

## Read bound

About 40 KB, measured. Read the ranges, search (`rg -n`) and read 40 lines around a hit for anything else; never open a
file over 30 KB whole.

| File | Lines | For |
|---|---|---|
| `tools/MorningBrief/MorningFacts.cs` | 33–192 | every fix |
| `tools/MorningBrief/MorningPromptComposer.cs` | all (6 KB) | how facts print |
| `Kor.Operations.App/Kor.Transmittals.App.Tests/BusinessDevelopment/MorningBriefTests.cs` | search `PursuitFacts`, `EmployeeFacts` | where gates go |
| `Kor.Operations.Business/WipFinancialsService.cs` | search `WipProjectRow`, `ChargeType` | fix 1 |
| `Kor.Operations.Business/ArFinancialsService.cs` | search `DueDate`, `InvoiceRow` | fix 4 |
| `Kor.Operations.Business/Analytics/ProjectAnalyticsService.cs` | search `Status`, `LoadProjectRowsSync` | fix 2 |
| `Kor.Operations.Mcp/Tools/AtRiskProjectsTool.cs` | search `Status` | fix 2 |
| `Kor.Operations.Business/Analytics/EmployeeAnalyticsService.cs` | search `EmployeeWeeklyHours` | fix 3 |

Do not open the live databases, Deltek or APP01. Do not touch anything outside the composer, its tests, and the minimal
row-field additions fixes 1 and 2 may need.
