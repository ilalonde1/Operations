// One-shot importer for the APC historical-data CSV
// (https://purchasing.alberta.ca/historical-data — point-in-time June 7, 2024
// snapshot of all open/closed/awarded postings from the legacy APC platform).
//
// ALREADY RUN — DO NOT RE-RUN (audit-v2 sweep). This tool MERGEs award rows
// with raw org strings and no canonical resolution; that was flagged as a
// resolver-bypass. Deliberately NOT retrofitted: the snapshot never updates,
// the import is complete, and OpportunityAwards.Awarding/AwardedTo are in
// CanonicalColumnRegistry — the weekly CanonicalLinkBackfillJob links this
// tool's historical rows. Any future re-import must go through
// SqlOpportunityAwardStore's resolver overload (see GovCanEngineeringImport).
//
// Why a separate tool: this is a 124MB / 95,663-row bulk import that runs
// ONCE (the source file is never updated). Pulling it through the regular
// IngestionService + IIngestionRunStore + per-row Worker scrape would be
// massively slower than direct ADO + a shared connection.
//
// Output: rows with Opportunity Status == AWARDED get upserted into
// opportunities.OpportunityAwards under a dedicated source row,
// "APC_Historical_2024" (SourceType = AlbertaPurchasingConnectionAwards = 13).
// Winner name + address are parsed out of the CSV's formatted_award_info
// column. Interested-suppliers list (competitor intel) is preserved in the
// RawJson column for downstream analytics.
//
// Usage:
//   dotnet run --project Kor.Opportunities.ApcImport --
//       [csv path]
//       [--connection "Server=..."]
//       [--limit N]               (smoke test small sample)
//       [--include-closed]        (also import CLOSED status, not just AWARDED)

using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Data.SqlClient;

internal static class Program
{
    private const string DefaultCsvPath = @"C:\Users\ilalonde\Desktop\Claude\APC_historical_data_June_7_2024.csv";
    // BD-Audit-2026-06-09 C7: the connection string (with the opportunities_app
    // password) used to be a compiled-in constant — the only tool in the
    // pipeline that didn't read the standard env var. Resolved at startup now;
    // the credential itself must be rotated since it lives in git history.
    private static readonly string DefaultConnectionString =
        Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB")
        ?? throw new InvalidOperationException("KOR_OPPORTUNITIES_OPPORTUNITIESDB env var missing (pass --db to override).");
    private const string SourceName = "APC_Historical_2024";
    private const int SourceTypeAlbertaPurchasingConnectionAwards = 13;

    // formatted_award_info layouts seen in production CSV (varies by year):
    //   "{Vendor}   {YYYY-MM-DD} {address}"                               (older rows, no $)
    //   "{Vendor} ${value} Awarded Contract {YYYY-MM-DD} {address}"       (newer rows, with $ + status)
    //   "{Vendor} ${value} Contract {YYYY-MM-DD} {address}"               (variant)
    // Multiple winners separated by " | ".
    // Vendor is lazy, date is the anchor; $value + status phrase are optional.
    private static readonly Regex AwardInfoRegex = new(
        @"^\s*(?<vendor>.+?)\s+(?:\$(?<value>[\d,]+(?:\.\d{2})?)\s+(?:Awarded\s+)?(?:Contract\s+)?)?(?<date>\d{4}-\d{2}-\d{2})\s+(?<addr>.+?)\s*$",
        RegexOptions.Compiled);

    // APC commonly appends a trailing "    -AB" / "-BC" / "-NWT" location code
    // to the Opportunity Title — strip it for cleaner display.
    private static readonly Regex TitleLocationSuffixRegex = new(
        @"\s+-(?:AB|BC|SK|MB|ON|QC|NS|NB|PE|NL|YT|NT|NWT|NU|US|USA)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var opts = ParseArgs(args);
            Console.WriteLine();
            Console.WriteLine("=== APC historical CSV importer ===");
            Console.WriteLine($"CSV path        : {opts.CsvPath}");
            Console.WriteLine($"Connection      : {Redact(opts.ConnectionString)}");
            Console.WriteLine($"Statuses to import: AWARDED{(opts.IncludeClosed ? " + CLOSED" : "")}");
            Console.WriteLine($"Row limit       : {(opts.Limit is null ? "(no limit)" : opts.Limit.ToString())}");
            Console.WriteLine();

            if (!File.Exists(opts.CsvPath))
            {
                Console.Error.WriteLine($"CSV not found at {opts.CsvPath}");
                return 2;
            }

            await using var connection = new SqlConnection(opts.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var sourceId = await EnsureSourceRowAsync(connection).ConfigureAwait(false);
            Console.WriteLine($"OpportunitySource '{SourceName}' Id = {sourceId}");

            var runId = await StartIngestionRunAsync(connection, sourceId).ConfigureAwait(false);
            Console.WriteLine($"IngestionRun Id  = {runId}");
            Console.WriteLine();

            var stats = await ImportAsync(opts, connection, sourceId, runId).ConfigureAwait(false);

            await CompleteIngestionRunAsync(connection, runId, stats).ConfigureAwait(false);

            Console.WriteLine();
            Console.WriteLine("=== Import complete ===");
            Console.WriteLine($"Rows read       : {stats.RowsRead:N0}");
            Console.WriteLine($"Rows imported   : {stats.RowsImported:N0}");
            Console.WriteLine($"Rows skipped    : {stats.RowsSkipped:N0}  (status filter or missing key fields)");
            Console.WriteLine($"Parse warnings  : {stats.ParseWarnings:N0}");
            Console.WriteLine($"DB errors       : {stats.DbErrors:N0}");
            Console.WriteLine($"Elapsed         : {stats.Elapsed:hh\\:mm\\:ss}");
            return stats.DbErrors == 0 ? 0 : 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Importer failed: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 99;
        }
    }

    private static async Task<Guid> EnsureSourceRowAsync(SqlConnection con)
    {
        // Idempotent — re-running the importer reuses the same source row + appends
        // a new IngestionRun. The MERGE on (SourceId, ExternalReference) further
        // upstream handles row-level dedup.
        const string sql = @"
DECLARE @Id uniqueidentifier;
SELECT @Id = Id FROM opportunities.OpportunitySources WHERE Name = @name;
IF @Id IS NULL
BEGIN
    SET @Id = NEWID();
    INSERT INTO opportunities.OpportunitySources
        (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds, CreatedAtUtc, UpdatedAtUtc)
    VALUES
        (@Id, @name, @sourceType, @baseUrl, 0, 0, 0, sysdatetimeoffset(), sysdatetimeoffset());
END
SELECT @Id;";
        await using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = SourceName;
        cmd.Parameters.Add("@sourceType", SqlDbType.Int).Value = SourceTypeAlbertaPurchasingConnectionAwards;
        cmd.Parameters.Add("@baseUrl", SqlDbType.NVarChar, 800).Value = "https://purchasing.alberta.ca/historical-data";
        var id = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
        return (Guid)id!;
    }

    private static async Task<Guid> StartIngestionRunAsync(SqlConnection con, Guid sourceId)
    {
        var runId = Guid.NewGuid();
        const string sql = @"
INSERT INTO opportunities.IngestionRuns
    (Id, StartedAtUtc, EndedAtUtc, ProviderName, Success, InsertedCount, DuplicateCount, SkippedCount, FailedCount,
     ErrorSummary, HostInstance, CorrelationId)
VALUES
    (@id, sysdatetimeoffset(), NULL, @name, 0, 0, 0, 0, 0,
     NULL, @host, @corr);";
        await using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = runId;
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = $"Awards: {SourceName} (csv-import)";
        cmd.Parameters.Add("@host", SqlDbType.NVarChar, 200).Value = $"{Environment.MachineName}/csv-import";
        cmd.Parameters.Add("@corr", SqlDbType.NVarChar, 100).Value = (object?)null ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        return runId;
    }

    private static async Task CompleteIngestionRunAsync(SqlConnection con, Guid runId, ImportStats stats)
    {
        const string sql = @"
UPDATE opportunities.IngestionRuns
   SET EndedAtUtc = sysdatetimeoffset(),
       Success = @success,
       InsertedCount = @ins,
       DuplicateCount = 0,
       SkippedCount = @skipped,
       FailedCount = @failed,
       ErrorSummary = @err
 WHERE Id = @id;";
        await using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = runId;
        cmd.Parameters.Add("@success", SqlDbType.Bit).Value = stats.DbErrors == 0;
        cmd.Parameters.Add("@ins", SqlDbType.Int).Value = stats.RowsImported;
        cmd.Parameters.Add("@skipped", SqlDbType.Int).Value = stats.RowsSkipped + stats.ParseWarnings;
        cmd.Parameters.Add("@failed", SqlDbType.Int).Value = stats.DbErrors;
        cmd.Parameters.Add("@err", SqlDbType.NVarChar, -1).Value = stats.DbErrors > 0
            ? $"{stats.DbErrors} DB errors during CSV import — see console output"
            : (object)DBNull.Value;
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<ImportStats> ImportAsync(Options opts, SqlConnection con, Guid sourceId, Guid runId)
    {
        var stats = new ImportStats { Started = DateTime.UtcNow };

        // Prepare a reusable SqlCommand — saves time over re-binding on every row.
        const string sql = @"
MERGE opportunities.OpportunityAwards AS target
USING (SELECT @sourceId AS OpportunitySourceId, @extRef AS ExternalReference) AS src
   ON target.OpportunitySourceId = src.OpportunitySourceId
  AND target.ExternalReference = src.ExternalReference
WHEN MATCHED THEN UPDATE SET
    Title = @title,
    SolicitationType = @stype,
    AwardingOrganization = @awardingOrg,
    AwardedToOrganization = @awardedTo,
    ContractValue = @value,
    ContractCurrency = @currency,
    AwardedAtUtc = @awardedAt,
    IssuingLocation = @issLoc,
    SupplierAddress = @supAddr,
    ContactEmail = @contactEmail,
    ContractNumber = @contractNumber,
    SourceUrl = @url,
    RawJson = @raw,
    UpdatedAtUtc = sysdatetimeoffset(),
    IngestionRunId = COALESCE(@runId, target.IngestionRunId)
WHEN NOT MATCHED THEN INSERT
    (ExternalReference, OpportunitySourceId, Title, SolicitationType,
     AwardingOrganization, AwardedToOrganization, ContractValue, ContractCurrency,
     AwardedAtUtc, IssuingLocation, SupplierAddress, ContactEmail, ContractNumber,
     SourceUrl, RawJson, IngestionRunId)
VALUES
    (@extRef, @sourceId, @title, @stype,
     @awardingOrg, @awardedTo, @value, @currency,
     @awardedAt, @issLoc, @supAddr, @contactEmail, @contractNumber,
     @url, @raw, @runId);";
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        var pSourceId = cmd.Parameters.Add("@sourceId", SqlDbType.UniqueIdentifier);
        var pExtRef = cmd.Parameters.Add("@extRef", SqlDbType.NVarChar, 200);
        var pTitle = cmd.Parameters.Add("@title", SqlDbType.NVarChar, 400);
        var pStype = cmd.Parameters.Add("@stype", SqlDbType.NVarChar, 150);
        var pAwardingOrg = cmd.Parameters.Add("@awardingOrg", SqlDbType.NVarChar, 300);
        var pAwardedTo = cmd.Parameters.Add("@awardedTo", SqlDbType.NVarChar, 300);
        var pValue = cmd.Parameters.Add("@value", SqlDbType.Decimal);
        pValue.Precision = 18;
        pValue.Scale = 2;
        var pCurrency = cmd.Parameters.Add("@currency", SqlDbType.Char, 3);
        var pAwardedAt = cmd.Parameters.Add("@awardedAt", SqlDbType.DateTimeOffset);
        var pIssLoc = cmd.Parameters.Add("@issLoc", SqlDbType.NVarChar, 300);
        var pSupAddr = cmd.Parameters.Add("@supAddr", SqlDbType.NVarChar, 500);
        var pContactEmail = cmd.Parameters.Add("@contactEmail", SqlDbType.NVarChar, 200);
        var pContractNumber = cmd.Parameters.Add("@contractNumber", SqlDbType.NVarChar, 150);
        var pUrl = cmd.Parameters.Add("@url", SqlDbType.NVarChar, 800);
        var pRaw = cmd.Parameters.Add("@raw", SqlDbType.NVarChar, -1);
        var pRunId = cmd.Parameters.Add("@runId", SqlDbType.UniqueIdentifier);

        pSourceId.Value = sourceId;
        pCurrency.Value = "CAD";
        pRunId.Value = runId;

        using var reader = new StreamReader(opts.CsvPath);
        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = null,        // tolerate stray quotes — common in legacy text
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim,
        };
        using var csv = new CsvReader(reader, csvConfig);
        await csv.ReadAsync().ConfigureAwait(false);
        csv.ReadHeader();

        while (await csv.ReadAsync().ConfigureAwait(false))
        {
            stats.RowsRead++;
            if (opts.Limit is int limit && stats.RowsRead > limit) break;

            var status = csv.GetField<string?>("Opportunity Status")?.Trim().ToUpperInvariant();
            var statusMatches = status == "AWARDED" || (opts.IncludeClosed && status == "CLOSED");
            if (!statusMatches) { stats.RowsSkipped++; continue; }

            var refNumber = csv.GetField<string?>("Reference Number")?.Trim();
            var title = csv.GetField<string?>("Opportunity Title")?.Trim();
            if (string.IsNullOrWhiteSpace(refNumber) || string.IsNullOrWhiteSpace(title))
            {
                stats.RowsSkipped++;
                continue;
            }

            var ouName = csv.GetField<string?>("OU Name")?.Trim();
            var ouPath = csv.GetField<string?>("OU Path")?.Trim();
            var solicitationType = csv.GetField<string?>("Solicitation Type")?.Trim();
            var contactEmail = csv.GetField<string?>("Contact Email")?.Trim();
            var awardedDateRaw = csv.GetField<string?>("Awarded Date UTC")?.Trim();
            var closeDateRaw = csv.GetField<string?>("Close Date UTC")?.Trim();
            var awardInfo = csv.GetField<string?>("formatted_award_info")?.Trim();
            var bidInfo = csv.GetField<string?>("formatted_bid_info")?.Trim();
            var interestedSuppliers = csv.GetField<string?>("interested_suppliers")?.Trim();
            var category = csv.GetField<string?>("Category")?.Trim();
            var agreementType = csv.GetField<string?>("Agreement Type")?.Trim();

            var (winnerName, winnerAddr, contractValue, parseOk) = ParseAwardInfo(awardInfo);
            if (!parseOk && status == "AWARDED") stats.ParseWarnings++;

            // Strip APC's trailing "    -AB" location codes that appear on most titles.
            title = TitleLocationSuffixRegex.Replace(title, "").Trim();

            // Build a RawJson blob preserving the rich competitor-intel fields.
            var raw = BuildRawJson(
                ouName, ouPath, status, solicitationType, category, agreementType,
                awardInfo, bidInfo, interestedSuppliers);

            pExtRef.Value = ToParam(refNumber, 200);
            pTitle.Value = ToParam(title, 400);
            pStype.Value = ToParam(solicitationType, 150) ?? (object)DBNull.Value;
            pAwardingOrg.Value = ToParam(ouName, 300) ?? "Alberta Purchasing Connection";
            pAwardedTo.Value = string.IsNullOrWhiteSpace(winnerName) ? "Unknown" : ToParam(winnerName, 300)!;
            pValue.Value = (object?)contractValue ?? DBNull.Value;
            pAwardedAt.Value = ParseDate(awardedDateRaw) ?? ParseDate(closeDateRaw) ?? (object)DBNull.Value;
            pIssLoc.Value = ToParam(ouName, 300) ?? (object)DBNull.Value;
            pSupAddr.Value = ToParam(winnerAddr, 500) ?? (object)DBNull.Value;
            pContactEmail.Value = ToParam(contactEmail, 200) ?? (object)DBNull.Value;
            pContractNumber.Value = ToParam(csv.GetField<string?>("Solicitation Number")?.Trim(), 150) ?? (object)DBNull.Value;
            pUrl.Value = $"https://purchasing.alberta.ca/historical-data#{refNumber}";
            pRaw.Value = (object?)raw ?? DBNull.Value;

            try
            {
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                stats.RowsImported++;
            }
            catch (SqlException ex)
            {
                stats.DbErrors++;
                if (stats.DbErrors <= 10)
                {
                    Console.Error.WriteLine($"[row {stats.RowsRead}] {refNumber}: {ex.Message}");
                }
            }

            if (stats.RowsImported % 1000 == 0 && stats.RowsImported > 0)
            {
                Console.WriteLine($"  {stats.RowsImported:N0} imported  ({stats.RowsRead:N0} read; {stats.Elapsed:hh\\:mm\\:ss} elapsed)");
            }
        }

        return stats;
    }

    private static (string? vendor, string? address, decimal? value, bool ok) ParseAwardInfo(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null, null, false);

        // Multiple winners are pipe-delimited; take the first as the primary winner.
        var firstSegment = raw.Split('|', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        if (string.IsNullOrWhiteSpace(firstSegment)) return (null, null, null, false);

        var m = AwardInfoRegex.Match(firstSegment);
        if (!m.Success)
        {
            // Couldn't tease apart — keep the whole segment as the vendor and skip address/value.
            return (firstSegment, null, null, false);
        }

        var vendor = NormalizeSpaces(m.Groups["vendor"].Value);
        var address = NormalizeSpaces(m.Groups["addr"].Value);
        decimal? value = null;
        if (m.Groups["value"].Success
            && decimal.TryParse(
                m.Groups["value"].Value.Replace(",", ""),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var parsedValue))
        {
            value = parsedValue;
        }
        return (vendor, address, value, true);
    }

    private static string NormalizeSpaces(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    private static DateTimeOffset? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string BuildRawJson(
        string? ouName, string? ouPath, string? status, string? solicitationType,
        string? category, string? agreementType,
        string? awardInfo, string? bidInfo, string? interestedSuppliers)
    {
        // Minimal, escaped, fixed-key JSON — avoids pulling in a serializer dependency.
        var sb = new System.Text.StringBuilder(512);
        sb.Append('{');
        AppendJson(sb, "ouName", ouName); sb.Append(',');
        AppendJson(sb, "ouPath", ouPath); sb.Append(',');
        AppendJson(sb, "status", status); sb.Append(',');
        AppendJson(sb, "solicitationType", solicitationType); sb.Append(',');
        AppendJson(sb, "category", category); sb.Append(',');
        AppendJson(sb, "agreementType", agreementType); sb.Append(',');
        AppendJson(sb, "awardInfo", awardInfo); sb.Append(',');
        AppendJson(sb, "bidInfo", bidInfo); sb.Append(',');
        AppendJson(sb, "interestedSuppliers", interestedSuppliers);
        sb.Append('}');
        return sb.ToString();
    }

    private static void AppendJson(System.Text.StringBuilder sb, string key, string? value)
    {
        sb.Append('"').Append(key).Append("\":");
        if (value is null) { sb.Append("null"); return; }
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"':  sb.Append("\\\""); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append($"\\u{(int)c:X4}");
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    private static object? ToParam(string? s, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return s.Length > maxLen ? s[..maxLen] : s;
    }

    private static Options ParseArgs(string[] args)
    {
        var o = new Options
        {
            CsvPath = DefaultCsvPath,
            ConnectionString = DefaultConnectionString,
        };
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a.ToLowerInvariant())
            {
                case "--connection" when i + 1 < args.Length: o.ConnectionString = args[++i]; break;
                case "--limit" when i + 1 < args.Length && int.TryParse(args[++i], out var n): o.Limit = n; break;
                case "--include-closed": o.IncludeClosed = true; break;
                default:
                    if (!a.StartsWith("--", StringComparison.Ordinal)) o.CsvPath = a;
                    break;
            }
        }
        return o;
    }

    private static string Redact(string connStr)
    {
        return Regex.Replace(connStr, @"(?i)(Password|Pwd)\s*=\s*[^;]+", "$1=***");
    }

    private sealed class Options
    {
        public string CsvPath { get; set; } = "";
        public string ConnectionString { get; set; } = "";
        public int? Limit { get; set; }
        public bool IncludeClosed { get; set; }
    }

    private sealed class ImportStats
    {
        public int RowsRead { get; set; }
        public int RowsImported { get; set; }
        public int RowsSkipped { get; set; }
        public int ParseWarnings { get; set; }
        public int DbErrors { get; set; }
        public DateTime Started { get; set; }
        public TimeSpan Elapsed => DateTime.UtcNow - Started;
    }
}
