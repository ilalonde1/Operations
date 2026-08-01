# BdOrphanOrgPurge

Dry-run first purge helper for unreferenced junk canonical organizations.

Default mode writes `output/orphan-orgs.csv` and performs no deletes.

```powershell
dotnet run --project tools/BdOrphanOrgPurge
dotnet run --project tools/BdOrphanOrgPurge -- --out output
dotnet run --project tools/BdOrphanOrgPurge -- --commit
```

Required environment:

- `KOR_OPPORTUNITIES_OPPORTUNITIESDB` or `KOR_BD_OPPORTUNITIESDB`

The commit path deletes aliases first, re-checks all hard-coded canonical-org reference columns inside the transaction, and skips any org that has become referenced.
