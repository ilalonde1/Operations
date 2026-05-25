#nullable enable
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
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
                var pending = await store.ListPendingAgentEnrichmentAsync(batchSize, maxAttempts: 3, CancellationToken.None)
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
        int SleepMsBetweenRows)
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
                }
            }

            return new ToolOptions(
                db,
                string.IsNullOrWhiteSpace(ollama) ? "http://localhost:11434" : ollama,
                string.IsNullOrWhiteSpace(model) ? "qwen2.5:14b" : model,
                Math.Max(1, batch),
                Math.Max(0, max),
                Math.Max(0, sleep));
        }

        private static int ReadInt(string? value, int fallback)
            => int.TryParse(value, out var parsed) ? parsed : fallback;
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
