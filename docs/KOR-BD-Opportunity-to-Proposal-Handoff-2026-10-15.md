# Opportunity → Proposal: handoff to the BD Brain session, 2026-10-15

Written by the Leduc session (operations-57) for the session building the BD Director window
(operations-2b). It records the proposal process that was run by hand on 13–15 October for the City
of Leduc West Campus RFP (2026-RFP-IP-061, submitted 2026-10-15), Ian's decisions on how the
platform should carry it, and the scripts that already exist. **Sections 1 and 2 are Ian's
decisions. Do not re-derive them.**

---

## 0. Read first

1. `CLAUDE.md` rules 1, 2, 8 and 9. This process touched every one of them.
2. Memory `reference_fee_proposals_live_in_project_opportunities_pending` — where the team writes
   proposals today and how a draft's state was read mechanically.
3. `docs/KOR-BD-Workspace-Handoff-2026-10-14.md` — the BD workspace state this builds on.

Searched before writing: `docs/BD-Submissions-2026-07/` and the July RFPQ26 submission tracker are
manual one-off documents; `M:\Project Opportunities\Fee Proposal Tracking.xlsx` is a hand-kept list;
`opportunities.Opportunities.Status` knows New (1) and auto-expired (7); Deltek `PR.Stage` knows the
outcome. Nothing in the platform carries an opportunity from "we are bidding" to "submitted".

## 1. Ian's decisions (settled, 2026-10-15)

- **The BD Brain owns the pursuit lifecycle.** The stage is platform state, set in the app.
  **Do not retrofit to the existing folder structure** on `M:\Project Opportunities\` — no scraping
  of `Pending\` / `Submitted to Client\` / `Rejected\`, no stage inferred from where a folder sits.
  A folder path may be stored as a link on the pursuit when someone pastes it. Nothing more.
- **It shows the opportunity moving to a proposal, and the stage reads "Pending"** while the
  proposal is being prepared.
- **Each pursuit is assigned to a person.** Leduc is assigned to **Islam Shabana**. Assignee is a
  KOR staff member; the Pending tab filters by assignee.

## 2. The lifecycle to model

| Stage | Meaning | Set by |
|---|---|---|
| Opportunity | a row in `opportunities.Opportunities`, not yet pursued | ingestion |
| **Pending** | KOR has decided to bid; a pursuit exists, assigned to a person, due date fixed | user, in the app |
| Submitted | the proposal went in; date/time and the file name recorded | user, in the app |
| Won / Lost / No decision | outcome; Deltek `PR.Stage` can confirm Won later | user, Deltek |

Pursuit record (new, `opportunities` schema): opportunity id · assignee · stage · due (UTC, from
`SubmissionDeadlineUtc`, editable) · RFP number · submitted at · submitted file name · optional
folder link · notes. One pursuit per opportunity. Stage changes are logged (who, when).

**Pending tab, one row per pursuit:** client · project · RFP number · assignee · due in local time
with a countdown · stage · submitted at · link. Sort by due. Red when due < 24 h and not submitted.
"Mine" filter on assignee. Director's view: count of Pending by assignee and by due week.

### Built on, 2026-10-15

The pursuit record already exists as `opportunities.CrmEngagements`, with its assignee,
stage history and opportunity link. 9g adds Pending as the display name of its existing
Drafting stage, submitted file name and pasted folder link, and a Pending bids tab with
Mine, assignment, opportunity editing and Mark submitted. A bid is `OpportunityId IS NOT NULL`;
relationship pursuits are counted separately. Due remains `Opportunities.SubmissionDeadlineUtc`
and submitted time comes from stage history. Leduc opportunity rows 39322 / 39332 are a
cross-source duplicate for ingestion to settle; 39322 is the row to use here.

## 3. Actions the platform should offer later — each exists as a script today

These are not part of the stage model. They are what the Leduc session did by hand, promoted into
`tools/` so the next build starts from code, not from memory.

1. **Compliance check** of a draft against the RFP: `tools/ProposalReview/inspect_docx.py` maps a
   `.docx` (paragraphs, tables, text boxes); `docx_meta.py` reads author, last save, revision, pages,
   and the Word lock-file owner. The checklist that held for Leduc is in §4. ⛔ Quote the RFP; never
   turn "such as 11-point Arial" into a rule. A reviewer caught that on 10-15 and was right.
2. **Org chart**: `tools/New-ProposalOrgChart.ps1 -Spec <json> -OutDir <dir> -BaseName <name>` →
   `.vsdx .pdf .svg .png`. Example spec `tools/New-ProposalOrgChart.example.json` (the Leduc team).
   Project team only; the five scored positions in bold; insert the SVG into Word.
3. **Fee reconciliation** per RFP 7.6: `base × (1+burden) = excl.OH`; `× (1+OH%) = incl.OH`;
   `× (1+profit%) = incl.OH&profit`; one overhead % and one profit % per proponent, stated above the
   table. Flag a table that implies a different OH than the one stated (Leduc: stated 24%, implied
   60–179% by person), and a burden % that cannot cover CPP+EI+WCB+vacation+stat (5% cannot).
4. **Scripted edits on a versioned copy**: `tools/ProposalReview/build_leduc_example.py` — every edit
   logged, a missing target raises, output is a new file beside the original, rendered through Word
   COM and looked at before shipping. Reuse the helpers, not the Leduc specifics.
5. **Submission**: submit the PDF, named `<RFP no> - KOR Structural - <document type>.pdf`
   (resumes as `… - Resumes.pdf`); tick every addendum on the portal form; upload with a buffer —
   the portal clock counts receipt.

## 4. The checklist that held (from 2026-RFP-IP-061 Part III; generalise per RFP)

- Cover: RFP title/number · proponent name and contact · date of submission.
- Sections in the RFP's order: Executive Summary · Narrative · Team · References · Methodology ·
  Fee (appendix) · Other.
- Page limit and its exclusions; count body pages from the rendered PDF, not Word's total.
- Executive summary: services · advantages · preparer (name, title, address) · signer empowered to
  finalise (name, **signature**, title, address) · assumptions · **AI disclosure** — about the
  proposal's preparation, not design practice. Ian's wording: *"Kor Structural operates a
  proprietary, in-house operations platform (KOR-Operations) that incorporates AI language-model
  services. It was used in a supporting role in preparing this proposal: …"* Keep "incorporates AI";
  without it there is no disclosure.
- Team: a resume per member · an organizational chart · the five positions named (SMT, PMT, Design
  Lead, BIM Lead, Construction Contract Administration) · succession for SMT and PMT · the team
  leader's $65M collaborative-delivery project mapped to IPD mechanics.
- References ≥ 3, each clearing a bar: ≥ $65M · a publicly operated organisation · a recreation
  centre · ≤ 10 years · none from the buyer · name/address/phone · **named contact with title** ·
  scope, **date**, duration, relevance · uninflated values with years.
- Fee per §3 item 3. Rates for everyone; evaluation on the five positions only.
- Addenda acknowledged on the portal.

## 5. Traps paid for this week

- **PowerShell `$spec` is `[string]$Spec`.** Case-insensitive names; the parsed object was coerced to
  a string; every property read null; zero errors; four empty shapes. Name the object differently
  from every parameter.
- **Floating Word tables** (`w:tblpPr`): inserted paragraphs flow around them and land below.
  Remove `tblpPr`, place the table explicitly, set `tblInd` to the body indent.
- **Text boxes** carry their text twice (DrawingML and VML); runs are not direct children; the outer
  anchor paragraph iterates the nested text. Walk `w:t`, take its own `w:p`, touch that one only.
- **New runs get the Normal font.** Copy `rPr` from a neighbouring run.
- **A figure orphans the next heading.** `pageBreakBefore` on the heading.
- **The docx skill's `soffice.py` fails on Windows** (`socket.AF_UNIX`). Render through Word COM:
  `ExportAsFixedFormat(pdf, 17)`; `ComputeStatistics(2)` is the true page count.
- **Draft status comes from the file**, never from memory: `core.xml` + lock file + `pandoc` diff
  showed three authors, two forks and a 24-minute review without asking anyone.

## 6. Files

- `tools/New-ProposalOrgChart.ps1` · `tools/New-ProposalOrgChart.example.json`
- `tools/ProposalReview/` — `docx_meta.py` · `inspect_docx.py` · `build_leduc_example.py` · `README.md`
- Worked example on the share: `\\KOR-FS01\Management\Project Opportunities\Pending\City of Leduc
  Recreation Facility JM IS (DUE OCT 15)\Leduc RFP\` — drafts, the IL/IL2 versions, the final named
  PDF, the org chart in four formats, the resumes zip.
