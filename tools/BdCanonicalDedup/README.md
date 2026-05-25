# BD Canonical Org Dedup

Disposable console tool for planning and optionally committing duplicate `opportunities.CanonicalOrg` merges.

The tool is dry-run by default. It writes `dedupe-plan.csv` for review on every run.

```powershell
dotnet run --project tools/BdCanonicalDedup -- --db "<connection-string>"
dotnet run --project tools/BdCanonicalDedup -- --commit --merge-dba --out tools/BdCanonicalDedup/output
```

Options:

- `--db <connstr>`: Opportunities DB connection string. If omitted, reads `KOR_OPPORTUNITIES_OPPORTUNITIESDB`.
- `--commit`: execute merge transactions. Without this, the tool only reads data and writes the plan CSV.
- `--merge-dba`: also group `Person DBA: Company` variants by the post-DBA business name.
- `--out <dir>`: directory for `dedupe-plan.csv`. Defaults to `tools/BdCanonicalDedup/output`.

Each committed duplicate group runs in its own transaction with `XACT_ABORT ON`. Failed groups are rolled back and logged while the remaining groups continue.
