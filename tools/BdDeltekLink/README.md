# BdDeltekLink

Backfills `opportunities.CanonicalOrg.ClendorClientId` for curated BD organizations by fuzzy matching unlinked canonical orgs against active Deltek Clendor clients.

Default mode is dry-run: it writes `link-plan.csv`, `review.csv`, and `dedup-candidates.csv` under `--out` without updating the database.

```powershell
dotnet run --project tools/BdDeltekLink -- --kinds Buyer,Architect --out output
dotnet run --project tools/BdDeltekLink -- --commit
dotnet run --project tools/BdDeltekLink -- --pairs reviewed.csv
```

Required environment:

- `KOR_OPPORTUNITIES_OPPORTUNITIESDB` or `KOR_BD_OPPORTUNITIESDB`
- `KOR_BD_DELTEK_DSN`
- `KOR_BD_DELTEK_USER`
- `KOR_BD_DELTEK_PWD`
- optional `KOR_BD_DELTEK_CATALOG`
