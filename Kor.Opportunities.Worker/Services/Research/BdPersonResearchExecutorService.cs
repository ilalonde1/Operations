#nullable enable
using System.Data;
using System.Globalization;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.People;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Opportunities.Worker.Services.Research;

/// <summary>
/// Person auto-refresh executor. Parallels <c>BdResearchExecutorService</c>
/// (orgs) and <c>BdProjectResearchExecutorService</c> (projects). Picks stale
/// IntelPerson rows that have a current affiliation, calls Sonnet via the
/// shared <see cref="IResearchExecutorService"/> with the PersonBrief
/// structured-output schema, and pushes the result through
/// <see cref="IPersonRefreshChokepoint"/>.
/// </summary>
public sealed class BdPersonResearchExecutorService
{
    private const string BatchProviderName = "PersonBrief";
    private const int CommandTimeoutSeconds = 120;

    /// <summary>
    /// Structured-output schema for the format phase, threaded through
    /// <see cref="ResearchTarget.StructuredOutputJsonSchema"/>. Matches the
    /// JSON shape <c>PersonBriefExtractor</c> parses.
    /// </summary>
    private const string PersonBriefSchema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""overallConfidence"": { ""type"": ""number"", ""description"": ""0.0 to 1.0"" },
    ""person"": {
      ""type"": ""object"",
      ""properties"": {
        ""email"":       { ""type"": [""string"", ""null""] },
        ""phone"":       { ""type"": [""string"", ""null""] },
        ""linkedinUrl"": { ""type"": [""string"", ""null""] },
        ""notes"":       { ""type"": [""string"", ""null""], ""description"": ""2-4 sentences capturing what KOR should know about this person right now."" }
      }
    },
    ""currentAffiliation"": {
      ""type"": ""object"",
      ""properties"": {
        ""title"":           { ""type"": [""string"", ""null""] },
        ""department"":      { ""type"": [""string"", ""null""] },
        ""startDateApprox"": { ""type"": [""string"", ""null""] },
        ""confirmed"":       { ""type"": ""boolean"", ""description"": ""false if the person has moved to a different employer."" }
      }
    },
    ""recentSignals"": { ""type"": ""array"", ""items"": {
      ""type"": ""object"",
      ""properties"": {
        ""type"":      { ""type"": ""string"", ""description"": ""LeadershipChange | HiringSurge | OfficeMove | OwnershipMnA | CapacityStrain | RecentWin | Other"" },
        ""subject"":   { ""type"": ""string"" },
        ""detail"":    { ""type"": [""string"", ""null""] },
        ""occurredAt"": { ""type"": [""string"", ""null""] },
        ""sourceUrl"": { ""type"": [""string"", ""null""] }
      },
      ""required"": [""type"", ""subject""]
    }},
    ""korActions"": { ""type"": ""array"", ""items"": {
      ""type"": ""object"",
      ""properties"": {
        ""type"":           { ""type"": ""string"", ""description"": ""ContactStrategy | PursuitAngle | TimingWindow | HowToGetOnRoster | KorDisplacementRead | Other"" },
        ""recommendation"": { ""type"": ""string"" },
        ""timingNotes"":    { ""type"": [""string"", ""null""] }
      },
      ""required"": [""type"", ""recommendation""]
    }}
  },
  ""required"": [""overallConfidence""]
}";

    private const string PersonBriefFormatInstruction =
        "Include person (with notes), currentAffiliation, recentSignals, and korActions whenever the research supports them.";

    private readonly OpportunitiesWorkerOptions _workerOptions;
    private readonly BdPersonResearchExecutorOptions _options;
    private readonly IResearchExecutorService _executor;
    private readonly IPersonResearchPromptCatalog _catalog;
    private readonly IPersonRefreshChokepoint _chokepoint;
    private readonly ILogger<BdPersonResearchExecutorService> _logger;

    public BdPersonResearchExecutorService(
        IOptions<OpportunitiesWorkerOptions> workerOptions,
        IOptions<BdPersonResearchExecutorOptions> options,
        IResearchExecutorService executor,
        IPersonResearchPromptCatalog catalog,
        IPersonRefreshChokepoint chokepoint,
        ILogger<BdPersonResearchExecutorService> logger)
    {
        _workerOptions = workerOptions?.Value ?? throw new ArgumentNullException(nameof(workerOptions));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _chokepoint = chokepoint ?? throw new ArgumentNullException(nameof(chokepoint));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<BdPersonResearchExecutorRunResult> RunBatchAsync(CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("BD person research executor is disabled.");
            return new BdPersonResearchExecutorRunResult(0, 0, 0, 0, 0, 0);
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
                    "BD person research executor stopping early: output token budget reached. outputTok={OutputTokens}; budget={Budget}.",
                    totalOutputTokens,
                    _options.DailyOutputTokenBudget);
                break;
            }

            var prompt = _catalog.Resolve(
                BatchProviderName,
                candidate.DisplayName,
                candidate.CurrentTitle,
                candidate.CurrentEmployerName,
                candidate.Id);
            if (prompt is null)
            {
                _logger.LogInformation(
                    "BD person research executor skipped IntelPerson {PersonId}/{DisplayName}: no prompt template.",
                    candidate.Id,
                    candidate.DisplayName);
                continue;
            }

            executed++;
            try
            {
                var result = await _executor.ExecuteAsync(
                    new ResearchTarget(
                        candidate.Id,
                        candidate.DisplayName,
                        OrgKind: "Person",
                        BatchProviderName,
                        prompt.SystemPrompt,
                        prompt.UserPrompt,
                        PersonBriefSchema,
                        PersonBriefFormatInstruction),
                    ct).ConfigureAwait(false);

                if (result is null)
                {
                    failures++;
                    continue;
                }

                await WriteOutputAsync(result, candidate.Id, ct).ConfigureAwait(false);
                await PushThroughChokepointAsync(result, candidate.Id, ct).ConfigureAwait(false);
                successes++;
                totalInputTokens += result.InputTokens;
                totalOutputTokens += result.OutputTokens;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures++;
                _logger.LogWarning(
                    ex,
                    "BD person research executor failed IntelPerson {PersonId}/{DisplayName}.",
                    candidate.Id,
                    candidate.DisplayName);
            }
        }

        return new BdPersonResearchExecutorRunResult(
            candidates.Count,
            executed,
            successes,
            failures,
            totalInputTokens,
            totalOutputTokens);
    }

    public async Task<ExecutedResearch?> ExecuteOneAsync(long intelPersonId, CancellationToken ct)
    {
        try
        {
            var person = await LoadPersonAsync(intelPersonId, ct).ConfigureAwait(false);
            if (person is null)
            {
                _logger.LogWarning(
                    "BD person research executor manual refresh skipped: IntelPerson {PersonId} not found or no current affiliation.",
                    intelPersonId);
                return null;
            }

            var prompt = _catalog.Resolve(
                BatchProviderName,
                person.DisplayName,
                person.CurrentTitle,
                person.CurrentEmployerName,
                person.Id);
            if (prompt is null)
            {
                _logger.LogWarning(
                    "BD person research executor manual refresh skipped: no prompt template for IntelPerson {PersonId}.",
                    intelPersonId);
                return null;
            }

            var result = await _executor.ExecuteAsync(
                new ResearchTarget(
                    person.Id,
                    person.DisplayName,
                    OrgKind: "Person",
                    BatchProviderName,
                    prompt.SystemPrompt,
                    prompt.UserPrompt,
                    PersonBriefSchema,
                    PersonBriefFormatInstruction),
                ct).ConfigureAwait(false);

            if (result is null)
            {
                return null;
            }

            await WriteOutputAsync(result, person.Id, ct).ConfigureAwait(false);
            await PushThroughChokepointAsync(result, person.Id, ct).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "BD person research executor manual refresh failed for IntelPerson {PersonId}.",
                intelPersonId);
            return null;
        }
    }

    private async Task<IReadOnlyList<ResearchPersonCandidate>> LoadCandidatesAsync(CancellationToken ct)
    {
        if (_options.MaxPeoplePerRun <= 0)
        {
            return Array.Empty<ResearchPersonCandidate>();
        }

        // Staleness is measured by the last DEDICATED PersonBrief refresh
        // (CanonicalOrgEnrichment row whose ProviderName matches the
        // synthetic "PersonBrief-{personId}" tag this executor writes).
        //
        // Why not IntelPerson.LastSeenAtUtc: org-side R83 refreshes bump
        // LastSeenAtUtc on every IntelPerson the model mentions, which
        // would make this cron a no-op as long as the org executor is
        // running. The dedicated-refresh check is independent and gives
        // each person their own refresh budget.
        //
        // LEFT JOIN on ProviderName alone (not CanonicalOrgId) so a person
        // who's switched employers between refreshes still finds their
        // last refresh row (which was archived against the prior employer).
        const string sql = @"
SELECT TOP (@max)
    p.Id,
    p.DisplayName,
    cur.Title             AS CurrentTitle,
    cur.OrgName           AS CurrentEmployerName,
    e.LastRefreshAtUtc    AS LastPersonRefreshAtUtc
FROM opportunities.IntelPerson p
OUTER APPLY (
    SELECT TOP 1
        a.CanonicalOrgId,
        co.DisplayName AS OrgName,
        a.Title,
        a.LastSeenAtUtc
    FROM opportunities.IntelPersonAffiliation a
    INNER JOIN opportunities.CanonicalOrg co ON co.Id = a.CanonicalOrgId
    WHERE a.IntelPersonId = p.Id
      AND a.IsCurrent = 1
      AND a.RetiredAtUtc IS NULL
    ORDER BY a.LastSeenAtUtc DESC
) cur
LEFT JOIN opportunities.CanonicalOrgEnrichment e
    ON e.ProviderName = N'PersonBrief-' + CAST(p.Id AS NVARCHAR(20))
WHERE p.RetiredAtUtc IS NULL
  AND cur.CanonicalOrgId IS NOT NULL
  AND (e.LastRefreshAtUtc IS NULL
       OR e.LastRefreshAtUtc < DATEADD(DAY, -@staleness, sysdatetimeoffset()))
ORDER BY ISNULL(e.LastRefreshAtUtc, '0001-01-01') ASC, p.Id ASC;";

        var rows = new List<ResearchPersonCandidate>();
        await using var con = new SqlConnection(_workerOptions.OpportunitiesDb);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con)
        {
            CommandType = CommandType.Text,
            CommandTimeout = CommandTimeoutSeconds,
        };
        cmd.Parameters.Add("@max", SqlDbType.Int).Value = _options.MaxPeoplePerRun;
        cmd.Parameters.Add("@staleness", SqlDbType.Int).Value = _options.StalenessDays;

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new ResearchPersonCandidate(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return rows;
    }

    private async Task<ResearchPersonCandidate?> LoadPersonAsync(long intelPersonId, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP 1
    p.Id,
    p.DisplayName,
    cur.Title         AS CurrentTitle,
    cur.OrgName       AS CurrentEmployerName
FROM opportunities.IntelPerson p
OUTER APPLY (
    SELECT TOP 1
        a.CanonicalOrgId,
        co.DisplayName AS OrgName,
        a.Title,
        a.LastSeenAtUtc
    FROM opportunities.IntelPersonAffiliation a
    INNER JOIN opportunities.CanonicalOrg co ON co.Id = a.CanonicalOrgId
    WHERE a.IntelPersonId = p.Id
      AND a.IsCurrent = 1
      AND a.RetiredAtUtc IS NULL
    ORDER BY a.LastSeenAtUtc DESC
) cur
WHERE p.Id = @id
  AND p.RetiredAtUtc IS NULL
  AND cur.CanonicalOrgId IS NOT NULL;";

        await using var con = new SqlConnection(_workerOptions.OpportunitiesDb);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con)
        {
            CommandType = CommandType.Text,
            CommandTimeout = CommandTimeoutSeconds,
        };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = intelPersonId;

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new ResearchPersonCandidate(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private async Task WriteOutputAsync(ExecutedResearch result, long intelPersonId, CancellationToken ct)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dir = Path.Combine(_options.OutputDir, today);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"refresh-person-{intelPersonId}-{SafeFilePart(result.ProviderName)}.json");
        await File.WriteAllTextAsync(path, result.ResultJson, ct).ConfigureAwait(false);
    }

    private async Task PushThroughChokepointAsync(ExecutedResearch executed, long intelPersonId, CancellationToken ct)
    {
        try
        {
            var result = new EnrichmentResult(
                EnrichmentStatuses.Ok,
                null,
                executed.ResultJson,
                $"Auto-refreshed via BdPersonResearchExecutor at {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");
            var nextRefresh = DateTimeOffset.UtcNow.AddDays(Math.Max(7, _options.StalenessDays));
            await _chokepoint.RecordAttemptAsync(
                intelPersonId,
                result,
                nextRefresh,
                ct).ConfigureAwait(false);

            _logger.LogInformation(
                "BD person research executor pushed result through person chokepoint for IntelPerson {PersonId}/{ProviderName}.",
                intelPersonId,
                executed.ProviderName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "BD person research executor saved file but chokepoint push failed for IntelPerson {PersonId}/{ProviderName}.",
                intelPersonId,
                executed.ProviderName);
        }
    }

    private static string SafeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(ch => invalid.Contains(ch) ? '_' : ch));
    }

    private sealed record ResearchPersonCandidate(
        long Id,
        string DisplayName,
        string? CurrentTitle,
        string? CurrentEmployerName);
}

public sealed record BdPersonResearchExecutorRunResult(
    int PeopleConsidered,
    int PeopleExecuted,
    int Successes,
    int Failures,
    long TotalInputTokens,
    long TotalOutputTokens);
