#nullable enable
using System.Data;
using System.Globalization;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Projects;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Opportunities.Worker.Services.Research;

public sealed class BdProjectResearchExecutorService
{
    private const string BatchProviderName = "ProjectBrief";
    private const int CommandTimeoutSeconds = 120;

    // Structured-output schema passed into the AnthropicResearchExecutorService
    // format phase. Matches the JSON shape ProjectBriefExtractor parses
    // (description / schedule / status / korAngle / signals / actions /
    // risks / keyPeople). When this is null the executor falls back to
    // the org/FirmNarrative schema, which is the wrong shape for projects.
    private const string ProjectBriefSchema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""overallConfidence"": { ""type"": ""number"", ""description"": ""0.0 to 1.0"" },
    ""description"": { ""type"": [""string"", ""null""], ""description"": ""Long-form description of the project scope, drivers, owner intent."" },
    ""schedule"":    { ""type"": [""string"", ""null""], ""description"": ""Timing notes, milestones, expected RFP windows."" },
    ""status"":      { ""type"": [""string"", ""null""], ""description"": ""Current stage and recent status updates."" },
    ""korAngle"":    { ""type"": [""string"", ""null""], ""description"": ""How KOR competes here, displacement read."" },
    ""signals"": { ""type"": ""array"", ""items"": {
      ""type"": ""object"",
      ""properties"": {
        ""type"":    { ""type"": ""string"", ""description"": ""StageChange | Awarded | Delayed | ScopeChange | BudgetChange | TeamChange | Other"" },
        ""subject"": { ""type"": ""string"" },
        ""detail"":  { ""type"": [""string"", ""null""] },
        ""occurredAt"": { ""type"": [""string"", ""null""], ""description"": ""YYYY-MM or YYYY-MM-DD when known"" },
        ""sourceUrl"":  { ""type"": [""string"", ""null""] }
      },
      ""required"": [""type"", ""subject""]
    }},
    ""actions"": { ""type"": ""array"", ""items"": {
      ""type"": ""object"",
      ""properties"": {
        ""type"":           { ""type"": ""string"", ""description"": ""ContactStrategy | PursuitAngle | TimingWindow | TeamingMove | Other"" },
        ""recommendation"": { ""type"": ""string"" },
        ""targetPerson"":   { ""type"": [""string"", ""null""] },
        ""targetOrg"":      { ""type"": [""string"", ""null""] },
        ""timingNotes"":    { ""type"": [""string"", ""null""] }
      },
      ""required"": [""type"", ""recommendation""]
    }},
    ""risks"": { ""type"": ""array"", ""items"": {
      ""type"": ""object"",
      ""properties"": {
        ""type"":        { ""type"": ""string"", ""description"": ""StageDelay | BudgetCut | ScopeShrink | TeamFlip | CompetitorEntrenched | DataIssue | Other"" },
        ""description"": { ""type"": ""string"" },
        ""mitigation"":  { ""type"": [""string"", ""null""] }
      },
      ""required"": [""type"", ""description""]
    }},
    ""keyPeople"": { ""type"": ""array"", ""items"": {
      ""type"": ""object"",
      ""properties"": {
        ""name"":    { ""type"": ""string"" },
        ""title"":   { ""type"": [""string"", ""null""] },
        ""side"":    { ""type"": ""string"", ""description"": ""Owner | Architect | GC | Structural | Other"" },
        ""orgName"": { ""type"": [""string"", ""null""] }
      },
      ""required"": [""name"", ""side""]
    }}
  },
  ""required"": [""overallConfidence""]
}";

    private const string ProjectBriefFormatInstruction =
        "Include all description, schedule, status, korAngle, signals, actions, risks, and keyPeople fields supported by the research.";

    private readonly OpportunitiesWorkerOptions _workerOptions;
    private readonly BdProjectResearchExecutorOptions _options;
    private readonly IResearchExecutorService _executor;
    private readonly IProjectResearchPromptCatalog _catalog;
    private readonly IMajorProjectEnrichmentTrackingStore _enrichmentStore;
    private readonly ILogger<BdProjectResearchExecutorService> _logger;

    public BdProjectResearchExecutorService(
        IOptions<OpportunitiesWorkerOptions> workerOptions,
        IOptions<BdProjectResearchExecutorOptions> options,
        IResearchExecutorService executor,
        IProjectResearchPromptCatalog catalog,
        IMajorProjectEnrichmentTrackingStore enrichmentStore,
        ILogger<BdProjectResearchExecutorService> logger)
    {
        _workerOptions = workerOptions?.Value ?? throw new ArgumentNullException(nameof(workerOptions));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _enrichmentStore = enrichmentStore ?? throw new ArgumentNullException(nameof(enrichmentStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<BdProjectResearchExecutorRunResult> RunBatchAsync(CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("BD project research executor is disabled.");
            return new BdProjectResearchExecutorRunResult(0, 0, 0, 0, 0, 0);
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
                    "BD project research executor stopping early: output token budget reached. outputTok={OutputTokens}; budget={Budget}.",
                    totalOutputTokens,
                    _options.DailyOutputTokenBudget);
                break;
            }

            var prompt = _catalog.Resolve(
                candidate.Stage,
                BatchProviderName,
                candidate.ProjectName,
                candidate.ProponentName,
                candidate.City,
                candidate.Province);
            if (prompt is null)
            {
                _logger.LogInformation(
                    "BD project research executor skipped MPI {MpiId}/{ProjectName}: no prompt template for provider {ProviderName}.",
                    candidate.Id,
                    candidate.ProjectName,
                    BatchProviderName);
                continue;
            }

            executed++;
            try
            {
                var result = await _executor.ExecuteAsync(
                    new ResearchTarget(
                        candidate.Id,
                        candidate.ProjectName,
                        candidate.Stage,
                        BatchProviderName,
                        prompt.SystemPrompt,
                        prompt.UserPrompt.Replace("{CURRENT_INTEL_JSON}", "{}", StringComparison.Ordinal),
                        ProjectBriefSchema,
                        ProjectBriefFormatInstruction),
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
                    "BD project research executor failed MPI {MpiId}/{ProjectName}.",
                    candidate.Id,
                    candidate.ProjectName);
            }
        }

        return new BdProjectResearchExecutorRunResult(
            candidates.Count,
            executed,
            successes,
            failures,
            totalInputTokens,
            totalOutputTokens);
    }

    public async Task<ExecutedResearch?> ExecuteOneAsync(long mpiId, string providerName, CancellationToken ct)
    {
        try
        {
            var project = await LoadProjectAsync(mpiId, ct).ConfigureAwait(false);
            if (project is null)
            {
                _logger.LogWarning("BD project research executor manual refresh skipped: MPI {MpiId} was not found.", mpiId);
                return null;
            }

            var prompt = _catalog.Resolve(
                project.Stage,
                providerName,
                project.ProjectName,
                project.ProponentName,
                project.City,
                project.Province);
            if (prompt is null)
            {
                _logger.LogWarning(
                    "BD project research executor manual refresh skipped for MPI {MpiId}/{ProjectName}: no prompt template for provider {ProviderName}.",
                    mpiId,
                    project.ProjectName,
                    providerName);
                return null;
            }

            var result = await _executor.ExecuteAsync(
                new ResearchTarget(
                    project.Id,
                    project.ProjectName,
                    project.Stage,
                    providerName,
                    prompt.SystemPrompt,
                    prompt.UserPrompt.Replace("{CURRENT_INTEL_JSON}", "{}", StringComparison.Ordinal),
                    ProjectBriefSchema,
                    ProjectBriefFormatInstruction),
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
                "BD project research executor manual refresh failed for MPI {MpiId}, provider {ProviderName}.",
                mpiId,
                providerName);
            return null;
        }
    }

    private async Task<IReadOnlyList<ResearchProjectCandidate>> LoadCandidatesAsync(CancellationToken ct)
    {
        var eligibleStages = _options.EligibleStages
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (eligibleStages.Length == 0 || _options.MaxProjectsPerRun <= 0)
        {
            return Array.Empty<ResearchProjectCandidate>();
        }

        var stageParameters = string.Join(", ", eligibleStages.Select((_, i) => "@s" + i.ToString(CultureInfo.InvariantCulture)));
        var sql = @"
SELECT TOP (@max)
    mpi.Id,
    mpi.ProjectName,
    mpi.Stage,
    mpi.ProponentName,
    mpi.MunicipalityName,
    mpi.Province,
    last_seen.LastSeenAtUtc
FROM opportunities.MajorProjectsInventory mpi
LEFT JOIN (
    SELECT MajorProjectsInventoryId AS Id, MAX(LastSeenAtUtc) AS LastSeenAtUtc
    FROM (
        SELECT MajorProjectsInventoryId, LastSeenAtUtc
          FROM opportunities.IntelProject       WHERE RetiredAtUtc IS NULL
        UNION ALL
        SELECT MajorProjectsInventoryId, LastSeenAtUtc
          FROM opportunities.IntelProjectSignal WHERE RetiredAtUtc IS NULL
        UNION ALL
        SELECT MajorProjectsInventoryId, LastSeenAtUtc
          FROM opportunities.IntelProjectAction WHERE RetiredAtUtc IS NULL
    ) u
    GROUP BY MajorProjectsInventoryId
) last_seen ON last_seen.Id = mpi.Id
WHERE mpi.RetiredAtUtc IS NULL
  AND mpi.Stage IN (" + stageParameters + @")
  AND (last_seen.LastSeenAtUtc IS NULL
       OR last_seen.LastSeenAtUtc < DATEADD(DAY, -@staleness, sysdatetimeoffset()))
ORDER BY ISNULL(last_seen.LastSeenAtUtc, '0001-01-01') ASC;";

        var rows = new List<ResearchProjectCandidate>();
        await using var con = new SqlConnection(_workerOptions.OpportunitiesDb);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con)
        {
            CommandType = CommandType.Text,
            CommandTimeout = CommandTimeoutSeconds,
        };
        cmd.Parameters.Add("@max", SqlDbType.Int).Value = _options.MaxProjectsPerRun;
        cmd.Parameters.Add("@staleness", SqlDbType.Int).Value = _options.StalenessDays;
        for (var i = 0; i < eligibleStages.Length; i++)
        {
            cmd.Parameters.Add("@s" + i.ToString(CultureInfo.InvariantCulture), SqlDbType.NVarChar, 100).Value = eligibleStages[i];
        }

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new ResearchProjectCandidate(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return rows;
    }

    private async Task<ResearchProjectCandidate?> LoadProjectAsync(long mpiId, CancellationToken ct)
    {
        const string sql = @"
SELECT Id, ProjectName, Stage, ProponentName, MunicipalityName, Province
FROM opportunities.MajorProjectsInventory
WHERE Id = @id
  AND RetiredAtUtc IS NULL;";

        await using var con = new SqlConnection(_workerOptions.OpportunitiesDb);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con)
        {
            CommandType = CommandType.Text,
            CommandTimeout = CommandTimeoutSeconds,
        };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = mpiId;

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new ResearchProjectCandidate(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? "Unknown" : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5));
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
                $"Auto-refreshed via BdProjectResearchExecutor at {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");
            var nextRefresh = DateTimeOffset.UtcNow.AddDays(Math.Max(7, _options.StalenessDays));
            await _enrichmentStore.RecordAttemptAsync(
                executed.CanonicalOrgId,
                executed.ProviderName,
                result,
                nextRefresh,
                ct).ConfigureAwait(false);

            _logger.LogInformation(
                "BD project research executor pushed result through project chokepoint for MPI {MpiId}/{ProviderName}. Project Intel* auto-decomposition will run inline.",
                executed.CanonicalOrgId,
                executed.ProviderName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "BD project research executor saved file but chokepoint push failed for MPI {MpiId}/{ProviderName}.",
                executed.CanonicalOrgId,
                executed.ProviderName);
        }
    }

    private static string SafeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(ch => invalid.Contains(ch) ? '_' : ch));
    }

    private sealed record ResearchProjectCandidate(
        long Id,
        string ProjectName,
        string Stage,
        string? ProponentName,
        string? City,
        string? Province);
}

public sealed record BdProjectResearchExecutorRunResult(
    int ProjectsConsidered,
    int ProjectsExecuted,
    int Successes,
    int Failures,
    long TotalInputTokens,
    long TotalOutputTokens);
