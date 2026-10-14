# Codex brief — recompute the CAD vs Revit report from the raw pulls, then try to break it

**READ-ONLY. DO NOT BUILD. DO NOT RUN TESTS. DO NOT RUN ANY TOOL AGAINST A DATABASE. NO GIT.**
Open only the files named under "The bound". Load the CSVs with Python; never `cat`, `head`, print
or open them in the editor (the per-job file is 2,328 rows). Do not grep, list or search the
repository. Reasoning medium. One response file, 150 lines at most.

## Your job

A report shipped today that says, on one page, how much CAD drafting the firm still does and whether
CAD jobs make money against Revit jobs. Two managers were about to answer the same question by counting
`.dwg` and `.rvt` files in job folders. It is going in front of readers who, at the first number they
can call wrong, stop reading and never trust the app again.

**Compute the answer yourself from the raw data, then try to make the page wrong.** Not review it:
falsify it. A number that is quietly out by five points and looks plausible is worse than a crash,
because it reaches a partners' meeting. Rank every finding by whether it can put a wrong number or an
unsupported sentence in front of a reader with nothing on the page saying so.

## What was built

| file | what it is |
|---|---|
| `tools/build_cad_vs_revit_report.py` | the generator: loads ten CSVs, computes every table and every sentence, writes the HTML page and the workbook. No number on the page is typed by hand. |
| `db/KorTransmittals/008_ProjectDraftingProfitabilitySnapshot.sql` | the same measurement as SQL: two snapshot tables, a view with the derived figures, a proc that fills them from Deltek. It has been run; its counts matched the CSVs (2,328 jobs, 1,316 residential). |
| `docs/KOR-Financials-CAD-vs-Revit-2026-10-14.html` | the page as shipped (the PDF is this file printed). |
| `docs/KOR-Financials-CAD-vs-Revit-2026-10-14-data/*.csv` | the ten raw pulls the generator read. Column meanings are in the generator's docstring and loaders. |

## Measured on 2026-10-14 — use these as given, do not re-derive

| Fact | Value |
|---|---|
| Hours | `tkDetail`; Engineering = LaborCode 10 and 30, Drafting = 20; lines with `LineItemApprovalStatus = 'R'` excluded |
| Billable hours | LaborCode not 70 or 80, on job numbers not matching `[A-Z]%`, `9[A-Z]%`, `99%` |
| Billed | `PRSummaryMain`: `BilledFee` when non-zero, else `Revenue`; posted through October 2026 |
| Population | top-level regular projects (`ChargeType='R'`, WBS2 and WBS3 blank) with engineering or drafting hours: 2,328 jobs, 1,316 residential |
| Drafting type | `ProjectCustomTabFields.CustDraftingType`: RV, CAD, General, blank. 1,281 of 2,328 jobs carry one |
| Sector | `PR.ProjectType`: Res, Com, Hotel, Off, Ind-Garage, Inst-Rec-C, R-S-R, Small Job |
| Posted cost | `RegAmt + OvtAmt + SpecialOvtAmt` per timesheet line, the rate in effect at the time, in the project's currency |
| Partners | employee ids starting with `P`; their posted amount is a nominal payroll figure (about $55/hr); the report re-prices their hours at $250, the app's `PartnerImputedCostRate` |
| Currency | `Org = 'USA'`: fee, billings and posted amounts × 1.36; current-rate cost is CAD already |
| Completed | fee > 0 and billed ≥ 90% of fee |
| Cohort | year of the job's first timesheet; Deltek timesheet history starts in 2019, so 2019 means "2019 or earlier" |
| Already checked | the method reproduces the app's Historical screen cost and margin to the dollar on six projects; two independent pulls of drafting hours agree to the hour on all 31 year/type cells; the population reconciles exactly to the screen's Has Hours + Fee view (1,152 projects, $99,123,900; 1,146 in the report, 6 hourly-only); 4,037 of 457,854 billable hours carry no posted amount, all 2019–2021 |

## 1. Recompute the page with your own code

From the definitions in `008`'s view (`LaborCost`, `DraftCost`, `IsCompleted`, `MarginPct`) and the
table above, write your own script over the CSVs. Do not import, call or copy the generator. Produce
each figure below and set it beside the report's: **CLAIM | REPORT | YOURS | MATCH or DIFF**. A
difference inside the displayed rounding is MATCH.

| # | Claim | Report says |
|---|---|---|
| 1 | CAD share of firm drafting hours, CAD ÷ (CAD + Revit), 2019 and 2026 to date; residential 2026 | 54%, 23%; 24% |
| 2 | Residential jobs with hours: CAD share of jobs and of billings, CAD ÷ (CAD + Revit); median fee CAD and Revit | 63%, 39%; $50K, $175K |
| 3 | Residential completed, report basis: jobs, fee-weighted margin, median job margin, CAD and Revit | 162, 23%, 10%; 89, 25%, 33% |
| 4 | Same, by cohort: started 2019 or earlier CAD and Revit; started 2020 on CAD and Revit, with job counts | 31%, 37%; −14% on 44, 13% on 39 |
| 5 | Completed, fee $100K–$300K, all cohorts: margin and billed per production hour, CAD and Revit | 38% at $220; 33% at $159 |
| 6 | Calendar 2020–2025, residential: billed, labour cost, margin, years below zero, CAD and Revit; 2025 alone | $9.4M vs $11.6M, −23%, 5 of 6; $21.6M vs $19.9M, 8%, 2 of 6; −24%, 8% |
| 7 | Partner hours at $250 as a share of all labour cost; completed residential margins with partners at posted payroll instead | 75%; 70% CAD, 63% Revit |
| 8 | Hero: jobs with production hours; tagged; untagged residential billings | 2,328; 1,281; $3.8M |

For any DIFF, say which side is wrong and the line in the generator or the definition in the view that
causes it.

## 2. The specific attacks I want tried

**On the generator** (a number on the page is wrong = Critical; right by accident or a sentence that
overreaches = Material; else Minor). Cite `file:line`, give the input that produces the wrong output
and the corrected expression.

- Every `table_html(...)` call: headers against row columns, in order and count. The workbook's
  `job_headers` against `job_row(...)`, and the `money_cols` / `pct_cols` indices.
- `apply_hist`: the re-pricing of partner hours in `cost_all` and, separately, in `draft_cost`.
- Cohort banding and `first_year == 0` rows; the "to date" label; the "below zero in N of M years"
  count and its denominator.
- `summ()` on an empty set, `median()` over `None`s, every share with a zero denominator.
- `money()` and `pct()` rounding where a sentence compares two rounded values.
- Any sentence in the overview or the cards computed from a different population than the table it
  sits beside.

**On 008**, anything that would make the SQL snapshot disagree with the CSV report: `INSERT` column
lists against `CREATE TABLE`, both tables; the partner re-pricing arithmetic
(`… − PartnerHrs × 250 + PartnerHrs × @PartnerImputedCostRate`) for all-staff, active-staff,
engineering and drafting; where `@UsdToCadRate` is and is not applied; the view's `LaborCost`,
`DraftCost`, `IsCompleted`, `MarginPct` against the generator's `apply_hist` and `derive`; OPENQUERY
quoting (every `'` inside is `''`); the `UNION` key join and `JobsWithHours` after summing across the
currency split.

**On the page**, as a hostile reader: the overview ("The short version"), the three cards, every
`<p class="lead">`, every `.note`, both boxes at the end. For each sentence a reader could call wrong,
unsupported by the table it sits with, or readable two ways: quote it, say what is wrong, give the
sentence that is right.

## Two claims to check rather than trust

1. **"Closed jobs that never billed 90% of their fee are excluded too; including them changes no
   comparison by more than two points."** Test it on the CSVs: residential jobs with Status D or I,
   regardless of billing, CAD against Revit, report basis.
2. **"The level figure is the old book."** Is the 23% / 25% headline carried by the 2019-or-earlier
   cohort, and does the page say so in a way that cannot be misread as "CAD is fine"?

## What a finding looks like

State the input, the wrong output, and what should have happened. If you cannot construct an input
that produces a wrong number or an unsupported sentence, say the check is clean rather than listing
style opinions; a long list of nits buries the one real defect. Say which of these you could **not**
determine from the files alone, and what would settle it.

Then, at most five lines: what would make the page simpler without losing a number. No restyling, no
new sections.

## The bound — files you will read (about 108 KB; the CSVs are processed, not read)

```
tools/build_cad_vs_revit_report.py                                  46 KB
db/KorTransmittals/008_ProjectDraftingProfitabilitySnapshot.sql       26 KB
docs/KOR-Financials-CAD-vs-Revit-2026-10-14.html                     36 KB
docs/KOR-Financials-CAD-vs-Revit-2026-10-14-data/jobs_all.csv        626 KB  (Python only)
docs/KOR-Financials-CAD-vs-Revit-2026-10-14-data/jobs_histcost.csv   168 KB  (Python only)
docs/KOR-Financials-CAD-vs-Revit-2026-10-14-data/*.csv (eight more)   45 KB  (Python only)
```

## Files you will create

```
docs/codex/CODEX-CAD-VS-REVIT-SOBER-SECOND-THOUGHT-RESPONSE.md
```

In this order, nothing else: the verdict table for section 1; defects (Critical, Material, Minor) with
`file:line` and the fix; the sentences, quoted, with replacements; the two claims; the five simpler
lines; **exactly one ship-blocker**, or the word "none"; and your recomputation script as an appendix
under a heading, so it can be re-run.
