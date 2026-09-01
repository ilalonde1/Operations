## Shape Chosen

Publishing now lives behind `takeoff publish` and the compiled `JobPublisher` flow:

- `PublishDiscovery`: finds the job, ETABS model folder, DXF folder, and engineer reference; refuses ambiguous references.
- `JobPublisher`: orchestrates discovery, staging, generation, invariant checks, summaries, explainer gates, and landing.
- `PublishSummary`: writes `KOR-<label>-SUMMARY.pdf` from `DxfToEtabsReport` and `E2kModelContents`, with the 8/6/4/3/2 one-page loop.
- `PublishExplainers`: checks dossier applicability, stale source files, source/PDF claims, and withdraws stale landed explainers on failed or skipped dossier publishes.
- `PublishExternalTools`: the only shell-out wrapper, for `Format-BdWebPdf.ps1` and `pdfinfo.exe`.

`E2kModelContents` now carries headers and openings, so the publish summary and explainer gate do not re-count the saved `.e2k` text for those missing numbers.

## Carried Across

Carried across from `tools/Publish-EtabsModel.ps1`:

- project/model/DXF/reference discovery;
- refusal without `KOR_ENGINEERINGTOOLS_STANDARDSDB` or `--rules-db`;
- staging before landing;
- per-job one-page summary PDF;
- verbatim report findings, shortened only to fit one page;
- dossier job-number applicability gate;
- dossier/one-pager claim gate before copy;
- stale explainer withdrawal;
- stale landed-owned-file check;
- superseded `QUESTIONS-for-Andrea.xlsx` withdrawal;
- `--tower`, `--top-storey`, `--variant`, `--skip-dossier`, `--per-building`, `--drop-storeys`, `--land`.

## Deliberately Not Carried Across

I did not keep the script's regex-derived `.e2k` counts. The summary uses `report.SavedModel`; the explainer count gate uses `E2kDocument.ReadContents()` for other named jobs.

I did not update historical audit records that mention `Publish-EtabsModel.ps1`. Those are evidence of past failures, not current operator instructions. Current operator docs now name `takeoff publish`.

## Verification

Per the brief, I did not run `dotnet build` or `dotnet test`, and I did not touch the 31168 job share or publish anything.

Static checks run:

- `git diff --check`
- targeted `rg` checks for current docs still naming `Publish-EtabsModel.ps1`
- targeted API/reference checks for `E2kModelContents` and `JobPublisher`
