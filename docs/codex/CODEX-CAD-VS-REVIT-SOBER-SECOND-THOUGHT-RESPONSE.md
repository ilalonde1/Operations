| CLAIM | REPORT | YOURS (CAD, then Revit where paired) | VERDICT |
|---|---|---|---|
| 1. Drafting-hour share, CAD/(CAD+Revit) | Firm 2019 54%, 2026 23%; residential 2026 24% | 54.313%, 22.857%; 23.920% | MATCH |
| 2. Residential volume and median fee | Jobs 63%, billed 39%; $50K/$175K | 62.593%, 38.941%; $50,000/$174,750 | MATCH |
| 3. Completed residential: count, aggregate margin, median margin | 162, 23%, 10%; 89, 25%, 33% | 162, 23.142%, 10.476%; 89, 25.478%, 33.437% | MATCH numbers; “fee-weighted” is wrong—see M4 |
| 4. Completed cohorts | Old 31%/37%; recent −14% on 44 / 13% on 39 | Old 30.819% on 118 / 37.378% on 50; recent −14.151% on 44 / 12.662% on 39 | MATCH |
| 5. Completed $100K–<$300K | 38% at $220 / 33% at $159 | 37.860% at $219.93 / 32.802% at $159.42 | MATCH |
| 6. Residential calendar 2020–2025; 2025 alone | CAD $9.4M/$11.6M, −23%, 5/6; RV $21.6M/$19.9M, 8%, 2/6; −24%/8% | CAD billed $9,411,625.36, cost $11,618,443.38, −23.448%, 5/6; RV $21,641,842.05, $19,935,297.97, 7.885%, 2/6; 2025 −24.376%/8.388% | MATCH |
| 7. Partner policy share; completed payroll-only margins | 75%; 70%/63% | 75.468%; 70.376%/63.083% | MATCH |
| 8. Hero and coverage | 2,328 jobs; 1,281 tagged; untagged residential $3.8M | 2,328; 1,281; $3,832,194.37 | MATCH |

## Defects, in consequence order within severity

References: G = tools/build_cad_vs_revit_report.py; S = db/KorTransmittals/008_ProjectDraftingProfitabilitySnapshot.sql; H = docs/KOR-Financials-CAD-vs-Revit-2026-10-14.html.
**Critical (the brief's wrong-number rule; small numerical impact): C1.** H:175 prints **457,850**, versus the supplied authoritative **457,854** billable hours. G:471/552 sums the coverage CSV buckets; all 16 buckets have integral hour values, so lost export precision is plausible, not proven. Correct input/expression: retain unrounded source hours and round only sum(exact_hours); do not hard-code the supplied total. Attribution between the CSV export and the supplied measurement cannot be settled from these files.
**Material M1 — the old-book profitability inference lacks matching history.** S:200 sums all billing periods; S:222/230 uses available timesheets, which begin in 2019. G:102/181/492 turns that into an apparent lifetime margin. Counterexample: a pre-2019 job with lifetime bills 100, labour 80 before 2019 and 10 after displays 90% instead of 10%. The banked pre-2019 pull contains $7.748M CAD / $5.918M Revit billings, but no corresponding pre-2019 labour. Correct expression: (billings_in_cost_window − costs_in_same_window)/billings_in_cost_window, or acquire full-history costs. Existing files cannot calculate corrected lifetime margins; the page discloses the calendar boundary, not this bias in the completion headline.
**Material M2 — “no comparison … more than two points” is false.** G:551; H:174. Adding Status D/I jobs to the completion-qualified population moves recent CAD −14.151%→−2.108% (12.043 points), recent Revit 12.662%→17.527% (4.865), and the overall Revit median 33.437%→39.757% (6.319). Fix: compute each stated sensitivity on completed OR status IN ('D','I'), and limit the two-point statement to the two aggregate all-cohort margins.
**Material M3 — workbook annual detail silently changes cost basis.** G:723–726 uses current-rate CostAll; HTML G:305–308 uses posted non-partner cost plus partner hours×250. Input: residential CAD 2020 produces workbook cost $3,154,156 and margin −60.689%, versus report $2,844,319 and −44.904%. Correct annual expression: cost_all = year_cost(*k), or explicitly label the sheet's current-rate basis. G:134/643 also leaves “Eng cost” at posted payroll while “Draft cost” includes the partner policy; label engineering “posted payroll” or obtain partner-engineering inputs before repricing it.
**Material M4 — denominators and policy sensitivity are misdescribed.** G:490–492/480–481; H:145–147/172. Actual CAD shares among *all* firm drafting hours are 48.389% in 2019 and 20.518% in 2026, not 54%/23%; among *all* residential jobs/billings, 25.684%/34.436%, not 63%/39%. The printed figures correctly exclude General/untagged, but the overview omits that restriction. Sum(billed−cost)/sum(billed) is billings-weighted; genuine fee weighting gives 24.101% CAD / 25.307% Revit. Partner repricing reduces completed margins by **47.234 / 37.605 points**, reversing the payroll-basis ranking; “about the same amount” hides a 9.629-point differential. Fix labels and compute the sensitivity explicitly.
**Material M5 — fee bands do not isolate the software effect.** G:495/530; H:150/157. The $100K–$300K samples have median fees $155K/$207.8K and mixed cohorts. Restricting that same band to 2020+ gives CAD **7.416% on 6** versus Revit **12.720% on 15**, reversing the pooled 37.860%/32.802% ordering. Correct comparison filters must match cohort as well as fee band; otherwise describe an association, not “Size, not tool” or matched $150K jobs. Recent CAD under $100K is −27.072% on 38/44 jobs, whereas the displayed all-cohort under-$100K CAD row is +0.735%.
**Material M6 — SQL annual window differs from the CSV report.** S:276–289/335 retains every billing year; the annual CSVs contain only 2019–2026. Input: any of the documented pre-2019 billings creates an extra SQL year with billed revenue but no available labour, potentially read as 100% margin. Correct report export predicate: Year >= 2019 with the same end cutoff; either constrain the snapshot query similarly or explicitly document its wider window.
**Material M7 — the advertised payroll-only SQL option removes partner cost.** S:39/145–146. Input: posted labour 1,550, partner payroll 550, partner hours 10, rate 0 produces labour 1,000, not payroll-only 1,550. Repricing arithmetic itself is correct. Fix the example to say “exclude partner cost,” or add a payroll mode whose LaborCost = LaborAmtPosted and DraftCost = DraftAmtPosted.
**Material M8 — concrete latent input failures, absent from this pull.** G:125/235: remove one matching history row and its current-rate cost silently survives under a posted-cost label; require complete job keys and cost-year keys. G:278/444: a billed job with blank FirstHrs enters “old” totals but no cohort row; group unknown dates separately and use 0 < first_year <= 2019. Two pre-2019 years also produce duplicate “2019 or earlier” rows; aggregate the band before rendering. G:442/494: with only 2020 and 2025 observed, both losses become “2 of 6”; use an explicit complete-year coverage check or an observed-year denominator. A year with billed=0,cost=100 is omitted from losses because None or 0 is zero; count billed < cost, displaying its percentage as unavailable. G:256/314 and S:157 do not enforce an as-of cutoff: relabel this pull 2025 and 2026 data remains; validate input cutoff metadata or label snapshot capture date distinctly.
**Minor M9 — rounding obscures one loss-year.** G:211/442; H:155. Residential Revit 2021 is −0.338780% ($13,337.08 loss), shown “−0%” while counted below zero. Show one decimal for percentages with 0 < abs(x) < .005; the existing 2/6 count is correct. No comparison is calculated from rounded money/pct values, but fixed prose such as “more jobs, smaller ones” at G:414 would remain false for an empty or reversed future population; condition or suppress that prose.
Clean by source inspection: all **7** HTML header/row mappings (8/9/9/9/11/11/11 columns), the **30** job headers/values and money/pct indices; apply_hist all-labour and drafting repricing; summ([]), all-None medians and guarded zero-denominator shares. SQL's **30/16** INSERT mappings, default and alternate current-rate repricing for all/active/engineering/drafting, and FX application to fee/billings/posted amounts—not CAD current-rate costs—align. All three OPENQUERY strings have balanced escaped quotes. UNION keys and summed currency-partition job counts are sound assuming one top-level PR row/currency per WBS1. No missing historical matches, unknown first years, pre-2019 first years, or unsupported tags occur in this pull.
Not determined: actual historical software usage, pre-2019 labour, the folder-sample claim, rendered PDF/XLSX appearance, upstream join uniqueness, exact within-2026 cutoff, or live SQL results. Settle these with source records/dated exports and artifact inspection; no database, build, tests or generator execution was performed. The initial Git/status search happened before the brief's restriction was read; no subsequent Git or repository search was used.

## Sentences to replace

- H:145 “CAD is 23% of the firm's drafting hours this year” → “Among CAD- or Revit-tagged jobs, CAD accounts for 23% of 2026 drafting hours; including General and untagged, it is 21%.” Keep the 54%→23% trend explicitly on the same two-tag denominator.
- H:146 “Counting jobs overstates CAD” / “CAD is 63% of jobs with hours but 39% of billings” → “Among CAD- or Revit-tagged residential jobs, CAD represents 63% of jobs and 39% of billings; the median fees are $50K and $175K.”
- H:147 “residential margins are level overall … fee-weighted” and H:153 “Margin on residential jobs billed to completion” → “For residential jobs billed at least 90% of a positive fee, the recorded billings-weighted margins are 23% CAD and 25% Revit. Older jobs include lifetime billings but only available labour from 2019, so these are not established lifetime margins.”
- H:148 “The level figure is the old book” → “The old cohort carries the pooled result: 83% of CAD and 52% of Revit billings. For first recorded hours in 2020 onward, recorded margins are −14% on 44 CAD jobs and 13% on 39 Revit jobs.”
- H:150 “Size, not tool, explains part of it” / “the CAD losses sit in the under-$100K band” → “Size and cohort mix differ. Across all cohorts, the $100K–$300K band shows 38% at $220/hour for CAD and 33% at $159/hour for Revit; among 2020+ completed CAD jobs, 38 of 44 are under $100K and that subgroup's margin is −27%. These comparisons do not identify a software effect.”
- H:155 “Billings posted before 2019 … are outside the window” → “The calendar tables start in 2019. The job-level tables include earlier billings but lack earlier timesheet costs; the $7.7M CAD and $5.9M Revit pre-2019 totals cannot be allocated to the completion-qualified subset from these exports.”
- H:157 “Like for like: a $150K Revit job against a $150K CAD job” → “Broad fee bands, not matched jobs: the middle-band median fees are $155K CAD and $208K Revit, and cohorts differ.”
- H:172 “It moves CAD and Revit margins by about the same amount” → “Partner repricing lowers the completed margins by 47.2 points for CAD and 37.6 for Revit; payroll-only margins are 70% and 63%, reversing their order.”
- H:174 “In-flight jobs are excluded from margin” / “including them changes no comparison by more than two points” → “The cohort/fee tables use the 90%-billed rule regardless of status: 24 CAD and 13 Revit qualifying jobs remain Active. Calendar tables include in-flight work. Adding closed jobs changes the overall aggregate margins by 1.81/0.95 points, but changes recent-cohort margins by 12.04/4.87 points.”
- H:175 “4,037 of 457,850 billable hours” → “4,037 of 457,854 billable hours,” subject to reconciling the integer-bucket export with the supplied exact total. Other leads/notes contain no independently demonstrated wording defect beyond the scope/history qualifications above; H:176's precise folder-sample counts remain unsupported by the bounded files.

## The two claims

1. **Closure robustness: false as written.** The requested D/I-only population is **265 CAD at 29.011% / 143 Revit at 30.422%**, versus 23.142%/25.478%. Literal *addition* to the original population gives **289 at 24.952% / 156 at 26.423%**: the two overall aggregate margins stay within two points, but cohorts and medians do not (M2).
2. **Old-book dependence: yes; reassurance: no.** The old cohort contributes **110.439% of CAD / 76.073% of Revit recorded profit**. CAD's recent cohort loses money. The adjacent recent-cohort bullet helps, but the stand-alone 23%/25% card and “level overall” can still imply CAD is fine; neither resolves the missing early labour.

## Simpler without losing a number

1. Put “CAD+Revit only” directly into both share labels.
2. Replace “completed” with “≥90% billed” consistently.
3. Name the aggregate column “Billings-weighted margin”; retain median margin separately.
4. Shorten repeated method prose to one shared cost-basis sentence, retaining both sensitivity figures.
5. Replace causal and “like for like” prose with the existing fee-band and cohort qualifications.

**Ship-blocker — one:** resolve or prominently disclose the unmatched pre-2019 billings/cost history before the pooled 23%/25% headline is presented as evidence of job profitability.

## Appendix — independent recomputation script

Run from the repository root with Python; reads only the ten bounded CSVs and prints derived aggregates. It neither imports nor calls the generator. Values are Decimal until output; the supplied external measurements are not remeasured.

```python
import csv, re, json
from pathlib import Path
from decimal import Decimal as D
from statistics import median
from collections import defaultdict, Counter
root=Path("docs/KOR-Financials-CAD-vs-Revit-2026-10-14-data")
data={p.stem:list(csv.DictReader(p.open(encoding="utf-8-sig",newline=""))) for p in root.glob("*.csv")}
def val(row,col): return D(row[col].strip() or "0")
def ratio(a,b): return a/b if b else None
def emit(label,x): print(label,json.dumps(x,default=lambda x:float(x) if isinstance(x,D) else str(x),sort_keys=True))
hist={r["WBS1"].strip():r for r in data["jobs_histcost"]}
jobs=[]
for r in data["jobs_all"]:
    h=hist[r["WBS1"].strip()]; fee,bill=val(r,"Fee"),val(r,"FeeBilled")
    y=re.search(r"\b(?:19|20)\d{2}\b",r["FirstHrs"])
    jobs.append(dict(id=r["WBS1"].strip(),sector=r["Sector"].strip(),type=r["DraftType"].strip(),status=r["Status"].strip(),y=int(y[0]) if y else 0,fee=fee,b=bill,h=val(r,"EngHrs")+val(r,"DraftHrs"),ph=val(h,"PartnerHrs"),bh=val(r,"BillableHrs"),book=val(h,"PostedCost"),c=val(h,"PostedCost")-val(h,"PartnerPostedAmt")+250*val(h,"PartnerHrs"),dc=val(h,"DraftAmt")-val(h,"PartnerDraftAmt")+250*val(h,"PartnerDraftHrs"),ec=val(h,"EngAmt"),current=val(r,"CostAll"),done=fee>0 and bill>=fee*D(".9")))
def total(rows,key): return sum((r[key] for r in rows),D(0))
def stats(rows):
    b,c=total(rows,"b"),total(rows,"c"); m=[(r["b"]-r["c"])/r["b"] for r in rows if r["b"]>0]
    return dict(n=len(rows),fee=total(rows,"fee"),b=b,c=c,margin=ratio(b-c,b),median=median(m) if m else None,median_fee=median([r["fee"] for r in rows]) if rows else None,perhr=ratio(b,total(rows,"h")),payroll_margin=ratio(b-total(rows,"book"),b),fee_weighted=ratio(sum((r["fee"]*(r["b"]-r["c"])/r["b"] for r in rows if r["b"]>0),D(0)),sum((r["fee"] for r in rows if r["b"]>0),D(0))))
res=[r for r in jobs if r["sector"]=="Res"]
for fname,yr in [("drafthrs_by_year",2019),("drafthrs_by_year",2026),("drafthrs_by_year_res",2026)]:
    hours=defaultdict(D)
    for r in data[fname]:
        if int(r["Yr"])==yr: hours[r["DraftType"].strip()]+=val(r,"DraftHrs")
    emit("hours "+fname+" "+str(yr),dict(cad_pair=ratio(hours["CAD"],hours["CAD"]+hours["RV"]),cad_all=ratio(hours["CAD"],sum(hours.values())),hours=hours))
for t in ("CAD","RV"):
    rr=[r for r in res if r["type"]==t]; done=[r for r in rr if r["done"]]
    emit("all "+t,stats(rr)); emit("complete "+t,stats(done))
    for label,subset in [("old",[r for r in done if 0<r["y"]<=2019]),("recent",[r for r in done if r["y"]>=2020]),("mid",[r for r in done if 100000<=r["fee"]<300000]),("closed",[r for r in rr if r["status"] in ("D","I")]),("complete_or_closed",[r for r in rr if r["done"] or r["status"] in ("D","I")]),("active_complete",[r for r in done if r["status"] not in ("D","I")]),("recent_small",[r for r in done if r["y"]>=2020 and r["fee"]<100000])]:
        emit(label+" "+t,stats(subset))
    for lo,hi in [(0,100000),(100000,300000),(300000,10**15)]:
        emit("recent_band "+t+" "+str(lo),stats([r for r in done if r["y"]>=2020 and lo<=r["fee"]<hi]))
emit("shares",dict(jobs=ratio(sum(r["type"]=="CAD" for r in res),sum(r["type"] in ("CAD","RV") for r in res)),billed=ratio(total([r for r in res if r["type"]=="CAD"],"b"),total([r for r in res if r["type"] in ("CAD","RV")],"b"))))
yearly=defaultdict(lambda:defaultdict(D))
for fname,columns in [("billed_by_year",("Billed",)),("cost_by_year",("CostAll","EngHrs","DraftHrs")),("cost_by_year_hist",("PostedCost","PartnerHrs","PartnerPostedAmt"))]:
    for r in data[fname]:
        for scope in ("Firm","Res") if r["Sector"].strip()=="Res" else ("Firm",):
            key=(scope,int(r["Yr"]),r["DraftType"].strip())
            for col in columns: yearly[key][col]+=val(r,col)
def yc(r): return r["PostedCost"]-r["PartnerPostedAmt"]+250*r["PartnerHrs"]
for t in ("CAD","RV"):
    yy=[yearly[("Res",y,t)] for y in range(2020,2026)]; b=sum(r["Billed"] for r in yy); c=sum(yc(r) for r in yy)
    emit("calendar "+t,dict(b=b,c=c,margin=ratio(b-c,b),negative=sum(r["Billed"]-yc(r)<0 for r in yy),years=len(yy)))
    for y in range(2019,2027):
        r=yearly[("Res",y,t)]; b=r["Billed"]
        emit("year "+t+" "+str(y),dict(b=b,c=yc(r),margin=ratio(b-yc(r),b),current=r["CostAll"],current_margin=ratio(b-r["CostAll"],b)))
emit("hero",dict(jobs=len(jobs),tagged=sum(bool(r["type"]) for r in jobs),untagged_res_billed=total([r for r in res if not r["type"]],"b"),partner_share=ratio(250*total(jobs,"ph"),total(jobs,"c")),partner_hours_share=ratio(total(jobs,"ph"),total(jobs,"bh"))))
emit("input_health",dict(unknown_first=sum(r["y"]==0 for r in jobs),earlier_than_2019=sum(0<r["y"]<2019 for r in jobs),unknown_tags=sorted({r["type"] for r in jobs}-{"CAD","RV","General",""}),duplicate_jobs=len(jobs)-len({r["id"] for r in jobs}),duplicate_hist=len(data["jobs_histcost"])-len(hist),hist_missing=sum(r["WBS1"].strip() not in hist for r in data["jobs_all"])))
emit("partner_drafting",dict(hours=sum(val(r,"PartnerDraftHrs") for r in data["jobs_histcost"]),payroll=sum(val(r,"PartnerDraftAmt") for r in data["jobs_histcost"])))
for t in ("CAD","RV"):
    rr=[r for r in res if r["type"]==t]; done=[r for r in rr if r["done"]]; added=[r for r in rr if r["done"] or r["status"] in ("D","I")]; old=[r for r in done if 0<r["y"]<=2019]
    emit("old_weight "+t,dict(billed_share=ratio(total(old,"b"),total(done,"b")),profit_share=ratio(total(old,"b")-total(old,"c"),total(done,"b")-total(done,"c"))))
    for label,pick in [("all",lambda r:True),("recent",lambda r:r["y"]>=2020),("small",lambda r:r["fee"]<100000)]:
        emit("closure_change "+t+" "+label,dict(before=stats([r for r in done if pick(r)]),after=stats([r for r in added if pick(r)])))
emit("all_res_shares",dict(cad_jobs=ratio(sum(r["type"]=="CAD" for r in res),len(res)),cad_billed=ratio(total([r for r in res if r["type"]=="CAD"],"b"),total(res,"b"))))
emit("pre2019",{r["DraftType"] or "untagged":dict(jobs=int(r["Jobs"]),billed=val(r,"BilledBefore2019")) for r in data["pre2019_billed"]})
emit("year_coverage",{n:sorted({int(r["Yr"]) for r in data[n]}) for n in ("billed_by_year","cost_by_year","cost_by_year_hist")})
emit("coverage_precision",{k:sum(val(r,k)!=val(r,k).to_integral_value() for r in data["coverage_amt"]) for k in ("BillableHrs","HrsNoAmt")})
```
