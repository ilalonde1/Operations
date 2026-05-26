#nullable enable
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kor.GovCanEngineeringImport;

internal static partial class Program
{
    private const string DefaultFile = @"C:\VIsual Studio Projects\KOR-GovCan-Import\engineering-awards.jsonl";
    private const string SourceName = "GovCanada_EngineeringServices";
    private const string SourceUrl = "https://search.open.canada.ca/contracts/";

    private static async Task<int> Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var options = ToolOptions.Load(config, args);
        if (!File.Exists(options.FilePath))
        {
            Console.Error.WriteLine($"Input file not found: {options.FilePath}");
            return 2;
        }

        if (!options.DryRun && string.IsNullOrWhiteSpace(options.OpportunitiesDb))
        {
            Console.Error.WriteLine("Missing connection string. Set KOR_OPPORTUNITIES_OPPORTUNITIESDB, appsettings:OpportunitiesDb, or --db.");
            return 2;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var stats = new ImportStats();
        var sw = Stopwatch.StartNew();

        Guid sourceId;
        SqlOpportunityAwardStore? store = null;
        if (options.DryRun)
        {
            sourceId = Guid.Empty;
            Console.WriteLine(options.Quiet
                ? "GovCan engineering awards dry-run starting."
                : $"GovCan engineering awards dry-run starting: file={options.FilePath}");
        }
        else
        {
            sourceId = await LookupSourceIdAsync(options.OpportunitiesDb, cts.Token).ConfigureAwait(false);
            var resolver = new CanonicalOrgResolver(
                new SqlCanonicalOrgStore(options.OpportunitiesDb),
                NullLogger<CanonicalOrgResolver>.Instance);
            store = new SqlOpportunityAwardStore(
                options.OpportunitiesDb,
                resolver,
                NullLogger<SqlOpportunityAwardStore>.Instance);
            Console.WriteLine(options.Quiet
                ? $"GovCan engineering awards import starting: source={SourceName}."
                : $"GovCan engineering awards import starting: source={SourceName} ({sourceId}); file={options.FilePath}");
        }

        await ProcessFileAsync(options.FilePath, sourceId, store, options.DryRun, stats, cts.Token).ConfigureAwait(false);

        sw.Stop();
        Console.WriteLine(
            $"GovCan engineering awards {(options.DryRun ? "dry-run" : "import")} complete: lines={stats.LinesRead:N0}, imported={stats.Imported:N0}, skippedBlankRef={stats.SkippedBlankReference:N0}, parseFailures={stats.ParseFailures:N0}, valueParseFailures={stats.ValueParseFailures:N0}, dateParseFailures={stats.DateParseFailures:N0}, elapsed={sw.Elapsed}.");
        return stats.ParseFailures > 0 ? 1 : 0;
    }

    private static async Task ProcessFileAsync(
        string path,
        Guid sourceId,
        SqlOpportunityAwardStore? store,
        bool dryRun,
        ImportStats stats,
        CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            ct.ThrowIfCancellationRequested();
            stats.LinesRead++;
            if (string.IsNullOrWhiteSpace(line))
            {
                stats.ParseFailures++;
                continue;
            }

            OpportunityAward? award;
            try
            {
                award = MapLine(line, sourceId, stats);
            }
            catch (JsonException)
            {
                stats.ParseFailures++;
                continue;
            }

            if (award is null)
            {
                stats.SkippedBlankReference++;
            }
            else if (dryRun)
            {
                stats.Imported++;
            }
            else
            {
                await store!.UpsertAsync(award, ct).ConfigureAwait(false);
                stats.Imported++;
            }

            if (stats.LinesRead % 2_000 == 0)
            {
                Console.WriteLine(
                    $"Progress: lines={stats.LinesRead:N0}, imported={stats.Imported:N0}, skippedBlankRef={stats.SkippedBlankReference:N0}, parseFailures={stats.ParseFailures:N0}, valueParseFailures={stats.ValueParseFailures:N0}, dateParseFailures={stats.DateParseFailures:N0}");
            }
        }
    }

    private static OpportunityAward? MapLine(string line, Guid sourceId, ImportStats stats)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("JSONL line root was not an object.");
        }

        var externalReference = GetString(root, "reference_number");
        if (string.IsNullOrWhiteSpace(externalReference))
        {
            return null;
        }

        var description = GetString(root, "description_en");
        var commodityCode = GetString(root, "commodity_code");
        var buyerName = GetString(root, "buyer_name");
        var ownerOrgTitle = GetString(root, "owner_org_title");
        var contractValueText = GetString(root, "contract_value");
        var contractDateText = GetString(root, "contract_date");
        var contractValue = ParseContractValue(contractValueText);
        var awardedAtUtc = ParseContractDate(contractDateText);
        if (!string.IsNullOrWhiteSpace(contractValueText) && !contractValue.HasValue)
        {
            stats.ValueParseFailures++;
        }

        if (!string.IsNullOrWhiteSpace(contractDateText) && !awardedAtUtc.HasValue)
        {
            stats.DateParseFailures++;
        }

        return new OpportunityAward
        {
            ExternalReference = externalReference,
            OpportunitySourceId = sourceId,
            Title = FirstNonBlank(description, commodityCode) ?? "",
            AwardingOrganization = FirstNonBlank(buyerName, ownerOrgTitle) ?? "",
            AwardedToOrganization = GetString(root, "vendor_name") ?? "",
            ContractValue = contractValue,
            ContractCurrency = "CAD",
            AwardedAtUtc = awardedAtUtc,
            ContractNumber = GetString(root, "procurement_id"),
            SolicitationType = GetString(root, "solicitation_procedure"),
            IssuingLocation = GetString(root, "country_of_vendor"),
            SourceUrl = SourceUrl,
            RawJson = line,
        };
    }

    private static async Task<Guid> LookupSourceIdAsync(string connectionString, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (1) Id
FROM opportunities.OpportunitySources
WHERE Name = @name;";

        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = SourceName;

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is Guid id)
        {
            return id;
        }

        throw new InvalidOperationException($"Opportunity source not found: {SourceName}");
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var text = value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static decimal? ParseContractValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = MoneyNoiseRegex().Replace(value, "");
        return decimal.TryParse(
            normalized,
            NumberStyles.Number | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset? ParseContractDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParseExact(
            value.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Utc))
            : null;
    }

    [GeneratedRegex(@"[$,\s]")]
    private static partial Regex MoneyNoiseRegex();

    private sealed class ImportStats
    {
        public long LinesRead { get; set; }
        public long Imported { get; set; }
        public long SkippedBlankReference { get; set; }
        public long ParseFailures { get; set; }
        public long ValueParseFailures { get; set; }
        public long DateParseFailures { get; set; }
    }

    private sealed record ToolOptions(string FilePath, string OpportunitiesDb, bool DryRun, bool Quiet)
    {
        public static ToolOptions Load(IConfiguration config, string[] args)
        {
            var file = config["File"] ?? DefaultFile;
            var db = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB")
                ?? config["OpportunitiesDb"]
                ?? "";
            var dryRun = false;
            var quiet = false;

            for (var i = 0; i < args.Length; i++)
            {
                var key = args[i];
                var value = i + 1 < args.Length ? args[i + 1] : "";
                switch (key)
                {
                    case "--file":
                        file = string.IsNullOrWhiteSpace(value) ? file : value;
                        i++;
                        break;
                    case "--db":
                        db = string.IsNullOrWhiteSpace(value) ? db : value;
                        i++;
                        break;
                    case "--dry-run":
                        dryRun = true;
                        break;
                    case "--quiet":
                        quiet = true;
                        break;
                }
            }

            return new ToolOptions(file, db, dryRun, quiet);
        }
    }
}
