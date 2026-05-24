#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Odbc;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Deltek;

namespace Kor.Operations.Data.Deltek;

public sealed class DeltekKorPursuitDeltekAccessor : IKorPursuitDeltekAccessor
{
    private const string DefaultCatalog = "C0000052267P_1_KOR00000000";
    private static readonly Regex CatalogNamePattern = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly VpOdbcDsnFactory _factory;
    private readonly string _catalog;

    public DeltekKorPursuitDeltekAccessor(VpOdbcDsnFactory factory, string? catalog)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _catalog = ResolveCatalog(catalog);
    }

    public Task<IReadOnlyList<DeltekPursuitRow>> GetExplicitStagePursuitsAsync(CancellationToken ct)
        => QueryAsync("pr.Stage IN ('InPursuit', 'LOST', 'DNP')", ct);

    public Task<IReadOnlyList<DeltekPursuitRow>> GetPromotionalPursuitsAsync(CancellationToken ct)
        => QueryAsync("pr.ChargeType = 'P'", ct);

    private Task<IReadOnlyList<DeltekPursuitRow>> QueryAsync(string predicate, CancellationToken ct)
    {
        return Task.Run<IReadOnlyList<DeltekPursuitRow>>(() =>
        {
            using var cn = _factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (KOR pursuit sync).", ex); }

            var prColumns = LoadColumnSet(cn, "PR", ct);
            var lostToValue = OptionalTextValue(prColumns, "LostTo", 32);
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = 120;
            cmd.CommandText = $@"
SELECT
    pr.WBS1,
    pr.Name,
    pr.ClientID,
    cl.Name AS BuyerName,
    pr.Stage,
    pr.ChargeType,
    {OptionalText(prColumns, "LostTo", "LostTo", 32)},
    lost.Name AS LostToName,
    pr.OpenDate,
    {OptionalDate(prColumns, "ProposalDueDate")},
    {OptionalDate(prColumns, "AwardDate")},
    pr.ProjMgr,
    pr.Principal,
    {OptionalText(prColumns, "BusinessDeveloper", "BusinessDeveloper", 100)},
    {OptionalText(prColumns, "ProposalManager", "ProposalManager", 100)}
FROM   [{_catalog}].dbo.PR pr
LEFT JOIN [{_catalog}].dbo.Clendor cl ON cl.ClientID = pr.ClientID
LEFT JOIN [{_catalog}].dbo.Clendor lost ON lost.ClientID = {lostToValue}
WHERE  (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
  AND  (pr.WBS3 IS NULL OR LTRIM(RTRIM(pr.WBS3)) = '')
  AND  {predicate}
ORDER BY pr.OpenDate DESC, pr.WBS1;";

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { /* best-effort cancel; may race with disposal */ } });
            using var r = cmd.ExecuteReader();
            var rows = new List<DeltekPursuitRow>();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var wbs1 = GetString(r, 0);
                if (string.IsNullOrWhiteSpace(wbs1))
                {
                    continue;
                }

                rows.Add(new DeltekPursuitRow(
                    Wbs1: wbs1,
                    Name: GetString(r, 1) ?? "(untitled pursuit)",
                    ClientID: GetString(r, 2),
                    BuyerName: GetString(r, 3),
                    Stage: GetString(r, 4) ?? string.Empty,
                    ChargeType: GetString(r, 5) ?? string.Empty,
                    LostTo: GetString(r, 6),
                    LostToName: GetString(r, 7),
                    OpenDate: GetDate(r, 8),
                    ProposalDueDate: GetDate(r, 9),
                    AwardDate: GetDate(r, 10),
                    ProjMgr: GetString(r, 11),
                    Principal: GetString(r, 12),
                    BusinessDeveloper: GetString(r, 13),
                    ProposalManager: GetString(r, 14)));
            }

            return rows;
        }, ct);
    }

    private HashSet<string> LoadColumnSet(OdbcConnection cn, string tableName, CancellationToken ct)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = $@"
SELECT COLUMN_NAME
FROM [{_catalog}].INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = ?";
        cmd.Parameters.Add(new OdbcParameter("@table", OdbcType.NVarChar, 128) { Value = tableName });
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { /* best-effort cancel; may race with disposal */ } });
        using var r = cmd.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            var name = GetString(r, 0);
            if (!string.IsNullOrWhiteSpace(name))
            {
                columns.Add(name);
            }
        }

        return columns;
    }

    private static string OptionalDate(HashSet<string> columns, string column)
        => columns.Contains(column)
            ? $"pr.{column}"
            : $"CAST(NULL AS datetime) AS {column}";

    private static string OptionalText(HashSet<string> columns, string column, string alias, int length)
        => columns.Contains(column)
            ? $"pr.{column}"
            : $"CAST(NULL AS varchar({length})) AS {alias}";

    private static string OptionalTextValue(HashSet<string> columns, string column, int length)
        => columns.Contains(column)
            ? $"pr.{column}"
            : $"CAST(NULL AS varchar({length}))";

    private static string ResolveCatalog(string? catalog)
    {
        var value = string.IsNullOrWhiteSpace(catalog) ? DefaultCatalog : catalog.Trim();
        if (!CatalogNamePattern.IsMatch(value))
        {
            throw new InvalidOperationException("Deltek catalog name contains unsupported characters.");
        }

        return value;
    }

    private static string? GetString(IDataRecord r, int i)
    {
        if (r.IsDBNull(i)) return null;
        return Convert.ToString(r.GetValue(i))?.Trim();
    }

    private static DateTime? GetDate(IDataRecord r, int i)
    {
        if (r.IsDBNull(i)) return null;
        return Convert.ToDateTime(r.GetValue(i));
    }
}
