# GovCan Engineering Awards Import

Imports the dev-box JSONL export of federal engineering commodity-code contracts into `opportunities.OpportunityAwards`.

## Usage

```powershell
dotnet run --project tools/GovCanEngineeringImport -- --dry-run
dotnet run --project tools/GovCanEngineeringImport -- --file "C:\VIsual Studio Projects\KOR-GovCan-Import\engineering-awards.jsonl" --db "<connection-string>"
```

Options:

- `--file <path>`: JSONL input file. Defaults to `C:\VIsual Studio Projects\KOR-GovCan-Import\engineering-awards.jsonl`.
- `--db <connstr>`: Opportunities DB connection string. If omitted, reads `KOR_OPPORTUNITIES_OPPORTUNITIESDB`.
- `--dry-run`: streams and maps the file without opening the database or writing rows.

Live imports look up `opportunities.OpportunitySources.Name = 'GovCanada_EngineeringServices'`, then upsert each mapped award through `SqlOpportunityAwardStore` with a `CanonicalOrgResolver` so buyers and vendors are canonical-linked. Re-running is idempotent on `(OpportunitySourceId, ExternalReference)`.
