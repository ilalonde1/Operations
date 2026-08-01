# BD Research Harvest Importer

Imports the overnight BD research payloads into:

- `opportunities.CanonicalOrg`
- `opportunities.CanonicalOrgEnrichment`
- `opportunities.MajorProjectsInventory`

Current payload roots under the base directory:

- `KOR-Contractor-Research`
- `KOR-PublicSector-Research`
- `KOR-Indigenous-Development`
- `KOR-BC-Development-Pipeline`
- `KOR-LA-Market`
- `KOR-PacNW-Market`

## Usage

```powershell
dotnet run --project tools/BdResearchImport -- --base "C:\VIsual Studio Projects" --db "<connection-string>"
dotnet run --project tools/BdResearchImport -- --dry-run
```

Options:

- `--base <dir>`: root containing the research payload directories. Defaults to `C:\VIsual Studio Projects`.
- `--db <connstr>`: Opportunities DB connection string. If omitted, reads `KOR_OPPORTUNITIES_OPPORTUNITIESDB`.
- `--dry-run`: reads payloads and logs planned org, enrichment, and project writes without touching the database.

The importer is idempotent: orgs are upserted through `SqlCanonicalOrgStore`, enrichments through `SqlEnrichmentTrackingStore.RecordAttemptAsync`, and projects through a locked update-then-insert on `(Province, SourceKey)`.

US market project costs are stored in CAD using a fixed 1.36 USD/CAD multiplier, with the original USD value retained in `EstimatedCostText`.
