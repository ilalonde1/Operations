# BD Research Harvest Importer

Imports the overnight BD research payloads into:

- `opportunities.CanonicalOrg`
- `opportunities.CanonicalOrgEnrichment`
- `opportunities.MajorProjectsInventory`

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
