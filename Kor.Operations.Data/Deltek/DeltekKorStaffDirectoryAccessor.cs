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

/// <summary>
/// Reads active KOR employees (name + email) from Deltek EMMain over ODBC.
/// Mirrors DeltekKorPursuitDeltekAccessor: capability-detected columns, DSN
/// factory, best-effort cancel. Read-only.
/// </summary>
public sealed class DeltekKorStaffDirectoryAccessor : IKorStaffDirectoryAccessor
{
    private const string DefaultCatalog = "C0000052267P_1_KOR00000000";
    private static readonly Regex CatalogNamePattern = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly VpOdbcDsnFactory _factory;
    private readonly string _catalog;

    public DeltekKorStaffDirectoryAccessor(VpOdbcDsnFactory factory, string? catalog)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _catalog = ResolveCatalog(catalog);
    }

    public Task<IReadOnlyList<DeltekStaffRow>> GetActiveStaffAsync(CancellationToken ct)
    {
        return Task.Run<IReadOnlyList<DeltekStaffRow>>(() =>
        {
            using var cn = _factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (staff directory sync).", ex); }

            var cols = LoadColumnSet(cn, "EMMain", ct);
            // Only restrict on Status if the column exists; 'A' = Active in
            // Vantagepoint. If absent, take everyone with an email.
            var statusPredicate = cols.Contains("Status") ? "AND m.Status = 'A'" : string.Empty;

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = 60;
            cmd.CommandText = $@"
SELECT m.Employee, m.FirstName, m.LastName, m.EMail
FROM   [{_catalog}].dbo.EMMain m
WHERE  m.EMail IS NOT NULL AND LTRIM(RTRIM(m.EMail)) <> ''
  {statusPredicate}
ORDER BY m.LastName, m.FirstName;";

            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { /* best-effort */ } });
            using var r = cmd.ExecuteReader();
            var rows = new List<DeltekStaffRow>();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var email = GetString(r, 3);
                if (string.IsNullOrWhiteSpace(email)) continue;
                rows.Add(new DeltekStaffRow(
                    Employee: GetString(r, 0) ?? string.Empty,
                    FirstName: GetString(r, 1),
                    LastName: GetString(r, 2),
                    Email: email));
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
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { /* best-effort */ } });
        using var r = cmd.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            var name = GetString(r, 0);
            if (!string.IsNullOrWhiteSpace(name)) columns.Add(name);
        }

        return columns;
    }

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
}
