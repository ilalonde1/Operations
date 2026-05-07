#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Options;
using Kor.Operations.Data;
using Kor.Operations.Financials;

namespace Kor.Operations.App.Crm;

internal sealed class DeltekLookupService : IDeltekLookupService
{
    private readonly VpOdbcDsnFactory _factory;
    private readonly DeltekOdbcOptions _odbcOptions;

    public DeltekLookupService(VpOdbcDsnFactory factory, DeltekOdbcOptions odbcOptions)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _odbcOptions = odbcOptions ?? throw new ArgumentNullException(nameof(odbcOptions));
    }

    public Task<IReadOnlyList<DeltekClientCandidate>> FindClientByNameAsync(
        string companyName, int max, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(companyName))
        {
            return Task.FromResult<IReadOnlyList<DeltekClientCandidate>>(Array.Empty<DeltekClientCandidate>());
        }

        return Task.Run<IReadOnlyList<DeltekClientCandidate>>(
            () => FindClientSync(companyName.Trim(), Math.Clamp(max, 1, 100), ct), ct);
    }

    public Task<IReadOnlyList<DeltekContactCandidate>> FindContactAsync(
        string? fullName,
        string? email,
        string? clientIdScope,
        int max,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fullName) && string.IsNullOrWhiteSpace(email))
        {
            return Task.FromResult<IReadOnlyList<DeltekContactCandidate>>(Array.Empty<DeltekContactCandidate>());
        }

        return Task.Run<IReadOnlyList<DeltekContactCandidate>>(
            () => FindContactSync(fullName, email, clientIdScope, Math.Clamp(max, 1, 100), ct), ct);
    }

    private IReadOnlyList<DeltekClientCandidate> FindClientSync(string raw, int max, CancellationToken ct)
    {
        var catalog = GetCatalog();
        var normalizedQuery = NormalizeCompany(raw);
        if (normalizedQuery.Length == 0)
        {
            return Array.Empty<DeltekClientCandidate>();
        }

        // Cheap prefix LIKE filter on the DB side; full Levenshtein in C#.
        var prefix = normalizedQuery.Substring(0, Math.Min(3, normalizedQuery.Length)) + "%";

        using var cn = _factory.Create();
        try { cn.Open(); }
        catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (DeltekLookup).", ex); }

        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 15;
        cmd.CommandText = $@"
SELECT ClientID, Name, Type
FROM [{catalog}].dbo.Clendor
WHERE ClientInd = 'Y'
  AND (Status IS NULL OR Status <> 'I')
  AND LOWER(Name) LIKE ?";
        cmd.Parameters.Add(new OdbcParameter("@p", OdbcType.NVarChar, 100) { Value = prefix });
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();

        var rows = new List<DeltekClientCandidate>(64);
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            var clientId = DataReaderHelpers.GetTrimmed(r, 0);
            var name = DataReaderHelpers.GetTrimmed(r, 1);
            var type = r.IsDBNull(2) ? null : Convert.ToString(r.GetValue(2))?.Trim();
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var similarity = Similarity(normalizedQuery, NormalizeCompany(name));
            if (similarity >= 0.5)
            {
                rows.Add(new DeltekClientCandidate(clientId, name, type, similarity));
            }
        }

        return rows
            .OrderByDescending(c => c.SimilarityScore)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }

    private IReadOnlyList<DeltekContactCandidate> FindContactSync(
        string? fullName,
        string? email,
        string? clientIdScope,
        int max,
        CancellationToken ct)
    {
        var catalog = GetCatalog();
        using var cn = _factory.Create();
        try { cn.Open(); }
        catch (OdbcException ex) { throw new InvalidOperationException("ODBC connection failed (DeltekLookup).", ex); }

        var trimmedScope = string.IsNullOrWhiteSpace(clientIdScope) ? null : clientIdScope.Trim();
        var trimmedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(trimmedEmail))
        {
            var emailRows = FindContactsByEmail(cn, catalog, trimmedEmail, trimmedScope, max, ct);
            if (emailRows.Count > 0)
            {
                return emailRows;
            }
        }

        var normalizedName = NormalizePersonName(fullName ?? string.Empty);
        if (normalizedName.Length == 0)
        {
            return Array.Empty<DeltekContactCandidate>();
        }

        return FindContactsByName(cn, catalog, normalizedName, trimmedScope, max, ct);
    }

    private static IReadOnlyList<DeltekContactCandidate> FindContactsByEmail(
        OdbcConnection cn,
        string catalog,
        string email,
        string? clientIdScope,
        int max,
        CancellationToken ct)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 15;
        cmd.CommandText = $@"
SELECT TOP {max} c.ContactID, c.ClientID, c.FirstName, c.LastName, c.EMail, c.Title
FROM [{catalog}].dbo.Contacts c
WHERE {(clientIdScope is null ? string.Empty : "c.ClientID = ? AND ")}LOWER(c.EMail) = ?
  AND (c.ContactStatus IS NULL OR c.ContactStatus IN ('A', 'Active'))
ORDER BY c.LastName, c.FirstName";
        if (clientIdScope is not null)
        {
            cmd.Parameters.Add(new OdbcParameter("@client", OdbcType.NVarChar, 32) { Value = clientIdScope });
        }

        cmd.Parameters.Add(new OdbcParameter("@email", OdbcType.NVarChar, 255) { Value = email });
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        return ReadContactCandidates(r, _ => 1.0, ct);
    }

    private static IReadOnlyList<DeltekContactCandidate> FindContactsByName(
        OdbcConnection cn,
        string catalog,
        string normalizedName,
        string? clientIdScope,
        int max,
        CancellationToken ct)
    {
        var prefix = normalizedName.Substring(0, Math.Min(3, normalizedName.Length)) + "%";
        var sqlTop = Math.Max(max * 10, max);
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 15;
        cmd.CommandText = $@"
SELECT TOP {sqlTop} c.ContactID, c.ClientID, c.FirstName, c.LastName, c.EMail, c.Title
FROM [{catalog}].dbo.Contacts c
WHERE {(clientIdScope is null ? string.Empty : "c.ClientID = ? AND ")}LOWER(COALESCE(c.FirstName, '') + ' ' + COALESCE(c.LastName, '')) LIKE ?
  AND (c.ContactStatus IS NULL OR c.ContactStatus IN ('A', 'Active'))";
        if (clientIdScope is not null)
        {
            cmd.Parameters.Add(new OdbcParameter("@client", OdbcType.NVarChar, 32) { Value = clientIdScope });
        }

        cmd.Parameters.Add(new OdbcParameter("@name", OdbcType.NVarChar, 120) { Value = prefix });
        using var reg = ct.Register(() => { try { cmd.Cancel(); } catch { } });
        using var r = cmd.ExecuteReader();
        return ReadContactCandidates(r, name => Similarity(normalizedName, NormalizePersonName(name)), ct)
            .Where(c => c.SimilarityScore >= 0.5)
            .OrderByDescending(c => c.SimilarityScore)
            .ThenBy(c => c.FullName, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }

    private static IReadOnlyList<DeltekContactCandidate> ReadContactCandidates(
        OdbcDataReader r,
        Func<string, double> score,
        CancellationToken ct)
    {
        var rows = new List<DeltekContactCandidate>();
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            var contactId = DataReaderHelpers.GetTrimmed(r, 0);
            var clientId = DataReaderHelpers.GetTrimmed(r, 1);
            var firstName = DataReaderHelpers.GetTrimmed(r, 2);
            var lastName = DataReaderHelpers.GetTrimmed(r, 3);
            if (string.IsNullOrWhiteSpace(contactId) || string.IsNullOrWhiteSpace(clientId))
            {
                continue;
            }

            var fullName = string.Join(" ", new[] { firstName, lastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            rows.Add(new DeltekContactCandidate(
                contactId,
                clientId,
                fullName,
                r.IsDBNull(4) ? null : Convert.ToString(r.GetValue(4))?.Trim(),
                r.IsDBNull(5) ? null : Convert.ToString(r.GetValue(5))?.Trim(),
                score(fullName)));
        }

        return rows;
    }

    /// <summary>
    /// Lowercase, trim, collapse internal whitespace, strip common company
    /// suffix tokens. e.g.:
    ///   "Concord Pacific Inc." -> "concord pacific"
    ///   "RBI Group, LLC" -> "rbi"
    /// </summary>
    internal static string NormalizeCompany(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var lower = s.ToLowerInvariant();
        var sb = new StringBuilder(lower.Length);
        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch) || ch == ' ')
            {
                sb.Append(ch);
            }
            else if (ch == ',' || ch == '.' || ch == '-' || ch == '/' || ch == '&')
            {
                sb.Append(' ');
            }
        }

        var tokens = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var kept = new List<string>(tokens.Length);
        foreach (var t in tokens)
        {
            if (CompanySuffixTokens.Contains(t)) continue;
            kept.Add(t);
        }

        return string.Join(' ', kept);
    }

    /// <summary>
    /// Similarity in [0,1]. 1.0 == exact match. Uses Levenshtein distance
    /// normalized by max length. Empty inputs return 0. Identical inputs
    /// return 1.
    /// </summary>
    internal static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0.0;
        if (a == b) return 1.0;
        var distance = Levenshtein(a, b);
        var maxLen = Math.Max(a.Length, b.Length);
        return 1.0 - ((double)distance / maxLen);
    }

    private string GetCatalog() => DeltekCatalogValidator.ResolveCatalog(_odbcOptions.Catalog);

    private static string NormalizePersonName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var lower = s.ToLowerInvariant();
        var sb = new StringBuilder(lower.Length);
        foreach (var ch in lower)
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch == ' ' ? ch : ' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static readonly HashSet<string> CompanySuffixTokens = new(StringComparer.Ordinal)
    {
        "inc", "incorporated", "ltd", "limited", "llc", "llp", "lp",
        "corp", "corporation", "co", "company",
        "group", "holdings", "international", "intl",
        "properties", "property",
        "construction", "constructors",
        "development", "developments", "developers", "dev",
        "consulting", "consultants",
        "architects", "architecture",
        "engineering", "engineers"
    };

    /// <summary>Iterative two-row Levenshtein. O(min(a,b)) memory.</summary>
    private static int Levenshtein(string a, string b)
    {
        if (a.Length < b.Length) (a, b) = (b, a);
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }

            (prev, cur) = (cur, prev);
        }

        return prev[b.Length];
    }
}
