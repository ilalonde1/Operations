# Deltek fee split: original vs extra (findings, 26 Nov 2026)

Read-only research against KOR's Vantagepoint (linked server `DELTEK_VP`), run 26 Nov 2026 ~18:00-18:42 PT
(`date` at write: Thu Nov 26 18:42:35 BCDT 2026). Every number below carries the query that produced it.
All queries ran as `python -I tools\SqlQuery\oq.py -q "SELECT * FROM OPENQUERY(DELTEK_VP, '<inner>')"`; inner SQL is
shown with single quotes un-doubled for readability.

**Population.** "Hot list" = the app's own base filter (`FinancialsService.LoadBaseProjectsSync`, lines 489-528):

```sql
-- HOT
WITH hot AS (
  SELECT pr.WBS1, pr.Fee, pr.Org FROM PR pr
  JOIN ProjectCustomTabFields pctf ON pctf.WBS1 = pr.WBS1 AND (pctf.WBS2 IS NULL OR LTRIM(RTRIM(pctf.WBS2)) = '')
  WHERE (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
    AND UPPER(LTRIM(RTRIM(pr.Status))) IN ('A','ACTIVE')
    AND pr.WBS1 NOT LIKE '[A-Z]%' AND pr.WBS1 NOT LIKE '9[A-Z]%' AND pr.WBS1 NOT LIKE '99%'
    AND UPPER(LTRIM(RTRIM(pctf.CustWatchlist))) IN ('Y','YES','TRUE','1'))
```

It returns **61 projects** (Q-B and Q-C below each return 61 rows). Nothing below was checked outside these 61.

---

## Headline

KOR's WBS template marks extras structurally: every phased project has original phases `1.PD 2.SD 3.DD 4.CD 5.CA`
at WBS2 and a parallel set **`X.1 .. X.5` named "Extra - <phase>"**, each with a `01 General` starter task at WBS3 and
one WBS3 element per extra (`02`, `03`, ...). Fee rolls up exactly: top Fee = sum of WBS2 Fee in 50 of 50 projects that
have WBS2 rows. So:

> **Extra fee = SUM(PR.Fee) of WBS2 rows whose code starts with `X`, plus the app's existing hourlyRev (which on the
> hot list is 100% X-element billing). Original fee = top-level PR.Fee minus the X fee.**

On the hot list this gives **11 of 61 projects with extras** (8 with X fee, 3 more with hourly-billed X elements only).

---

## 1. WBS2/WBS3 rows in PR, and the rollup

### Fee columns on PR

```sql
SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'PR'
  AND (COLUMN_NAME LIKE '%Fee%' OR COLUMN_NAME LIKE '%Consult%' OR COLUMN_NAME LIKE '%Reimb%' OR COLUMN_NAME LIKE '%Contract%'
       OR COLUMN_NAME LIKE '%Type%' OR COLUMN_NAME LIKE '%Charge%' OR COLUMN_NAME LIKE '%Sub%' OR COLUMN_NAME LIKE '%Level%')
```

60 rows. Fee-bearing columns that exist: `Fee, FeeDirLab, FeeDirExp, ConsultFee, ReimbAllow, ReimbAllowExp, ReimbAllowCons,
TotalContractValue, EstFees` (+ Billing/Functional-currency twins). Also `SubLevel, ChargeType, RevType, ContractDate`.

**Non-zero:** only `Fee` and `FeeDirLab` (equal to each other on every row of Q-A). `FeeDirExp, ConsultFee, ReimbAllow` summed
over all hot-list WBS2 rows are 0 in 61 of 61 projects (Q-C, columns ConsFee/Reimb/DirExp). `TotalContractValue` and
`EstFees` are 0 on every row of Q-A.

### Q-A: three real examples

```sql
SELECT WBS1, WBS2, WBS3, SubLevel, ChargeType, Status, LEFT(Name,40) AS Name, Fee, FeeDirLab, FeeDirExp, ConsultFee,
       ReimbAllow, ReimbAllowExp, ReimbAllowCons, TotalContractValue, EstFees, RevType
FROM PR WHERE WBS1 IN ('31170-01','31097-01','31184-01') ORDER BY WBS1, WBS2, WBS3
```

55 rows. Rows with non-zero Fee (FeeDirLab identical; every other fee column 0; ChargeType R, RevType B throughout):

| WBS1 | WBS2 | WBS3 | Name | Fee |
|---|---|---|---|---|
| 31097-01 | | | Heather Lands Parcel B | 432,000 |
| | 2.SD / 3.DD / 4.CD / 5.CA | | phases | 32,000 / 260,000 / 28,000 / 80,000 (= 400,000) |
| | X.2 | | Extra - Schematic Design | 32,000 |
| | X.2 | 02 | Schematic Design Iterations | 32,000 |
| | X.4 | 02 | Suite Changes to Suit City Comments | 0 (billed hourly, see 3) |
| 31170-01 | | | 2005-2045 West 49 Ave Vancouver | 143,000 |
| | 2.SD / 3.DD / 4.CD / 5.CA | | phases | 12,000 / 53,000 / 30,000 / 16,000 (= 111,000) |
| | X.4 | | Extra - Construction Documents | 32,000 |
| | X.4 | 02 | Detention Tank - Design and coordination | 5,000 |
| | X.4 | 03 | Redesign Lateral System per BCBC 2024 (Status D) | 0 |
| | X.4 | 04 | Owner requested suite revisions | 27,000 |
| 31184-01 | | | 7788 Cambie St Vancouver | 56,500 |
| | 2.SD / 3.DD / 4.CD / 5.CA | | phases | 2,000 / 19,000 / 3,000 / 14,000 (= 38,000) |
| | X.3 | | Extra - Design Development | 18,500 |
| | X.3 | 02 | Impact Study of a Building | 2,000 |
| | X.3 | 03 | Redesign from L1 to Roof | 16,500 |

Every X.1..X.5 in these three carries a `01 General` WBS3 at Fee 0. Original phases have no WBS3 rows.
SubLevel = Y on rows that have children (top, X.n), N on leaves.

### Q-B/Q-C: is top Fee the sum of WBS2 Fee? (all 61)

```sql
-- HOT, then
SELECT hot.WBS1, hot.Fee AS TopFee, COUNT(w2.WBS2) AS nWbs2, SUM(w2.Fee) AS SumWbs2Fee,
       SUM(CASE WHEN w2.WBS2 LIKE 'X%' THEN w2.Fee ELSE 0 END) AS XFee,
       SUM(CASE WHEN w2.WBS2 NOT LIKE 'X%' THEN w2.Fee ELSE 0 END) AS NonXFee,
       SUM(CASE WHEN w2.WBS2 LIKE 'X%' THEN 1 ELSE 0 END) AS nX, MIN(w2.WBS2), MAX(w2.WBS2),
       SUM(w2.ConsultFee) AS ConsFee, SUM(w2.ReimbAllow) AS Reimb, SUM(w2.FeeDirExp) AS DirExp
FROM hot LEFT JOIN PR w2 ON w2.WBS1 = hot.WBS1
  AND LTRIM(RTRIM(COALESCE(w2.WBS2,''))) <> '' AND LTRIM(RTRIM(COALESCE(w2.WBS3,''))) = ''
GROUP BY hot.WBS1, hot.Fee ORDER BY hot.WBS1
```

61 rows, counted by a script over the full output:

- Top Fee = sum of WBS2 Fee: **50 of 50** projects that have WBS2 rows.
- No WBS2 rows at all (flat project): **11 of 61** (01803-01, 01820-01, 30101-12, 30978-03, 31057-04, 31231-01, 60058-24,
  60074-02, 80056-09, 90070-17, 90110-01).
- X.1-X.5 template present: **48 of 61** (always 10 WBS2 rows, 1.PD..X.5). WBS2 phases but no X template: 2 (01787-01,
  90108-04, each 3.DD..5.CA only).
- XFee > 0: **8 of 61**.

## 2. Telling original from extra

### Contract tables exist and KOR fills them

```sql
SELECT TABLE_NAME, COUNT(*) AS cols FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME LIKE '%Contract%' GROUP BY TABLE_NAME
```
18 tables: `Contracts` (16 cols), `ContractDetails` (34), `ContractCredit` (9), `CFGContractType*`, `CFGContractStatus*`,
`CFGMasterContract*`, `CFGContractTypeGovCon*`, and `*Template` versions. No `PRContract*`.

```sql
SELECT (SELECT COUNT(*) FROM Contracts), (SELECT COUNT(DISTINCT WBS1) FROM Contracts), (SELECT COUNT(*) FROM ContractDetails),
       (SELECT COUNT(*) FROM ContractCredit), (SELECT COUNT(*) FROM CFGContractType), (SELECT COUNT(*) FROM CFGContractStatus)
```
Contracts 1,899 rows over 1,468 WBS1; ContractDetails 8,094; ContractCredit 0; 4 contract types; 5 statuses.

`CFGContractTypeDescriptions` (en-US): **01 Original, 02 Change Order, 03 Additional Services, 04 Letter of Agreement**.
`CFGContractStatusDescriptions` (en-US): 01 Negotiation, 02 Pending, 03 Sent For Signature, 04 Approved & Signed, 05 Rejected.
`Contracts.FeeIncludeInd` Y/N = whether the contract counts toward PR.Fee.

Distribution (`SELECT ContractType, ContractStatus, FeeIncludeInd, COUNT(*), COUNT(DISTINCT WBS1), MIN(CreateDate), MAX(CreateDate)
FROM Contracts GROUP BY ...`, 41 rows): largest groups 01/04/Y 779 rows, 01/03/Y 426, 03/03/Y 156, 03/04/Y 133, 01/NULL/Y 76,
NULL/NULL/Y 61; last created 2026-11-26. In use and current.

### Q-D: contracts per hot-list project

```sql
-- HOT, then
, cs AS (SELECT c.WBS1, c.ContractType, c.FeeIncludeInd, COUNT(*) AS nC, SUM(d.Fee) AS dFee FROM Contracts c
         LEFT JOIN (SELECT WBS1, ContractNumber, SUM(Fee) AS Fee FROM ContractDetails
                    WHERE WBS2 IS NULL OR LTRIM(RTRIM(WBS2)) = '' GROUP BY WBS1, ContractNumber) d
           ON d.WBS1 = c.WBS1 AND d.ContractNumber = c.ContractNumber
         GROUP BY c.WBS1, c.ContractType, c.FeeIncludeInd)
SELECT hot.WBS1, hot.Org, hot.Fee, cs.ContractType, cs.FeeIncludeInd, cs.nC, cs.dFee FROM hot LEFT JOIN cs ON cs.WBS1 = hot.WBS1
```
71 rows over 61 projects (script count):
- PR.Fee = sum of FeeIncludeInd=Y contract fees: **61 of 61** (incl. the 6 with no contract and Fee 0). Contracts are how Fee is set.
- No contract rows: 6 of 61.
- Has an included 02/03/04 contract: **7 of 61**.

### ContractType vs the X prefix

Comparing Q-C XFee with Q-D included 02/03/04 fee: **agree in 6 of 8** projects that have either. The 2 disagreements
(contracts and details read with `SELECT c.WBS1, c.ContractNumber, c.ContractType, c.ContractStatus, c.FeeIncludeInd, c.ApprovedDate,
c.ContractDescription, d.WBS2, d.WBS3, d.Fee FROM Contracts c JOIN ContractDetails d ON ... WHERE c.WBS1 IN ('30993-01','31130-01') AND d.Fee <> 0`):

| WBS1 | XFee | Type-03 fee | Why |
|---|---|---|---|
| 30993-01 | 210,000 | 115,000 | contract `30993-01 X.3 02 PBD` (X.3/02, $95,000) is typed **01 Original** |
| 31130-01 | 55,000 | 0 | contracts `31130-01X.4 02` ($35,000) and `31130-01X.4 03` ($20,000), both described "Additional Services", typed **01 Original** |

So ContractType is hand-set and wrong in 2 of 8; the contract details themselves point at X elements. **The X prefix on WBS2
is the reliable marker; ContractType is not.** Also, 02 (Change Order) and 04 (LOA) never appear among included hot-list
contracts; only 03.

Other markers looked at and not useful: `ChargeType` (R on all Q-A rows), `RevType` (B on all), `SubLevel` (structure only),
`DefaultTaskType`/`ProjectType` (not inspected per row: not checked). `01 General` is a starter at Fee 0 in every X.n of Q-A.

## 3. What hourlyRev is

Code: `FinancialsService.LoadHourlyRevenueSync` (lines 650-683), used at line 341 `totalFee = p.Fee + hourlyRev`:

```sql
SELECT sm.WBS1, SUM(CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE COALESCE(sm.Revenue,0) END) AS HourlyRevenue
FROM PRSummaryMain sm JOIN PR pr ON pr.WBS1 = sm.WBS1 AND pr.WBS2 = sm.WBS2 AND pr.WBS3 = sm.WBS3
WHERE sm.WBS1 IN (...) AND pr.Fee = 0 AND pr.WBS2 <> '' AND pr.WBS3 <> ''
GROUP BY sm.WBS1 HAVING ... > 0
```

It is **billed amount on WBS3 tasks that have no fee**, used as if it were fee (so those tasks read 100% billed).
Since original phases have no WBS3 rows, it can only land on X elements in this template. Measured:

```sql
-- HOT, then
SELECT sm.WBS1, sm.WBS2, sm.WBS3, LEFT(pr.Name,40), SUM(CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE COALESCE(sm.Revenue,0) END)
FROM PRSummaryMain sm JOIN PR pr ON pr.WBS1=sm.WBS1 AND pr.WBS2=sm.WBS2 AND pr.WBS3=sm.WBS3 JOIN hot ON hot.WBS1=sm.WBS1
WHERE pr.Fee = 0 AND <WBS2, WBS3 non-blank> GROUP BY sm.WBS1, sm.WBS2, sm.WBS3, pr.Name HAVING ... <> 0
```
4 rows, **all on X elements** (grouping by X/non-X returned only `X` classes):

| WBS1 | Element | Name | hourlyRev |
|---|---|---|---|
| 01773-01 | X.3/02 | Redesign to Allow Concrete Topping | 1,975 |
| 31084-01 | X.4/03 | Addition of Spa at Level 2 | 7,500 |
| 31097-01 | X.4/02 | Suite Changes to Suit City Comments | 25,000 |
| 31121-01 | X.3/02 | Structural Revisions | 12,000 |

**Yes, on the hot list hourlyRev is extras** (46,475 total, 4 projects): extras billed hourly with no fee entered. 31121-01's
$12,000 matches a type-03 contract with FeeIncludeInd = N ($12,000, Q-D) - a pending extra kept out of Fee but billed.
Not checked outside the hot list: a non-template project with WBS3 tasks under original phases would put original work here.

## 4. Recommended rule

Per top-level WBS1 (no change to how projects are listed):

- **ExtraFee = SUM(PR.Fee) WHERE WBS2 LIKE 'X%' AND WBS3 blank, + hourlyRev restricted to WBS2 LIKE 'X%'**
- **OriginalFee = top-level PR.Fee - SUM(PR.Fee WHERE WBS2 LIKE 'X%', WBS3 blank)** (+ any hourlyRev on non-X WBS3, which is 0 on the hot list)
- TotalFee unchanged (= Fee + hourlyRev). %Billed of original = total billed / OriginalFee, so >100% shows extras being billed.
  Billed can also be split by element from PRSummaryMain.WBS2 (Q-E).

**Count: 11 of 61** hot-list projects have extras: 8 with X fee (30993-01 210,000; 31097-01 32,000; 31120-01 8,000;
31130-01 55,000; 31170-01 32,000; 31184-01 18,500; 31195-01 15,000; 31202-01 32,500) plus 3 with X hourlyRev only
(01773-01, 31084-01, 31121-01). 31097-01 has both.

Q-E, billed split (`-- HOT, then SELECT sm.WBS1, MAX(hot.Fee), SUM(billed WHERE sm.WBS2 LIKE 'X%') AS BilledX, SUM(billed WHERE
NOT LIKE 'X%' OR NULL) AS BilledNonX, SUM(billed WHERE WBS2 blank) FROM PRSummaryMain sm JOIN hot ... GROUP BY sm.WBS1`, 23 rows):
e.g. 31097-01 original 400,000, billed non-X 318,600 + X 57,000 = 375,600 (93.9% of original); 31170-01 original 111,000,
billed 95,000 + 32,000 = 127,000 (**114% of original**); 01773-01 original 10,000, billed 10,075 + 1,975 (120%).

### Known failure cases

1. **Flat projects (11 of 61, no WBS2):** rule reports 0 extras whatever happened. Includes Fee-0 hourly jobs billed only at the
   top row (60058-24 177,222; 60074-02 109,682) - hourlyRev misses these too (it needs WBS3), so they read 0% billed today.
2. **No X template (2 of 61: 01787-01, 90108-04):** an extra would have to go into a phase fee and would read as original.
3. **Extras on sub-numbers:** 12 of 61 hot projects have other active top-level siblings on the same 5-digit root
   (`SELECT hot.WBS1, s.WBS1, ... FROM hot JOIN PR s ON LEFT(s.WBS1,5) = LEFT(hot.WBS1,5) AND s.WBS1 <> hot.WBS1 AND s.WBS2 blank
   WHERE s.Status A`, 33 rows). Several are same-client extras, not different-client services: 60058-05..-37 (11 Sutton
   siblings, all client CL00299, all Fee 0, e.g. "Redesign NE Corner"), 60074-04..-07 (CL00299, "Redesign Piles to Geotech"),
   31057-04 "Lot C Structural Redesign" (same client CL00099 as -01..-03). Others are genuine parallel originals (31168-01/-02/-03
   buildings A/B/C). The rule keeps each as its own project, which is Jim's rule; it cannot tell the two cases apart.
4. **An extra folded into a phase fee** (fee raised on 3.DD instead of an X element) is invisible.
5. **Hourly extras** count billed as fee, so their own % billed is always 100% and the "extra fee" grows with billing.
6. **Pending extras** (contract FeeIncludeInd = N) are not in Fee until approved (31121-01's $12,000; it shows only as hourlyRev).
7. Do **not** substitute Contracts.ContractType: wrong on 2 of 8 (section 2).

## 5. Earned (BillExt) and cost from tkDetail

```sql
SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'tkDetail'
  AND (COLUMN_NAME LIKE '%Amt%' OR COLUMN_NAME LIKE '%Bill%' OR COLUMN_NAME LIKE '%Rate%' OR COLUMN_NAME LIKE '%Hrs%' OR COLUMN_NAME LIKE '%Currency%')
```
Confirmed on tkDetail: `RegHrs OvtHrs SpecialOvtHrs`, `RegAmt OvtAmt SpecialOvtAmt` (+ `*ProjectCurrency`), **`BillExt`**,
`Rate OvtRate SpecialOvtRate`, `BillCategory` (each listed twice: the view exposes two same-named objects, not investigated).
LD (posted labor) has the same plus `*BillingCurrency` and `*EmployeeCurrency`.

Per project, excluding rejected lines as the app does:

```sql
SELECT t.WBS1, COUNT(*), SUM(RegHrs+OvtHrs+SpecialOvtHrs) AS Hrs, SUM(t.BillExt) AS BillExt,
       SUM(CASE WHEN COALESCE(t.BillExt,0)=0 THEN 1 ELSE 0 END) AS rowsBillExtZero,
       SUM(RegAmt+OvtAmt+SpecialOvtAmt) AS Cost, SUM(<*ProjectCurrency>) AS CostProjCur, MIN(TransDate), MAX(TransDate)
FROM tkDetail t WHERE t.WBS1 IN ('31170-01','31195-01','31097-01') AND COALESCE(t.LineItemApprovalStatus,'') <> 'R' GROUP BY t.WBS1
-- billed: SELECT WBS1, SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE COALESCE(Revenue,0) END), MAX(Period) FROM PRSummaryMain WHERE WBS1 IN (...) GROUP BY WBS1
```

| WBS1 | Org / currency | Hours | Earned SUM(BillExt) | Cost SUM(Reg+Ovt+SpOvt Amt) | Billed (PRSummaryMain, to period) | Billed/Earned | Billed/Cost | Earned $/h | Cost $/h |
|---|---|---|---|---|---|---|---|---|---|
| 31097-01 | CAD | 2,195.5 | 415,248.15 | 130,689.40 | 375,600 (202611) | 0.90 | 2.87 | 189 | 60 |
| 31170-01 | CAD | 756.5 | 145,074.97 | 44,631.15 | 127,000 (202611) | 0.88 | 2.85 | 192 | 59 |
| 31195-01 | USA / USD | 1,006.0 | 201,587.49 | 42,257.44 | 235,000 (202609) | 1.17 | 5.56 | 200 | 42 |

- Earned = hours x charge-out = SUM(BillExt): yes. By labor code on 31170-01, BillExt/hours = 199/h (code 10), 175/h (20), 232/h (30);
  rows with BillExt 0 carry 0 hours (31170-01: 0 of 485 / 252.5 / 19 hours; 31195-01: 3 of 610 hours on code 10).
- Cost = SUM(RegAmt+OvtAmt+SpecialOvtAmt): yes, same as the app's `LoadCostByLaborSync`. tkDetail includes lines dated up to
  2026-11-29 (current week, not yet posted); LD (posted) is slightly lower (31097-01: 2,136.5 h, BillExt 403,123, cost 125,545).
- **Currency:** amounts are in the project (= pr.Org) currency. For 31195-01 (USA, ProjectCurrencyCode USD) LD shows
  RegAmt = RegAmtProjectCurrency = RegAmtBillingCurrency = 40,816.26 while **RegAmtEmployeeCurrency = 56,319.31** (CAD pay),
  ratio 1.38. tkDetail RegAmt equals its ProjectCurrency column (42,257.44 both). So USD-project cost is USD; convert before
  mixing with CAD projects. 31195-01's high billed/cost (5.56) is partly that ~$42 USD/h cost rate.
