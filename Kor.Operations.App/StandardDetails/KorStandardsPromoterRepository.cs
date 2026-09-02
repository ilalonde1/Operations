#nullable enable
#pragma warning disable SA1649
using System;
using System.Data;
using System.Threading.Tasks;
using Kor.Operations.Data;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.StandardDetails;

internal sealed class KorStandardsPromoterRepository
{
    private readonly string _connectionString;

    internal KorStandardsPromoterRepository(string promoterConnectionString)
    {
        _connectionString = promoterConnectionString ?? throw new ArgumentNullException(nameof(promoterConnectionString));
    }

    internal async Task<(bool ok, string message)> PromoteAsync(string detailNumber, string toConfidence, string basis, string changedBy)
    {
        try
        {
            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("detail.PromoteDetail", cn);
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = SqlTimeouts.UiFacing;
            AddNVarChar(cmd, "@DetailNumber", 24, detailNumber);
            AddNVarChar(cmd, "@ToConfidence", 32, toConfidence);
            AddNVarChar(cmd, "@Basis", 1000, basis);
            AddNVarChar(cmd, "@ChangedBy", 150, changedBy);

            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
            {
                return (true, "Promotion completed.");
            }

            var resultDetailNumber = r.GetStringOrEmpty(1);
            var fromConfidence = r.GetStringOrEmpty(2);
            var resultToConfidence = r.GetStringOrEmpty(3);
            var changed = !r.IsDBNull(4) && r.GetBoolean(4);
            return (true, changed
                ? $"{resultDetailNumber} promoted from {fromConfidence} to {resultToConfidence}."
                : $"{resultDetailNumber} already {resultToConfidence}; no change needed.");
        }
        catch (SqlException ex)
        {
            return (false, ex.Message);
        }
    }

    private static void AddNVarChar(SqlCommand cmd, string name, int size, string value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.NVarChar, size);
        p.Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }
}
