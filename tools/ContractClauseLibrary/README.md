# ContractClauseLibrary

Every client contract document on the Projects share and in Project Opportunities, read, cut into
clauses and tagged by topic, so a new contract can be checked against what KOR has already signed.
Built 2026-10-28 for Rory's clause database and the FeeProposal builder's terms block. The findings
are in `docs/KOR-Contract-Positions-On-File-2026-10-28-web.pdf`; the data in `docs/contract-clause-library/`.

Searched before building: `tools/` had `ProposalReview` (Word-file editing) and nothing that reads
contracts; `docs/` had the Matthews and Starlight reviews only; the FeeProposal builder stores typed
blocks in `dbo.ProposalBlockTemplates` and has no terms block (`ProposalBlockType`, 15 members).

## The pipeline, in order

| Step | Script | Runs where | Does |
|---|---|---|---|
| 1 | `fs01-contracts-enum.ps1` | ON KOR-FS01 (`Invoke-CimMethod … Win32_Process Create`, DCOM) | Lists candidate files under `E:\Projects\Projects` and `E:\Management\Project Opportunities` to `C:\Windows\Temp\kor-contracts-enum.csv`. 23 min for 13,371 rows. `-Include` does not filter there; the gate is step 2. |
| 2 | `inventory_classify.py` | here | The contract-relevance gate (document types, size caps, contract token in the name OR a contracts folder), job + Deltek client, the per-job trail, the pull list. Writes `inventory.csv`, `jobs.csv`, `dropped.csv` (with reasons), `pull-list.txt`, `summary.txt`. |
| 3 | `fs01-contracts-zip.ps1`, `fs01-contracts-zip2.ps1` | ON KOR-FS01 | Zips the pull list; the second pass resolves names the listing wrote with U+FFFD by wildcard. One zip crosses the wire. |
| 4 | `extract_clauses.py` | here | PDF (PyMuPDF), .docx (python-docx), .msg (extract_msg) and .eml with attachments, recursively. Optional third argument: a folder of `<sha12>.txt` OCR / converted texts. Cuts numbered sections, falls back to paragraph chunks for Word auto-numbered documents, tags 19 topics. Writes `documents.csv`, `clauses.csv`, `text/`. |
| 4a | `ocr_batch.py` + `win_ocr.ps1` | here (PS 5.1) | Windows' built-in OCR for scanned PDFs (84 of 87 in the first run, 504 pages, 8 min). No install. |
| 4b | `doc_convert.ps1` | here (PS 5.1) | Legacy .doc → text through Word COM. Exact-extension filter and a short temp path, or Word fails. |
| 5 | `build_library.py` | here | Roles (executed / markup / draft / proposal / other), KOR paper vs client paper, version order and latest-on-file per job, the contract-shape test. Writes `library_clauses.csv`, `documents_roles.csv`, `positions.csv`, `job_topic_matrix.csv`, `summary.txt`. |
| 6 | `Import-ContractClauses.ps1` | here → KOR-APP01 | Loads step 5 into `dbo.ContractClauseSources` / `dbo.ContractClauses` in **KorTransmittals** (`db/KorTransmittals/011_ContractClauseLibrary.sql`), the database that holds the builder's `dbo.ProposalBlockTemplates`. Truncate-and-reload. `012_KorStandardTermsTemplate.sql` seeds the builder's "Terms" block from the Starlight wording. |
| look | `quality_look.py`, `positions_digest.py`, `clause_dump.py` | here | What the gate kept and dropped; the key clause per topic per signed document; full clause text for the documents that matter. Read these before believing a count. |

Run Python with `python -I` and keep the pulled files in their own folder: they are downloaded content.

## What it does not do

- It does not judge a clause. It locates, cuts and tags text; the reading is a person's.
- Role 'executed' comes from the file name or a DocuSign/Adobe Sign marker in the text. A signed scan
  named like a draft is 'draft' with `IsLatest` set. "IN WITNESS WHEREOF" proves nothing: it is in every draft.
- 'Latest on file' is by file date, so a copied file can carry a newer date than the signed one.
- The topic regexes are generous on `permits_approvals` and `suspension` (a suspended ceiling matches).
  The `limitation_of_liability` regex is tight and skips insurance clauses that quote policy limits.
- Three scanned PDFs and twelve old OLE files were unreadable; 89 files with an en dash in the path were
  zipped in a second pass and are still on FS01 (`C:\Windows\Temp\kor-contracts-2.zip`) at the time of writing.

## Numbers from the 2026-10-28 run

13,371 listed → 3,191 relevant → 1,957 selected → 1,955 pulled (1,866 + the 89-file supplement) → 2,653
document parts read (1,060 emails with attachments, 86 scans by OCR, 35 legacy .doc, 18 unreadable) →
5,175 clause rows in 18 topics across 126 jobs and pursuits → 54 with client paper on file (27 Deltek-named
clients); signed client paper = 8 distinct files (Wesgroup 2020 ×2 and 2022, Aquilini 2021, Beedie 2023,
India & Beech 2023 in four copies, one NDA, one file licence); 13 KOR fee proposals countersigned. Loaded
into KorTransmittals.
