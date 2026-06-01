#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

[DisallowConcurrentExecution]
public sealed class BdResearchQueueBuilderJob : IJob
{
    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<BdResearchQueueBuilderJob> _logger;

    public BdResearchQueueBuilderJob(
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<BdResearchQueueBuilderJob> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var opt = _options.Value;
        if (!opt.BdResearchQueueEnabled)
        {
            _logger.LogDebug(
                "{Job} skipped: feature disabled via {Flag}.",
                nameof(BdResearchQueueBuilderJob),
                nameof(opt.BdResearchQueueEnabled));
            return;
        }

        var ct = context.CancellationToken;
        try
        {
            var batchSize = Math.Max(1, opt.BdResearchQueueBatchSize);
            _logger.LogInformation(
                "{Job}: building nightly BD research queue batchSize={BatchSize} outputDir={OutputDir}.",
                nameof(BdResearchQueueBuilderJob),
                batchSize,
                opt.BdResearchQueueOutputDir);

            var rows = await LoadQueueRowsAsync(opt.OpportunitiesDb, batchSize, ct).ConfigureAwait(false);
            var latestPath = WriteCsvs(opt.BdResearchQueueOutputDir, DateTimeOffset.Now, rows);

            if (rows.Count > 0)
            {
                _logger.LogInformation(
                    "{Job}: queued {Count} canonical orgs for research (gap providers: DataHoning/StructuralPartnerMap/CompetitorSignals). Latest written to {Path}.",
                    nameof(BdResearchQueueBuilderJob),
                    rows.Count,
                    latestPath);
            }
            else
            {
                _logger.LogInformation(
                    "{Job}: zero un-enriched canonical orgs above batch threshold - nothing to queue.",
                    nameof(BdResearchQueueBuilderJob));
            }

            context.Result = $"{nameof(BdResearchQueueBuilderJob)} queued {rows.Count} row(s). Latest={latestPath}";
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "{Job} canceled.", nameof(BdResearchQueueBuilderJob));
            context.Result = $"{nameof(BdResearchQueueBuilderJob)} canceled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Job} failed.", nameof(BdResearchQueueBuilderJob));
            context.Result = $"{nameof(BdResearchQueueBuilderJob)} failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static async Task<IReadOnlyList<ResearchQueueRow>> LoadQueueRowsAsync(
        string connectionString,
        int batchSize,
        CancellationToken ct)
    {
        const string sql = @"
WITH OrgGap AS
(
    SELECT TOP (@batch)
        co.Id,
        co.DisplayName,
        co.Kind,
        ISNULL(co.KorProjectsCount, 0) AS KorProjectsCount,
        co.LastKorProjectAtUtc
    FROM opportunities.CanonicalOrg co
    LEFT JOIN
    (
        SELECT CanonicalOrgId, COUNT(*) AS RichCount
        FROM opportunities.CanonicalOrgEnrichment
        WHERE ProviderName IN
            (N'DataHoning', N'StructuralPartnerMap', N'CompetitorSignals')
        GROUP BY CanonicalOrgId
    ) e ON e.CanonicalOrgId = co.Id
    WHERE co.Kind IN
        (N'Architect', N'Buyer', N'Competitor', N'Developer')
      AND ISNULL(e.RichCount, 0) < 3
    ORDER BY co.KorProjectsCount DESC,
             co.LastKorProjectAtUtc DESC,
             co.DisplayName ASC
)
SELECT
    o.Id,
    o.DisplayName,
    o.Kind,
    o.KorProjectsCount,
    o.LastKorProjectAtUtc,
    STUFF((
        SELECT N',' + p.ProviderName
        FROM (VALUES
            (N'DataHoning'),
            (N'StructuralPartnerMap'),
            (N'CompetitorSignals')) AS p(ProviderName)
        WHERE NOT EXISTS (
            SELECT 1 FROM opportunities.CanonicalOrgEnrichment e2
            WHERE e2.CanonicalOrgId = o.Id
              AND e2.ProviderName = p.ProviderName)
        FOR XML PATH(N''), TYPE).value(N'.', N'nvarchar(max)'),
        1, 1, N'') AS MissingProviders
FROM OrgGap o
ORDER BY o.KorProjectsCount DESC,
         o.LastKorProjectAtUtc DESC,
         o.DisplayName ASC;";

        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 120 };
        cmd.Parameters.Add("@batch", SqlDbType.Int).Value = batchSize;

        var rows = new List<ResearchQueueRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new ResearchQueueRow(
                r.GetInt64(0),
                r.GetString(1),
                r.GetString(2),
                r.GetInt32(3),
                r.IsDBNull(4) ? null : r.GetDateTimeOffset(4),
                r.IsDBNull(5) ? string.Empty : r.GetString(5)));
        }

        return rows;
    }

    private static string WriteCsvs(string outputDir, DateTimeOffset timestamp, IReadOnlyList<ResearchQueueRow> rows)
    {
        Directory.CreateDirectory(outputDir);
        var latestPath = Path.Combine(outputDir, "next-batch.csv");
        var stampedPath = Path.Combine(
            outputDir,
            timestamp.ToString("yyyy-MM-ddTHH-mm", CultureInfo.InvariantCulture) + "-next-batch.csv");
        var lines = new[] { "CanonicalOrgId,DisplayName,Kind,KorProjectsCount,LastKorProjectAtUtc,MissingProviders" }
            .Concat(rows.Select(r => CsvRow(
                r.CanonicalOrgId.ToString(CultureInfo.InvariantCulture),
                r.DisplayName,
                r.Kind,
                r.KorProjectsCount.ToString(CultureInfo.InvariantCulture),
                r.LastKorProjectAtUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                r.MissingProviders)))
            .ToArray();

        File.WriteAllLines(stampedPath, lines, Encoding.UTF8);
        File.WriteAllLines(latestPath, lines, Encoding.UTF8);
        return latestPath;
    }

    private static string CsvRow(params string?[] values)
        => string.Join(",", values.Select(Csv));

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private sealed record ResearchQueueRow(
        long CanonicalOrgId,
        string DisplayName,
        string Kind,
        int KorProjectsCount,
        DateTimeOffset? LastKorProjectAtUtc,
        string MissingProviders);
}
