# Codex prompt — SF (and any address-keyed) funnel source should name projects by composite address

One change in `Kor.Opportunities.Data/Ingestion/Providers/CaSocrataMajorProjectsInventoryProvider.cs`. No `dotnet build`/`test` (env hangs); Claude verifies the build + deploys.

**Problem.** The SF Socrata source was configured `projectNameColumn=description`, so SF permit rows got named by their raw lowercase permit text ("to erect 60 stories, 4 basement, type ia, 109 units..."). SF's address is split across separate columns (`street_number`, `street_name`, `street_suffix`) with no single combined field, so config-only can't produce a clean name. Add composite-address support so a source can be named by joining several columns.

**Change (in `MapRowAsync`):**

1. Add a config key **`addressColumns`** = a comma-separated list of column names. After the existing `address` line:
```csharp
var address = Read(row, sourceConfig, "addressColumn", "address", "site_address", "street_address", "location", "full_address");
```
add: if `addressColumns` is configured, build a composite address by reading each listed column from `row` (use the same `TryGetString` the provider already uses), joining the non-empty values in order with single spaces, collapsing runs of whitespace, and trimming. If the composite is non-empty, use it as `address` (it overrides the single-column read). Example helper:
```csharp
var addressColsCfg = Get(sourceConfig, "addressColumns");
if (!string.IsNullOrWhiteSpace(addressColsCfg))
{
    var parts = new List<string>();
    foreach (var col in addressColsCfg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (TryGetString(row, col, out var v) && !string.IsNullOrWhiteSpace(v))
            parts.Add(v.Trim());
    }
    var composite = System.Text.RegularExpressions.Regex.Replace(string.Join(" ", parts), @"\s+", " ").Trim();
    if (composite.Length > 0) address = composite;
}
```

2. Reorder the `projectName` fallback chain so the (now possibly composite) address is preferred over the raw description — insert `address` immediately after the `projectNameColumn` read and before `DescriptionLead`:
```csharp
var projectName = FirstNonBlank(
    Read(row, sourceConfig, "projectNameColumn", "project_name", "project", "name"),
    address,                         // clean street address (composite when addressColumns set)
    DescriptionLead(description),
    address is null ? null : $"{type} - {address}",
    permitNumber);
```
Sources that map `projectNameColumn` (San Jose CKAN = FOLDERNAME, San Diego CSV = GIS_ADDRESS) are unaffected — their `projectNameColumn` read still wins. Only SF (which will have `projectNameColumn` removed and `addressColumns` set, via migration 219) changes: it gets named "50 01st St", "546 Howard St", etc. The description still flows into ScheduleNotes via `descriptionColumn`.

After this is deployed, Claude applies migration 219 (removes SF `projectNameColumn`, adds `addressColumns=street_number,street_name,street_suffix`).
