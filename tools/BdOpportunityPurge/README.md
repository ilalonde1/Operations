# BdOpportunityPurge

Dry-run first purge helper for auto-ingested, untouched BD opportunities rejected by `StructuralRelevanceGate`.

Default mode writes `output/purge-opportunities.csv` and performs no deletes.

```powershell
dotnet run --project tools/BdOpportunityPurge
dotnet run --project tools/BdOpportunityPurge -- --out output
dotnet run --project tools/BdOpportunityPurge -- --commit
```

Required environment:

- `KOR_OPPORTUNITIES_OPPORTUNITIESDB` or `KOR_BD_OPPORTUNITIESDB`

The commit path repeats the same hard safety guards used by the candidate query before deleting.
