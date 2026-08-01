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
/// Live Deltek read-through for won KOR project history. Deltek remains the
/// system of record; callers cache or index only derived signals.
/// </summary>
public sealed class DeltekKorWonProjectAccessor : IKorWonProjectAccessor
{
    private const string DefaultCatalog = "C0000052267P_1_KOR00000000";
    private static readonly Regex CatalogNamePattern = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly VpOdbcDsnFactory _factory;
    private readonly string _catalog;

    public DeltekKorWonProjectAccessor(VpOdbcDsnFactory factory, string? catalog)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _catalog = ResolveCatalog(catalog);
    }

    public Task<IReadOnlyList<KorWonProjectRow>> GetForClientAsync(
        string clendorClientId,
        int maxRows,
        CancellationToken ct)
    {
        var clientId = (clendorClientId ?? string.Empty).Trim();
        if (clientId.Length == 0 || maxRows <= 0)
        {
            return Task.FromResult<IReadOnlyList<KorWonProjectRow>>(Array.Empty<KorWonProjectRow>());
        }

        var take = Math.Min(maxRows, 200);
        return Task.Run<IReadOnlyList<KorWonProjectRow>>(() =>
        {
            using var cn = _factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (KOR won projects).", ex); }

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = 30;
            cmd.CommandText = $@"
SELECT TOP ({take}) pr.WBS1, pr.Name, pr.ProjMgr, pr.Principal,
       pr.OpenDate AS StartDate, pr.EndDate, pr.Stage
FROM   [{_catalog}].dbo.PR pr
WHERE  pr.ChargeType = 'R'
  AND  (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
  AND  (pr.WBS3 IS NULL OR LTRIM(RTRIM(pr.WBS3)) = '')
  AND  pr.ClientID = ?
ORDER  BY pr.OpenDate DESC;";
            cmd.Parameters.Add(new OdbcParameter("@client", OdbcType.NVarChar, 32) { Value = clientId });
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { /* best-effort cancel; may race with disposal */ } });
            using var r = cmd.ExecuteReader();
            var rows = new List<KorWonProjectRow>();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                rows.Add(new KorWonProjectRow(
                    Wbs1: GetString(r, 0) ?? string.Empty,
                    Name: GetString(r, 1) ?? string.Empty,
                    ProjectManager: GetString(r, 2),
                    Principal: GetString(r, 3),
                    StartDate: GetDate(r, 4),
                    EndDate: GetDate(r, 5),
                    Stage: GetString(r, 6),
                    FeeBilled: null));
            }

            return rows;
        }, ct);
    }

    public Task<IReadOnlyList<KorWonProjectAggregate>> GetAllClientAggregatesAsync(CancellationToken ct)
    {
        return Task.Run<IReadOnlyList<KorWonProjectAggregate>>(() =>
        {
            using var cn = _factory.Create();
            try { cn.Open(); }
            catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (KOR won project aggregates).", ex); }

            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = 120;
            cmd.CommandText = $@"
SELECT pr.ClientID,
       MAX(pr.OpenDate) AS LastStartDate,
       COUNT(*)         AS ProjectCount
FROM   [{_catalog}].dbo.PR pr
WHERE  pr.ChargeType = 'R'
  AND  (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
  AND  (pr.WBS3 IS NULL OR LTRIM(RTRIM(pr.WBS3)) = '')
  AND  pr.ClientID IS NOT NULL AND LTRIM(RTRIM(pr.ClientID)) <> ''
GROUP  BY pr.ClientID;";
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { /* best-effort cancel; may race with disposal */ } });
            using var r = cmd.ExecuteReader();
            var rows = new List<KorWonProjectAggregate>();
            while (r.Read())
            {
                ct.ThrowIfCancellationRequested();
                var clientId = GetString(r, 0);
                var lastStartDate = GetDate(r, 1);
                if (string.IsNullOrWhiteSpace(clientId) || lastStartDate is null)
                {
                    continue;
                }

                rows.Add(new KorWonProjectAggregate(
                    ClendorClientId: clientId,
                    LastStartDate: lastStartDate.Value,
                    ProjectCount: r.IsDBNull(2) ? 0 : Convert.ToInt32(r.GetValue(2))));
            }

            return rows;
        }, ct);
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

    private static DateTime? GetDate(IDataRecord r, int i)
    {
        if (r.IsDBNull(i)) return null;
        return Convert.ToDateTime(r.GetValue(i));
    }
}
