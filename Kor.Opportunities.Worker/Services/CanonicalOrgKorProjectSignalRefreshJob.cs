#nullable enable
using System;
using System.Data;
using System.Diagnostics;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Deltek;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

[DisallowConcurrentExecution]
public sealed class CanonicalOrgKorProjectSignalRefreshJob : IJob
{
    private readonly IKorWonProjectAccessor _accessor;
    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<CanonicalOrgKorProjectSignalRefreshJob> _logger;

    public CanonicalOrgKorProjectSignalRefreshJob(
        IKorWonProjectAccessor accessor,
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<CanonicalOrgKorProjectSignalRefreshJob> logger)
    {
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var opt = _options.Value;
        if (!opt.CanonicalOrgKorProjectSignalRefreshEnabled)
        {
            _logger.LogDebug(
                "{Job} skipped: feature disabled via {Flag}.",
                nameof(CanonicalOrgKorProjectSignalRefreshJob),
                nameof(opt.CanonicalOrgKorProjectSignalRefreshEnabled));
            return;
        }

        if (_accessor is NullKorWonProjectAccessor)
        {
            _logger.LogDebug("CanonicalOrg KOR-project signal refresh skipped: Deltek access disabled.");
            return;
        }

        var sw = Stopwatch.StartNew();
        var ct = context.CancellationToken;
        var aggregates = await _accessor.GetAllClientAggregatesAsync(ct).ConfigureAwait(false);
        if (aggregates.Count == 0)
        {
            _logger.LogInformation("CanonicalOrg KOR-project signal refresh: {Updated} rows in {ElapsedMs}ms.", 0, sw.ElapsedMilliseconds);
            return;
        }

        var table = new DataTable();
        table.Columns.Add("ClendorClientId", typeof(string));
        table.Columns.Add("LastStartDate", typeof(DateTime));
        table.Columns.Add("ProjectCount", typeof(int));
        foreach (var row in aggregates)
        {
            table.Rows.Add(row.ClendorClientId, row.LastStartDate, row.ProjectCount);
        }

        await using var cn = new SqlConnection(opt.OpportunitiesDb);
        await cn.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandTimeout = 120;
            cmd.CommandText = @"
CREATE TABLE #KorProjectSignal (
    ClendorClientId varchar(32) NOT NULL,
    LastStartDate datetime2(3) NOT NULL,
    ProjectCount int NOT NULL
);";
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        using (var bulk = new SqlBulkCopy(cn, SqlBulkCopyOptions.Default, tx))
        {
            bulk.DestinationTableName = "#KorProjectSignal";
            bulk.BatchSize = 1000;
            bulk.BulkCopyTimeout = 120;
            bulk.ColumnMappings.Add("ClendorClientId", "ClendorClientId");
            bulk.ColumnMappings.Add("LastStartDate", "LastStartDate");
            bulk.ColumnMappings.Add("ProjectCount", "ProjectCount");
            await bulk.WriteToServerAsync(table, ct).ConfigureAwait(false);
        }

        var updated = 0;
        await using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandTimeout = 120;
            cmd.CommandText = @"
UPDATE co
SET    LastKorProjectAtUtc = s.LastStartDate,
       KorProjectsCount = s.ProjectCount,
       UpdatedAtUtc = sysdatetimeoffset()
FROM   opportunities.CanonicalOrg co
JOIN   #KorProjectSignal s ON s.ClendorClientId = co.ClendorClientId
WHERE  co.RetiredAtUtc IS NULL;";
            updated = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
        _logger.LogInformation(
            "CanonicalOrg KOR-project signal refresh: {Updated} rows in {ElapsedMs}ms.",
            updated,
            sw.ElapsedMilliseconds);
    }
}
