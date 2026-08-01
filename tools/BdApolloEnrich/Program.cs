#nullable enable
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

// BdApolloEnrich — discover + reveal decision-makers at thin high-value BD orgs
// via Apollo.io, and persist them into the IntelPerson / IntelPersonAffiliation
// graph using the SAME contract as Schema/162_IngestApolloBuyerContacts.sql
// (NaturalKey = SHA1(normalize(name)); COALESCE-email never clobbers; one
// CanonicalOrgEnrichment parent per org so LastRefreshAtUtc — and the brief's
// freshness line — moves too).
//
// Apollo model (paid plan): mixed_people/api_search returns first_name +
// last_name_obfuscated + title + has_email (cheap discovery); people/match with
// reveal_personal_emails=true returns the full name + VERIFIED work email +
// linkedin (1 enrichment credit each). We verify the revealed org domain matches
// the target before writing (Apollo's same-name-wrong-company guard).
//
//   dry-run (default): api_search only, NO reveal credits — previews who'd be enriched.
//   --commit         : reveal (spends credits) + write to the graph.
//   --max-orgs N     : cap orgs (default 12 dry / 25 commit).
//   --per-org N      : reveal at most N people per org (default 4).
//   --org-id N       : target one specific CanonicalOrg id (repeatable; overrides the query).

var db = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB");
var key = Environment.GetEnvironmentVariable("KOR_APOLLO_APIKEY");
if (string.IsNullOrWhiteSpace(db)) { Console.Error.WriteLine("Missing KOR_OPPORTUNITIES_OPPORTUNITIESDB."); return 2; }
if (string.IsNullOrWhiteSpace(key)) { Console.Error.WriteLine("Missing KOR_APOLLO_APIKEY."); return 2; }

bool commit = args.Contains("--commit");
int maxOrgs = ArgInt(args, "--max-orgs", commit ? 25 : 12);
int perOrg = ArgInt(args, "--per-org", 4);
var explicitOrgIds = ArgLongs(args, "--org-id");
var provider = "Apollo-Discover-" + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

var http = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
http.DefaultRequestHeaders.Add("X-Api-Key", key);
http.DefaultRequestHeaders.Add("Cache-Control", "no-cache");

string[] seniorities = { "owner", "founder", "c_suite", "partner", "vp", "head", "director" };

await using var con = new SqlConnection(db);
await con.OpenAsync().ConfigureAwait(false);

Console.WriteLine($"BdApolloEnrich: mode={(commit ? "COMMIT" : "dry-run")}; maxOrgs={maxOrgs}; perOrg={perOrg}; provider={provider}");

// ---- 1. targets -----------------------------------------------------------
var targets = new List<(long Id, string Name, string Domain)>();
if (explicitOrgIds.Count > 0)
{
    foreach (var id in explicitOrgIds)
    {
        await using var cmd = new SqlCommand(
            "SELECT Id, DisplayName, Website FROM opportunities.CanonicalOrg WHERE Id=@id AND RetiredAtUtc IS NULL", con);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
        {
            var dom = ExtractDomain(r.IsDBNull(2) ? null : r.GetString(2));
            if (!string.IsNullOrEmpty(dom)) targets.Add((r.GetInt64(0), r.GetString(1), dom));
            else Console.WriteLine($"  skip {id}: no website domain");
        }
    }
}
else
{
    const string sql = @"
SELECT TOP (@max) co.Id, co.DisplayName, co.Website
FROM opportunities.CanonicalOrg co
WHERE co.RetiredAtUtc IS NULL
  AND co.Kind IN (N'Architect',N'GC',N'Developer',N'KorClient',N'Buyer')
  AND NULLIF(LTRIM(RTRIM(co.Website)),'') IS NOT NULL
  AND (SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation a
       JOIN opportunities.IntelPerson p ON p.Id=a.IntelPersonId AND p.RetiredAtUtc IS NULL
       WHERE a.CanonicalOrgId=co.Id AND a.RetiredAtUtc IS NULL
         AND NULLIF(LTRIM(RTRIM(p.Email)),'') IS NOT NULL) < 2
ORDER BY co.KorProjectsCount DESC, co.Id;";
    await using var cmd = new SqlCommand(sql, con);
    cmd.Parameters.Add("@max", SqlDbType.Int).Value = maxOrgs;
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
    {
        var dom = ExtractDomain(r.IsDBNull(2) ? null : r.GetString(2));
        if (!string.IsNullOrEmpty(dom)) targets.Add((r.GetInt64(0), r.GetString(1), dom));
    }
}
Console.WriteLine($"Targets with a domain: {targets.Count}");

// ---- 2. discover + (commit) reveal ----------------------------------------
var contacts = new List<Contact>();
int searches = 0, revealCredits = 0, orgMismatch = 0;
foreach (var t in targets)
{
    searches++;
    List<JsonElement> people;
    try { people = await ApolloSearchAsync(http, t.Domain, seniorities, perOrg * 3); }
    catch (Exception ex) { Console.WriteLine($"  [{t.Name}] search error: {ex.Message}"); continue; }

    var withEmail = people.Where(p => GetBool(p, "has_email")).Take(perOrg).ToList();
    Console.WriteLine($"  [{t.Name} / {t.Domain}] api_search: {people.Count} people, {withEmail.Count} with email (top {perOrg})");

    foreach (var p in withEmail)
    {
        var apolloId = GetStr(p, "id");
        var firstName = GetStr(p, "first_name");
        var title = GetStr(p, "title");
        var obf = GetStr(p, "last_name_obfuscated");
        if (string.IsNullOrWhiteSpace(apolloId)) continue;

        if (!commit)
        {
            Console.WriteLine($"      • {firstName} {obf} — {title}  (would reveal)");
            continue;
        }

        revealCredits++;
        JsonElement person;
        try { person = await ApolloMatchAsync(http, apolloId); }
        catch (Exception ex) { Console.WriteLine($"      reveal error: {ex.Message}"); continue; }
        if (person.ValueKind != JsonValueKind.Object) continue;

        var fullName = ($"{GetStr(person, "first_name")} {GetStr(person, "last_name")}").Trim();
        var email = GetStr(person, "email");
        var emailStatus = GetStr(person, "email_status");
        var linkedin = GetStr(person, "linkedin_url");
        var revealedDomain = person.TryGetProperty("organization", out var org) && org.ValueKind == JsonValueKind.Object
            ? ExtractDomain(GetStr(org, "primary_domain") ?? GetStr(org, "website_url")) : "";

        // same-name-wrong-company guard: revealed org domain must match the target.
        if (!string.IsNullOrEmpty(revealedDomain) && !DomainsMatch(revealedDomain, t.Domain))
        {
            orgMismatch++;
            Console.WriteLine($"      ! {fullName}: org domain '{revealedDomain}' != target '{t.Domain}' — DISCARDED");
            continue;
        }
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Length < 3) continue;
        if (string.IsNullOrWhiteSpace(email) || email.Contains("not_unlocked", StringComparison.OrdinalIgnoreCase)) email = null;

        var conf = string.Equals(emailStatus, "verified", StringComparison.OrdinalIgnoreCase) ? (byte)85 : (byte)60;
        contacts.Add(new Contact(t.Id, fullName, title, email, linkedin, conf));
        Console.WriteLine($"      ✓ {fullName} — {title}  {(email ?? "(no email)")}  [{emailStatus}]");
    }
}

Console.WriteLine($"\nSearches: {searches}; reveal credits spent: {revealCredits}; org-mismatch discarded: {orgMismatch}; writable contacts: {contacts.Count}");

if (!commit)
{
    Console.WriteLine("dry-run: no reveals, no writes. Re-run with --commit to reveal emails + persist.");
    return 0;
}
if (contacts.Count == 0) { Console.WriteLine("Nothing to write."); return 0; }

// ---- 3. persist via the 162 contract --------------------------------------
var written = await PersistAsync(con, contacts, provider);
Console.WriteLine($"\nPersisted: {written.PersonsMerged} persons, {written.AffiliationsMerged} affiliations (provider '{provider}'). " +
                  $"Enrichment parents touched: {written.OrgsTouched} (LastRefreshAtUtc moved).");
return 0;

// ===========================================================================
static async Task<List<JsonElement>> ApolloSearchAsync(HttpClient http, string domain, string[] seniorities, int perPage)
{
    var payload = new
    {
        q_organization_domains_list = new[] { domain },
        page = 1,
        per_page = Math.Clamp(perPage, 1, 25),
        person_seniorities = seniorities,
    };
    using var resp = await http.PostAsync("https://api.apollo.io/api/v1/mixed_people/api_search",
        new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
    resp.EnsureSuccessStatusCode();
    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    var list = new List<JsonElement>();
    if (doc.RootElement.TryGetProperty("people", out var people) && people.ValueKind == JsonValueKind.Array)
        foreach (var p in people.EnumerateArray()) list.Add(p.Clone());
    return list;
}

static async Task<JsonElement> ApolloMatchAsync(HttpClient http, string apolloId)
{
    var payload = new { id = apolloId, reveal_personal_emails = true };
    using var resp = await http.PostAsync("https://api.apollo.io/api/v1/people/match",
        new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
    resp.EnsureSuccessStatusCode();
    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    return doc.RootElement.TryGetProperty("person", out var person) ? person.Clone() : default;
}

static async Task<(int PersonsMerged, int AffiliationsMerged, int OrgsTouched)> PersistAsync(
    SqlConnection con, List<Contact> contacts, string provider)
{
    await using var tx = (SqlTransaction)await con.BeginTransactionAsync();
    try
    {
        await using (var create = new SqlCommand(@"
CREATE TABLE #c (OrgId BIGINT, Person NVARCHAR(200), Title NVARCHAR(200), Email NVARCHAR(200),
  Linkedin NVARCHAR(500), Conf TINYINT, NormTitle NVARCHAR(200),
  AffKey CHAR(40), PersonId BIGINT);", con, tx))
            await create.ExecuteNonQueryAsync();

        foreach (var c in contacts)
        {
            long personId;
            await using (var rp = new SqlCommand("opportunities.usp_ResolveOrCreateIntelPerson", con, tx)
                { CommandType = CommandType.StoredProcedure })
            {
                rp.Parameters.Add("@displayName", SqlDbType.NVarChar, 400).Value = c.DisplayName;
                rp.Parameters.Add("@email", SqlDbType.NVarChar, 400).Value = (object?)c.Email ?? DBNull.Value;
                rp.Parameters.Add("@linkedinUrl", SqlDbType.NVarChar, 800).Value = (object?)c.Linkedin ?? DBNull.Value;
                rp.Parameters.Add("@orgId", SqlDbType.BigInt).Value = c.OrgId;
                rp.Parameters.Add("@sourceProviderName", SqlDbType.NVarChar, 200).Value = provider;
                rp.Parameters.Add("@emailSource", SqlDbType.NVarChar, 50).Value =
                    string.IsNullOrWhiteSpace(c.Email) ? (object)DBNull.Value : "Apollo";
                rp.Parameters.Add("@emailConfidence", SqlDbType.Int).Value = c.EmailConfidence;
                var pidOut = rp.Parameters.Add("@personId", SqlDbType.BigInt);
                pidOut.Direction = ParameterDirection.Output;
                await rp.ExecuteNonQueryAsync();
                personId = Convert.ToInt64(pidOut.Value);
            }

            await using var ins = new SqlCommand(
                "INSERT INTO #c (OrgId, Person, Title, Email, Linkedin, Conf, PersonId) VALUES (@o,@p,@t,@e,@l,@cf,@pid)", con, tx);
            ins.Parameters.AddWithValue("@o", c.OrgId);
            ins.Parameters.AddWithValue("@p", c.DisplayName);
            ins.Parameters.AddWithValue("@t", (object?)c.Title ?? DBNull.Value);
            ins.Parameters.AddWithValue("@e", (object?)c.Email ?? DBNull.Value);
            ins.Parameters.AddWithValue("@l", (object?)c.Linkedin ?? DBNull.Value);
            ins.Parameters.AddWithValue("@cf", c.EmailConfidence);
            ins.Parameters.AddWithValue("@pid", personId);
            await ins.ExecuteNonQueryAsync();
        }

        // Mirrors Schema/162 for affiliation identity. Person identity is resolved by
        // opportunities.usp_ResolveOrCreateIntelPerson before rows enter #c.
        const string merge = @"
DECLARE @provider NVARCHAR(120) = @prov;
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();

UPDATE #c SET
  NormTitle = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
             LOWER(LTRIM(RTRIM(ISNULL(Title,N'')))),' ',''),'.',''),',',''),'''',''),'-',''),'&',''),'/',''),'(',''),')',''),'+','');

DECLARE @enr TABLE (EnrId BIGINT, OrgId BIGINT);
MERGE opportunities.CanonicalOrgEnrichment AS T
USING (SELECT DISTINCT OrgId FROM #c) AS S
  ON T.CanonicalOrgId = S.OrgId AND T.ProviderName = @provider
WHEN MATCHED THEN UPDATE SET LastRefreshAtUtc = @now, UpdatedAtUtc = @now, Status = N'ok'
WHEN NOT MATCHED THEN INSERT (CanonicalOrgId, ProviderName, Status, Attempts, LastRefreshAtUtc, CreatedAtUtc, UpdatedAtUtc)
  VALUES (S.OrgId, @provider, N'ok', 1, @now, @now, @now)
OUTPUT inserted.Id, inserted.CanonicalOrgId INTO @enr (EnrId, OrgId);

UPDATE #c SET AffKey = CONVERT(CHAR(40), HASHBYTES('SHA1',
   CAST(CAST(PersonId AS VARCHAR(20)) + '|' + CAST(OrgId AS VARCHAR(20)) + '|' + NormTitle AS VARCHAR(8000))), 2);

MERGE opportunities.IntelPersonAffiliation WITH (HOLDLOCK) AS T
USING (SELECT c.AffKey, c.PersonId, c.OrgId, MIN(c.Title) AS Title, MIN(e.EnrId) AS EnrId
       FROM #c c JOIN @enr e ON e.OrgId = c.OrgId
       GROUP BY c.AffKey, c.PersonId, c.OrgId) AS S
   ON T.NaturalKey = S.AffKey
WHEN MATCHED THEN UPDATE SET LastSeenAtUtc = @now, UpdatedAtUtc = @now
WHEN NOT MATCHED THEN INSERT
   (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
    FirstSeenAtUtc, LastSeenAtUtc, IntelPersonId, CanonicalOrgId, Title, IsCurrent)
   VALUES
   (@provider, S.EnrId, N'High', S.AffKey, @now, @now, S.PersonId, S.OrgId, S.Title, 1);

SELECT (SELECT COUNT(DISTINCT PersonId) FROM #c), (SELECT COUNT(DISTINCT AffKey) FROM #c), (SELECT COUNT(*) FROM @enr);";
        int persons = 0, affs = 0, orgs = 0;
        await using (var m = new SqlCommand(merge, con, tx) { CommandTimeout = 120 })
        {
            m.Parameters.Add("@prov", SqlDbType.NVarChar, 120).Value = provider;
            await using var r = await m.ExecuteReaderAsync();
            if (await r.ReadAsync()) { persons = r.GetInt32(0); affs = r.GetInt32(1); orgs = r.GetInt32(2); }
        }
        await tx.CommitAsync();
        return (persons, affs, orgs);
    }
    catch { await tx.RollbackAsync(); throw; }
}

// ---- helpers --------------------------------------------------------------
static int ArgInt(string[] a, string name, int dflt)
{
    var i = Array.IndexOf(a, name);
    return i >= 0 && i + 1 < a.Length && int.TryParse(a[i + 1], out var v) ? v : dflt;
}
static List<long> ArgLongs(string[] a, string name)
{
    var outp = new List<long>();
    for (int i = 0; i < a.Length - 1; i++) if (a[i] == name && long.TryParse(a[i + 1], out var v)) outp.Add(v);
    return outp;
}
static bool GetBool(JsonElement e, string p) => e.TryGetProperty(p, out var v) && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && v.GetString() == "true"));
static string GetStr(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";
static string ExtractDomain(string? website)
{
    if (string.IsNullOrWhiteSpace(website)) return "";
    var s = website.Trim();
    var i = s.IndexOf("://", StringComparison.Ordinal); if (i >= 0) s = s[(i + 3)..];
    if (s.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) s = s[4..];
    var slash = s.IndexOfAny(new[] { '/', '?', '#', ' ' }); if (slash >= 0) s = s[..slash];
    return s.Trim().ToLowerInvariant();
}
static bool DomainsMatch(string a, string b)
{
    if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
    if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
    // tolerate registrable-domain match (sub.foo.com vs foo.com)
    string Reg(string d) { var parts = d.Split('.'); return parts.Length >= 2 ? string.Join('.', parts[^2..]) : d; }
    return string.Equals(Reg(a), Reg(b), StringComparison.OrdinalIgnoreCase);
}

internal sealed record Contact(long OrgId, string DisplayName, string? Title, string? Email, string? Linkedin, byte EmailConfidence);
