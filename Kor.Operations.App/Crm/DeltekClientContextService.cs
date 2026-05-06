#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Odbc;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Options;
using Kor.Operations.Data;
using Kor.Operations.Financials;

namespace Kor.Operations.App.Crm;

/// <summary>
/// Rich roll-up of a Deltek client's history with KOR - used by the CRM
/// engagement detail panel and the AI context. Pulled from Deltek over ODBC
/// using the same DSN/catalog as <c>FinancialsService</c>.
/// </summary>
public sealed record DeltekClientIntelligence
{
    // Original 6 fields - preserved verbatim for binding compatibility.
    public string ClientId { get; init; } = "";
    public string ClientName { get; init; } = "";
    public int ProjectCount { get; init; }
    public decimal LifetimeFee { get; init; }
    public DateTime? LatestProjectStart { get; init; }
    public string? LatestProjectName { get; init; }

    // New: company-level facts pulled from Clendor.
    public DeltekCompanyFacts? Company { get; init; }

    // New: top 50 KOR projects with this client, OpenDate DESC.
    public IReadOnlyList<DeltekProjectSummary> Projects { get; init; } = Array.Empty<DeltekProjectSummary>();

    // New: top 50 contacts at this client.
    public IReadOnlyList<DeltekContactSummary> Contacts { get; init; } = Array.Empty<DeltekContactSummary>();

    // New: AR rollup - total outstanding + 90+ aging.
    public DeltekArSummary? Ar { get; init; }

    // New: top 10 Deltek Activity rows tied to this client, StartDate DESC.
    public IReadOnlyList<DeltekActivitySummary> RecentActivity { get; init; } = Array.Empty<DeltekActivitySummary>();

    // True when one of the section queries hit a permission error
    // (table inaccessible). The panel can show a soft warning instead
    // of pretending the absence is "no data".
    public bool HasDegradedSections { get; init; }
}

public sealed record DeltekCompanyFacts(
    string ClientId,
    string? Type,
    string? Status,
    string? Specialty,
    string? Market,
    string? Memo,
    string? ParentId,
    string? Website,
    bool PriorWork,
    bool Recommend,
    bool GovernmentAgency,
    bool Competitor,
    int? Employees,
    decimal? AnnualRevenue);

public sealed record DeltekProjectSummary(
    string Wbs1,
    string Name,
    DateTime? OpenDate,
    string? Status,
    decimal Fee,
    decimal FeeBilled);

public sealed record DeltekContactSummary(
    string ContactId,
    string FirstName,
    string LastName,
    string? Title,
    string? Email,
    string? Phone,
    string? CellPhone,
    bool IsPrimary,
    string? Rating);

public sealed record DeltekArSummary(
    decimal TotalOutstanding,
    decimal Outstanding90Plus,
    int OpenInvoiceCount);

public sealed record DeltekActivitySummary(
    string ActivityId,
    string? Type,
    string? Subject,
    DateTime? StartDate,
    string? Employee,
    string? Wbs1);

public interface IDeltekClientContextService
{
    /// <summary>Returns null if the client id is blank or no projects link to it.</summary>
    Task<DeltekClientIntelligence?> LoadAsync(string? deltekClientId, CancellationToken ct);
}

internal sealed class DeltekClientContextService : IDeltekClientContextService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan NullCacheTtl = TimeSpan.FromSeconds(60);
    private const int MaxCacheEntries = 200;

    private readonly VpOdbcDsnFactory _factory;
    private readonly DeltekOdbcOptions _odbcOptions;
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, (DeltekClientIntelligence? Value, DateTime ExpiresUtc, DateTime InsertedUtc)> _cache
        = new(StringComparer.OrdinalIgnoreCase);

    public DeltekClientContextService(VpOdbcDsnFactory factory, DeltekOdbcOptions odbcOptions)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _odbcOptions = odbcOptions ?? throw new ArgumentNullException(nameof(odbcOptions));
    }

    public Task<DeltekClientIntelligence?> LoadAsync(string? deltekClientId, CancellationToken ct)
    {
        var trimmed = deltekClientId?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Task.FromResult<DeltekClientIntelligence?>(null);
        }

        lock (_cacheGate)
        {
            if (_cache.TryGetValue(trimmed, out var hit) && DateTime.UtcNow < hit.ExpiresUtc)
            {
                return Task.FromResult(hit.Value);
            }
        }

        // Run the ODBC roll-up on a thread-pool thread - VpOdbcDsnFactory.Create()
        // returns an OdbcConnection which is sync-only. Pattern matches the rest
        // of FinancialsService (sync work wrapped in Task.Run for the UI).
        return Task.Run<DeltekClientIntelligence?>(() =>
        {
            var loaded = LoadSync(trimmed, ct);
            lock (_cacheGate)
            {
                var now = DateTime.UtcNow;
                _cache[trimmed] = (loaded, now + (loaded is null ? NullCacheTtl : CacheTtl), now);
                EvictOldestEntriesIfNeeded();
            }

            return loaded;
        }, ct);
    }

    private void EvictOldestEntriesIfNeeded()
    {
        if (_cache.Count <= MaxCacheEntries)
            return;

        var removeCount = Math.Max(1, MaxCacheEntries / 4);
        foreach (var key in _cache
                     .OrderBy(kvp => kvp.Value.InsertedUtc)
                     .Take(removeCount)
                     .Select(kvp => kvp.Key)
                     .ToList())
        {
            _cache.Remove(key);
        }
    }

    private DeltekClientIntelligence? LoadSync(string clientId, CancellationToken ct)
    {
        var catalog = DeltekCatalogValidator.ResolveCatalog(_odbcOptions.Catalog);

        using var cn = _factory.Create();
        try { cn.Open(); }
        catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (DeltekClientIntelligence).", ex); }

        var company = LoadCompanyFacts(cn, catalog, clientId, ct, out var clientName);
        if (company is null)
        {
            return null;
        }

        var (projectCount, lifetimeFee, latestStart) = LoadProjectAggregate(cn, catalog, clientId, ct);

        var degraded = false;
        var projects = Array.Empty<DeltekProjectSummary>() as IReadOnlyList<DeltekProjectSummary>;
        var contacts = Array.Empty<DeltekContactSummary>() as IReadOnlyList<DeltekContactSummary>;
        DeltekArSummary? ar = null;
        var recentActivity = Array.Empty<DeltekActivitySummary>() as IReadOnlyList<DeltekActivitySummary>;

        try
        {
            projects = LoadProjects(cn, catalog, clientId, ct);
        }
        catch (OdbcException ex)
        {
            degraded = true;
            Debug.WriteLine($"[DeltekIntelligence] section projects unavailable: {ex.Message}");
        }

        try
        {
            contacts = LoadContacts(cn, catalog, clientId, ct);
        }
        catch (OdbcException ex)
        {
            degraded = true;
            Debug.WriteLine($"[DeltekIntelligence] section contacts unavailable: {ex.Message}");
        }

        try
        {
            ar = LoadArSummary(cn, catalog, clientId, ct);
        }
        catch (OdbcException ex)
        {
            degraded = true;
            Debug.WriteLine($"[DeltekIntelligence] section ar unavailable: {ex.Message}");
        }

        try
        {
            recentActivity = LoadRecentActivity(cn, catalog, clientId, ct);
        }
        catch (OdbcException ex)
        {
            degraded = true;
            Debug.WriteLine($"[DeltekIntelligence] section activity unavailable: {ex.Message}");
        }

        return new DeltekClientIntelligence
        {
            ClientId = clientId,
            ClientName = clientName,
            ProjectCount = projectCount,
            LifetimeFee = lifetimeFee,
            LatestProjectStart = latestStart,
            LatestProjectName = projects.FirstOrDefault(p => latestStart.HasValue && p.OpenDate.HasValue && p.OpenDate.Value == latestStart.Value)?.Name,
            Company = company,
            Projects = projects,
            Contacts = contacts,
            Ar = ar,
            RecentActivity = recentActivity,
            HasDegradedSections = degraded,
        };
    }

    private static DeltekCompanyFacts? LoadCompanyFacts(
        OdbcConnection cn,
        string catalog,
        string clientId,
        CancellationToken ct,
        out string clientName)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = $@"
SELECT ClientID, Name, Type, Status, Specialty, Market, Memo,
       ParentID, WebSite, PriorWork, Recommend, GovernmentAgency,
       Competitor, Employees, AnnualRevenue
FROM [{catalog}].dbo.Clendor
WHERE ClientID = ?";
        cmd.Parameters.Add(new OdbcParameter("@id", OdbcType.NVarChar, 32) { Value = clientId });
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        try
        {
            using var r = cmd.ExecuteReader();
            if (!r.Read())
            {
                clientName = string.Empty;
                return null;
            }

            clientName = GetString(r, 1) ?? clientId;
            return new DeltekCompanyFacts(
                ClientId: GetString(r, 0) ?? clientId,
                Type: GetString(r, 2),
                Status: GetString(r, 3),
                Specialty: GetString(r, 4),
                Market: GetString(r, 5),
                Memo: GetString(r, 6),
                ParentId: GetString(r, 7),
                Website: GetString(r, 8),
                PriorWork: IsYes(r, 9),
                Recommend: IsYes(r, 10),
                GovernmentAgency: IsYes(r, 11),
                Competitor: IsYes(r, 12),
                Employees: GetInt(r, 13),
                AnnualRevenue: GetDecimal(r, 14));
        }
        catch (OdbcException ex)
        {
            throw new InvalidOperationException("ODBC query failed (Deltek client company facts).", ex);
        }
    }

    private static (int ProjectCount, decimal LifetimeFee, DateTime? LatestStart) LoadProjectAggregate(
        OdbcConnection cn,
        string catalog,
        string clientId,
        CancellationToken ct)
    {
        using var cmd = cn.CreateCommand();
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
            return (0, 0m, null);
        }

        return (
            ProjectCount: r.IsDBNull(0) ? 0 : Convert.ToInt32(r.GetValue(0)),
            LifetimeFee: GetDecimal(r, 1) ?? 0m,
            LatestStart: GetDate(r, 2));
    }

    private static IReadOnlyList<DeltekProjectSummary> LoadProjects(
        OdbcConnection cn,
        string catalog,
        string clientId,
        CancellationToken ct)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = $@"
WITH ClientWbs AS (
    SELECT DISTINCT ar.WBS1
    FROM [{catalog}].dbo.AR ar
    WHERE ar.ClientID = ?
      AND LTRIM(RTRIM(ISNULL(ar.WBS1, ''))) <> ''
),
ProjectBilling AS (
    SELECT sm.WBS1,
           SUM(CASE WHEN sm.BilledFee <> 0 THEN sm.BilledFee ELSE COALESCE(sm.Revenue, 0) END) AS FeeBilled
    FROM [{catalog}].dbo.PRSummaryMain sm
    WHERE sm.WBS2 IS NULL OR LTRIM(RTRIM(sm.WBS2)) = ''
    GROUP BY sm.WBS1
)
SELECT TOP 50 pr.WBS1, pr.Name, pr.OpenDate, pr.Status, pr.Fee,
       COALESCE(pb.FeeBilled, 0) AS FeeBilled
FROM ClientWbs cw
INNER JOIN [{catalog}].dbo.PR pr ON pr.WBS1 = cw.WBS1
LEFT JOIN ProjectBilling pb ON pb.WBS1 = pr.WBS1
WHERE pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = ''
ORDER BY pr.OpenDate DESC;";
        cmd.Parameters.Add(new OdbcParameter("@id", OdbcType.NVarChar, 32) { Value = clientId });
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        var rows = new List<DeltekProjectSummary>();
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            rows.Add(new DeltekProjectSummary(
                Wbs1: GetString(r, 0) ?? "",
                Name: GetString(r, 1) ?? "",
                OpenDate: GetDate(r, 2),
                Status: GetString(r, 3),
                Fee: GetDecimal(r, 4) ?? 0m,
                FeeBilled: GetDecimal(r, 5) ?? 0m));
        }

        return rows;
    }

    private static IReadOnlyList<DeltekContactSummary> LoadContacts(
        OdbcConnection cn,
        string catalog,
        string clientId,
        CancellationToken ct)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = $@"
SELECT TOP 50 ContactID, FirstName, LastName, Title, EMail, Phone,
       CellPhone, PrimaryInd, Rating
FROM [{catalog}].dbo.Contacts
WHERE ClientID = ?
  AND (ContactStatus IS NULL OR ContactStatus IN ('A', 'Active'))
ORDER BY PrimaryInd DESC, LastName, FirstName;";
        cmd.Parameters.Add(new OdbcParameter("@id", OdbcType.NVarChar, 32) { Value = clientId });
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        var rows = new List<DeltekContactSummary>();
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            rows.Add(new DeltekContactSummary(
                ContactId: GetString(r, 0) ?? "",
                FirstName: GetString(r, 1) ?? "",
                LastName: GetString(r, 2) ?? "",
                Title: GetString(r, 3),
                Email: GetString(r, 4),
                Phone: GetString(r, 5),
                CellPhone: GetString(r, 6),
                IsPrimary: IsYes(r, 7),
                Rating: GetString(r, 8)));
        }

        return rows;
    }

    private static DeltekArSummary? LoadArSummary(
        OdbcConnection cn,
        string catalog,
        string clientId,
        CancellationToken ct)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = $@"
SELECT
    SUM(InvBalanceSourceCurrency) AS TotalOutstanding,
    SUM(CASE WHEN DATEDIFF(day, COALESCE(DueDate, InvoiceDate), CAST(GETDATE() AS date)) > 90
             THEN InvBalanceSourceCurrency ELSE 0 END) AS Outstanding90Plus,
    COUNT(*) AS OpenInvoiceCount
FROM [{catalog}].dbo.AR
WHERE ClientID = ?
  AND COALESCE(InvBalanceSourceCurrency, 0) > 0;";
        cmd.Parameters.Add(new OdbcParameter("@id", OdbcType.NVarChar, 32) { Value = clientId });
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        if (!r.Read())
        {
            return null;
        }

        return new DeltekArSummary(
            TotalOutstanding: GetDecimal(r, 0) ?? 0m,
            Outstanding90Plus: GetDecimal(r, 1) ?? 0m,
            OpenInvoiceCount: r.IsDBNull(2) ? 0 : Convert.ToInt32(r.GetValue(2)));
    }

    private static IReadOnlyList<DeltekActivitySummary> LoadRecentActivity(
        OdbcConnection cn,
        string catalog,
        string clientId,
        CancellationToken ct)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = $@"
SELECT TOP 10 ActivityID, Type, Subject, StartDate, Employee, WBS1
FROM [{catalog}].dbo.Activity
WHERE ClientID = ?
ORDER BY StartDate DESC, CreateDate DESC;";
        cmd.Parameters.Add(new OdbcParameter("@id", OdbcType.NVarChar, 32) { Value = clientId });
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        var rows = new List<DeltekActivitySummary>();
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            rows.Add(new DeltekActivitySummary(
                ActivityId: GetString(r, 0) ?? "",
                Type: GetString(r, 1),
                Subject: GetString(r, 2),
                StartDate: GetDate(r, 3),
                Employee: GetString(r, 4),
                Wbs1: GetString(r, 5)));
        }

        return rows;
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

    private static decimal? GetDecimal(IDataRecord r, int i)
    {
        if (r.IsDBNull(i)) return null;
        return Convert.ToDecimal(r.GetValue(i));
    }

    private static int? GetInt(IDataRecord r, int i)
    {
        if (r.IsDBNull(i)) return null;
        return Convert.ToInt32(r.GetValue(i));
    }

    private static bool IsYes(IDataRecord r, int i)
    {
        var value = GetString(r, i);
        return string.Equals(value, "Y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "YES", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase)
            || value == "1";
    }
}
