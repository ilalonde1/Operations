# Codex prompts — Block 3 permit adapters (Surrey / Victoria / Calgary / Edmonton)

Two new open-data permit transports plumbed into the existing, already-extensible
`BuildingPermitsImportService` (dispatch is a `switch` on `PermitSourceRow.Adapter`).
Mirror `Kor.Opportunities.Data/Awards/VancouverOpenDataPermitAdapter.cs` exactly for
the row→upsert→canonical-resolve loop. **Run prompt A first, then prompt B** (B depends
on the base class from A). Do NOT run `dotnet build`/`test` — Claude verifies locally.

---

## Prompt A — base class + ArcGIS transport (Surrey + Victoria)

Goal: add a reusable open-data permit base + an ArcGIS FeatureServer adapter, wired into
the existing permit ingest. Surrey and Victoria publish issued building permits as ArcGIS
REST FeatureServers (`/query?...&f=json`, rows under `features[].attributes`).

In `Kor.Opportunities.Data/Awards/`:

1. New file `PermitFieldMap.cs` — a record describing one city's field mapping:
   ```
   public sealed record PermitFieldMap(
       string City,
       string ExternalIdField,
       string? PermitNumberField,
       string? PermitCategoryField,
       string? WorkTypeField,
       string? DescriptionField,
       string? ValueField,
       string? UnitsField,
       string[] AddressFields,          // 1+ field names, joined with a space, nulls skipped
       string? GeoLocalAreaField,
       string? LatField,
       string? LngField,
       string? AppliedDateField,
       string? IssuedDateField,
       string? OwnerField,
       string? ApplicantField,
       string? ContractorField,
       string? SpecificUseField,
       string? PropertyUseField,
       bool DatesAreYyyyMmdd = false,   // Surrey/Victoria store dates as YYYYMMDD (string or int)
       bool TrimOrgStrings = false);    // Surrey applicant/contractor are "Org Name, address..." → keep text before first comma
   ```

2. New file `OpenDataPermitAdapterBase.cs` — `public abstract class`. Lift the read helpers
   (`ReadString`/`ReadDecimal`/`ReadInt`/`SameName`), the per-row try/catch loop, and the
   owner/applicant/contractor canonical-resolution block **verbatim** from
   `VancouverOpenDataPermitAdapter`. Differences:
   - ctor takes `IBuildingPermitStore`, `CanonicalOrgResolver`, `ILogger`, `maxBytesPerResponse`, `maxRowsPerRun` (same defaults as Vancouver). Keep `HttpClient` in the concrete subclasses.
   - Expose `public sealed record AdapterResult(int Pulled, int Upserted, int CanonicalsResolved, int Failed);` (reuse the Vancouver shape).
   - Two abstract members the subclasses implement:
     - `protected abstract PermitFieldMap? GetFieldMap(string sourceName);`
     - `protected abstract Task<IReadOnlyList<System.Text.Json.Nodes.JsonNode>> FetchRowsAsync(PermitSourceRow source, int rowCap, CancellationToken ct);` — returns FLAT field objects (one JsonNode per permit; ArcGIS subclass unwraps `.attributes`).
   - `public async Task<AdapterResult> ImportAsync(PermitSourceRow source, int maxRowsPerSource, CancellationToken ct)`:
     resolve `map = GetFieldMap(source.Name)` (throw `InvalidOperationException($"No PermitFieldMap for source '{source.Name}'.")` if null);
     `rowCap = maxRowsPerSource>0 ? Math.Min(_maxRowsPerRun, maxRowsPerSource) : _maxRowsPerRun`;
     `var rows = await FetchRowsAsync(source, rowCap, ct)`; then run the Vancouver per-row loop over `rows`, mapping fields via `map` (use `MapUpsert(node, map, source)`).
   - `private BuildingPermitUpsert MapUpsert(JsonNode node, PermitFieldMap m, PermitSourceRow source)`:
     read each field via `node[m.XField]` using the helpers; `ExternalId` from `m.ExternalIdField` (skip row if blank, count Failed);
     `City = m.City`; `Address` = join non-blank `m.AddressFields` values with a space;
     dates via the new `ReadDate(node[field], m.DatesAreYyyyMmdd)`;
     owner/applicant/contractor: read the mapped field, and **if `m.TrimOrgStrings`** keep only the text before the first comma (trimmed) — apply to all three;
     `RawJson = node.ToJsonString()`.
   - New `ReadDate(JsonNode?, bool yyyyMmdd)`: normalize the node to a string (handle JSON number, e.g. Victoria `20251202`, via `node.GetValue<long>()` → `ToString(CultureInfo.InvariantCulture)`); if `yyyyMmdd` and the string is exactly 8 digits, `DateTime.ParseExact(s, "yyyyMMdd", InvariantCulture)`; otherwise fall back to the existing `DateTime.TryParse(... AssumeUniversal)`. Return `.Date` or null.

3. New file `ArcGisPermitAdapter.cs` — `public sealed class ArcGisPermitAdapter : OpenDataPermitAdapterBase`.
   - `public const string AdapterName = "ArcGisFeatureServer";`
   - ctor adds `HttpClient _http` plus the base args; pass base args through.
   - `GetFieldMap`: a `static readonly Dictionary<string,PermitFieldMap>` (StringComparer.OrdinalIgnoreCase) with these two entries (key = exact PermitSource.Name):

     **"City of Surrey — issued-building-permits"** (residential-only, no geo):
     City=`Surrey`, ExternalId=`PermitNumber`, PermitNumber=`PermitNumber`, PermitCategory=null,
     WorkType=`WorkDescription`, Description=`SubDescription`, Value=`ValueOfConstruction`,
     Units=`DwellingUnits`, Address=[`ProjectAddress`], GeoLocalArea=null, Lat=null, Lng=null,
     AppliedDate=null, IssuedDate=`IssuedDate`, Owner=null, Applicant=`ApplicantOrganization`,
     Contractor=`BuildingGeneralContractorOrganization`, SpecificUse=`SubDescription`, PropertyUse=null,
     DatesAreYyyyMmdd=true, TrimOrgStrings=true.

     **"City of Victoria — issued-building-permits"**:
     City=`Victoria`, ExternalId=`PermitNo`, PermitNumber=`PermitNo`, PermitCategory=`CATEGORY`,
     WorkType=`type`, Description=`Purpose`, Value=`BldgValue`, Units=null,
     Address=[`House`,`Street`], GeoLocalArea=`Neighbourhood`, Lat=`Y_LAT`, Lng=`X_LONG`,
     AppliedDate=null, IssuedDate=`IssuedDate`, Owner=null, Applicant=`Name`, Contractor=null,
     SpecificUse=`ActualUse`, PropertyUse=`AUC_Group`, DatesAreYyyyMmdd=true, TrimOrgStrings=false.

   - `FetchRowsAsync`: page the FeatureServer. The source.Endpoint already contains
     `?where=...&outFields=*&f=json&orderByFields=...` (no paging params). For each page append
     `&resultOffset={offset}&resultRecordCount={pageSize}` (pageSize=1000). GET, cap bytes via
     `Kor.Opportunities.Data.Ingestion.HttpReadHelpers.ReadStringWithCapAsync`, parse, read
     `root["features"]` array, add each `feature["attributes"]` JsonNode to the result. Stop when:
     features empty, `root["exceededTransferLimit"]` is not true, or the accumulated count ≥ rowCap.
     Throw `InvalidOperationException($"ArcGIS {(int)resp.StatusCode}")` on non-success (mirror Vancouver).

4. Wire DI in `Kor.Opportunities.Worker/Program.cs` right after the Vancouver adapter
   registration (~line 354): add an `AddHttpClient(nameof(ArcGisPermitAdapter))` (5-min timeout,
   `RetryPolicy(sp,"ArcGisPermits")`) and `AddSingleton<ArcGisPermitAdapter>(...)` mirroring the
   Vancouver singleton, passing `options.IngestionMaxBytesPerResponse` and a new
   `options.OpenDataPermitsMaxRowsPerRun`.

5. In `Kor.Opportunities.Worker/Options/OpportunitiesWorkerOptions.cs` add
   `public int OpenDataPermitsMaxRowsPerRun { get; set; } = 20000;` next to `VancouverPermitsMaxRowsPerRun`.

6. In `Kor.Opportunities.Data/Awards/BuildingPermitsImportService.cs`: inject
   `ArcGisPermitAdapter` and add a `switch` arm
   `ArcGisPermitAdapter.AdapterName => await _arcGisAdapter.ImportAsync(source, maxRowsPerSource, ct)...`.

---

## Prompt B — Socrata transport (Calgary + Edmonton) — depends on Prompt A's base

Goal: add a Socrata adapter on the base from Prompt A. Calgary (`data.calgary.ca`) and
Edmonton (`data.edmonton.ca`) return a flat JSON array; page with `$limit`/`$offset`.

In `Kor.Opportunities.Data/Awards/`:

1. New file `SocrataPermitAdapter.cs` — `public sealed class SocrataPermitAdapter : OpenDataPermitAdapterBase`.
   - `public const string AdapterName = "Socrata";`
   - ctor adds `HttpClient _http` + base args.
   - `GetFieldMap`: `static readonly Dictionary<string,PermitFieldMap>` (OrdinalIgnoreCase):

     **"City of Calgary — issued-building-permits"**:
     City=`Calgary`, ExternalId=`permitnum`, PermitNumber=`permitnum`, PermitCategory=`permitclass`,
     WorkType=`workclassmapped`, Description=`description`, Value=`estprojectcost`, Units=`housingunits`,
     Address=[`originaladdress`], GeoLocalArea=`communityname`, Lat=`latitude`, Lng=`longitude`,
     AppliedDate=`applieddate`, IssuedDate=`issueddate`, Owner=null, Applicant=`applicantname`,
     Contractor=`contractorname`, SpecificUse=`permittypemapped`, PropertyUse=`permitclassmapped`,
     DatesAreYyyyMmdd=false, TrimOrgStrings=false.

     **"City of Edmonton — issued-building-permits"** (permit# FOIP-redacted → dedup on row_id; no party names):
     City=`Edmonton`, ExternalId=`row_id`, PermitNumber=`permit_number`, PermitCategory=`job_category`,
     WorkType=`work_type`, Description=`job_description`, Value=`construction_value`, Units=`units_added`,
     Address=[`address`], GeoLocalArea=`neighbourhood`, Lat=`latitude`, Lng=`longitude`,
     AppliedDate=null, IssuedDate=`issue_date`, Owner=null, Applicant=null, Contractor=null,
     SpecificUse=`building_type`, PropertyUse=`job_category`, DatesAreYyyyMmdd=false, TrimOrgStrings=false.

   - `FetchRowsAsync`: page the resource. source.Endpoint already has `?$where=...&$order=...`
     (no paging). For each page append `&$limit={pageSize}&$offset={offset}` (pageSize=5000). GET,
     cap bytes via `HttpReadHelpers.ReadStringWithCapAsync`, parse `JsonNode.Parse(body)?.AsArray()`,
     add each element. Stop when a page returns fewer than pageSize rows or accumulated ≥ rowCap.
     Non-success → `InvalidOperationException($"Socrata {(int)resp.StatusCode}")`.

2. DI in `Program.cs`: `AddHttpClient(nameof(SocrataPermitAdapter))` + `AddSingleton<SocrataPermitAdapter>(...)`
   mirroring the ArcGIS one (RetryPolicy "SocrataPermits", same options).

3. `BuildingPermitsImportService.cs`: inject `SocrataPermitAdapter`, add the switch arm
   `SocrataPermitAdapter.AdapterName => await _socrataAdapter.ImportAsync(source, maxRowsPerSource, ct)...`.

---

## After Codex confirms both
Claude builds Data + Worker locally, reverts any csproj publish-bumps, commits, deploys the
Worker to KOR-APP01, then runs `output/add-permit-sources.ps1` (4 PermitSource rows, IsActive=1)
and verifies per-source `BuildingPermit` counts + canonical resolves.
