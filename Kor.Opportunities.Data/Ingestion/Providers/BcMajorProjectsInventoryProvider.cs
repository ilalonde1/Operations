#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
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
/// Ingests British Columbia's Major Projects Inventory from DataBC into the
/// dedicated MPI table. Like the AB MPI provider, this is wired through
/// IOpportunityProvider for source dispatch but emits no opportunity candidates.
/// </summary>
public sealed class BcMajorProjectsInventoryProvider : IOpportunityProvider
{
    private const string Province = "BC";
    private const string ProponentAliasSource = "MajorProjectsInventory.Proponent";
    private const string ArchitectAliasSource = "MajorProjectsInventory.Architect";
    private const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36";
    private static readonly Regex LeadingRegionOrdinal = new(@"^\s*\d+\.\s*", RegexOptions.Compiled);
    private static readonly Regex QuarterYear = new(@"^\s*((?:19|20)\d{2})-Q[1-4]\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly HttpClient _httpClient;
    private readonly string _connectionString;
    private readonly CanonicalOrgResolver _canonicalResolver;
    private readonly ILogger<BcMajorProjectsInventoryProvider> _logger;
    private readonly int _maxBytesPerResponse;
    private readonly int _maxRowsPerRun;

    public BcMajorProjectsInventoryProvider(
        HttpClient httpClient,
        string connectionString,
        CanonicalOrgResolver canonicalResolver,
        ILogger<BcMajorProjectsInventoryProvider> logger,
        int maxBytesPerResponse = 50 * 1024 * 1024,
        int maxRowsPerRun = 20_000)
    {
        _httpClient = httpClient;
        _connectionString = connectionString;
        _canonicalResolver = canonicalResolver;
        _logger = logger;
        _maxBytesPerResponse = maxBytesPerResponse > 0 ? maxBytesPerResponse : int.MaxValue;
        _maxRowsPerRun = maxRowsPerRun > 0 ? maxRowsPerRun : int.MaxValue;
    }

    public OpportunitySourceType SourceType => OpportunitySourceType.MajorProjectsInventory;

    public async Task<IReadOnlyList<OpportunityCandidate>> FetchAsync(
        OpportunitySource source,
        IReadOnlyDictionary<string, string> sourceConfig,
        CancellationToken ct)
    {
        var csvUrl = await ResolveCsvUrlAsync(source.BaseUrl, ct).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Get, csvUrl);
        request.Headers.UserAgent.ParseAdd(BrowserUserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "text/csv,*/*;q=0.8");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 8192);
        IReadOnlyList<string>? rawHeaders = null;
        IReadOnlyList<string>? headers = null;
        var rowNumber = 0;
        var inserted = 0;
        var updated = 0;
        var nameMatched = 0;
        var skipped = 0;
        var processed = 0;

        await foreach (var row in ReadCsvRowsAsync(reader, _maxBytesPerResponse, source.Name, ct).ConfigureAwait(false))
        {
            rowNumber++;
            if (rowNumber == 1)
            {
                rawHeaders = row;
                headers = CsvParser.NormalizeHeaderRow(rawHeaders);
                continue;
            }

            ct.ThrowIfCancellationRequested();
            if (processed >= _maxRowsPerRun)
            {
                _logger.LogWarning("BC Major Projects Inventory row cap {MaxRows} reached; remaining rows skipped.", _maxRowsPerRun);
                break;
            }

            if (headers is null || rawHeaders is null)
            {
                skipped++;
                continue;
            }

            processed++;
            var record = await MapRowAsync(source, row, headers, rawHeaders, ct).ConfigureAwait(false);
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
            "BC Major Projects Inventory ingestion: inserted={Inserted} updated={Updated} nameMatched={NameMatched} skipped={Skipped} processed={Processed}.",
            inserted,
            updated,
            nameMatched,
            skipped,
            processed);

        if (headers is null)
        {
            _logger.LogWarning("BC Major Projects Inventory returned no header row; nothing ingested.");
        }

        return Array.Empty<OpportunityCandidate>();
    }

    private async Task<Uri> ResolveCsvUrlAsync(string baseUrl, CancellationToken ct)
    {
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var direct)
            && direct.AbsolutePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return direct;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl);
        request.Headers.UserAgent.ParseAdd(BrowserUserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json,*/*;q=0.8");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("resources", out var resources)
            || resources.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("BC Major Projects Inventory CKAN response did not contain result.resources.");
        }

        Uri? selected = null;
        DateTimeOffset selectedModified = DateTimeOffset.MinValue;
        foreach (var resource in resources.EnumerateArray())
        {
            if (!TryGetString(resource, "url", out var url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                continue;
            }

            var formatIsCsv = TryGetString(resource, "format", out var format)
                && string.Equals(format.Trim(), "CSV", StringComparison.OrdinalIgnoreCase);
            var urlIsCsv = uri.AbsolutePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
            if (!formatIsCsv && !urlIsCsv)
            {
                continue;
            }

            var modified = MaxDate(
                TryGetDate(resource, "last_modified"),
                TryGetDate(resource, "created"));
            if (selected is null || modified > selectedModified)
            {
                selected = uri;
                selectedModified = modified;
            }
        }

        return selected ?? throw new InvalidOperationException("BC Major Projects Inventory CKAN response did not contain a CSV resource.");
    }

    private async Task<MajorProjectRecord?> MapRowAsync(
        OpportunitySource source,
        IReadOnlyList<string> row,
        IReadOnlyList<string> headers,
        IReadOnlyList<string> rawHeaders,
        CancellationToken ct)
    {
        var projectName = Read(row, headers, "PROJECT_NAME");
        var projectId = Read(row, headers, "PROJECT_ID");
        if (string.IsNullOrWhiteSpace(projectName) || string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        var description = Read(row, headers, "PROJECT_DESCRIPTION");
        var proponent = Read(row, headers, "DEVELOPER");
        var architect = Read(row, headers, "ARCHITECT");
        var sector = Read(row, headers, "CONSTRUCTION_TYPE");
        var subSector = FirstNonBlank(
            Read(row, headers, "CONSTRUCTION_SUBTYPE"),
            Read(row, headers, "PROJECT_TYPE"));
        var stage = FirstNonBlank(
            Read(row, headers, "PROJECT_STAGE"),
            Read(row, headers, "PROJECT_STATUS"));
        var municipality = Read(row, headers, "MUNICIPALITY");
        var region = MapRegion(Read(row, headers, "REGION"));
        var costText = Read(row, headers, "ESTIMATED_COST");
        var sourceUrl = FirstNonBlank(Read(row, headers, "PROJECT_WEBSITE"), source.BaseUrl);

        var relevance = StructuralRelevanceGate.Evaluate(
            projectName,
            string.Join(" — ", new[] { sector, subSector, description }.Where(s => !string.IsNullOrWhiteSpace(s))!),
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

        var rawJson = BuildRowJson(row, rawHeaders);

        return new MajorProjectRecord(
            Province,
            BuildSourceKey(projectId),
            projectName,
            sector,
            subSector,
            ParseCostMillions(costText),
            costText,
            stage,
            proponent,
            proponentCanonicalId,
            architect,
            architectCanonicalId,
            municipality,
            region,
            ParseQuarterYear(Read(row, headers, "STANDARDIZED_START_DATE")),
            ParseQuarterYear(Read(row, headers, "STANDARDIZED_COMPLETION_DATE")),
            description,
            sourceUrl,
            rawJson);
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
    -- C9 guard: the same project arriving via a different SourceKey must not fork
    -- a duplicate row. Match active rows on normalized name + compatible
    -- municipality; generic names known to repeat across distinct projects are
    -- exempt from the check.
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
        -- Same project under another SourceKey: refresh seen timestamps and
        -- COALESCE-fill gaps only; never overwrite existing non-null values.
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
                "BC Major Projects Inventory name-matched existing MPI {MatchedId}: project '{ProjectName}' arrived with new SourceKey {SourceKey}; merged into existing row instead of inserting.",
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

    private static string? Read(IReadOnlyList<string> row, IReadOnlyList<string> headers, params string[] names) =>
        CsvParser.FirstValue(row, headers, names);

    private static decimal? ParseCostMillions(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var numeric = Regex.Replace(value.Trim(), @"[$,\s]", "");
        return decimal.TryParse(numeric, NumberStyles.Number | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            ? decimal.Round(parsed * 1_000_000m, 0)
            : null;
    }

    private static short? ParseQuarterYear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = QuarterYear.Match(value);
        return match.Success
            && short.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            ? year
            : null;
    }

    private static string? MapRegion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = LeadingRegionOrdinal.Replace(value.Trim(), "");
        return cleaned switch
        {
            _ when cleaned.Equals("Mainland/Southwest", StringComparison.OrdinalIgnoreCase) => "Lower Mainland",
            _ when cleaned.Equals("Vancouver Island/Coast", StringComparison.OrdinalIgnoreCase) => "Vancouver Island",
            _ when cleaned.Equals("Thompson-Okanagan", StringComparison.OrdinalIgnoreCase) => "Okanagan",
            _ => cleaned,
        };
    }

    private static string BuildSourceKey(string projectId)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(projectId.Trim().ToUpperInvariant()));
        return Province + "-" + Convert.ToHexString(hash);
    }

    private static string BuildRowJson(IReadOnlyList<string> row, IReadOnlyList<string> rawHeaders)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rawHeaders.Count && i < row.Count; i++)
        {
            var key = rawHeaders[i].Trim();
            if (key.Length > 0)
            {
                dict[key] = row[i] ?? string.Empty;
            }
        }

        return JsonSerializer.Serialize(dict);
    }

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

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = "";
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? "";
        return !string.IsNullOrWhiteSpace(value);
    }

    private static DateTimeOffset TryGetDate(JsonElement element, string propertyName)
    {
        return TryGetString(element, propertyName, out var raw)
            && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
    }

    private static DateTimeOffset MaxDate(DateTimeOffset first, DateTimeOffset second) =>
        first >= second ? first : second;

    private static async IAsyncEnumerable<IReadOnlyList<string>> ReadCsvRowsAsync(
        StreamReader reader,
        int maxBytes,
        string sourceName,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var totalBytes = 0;
        var buffer = new char[8192];

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            totalBytes += Encoding.UTF8.GetByteCount(buffer.AsSpan(0, read));
            if (totalBytes > maxBytes)
            {
                throw new InvalidOperationException(
                    $"BC Major Projects Inventory response for {sourceName} exceeded configured cap of {maxBytes} bytes.");
            }

            for (var i = 0; i < read; i++)
            {
                var c = buffer[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < read && buffer[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }

                    continue;
                }

                if (c == '"')
                {
                    inQuotes = true;
                    continue;
                }

                if (c == ',')
                {
                    row.Add(field.ToString());
                    field.Clear();
                    continue;
                }

                if (c == '\r')
                {
                    continue;
                }

                if (c == '\n')
                {
                    row.Add(field.ToString());
                    field.Clear();
                    yield return row;
                    row = new List<string>();
                    continue;
                }

                field.Append(c);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            yield return row;
        }
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
