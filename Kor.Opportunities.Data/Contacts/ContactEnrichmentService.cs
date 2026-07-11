#nullable enable
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Kor.Opportunities.Data.Awards;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Contacts;

/// <summary>
/// Shared decision-maker email enrichment — ONE implementation used by both the
/// BdContactEnrich CLI and the weekly ContactEnrichmentJob (no split-brain).
///
/// Two strategies, cheapest first:
///   PatternPropagate  — FREE. Derives each firm's email format from the clean emails
///                        already held (asis + Hunter), constructs the rest. No API.
///   HunterFinderPass  — PAID, hard credit-capped. Hunter email-finder (deterministic
///                        1 credit/person — NOT the per-email domain-search that
///                        overran the budget) for the highest-importance selectors that
///                        still lack an email. Stops at the credit cap.
///
/// Writes Email only where it is currently NULL, tagged EmailSource/EmailConfidence so
/// inferred (55) / Hunter (its score) / asis (80) stay distinguishable. Never overwrites,
/// never lets an LLM invent an address.
/// </summary>
public sealed class ContactEnrichmentService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly string _connectionString;

    public ContactEnrichmentService(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    public sealed record EnrichResult(int PatternFilled, int HunterFilled, int CreditsUsed);

    // ---- FREE: pattern propagation ----------------------------------------
    public async Task<int> PatternPropagateAsync(bool commit, CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        var firms = new List<(long Id, string? Website)>();
        await using (var cmd = new SqlCommand(@"
SELECT a.CanonicalOrgId, MAX(co.Website) AS Website
FROM opportunities.IntelPersonAffiliation a
JOIN opportunities.IntelPerson p ON p.Id = a.IntelPersonId AND p.RetiredAtUtc IS NULL
JOIN opportunities.CanonicalOrg co ON co.Id = a.CanonicalOrgId
WHERE a.RetiredAtUtc IS NULL
GROUP BY a.CanonicalOrgId
HAVING SUM(CASE WHEN NULLIF(LTRIM(RTRIM(p.Email)),'') IS NOT NULL THEN 1 ELSE 0 END) >= 1
   AND SUM(CASE WHEN NULLIF(LTRIM(RTRIM(p.Email)),'') IS NULL THEN 1 ELSE 0 END) >= 1;", con) { CommandTimeout = 120 })
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await r.ReadAsync(ct).ConfigureAwait(false)) firms.Add((r.GetInt64(0), r.IsDBNull(1) ? null : r.GetString(1)));

        int written = 0;
        foreach (var firm in firms)
        {
            ct.ThrowIfCancellationRequested();
            var (known, gaps) = await LoadFirmPeopleAsync(con, firm.Id, ct).ConfigureAwait(false);
            var derived = DerivePattern(known);
            if (derived is null) continue;
            var (build, domain) = derived.Value;
            // Domain guard: only propagate when the firm's known-email domain matches its OWN
            // website. Prevents inheriting a foreign domain from a mis-affiliated person
            // (e.g. an architect roster polluted with a builder's people on the builder's domain).
            var siteDomain = CanonicalOrgResolver.ExtractWebsiteDomain(firm.Website);
            if (string.IsNullOrWhiteSpace(siteDomain) || !string.Equals(siteDomain, domain, StringComparison.OrdinalIgnoreCase))
                continue;
            // Audit-v2 #12: intra-firm duplicate-local guard. Two people whose names
            // derive the same local part (J. Smith / Jane Smith -> jsmith@) must not
            // both receive the same guessed address — one email on two person rows
            // is an identity collision waiting to happen. Locals already taken by a
            // KNOWN email at the firm are also off-limits.
            var takenLocals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in known)
            {
                var at = k.Email.IndexOf('@');
                if (at > 0) takenLocals.Add(k.Email[..at].Trim());
            }

            foreach (var g in gaps)
            {
                var (gf, gl) = SplitName(g.Name);
                if (Norm(gf).Length == 0 || Norm(gl).Length == 0) continue;
                var local = build(Norm(gf), Norm(gl));
                if (string.IsNullOrWhiteSpace(local)) continue;
                if (!takenLocals.Add(local)) continue;
                if (commit && await SetEmailAsync(con, g.Id, $"{local}@{domain}", "PatternInferred", 55, ct).ConfigureAwait(false))
                    written++;
                else if (!commit) written++;
            }
        }
        return written;
    }

    // ---- PAID (capped): Hunter email-finder for top selectors --------------
    public async Task<EnrichResult> HunterFinderPassAsync(string apiKey, int creditCap, int minConfidence, bool commit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || creditCap <= 0)
            return new EnrichResult(0, 0, 0);

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        // Highest-importance selectors still missing an email, at firms with a website domain.
        var targets = new List<(long PersonId, string Name, string Domain)>();
        await using (var cmd = new SqlCommand(TargetSql, con) { CommandTimeout = 120 })
        {
            cmd.Parameters.Add("@max", SqlDbType.Int).Value = creditCap;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                targets.Add((r.GetInt64(0), r.GetString(1), CanonicalOrgResolver.ExtractWebsiteDomain(r.GetString(2))));
        }

        int credits = 0, filled = 0;
        foreach (var t in targets)
        {
            if (credits >= creditCap) break;
            if (string.IsNullOrWhiteSpace(t.Domain)) continue;
            var (first, last) = SplitName(t.Name);
            if (Norm(first).Length == 0 || Norm(last).Length == 0) continue;

            credits++;
            (string Email, int Conf)? hit;
            try { hit = await HunterEmailFinderAsync(apiKey, t.Domain, first, last, ct).ConfigureAwait(false); }
            catch { continue; }
            if (hit is null || hit.Value.Conf < minConfidence) continue;

            if (commit && await SetEmailAsync(con, t.PersonId, hit.Value.Email, "Hunter", hit.Value.Conf, ct).ConfigureAwait(false))
                filled++;
            else if (!commit) filled++;
        }
        return new EnrichResult(0, filled, credits);
    }

    private const string TargetSql = @"
WITH MpiVerdict AS (
    SELECT e.MajorProjectsInventoryId AS MpiId,
           MAX(CASE COALESCE(NULLIF(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'),''), NULLIF(JSON_VALUE(e.ResultJson,'$.verdict'),''))
                WHEN N'PURSUE_URGENT' THEN 5.0 WHEN N'PURSUE' THEN 3.0 WHEN N'MONITOR' THEN 1.0 ELSE 0.25 END) AS W
    FROM opportunities.MajorProjectEnrichment e
    WHERE e.ProviderName = N'ProjectBriefHoning' AND e.ResultJson IS NOT NULL
    GROUP BY e.MajorProjectsInventoryId),
OrgImportance AS (
    SELECT x.OrgId, SUM(ISNULL(v.W,0.25)) AS W
    FROM opportunities.MajorProjectsInventory m
    CROSS APPLY (VALUES (m.ArchitectCanonicalOrgId),(m.GeneralContractorCanonicalOrgId),
                        (m.StructuralEngineerCanonicalOrgId),(m.ProponentCanonicalOrgId)) x(OrgId)
    LEFT JOIN MpiVerdict v ON v.MpiId = m.Id
    WHERE m.RetiredAtUtc IS NULL AND x.OrgId IS NOT NULL
    GROUP BY x.OrgId)
SELECT TOP (@max) p.Id, p.DisplayName, co.Website
FROM opportunities.IntelPersonAffiliation a
JOIN opportunities.IntelPerson p ON p.Id = a.IntelPersonId AND p.RetiredAtUtc IS NULL
JOIN opportunities.CanonicalOrg co ON co.Id = a.CanonicalOrgId AND co.RetiredAtUtc IS NULL
JOIN OrgImportance oi ON oi.OrgId = co.Id
WHERE a.RetiredAtUtc IS NULL
  AND NULLIF(LTRIM(RTRIM(p.Email)),'') IS NULL
  AND NULLIF(LTRIM(RTRIM(co.Website)),'') IS NOT NULL
ORDER BY oi.W DESC, co.Id, p.Id;";

    private static async Task<(string Email, int Conf)?> HunterEmailFinderAsync(string key, string domain, string first, string last, CancellationToken ct)
    {
        var url = $"https://api.hunter.io/v2/email-finder?domain={Uri.EscapeDataString(domain)}" +
                  $"&first_name={Uri.EscapeDataString(first)}&last_name={Uri.EscapeDataString(last)}&api_key={key}";
        using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
        var email = data.TryGetProperty("email", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        if (string.IsNullOrWhiteSpace(email)) return null;
        var conf = data.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0;
        return (email!, conf);
    }

    // ---- shared DB + parsing helpers --------------------------------------
    private static async Task<(List<(string Name, string Email)> Known, List<(long Id, string Name)> Gaps)>
        LoadFirmPeopleAsync(SqlConnection con, long firmId, CancellationToken ct)
    {
        var known = new List<(string, string)>(); var gaps = new List<(long, string)>();
        await using var cmd = new SqlCommand(@"
SELECT p.Id, p.DisplayName, p.Email FROM opportunities.IntelPersonAffiliation a
JOIN opportunities.IntelPerson p ON p.Id=a.IntelPersonId AND p.RetiredAtUtc IS NULL
WHERE a.RetiredAtUtc IS NULL AND a.CanonicalOrgId=@org", con) { CommandTimeout = 60 };
        cmd.Parameters.Add("@org", SqlDbType.BigInt).Value = firmId;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var name = r.GetString(1); var email = r.IsDBNull(2) ? null : r.GetString(2);
            if (!string.IsNullOrWhiteSpace(email)) known.Add((name, email!)); else gaps.Add((r.GetInt64(0), name));
        }
        return (known, gaps);
    }

    private static async Task<bool> SetEmailAsync(SqlConnection con, long personId, string email, string source, int conf, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(@"
UPDATE opportunities.IntelPerson
SET Email=@email, EmailSource=@src, EmailConfidence=@conf, EmailCheckedAtUtc=sysdatetimeoffset(), UpdatedAtUtc=sysdatetimeoffset()
WHERE Id=@id AND NULLIF(LTRIM(RTRIM(Email)),'') IS NULL;", con) { CommandTimeout = 60 };
        cmd.Parameters.Add("@email", SqlDbType.NVarChar, 256).Value = email;
        cmd.Parameters.Add("@src", SqlDbType.NVarChar, 20).Value = source;
        cmd.Parameters.Add("@conf", SqlDbType.TinyInt).Value = (byte)Math.Clamp(conf, 0, 100);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = personId;
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    internal static string Norm(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) if (char.IsLetter(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    internal static (string First, string Last) SplitName(string display)
    {
        var parts = display.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return ("", "");
        if (parts.Length == 1) return ("", parts[0]);
        return (parts[0], parts[^1]);
    }


    internal static (Func<string, string, string> Build, string Domain)? DerivePattern(List<(string Name, string Email)> known)
    {
        var pairs = new List<(string F, string L, string Local, string Domain)>();
        foreach (var (name, email) in known)
        {
            var at = email.IndexOf('@'); if (at <= 0) continue;
            var (f, l) = SplitName(name); if (Norm(f).Length == 0 || Norm(l).Length == 0) continue;
            pairs.Add((Norm(f), Norm(l), email[..at].ToLowerInvariant(), email[(at + 1)..].ToLowerInvariant()));
        }
        if (pairs.Count == 0) return null;
        var cands = new Func<string, string, string>[]
        {
            (f,l)=>$"{f}.{l}", (f,l)=>$"{f[..1]}{l}", (f,l)=>$"{f[..1]}.{l}", (f,l)=>$"{f}{l}",
            (f,l)=>$"{f}_{l}", (f,l)=>f, (f,l)=>$"{f}{l[..1]}", (f,l)=>$"{l}{f[..1]}",
        };
        var best = cands.Select(c => (Fn: c, Hits: pairs.Count(p => c(p.F, p.L) == p.Local)))
                        .OrderByDescending(x => x.Hits).First();
        if (best.Hits == 0 || best.Hits * 2 < pairs.Count) return null;
        var domain = pairs.GroupBy(p => p.Domain).OrderByDescending(g => g.Count()).First().Key;
        var fn = best.Fn;
        return ((nf, nl) => (nf.Length == 0 || nl.Length == 0) ? "" : fn(nf, nl), domain);
    }
}
