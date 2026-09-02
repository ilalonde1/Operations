#nullable enable
#pragma warning disable SA1649
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Kor.Operations.Data;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.StandardDetails;

internal sealed record PaletteDetailRow(string DetailNumber, string Title, string Discipline, string Confidence, bool IsPlaceable, bool VariantsDiverge, int VariantCount);
internal sealed record ComponentRegisterRow(string Palette, string Label, string FamilyName, string TypeName, string Origin, bool IsRetired, int InstanceCount, int UsedInDetails);

internal sealed class KorStandardsReadRepository
{
    private const int QueryMax = 2000;
    private readonly string _connectionString;

    internal KorStandardsReadRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    internal async Task<IReadOnlyList<PaletteDetailRow>> LoadPaletteDetailsAsync(string query)
    {
        const string sql = @"
SELECT DetailNumber,
       MIN(Title) AS Title,
       MIN(Discipline) AS Discipline,
       MIN(Confidence) AS Confidence,
       MAX(CAST(IsPlaceable AS int)) AS IsPlaceable,
       MAX(CAST(VariantsDiverge AS int)) AS VariantsDiverge,
       COUNT(*) AS VariantCount
FROM detail.vw_PaletteCatalog
WHERE (@q = '' OR DetailNumber LIKE @like OR Title LIKE @like)
GROUP BY DetailNumber
ORDER BY DetailNumber;";

        var q = query?.Trim() ?? string.Empty;
        var rows = new List<PaletteDetailRow>();
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddNVarChar(cmd, "@q", QueryMax, q);
        AddNVarChar(cmd, "@like", QueryMax + 2, $"%{q}%");

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            rows.Add(new PaletteDetailRow(
                r.GetStringOrEmpty(0),
                r.GetStringOrEmpty(1),
                r.GetStringOrEmpty(2),
                r.GetStringOrEmpty(3),
                !r.IsDBNull(4) && r.GetInt32(4) == 1,
                !r.IsDBNull(5) && r.GetInt32(5) == 1,
                r.IsDBNull(6) ? 0 : r.GetInt32(6)));
        }

        return rows;
    }

    internal async Task<int> CountDistinctDetailsAsync()
    {
        const string sql = @"
SELECT COUNT(DISTINCT DetailNumber)
FROM detail.vw_PaletteCatalog;";

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        return System.Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
    }

    internal async Task<IReadOnlyList<ComponentRegisterRow>> LoadComponentRegisterAsync(string query)
    {
        const string sql = @"
SELECT Palette, Label, FamilyName, TypeName, Origin, IsRetired, InstanceCount, UsedInDetails
FROM detail.vw_ComponentRegister
WHERE (@q = '' OR Label LIKE @like OR FamilyName LIKE @like OR TypeName LIKE @like OR Palette LIKE @like)
ORDER BY Palette, Label, FamilyName, TypeName;";

        var q = query?.Trim() ?? string.Empty;
        var rows = new List<ComponentRegisterRow>();
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddNVarChar(cmd, "@q", QueryMax, q);
        AddNVarChar(cmd, "@like", QueryMax + 2, $"%{q}%");

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            rows.Add(new ComponentRegisterRow(
                r.GetStringOrEmpty(0),
                r.GetStringOrEmpty(1),
                r.GetStringOrEmpty(2),
                r.GetStringOrEmpty(3),
                r.GetStringOrEmpty(4),
                !r.IsDBNull(5) && r.GetBoolean(5),
                r.IsDBNull(6) ? 0 : r.GetInt32(6),
                r.IsDBNull(7) ? 0 : r.GetInt32(7)));
        }

        return rows;
    }

    private static void AddNVarChar(SqlCommand cmd, string name, int size, string value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.NVarChar, size);
        p.Value = value;
    }
}
