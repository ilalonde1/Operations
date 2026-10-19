# KOR win/loss record from the Management share

2026-10-18. Built from `M:\Project Opportunities` (Pending / Submitted to Client / Rejected / Word Documents for Awarded
Projects), `M:\Project Management` job folders, `Fee Proposal Tracking.xlsx`, and the text of 7,503 proposal and contract
documents extracted on FS01. Reconciled against `opportunities.CrmEngagements` only. Deltek projects used **read-only**,
only to split "submitted" into won and not-won. Nothing was written to any CRM table.

Data (Ian only): `C:\Users\ilalonde\Desktop\BD\Management\`
`winloss-records-2026-10-18.csv` (4,785 pursuits) · `winloss-ambiguous-2026-10-18.csv` (45) ·
`winloss-crm-reconciliation-2026-10-18.csv` (177) · `winloss-stats-2026-10-18.json`.
Rebuild: `KOR_MGMT_WORK=<work dir> python tools/ManagementShare/win_loss.py` (deterministic: two runs byte-identical).

## Headline

**The tracker years, 2020–2026 (701 pursuits): KOR won 281 and lost 109 = 72% of decided proposals. But 266 more were
marked "Submitted for Approval" and never closed, and none of them became a Deltek job. Counting those as not won, KOR
converts 43% of what it submits.** 27 more are under 180 days old and still open.

| Measure | Won | Lost | Not won (inferred) | Open | Win rate, decided | Win rate, inferred |
|---|---:|---:|---:|---:|---:|---:|
| Tracker pursuits 2020–2026 (the clean register) | 281 | 109 | 266 | 27 | **72%** | **43%** |
| All folder pursuits dated 2017+, new commissions | 1,115 | 478 | 501 | 65 | 70% | 53% |

The tracker is the reliable denominator. The folder series is not consistent year to year, because filing practice
changed: 2017 shows 167 won against 14 lost, while 2018–2020 show more losses than wins. So the folder rates are context,
not a trend.

Proposed fee, tracker pursuits: median **$90k on wins** and **$115k on losses and not-won**. Total proposed: $42.5M won
against $60.5M lost or not won.

## How outcomes were decided, and how far to trust them

- **Won**: an awarded proposal document, a Project Management job folder, or tracker "Proposal Accepted".
- **Lost**: a Rejected folder or document, or tracker "Rejected :(".
- **Conflict** (16): both, e.g. one phase won and another lost. Listed in the ambiguous file.
- **Submitted only**: split with Deltek. A real job (ChargeType R; not the 10xxx promo records, not the 20xxx pursuit
  register, not 99/9-letter) whose `LEFT(WBS1,5)` carries labour hours, matching the site or job number and created
  from 60 days before to 540 days after the proposal, counts as **Won (Deltek)** (10). With no such job: **Not won** if
  older than 180 days (507), **Open** if younger (65). Hours were aggregated by `LEFT(WBS1,5)` before any join.
- **Validation of the splitter** (`tools/ManagementShare/wl_recall.py`): **249 of 266** tracker-accepted pursuits have a
  high-confidence Deltek job, against **6 of 478** known losses. So a missing Deltek job is strong evidence of not won.
- **Second check against Deltek's own outcome link** (below): of 13 M:-inferred "not won" pursuits whose Deltek promo
  record is closed, 13 are LOST or DNP and 0 are won.
- One defect was caught by reading rows rather than counts. The first pass matched 10xxx numbers in folder names to
  Deltek records created days before the due date, scoring 110 "Deltek wins". Those 10xxx records are ChargeType P
  promotional projects that carry the proposal-writing hours. Excluding P and H records and the 20xxx pursuit register
  cut the 110 to 10.

## Breakdowns (tracker pursuits, 2020–2026)

**By year**

| Year | Won | Lost | Not won | Open | Decided | Inferred |
|---|---:|---:|---:|---:|---:|---:|
| 2021 | 48 | 22 | 42 | 0 | 69% | 43% |
| 2022 | 53 | 24 | 50 | 0 | 69% | 42% |
| 2023 | 68 | 20 | 31 | 0 | 77% | 57% |
| 2024 | 44 | 27 | 54 | 0 | 62% | 35% |
| 2025 | 41 | 8 | 53 | 0 | 84% | 40% |
| 2026 (part) | 9 | 5 | 32 | 27 | 64% | 20% |

**By lead engineer**: the per-person table is kept out of the repo, in Ian's `HR-FINDINGS.md` (it is individual
performance data). Firm-level point: one principal leads 345 of 701 tracker pursuits (49%).

**By region**

| Region | Won | Lost | Not won | Decided | Inferred |
|---|---:|---:|---:|---:|---:|
| Metro Vancouver (core) | 170 | 68 | 144 | 71% | 44% |
| South of Fraser / Fraser Valley | 50 | 12 | 64 | 81% | 40% |
| USA | 22 | 12 | 19 | 65% | 42% |
| Okanagan / Interior | 14 | 6 | 14 | 70% | 41% |
| Vancouver Island | 11 | 10 | 13 | 52% | 32% |

**By proposed fee**: the bigger the fee, the lower the decided rate.

| Fee band | Won | Lost | Not won | Decided | Inferred |
|---|---:|---:|---:|---:|---:|
| $10–25k | 15 | 1 | 7 | 94% | 65% |
| $25–50k | 49 | 7 | 34 | 88% | 54% |
| $50–100k | 87 | 27 | 81 | 76% | 45% |
| $100–250k | 83 | 37 | 90 | 69% | 40% |
| ≥ $250k | 45 | 32 | 34 | 58% | 40% |

**By client** (≥ 4 decided; client key = first two distinctive words)

- Converts reliably: Westland 8 won / 0 lost, Northland 7/0, Cressey 7/0, Domus 6/0, Kerkhoff 6/0, Locarno Legacy 5/0,
  Polygon 6/1, Martinez + Cutri 5/1.
- Proposals that mostly go nowhere: **NSDA 4 won / 6 lost / 12 not won**, **Wesgroup 4/6/6**, Strand 3/3/7,
  Intracorp 3/3/4, Aquilini 1/3/1, Third Space 1/3/1, Liberty National 2/2/5, Porte 0/0/5, **Starlight 1/0/5**.

**By sector**: biased, use with care. Won pursuits inherit a sector from their Project Management folder, but lost and
unclosed ones are classified only by keywords in their names, and 211 of 701 tracker rows stay unclassified (13 won /
61 lost / 137 not won). Among classified rows, office/commercial (90% decided), small/TI (88%), hotel (80%) and
institutional (82%) convert better than the residential towers that dominate fee value.

## Why KOR loses (where the documents say)

Reasons appear in the tracker comments only. The proposal letters do not record outcomes, and `PR.LostTo` in Deltek is
effectively unused. Of 291 lost or not-won pursuits with any comment, most comments are status notes ("Fee Proposal
Submitted"). The stated reasons:

- Awarded to another firm or competitor, unnamed: 15.
- **Glotman Simpson named: 6** (Stober 2021, BCGEU 2021, Intracorp 2021, Primex 2025 among them). RJC: 1. WSB: 1.
- Fee too high: 3 (Belford 2023, Frank Lin 2021, one more).
- Other: building sold so not rehired (Pacific Property 2022); "fees only valid if awarded both Kemsley and Tyndall"
  (Strand 2022); scope changed to another engineer (Sakura 2024).

Process finding: a single follow-up push is visible, with "Advised <PM> to follow up May 29, 2023" on about 20 rows. There
is no standing close-out step, which is why 299 rows still read "Submitted for Approval".

## Reconciliation with CrmEngagements (read-only)

- CRM holds 316 engagements. 177 are the migration-268 backfill of Deltek CustomProposal wins (opened 2022-06 to
  2026-03). The other 139 are client relationship entries with no project, fee or file, so they cannot be matched to
  proposals.
- Of the 177 CRM wins: **129 confirmed won on M:**. 10 disagree: 8 show on M: as submitted with no Deltek job, 1 as
  lost and 1 as pending. 38 could not be matched, mostly because CRM titles use address forms the matcher misses
  (e.g. "13508 - 13546 98A Ave Surrey"). All 177 are listed in the reconciliation CSV.
- **M: holds 571 new-commission wins dated 2023 or later. 106 link to a CRM win.** The other ~465, many of them small,
  are absent from CRM.

## Proposed import, for Ian's decision (nothing written)

1. **Tracker into CRM first.** One `CrmEngagements` row per tracker pursuit (701), `ExternalSource = 'M.FeeProposalTracking'`,
   `ExternalSourceKey = '<sheet>!row<n>'` (idempotent through the migration-267 unique index). Stage from the reconciled
   outcome (Won 6 / Lost / Submitted / Drafting), ProposedFee from the tracker, OpenedAtUtc from RFP or submitted date,
   ClosedAtUtc from accepted date, OutcomeReason from the comments. Skip the 129 rows already linked to a migration-268
   win and update them in place instead. The 266 never-closed rows go in as Lost with
   `OutcomeReason = 'No Deltek job after 180 days (inferred 2026-10-18)'`, so the inference stays visible and reversible.
2. **Folder history 2017+** (1,115 won / 478 lost / 501 not won) as `ExternalSource = 'M.ProjectOpportunities'`, key = the
   share-relative path, only after Ian has looked at the 45 ambiguous rows.
3. A migration in `Kor.Opportunities.Data/Schema` (next number), written like 268 (NOT EXISTS guard, reversible DELETE by
   ExternalSource), with a dry-run count first.

## Deltek backfill — what it would need (not built)

Deltek does hold a usable outcome link from July 2023. Each proposal is opened as a **promotional project**:
`PR.WBS1` 10xxx, `ChargeType = 'P'`, holding the proposal hours. Its **`PR.SiblingWBS1`** points either to the real job
when won (ChargeType R, e.g. 31xxx, `Stage = '~WDEF~'`) or to a **20xxx pursuit record** whose `PR.Stage` is
`LOST`, `DNP` or `InPursuit`. Of 329 linked promo records (created 2023-07-28 to 2026-01-15): **130 won, 69 lost, 7 did
not pursue, 123 still InPursuit**, which is 65% of decided. Only 52 of the 329 promo numbers appear in M: folder names.
On those 52, Deltek and M: agree except for 4 M: wins that Deltek shows as Lost (2) or InPursuit (2).

A backfill would read: `PR` (WBS1, WBS2/WBS3 blank, ChargeType, Stage, SiblingWBS1, CreateDate, ClientID, Name,
LongName, Address1, City, Fee, ProjMgr, Principal), `Clendor` (ClientID → Name), `ClendorProjectAssoc`
(Role = 'Architect'), and `LD` (RegHrs + OvtHrs, aggregated by `LEFT(WBS1,5)` first because joins fan out). The 123
InPursuit records need the same close-out discipline as the tracker. `PR.ProposalWBS1`, `PreAwardWBS1`, `OpportunityID`
and `ClosedReason` are empty on all top-level rows, and `LostTo` is effectively unused.

## Limits

- Merging is by site key (street number + first street word) within 365 days, or by job number. Different addresses for
  the same project stay apart (e.g. "293" and "295 East 11th Ave"), and different phases at one address merge.
- Add-on letters on existing jobs are marked "add-on to existing job" only when the name says so (141 of 4,785), so the
  folder win counts still include some extra-services letters. The tracker figures are unaffected.
- Fees come from the tracker (701 rows) or from the largest "Total … Fees" line of the latest proposal letter. Scanned
  PDFs give nothing, and a few small values ($800–$1,500) are probably per-visit rates rather than totals.
- 2,578 → 880 pursuits lack an engineer after parsing pre-2018 file names. Sector and region parse from names.
