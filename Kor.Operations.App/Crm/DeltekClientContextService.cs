#nullable enable
using System;
using System.Data;
using System.Data.Odbc;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Options;
using Kor.Operations.Data;

namespace Kor.Operations.App.Crm;

/// <summary>
/// One-row roll-up of a Deltek client's history with KOR — used by the CRM
/// engagement detail panel and the AI context. Pulled from Deltek over ODBC
/// using the same DSN/catalog as <c>FinancialsService</c>.
/// </summary>
public sealed record DeltekClientContext(
    string ClientId,
    string ClientName,
    int ProjectCount,
    decimal LifetimeFee,
    DateTime? LatestProjectStart,
    string? LatestProjectName);

public interface IDeltekClientContextService
{
    /// <summary>Returns null if the client id is blank or no projects link to it.</summary>
    Task<DeltekClientContext?> LoadAsync(string? deltekClientId, CancellationToken ct);
}

internal sealed class DeltekClientContextService : IDeltekClientContextService
{
    private readonly VpOdbcDsnFactory _factory;
    private readonly DeltekOdbcOptions _odbcOptions;

    public DeltekClientContextService(VpOdbcDsnFactory factory, DeltekOdbcOptions odbcOptions)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _odbcOptions = odbcOptions ?? throw new ArgumentNullException(nameof(odbcOptions));
    }

    public Task<DeltekClientContext?> LoadAsync(string? deltekClientId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deltekClientId))
        {
            return Task.FromResult<DeltekClientContext?>(null);
        }

        // Run the ODBC roll-up on a thread-pool thread — VpOdbcDsnFactory.Create()
        // returns an OdbcConnection which is sync-only. Pattern matches the rest
        // of FinancialsService (sync work wrapped in Task.Run for the UI).
        return Task.Run<DeltekClientContext?>(() => LoadSync(deltekClientId.Trim(), ct), ct);
    }

    private DeltekClientContext? LoadSync(string clientId, CancellationToken ct)
    {
        var catalog = string.IsNullOrWhiteSpace(_odbcOptions.Catalog)
            ? "C0000052267P_1_KOR00000000"
            : _odbcOptions.Catalog;

        using var cn = _factory.Create();
        try { cn.Open(); }
        catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (DeltekClientContext).", ex); }

        // Step 1: client display name (Clendor is the lookup table — verified
        // against FinancialsService.LoadClientLookupSync 2026-05-03).
        string clientName;
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandTimeout = 15;
            cmd.CommandText = $"SELECT Name FROM [{catalog}].dbo.Clendor WHERE ClientID = ?";
            cmd.Parameters.Add(new OdbcParameter("@id", OdbcType.NVarChar, 32) { Value = clientId });
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            var raw = cmd.ExecuteScalar();
            clientName = raw is string s ? s.Trim() : string.Empty;
        }

        if (string.IsNullOrEmpty(clientName))
        {
            // Unknown client id — surface the id itself rather than fail silently.
            clientName = clientId;
        }

        // Step 2: aggregate the projects linked to this client via AR (same
        // join FinancialsService uses to roll up clients). One round-trip.
        int projectCount;
        decimal lifetimeFee;
        DateTime? latestStart;
        string? latestName;
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandTimeout = 30;
            cmd.CommandText = $@"
WITH ClientWbs AS (
    SELECT DISTINCT ar.WBS1
    FROM [{catalog}].dbo.AR ar
    WHERE ar.ClientID = ?
      AND LTRIM(RTRIM(ISNULL(ar.WBS1, ''))) <> ''
)
SELECT
    COUNT(DISTINCT pr.WBS1)                                        AS ProjectCount,
    ISNULL(SUM(CASE WHEN (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
                    THEN pr.Fee END), 0)                            AS LifetimeFee,
    MAX(pr.OpenDate)                                                AS LatestStart
FROM ClientWbs cw
INNER JOIN [{catalog}].dbo.PR pr ON pr.WBS1 = cw.WBS1;";
            cmd.Parameters.Add(new OdbcParameter("@id", OdbcType.NVarChar, 32) { Value = clientId });
            using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
            using var r = cmd.ExecuteReader();
            if (!r.Read())
            {
                return new DeltekClientContext(clientId, clientName, 0, 0m, null, null);
            }

            projectCount = r.IsDBNull(0) ? 0 : Convert.ToInt32(r.GetValue(0));
            lifetimeFee  = r.IsDBNull(1) ? 0m : Convert.ToDecimal(r.GetValue(1));
            latestStart  = r.IsDBNull(2) ? (DateTime?)null : Convert.ToDateTime(r.GetValue(2));
        }

        if (projectCount == 0)
        {
            return new DeltekClientContext(clientId, clientName, 0, 0m, null, null);
        }

        // Step 3: name of the most-recent project (by OpenDate). Best-effort —
        // failure here just leaves LatestProjectName null.
        latestName = null;
        if (latestStart.HasValue)
        {
            try
            {
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = 15;
                cmd.CommandText = $@"
SELECT TOP 1 pr.Name
FROM [{catalog}].dbo.PR pr
INNER JOIN [{catalog}].dbo.AR ar ON ar.WBS1 = pr.WBS1
WHERE ar.ClientID = ?
  AND pr.OpenDate IS NOT NULL
  AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
ORDER BY pr.OpenDate DESC;";
                cmd.Parameters.Add(new OdbcParameter("@id", OdbcType.NVarChar, 32) { Value = clientId });
                using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
                var raw = cmd.ExecuteScalar();
                if (raw is string s && !string.IsNullOrWhiteSpace(s))
                {
                    latestName = s.Trim();
                }
            }
            catch (OdbcException)
            {
                // Best-effort — leave null.
            }
        }

        return new DeltekClientContext(
            ClientId: clientId,
            ClientName: clientName,
            ProjectCount: projectCount,
            LifetimeFee: lifetimeFee,
            LatestProjectStart: latestStart,
            LatestProjectName: latestName);
    }
}
