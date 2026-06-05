#nullable enable
using System.Data;
using System.Globalization;
using System.Text.Json;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Data.Intel;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Opportunities.Worker.Services.Research;

public sealed class BdResearchExecutorService
{
    private const string BatchProviderName = "FirmNarrative";
    private const int CommandTimeoutSeconds = 120;

    private readonly OpportunitiesWorkerOptions _workerOptions;
    private readonly BdResearchExecutorOptions _options;
    private readonly IResearchExecutorService _executor;
    private readonly IResearchPromptCatalog _catalog;
    private readonly IntelReadService _intelReadService;
    private readonly IEnrichmentTrackingStore _enrichmentStore;
    private readonly ILogger<BdResearchExecutorService> _logger;

    public BdResearchExecutorService(
        IOptions<OpportunitiesWorkerOptions> workerOptions,
        IOptions<BdResearchExecutorOptions> options,
        IResearchExecutorService executor,
        IResearchPromptCatalog catalog,
        IntelReadService intelReadService,
        IEnrichmentTrackingStore enrichmentStore,
        ILogger<BdResearchExecutorService> logger)
    {
        _workerOptions = workerOptions?.Value ?? throw new ArgumentNullException(nameof(workerOptions));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _intelReadService = intelReadService ?? throw new ArgumentNullException(nameof(intelReadService));
        _enrichmentStore = enrichmentStore ?? throw new ArgumentNullException(nameof(enrichmentStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<BdResearchExecutorRunResult> RunBatchAsync(CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("BD research executor is disabled.");
            return new BdResearchExecutorRunResult(0, 0, 0, 0, 0, 0);
        }

        var candidates = await LoadCandidatesAsync(ct).ConfigureAwait(false);
        var executed = 0;
        var successes = 0;
        var failures = 0;
        var totalInputTokens = 0L;
        var totalOutputTokens = 0L;

        foreach (var candidate in candidates)
        {
            if (totalOutputTokens >= _options.DailyOutputTokenBudget)
            {
                _logger.LogInformation(
                    "BD research executor stopping early: output token budget reached. outputTok={OutputTokens}; budget={Budget}.",
                    totalOutputTokens,
                    _options.DailyOutputTokenBudget);
                break;
            }

            var prompt = _catalog.Resolve(candidate.Kind, BatchProviderName, candidate.DisplayName);
            if (prompt is null)
            {
                _logger.LogInformation(
                    "BD research executor skipped org {CanonicalOrgId}/{OrgName}: no prompt template for provider {ProviderName}.",
                    candidate.Id,
                    candidate.DisplayName,
                    BatchProviderName);
                continue;
            }

            executed++;
            try
            {
                var existing = await _intelReadService.GetOrgIntelAsync(candidate.Id, ct).ConfigureAwait(false);
                var currentIntelJson = SerializeBundleAsContext(existing);
                var userPrompt = prompt.UserPrompt.Replace("{CURRENT_INTEL_JSON}", currentIntelJson, StringComparison.Ordinal);
                var result = await _executor.ExecuteAsync(
                    new ResearchTarget(
                        candidate.Id,
                        candidate.DisplayName,
                        candidate.Kind,
                        BatchProviderName,
                        prompt.SystemPrompt,
                        userPrompt),
                    ct).ConfigureAwait(false);

                if (result is null)
                {
                    failures++;
                    continue;
                }

                await WriteOutputAsync(result, ct).ConfigureAwait(false);
                await PushThroughChokepointAsync(result, ct).ConfigureAwait(false);
                successes++;
                totalInputTokens += result.InputTokens;
                totalOutputTokens += result.OutputTokens;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures++;
                _logger.LogWarning(
                    ex,
                    "BD research executor failed org {CanonicalOrgId}/{OrgName}.",
                    candidate.Id,
                    candidate.DisplayName);
            }
        }

        return new BdResearchExecutorRunResult(
            candidates.Count,
            executed,
            successes,
            failures,
            totalInputTokens,
            totalOutputTokens);
    }

    public async Task<ExecutedResearch?> ExecuteOneAsync(long canonicalOrgId, string providerName, CancellationToken ct)
    {
        try
        {
            var org = await LoadOrgAsync(canonicalOrgId, ct).ConfigureAwait(false);
            if (org is null)
            {
                _logger.LogWarning("BD research executor manual refresh skipped: CanonicalOrgId {CanonicalOrgId} was not found.", canonicalOrgId);
                return null;
            }

            var prompt = _catalog.Resolve(org.Kind, providerName, org.DisplayName);
            if (prompt is null)
            {
                _logger.LogWarning(
                    "BD research executor manual refresh skipped for org {CanonicalOrgId}/{OrgName}: no prompt template for provider {ProviderName}.",
                    canonicalOrgId,
                    org.DisplayName,
                    providerName);
                return null;
            }

            var existing = await _intelReadService.GetOrgIntelAsync(org.Id, ct).ConfigureAwait(false);
            var currentIntelJson = SerializeBundleAsContext(existing);
            var userPrompt = prompt.UserPrompt.Replace("{CURRENT_INTEL_JSON}", currentIntelJson, StringComparison.Ordinal);
            var result = await _executor.ExecuteAsync(
                new ResearchTarget(
                    org.Id,
                    org.DisplayName,
                    org.Kind,
                    providerName,
                    prompt.SystemPrompt,
                    userPrompt),
                ct).ConfigureAwait(false);

            if (result is null)
            {
                return null;
            }

            await WriteOutputAsync(result, ct).ConfigureAwait(false);
            await PushThroughChokepointAsync(result, ct).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "BD research executor manual refresh failed for CanonicalOrgId {CanonicalOrgId}, provider {ProviderName}.",
                canonicalOrgId,
                providerName);
            return null;
        }
    }

    private async Task<IReadOnlyList<ResearchOrgCandidate>> LoadCandidatesAsync(CancellationToken ct)
    {
        var eligibleKinds = _options.EligibleKinds
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (eligibleKinds.Length == 0 || _options.MaxOrgsPerRun <= 0)
        {
            return Array.Empty<ResearchOrgCandidate>();
        }

        var kindParameters = string.Join(", ", eligibleKinds.Select((_, i) => "@k" + i.ToString(CultureInfo.InvariantCulture)));
        var sql = @"
SELECT TOP (@max) co.Id,
       co.DisplayName,
       co.Kind,
       last_seen.LastSeenAtUtc
FROM opportunities.CanonicalOrg co
LEFT JOIN (
    SELECT CanonicalOrgId, MAX(LastSeenAtUtc) AS LastSeenAtUtc
    FROM (
        SELECT a.CanonicalOrgId, a.LastSeenAtUtc FROM opportunities.IntelAction a WHERE a.RetiredAtUtc IS NULL
        UNION ALL
        SELECT s.CanonicalOrgId, s.LastSeenAtUtc FROM opportunities.IntelSignal s WHERE s.RetiredAtUtc IS NULL
        UNION ALL
        SELECT n.CanonicalOrgId, n.LastSeenAtUtc FROM opportunities.IntelNarrative n WHERE n.RetiredAtUtc IS NULL
    ) u
    GROUP BY u.CanonicalOrgId
) last_seen ON last_seen.CanonicalOrgId = co.Id
WHERE co.Kind IN (" + kindParameters + @")
  AND (last_seen.LastSeenAtUtc IS NULL
       OR last_seen.LastSeenAtUtc < DATEADD(DAY, -@staleness, sysdatetimeoffset()))
ORDER BY ISNULL(last_seen.LastSeenAtUtc, '0001-01-01') ASC;";

        var rows = new List<ResearchOrgCandidate>();
        await using var con = new SqlConnection(_workerOptions.OpportunitiesDb);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con)
        {
            CommandType = CommandType.Text,
            CommandTimeout = CommandTimeoutSeconds,
        };
        cmd.Parameters.Add("@max", SqlDbType.Int).Value = _options.MaxOrgsPerRun;
        cmd.Parameters.Add("@staleness", SqlDbType.Int).Value = _options.StalenessDays;
        for (var i = 0; i < eligibleKinds.Length; i++)
        {
            cmd.Parameters.Add("@k" + i.ToString(CultureInfo.InvariantCulture), SqlDbType.NVarChar, 100).Value = eligibleKinds[i];
        }

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new ResearchOrgCandidate(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return rows;
    }

    private async Task<ResearchOrgCandidate?> LoadOrgAsync(long canonicalOrgId, CancellationToken ct)
    {
        const string sql = @"
SELECT Id, DisplayName, Kind
FROM opportunities.CanonicalOrg
WHERE Id = @id;";

        await using var con = new SqlConnection(_workerOptions.OpportunitiesDb);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con)
        {
            CommandType = CommandType.Text,
            CommandTimeout = CommandTimeoutSeconds,
        };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = canonicalOrgId;

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new ResearchOrgCandidate(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2));
    }

    private async Task WriteOutputAsync(ExecutedResearch result, CancellationToken ct)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dir = Path.Combine(_options.OutputDir, today);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"refresh-{result.CanonicalOrgId}-{SafeFilePart(result.ProviderName)}.json");
        await File.WriteAllTextAsync(path, result.ResultJson, ct).ConfigureAwait(false);
    }

    private async Task PushThroughChokepointAsync(ExecutedResearch executed, CancellationToken ct)
    {
        try
        {
            var result = new EnrichmentResult(
                EnrichmentStatuses.Ok,
                null,
                executed.ResultJson,
                $"Auto-refreshed via BdResearchExecutor at {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");
            var nextRefresh = DateTimeOffset.UtcNow.AddDays(Math.Max(7, _options.StalenessDays));
            await _enrichmentStore.RecordAttemptAsync(
                executed.CanonicalOrgId,
                executed.ProviderName,
                result,
                nextRefresh,
                ct).ConfigureAwait(false);

            _logger.LogInformation(
                "BD research executor pushed result through R79 chokepoint for org {CanonicalOrgId}/{ProviderName}. Intel* auto-decomposition will run inline.",
                executed.CanonicalOrgId,
                executed.ProviderName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "BD research executor saved file but chokepoint push failed for org {CanonicalOrgId}/{ProviderName}. Catch-up will pick it up.",
                executed.CanonicalOrgId,
                executed.ProviderName);
        }
    }

    private static string SafeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(ch => invalid.Contains(ch) ? '_' : ch));
    }

    private static string SerializeBundleAsContext(OrgIntelBundle bundle)
    {
        if (bundle.People.Count == 0
            && bundle.Signals.Count == 0
            && bundle.Actions.Count == 0
            && bundle.Works.Count == 0
            && bundle.Risks.Count == 0
            && bundle.Narratives.Count == 0)
        {
            return "{}";
        }

        var context = new
        {
            people = bundle.People.Select(p => new
            {
                name = p.DisplayName,
                title = p.Title,
                isCurrent = p.IsCurrent,
                notes = p.AffiliationNotes,
            }),
            signals = bundle.Signals.Select(s => new
            {
                type = s.SignalType,
                subject = s.Subject,
                occurredAt = s.OccurredAtApprox,
            }),
            actions = bundle.Actions.Select(a => new
            {
                type = a.ActionType,
                text = a.Recommendation,
            }),
            works = bundle.Works.Select(w => new
            {
                projectName = w.ProjectName,
                role = w.Role,
                year = w.YearApprox,
            }),
            risks = bundle.Risks.Select(r => new
            {
                type = r.RiskType,
                description = r.Description,
            }),
            narratives = bundle.Narratives.Select(n => new
            {
                type = n.NarrativeType,
                text = n.ParagraphText,
            }),
        };

        return JsonSerializer.Serialize(context, new JsonSerializerOptions
        {
            WriteIndented = false,
        });
    }

    private sealed record ResearchOrgCandidate(long Id, string DisplayName, string Kind);
}

public sealed record BdResearchExecutorRunResult(
    int OrgsConsidered,
    int OrgsExecuted,
    int Successes,
    int Failures,
    long TotalInputTokens,
    long TotalOutputTokens);
