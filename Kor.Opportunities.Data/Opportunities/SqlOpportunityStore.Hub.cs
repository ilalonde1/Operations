#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Opportunities;

public sealed partial class SqlOpportunityStore
{
    public async Task<IReadOnlyList<OpportunityHubRecord>> ListHubAsync(CancellationToken ct)
    {
        var result = new List<OpportunityHubRecord>();
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        // Freeze the upper ID boundary so a busy ingestion run cannot extend this read forever.
        await using var boundary = new SqlCommand("SELECT ISNULL(MAX(Id),0) FROM opportunities.Opportunities;", con);
        var lastId = Convert.ToInt64(await boundary.ExecuteScalarAsync(ct).ConfigureAwait(false));
        long after = 0;
        while (after < lastId)
        {
            await using var cmd = new SqlCommand($@"
DECLARE @page TABLE (Id bigint PRIMARY KEY);
INSERT @page SELECT TOP (1000) Id FROM opportunities.Opportunities
WHERE Id > @after AND Id <= @last AND DismissedAtUtc IS NULL ORDER BY Id;
SELECT {AllColumns} FROM opportunities.Opportunities WHERE Id IN (SELECT Id FROM @page) ORDER BY Id;
SELECT ob.OpportunityId, s.SourceType, m.ValueJson, ob.PostedDateUtc
FROM opportunities.OpportunityObservations ob
JOIN opportunities.OpportunitySources s ON s.Id = ob.OpportunitySourceId
LEFT JOIN opportunities.OpportunitySourceMappings m ON m.OpportunitySourceId = s.Id AND m.[Key] = 'bd.kind'
WHERE ob.OpportunityId IN (SELECT Id FROM @page);", con) { CommandTimeout = CommandTimeoutSeconds };
            cmd.Parameters.Add("@after", SqlDbType.BigInt).Value = after;
            cmd.Parameters.Add("@last", SqlDbType.BigInt).Value = lastId;
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            var page = new List<Opportunity>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false)) page.Add(MapReader(reader));
            if (page.Count == 0) break;
            var kinds = new Dictionary<long, List<OpportunityKind>>();
            var dates = new Dictionary<long, DateTimeOffset>();
            await reader.NextResultAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var id = reader.GetInt64(0);
                if (!kinds.TryGetValue(id, out var list)) kinds[id] = list = new();
                list.Add(OpportunityKinds.FromSource((OpportunitySourceType)reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
                if (!reader.IsDBNull(3))
                {
                    var date = reader.GetDateTimeOffset(3);
                    if (!dates.TryGetValue(id, out var previous) || date > previous) dates[id] = date;
                }
            }
            foreach (var row in page)
                result.Add(new(row, kinds.TryGetValue(row.Id, out var values) ? OpportunityKinds.Combine(values) : OpportunityKind.Other,
                    dates.TryGetValue(row.Id, out var date) ? date : null));
            after = page[^1].Id;
        }
        return result;
    }
}
