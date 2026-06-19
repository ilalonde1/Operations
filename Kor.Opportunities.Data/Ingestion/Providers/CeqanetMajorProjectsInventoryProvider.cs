#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
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
/// Scrapes CEQAnet recent filings and writes structurally relevant projects
/// directly to MajorProjectsInventory.
/// </summary>
public sealed class CeqanetMajorProjectsInventoryProvider : IOpportunityProvider
{
    private const string Province = "CA";
    private const string ProponentAliasSource = "MajorProjectsInventory.Proponent";
    private const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36";
    private static readonly Regex SchRegex = new(@"\b((?:19|20)\d{8,10})\b", RegexOptions.Compiled);
    private static readonly Regex YearRegex = new(@"\b(19|20)\d{2}\b", RegexOptions.Compiled);
    private static readonly Regex AnchorRegex = new(@"<a\b[^>]*href\s*=\s*[""'](?<href>[^""']+)[""'][^>]*>(?<text>.*?)</a>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex TableRowRegex = new(@"<tr\b[^>]*>(?<row>.*?)</tr>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex TableCellRegex = new(@"<td[^>]*>(.*?)</td>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private readonly HttpClient _httpClient;
    private readonly string _connectionString;
    private readonly CanonicalOrgResolver _canonicalResolver;
    private readonly ILogger<CeqanetMajorProjectsInventoryProvider> _logger;
    private readonly int _maxBytesPerResponse;
    private readonly int _maxRowsPerRun;

    public CeqanetMajorProjectsInventoryProvider(
        HttpClient httpClient,
        string connectionString,
        CanonicalOrgResolver canonicalResolver,
        ILogger<CeqanetMajorProjectsInventoryProvider> logger,
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
        var paceMs = ParseInt(Get(sourceConfig, "paceMilliseconds"), 750);
        if (paceMs > 0)
        {
            await Task.Delay(Math.Min(paceMs, 5000), ct).ConfigureAwait(false);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, source.BaseUrl);
        request.Headers.UserAgent.ParseAdd(BrowserUserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,*/*;q=0.8");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var html = await ReadHtmlAsync(response, source.Name, ct).ConfigureAwait(false);

        var inserted = 0;
        var updated = 0;
        var nameMatched = 0;
        var skipped = 0;
        var processed = 0;

        foreach (var filing in ParseFilings(html, source.BaseUrl).Take(_maxRowsPerRun))
        {
            ct.ThrowIfCancellationRequested();
            processed++;
            var record = await MapFilingAsync(filing, ct).ConfigureAwait(false);
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
            "CA CEQAnet Major Projects Inventory ingestion: inserted={Inserted} updated={Updated} nameMatched={NameMatched} skipped={Skipped} processed={Processed}.",
            inserted,
            updated,
            nameMatched,
            skipped,
            processed);

        return Array.Empty<OpportunityCandidate>();
    }

    private async Task<MajorProjectRecord?> MapFilingAsync(CeqaFiling filing, CancellationToken ct)
    {
        var projectText = string.Join(" - ", new[] { filing.Title, filing.Description, filing.DocumentType, filing.County }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!IsLaneProject(projectText))
        {
            return null;
        }

        var relevance = StructuralRelevanceGate.Evaluate(filing.Title, projectText, filing.LeadAgency);
        if (!relevance.Keep)
        {
            return null;
        }

        var leadAgencyCanonicalId = !string.IsNullOrWhiteSpace(filing.LeadAgency)
            ? await _canonicalResolver.ResolveAsync(
                filing.LeadAgency,
                OrgKinds.Unknown,
                ProponentAliasSource,
                ct,
                allowCreate: true,
                minConfidenceForCreate: 70).ConfigureAwait(false)
            : null;

        var rawJson = JsonSerializer.Serialize(filing);
        return new MajorProjectRecord(
            Province,
            "ceqa:" + filing.SchNumber,
            filing.Title,
            "CEQA Filing",
            filing.DocumentType,
            null,
            null,
            FirstNonBlank(filing.DocumentType, "CEQA filing"),
            filing.LeadAgency,
            leadAgencyCanonicalId,
            null,
            null,
            null,
            filing.County,
            ParseYear(filing.ReceivedDate),
            null,
            FirstNonBlank(filing.Description, filing.ReceivedDate),
            filing.SourceUrl,
            rawJson);
    }

    private static IReadOnlyList<CeqaFiling> ParseFilings(string html, string baseUrl)
    {
        var filings = new List<CeqaFiling>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match rowMatch in TableRowRegex.Matches(html))
        {
            var rowHtml = rowMatch.Groups["row"].Value;
            var cells = TableCellRegex.Matches(rowHtml)
                .Cast<Match>()
                .Select(m => CleanText(m.Groups[1].Value))
                .ToList();
            if (cells.Count < 5)
            {
                continue;
            }

            var sch = SchRegex.Match(cells[0]);
            if (!sch.Success || !seen.Add(sch.Value))
            {
                continue;
            }

            var hrefMatch = AnchorRegex.Match(rowHtml);
            var href = hrefMatch.Success
                ? WebUtility.HtmlDecode(hrefMatch.Groups["href"].Value)
                : null;
            filings.Add(new CeqaFiling(
                sch.Value,
                cells[1],
                cells[2],
                cells[3],
                cells[4],
                null,
                null,
                !string.IsNullOrWhiteSpace(href) ? AbsoluteUrl(baseUrl, href) : baseUrl));
        }

        if (filings.Count > 0)
        {
            return filings;
        }

        foreach (Match anchor in AnchorRegex.Matches(html))
        {
            var href = WebUtility.HtmlDecode(anchor.Groups["href"].Value);
            var anchorText = CleanText(anchor.Groups["text"].Value);
            var combined = href + " " + anchorText;
            var sch = SchRegex.Match(combined);
            if (!sch.Success || !seen.Add(sch.Value))
            {
                continue;
            }

            var segment = SurroundingSegment(html, anchor.Index, anchor.Length);
            var cleaned = CleanText(segment);
            filings.Add(new CeqaFiling(
                sch.Value,
                ExtractDocumentType(cleaned),
                ExtractAfter(cleaned, "Lead Agency", "Lead Agency:"),
                ExtractAfter(cleaned, "Received", "Received Date", "Received:"),
                FirstNonBlank(anchorText, ExtractTitle(cleaned, sch.Value), "CEQA " + sch.Value)!,
                ExtractDescription(cleaned, anchorText),
                ExtractAfter(cleaned, "County", "County:"),
                AbsoluteUrl(baseUrl, href)));
        }

        if (filings.Count > 0)
        {
            return filings;
        }

        foreach (Match match in SchRegex.Matches(CleanText(html)))
        {
            if (!seen.Add(match.Value))
            {
                continue;
            }

            var segment = SurroundingText(CleanText(html), match.Index, 2000);
            filings.Add(new CeqaFiling(
                match.Value,
                ExtractDocumentType(segment),
                ExtractAfter(segment, "Lead Agency", "Lead Agency:"),
                ExtractAfter(segment, "Received", "Received Date", "Received:"),
                FirstNonBlank(ExtractTitle(segment, match.Value), "CEQA " + match.Value)!,
                ExtractDescription(segment, null),
                ExtractAfter(segment, "County", "County:"),
                baseUrl));
        }

        return filings;
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
                "CA CEQAnet Major Projects Inventory name-matched existing MPI {MatchedId}: project '{ProjectName}' arrived with new SourceKey {SourceKey}; merged into existing row instead of inserting.",
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

    private async Task<string> ReadHtmlAsync(HttpResponseMessage response, string sourceName, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var content = new System.IO.MemoryStream();
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
                    $"CA CEQAnet response for {sourceName} exceeded configured cap of {_maxBytesPerResponse} bytes.");
            }

            await content.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        return System.Text.Encoding.UTF8.GetString(content.ToArray());
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

    private static bool IsLaneProject(string value)
    {
        var lower = value.ToLowerInvariant();
        return Regex.IsMatch(lower, @"\b(residential|housing|apartment|apartments|multifamily|multi-family|condo|condominium|mixed[- ]use|hotel|commercial|office|retail|school|campus|hospital|medical)\b")
            && !Regex.IsMatch(lower, @"\b(road|bridge|highway|freeway|utility|utilities|trail|pipeline|sewer|water main|transmission|substation|solar|wind|battery|rail|transit)\b");
    }

    private static string? ExtractDocumentType(string text)
    {
        var match = Regex.Match(text, @"\b(NOP|NOD|NOE|EIR|MND|ND|Addendum|Mitigated Negative Declaration|Environmental Impact Report|Notice of Determination)\b", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.Trim() : null;
    }

    private static string? ExtractAfter(string text, params string[] labels)
    {
        foreach (var label in labels)
        {
            var pattern = Regex.Escape(label) + @"\s*:?\s*(?<value>.{1,160}?)(?=\s{2,}| County\s*:| Lead Agency\s*:| Received(?: Date)?\s*:| Document Type\s*:|$)";
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return CleanPlainText(match.Groups["value"].Value);
            }
        }

        return null;
    }

    private static string? ExtractTitle(string text, string sch)
    {
        var idx = text.IndexOf(sch, StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }

        var after = text[(idx + sch.Length)..].Trim(' ', '-', ':');
        if (after.Length == 0)
        {
            return null;
        }

        var stop = Regex.Match(after, @"\s{2,}| Lead Agency\s*:| County\s*:| Received", RegexOptions.IgnoreCase);
        return CleanPlainText(stop.Success ? after[..stop.Index] : after.Length > 180 ? after[..180] : after);
    }

    private static string? ExtractDescription(string text, string? title)
    {
        var description = ExtractAfter(text, "Description", "Project Description", "Summary");
        if (!string.IsNullOrWhiteSpace(description))
        {
            return description;
        }

        var cleaned = CleanPlainText(text);
        if (!string.IsNullOrWhiteSpace(title))
        {
            cleaned = cleaned.Replace(title, "", StringComparison.OrdinalIgnoreCase);
        }

        return cleaned.Length > 1000 ? cleaned[..1000] : cleaned;
    }

    private static string SurroundingSegment(string html, int index, int length)
    {
        var start = Math.Max(0, html.LastIndexOf("<tr", index, StringComparison.OrdinalIgnoreCase));
        var end = html.IndexOf("</tr>", index + length, StringComparison.OrdinalIgnoreCase);
        if (start >= 0 && end > start)
        {
            return html[start..(end + 5)];
        }

        start = Math.Max(0, index - 1200);
        end = Math.Min(html.Length, index + length + 1800);
        return html[start..end];
    }

    private static string SurroundingText(string text, int index, int width)
    {
        var start = Math.Max(0, index - width / 2);
        var end = Math.Min(text.Length, index + width / 2);
        return text[start..end];
    }

    private static string CleanText(string html)
    {
        var noScript = Regex.Replace(html, "<script.*?</script>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        noScript = Regex.Replace(noScript, "<style.*?</style>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var text = Regex.Replace(noScript, "<[^>]+>", " ");
        return CleanPlainText(WebUtility.HtmlDecode(text));
    }

    private static string CleanPlainText(string text) =>
        Regex.Replace((text ?? "").Trim(), @"\s+", " ");

    private static string AbsoluteUrl(string baseUrl, string href)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        return Uri.TryCreate(new Uri(baseUrl), href, out var relative)
            ? relative.ToString()
            : baseUrl;
    }

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

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

    private static string? Get(IReadOnlyDictionary<string, string> config, string key) =>
        config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
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

    private sealed record CeqaFiling(
        string SchNumber,
        string? DocumentType,
        string? LeadAgency,
        string? ReceivedDate,
        string Title,
        string? Description,
        string? County,
        string SourceUrl);

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
