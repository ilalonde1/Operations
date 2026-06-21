#nullable enable
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kor.Opportunities.Data.AwardPrograms;
using Kor.Opportunities.Worker.Options;
using Kor.Opportunities.Worker.Services.Research;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

[DisallowConcurrentExecution]
internal sealed class AwardProgramFinderJob : IJob
{
    private const string ProviderName = "AwardProgramFinder";
    private const string StructuredOutputSchema = """
        {
          "type": "object",
          "properties": {
            "awardPrograms": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "awardingBody": { "type": "string" },
                  "programName": { "type": "string" },
                  "cycleYear": { "type": ["integer", "null"] },
                  "category": { "type": ["string", "null"] },
                  "discipline": { "type": ["string", "null"] },
                  "region": { "type": ["string", "null"] },
                  "eligibilitySummary": { "type": ["string", "null"] },
                  "submissionDeadline": { "type": ["string", "null"] },
                  "entryFee": { "type": ["string", "null"] },
                  "url": { "type": ["string", "null"] }
                },
                "required": ["awardingBody", "programName"]
              }
            }
          },
          "required": ["awardPrograms"]
        }
        """;

    private const string StructuredOutputInstruction =
        "Submit JSON with an awardPrograms array. Dates must be ISO yyyy-MM-dd or null. Do not include contract-award/bid-result intelligence.";

    private readonly IAwardProgramStore _store;
    private readonly IResearchExecutorService _executor;
    private readonly IAwardProgramResearchPromptCatalog _catalog;
    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<AwardProgramFinderJob> _logger;

    public AwardProgramFinderJob(
        IAwardProgramStore store,
        IResearchExecutorService executor,
        IAwardProgramResearchPromptCatalog catalog,
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<AwardProgramFinderJob> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var options = _options.Value;
        if (!options.AwardProgramFinderEnabled)
        {
            _logger.LogDebug("{Job} skipped: disabled.", nameof(AwardProgramFinderJob));
            return;
        }

        var lastRefresh = await _store.GetLastCatalogRefreshUtcAsync(ct).ConfigureAwait(false);
        var freshnessDays = options.AwardProgramFinderFreshnessDays > 0 ? options.AwardProgramFinderFreshnessDays : 6;
        if (lastRefresh is { } seen && seen >= DateTimeOffset.UtcNow.AddDays(-freshnessDays))
        {
            _logger.LogInformation(
                "{Job} skipped: catalog refreshed {LastRefresh:u}, inside {FreshnessDays}-day freshness window.",
                nameof(AwardProgramFinderJob),
                seen,
                freshnessDays);
            return;
        }

        var prompts = _catalog.Resolve();
        if (prompts is null)
        {
            _logger.LogWarning("{Job} skipped: prompt templates unavailable.", nameof(AwardProgramFinderJob));
            return;
        }

        var target = new ResearchTarget(
            0,
            "AEC industry award programs",
            "AwardProgramCatalog",
            ProviderName,
            prompts.SystemPrompt,
            prompts.UserPrompt,
            StructuredOutputSchema,
            StructuredOutputInstruction);

        var result = await _executor.ExecuteAsync(target, ct).ConfigureAwait(false);
        if (result is null)
        {
            _logger.LogWarning("{Job} produced no research result.", nameof(AwardProgramFinderJob));
            return;
        }

        var programs = ParsePrograms(result.ResultJson);
        var maxRows = options.AwardProgramFinderMaxRowsPerRun > 0 ? options.AwardProgramFinderMaxRowsPerRun : 40;
        var upserts = programs.Take(maxRows).ToList();
        var affected = await _store.UpsertAsync(upserts, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "{Job} completed: parsed={Parsed} upserted={Upserted} affected={Affected} toolCalls={ToolCalls} inputTokens={InputTokens} outputTokens={OutputTokens}.",
            nameof(AwardProgramFinderJob),
            programs.Count,
            upserts.Count,
            affected,
            result.ToolCallCount,
            result.InputTokens,
            result.OutputTokens);
    }

    private static IReadOnlyList<AwardProgramUpsert> ParsePrograms(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("awardPrograms", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AwardProgramUpsert>();
        }

        var rows = new List<AwardProgramUpsert>();
        foreach (var item in items.EnumerateArray())
        {
            var body = ReadString(item, "awardingBody");
            var program = ReadString(item, "programName");
            if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(program))
            {
                continue;
            }

            var year = ReadInt(item, "cycleYear") ?? InferCycleYear(ReadString(item, "submissionDeadline"));
            rows.Add(new AwardProgramUpsert(
                NaturalKey(body, program, year),
                body.Trim(),
                program.Trim(),
                year,
                ReadString(item, "category"),
                ReadString(item, "discipline"),
                ReadString(item, "region"),
                ReadString(item, "eligibilitySummary"),
                ReadDate(item, "submissionDeadline"),
                ReadString(item, "entryFee"),
                ReadString(item, "url"),
                ProviderName));
        }

        return rows;
    }

    private static string? ReadString(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? NullIfBlank(value.GetString()) : NullIfBlank(value.ToString());
    }

    private static int? ReadInt(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n))
        {
            return n;
        }

        return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static DateOnly? ReadDate(JsonElement item, string property)
    {
        var text = ReadString(item, property);
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static int? InferCycleYear(string? deadline)
    {
        return DateOnly.TryParseExact(deadline, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.Year
            : null;
    }

    private static string NaturalKey(string body, string program, int? year)
    {
        var raw = $"{Normalize(body)}|{Normalize(program)}|{year?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}";
        using var sha = SHA1.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw)));
    }

    private static string Normalize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
