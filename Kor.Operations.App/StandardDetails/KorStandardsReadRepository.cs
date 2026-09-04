#nullable enable
#pragma warning disable SA1649
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Kor.Operations.Data;
using Microsoft.Data.SqlClient;

namespace Kor.Operations.StandardDetails;

internal sealed record PaletteDetailRow(string DetailNumber, string Title, string Discipline, string Kind, bool IsSheet, string ViewGroup, string Confidence, bool IsPlaceable, bool VariantsDiverge, int VariantCount);
internal sealed record SheetComposerDetailRow(string DetailNumber, string Title, string Discipline, string Kind, string CanonicalViewName);
internal sealed record ComponentRegisterRow(string Palette, string Label, string FamilyName, string TypeName, string Origin, bool IsRetired, int InstanceCount, int UsedInDetails);
// The Quick Insert catalog: the placeable, governed parts (family+type) production reads. Same
// confidence ladder as details, so a part is Approved/Pending exactly like a detail.
internal sealed record QuickInsertPartRow(string Palette, string Label, string FamilyName, string TypeName, string Confidence, bool IsPlaceable);
// (family, type, ImageFile) for syncing the Quick Insert thumbnails into the DB art store.
internal sealed record ComponentImageRef(string FamilyName, string TypeName, string ImageFile);

internal sealed class KorStandardsReadRepository
{
    private const int QueryMax = 2000;
    private readonly string _connectionString;

    internal KorStandardsReadRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    internal async Task<IReadOnlyList<PaletteDetailRow>> LoadPaletteDetailsAsync(string query, string? discipline = null, string? kind = null, bool? isSheet = null, bool orderByViewGroup = false)
    {
        const string sql = @"
SELECT DetailNumber,
       MIN(Title) AS Title,
       MIN(Discipline) AS Discipline,
       MIN(Kind) AS Kind,
       MAX(CAST(IsSheet AS int)) AS IsSheet,
       MIN(ViewGroup) AS ViewGroup,
       MIN(Confidence) AS Confidence,
       MAX(CAST(IsPlaceable AS int)) AS IsPlaceable,
       MAX(CAST(VariantsDiverge AS int)) AS VariantsDiverge,
       COUNT(*) AS VariantCount
FROM detail.vw_PaletteCatalog
WHERE (@q = '' OR DetailNumber LIKE @like OR Title LIKE @like)
  AND (@discipline IS NULL OR Discipline = @discipline)
  AND (@kind IS NULL OR Kind = @kind)
  AND (@isSheet IS NULL OR IsSheet = @isSheet)
GROUP BY DetailNumber
ORDER BY CASE WHEN @orderByViewGroup = 1 THEN MIN(ViewGroup) ELSE N'' END, DetailNumber;";

        var q = query?.Trim() ?? string.Empty;
        var rows = new List<PaletteDetailRow>();
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddNVarChar(cmd, "@q", QueryMax, q);
        AddNVarChar(cmd, "@like", QueryMax + 2, $"%{q}%");
        AddNullableNVarChar(cmd, "@discipline", 80, discipline);
        AddNullableNVarChar(cmd, "@kind", 16, kind);
        AddNullableBit(cmd, "@isSheet", isSheet);
        AddBit(cmd, "@orderByViewGroup", orderByViewGroup);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            rows.Add(new PaletteDetailRow(
                r.GetStringOrEmpty(0),
                r.GetStringOrEmpty(1),
                r.GetStringOrEmpty(2),
                r.GetStringOrEmpty(3),
                !r.IsDBNull(4) && r.GetInt32(4) == 1,
                r.GetStringOrEmpty(5),
                r.GetStringOrEmpty(6),
                !r.IsDBNull(7) && r.GetInt32(7) == 1,
                !r.IsDBNull(8) && r.GetInt32(8) == 1,
                r.IsDBNull(9) ? 0 : r.GetInt32(9)));
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

    internal async Task<IReadOnlySet<string>> LoadApprovedDetailNumbersAsync()
    {
        const string sql = @"
SELECT DISTINCT DetailNumber
FROM detail.vw_PaletteCatalog
WHERE IsPlaceable = 1
ORDER BY DetailNumber;";

        var rows = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.CommandTimeout = SqlTimeouts.UiFacing;

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var detailNumber = r.GetStringOrEmpty(0).Trim();
            if (!string.IsNullOrWhiteSpace(detailNumber))
            {
                rows.Add(detailNumber);
            }
        }

        return rows;
    }

    internal async Task<long?> GetCanonicalViewElementIdAsync(string detailNumber)
    {
        const string sql = @"
SELECT TOP 1 o.ViewElementId
FROM detail.DetailOccurrence o
JOIN detail.Detail d ON d.Id = o.DetailId
WHERE d.DetailNumber = @dn
  AND o.ViewElementId IS NOT NULL
ORDER BY CASE WHEN o.ViewKind = N'DraftingView' THEN 0 ELSE 1 END,
         o.ViewElementId;";

        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddNVarChar(cmd, "@dn", 64, detailNumber.Trim());
        var result = await cmd.ExecuteScalarAsync();
        return result is null || result is System.DBNull ? null : System.Convert.ToInt64(result);
    }

    internal async Task<IReadOnlyList<SheetComposerDetailRow>> LoadSheetComposerDetailsAsync(string query, string? discipline = null, string? kind = null)
    {
        const string sql = @"
SELECT DetailNumber,
       Title,
       Discipline,
       Kind,
       ViewName AS CanonicalViewName
FROM
(
    SELECT DetailNumber,
           Title,
           Discipline,
           Kind,
           ViewName,
           ROW_NUMBER() OVER (
               PARTITION BY DetailNumber
               ORDER BY
                   CASE WHEN ViewKind = N'DraftingView' THEN 0 ELSE 1 END,
                   CASE WHEN NULLIF(SizeToken, N'') IS NULL THEN 0 ELSE 1 END,
                   ViewName) AS rn
    FROM detail.vw_PaletteCatalog
    WHERE IsPlaceable = 1
      AND IsSheet = 0
      AND (@q = '' OR DetailNumber LIKE @like OR Title LIKE @like OR ViewName LIKE @like)
      AND (@discipline IS NULL OR Discipline = @discipline)
      AND (@kind IS NULL OR Kind = @kind)
) ranked
WHERE rn = 1
ORDER BY DetailNumber;";

        var q = query?.Trim() ?? string.Empty;
        var rows = new List<SheetComposerDetailRow>();
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddNVarChar(cmd, "@q", QueryMax, q);
        AddNVarChar(cmd, "@like", QueryMax + 2, $"%{q}%");
        AddNullableNVarChar(cmd, "@discipline", 80, discipline);
        AddNullableNVarChar(cmd, "@kind", 16, kind);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var detailNumber = r.GetStringOrEmpty(0).Trim();
            var viewName = r.GetStringOrEmpty(4).Trim();
            if (string.IsNullOrWhiteSpace(detailNumber) || string.IsNullOrWhiteSpace(viewName))
            {
                continue;
            }

            rows.Add(new SheetComposerDetailRow(
                detailNumber,
                r.GetStringOrEmpty(1),
                r.GetStringOrEmpty(2),
                r.GetStringOrEmpty(3),
                viewName));
        }

        return rows;
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

    // Parts = the Quick Insert catalog (placeable, governed). This is what production reads; it carries
    // the confidence ladder + IsPlaceable, so parts get the SAME Approved/Pending treatment as details.
    internal async Task<IReadOnlyList<QuickInsertPartRow>> LoadQuickInsertPartsAsync(string query)
    {
        const string sql = @"
SELECT Palette, Label, FamilyName, TypeName, Confidence, CAST(IsPlaceable AS int) AS IsPlaceable
FROM detail.vw_QuickInsertCatalog
WHERE (@q = '' OR Label LIKE @like OR FamilyName LIKE @like OR TypeName LIKE @like OR Palette LIKE @like)
ORDER BY Label, FamilyName, TypeName;";

        var q = query?.Trim() ?? string.Empty;
        var rows = new List<QuickInsertPartRow>();
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        AddNVarChar(cmd, "@q", QueryMax, q);
        AddNVarChar(cmd, "@like", QueryMax + 2, $"%{q}%");

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            rows.Add(new QuickInsertPartRow(
                r.GetStringOrEmpty(0),
                r.GetStringOrEmpty(1),
                r.GetStringOrEmpty(2),
                r.GetStringOrEmpty(3),
                r.GetStringOrEmpty(4),
                !r.IsDBNull(5) && r.GetInt32(5) == 1));
        }

        return rows;
    }

    // The art, straight from the governed store (detail.RenderedImage), keyed by identity — DetailNumber
    // for details, FamilyName|TypeName for parts. Returns null when there is no image yet, and degrades
    // to null (not a crash) if the store table isn't installed yet (migration 073 not run).
    internal async Task<byte[]?> LoadRenderedImageAsync(string entityKind, string entityKey)
    {
        const string sql = @"SELECT TOP 1 Png FROM detail.RenderedImage WHERE EntityKind = @kind AND EntityKey = @key;";
        try
        {
            await using var cn = new SqlConnection(_connectionString);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.CommandTimeout = SqlTimeouts.UiFacing;
            AddNVarChar(cmd, "@kind", 16, entityKind);
            AddNVarChar(cmd, "@key", 410, entityKey);
            var result = await cmd.ExecuteScalarAsync();
            return result as byte[];
        }
        catch (SqlException ex) when (ex.Number is 208 or 207)
        {
            // Store not installed yet — no art, but never a crash.
            return null;
        }
    }

    // Every component that carries a thumbnail reference, for the "Sync Part Images" tool. ImageFile is
    // either a rooted path (the fastener/bolt .png) or a bare name resolved against the QuickPick image root.
    internal async Task<IReadOnlyList<ComponentImageRef>> LoadComponentImageRefsAsync()
    {
        const string sql = @"
SELECT FamilyName, TypeName, ImageFile
FROM detail.vw_QuickInsertCatalog
WHERE NULLIF(LTRIM(ImageFile), '') IS NOT NULL
ORDER BY FamilyName, TypeName;";

        var rows = new List<ComponentImageRef>();
        await using var cn = new SqlConnection(_connectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.CommandTimeout = SqlTimeouts.UiFacing;
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            rows.Add(new ComponentImageRef(r.GetStringOrEmpty(0), r.GetStringOrEmpty(1), r.GetStringOrEmpty(2)));
        }

        return rows;
    }

    private static void AddNVarChar(SqlCommand cmd, string name, int size, string value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.NVarChar, size);
        p.Value = value;
    }

    private static void AddNullableNVarChar(SqlCommand cmd, string name, int size, string? value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.NVarChar, size);
        p.Value = string.IsNullOrWhiteSpace(value) ? System.DBNull.Value : value.Trim();
    }

    private static void AddNullableBit(SqlCommand cmd, string name, bool? value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.Bit);
        p.Value = value.HasValue ? (object)value.Value : System.DBNull.Value;
    }

    private static void AddBit(SqlCommand cmd, string name, bool value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.Bit);
        p.Value = value;
    }
}
