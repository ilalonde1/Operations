#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.MajorProjects;

public sealed class SqlPrimePipelineStore : IPrimePipelineStore
{
    private const int CommandTimeoutSeconds = 60;

    private readonly string _connectionString;

    public SqlPrimePipelineStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<PrimePipelineRow>> GetAllAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT PipelineType, SourceRef, ProjectName, BuyerOrOwner, Sector, Stage,
       EstimatedValueCad, Province, City, ArchitectName, SourceUrl
FROM opportunities.PrimePipeline
ORDER BY CASE WHEN EstimatedValueCad IS NULL THEN 1 ELSE 0 END,
         EstimatedValueCad DESC,
         ProjectName;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var rows = new List<PrimePipelineRow>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new PrimePipelineRow(
                PipelineType: r.IsDBNull(0) ? "" : r.GetString(0),
                SourceRef: r.IsDBNull(1) ? "" : r.GetString(1),
                ProjectName: r.IsDBNull(2) ? "" : r.GetString(2),
                BuyerOrOwner: r.IsDBNull(3) ? null : r.GetString(3),
                Sector: r.IsDBNull(4) ? null : r.GetString(4),
                Stage: r.IsDBNull(5) ? null : r.GetString(5),
                EstimatedValueCad: r.IsDBNull(6) ? null : r.GetDecimal(6),
                Province: r.IsDBNull(7) ? null : r.GetString(7),
                City: r.IsDBNull(8) ? null : r.GetString(8),
                ArchitectName: r.IsDBNull(9) ? null : r.GetString(9),
                SourceUrl: r.IsDBNull(10) ? null : r.GetString(10)));
        }

        return rows;
    }
}
