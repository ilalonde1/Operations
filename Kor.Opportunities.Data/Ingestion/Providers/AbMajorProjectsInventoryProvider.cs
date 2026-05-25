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
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Ingestion.Providers;

/// <summary>
/// Ingests Alberta's Major Projects Inventory into its dedicated table. This
/// provider is wired through IOpportunityProvider only so existing source
/// dispatch and IngestionTriggers can invoke it; it does not emit opportunity
/// candidates.
/// </summary>
public sealed class AbMajorProjectsInventoryProvider : IOpportunityProvider
{
    private const string Province = "AB";
    private const string AliasSource = "MajorProjectsInventory.Proponent";
    private const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36";
    private static readonly Regex YearRegex = new(@"\b(19|20)\d{2}\b", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly string _connectionString;
    private readonly CanonicalOrgResolver _canonicalResolver;
    private readonly ILogger<AbMajorProjectsInventoryProvider> _logger;
    private readonly int _maxBytesPerResponse;
    private readonly int _maxRowsPerRun;

    public AbMajorProjectsInventoryProvider(
        HttpClient httpClient,
        string connectionString,
        CanonicalOrgResolver canonicalResolver,
        ILogger<AbMajorProjectsInventoryProvider> logger,
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
        using var request = new HttpRequestMessage(HttpMethod.Get, source.BaseUrl);
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
                _logger.LogWarning("AB Major Projects Inventory row cap {MaxRows} reached; remaining rows skipped.", _maxRowsPerRun);
                break;
            }

            processed++;
            var record = await MapRowAsync(source, row, headers, rawHeaders, ct).ConfigureAwait(false);
            if (record is null)
            {
                skipped++;
                continue;
            }

            var wasInserted = await UpsertAsync(record, ct).ConfigureAwait(false);
            if (wasInserted)
            {
                inserted++;
            }
            else
            {
                updated++;
            }
        }

        _logger.LogInformation(
            "AB Major Projects Inventory ingestion: inserted={Inserted} updated={Updated} skipped={Skipped} processed={Processed}.",
            inserted,
            updated,
            skipped,
            processed);

        if (headers is null)
        {
            _logger.LogWarning("AB Major Projects Inventory returned no header row; nothing ingested.");
        }

        return Array.Empty<OpportunityCandidate>();
    }

    private async Task<MajorProjectRecord?> MapRowAsync(
        OpportunitySource source,
        IReadOnlyList<string> row,
        IReadOnlyList<string> headers,
        IReadOnlyList<string> rawHeaders,
        CancellationToken ct)
    {
        var projectName = Read(row, headers, "Name", "Project Name", "ProjectName");
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return null;
        }

        var municipality = Read(row, headers, "From Municipality", "Municipality", "Location");
        var region = Read(row, headers, "Location", "To Municipality", "Region");
        var proponent = Read(row, headers, "Developer", "Proponent", "Owner");
        var schedule = Read(row, headers, "Schedule");
        var costText = Read(row, headers, "Cost", "Estimated Cost ($ Million)", "Estimated Cost");
        var website = Read(row, headers, "Website");
        var details = Read(row, headers, "Details");
        var sourceUrl = !string.IsNullOrWhiteSpace(website) ? website : source.BaseUrl;
        var canonicalId = !string.IsNullOrWhiteSpace(proponent)
            ? await _canonicalResolver.ResolveAsync(
                proponent,
                OrgKinds.Unknown,
                AliasSource,
                ct,
                allowCreate: true,
                minConfidenceForCreate: 70).ConfigureAwait(false)
            : null;

        var (startYear, completionYear) = ParseScheduleYears(schedule);
        var rawJson = BuildRowJson(row, rawHeaders);

        return new MajorProjectRecord(
            Province,
            BuildSourceKey(projectName, municipality, proponent),
            projectName,
            Read(row, headers, "Sector"),
            Read(row, headers, "Type", "SubSector"),
            ParseCost(costText),
            costText,
            Read(row, headers, "Stage"),
            proponent,
            canonicalId,
            municipality,
            region,
            startYear,
            completionYear,
            FirstNonBlank(schedule, details),
            sourceUrl,
            rawJson);
    }

    private async Task<bool> UpsertAsync(MajorProjectRecord record, CancellationToken ct)
    {
        const string sql = @"
SET XACT_ABORT ON;

DECLARE @inserted table (Id bigint NOT NULL);

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
    INSERT INTO opportunities.MajorProjectsInventory
        (Province, SourceKey, ProjectName, Sector, SubSector, EstimatedCostCad,
         EstimatedCostText, Stage, ProponentName, ProponentCanonicalOrgId,
         MunicipalityName, RegionName, StartYear, CompletionYear, ScheduleNotes,
         SourceUrl, RawJson)
    OUTPUT inserted.Id INTO @inserted
    VALUES
        (@province, @sourceKey, @projectName, @sector, @subSector, @estimatedCostCad,
         @estimatedCostText, @stage, @proponentName, @proponentCanonicalOrgId,
         @municipalityName, @regionName, @startYear, @completionYear, @scheduleNotes,
         @sourceUrl, @rawJson);
END;

COMMIT TRAN;

SELECT CASE WHEN EXISTS (SELECT 1 FROM @inserted) THEN 1 ELSE 0 END;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        AddParams(cmd, record);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
    }

    private static void AddParams(SqlCommand cmd, MajorProjectRecord record)
    {
        cmd.Parameters.Add("@province", SqlDbType.NVarChar, 2).Value = record.Province;
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

    private static decimal? ParseCost(string? value)
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

    private static (short? StartYear, short? CompletionYear) ParseScheduleYears(string? schedule)
    {
        if (string.IsNullOrWhiteSpace(schedule))
        {
            return (null, null);
        }

        var years = new List<short>();
        foreach (Match match in YearRegex.Matches(schedule))
        {
            if (short.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
            {
                years.Add(year);
            }
        }

        return years.Count switch
        {
            0 => (null, null),
            1 => (years[0], years[0]),
            _ => (years[0], years[^1]),
        };
    }

    private static string BuildSourceKey(string projectName, string? municipality, string? proponent)
    {
        var keyInput = $"{projectName.Trim()}|{municipality?.Trim()}|{proponent?.Trim()}";
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(keyInput.ToUpperInvariant()));
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
                    $"AB Major Projects Inventory response for {sourceName} exceeded configured cap of {maxBytes} bytes.");
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
        string? MunicipalityName,
        string? RegionName,
        short? StartYear,
        short? CompletionYear,
        string? ScheduleNotes,
        string? SourceUrl,
        string RawJson);
}
