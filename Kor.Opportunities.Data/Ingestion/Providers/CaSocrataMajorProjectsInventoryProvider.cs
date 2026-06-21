#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Data.MajorProjects;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Ingestion.Providers;

/// <summary>
/// Ingests California permit/open-data feeds into MajorProjectsInventory. The
/// source row controls Socrata SODA or CKAN datastore_search endpoint details
/// through BaseUrl and ConfigJson/OpportunitySourceMappings.
/// </summary>
public sealed class CaSocrataMajorProjectsInventoryProvider : IOpportunityProvider
{
    private const string Province = "CA";
    private const string ProponentAliasSource = "MajorProjectsInventory.Proponent";
    private const string ArchitectAliasSource = "MajorProjectsInventory.Architect";
    private const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36";
    private static readonly Regex YearRegex = new(@"\b(19|20)\d{2}\b", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly string _connectionString;
    private readonly CanonicalOrgResolver _canonicalResolver;
    private readonly ILogger<CaSocrataMajorProjectsInventoryProvider> _logger;
    private readonly int _maxBytesPerResponse;
    private readonly int _maxRowsPerRun;
    private readonly string _defaultAppToken;

    public CaSocrataMajorProjectsInventoryProvider(
        HttpClient httpClient,
        string connectionString,
        CanonicalOrgResolver canonicalResolver,
        ILogger<CaSocrataMajorProjectsInventoryProvider> logger,
        int maxBytesPerResponse = 50 * 1024 * 1024,
        int maxRowsPerRun = 20_000,
        string defaultAppToken = "")
    {
        _httpClient = httpClient;
        _connectionString = connectionString;
        _canonicalResolver = canonicalResolver;
        _logger = logger;
        _maxBytesPerResponse = maxBytesPerResponse > 0 ? maxBytesPerResponse : int.MaxValue;
        _maxRowsPerRun = maxRowsPerRun > 0 ? maxRowsPerRun : int.MaxValue;
        _defaultAppToken = defaultAppToken ?? "";
    }

    public OpportunitySourceType SourceType => OpportunitySourceType.MajorProjectsInventory;

    public async Task<IReadOnlyList<OpportunityCandidate>> FetchAsync(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct)
    {
        var inserted = 0;
        var updated = 0;
        var nameMatched = 0;
        var skipped = 0;
        var processed = 0;

        await foreach (var recordJson in FetchRowsAsync(source, sourceConfig, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            if (processed >= _maxRowsPerRun)
            {
                _logger.LogWarning("CA open-data Major Projects Inventory row cap {MaxRows} reached for {Source}; remaining rows skipped.", _maxRowsPerRun, source.Name);
                break;
            }

            processed++;
            var record = await MapRowAsync(source, sourceConfig, recordJson, ct).ConfigureAwait(false);
            if (record is null)
            {
                skipped++;
                continue;
            }

            var outcome = await UpsertAsync(record, ct).ConfigureAwait(false);
            switch (outcome)
            {
                case UpsertOutcome.Inserted:
                    inserted++;
                    break;
                case UpsertOutcome.NameMatched:
                    nameMatched++;
                    break;
                default:
                    updated++;
                    break;
            }
        }

        _logger.LogInformation(
            "CA open-data Major Projects Inventory ingestion for {Source}: inserted={Inserted} updated={Updated} nameMatched={NameMatched} skipped={Skipped} processed={Processed}.",
            source.Name,
            inserted,
            updated,
            nameMatched,
            skipped,
            processed);

        return Array.Empty<OpportunityCandidate>();
    }

    private async IAsyncEnumerable<JsonElement> FetchRowsAsync(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var kind = Get(sourceConfig, "kind", "format");
        if (string.Equals(kind, "ckan", StringComparison.OrdinalIgnoreCase)
            || source.BaseUrl.Contains("/api/3/action/", StringComparison.OrdinalIgnoreCase))
        {
            await foreach (var row in FetchCkanRowsAsync(source, sourceConfig, ct).ConfigureAwait(false))
            {
                yield return row;
            }

            yield break;
        }

        if (string.Equals(kind, "csv", StringComparison.OrdinalIgnoreCase))
        {
            await foreach (var row in FetchCsvRowsAsync(source, sourceConfig, ct).ConfigureAwait(false))
            {
                yield return row;
            }

            yield break;
        }

        await foreach (var row in FetchSocrataRowsAsync(source, sourceConfig, ct).ConfigureAwait(false))
        {
            yield return row;
        }
    }

    private async IAsyncEnumerable<JsonElement> FetchSocrataRowsAsync(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var limit = ParseInt(Get(sourceConfig, "limit"), 500);
        var offset = 0;
        var emitted = 0;
        while (emitted < _maxRowsPerRun)
        {
            var where = Get(sourceConfig, "where");
            var url = AddQuery(
                source.BaseUrl,
                ("$limit", Math.Min(limit, _maxRowsPerRun - emitted).ToString(CultureInfo.InvariantCulture)),
                ("$offset", offset.ToString(CultureInfo.InvariantCulture)),
                ("$where", where ?? ""));

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddCommonHeaders(request, sourceConfig);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode && response.StatusCode == System.Net.HttpStatusCode.BadRequest && !string.IsNullOrWhiteSpace(where))
            {
                _logger.LogWarning("CA Socrata source {Source} rejected configured $where filter; retrying page without server-side filter.", source.Name);
                using var fallbackRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    AddQuery(
                        source.BaseUrl,
                        ("$limit", Math.Min(limit, _maxRowsPerRun - emitted).ToString(CultureInfo.InvariantCulture)),
                        ("$offset", offset.ToString(CultureInfo.InvariantCulture))));
                AddCommonHeaders(fallbackRequest, sourceConfig);
                using var fallbackResponse = await _httpClient.SendAsync(fallbackRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                fallbackResponse.EnsureSuccessStatusCode();
                using var fallbackDoc = await ReadJsonDocumentAsync(fallbackResponse, source.Name, ct).ConfigureAwait(false);
                var fallbackRows = ReadSocrataBatch(fallbackDoc, source.Name);
                emitted += fallbackRows.Count;
                foreach (var row in fallbackRows)
                {
                    yield return row;
                }

                if (fallbackRows.Count == 0 || fallbackRows.Count < limit)
                {
                    yield break;
                }

                offset += fallbackRows.Count;
                continue;
            }

            response.EnsureSuccessStatusCode();

            using var doc = await ReadJsonDocumentAsync(response, source.Name, ct).ConfigureAwait(false);
            var rows = ReadSocrataBatch(doc, source.Name);
            emitted += rows.Count;
            foreach (var row in rows)
            {
                yield return row;
            }

            if (rows.Count == 0 || rows.Count < limit)
            {
                yield break;
            }

            offset += rows.Count;
        }
    }

    private static IReadOnlyList<JsonElement> ReadSocrataBatch(JsonDocument doc, string sourceName)
    {
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"CA Socrata source {sourceName} returned non-array JSON.");
        }

        var rows = new List<JsonElement>();
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            rows.Add(row.Clone());
        }

        return rows;
    }

    private async IAsyncEnumerable<JsonElement> FetchCsvRowsAsync(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, source.BaseUrl);
        AddCommonHeaders(request, sourceConfig);
        request.Headers.TryAddWithoutValidation("Accept", "text/csv,*/*;q=0.8");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var capped = new MemoryStream();
        var buffer = new byte[81920];
        var total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > _maxBytesPerResponse)
            {
                throw new InvalidOperationException(
                    $"CA CSV Major Projects Inventory response for {source.Name} exceeded configured cap of {_maxBytesPerResponse} bytes.");
            }

            await capped.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        var csv = Encoding.UTF8.GetString(capped.ToArray());
        var rows = CsvParser.Parse(csv);
        if (rows.Count < 2)
        {
            yield break;
        }

        var headers = new List<string>(rows[0].Count);
        for (var i = 0; i < rows[0].Count; i++)
        {
            var header = rows[0][i].Trim();
            if (i == 0)
            {
                header = header.TrimStart('\uFEFF');
            }

            headers.Add(header);
        }

        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Count; i++)
            {
                if (headers[i].Length == 0)
                {
                    continue;
                }

                dict[headers[i]] = i < row.Count ? row[i] : "";
            }

            yield return JsonSerializer.SerializeToElement(dict);
        }
    }

    private async IAsyncEnumerable<JsonElement> FetchCkanRowsAsync(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var resourceId = Get(sourceConfig, "resourceId", "resource_id");
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            resourceId = await ResolveCkanResourceIdAsync(source, sourceConfig, ct).ConfigureAwait(false);
        }

        var limit = ParseInt(Get(sourceConfig, "limit"), 500);
        var offset = 0;
        var emitted = 0;
        while (emitted < _maxRowsPerRun)
        {
            var url = AddQuery(
                source.BaseUrl,
                ("resource_id", resourceId),
                ("limit", Math.Min(limit, _maxRowsPerRun - emitted).ToString(CultureInfo.InvariantCulture)),
                ("offset", offset.ToString(CultureInfo.InvariantCulture)),
                ("q", Get(sourceConfig, "ckanQuery", "q") ?? ""));

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddCommonHeaders(request, sourceConfig);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var doc = await ReadJsonDocumentAsync(response, source.Name, ct).ConfigureAwait(false);
            if (!doc.RootElement.TryGetProperty("result", out var result)
                || !result.TryGetProperty("records", out var records)
                || records.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"CA CKAN source {source.Name} returned no result.records array.");
            }

            var batchCount = 0;
            foreach (var row in records.EnumerateArray())
            {
                batchCount++;
                emitted++;
                yield return row.Clone();
            }

            if (batchCount == 0 || batchCount < limit)
            {
                yield break;
            }

            offset += batchCount;
        }
    }

    private async Task<string> ResolveCkanResourceIdAsync(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct)
    {
        var packageSearchUrl = Get(sourceConfig, "ckanPackageSearchUrl");
        if (string.IsNullOrWhiteSpace(packageSearchUrl))
        {
            var marker = "/api/3/action/";
            var idx = source.BaseUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                throw new InvalidOperationException($"CA CKAN source {source.Name} must configure resourceId or ckanPackageSearchUrl.");
            }

            packageSearchUrl = source.BaseUrl[..idx] + marker + "package_search";
        }

        var q = Get(sourceConfig, "ckanPackageQuery", "ckanQuery", "q") ?? "building permits";
        using var request = new HttpRequestMessage(HttpMethod.Get, AddQuery(packageSearchUrl, ("q", q), ("rows", "10")));
        AddCommonHeaders(request, sourceConfig);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var doc = await ReadJsonDocumentAsync(response, source.Name, ct).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("results", out var packages)
            || packages.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"CA CKAN source {source.Name} package search returned no result.results array.");
        }

        foreach (var package in packages.EnumerateArray())
        {
            if (!package.TryGetProperty("resources", out var resources) || resources.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var resource in resources.EnumerateArray())
            {
                if (TryGetString(resource, "datastore_active", out var activeText)
                    && bool.TryParse(activeText, out var active)
                    && !active)
                {
                    continue;
                }

                if (TryGetString(resource, "id", out var id))
                {
                    return id;
                }
            }
        }

        throw new InvalidOperationException($"CA CKAN source {source.Name} could not resolve a datastore resource id.");
    }

    private async Task<MajorProjectRecord?> MapRowAsync(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        JsonElement row,
        CancellationToken ct)
    {
        var permitNumber = Read(row, sourceConfig, "permitColumn", "permit_number", "permitnum", "permit_no", "permit", "record_id", "_id", "id");
        if (string.IsNullOrWhiteSpace(permitNumber))
        {
            return null;
        }

        var address = Read(row, sourceConfig, "addressColumn", "address", "site_address", "street_address", "location", "full_address");
        // Composite address: some sources (e.g. SF) split the address across several
        // columns with no combined field. Join the listed columns to form a clean name.
        var addressColsCfg = Get(sourceConfig, "addressColumns");
        var addressIsComposite = false;
        if (!string.IsNullOrWhiteSpace(addressColsCfg))
        {
            var parts = new List<string>();
            foreach (var col in addressColsCfg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (TryGetString(row, col, out var v) && !string.IsNullOrWhiteSpace(v))
                {
                    parts.Add(v.Trim());
                }
            }
            var composite = System.Text.RegularExpressions.Regex.Replace(string.Join(" ", parts), @"\s+", " ").Trim();
            if (composite.Length > 0)
            {
                address = composite;
                addressIsComposite = true;
            }
        }
        var description = Read(row, sourceConfig, "descriptionColumn", "description", "work_description", "scope", "project_description", "permit_description");
        var type = Read(row, sourceConfig, "typeColumn", "permit_type", "permittypemapped", "type", "record_type", "proposed_use", "use");
        var projectName = FirstNonBlank(
            Read(row, sourceConfig, "projectNameColumn", "project_name", "project", "name"),
            address,                         // clean street address (composite when addressColumns set)
            DescriptionLead(description),
            address is null ? null : $"{type} - {address}",
            permitNumber);
        var proponent = Read(row, sourceConfig, "proponentColumn", "applicant", "owner", "contractor", "developer", "name_of_applicant");
        var architect = Read(row, sourceConfig, "architectColumn", "architect", "design_professional");
        var municipality = FirstNonBlank(Get(sourceConfig, "municipality"), Read(row, sourceConfig, "municipalityColumn", "city", "municipality"));
        // Enrich an address-composite name with ", <municipality> - <descriptor>" when configured.
        projectName = ComposeRichName(projectName, address, addressIsComposite, row, sourceConfig, municipality);
        var county = FirstNonBlank(Get(sourceConfig, "county"), Read(row, sourceConfig, "countyColumn", "county"));
        var valuationText = Read(row, sourceConfig, "valuationColumn", "valuation", "estimated_cost", "construction_cost", "valuation_amount", "project_value");
        var unitsText = Read(row, sourceConfig, "unitsColumn", "units", "proposed_units", "dwelling_units", "number_of_units", "residential_units");
        var storiesText = Read(row, sourceConfig, "storiesColumn", "stories", "proposed_stories", "number_of_stories");
        var stage = FirstNonBlank(Read(row, sourceConfig, "stageColumn", "status", "permit_status", "application_status"), "Permit filing");
        if (IsTerminalPermitStage(stage))
        {
            return null;
        }

        var sourceUrl = FirstNonBlank(Read(row, sourceConfig, "sourceUrlColumn", "url", "link", "record_url"), source.BaseUrl);
        var valuation = ParseMoney(valuationText);
        var units = ParseInt(unitsText, 0);
        var minUnits = ParseInt(Get(sourceConfig, "minUnits"), 20);
        var minValuation = ParseDecimal(Get(sourceConfig, "minValuation"), 2_000_000m);
        var combined = string.Join(" - ", new[] { projectName, type, description, address }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!IsLaneProject(combined) || (units < minUnits && (!valuation.HasValue || valuation.Value < minValuation)))
        {
            return null;
        }

        var relevance = StructuralRelevanceGate.Evaluate(
            projectName,
            string.Join(" - ", new[] { type, description, address, unitsText, storiesText }.Where(s => !string.IsNullOrWhiteSpace(s))),
            proponent);
        if (!relevance.Keep)
        {
            return null;
        }

        var proponentCanonicalId = !string.IsNullOrWhiteSpace(proponent)
            ? await _canonicalResolver.ResolveAsync(
                proponent,
                OrgKinds.Unknown,
                ProponentAliasSource,
                ct,
                allowCreate: true,
                minConfidenceForCreate: 70).ConfigureAwait(false)
            : null;

        var architectCanonicalId = !string.IsNullOrWhiteSpace(architect)
            ? await _canonicalResolver.ResolveAsync(
                architect,
                OrgKinds.Architect,
                ArchitectAliasSource,
                ct,
                allowCreate: true,
                minConfidenceForCreate: 70).ConfigureAwait(false)
            : null;

        return new MajorProjectRecord(
            Province,
            BuildSourceKey(FirstNonBlank(Get(sourceConfig, "sourceKeyPrefix"), source.Name)!, permitNumber),
            projectName ?? permitNumber,
            "Building Permit",
            type,
            valuation,
            valuationText,
            stage,
            proponent,
            proponentCanonicalId,
            architect,
            architectCanonicalId,
            municipality,
            county,
            ParseYear(Read(row, sourceConfig, "filedDateColumn", "filed_date", "applied_date", "application_date", "issued_date", "created_date")),
            null,
            BuildScheduleNotes(description, address, unitsText, storiesText),
            sourceUrl,
            row.GetRawText());
    }

    private enum UpsertOutcome
    {
        Updated,
        Inserted,
        NameMatched,
    }

    private async Task<UpsertOutcome> UpsertAsync(MajorProjectRecord record, CancellationToken ct)
    {
        const string sql = @"
SET XACT_ABORT ON;

DECLARE @inserted table (Id bigint NOT NULL);
DECLARE @nameMatchedId bigint;

BEGIN TRAN;

UPDATE opportunities.MajorProjectsInventory WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
SET
    ProjectName             = @projectName,
    Sector                  = @sector,
    SubSector               = @subSector,
    EstimatedCostCad        = @estimatedCostCad,
    EstimatedCostText       = @estimatedCostText,
    Stage                   = @stage,
    ProponentName           = @proponentName,
    ProponentCanonicalOrgId = @proponentCanonicalOrgId,
    ArchitectName           = COALESCE(ArchitectName, @architectName),
    ArchitectCanonicalOrgId = COALESCE(ArchitectCanonicalOrgId, @architectCanonicalOrgId),
    MunicipalityName        = @municipalityName,
    RegionName              = @regionName,
    StartYear               = @startYear,
    CompletionYear          = @completionYear,
    ScheduleNotes           = @scheduleNotes,
    SourceUrl               = @sourceUrl,
    RawJson                 = @rawJson,
    LastSeenAtUtc           = sysdatetimeoffset(),
    UpdatedAtUtc            = sysdatetimeoffset()
WHERE Province = @province
  AND SourceKey = @sourceKey;

IF @@ROWCOUNT = 0
BEGIN
    IF LOWER(LTRIM(RTRIM(@projectName))) NOT IN
        (N'condominium development', N'residential condominium', N'mixed-use development',
         N'condo development', N'apartment building')
    BEGIN
        SELECT TOP (1) @nameMatchedId = Id
        FROM opportunities.MajorProjectsInventory WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
        WHERE RetiredAtUtc IS NULL
          AND Province = @province
          AND LOWER(LTRIM(RTRIM(ProjectName))) = LOWER(LTRIM(RTRIM(@projectName)))
          AND (MunicipalityName IS NULL OR LTRIM(RTRIM(MunicipalityName)) = N''
               OR @municipalityName IS NULL OR LTRIM(RTRIM(@municipalityName)) = N''
               OR LOWER(LTRIM(RTRIM(MunicipalityName))) = LOWER(LTRIM(RTRIM(@municipalityName))))
        ORDER BY Id;
    END;

    IF @nameMatchedId IS NOT NULL
    BEGIN
        UPDATE opportunities.MajorProjectsInventory
        SET
            Sector                  = COALESCE(Sector, @sector),
            SubSector               = COALESCE(SubSector, @subSector),
            EstimatedCostCad        = COALESCE(EstimatedCostCad, @estimatedCostCad),
            EstimatedCostText       = COALESCE(EstimatedCostText, @estimatedCostText),
            Stage                   = COALESCE(Stage, @stage),
            ProponentName           = COALESCE(ProponentName, @proponentName),
            ProponentCanonicalOrgId = COALESCE(ProponentCanonicalOrgId, @proponentCanonicalOrgId),
            ArchitectName           = COALESCE(ArchitectName, @architectName),
            ArchitectCanonicalOrgId = COALESCE(ArchitectCanonicalOrgId, @architectCanonicalOrgId),
            MunicipalityName        = COALESCE(MunicipalityName, @municipalityName),
            RegionName              = COALESCE(RegionName, @regionName),
            StartYear               = COALESCE(StartYear, @startYear),
            CompletionYear          = COALESCE(CompletionYear, @completionYear),
            ScheduleNotes           = COALESCE(ScheduleNotes, @scheduleNotes),
            SourceUrl               = COALESCE(SourceUrl, @sourceUrl),
            RawJson                 = COALESCE(RawJson, @rawJson),
            LastSeenAtUtc           = sysdatetimeoffset(),
            UpdatedAtUtc            = sysdatetimeoffset()
        WHERE Id = @nameMatchedId;
    END
    ELSE
    BEGIN
        INSERT INTO opportunities.MajorProjectsInventory
            (Province, SourceKey, ProjectName, Sector, SubSector, EstimatedCostCad,
             EstimatedCostText, Stage, ProponentName, ProponentCanonicalOrgId,
             ArchitectName, ArchitectCanonicalOrgId, MunicipalityName, RegionName, StartYear, CompletionYear, ScheduleNotes,
             SourceUrl, RawJson)
        OUTPUT inserted.Id INTO @inserted
        VALUES
            (@province, @sourceKey, @projectName, @sector, @subSector, @estimatedCostCad,
             @estimatedCostText, @stage, @proponentName, @proponentCanonicalOrgId,
             @architectName, @architectCanonicalOrgId, @municipalityName, @regionName, @startYear, @completionYear, @scheduleNotes,
             @sourceUrl, @rawJson);
    END;
END;

COMMIT TRAN;

SELECT
    CASE WHEN EXISTS (SELECT 1 FROM @inserted) THEN 1
         WHEN @nameMatchedId IS NOT NULL THEN 2
         ELSE 0 END AS Outcome,
    @nameMatchedId AS NameMatchedId;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        AddParams(cmd, record);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await reader.ReadAsync(ct).ConfigureAwait(false);
        var outcome = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
        if (outcome == 2)
        {
            _logger.LogInformation(
                "CA open-data Major Projects Inventory name-matched existing MPI {MatchedId}: project '{ProjectName}' arrived with new SourceKey {SourceKey}; merged into existing row instead of inserting.",
                reader.GetInt64(1),
                record.ProjectName,
                record.SourceKey);
            return UpsertOutcome.NameMatched;
        }

        return outcome == 1 ? UpsertOutcome.Inserted : UpsertOutcome.Updated;
    }

    private static void AddParams(SqlCommand cmd, MajorProjectRecord record)
    {
        cmd.Parameters.Add("@province", SqlDbType.NVarChar, 2).Value =
            ProvinceNormalizer.Normalize(record.Province, record.Province ?? "");
        cmd.Parameters.Add("@sourceKey", SqlDbType.NVarChar, 200).Value = record.SourceKey;
        cmd.Parameters.Add("@projectName", SqlDbType.NVarChar, 500).Value = Truncate(record.ProjectName, 500);
        cmd.Parameters.Add("@sector", SqlDbType.NVarChar, 100).Value = Db(record.Sector, 100);
        cmd.Parameters.Add("@subSector", SqlDbType.NVarChar, 100).Value = Db(record.SubSector, 100);
        cmd.Parameters.Add("@estimatedCostCad", SqlDbType.Decimal).Value = record.EstimatedCostCad.HasValue
            ? (object)record.EstimatedCostCad.Value
            : DBNull.Value;
        ((SqlParameter)cmd.Parameters["@estimatedCostCad"]).Precision = 18;
        ((SqlParameter)cmd.Parameters["@estimatedCostCad"]).Scale = 0;
        cmd.Parameters.Add("@estimatedCostText", SqlDbType.NVarChar, 200).Value = Db(record.EstimatedCostText, 200);
        cmd.Parameters.Add("@stage", SqlDbType.NVarChar, 50).Value = Db(record.Stage, 50);
        cmd.Parameters.Add("@proponentName", SqlDbType.NVarChar, 500).Value = Db(record.ProponentName, 500);
        cmd.Parameters.Add("@proponentCanonicalOrgId", SqlDbType.BigInt).Value = record.ProponentCanonicalOrgId.HasValue
            ? (object)record.ProponentCanonicalOrgId.Value
            : DBNull.Value;
        cmd.Parameters.Add("@architectName", SqlDbType.NVarChar, 500).Value = Db(record.ArchitectName, 500);
        cmd.Parameters.Add("@architectCanonicalOrgId", SqlDbType.BigInt).Value = record.ArchitectCanonicalOrgId.HasValue
            ? (object)record.ArchitectCanonicalOrgId.Value
            : DBNull.Value;
        cmd.Parameters.Add("@municipalityName", SqlDbType.NVarChar, 200).Value = Db(record.MunicipalityName, 200);
        cmd.Parameters.Add("@regionName", SqlDbType.NVarChar, 200).Value = Db(record.RegionName, 200);
        cmd.Parameters.Add("@startYear", SqlDbType.SmallInt).Value = record.StartYear.HasValue
            ? (object)record.StartYear.Value
            : DBNull.Value;
        cmd.Parameters.Add("@completionYear", SqlDbType.SmallInt).Value = record.CompletionYear.HasValue
            ? (object)record.CompletionYear.Value
            : DBNull.Value;
        cmd.Parameters.Add("@scheduleNotes", SqlDbType.NVarChar, 1000).Value = Db(record.ScheduleNotes, 1000);
        cmd.Parameters.Add("@sourceUrl", SqlDbType.NVarChar, 1000).Value = Db(record.SourceUrl, 1000);
        cmd.Parameters.Add("@rawJson", SqlDbType.NVarChar, -1).Value = Db(record.RawJson);
    }

    private void AddCommonHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string> sourceConfig)
    {
        request.Headers.UserAgent.ParseAdd(BrowserUserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json,*/*;q=0.8");
        var token = FirstNonBlank(Get(sourceConfig, "appToken", "socrataAppToken"), _defaultAppToken);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.TryAddWithoutValidation("X-App-Token", token);
        }
    }

    private async Task<JsonDocument> ReadJsonDocumentAsync(HttpResponseMessage response, string sourceName, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var capped = new MemoryStream();
        var buffer = new byte[81920];
        var total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > _maxBytesPerResponse)
            {
                throw new InvalidOperationException(
                    $"CA open-data Major Projects Inventory response for {sourceName} exceeded configured cap of {_maxBytesPerResponse} bytes.");
            }

            await capped.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        capped.Position = 0;
        return await JsonDocument.ParseAsync(capped, cancellationToken: ct).ConfigureAwait(false);
    }

    private static object Db(string? value, int? max = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DBNull.Value;
        }

        var trimmed = value.Trim();
        return max.HasValue && trimmed.Length > max.Value
            ? trimmed.Substring(0, max.Value)
            : trimmed;
    }

    private static string? Read(JsonElement row, IReadOnlyDictionary<string, string> config, string configKey, params string[] fallbackNames)
    {
        var configured = Get(config, configKey);
        if (!string.IsNullOrWhiteSpace(configured)
            && TryGetString(row, configured, out var configuredValue))
        {
            return configuredValue;
        }

        foreach (var name in fallbackNames)
        {
            if (TryGetString(row, name, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? Get(IReadOnlyDictionary<string, string> config, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = "";
        if (!element.TryGetProperty(propertyName, out var property))
        {
            foreach (var p in element.EnumerateObject())
            {
                if (string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    property = p.Value;
                    goto Found;
                }
            }

            return false;
        }

Found:
        value = property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? "",
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "",
        };
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string AddQuery(string url, params (string Key, string? Value)[] values)
    {
        var sb = new StringBuilder(url);
        var hasQuery = url.Contains('?', StringComparison.Ordinal);
        foreach (var (key, value) in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            sb.Append(hasQuery ? '&' : '?');
            hasQuery = true;
            sb.Append(key);
            sb.Append('=');
            sb.Append(WebUtility.UrlEncode(value));
        }

        return sb.ToString();
    }

    private static bool IsLaneProject(string value)
    {
        var lower = value.ToLowerInvariant();
        return Regex.IsMatch(lower, @"\b(apartment|apartments|multifamily|multi-family|condo|condominium|residential|dwelling|mixed[- ]use|hotel|commercial|office|retail|school|hospital|medical)\b")
            && !Regex.IsMatch(lower, @"\b(road|bridge|highway|utility|utilities|trail|pipeline|sewer|water main|transmission|substation)\b");
    }

    private static bool IsTerminalPermitStage(string? stage)
    {
        var normalized = NormalizePermitStage(stage);
        return normalized is "complete"
            or "issued"
            or "inspection followup"
            or "withdrawn"
            or "expired"
            or "cancelled"
            or "canceled"
            or "disapproved"
            or "suspend"
            or "suspended"
            or "closed"
            or "void";
    }

    private static string NormalizePermitStage(string? stage)
    {
        if (string.IsNullOrWhiteSpace(stage))
        {
            return string.Empty;
        }

        var normalized = Regex.Replace(stage.Trim().ToLowerInvariant(), @"[\s_\-]+", " ");
        normalized = Regex.Replace(normalized, @"[^a-z ]", string.Empty).Trim();
        return normalized;
    }

    private static decimal? ParseMoney(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var raw = value.Trim();
        var lower = raw.ToLowerInvariant();
        var multiplier = 1m;
        if (lower.Contains("billion") || Regex.IsMatch(lower, @"\bb\b"))
        {
            multiplier = 1_000_000_000m;
        }
        else if (lower.Contains("million") || Regex.IsMatch(lower, @"\bm\b"))
        {
            multiplier = 1_000_000m;
        }

        var numeric = Regex.Replace(lower, @"[$,\s]", "");
        numeric = Regex.Replace(numeric, "(billion|million|[mb])", "", RegexOptions.IgnoreCase);
        return decimal.TryParse(numeric, NumberStyles.Number | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            ? decimal.Round(parsed * multiplier, 0)
            : null;
    }

    private static int ParseInt(string? value, int fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        // Handle float-text counts (e.g. SF Socrata returns "48.0") + thousands separators.
        var match = Regex.Match(value.Replace(",", string.Empty), @"-?\d+(?:\.\d+)?");
        return match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? (int)parsed
            : fallback;
    }

    private static decimal ParseDecimal(string? value, decimal fallback) =>
        decimal.TryParse(value, NumberStyles.Number | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    // When a row is named by the composite address, optionally append ", <municipality>"
    // and a descriptor built from configured "descriptorParts" ("col={}-storey;col2=({} units)").
    // Only fires when the name IS the composite address (never overrides a real projectNameColumn).
    private static string ComposeRichName(string? name, string? address, bool addressIsComposite,
        JsonElement row, IReadOnlyDictionary<string, string> config, string? municipality)
    {
        if (string.IsNullOrWhiteSpace(name) || !addressIsComposite || !string.Equals(name, address, StringComparison.Ordinal))
        {
            return name ?? string.Empty;
        }

        var result = name;
        if (string.Equals(Get(config, "nameAppendMunicipality"), "true", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(municipality)
            && result.IndexOf(municipality!, StringComparison.OrdinalIgnoreCase) < 0)
        {
            result += ", " + municipality;
        }

        var dp = Get(config, "descriptorParts");
        if (!string.IsNullOrWhiteSpace(dp))
        {
            var segs = new List<string>();
            foreach (var entry in dp.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var eq = entry.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                var col = entry.Substring(0, eq).Trim();
                var fmt = entry.Substring(eq + 1);
                if (TryGetString(row, col, out var val) && !string.IsNullOrWhiteSpace(val))
                {
                    val = CleanNumeric(val.Trim());
                    if (val == "0")
                    {
                        continue;
                    }

                    segs.Add(fmt.Replace("{}", val));
                }
            }

            if (segs.Count > 0)
            {
                result += " - " + string.Join(" ", segs);
            }
        }

        return result;
    }

    private static string CleanNumeric(string v) =>
        double.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d == Math.Floor(d)
            ? ((long)d).ToString(CultureInfo.InvariantCulture)
            : v;

    private static short? ParseYear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = YearRegex.Match(value);
        return match.Success
            && short.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            ? year
            : null;
    }

    private static string? DescriptionLead(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var trimmed = description.Trim();
        var stop = trimmed.IndexOfAny(['.', ';', '\n', '\r']);
        return stop > 12 ? trimmed[..stop] : trimmed;
    }

    private static string? BuildScheduleNotes(params string?[] values) =>
        FirstNonBlank(values) is { } first
            ? string.Join(" - ", values.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()))
            : null;

    private static string? FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value.Substring(0, max);

    private static string BuildSourceKey(string prefix, string permitNumber)
    {
        var key = $"{prefix.Trim()}:{permitNumber.Trim()}";
        return key.Length <= 200 ? key : key[..200];
    }

    private sealed record MajorProjectRecord(
        string Province,
        string SourceKey,
        string ProjectName,
        string? Sector,
        string? SubSector,
        decimal? EstimatedCostCad,
        string? EstimatedCostText,
        string? Stage,
        string? ProponentName,
        long? ProponentCanonicalOrgId,
        string? ArchitectName,
        long? ArchitectCanonicalOrgId,
        string? MunicipalityName,
        string? RegionName,
        short? StartYear,
        short? CompletionYear,
        string? ScheduleNotes,
        string? SourceUrl,
        string RawJson);
}
