#nullable enable
using System.Data;
using System.Globalization;
using System.Text;
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
                var anchor = await BuildIdentityAnchorAsync(candidate, ct).ConfigureAwait(false);
                var userPrompt = anchor + prompt.UserPrompt.Replace("{CURRENT_INTEL_JSON}", currentIntelJson, StringComparison.Ordinal);
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

                var gate = ResearchIdentityGate.Evaluate(candidate.AnchorHost, result.ResultJson);
                if (!gate.Allow)
                {
                    failures++;
                    _logger.LogError(
                        "IDENTITY GATE blocked a narrative overwrite for org {CanonicalOrgId}/{OrgName}: {Reason}. Raw output kept; nothing was persisted.",
                        candidate.Id, candidate.DisplayName, gate.Reason);
                    continue;
                }

                if (!candidate.HasAnchor && gate.ResearchedHost is not null)
                {
                    await BackfillWebsiteAsync(candidate, gate.ResearchedHost, ct).ConfigureAwait(false);
                }

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
            var anchor = await BuildIdentityAnchorAsync(org, ct).ConfigureAwait(false);
            var userPrompt = anchor + prompt.UserPrompt.Replace("{CURRENT_INTEL_JSON}", currentIntelJson, StringComparison.Ordinal);
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

            // Always keep the raw output on disk, even when the gate refuses it —
            // a refused result is evidence, and discarding it would hide the near-miss.
            await WriteOutputAsync(result, ct).ConfigureAwait(false);

            var gate = ResearchIdentityGate.Evaluate(org.AnchorHost, result.ResultJson);
            if (!gate.Allow)
            {
                _logger.LogError(
                    "IDENTITY GATE blocked a narrative overwrite for org {CanonicalOrgId}/{OrgName}: {Reason}. Raw output kept; nothing was persisted.",
                    org.Id, org.DisplayName, gate.Reason);
                return null;
            }

            if (!org.HasAnchor && gate.ResearchedHost is not null)
            {
                await BackfillWebsiteAsync(org, gate.ResearchedHost, ct).ConfigureAwait(false);
            }

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
       co.Website,
       co.WebsiteDomain,
       last_seen.LastSeenAtUtc
FROM opportunities.CanonicalOrg co
LEFT JOIN (
    -- Staleness rollup is scoped to SourceProviderName='FirmNarrative'
    -- so that rows from other providers (PersonBrief-*, DataHoning,
    -- StructuralPartnerMap, etc.) cannot make an org appear fresh to
    -- the FirmNarrative auto-refresher. Without this scope, R91c
    -- person-side writes to a CanonicalOrg's IntelAction set would
    -- bump LastSeenAtUtc and skip the org's own dossier refresh.
    SELECT CanonicalOrgId, MAX(LastSeenAtUtc) AS LastSeenAtUtc
    FROM (
        SELECT a.CanonicalOrgId, a.LastSeenAtUtc FROM opportunities.IntelAction a
         WHERE a.RetiredAtUtc IS NULL AND a.SourceProviderName = N'FirmNarrative'
        UNION ALL
        SELECT s.CanonicalOrgId, s.LastSeenAtUtc FROM opportunities.IntelSignal s
         WHERE s.RetiredAtUtc IS NULL AND s.SourceProviderName = N'FirmNarrative'
        UNION ALL
        SELECT n.CanonicalOrgId, n.LastSeenAtUtc FROM opportunities.IntelNarrative n
         WHERE n.RetiredAtUtc IS NULL AND n.SourceProviderName = N'FirmNarrative'
    ) u
    GROUP BY u.CanonicalOrgId
) last_seen ON last_seen.CanonicalOrgId = co.Id
WHERE co.Kind IN (" + kindParameters + @")
  AND co.RetiredAtUtc IS NULL
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
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return rows;
    }

    private async Task<ResearchOrgCandidate?> LoadOrgAsync(long canonicalOrgId, CancellationToken ct)
    {
        const string sql = @"
SELECT Id, DisplayName, Kind, Website, WebsiteDomain
FROM opportunities.CanonicalOrg
WHERE Id = @id
  AND RetiredAtUtc IS NULL;";

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
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    /// <summary>
    /// Builds the ENTITY ON FILE preamble prepended to every research prompt.
    /// The identifying evidence we already hold — website, aliases, the people
    /// affiliated to the row — is exactly what would have stopped the Continuum
    /// rewrite: four Victoria architects were sitting on the row while the model
    /// was told only the words "Continuum Partners, LLC".
    /// </summary>
    private async Task<string> BuildIdentityAnchorAsync(ResearchOrgCandidate org, CancellationToken ct)
    {
        var aliases = new List<string>();
        var people = new List<string>();

        try
        {
            const string sql = @"
SELECT TOP (12) N'alias' AS Kind, oa.RawName AS Value
FROM opportunities.OrgAlias oa WHERE oa.CanonicalOrgId = @id
UNION ALL
SELECT TOP (12) N'person', p.DisplayName
FROM opportunities.IntelPersonAffiliation a
JOIN opportunities.IntelPerson p ON p.Id = a.IntelPersonId
WHERE a.CanonicalOrgId = @id AND a.RetiredAtUtc IS NULL AND p.RetiredAtUtc IS NULL;";

            await using var con = new SqlConnection(_workerOptions.OpportunitiesDb);
            await con.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = org.Id;
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var bucket = reader.GetString(0) == "alias" ? aliases : people;
                if (!reader.IsDBNull(1))
                {
                    bucket.Add(reader.GetString(1));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Evidence is an accuracy aid, not a correctness dependency — a failed
            // lookup must not stop the refresh, but it IS logged so a systematically
            // anchor-less run is visible rather than silent.
            _logger.LogWarning(ex, "Identity anchor evidence lookup failed for org {CanonicalOrgId}; proceeding with name only.", org.Id);
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== ENTITY ON FILE — research THIS organization and no other ===");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Name: {org.DisplayName}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Kind: {org.Kind}");
        sb.AppendLine(org.HasAnchor
            ? $"Official website (AUTHORITATIVE — the entity you research MUST be this one): {org.AnchorHost}"
            : "Official website: NOT ON FILE. Identify it from the evidence below.");

        if (aliases.Count > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Known names/aliases on file: {string.Join("; ", aliases)}");
        }

        if (people.Count > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"People already on file at this organization: {string.Join("; ", people)}");
        }

        sb.AppendLine();
        sb.AppendLine("RULES:");
        sb.AppendLine("1. Many firms share a name. If the evidence above points to a different");
        sb.AppendLine("   organization than the one your search surfaces, the evidence above wins.");
        sb.AppendLine("2. Return the official website of the entity you actually researched in");
        sb.AppendLine("   'entityWebsite', and set 'entityMatchesRecord' to true only if you are");
        sb.AppendLine("   confident it is the same organization as the evidence above.");
        sb.AppendLine("3. If you cannot confirm the match, set 'entityMatchesRecord' to false and");
        sb.AppendLine("   explain in '_confidence'. Do NOT assert that the record on file is wrong");
        sb.AppendLine("   and do NOT replace it with a different company's details.");
        sb.AppendLine("=== END ENTITY ON FILE ===");
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// Self-healing: an org with no anchor that has just been researched gets the
    /// discovered website written back, so the NEXT refresh is comparable. This is
    /// how the 7,200 anchor-less orgs acquire anchors without a separate backfill.
    /// </summary>
    private async Task BackfillWebsiteAsync(ResearchOrgCandidate org, string host, CancellationToken ct)
    {
        try
        {
            const string sql = @"
UPDATE opportunities.CanonicalOrg
SET Website = @site, WebsiteDomain = @host, UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = @id
  AND NULLIF(LTRIM(RTRIM(Website)), N'') IS NULL
  AND NULLIF(LTRIM(RTRIM(WebsiteDomain)), N'') IS NULL;";

            await using var con = new SqlConnection(_workerOptions.OpportunitiesDb);
            await con.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = org.Id;
            cmd.Parameters.Add("@site", SqlDbType.NVarChar, 500).Value = "https://" + host + "/";
            cmd.Parameters.Add("@host", SqlDbType.NVarChar, 255).Value = host;
            var n = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            if (n > 0)
            {
                _logger.LogInformation(
                    "Anchored org {CanonicalOrgId}/{OrgName} to {Host} from research output.",
                    org.Id, org.DisplayName, host);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Website backfill failed for org {CanonicalOrgId}.", org.Id);
        }
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

    // Website/WebsiteDomain are the ENTITY ANCHOR. Before 2026-09-03 the research
    // step received only Id/DisplayName/Kind, so a display name that collides with
    // a bigger company elsewhere researched that company instead — canonical 74300
    // held a Victoria architecture practice and was rewritten as a Denver developer
    // because both were called "Continuum". Nothing errored.
    private sealed record ResearchOrgCandidate(
        long Id,
        string DisplayName,
        string Kind,
        string? Website = null,
        string? WebsiteDomain = null)
    {
        /// <summary>The anchor host ("perkinswill.com"), or null when this org has none.</summary>
        public string? AnchorHost =>
            ResearchIdentityGate.NormalizeHost(Website) ?? ResearchIdentityGate.NormalizeHost(WebsiteDomain);

        public bool HasAnchor => AnchorHost is not null;
    }
}

public sealed record BdResearchExecutorRunResult(
    int OrgsConsidered,
    int OrgsExecuted,
    int Successes,
    int Failures,
    long TotalInputTokens,
    long TotalOutputTokens);
