# KOR Management share (M:) — map, classification and what was ingested

2026-10-18. `M:` = `\\KOR-FS01\Management` = `E:\Management` on FS01 (`Get-SmbShare`, run on FS01).
Companion: [win/loss findings](KOR-Management-Win-Loss-2026-10-18.md). Ian-only HR summary:
`C:\Users\ilalonde\Desktop\BD\Management\HR-FINDINGS.md`. Progress log: `...\Desktop\BD\Management\PROGRESS.md`.

## How it was measured (nothing walked the share from the PC)

| Step | Where it ran | Command / tool | Output |
|---|---|---|---|
| Inventory of every file and folder | **on FS01** (main session) | PowerShell walk of `E:\Management` | `C:\Windows\Temp\kor-mgmt-inventory.csv`, 67,242 rows, read back once |
| Tool probe | **on FS01** via `Win32_Process Create` | `tools/ManagementShare/launch_fs01.ps1` + probe | PowerShell 5.1 only: no Python, pdftotext, Office or .NET |
| Text extraction | **on FS01** | `tools/ShareTextExtract` (self-contained single-file exe, staged with Ian's go-ahead) driven by `tools/ManagementShare/fs01_extract.ps1` | one JSONL bundle per manifest, read back once |
| Counts, classification | locally, over the inventory CSV | `tools/ManagementShare/analyse.py`, `classify.py`, `junk.py`, `hr_metrics.py` | the tables below |
| Manifests (what to extract) | locally | `tools/ManagementShare/manifests.py` | 569 knowledge files, 7,503 proposal/contract files |

Prior art used, not rebuilt: `tools/BdKnowledgeIngest` (ingest / export / claims import / embed / relate), the claim
chunker, merger and slug folder from the 2026-10-18 canada run, the relations exporter (pass D) and the claim/relation
agent instructions. Migration 268 and `docs/KOR-BD-Opportunity-to-Proposal-Handoff-2026-10-15.md` for the CRM side.

## What is on M: — 63,738 files, 3,504 folders, 66.06 GB

| Top folder | Files | GB | What it is |
|---|---:|---:|---|
| Monthly Billings | 29,319 | 10.25 | invoices 2013–2026 (`2013-2025` 27,063; `Sent Invoices` 1,488; one folder per 2026 month) |
| Project Opportunities | 11,579 | 38.42 | every fee proposal: `Pending` 337, `Submitted to Client` 2,998, `Rejected` 3,135, `Word Documents for Awarded Projects` 5,101; `Fee Proposal Tracking.xlsx` |
| Project Management | 9,020 | 7.42 | one folder per awarded job by sector (signed PSAs, fee proposals, change orders), `Job Sheets` 2,391, `Project Specifications` 316 |
| Administration | 6,148 | 2.69 | `Timesheet Archive` 4,113 (2015–2019), `Transmittals` 1,556, `Shop Drawing Review` 331, reference procedures, notices, manuals |
| Correspondence | 4,551 | 1.49 | `04 Letters` 3,540 (1999–2021 project letters), `01 Fee Proposals` 460 (templates, T&Cs, rates, resumes, portfolios), reports, wordings |
| Legal Files | 1,620 | 2.41 | 25 folders (claim/litigation matters plus an archive), mostly strata actions |
| Recruitment | 864 | 0.51 | candidate resumes by role, 2020–2026 |
| LK | 478 | 0.93 | one senior engineer's working folder (`JB`), incl. an EGBC practice-review file |
| Business Development | 75 | 1.92 | brochures (with Photoshop working files), corporate portfolio, BizBrain BD reports, Leduc RFP |
| Meeting Minutes | 58 | 0.02 | Engineering, Construction and Drafting department minutes 2021–2023 |
| Field Review Reports | 14 | 0.00 | field-review procedures, booking notes, inspection tracking |
| Contact Information | 8 | 0.00 | client/site contact lists, PLI distribution list |
| Archiving & Project Updates, root | 4 | 0.00 | — |

By type: PDF 46,721 · DOCX 5,960 · DOC 5,501 · MSG 2,260 · JPG 555 · DWG 516 · XLSX 384 · EML 261 · ZIP 64 (6.12 GB).
By year modified: ≤2009 3,701 · 2010–14 4,130 · 2015–19 21,636 · 2020 2,879 · 2021 3,350 · 2022 4,436 · 2023 8,716 ·
2024 6,641 · 2025 5,888 · 2026 2,361.

## Classification (every file assigned once: 63,738 of 63,738)

| Class | Files | GB | Areas |
|---|---:|---:|---|
| **(a) Knowledge to ingest** | 1,174 | 2.63 | `Correspondence\01 Fee Proposals` (templates, T&Cs, charge-out rates, indemnification notes, reference notes, past templates, company introductions, proposal resumes, portfolios) 454; `Project Management\Project Specifications` 315; `Correspondence\EOR Letters` 78, `Wordings` 54, `07 Notices` 8; `Administration` reference/notices/templates/guides 120; `Meeting Minutes` 58; `Field Review Reports` 14; `Business Development` 73 |
| **(b) Structured data / records** | 54,388 | 57.06 | `Project Opportunities` (the win/loss record) 10,568; `Monthly Billings` 29,303 (Deltek is the authority for money; not ingested); `Project Management` job folders and `Job Sheets` 8,684; `Correspondence\04 Letters` 3,532, `05 Reports` 280, change orders, POs, responses; `Administration\Transmittals` 1,556, `Shop Drawing Review` 331 |
| **(c) Private / HR / legal — listed, not ingested** | 7,076 | 6.24 | `Administration\Timesheet Archive` 4,113 (12 in PAYROLL folders); `Recruitment\Resumes` 862; `Legal Files` 1,617 in 25 folders; `LK\JB` 476; `Contact Information` 8 |
| **(d) Junk / support** | 1,100 | 0.13 | CAD hatch and font files bundled with drawings in `Rejected` 960; equipment manuals 21; Thumbs.db 71, Office lock files 50, shortcuts 8, temp 7 |

Duplicates (estimate, by identical name **and** size, no hashing): 1,515 groups, 2,124 redundant copies, 1.71 GB —
1.27 GB of it in `Project Opportunities`. Within the knowledge set the extractor's text comparison dropped 63 exact and
19 near duplicates (e.g. `01 Fee Proposals\01 Fee Proposals\Company Introduction` repeats `Company Introduction`).

## (a) What was ingested into the knowledge base

569 files extracted on FS01, 569 of 569 read. 20 were scans or near-empty (no text layer: most signed contracts in
`Indemnification Notes`, the name-change notice, two brochures) and 82 were duplicates, leaving 467 documents in
seven sources (`--kind Document --licence Own`, author KOR Structural):

| Source | Docs | Passages | Claims |
|---|---:|---:|---:|
| 135 KOR BizBrain BD Reports and Leduc RFP Pursuit 2026 | 17 | 369 | 252 |
| 136 KOR Company Introductions, Portfolios and Proposal Resumes | 125 | 1,020 | 764 |
| 137 KOR Contract Indemnification and Liability Positions | 21 | 549 | 242 |
| 138 KOR Department Meeting Minutes 2021-2023 | 32 | 313 | 221 |
| 139 KOR Fee Proposal Templates, Terms, Rates and Reference Notes | 78 | 483 | 316 |
| 140 KOR Office, Field Review and Administration Procedures | 101 | 296 | 165 |
| 141 KOR Structural Specification Templates | 93 | 756 | 297 |
| **Total** | **467** | **3,786** | **2,257** |

Claims were written in session by 13 agents to `Desktop\BD\Books\claims\mgmt-2026-10-18\AGENT-INSTRUCTIONS.md`
(KOR's documents describe how KOR works and outrank books; no contact details, nothing about an individual's pay,
health or discipline). Merged with the existing validator (0 problems), 132 variant concept slugs folded, imported
2,257 accepted / 0 rejected, embedded. A mechanical sweep of all 2,257 statements found 0 phone numbers and 1 email
(the published inspections booking address). Relations pass E: 308 concepts (5,335 claims) exported to
`Desktop\BD\Books\relations-2026-10-18e`, linked by 10 agents, **1,011 relations imported / 0 rejected** (files hold 622
Refines, 302 Supports, 95 Contradicts; the 8 files-vs-imported difference is pairs the store already held). Store totals
after: 44,446 claims, 15,059 relations.

Source texts: `Desktop\BD\Management\sources-2026-10-18\` (with `_dropped.tsv` listing every skipped file and why).

**Not yet ingested from (a), and why:** `Correspondence\04 Letters` (3,540 project letters 1999–2021) and
`05 Reports` (280) are project-specific: high volume, low policy content; a sampled pass would find KOR's standard
positions in letters. Scanned contracts need OCR, which FS01 lacks.

## (b) Structured data

- **Win/loss record**: built — see the companion doc. 4,785 pursuits from 6,817 folder, document and tracker records.
- **`Fee Proposal Tracking.xlsx`**: `CAD Projects` 702 rows and `USA Projects` 87 rows (2020–2026), with status, RFP,
  due, submitted and accepted dates, PM, co-signer, client, architect, GFA, proposed and awarded fee, and comments. The two
  `In Pursuit & Won 2023/2024.xlsx` workbooks are per-PM pivots of the same tracker (duplicates).
- **`Project Management\Job Sheets`** (2,391 PDFs, one per job phase) and **`00 Old Database\JOB DATABASE.accdb`**: a
  job register that predates Deltek; not read here (Access, and Deltek holds the jobs).
- **`Monthly Billings`**: Deltek `LedgerAR` is the system of record; nothing here should be re-keyed.

## (c) Private, HR and legal — listed for Ian's decision, contents not opened

| Area | Files | Contents (from names) | Proposed handling |
|---|---:|---|---|
| `Administration\Timesheet Archive` | 4,113 | weekly timesheets 2015–2019 per person; 12 payroll sheets | leave; metrics only (HR-FINDINGS) |
| `Recruitment\Resumes` | 864 | candidate resumes by role (BIM 345, EIT 302, Systems IT 47, Admin 37, Site Inspector 35, Not Reviewed 33, CAD 29, UBC Co-op 27, Engineer 9); one letter of offer | leave; pipeline counts only |
| `LK\JB` | 478 | a senior engineer's working folder: field reviews, project files, **EGBC practice-review file SR24-023 (2024, 34 files)** | Ian decides; the EGBC file is regulatory/discipline |
| `Legal Files` | 1,620 | 25 folders, mostly strata building actions (e.g. 30339 Ultra Urban, 31103 Northwest, 30553 Evolve) | privileged; a claims-history summary (matter, year, project, status) would serve the COO/risk seat if Ian approves |
| `Correspondence\05 Reports\Archives` | 2 | APEGBC complaint memos, 2004 and 2005 | Ian decides |
| `Contact Information` | 8 | client/site contact lists, PLI distribution list | contact data; not ingested |

No file whose **path** indicates a SIN, void cheque, direct deposit, TD1 or medical record among the 34,419 files outside
`Monthly Billings` (`tools/ManagementShare/hr.py`; the invoice folders were not searched). Names are not contents: payroll sheets and resumes may still hold such data, which is why they were
not opened.

**No HR policy, handbook, org chart, salary or review file was found on M: by path.** `tools/ManagementShare/hr.py`
searched the 34,419 paths outside `Monthly Billings` for about 60 HR terms (handbook, policy, payroll, salary,
compensation, benefit, review, org chart, job description, onboarding and others). It matched only the areas above.
A search by name can miss a file with an unhelpful name, so this is "not found by path", not proof of absence. A depth-1 listing of FS01's other shares, run
on FS01, shows where they probably are: `E:\Partners` (Agreements & Resolutions, Meeting Minutes, Project Metrics),
`E:\Finance\01 Kor Structural (BC Partnership)`, and per-user `E:\Home` folders. Not walked; out of this job's scope.

## (d) Junk

1,069 support/temp files (30.8 MB) plus 21 equipment manuals and 4 purchasing files. Safe to ignore. Not deleted.

## Housekeeping found along the way

- 64 ZIPs hold 6.12 GB, the largest being drawing sets inside `Submitted to Client` (Townline International Plaza 0.88 GB
  and 0.78 GB; Pacific Beacon 0.86 GB).
- The extractor's 23 errors: 18 `.doc` files with malformed piece tables (`Awarded`), 2 locked files in `Pending`, and 3
  corrupt PDF/DOCX.
- Outdated or mis-attributed template text surfaced by the claim writers: a 2017 spec still citing the 2006 Alberta code
  under the old firm name; a 2021 spec naming another firm (Glotman Simpson) as "the Engineer"; an Abbotsford experience
  sheet still carrying the Surrey text; the San Diego Electra tower given as 50 storeys in one profile and 43 in another.
