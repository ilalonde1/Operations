# Codex prompt — CA major-projects funnel follow-ups (CEQAnet title fix + City of San Diego CSV)

Two independent changes in `Kor.Opportunities.Data`, plus one migration. No `dotnet build`/`dotnet test` — Claude verifies the build and deploys.

---

## Task 1 — Fix CEQAnet project titles (currently the SCH# bleeds into ProjectName)

File: `Kor.Opportunities.Data/Ingestion/Providers/CeqanetMajorProjectsInventoryProvider.cs`

**Problem.** The `/Search/Recent` page is a clean 5-column HTML table:
`SCH Number | Type | Lead/Public Agency | Received | Title`.
`ParseFilings` builds the title from the SCH#-anchor's link text (`anchorText`), but on that page the anchor text **is the SCH number**, so `filing.Title` (and therefore `ProjectName`) comes through as e.g. `2026060824` instead of the real project title.

**Fix.** Rewrite `ParseFilings` to parse the table **row-by-row** and map cells by position, which is what the page actually gives us:

- Iterate each data `<tr>…</tr>`. For each row, extract its `<td>…</td>` cells in order (regex `<td[^>]*>(.*?)</td>` with `Singleline|IgnoreCase`), run each cell through the existing `CleanText`.
- Keep a row only if it has **≥ 5 cells** and `cells[0]` matches `SchRegex`. De-dupe on the SCH# via the existing `seen` set.
- Map: `SchNumber = SchRegex.Match(cells[0]).Value`, `DocumentType = cells[1]`, `LeadAgency = cells[2]`, `ReceivedDate = cells[3]`, **`Title = cells[4]`**, `Description = null`, `County = null`.
- `SourceUrl`: pull the first `<a href>` inside the row (the SCH# link) and resolve it with the existing `AbsoluteUrl(baseUrl, href)`; fall back to `baseUrl` if none.
- **Keep the existing anchor-based + plaintext-`SchRegex` fallback paths intact**, but only run them when the table-row pass produced **zero** filings (resilience if the page markup changes). Do not delete `ExtractTitle`/`ExtractAfter`/`ExtractDescription`/`SurroundingSegment` — the fallback still uses them.

Everything downstream (`MapFilingAsync`, `IsLaneProject`, `StructuralRelevanceGate`, upsert) stays as-is — with the real title flowing in, the lane/relevance gates will now correctly keep buildings and drop transportation-plan/EIR noise.

---

## Task 2 — Add a CSV ingestion path for the City of San Diego feed

File: `Kor.Opportunities.Data/Ingestion/Providers/CaSocrataMajorProjectsInventoryProvider.cs`

The City of San Diego has **no Socrata/CKAN API** — only flat CSVs on `seshat.datasd.org`. Add a third `kind` to this provider alongside `socrata`/`ckan` so the existing `MapRowAsync` + gate + upsert pipeline is reused unchanged.

In `FetchRowsAsync`, before the Socrata fallthrough, add:

```csharp
if (string.Equals(kind, "csv", StringComparison.OrdinalIgnoreCase))
{
    await foreach (var row in FetchCsvRowsAsync(source, sourceConfig, ct).ConfigureAwait(false))
    {
        yield return row;
    }
    yield break;
}
```

Add `FetchCsvRowsAsync`:

- GET `source.BaseUrl` (`HttpCompletionOption.ResponseHeadersRead`), `AddCommonHeaders(request, sourceConfig)`, then `request.Headers.TryAddWithoutValidation("Accept", "text/csv,*/*;q=0.8")`. `EnsureSuccessStatusCode()`.
- Read the body into a capped `MemoryStream` using the **same byte-cap loop already in `ReadJsonDocumentAsync`** (honor `_maxBytesPerResponse`, same overflow `InvalidOperationException` message but say "CSV"); decode UTF-8 → string.
- Parse with the shared parser: `var rows = Kor.Opportunities.Core.Ingestion.CsvParser.Parse(csv);`. If `rows.Count < 2`, `yield break`.
- Build header names from **raw** `rows[0]` (trim each; strip a leading BOM `﻿` from the first cell). **Do NOT use `CsvParser.NormalizeHeaderRow`** here — it lowercases and strips underscores, which would break the exact column-name matching the config relies on (`APPROVAL_VALUATION`, etc.). `MapRowAsync`'s `TryGetString` already does case-insensitive matching.
- For each data row `rows[1..]`: build a `Dictionary<string, string>` of `header[i] -> (i < row.Count ? row[i] : "")`, then `yield return System.Text.Json.JsonSerializer.SerializeToElement(dict);`. That yields a JSON object whose properties are the CSV columns — exactly what `MapRowAsync`/`Read`/`TryGetString` consume.

No other changes — `MapRowAsync` already reads `permitColumn`/`projectNameColumn`/`valuationColumn`/`unitsColumn`/`addressColumn`/`stageColumn`/`filedDateColumn`/`typeColumn`/`storiesColumn` from config, applies the lane + min-units/min-valuation + `StructuralRelevanceGate` filters, resolves canonical orgs, and upserts.

---

## Task 3 — Migration `197_EnableCaSanDiegoCsv.sql`

New file: `Kor.Opportunities.Data/Schema/197_EnableCaSanDiegoCsv.sql`. **UPDATE** the existing `CA_SocrataSanDiego` source row (seeded disabled in migration 196 pointing at the wrong 267 MB `approvals_active` file). Pattern: `USE [KorOpportunitiesDb]; GO`, guarded `UPDATE … WHERE Name = N'CA_SocrataSanDiego'`, `PRINT`, `GO`.

Set:
- `BaseUrl = N'https://seshat.datasd.org/development_permits/approvals_created_2026_datasd.csv'` (14 MB, under the 50 MB cap; the `active` file is 267 MB and the wrong choice).
- `IsEnabled = 1`
- `UpdatedAtUtc = sysdatetimeoffset()`
- `ConfigJson` = the JSON below (all column names verified against the live CSV header on 2026-06-18):

```json
{"kind":"csv","sourceKeyPrefix":"sdcity","municipality":"San Diego","county":"San Diego County","minUnits":"20","minValuation":"2000000","permitColumn":"APPROVAL_ID","projectNameColumn":"PROJECT_TITLE","descriptionColumn":"PROJECT_SCOPE","typeColumn":"APPROVAL_TYPE","valuationColumn":"APPROVAL_VALUATION","unitsColumn":"APPROVAL_DU_NET_CHANGE","storiesColumn":"APPROVAL_STORIES","addressColumn":"GIS_ADDRESS","stageColumn":"APPROVAL_STATUS","filedDateColumn":"APPROVAL_CREATE_DATE"}
```

(In T-SQL the single quotes inside the JSON must be doubled.)
