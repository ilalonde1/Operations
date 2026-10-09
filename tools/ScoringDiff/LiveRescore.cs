using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Core.Scoring;
using Kor.Opportunities.Data.Opportunities;
using Kor.Opportunities.Data.Scoring;
using Microsoft.Data.SqlClient;

namespace Kor.ScoringDiff;

internal static class LiveRescore
{
    // Scores only applications. One paged production read, one bulk upload, one
    // set-based UPDATE. No calls to UpdateAsync (which also changes pursuit/audit
    // fields), no changes to source/schema/ingestion gates, no tender refresh.
    public static async Task Run(Snapshot snapshot, Movement[] results, string directory, bool apply, bool rollback)
    {
        var connection = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB")
            ?? throw new InvalidOperationException("Missing process database environment variable.");
        var timer = Stopwatch.StartNew();
        var live = await new SqlOpportunityStore(connection).ListHubAsync(CancellationToken.None);
        var byId = live.ToDictionary(r => r.Opportunity.Id);
        foreach (var row in snapshot.Rows)
        {
            if (!byId.TryGetValue(row.Opportunity.Id, out var current)
                || current.Opportunity.ScoringContext != new OpportunityScoringContext(row.Kind, row.Description)
                || current.Kind != row.Kind)
                throw new InvalidOperationException($"Production read context differs from snapshot for {row.Opportunity.Id}; capture/review a fresh snapshot.");
        }
        var readSeconds = timer.Elapsed.TotalSeconds;
        Console.WriteLine($"Production read: {live.Count} rows in {readSeconds:F2}s; every frozen description/kind matches.");
        if (!apply) return;
        // The Worker can refresh unrelated bid audit/classification fields after
        // the original corpus capture. Compare our write against the immediate
        // pre-write read, while keeping the original application concurrency guard.
        var verificationResults = results.Select(r => new Movement(new CorpusRow
        {
            Opportunity = byId[r.Row.Opportunity.Id].Opportunity, Kind = r.Row.Kind,
            Description = r.Row.Description, Sources = r.Row.Sources, Baseline = r.Row.Baseline,
            ObservationId = r.Row.ObservationId, IsBigTicket = r.Row.IsBigTicket,
        }, r.After)).ToArray();
        var profile = await new SqlScoringProfileStore(connection).LoadAsync(CancellationToken.None);
        if ((profile is not null) != snapshot.HasProfile || (profile is not null
            && JsonSerializer.Serialize(profile) != JsonSerializer.Serialize(snapshot.Options)))
            throw new InvalidOperationException("Scoring profile changed since capture.");

        using var table = new DataTable();
        table.Columns.Add("Id", typeof(long));
        table.Columns.Add("Score", typeof(decimal));
        table.Columns.Add("Tier", typeof(int));
        table.Columns.Add("ExpectedVersion", typeof(byte[]));
        table.Columns.Add("ObservationId", typeof(long));
        table.Columns.Add("Description", typeof(string));
        foreach (var row in results.Where(r => r.Row.Kind == OpportunityKind.Application))
            table.Rows.Add(row.Row.Opportunity.Id, row.After.Score, (int)row.After.Tier,
                row.Row.Opportunity.RowVersion, (object?)row.Row.ObservationId ?? DBNull.Value,
                (object?)row.Row.Description ?? DBNull.Value);

        await using var con = new SqlConnection(connection);
        await con.OpenAsync();
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(IsolationLevel.Serializable);
        async Task Execute(string sql)
        {
            await using var command = new SqlCommand(sql, con, tx) { CommandTimeout = 120 };
            await command.ExecuteNonQueryAsync();
        }
        await Execute("""
            CREATE TABLE #scores (Id bigint PRIMARY KEY, Score decimal(10,4), Tier int,
                ExpectedVersion binary(8), ObservationId bigint NULL, Description nvarchar(max) NULL);
            """);
        using (var copy = new SqlBulkCopy(con, SqlBulkCopyOptions.Default, tx) { DestinationTableName = "#scores", BulkCopyTimeout = 120 })
        {
            foreach (DataColumn column in table.Columns) copy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            await copy.WriteToServerAsync(table);
        }

        // Hold source memberships/mappings stable until commit. Use the SAME .NET
        // kind authority as the hub, not a second SQL classification CASE list.
        var kinds = new Dictionary<long, List<OpportunityKind>>();
        await using (var command = new SqlCommand("""
            SELECT ob.OpportunityId, s.SourceType, m.ValueJson
            FROM opportunities.OpportunityObservations ob
            JOIN #scores x ON x.Id=ob.OpportunityId
            JOIN opportunities.OpportunitySources s ON s.Id=ob.OpportunitySourceId
            LEFT JOIN opportunities.OpportunitySourceMappings m ON m.OpportunitySourceId=s.Id AND m.[Key]='bd.kind';
            """, con, tx) { CommandTimeout = 120 })
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
            {
                var id = reader.GetInt64(0);
                if (!kinds.TryGetValue(id, out var list)) kinds[id] = list = [];
                list.Add(OpportunityKinds.FromSource((OpportunitySourceType)reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        if (kinds.Count != table.Rows.Count || kinds.Values.Any(k => OpportunityKinds.Combine(k) != OpportunityKind.Application))
            throw new InvalidOperationException("Application source memberships changed; no scores written.");

        // Guard ALL application inputs, including those with no score movement.
        // A stale observation, edit, dismissal or missing row aborts the entire batch.
        await Execute("""
            IF EXISTS (
                SELECT 1 FROM #scores x LEFT JOIN opportunities.Opportunities o WITH (UPDLOCK) ON o.Id=x.Id
                OUTER APPLY (SELECT TOP (1) ob.Id, ob.Description FROM opportunities.OpportunityObservations ob
                             WHERE ob.OpportunityId=x.Id ORDER BY ob.Id DESC) d
                WHERE o.Id IS NULL OR o.RowVersion<>x.ExpectedVersion OR o.DismissedAtUtc IS NOT NULL
                   OR ISNULL(d.Id,-1)<>ISNULL(x.ObservationId,-1)
                   OR ISNULL(d.Description,N'') COLLATE Latin1_General_100_BIN2 <> ISNULL(x.Description,N'') COLLATE Latin1_General_100_BIN2)
                THROW 51000, 'Stale scoring inputs; capture/review a fresh snapshot. Nothing written.', 1;
            """);
        var ledger = new List<Applied>();
        await using (var command = new SqlCommand("""
            UPDATE o SET RelevanceScore=x.Score, RelevanceTier=x.Tier
            OUTPUT inserted.Id, deleted.RelevanceScore, deleted.RelevanceTier,
                   inserted.RelevanceScore, inserted.RelevanceTier, inserted.RowVersion
            FROM opportunities.Opportunities o JOIN #scores x ON x.Id=o.Id
            WHERE o.RelevanceScore IS NULL OR o.RelevanceScore<>x.Score
               OR o.RelevanceTier IS NULL OR o.RelevanceTier<>x.Tier;
            """, con, tx) { CommandTimeout = 120 })
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                ledger.Add(new(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetDecimal(1),
                    reader.IsDBNull(2) ? null : (RelevanceTier)reader.GetInt32(2), reader.GetDecimal(3),
                    (RelevanceTier)reader.GetInt32(4), (byte[])reader.GetValue(5)));

        var reportPath = Path.Combine(directory, rollback ? "rollback-ledger.json" : "applied-ledger.json");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(ledger.OrderBy(r => r.Id), new JsonSerializerOptions { WriteIndented = true }));
        if (rollback) await tx.RollbackAsync();
        else await tx.CommitAsync();
        var summary = $"{(rollback ? "ROLLED BACK" : "COMMITTED")}: {ledger.Count} application rows updated; "
            + $"tier promotions={ledger.Count(r => r.BeforeTier.HasValue && r.AfterTier > r.BeforeTier)}, "
            + $"demotions={ledger.Count(r => r.BeforeTier.HasValue && r.AfterTier < r.BeforeTier)}, "
            + $"previously unscored tiers={ledger.Count(r => !r.BeforeTier.HasValue)}. "
            + $"Production read={readSeconds:F2}s; total={timer.Elapsed.TotalSeconds:F2}s.\n";
        await File.WriteAllTextAsync(Path.Combine(directory, rollback ? "rollback.txt" : "applied.txt"), summary);
        Console.Write(summary);
        await Verify(snapshot, verificationResults, directory, rollback);
    }

    public static async Task Verify(Snapshot snapshot, Movement[] results, string directory, bool rolledBack = false)
    {
        var connection = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB")
            ?? throw new InvalidOperationException("Missing process database environment variable.");
        var live = (await new SqlOpportunityStore(connection).ListHubAsync(CancellationToken.None)).ToDictionary(r => r.Opportunity.Id);
        var failures = new List<string>();
        foreach (var result in results)
        {
            var before = result.Row.Opportunity;
            if (!live.TryGetValue(before.Id, out var entry)) { failures.Add($"{before.Id}: missing"); continue; }
            var after = entry.Opportunity;
            var shouldRescore = !rolledBack && result.Row.Kind == OpportunityKind.Application;
            var expectedScore = shouldRescore ? result.After.Score : before.RelevanceScore;
            var expectedTier = shouldRescore ? result.After.Tier : before.RelevanceTier;
            if (after.RelevanceScore != expectedScore || after.RelevanceTier != expectedTier)
                failures.Add($"{before.Id}: stored score/tier differs from expected");
            var changed = expectedScore != before.RelevanceScore || expectedTier != before.RelevanceTier;
            var comparable = after with { RelevanceScore = before.RelevanceScore, RelevanceTier = before.RelevanceTier,
                RowVersion = changed ? before.RowVersion : after.RowVersion };
            foreach (var property in typeof(Opportunity).GetProperties().Where(p => p.Name != nameof(Opportunity.ScoringContext)))
            {
                var oldValue = property.GetValue(before);
                var newValue = property.GetValue(comparable);
                var equal = oldValue is byte[] oldBytes && newValue is byte[] newBytes
                    ? oldBytes.SequenceEqual(newBytes) : Equals(oldValue, newValue);
                if (!equal) failures.Add($"{before.Id}: non-score field {property.Name} changed");
            }
            if (after.ScoringContext != new OpportunityScoringContext(result.Row.Kind, result.Row.Description))
                failures.Add($"{before.Id}: scoring evidence changed");
        }
        var summary = $"Verified {snapshot.Rows.Count} stored rows: score/tier, non-score fields, latest description/kind, "
            + $"and unchanged rowversions for untouched rows. Failures={failures.Count}; newly arrived rows={live.Count - snapshot.Rows.Count}.\n";
        await File.WriteAllTextAsync(Path.Combine(directory, rolledBack ? "rollback-verification.txt" : "live-verification.txt"), summary + string.Join('\n', failures));
        Console.Write(summary);
        if (failures.Count != 0) throw new InvalidOperationException("Live verification failed; see verification report.");
    }

    internal sealed record Applied(long Id, decimal? BeforeScore, RelevanceTier? BeforeTier,
        decimal AfterScore, RelevanceTier AfterTier, byte[] RowVersion);
}
