#nullable enable
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Kor.AwardOllamaBackfill;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var options = ToolOptions.Load(config, args);
        if (string.IsNullOrWhiteSpace(options.OpportunitiesDb))
        {
            Console.Error.WriteLine("Missing connection string. Set KOR_OPPORTUNITIES_OPPORTUNITIESDB or appsettings:OpportunitiesDb.");
            return 2;
        }

        var stopRequested = false;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("Cancellation requested. Finishing the in-flight row, then exiting...");
            stopRequested = true;
        };

        var store = new SqlOpportunityAwardStore(options.OpportunitiesDb);
        var queueFilters = await ResolveQueueFiltersAsync(options, CancellationToken.None).ConfigureAwait(false);
        PrintQueueFilters(queueFilters);

        using var http = new HttpClient
        {
            BaseAddress = new Uri(options.OllamaBaseUrl.TrimEnd('/')),
            Timeout = TimeSpan.FromMinutes(5),
        };
        var ollama = new OllamaClient(http, options.Model);
        var sw = Stopwatch.StartNew();
        var attempted = 0;
        var enriched = 0;
        var failed = 0;

        Console.WriteLine($"Award Ollama backfill starting. model={options.Model} batch={options.BatchSize} max={options.MaxRowsThisRun} ollama={options.OllamaBaseUrl}");

        try
        {
            while (!stopRequested)
            {
                var remaining = options.MaxRowsThisRun > 0
                    ? options.MaxRowsThisRun - attempted
                    : int.MaxValue;
                if (remaining <= 0)
                {
                    break;
                }

                var batchSize = Math.Min(options.BatchSize, remaining);
                var pending = queueFilters.HasActiveFilters
                    ? await store.ListPendingAgentEnrichmentAsync(
                        batchSize,
                        maxAttempts: 3,
                        queueFilters.ExcludedSourceIds,
                        queueFilters.MinContractValue,
                        CancellationToken.None).ConfigureAwait(false)
                    : await store.ListPendingAgentEnrichmentAsync(batchSize, maxAttempts: 3, CancellationToken.None)
                        .ConfigureAwait(false);
                if (pending.Count == 0)
                {
                    Console.WriteLine("Queue drained.");
                    break;
                }

                foreach (var row in pending)
                {
                    if (options.MaxRowsThisRun > 0 && attempted >= options.MaxRowsThisRun)
                    {
                        break;
                    }

                    attempted++;
                    try
                    {
                        var json = await ollama.GenerateJsonAsync(
                            PromptTemplate.SystemPrompt,
                            PromptTemplate.BuildUserPrompt(row),
                            CancellationToken.None).ConfigureAwait(false);
                        var payload = PayloadParser.Parse(json);
                        if (payload is null)
                        {
                            failed++;
                            await store.RecordAgentFailureAsync(row.Id, "Ollama returned no parseable JSON.", CancellationToken.None)
                                .ConfigureAwait(false);
                            PrintRow("FAIL", attempted, options.MaxRowsThisRun, row, null, "parse");
                        }
                        else
                        {
                            await store.RecordAgentEnrichmentAsync(row.Id, payload, CancellationToken.None).ConfigureAwait(false);
                            await store.RecordAgentVendorDetailsAsync(row.Id, payload, CancellationToken.None).ConfigureAwait(false);
                            enriched++;
                            PrintRow("OK", attempted, options.MaxRowsThisRun, row, payload, null);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        await store.RecordAgentFailureAsync(row.Id, ex.Message, CancellationToken.None)
                            .ConfigureAwait(false);
                        PrintRow("FAIL", attempted, options.MaxRowsThisRun, row, null, ex.GetType().Name);
                    }

                    if (!stopRequested && options.SleepMsBetweenRows > 0)
                    {
                        await Task.Delay(options.SleepMsBetweenRows, CancellationToken.None).ConfigureAwait(false);
                    }

                    if (stopRequested)
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Cancelled.");
        }

        sw.Stop();
        var rowsPerMinute = sw.Elapsed.TotalMinutes > 0 ? attempted / sw.Elapsed.TotalMinutes : 0;
        Console.WriteLine($"Done. attempted={attempted} enriched={enriched} failed={failed} elapsed={sw.Elapsed:g} rate={rowsPerMinute:0.0} rows/min");
        return failed == 0 ? 0 : 1;
    }

    private static async Task<QueueFilters> ResolveQueueFiltersAsync(ToolOptions options, CancellationToken ct)
    {
        var minValue = options.MinContractValue > 0 ? options.MinContractValue : (decimal?)null;
        if (options.SourceIncludePatterns.Count == 0 && options.SourceExcludePatterns.Count == 0)
        {
            return new QueueFilters(Array.Empty<Guid>(), Array.Empty<string>(), minValue);
        }

        var sources = await ListSourcesAsync(options.OpportunitiesDb, ct).ConfigureAwait(false);
        var excluded = new Dictionary<Guid, string>();

        if (options.SourceIncludePatterns.Count > 0)
        {
            foreach (var source in sources)
            {
                if (!MatchesAny(source.Name, options.SourceIncludePatterns))
                {
                    excluded[source.Id] = source.Name;
                }
            }
        }

        if (options.SourceExcludePatterns.Count > 0)
        {
            foreach (var source in sources)
            {
                if (MatchesAny(source.Name, options.SourceExcludePatterns))
                {
                    excluded[source.Id] = source.Name;
                }
            }
        }

        return new QueueFilters(
            excluded.Keys.ToArray(),
            excluded.Values.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToArray(),
            minValue);
    }

    private static async Task<IReadOnlyList<SourceSummary>> ListSourcesAsync(string connectionString, CancellationToken ct)
    {
        const string sql = @"
SELECT Id, Name
FROM opportunities.OpportunitySources
ORDER BY Name;";

        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var sources = new List<SourceSummary>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            sources.Add(new SourceSummary(
                reader.GetGuid(0),
                reader.GetString(1)));
        }

        return sources;
    }

    private static void PrintQueueFilters(QueueFilters filters)
    {
        if (!filters.HasActiveFilters)
        {
            Console.WriteLine("[Round21] No filters active.");
            return;
        }

        var minValue = filters.MinContractValue.HasValue
            ? filters.MinContractValue.Value.ToString("C0", CultureInfo.GetCultureInfo("en-CA"))
            : "$0";

        Console.WriteLine("[Round21] Active filters:");
        Console.WriteLine($"  Excluded sources ({filters.ExcludedSourceNames.Count}): {FormatSourceNames(filters.ExcludedSourceNames)}");
        Console.WriteLine($"  Min contract value: {minValue}");
    }

    private static string FormatSourceNames(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            return "(none)";
        }

        return names.Count <= 12
            ? string.Join(", ", names)
            : string.Join(", ", names.Take(12)) + $", ... +{names.Count - 12} more";
    }

    private static bool MatchesAny(string sourceName, IReadOnlyList<string> patterns) =>
        patterns.Any(pattern => Like(sourceName, pattern));

    private static bool Like(string value, string pattern)
    {
        var regex = "^" + Regex.Escape(pattern)
            .Replace("%", ".*", StringComparison.Ordinal)
            .Replace("_", ".", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static void PrintRow(
        string status,
        int attempted,
        int maxRows,
        PendingAgentEnrichmentRow row,
        AwardAgentEnrichmentPayload? payload,
        string? error)
    {
        var total = maxRows > 0 ? maxRows.ToString() : "?";
        var score = payload?.VendorKorOverlapScore?.ToString() ?? "-";
        var type = payload?.ContractProjectType ?? "-";
        var suffix = error is null ? "" : $" error={error}";
        Console.WriteLine(
            $"[{DateTime.Now:HH:mm:ss}] [{attempted}/{total}] {status,-5} AwardId={row.Id} vendor={row.AwardedToOrganization} score={score} type={type}{suffix}");
    }

    private sealed record ToolOptions(
        string OpportunitiesDb,
        string OllamaBaseUrl,
        string Model,
        int BatchSize,
        int MaxRowsThisRun,
        int SleepMsBetweenRows,
        IReadOnlyList<string> SourceIncludePatterns,
        IReadOnlyList<string> SourceExcludePatterns,
        decimal MinContractValue)
    {
        public static ToolOptions Load(IConfiguration config, string[] args)
        {
            var db = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB")
                ?? config["OpportunitiesDb"]
                ?? "";
            var ollama = config["OllamaBaseUrl"] ?? "http://localhost:11434";
            var model = config["Model"] ?? config["OllamaModel"] ?? "qwen2.5:14b";
            var batch = ReadInt(config["BatchSize"], 10);
            var max = ReadInt(config["MaxRowsThisRun"], 0);
            var sleep = ReadInt(config["SleepMsBetweenRows"], 0);
            var includePatterns = ParsePatternList(config["SourceInclude"]);
            var excludePatterns = ParsePatternList(config["SourceExclude"]);
            var minValue = ReadDecimal(config["MinContractValue"], 0);

            for (var i = 0; i < args.Length; i++)
            {
                var key = args[i];
                var value = i + 1 < args.Length ? args[i + 1] : "";
                switch (key)
                {
                    case "--batch":
                        batch = ReadInt(value, batch);
                        i++;
                        break;
                    case "--max":
                        max = ReadInt(value, max);
                        i++;
                        break;
                    case "--model":
                        model = string.IsNullOrWhiteSpace(value) ? model : value;
                        i++;
                        break;
                    case "--ollama":
                        ollama = string.IsNullOrWhiteSpace(value) ? ollama : value;
                        i++;
                        break;
                    case "--sleep":
                        sleep = ReadInt(value, sleep);
                        i++;
                        break;
                    case "--source-include":
                        includePatterns = ParsePatternList(value);
                        i++;
                        break;
                    case "--source-exclude":
                        excludePatterns = ParsePatternList(value);
                        i++;
                        break;
                    case "--min-value":
                        minValue = ReadDecimal(value, minValue);
                        i++;
                        break;
                }
            }

            return new ToolOptions(
                db,
                string.IsNullOrWhiteSpace(ollama) ? "http://localhost:11434" : ollama,
                string.IsNullOrWhiteSpace(model) ? "qwen2.5:14b" : model,
                Math.Max(1, batch),
                Math.Max(0, max),
                Math.Max(0, sleep),
                includePatterns,
                excludePatterns,
                Math.Max(0, minValue));
        }

        private static int ReadInt(string? value, int fallback)
            => int.TryParse(value, out var parsed) ? parsed : fallback;

        private static decimal ReadDecimal(string? value, decimal fallback)
            => decimal.TryParse(
                value,
                NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
                CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : fallback;

        private static IReadOnlyList<string> ParsePatternList(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<string>();
            }

            return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToArray();
        }
    }

    private sealed record SourceSummary(Guid Id, string Name);

    private sealed record QueueFilters(
        IReadOnlyCollection<Guid> ExcludedSourceIds,
        IReadOnlyList<string> ExcludedSourceNames,
        decimal? MinContractValue)
    {
        public bool HasActiveFilters => ExcludedSourceIds.Count > 0 || MinContractValue.HasValue;
    }
}

internal static class PayloadParser
{
    public static AwardAgentEnrichmentPayload? Parse(JsonNode? json)
    {
        if (json is null) return null;
        var score = GetInt(json, "vendor_kor_overlap_score");
        var competes = score.HasValue ? score.Value >= 5 : GetBool(json, "competes_with_kor");

        return new AwardAgentEnrichmentPayload
        {
            VendorProfile = GetString(json, "vendor_profile"),
            ContractContext = GetString(json, "contract_context"),
            CompetesWithKor = competes,
            CompetitionNotes = GetString(json, "competition_notes"),
            SourceUrls = ReadStringArray(json["source_urls"]),
            VendorWebsite = GetString(json, "vendor_website"),
            VendorHqLocation = GetString(json, "vendor_hq_location"),
            VendorSizeBand = GetString(json, "vendor_size_band"),
            VendorFoundedYear = GetInt(json, "vendor_founded_year"),
            VendorSpecialties = ReadStringArray(json["vendor_specialties"]),
            VendorLeadership = ReadLeadershipArray(json["key_leadership"]),
            VendorOwnershipStatus = GetString(json, "vendor_ownership_status"),
            VendorParentCompany = GetString(json, "vendor_parent_company"),
            VendorLocations = ReadStringArray(json["vendor_locations"]),
            VendorCertifications = ReadStringArray(json["vendor_certifications"]),
            VendorRecentNews = ReadNewsArray(json["vendor_recent_news"]),
            VendorLinkedInUrl = GetString(json, "vendor_linkedin_url"),
            VendorKorOverlapScore = score,
            ContractProjectType = GetString(json, "contract_project_type"),
        };
    }

    private static string? GetString(JsonNode node, string name)
    {
        var value = node[name];
        if (value is null) return null;
        try
        {
            var text = value.GetValue<string?>()?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static int? GetInt(JsonNode node, string name)
    {
        var value = node[name];
        if (value is null) return null;
        try
        {
            return value.GetValue<int?>();
        }
        catch
        {
            return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
        }
    }

    private static bool? GetBool(JsonNode node, string name)
    {
        var value = node[name];
        if (value is null) return null;
        try
        {
            return value.GetValue<bool?>();
        }
        catch
        {
            return bool.TryParse(value.ToString(), out var parsed) ? parsed : null;
        }
    }

    private static List<string> ReadStringArray(JsonNode? node)
    {
        var values = new List<string>();
        if (node is not JsonArray arr) return values;
        foreach (var item in arr)
        {
            var value = item?.GetValue<string?>()?.Trim();
            if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
        }

        return values;
    }

    private static List<VendorLeader> ReadLeadershipArray(JsonNode? node)
    {
        var values = new List<VendorLeader>();
        if (node is not JsonArray arr) return values;
        foreach (var item in arr)
        {
            var name = item?["name"]?.GetValue<string?>()?.Trim();
            var title = item?["title"]?.GetValue<string?>()?.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                values.Add(new VendorLeader(name, string.IsNullOrWhiteSpace(title) ? null : title));
            }
        }

        return values;
    }

    private static List<VendorNewsItem> ReadNewsArray(JsonNode? node)
    {
        var values = new List<VendorNewsItem>();
        if (node is not JsonArray arr) return values;
        foreach (var item in arr)
        {
            var headline = item?["headline"]?.GetValue<string?>()?.Trim();
            var url = item?["url"]?.GetValue<string?>()?.Trim();
            var date = item?["date"]?.GetValue<string?>()?.Trim();
            if (!string.IsNullOrWhiteSpace(headline))
            {
                values.Add(new VendorNewsItem(
                    headline,
                    string.IsNullOrWhiteSpace(url) ? null : url,
                    string.IsNullOrWhiteSpace(date) ? null : date));
            }
        }

        return values;
    }
}
