# BD Canonical Org Dedup

Disposable console tool for planning and optionally committing duplicate `opportunities.CanonicalOrg` merges.

The tool is dry-run by default. It writes `dedupe-plan.csv` for review on every run.

```powershell
dotnet run --project tools/BdCanonicalDedup -- --db "<connection-string>"
dotnet run --project tools/BdCanonicalDedup -- --commit --merge-dba --out tools/BdCanonicalDedup/output
dotnet run --project tools/BdCanonicalDedup -- --create --kind Architect --name "Example Architecture" --website "https://example.com/"
```

Options:

- `--db <connstr>`: Opportunities DB connection string. If omitted, reads `KOR_OPPORTUNITIES_OPPORTUNITIESDB`.
- `--commit`: execute merge transactions. Without this, the tool only reads data and writes the plan CSV.
- `--pairs <file.csv>`: merge exactly the `LoserId,SurvivorId` rows in the file (`#` lines and a
  header are skipped). The same four gates used by default mode run per pair and
  refuse into `output/rejected-pairs.csv`: name similarity (`NormalizeForFuzzyMatch`, bypassable by
  an entry in `dedup-non-similar-allowlist.d/*.csv` with a reason), both rows carrying a Deltek id
  (never — two billing entities), survivor is a branch row while the loser is the parent (the
  direction is backwards), and names asserting different countries. The last three are not
  allowlist-overridable. Reviewed batches live beside this README as `<topic>-merge-<date>.csv`.
- `--create --kind <kind> --name <display-name> [--website <url>]`: safe manual canonical-org
  create/attach path. It calls `CanonicalOrgResolver.ResolveAsync` with create enabled, so an
  existing strict/fuzzy/domain match is attached and a new row is inserted only when the resolver
  finds none.
- `--merge-dba`: also group `Person DBA: Company` variants by the post-DBA business name.
- `--backfill-fuzzy-key`: rewrite `FuzzyNormalizedName` on **every** row (893k, retired included)
  from the live normalizer. ⚠ It silently undoes deliberate overrides such as `mcw` on 71528; prefer
  repairing the rows `org_fuzzy_key_stale` lists, from that report's own `ExpectedFuzzy` column.
- `--out <dir>`: directory for `dedupe-plan.csv`. Defaults to `tools/BdCanonicalDedup/output`.

Default mode (no `--pairs`) groups live orgs by `NormalizeAggressiveKey`, then applies the same
per-pair gates before writing `dedupe-plan.csv` or committing a group. Rejected pairs go to
`output/rejected-pairs.csv`, and any group with a rejected loser->survivor pair is skipped.
`dedup-never-merge.csv` is checked in both directions before planning or committing, so deliberate
splits such as *Continuum Architecture* / *Continuum Partners* stay split.

A merge **deletes** the loser row; the id mapping survives in `opportunities.CanonicalOrgMerge`,
the loser's name, domain and notes do not. Allowlist files are read from the **build output**, so
a new `dedup-non-similar-allowlist.d/*.csv` or `dedup-never-merge.csv` change does nothing until
`dotnet build` copies it.

Each committed duplicate group runs in its own transaction with `XACT_ABORT ON`. Failed groups are rolled back and logged while the remaining groups continue.
