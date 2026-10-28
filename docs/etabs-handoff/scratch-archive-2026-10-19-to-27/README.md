# PDF Ingestor scratchpad, 2026-10-19 to 10-27: the archive

The PDF Ingestor session (d8312bb3) worked from a session scratchpad under `%TEMP%\claude`. Ian's rule (relayed by
operations-ee, 10-27): nothing that holds a decision, a measurement, a working script or evidence lives only in a scratchpad.
This folder is that scratchpad's keepable part, copied verbatim with its relative paths, on 2026-10-27.

## What was picked (tonight/archive_pick.py states the rule in code)

Every script (`.py .ps1 .cs .sql .lsp .sh .bat`) and every text file (`.txt .log .csv .md .json .out .html .tsv`) of at most
1 MB, at the scratchpad's top level or one directory down: each run's ledger, log, report and notes, the oversight-board
writers, the probes, the measurement scripts. 932 files, 15.2 MB, scanned before commit for passwords, keys, tokens and
private keys (0 files) and for personal terms (0 files).

## What was NOT picked, and where it is

- **Models, renders, DXFs and per-job run output** (about 39 GB): regenerable from the commit named in each run's log and
  ledger. Left in the scratchpad.
- **Copied takeoff bins** (`cli-*`, `tonight/takeoff-bin*`; about 3.9 GB): builds of commits that are in git.
- **Griffin's KorConcrete** (`griffin/installer|pkg|unpacked`, `kc/`, `kc-data-*`, `kccond/`): his software, its runtime data,
  and text read out of it. It never goes into our repo. The review that came out of it is `docs/KOR-KorConcrete-*-2026-10-20.*`;
  the review tooling (`kc-tools/`, `kcscripts/`, `griffin/tools/`) is here.
- **Text over 1 MB** (9 files, mostly `ledger-sheets.csv`, the per-sheet table each gate rewrites): left in place.
- **The census and drawing mirror** live in `%TEMP%\kor-drawings`, not here; they are a cache, re-made from the share.

## Where tonight's conclusions are

Commits 0f3e4ee4, 16b7ca17, 66b56d4e, 36d12618, d4352fc0, b94b8d33, e6fdbc8e; the evidence under `docs/etabs-handoff/corpus/*-2026-10-27-*`;
KorStandards `oversight` (boards 15, 43, 44, 49, 52, 53, 54; Mistakes 50-70 by the PDF Ingestor).

The repo ignores `*.log`; the 143 `.log` files here (gate and run logs - evidence) were added with `git add -f` for this folder only.
