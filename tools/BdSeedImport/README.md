# BD Seed Import

One-off importer for `KOR Structural BD Tracking 2026.xlsx` into KorOpportunitiesDb; this project is intentionally outside the main solution and will be deleted after the import lands.
Required env vars: `KOR_BD_OPPORTUNITIESDB`, `KOR_BD_DELTEK_DSN`, `KOR_BD_DELTEK_USER`, `KOR_BD_DELTEK_PWD`; optional `KOR_BD_DELTEK_CATALOG`.
Dry run: `dotnet run --project tools/BdSeedImport/BdSeedImport.csproj -- --xlsx "C:\Users\ilalonde\Desktop\KOR Structural BD Tracking 2026.xlsx" --dry-run`.
Live run: `dotnet run --project tools/BdSeedImport/BdSeedImport.csproj -- --xlsx "C:\Users\ilalonde\Desktop\KOR Structural BD Tracking 2026.xlsx" --actor Ian`.
Writes `output/bd-import-log.txt` and `output/bd-import-review.csv` under `--output-dir` or `tools/BdSeedImport/output/`.
Do not add this project to `Kor.Operations.App.sln`; it is a disposable operator tool.
