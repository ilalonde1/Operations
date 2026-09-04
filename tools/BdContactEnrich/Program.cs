#nullable enable
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

// BdContactEnrich — fill decision-maker emails from Hunter.io domain-search.
// Importance-first: spends Hunter credits on the firms with the highest verdict-
// weighted pursuit value (selectors on PURSUE/PURSUE_URGENT work) that have a
// website + contact gaps. 1 search credit per firm; domain-search returns the
// firm's whole roster, so we match many people per credit. Writes Email only where
// it is currently NULL, tagged EmailSource='Hunter' + the Hunter confidence — never
// overwrites a verified address, never lets the LLM invent an email.
//   dry-run (default): prints proposed fills.  --commit: writes.
//   --max-firms N  (credit cap; default 25 dry-run / 250 commit)
//   --min-confidence N  (reject Hunter hits below; default 80)

var db = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB");
var key = Environment.GetEnvironmentVariable("KOR_HUNTER_APIKEY");
if (string.IsNullOrWhiteSpace(db)) { Console.Error.WriteLine("Missing KOR_OPPORTUNITIES_OPPORTUNITIESDB."); return 2; }

bool commit = args.Contains("--commit");
bool patternMode = args.Contains("--pattern-propagate");
int maxFirms = ArgInt(args, "--max-firms", commit ? 250 : 25);
int minConf = ArgInt(args, "--min-confidence", 80);

await using var con = new SqlConnection(db);
await con.OpenAsync().ConfigureAwait(false);

if (patternMode)
{
    // FREE pass (no Hunter credits): derive each firm's email format from the clean
    // emails we already hold (asis + Hunter), construct the rest -> EmailSource='PatternInferred'.
    // Audit-v2 sweep: delegate to the ONE shared implementation
    // (ContactEnrichmentService) — it carries the own-domain guard and the
    // intra-firm duplicate-local guard the old local copy lacked.
    var svc = new Kor.Opportunities.Data.Contacts.ContactEnrichmentService(db);
    var patternFilled = await svc.PatternPropagateAsync(commit, CancellationToken.None).ConfigureAwait(false);
    Console.WriteLine($"PatternPropagate (FREE, shared/guarded): mode={(commit ? "COMMIT" : "dry-run")}; filled={patternFilled}");
    return 0;
}

if (string.IsNullOrWhiteSpace(key)) { Console.Error.WriteLine("Missing KOR_HUNTER_APIKEY (required for the Hunter mode; --pattern-propagate does not need it)."); return 2; }

// ── ROSTER INGEST ───────────────────────────────────────────────────────────
// The default Hunter mode fills a MISSING EMAIL on a person we already hold. It
// cannot add depth, because it never creates anyone. After the permit harvest
// decomposed 42 firms into the Brain, 37 of them had exactly ONE named contact —
// whoever happened to sign that application. One person is not a relationship map.
//
// This mode takes the same 1-credit domain search and ingests the whole personal
// roster it returns, creating IntelPerson + affiliation for people we do not yet
// hold. Same guards as everywhere else: personal emails only, a confidence floor,
// SHA1(lower(email)) natural key so a person already known is corroborated rather
// than duplicated, and dry-run unless --commit.
//   --roster-ingest [--provider PermitApplicants] [--max-firms N] [--min-confidence N] [--commit]
if (args.Contains("--roster-ingest"))
{
    var provider = ArgStr(args, "--provider", "PermitApplicants");
    Console.WriteLine($"RosterIngest: provider={provider}; mode={(commit ? "COMMIT" : "dry-run")}; maxFirms={maxFirms}; minConfidence={minConf}");

    using var rhttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    var targets = new List<(long OrgId, string Name, string Domain, long EnrichmentId)>();

    await using (var cmd = new SqlCommand(@"
SELECT TOP (@n) co.Id, co.DisplayName, co.WebsiteDomain, e.Id
FROM opportunities.CanonicalOrgEnrichment e
JOIN opportunities.CanonicalOrg co ON co.Id = e.CanonicalOrgId
WHERE e.ProviderName = @p AND co.WebsiteDomain IS NOT NULL AND co.RetiredAtUtc IS NULL
ORDER BY co.DisplayName;", con))
    {
        cmd.Parameters.AddWithValue("@n", maxFirms);
        cmd.Parameters.AddWithValue("@p", provider);
        await using var rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await rd.ReadAsync().ConfigureAwait(false))
        {
            targets.Add((rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetInt64(3)));
        }
    }

    Console.WriteLine($"Firms with a website anchor: {targets.Count}");
    int credits = 0, created = 0, corroborated = 0, skipped = 0;

    foreach (var t in targets)
    {
        HunterResult? hr;
        try { hr = await DomainSearchAsync(rhttp, key!, t.Domain).ConfigureAwait(false); }
        catch (Exception ex) { Console.Error.WriteLine($"[WARN] {t.Name} ({t.Domain}): {ex.Message}"); continue; }
        credits++;
        if (hr is null || hr.Emails.Count == 0) { Console.WriteLine($"  {t.Name}: no roster"); continue; }

        foreach (var e in hr.Emails)
        {
            if (e.Type != "personal" || e.Confidence < minConf) { skipped++; continue; }
            var display = $"{e.First} {e.Last}".Trim();
            if (display.Length < 3) { skipped++; continue; }

            Console.WriteLine($"  {t.Name} | {display} · {e.Value} · {e.Position ?? "(no title)"} (conf {e.Confidence})");
            if (!commit) { created++; continue; }

            await using var up = new SqlCommand(@"
DECLARE @nk char(40) = CONVERT(char(40), HASHBYTES('SHA1', LOWER(@email)), 2);
DECLARE @pid bigint;
SELECT @pid = Id FROM opportunities.IntelPerson WHERE NaturalKey = @nk;
IF @pid IS NULL
BEGIN
    INSERT INTO opportunities.IntelPerson
        (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
         FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc,
         DisplayName, NormalizedName, Email, Phone, LinkedinUrl, Corroborations,
         EmailSource, EmailConfidence, Notes)
    VALUES ('HunterRoster', @eid, 'Medium', @nk,
            SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(),
            @name, LOWER(@name), @email, @phone, @li, 1,
            'Hunter', @conf, @notes);
    SET @pid = SCOPE_IDENTITY();
END
ELSE
BEGIN
    UPDATE opportunities.IntelPerson
    SET LastSeenAtUtc = SYSDATETIMEOFFSET(), UpdatedAtUtc = SYSDATETIMEOFFSET(),
        Corroborations = Corroborations + 1,
        LinkedinUrl = COALESCE(LinkedinUrl, @li),
        Phone = COALESCE(Phone, @phone),
        Notes = COALESCE(Notes, @notes)
    WHERE Id = @pid;
END
IF NOT EXISTS (SELECT 1 FROM opportunities.IntelPersonAffiliation
               WHERE IntelPersonId = @pid AND CanonicalOrgId = @org)
BEGIN
    INSERT INTO opportunities.IntelPersonAffiliation
        (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
         FirstSeenAtUtc, LastSeenAtUtc, CreatedAtUtc, UpdatedAtUtc,
         IntelPersonId, CanonicalOrgId, IsCurrent, Title, Notes)
    VALUES ('HunterRoster', @eid, 'Medium',
            CONVERT(char(40), HASHBYTES('SHA1', CONVERT(varchar(40), @pid) + '|' + CONVERT(varchar(40), @org)), 2),
            SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(),
            @pid, @org, 1, @title, 'Hunter domain-search roster.');
END
SELECT CASE WHEN @@ROWCOUNT >= 0 THEN 1 ELSE 0 END;", con);
            up.Parameters.AddWithValue("@email", e.Value);
            up.Parameters.AddWithValue("@name", display);
            up.Parameters.AddWithValue("@org", t.OrgId);
            up.Parameters.AddWithValue("@eid", t.EnrichmentId);
            up.Parameters.AddWithValue("@conf", (byte)Math.Clamp(e.Confidence, 0, 255));
            up.Parameters.AddWithValue("@li", (object?)e.Linkedin ?? DBNull.Value);
            up.Parameters.AddWithValue("@phone", (object?)e.Phone ?? DBNull.Value);
            up.Parameters.AddWithValue("@title", (object?)e.Position ?? DBNull.Value);
            up.Parameters.AddWithValue("@notes", $"Hunter domain-search on {t.Domain}, confidence {e.Confidence}.");
            await up.ExecuteNonQueryAsync().ConfigureAwait(false);
            created++;
        }
    }

    Console.WriteLine();
    Console.WriteLine($"RosterIngest done: {credits} Hunter search credit(s); {created} person-rows {(commit ? "written" : "proposed")}; {skipped} below the bar; {corroborated} corroborated.");
    return 0;
}

static string ArgStr(string[] a, string name, string dflt)
{
    var i = Array.IndexOf(a, name);
    return i >= 0 && i + 1 < a.Length ? a[i + 1] : dflt;
}
Console.WriteLine($"BdContactEnrich: mode={(commit ? "COMMIT" : "dry-run")}; maxFirms={maxFirms}; minConfidence={minConf}");

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

var firms = await LoadTargetFirmsAsync(con, maxFirms).ConfigureAwait(false);
Console.WriteLine($"Target firms (importance-ordered, with website + contact gaps): {firms.Count}");

int searchesUsed = 0, firmsHit = 0, proposed = 0, written = 0, noMatch = 0, noDomain = 0;
foreach (var f in firms)
{
    var domain = ExtractDomain(f.Website);
    if (string.IsNullOrWhiteSpace(domain)) { noDomain++; continue; }

    var people = await LoadGapPeopleAsync(con, f.Id).ConfigureAwait(false);
    if (people.Count == 0) continue;

    HunterResult? hr;
    try { hr = await DomainSearchAsync(http, key!, domain).ConfigureAwait(false); }
    catch (Exception ex) { Console.Error.WriteLine($"[WARN] {f.Name} ({domain}): Hunter error {ex.Message}"); continue; }
    searchesUsed++;
    if (hr is null || hr.Emails.Count == 0) { continue; }
    firmsHit++;

    foreach (var person in people)
    {
        var (pf, pl) = SplitName(person.Name);
        if (pl.Length == 0) continue;
        var hit = hr.Emails.FirstOrDefault(e =>
            e.Type == "personal" && e.Confidence >= minConf &&
            Norm(e.Last) == Norm(pl) &&
            (Norm(e.First) == Norm(pf) || (pf.Length > 0 && e.First.Length > 0 && char.ToLowerInvariant(pf[0]) == char.ToLowerInvariant(e.First[0]))));
        if (hit is null) { noMatch++; continue; }

        proposed++;
        Console.WriteLine($"  {f.Name} | {person.Name} -> {hit.Value} (conf {hit.Confidence})");
        if (commit)
        {
            await SetEmailAsync(con, person.Id, hit.Value, hit.Confidence).ConfigureAwait(false);
            written++;
        }
    }
}

Console.WriteLine();
Console.WriteLine($"Done. searchesUsed(credits)={searchesUsed}; firmsWithHits={firmsHit}; noDomain={noDomain}; " +
                  $"proposed={proposed}; written={written}; peopleNoMatch={noMatch}");
Console.WriteLine(commit ? "Emails written with EmailSource='Hunter'." : "DRY-RUN — no writes. Re-run with --commit to apply.");
return 0;

// ----------------------------------------------------------------------------
static int ArgInt(string[] args, string name, int dflt)
{
    var i = Array.IndexOf(args, name);
    return (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v)) ? v : dflt;
}

static string Norm(string? s)
{
    if (string.IsNullOrWhiteSpace(s)) return string.Empty;
    var sb = new StringBuilder(s.Length);
    foreach (var c in s) if (char.IsLetter(c)) sb.Append(char.ToLowerInvariant(c));
    return sb.ToString();
}

static (string First, string Last) SplitName(string display)
{
    var parts = display.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0) return ("", "");
    if (parts.Length == 1) return ("", parts[0]);
    // drop trailing credentials like "P.Eng", "AIBC", "Dr." handled loosely by Norm
    return (parts[0], parts[^1]);
}

static string ExtractDomain(string? website)
{
    if (string.IsNullOrWhiteSpace(website)) return "";
    var s = website.Trim();
    var schemeIdx = s.IndexOf("://", StringComparison.Ordinal);
    if (schemeIdx >= 0) s = s[(schemeIdx + 3)..];
    if (s.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) s = s[4..];
    var slash = s.IndexOfAny(new[] { '/', '?', '#', ' ' });
    if (slash >= 0) s = s[..slash];
    return s.Trim().ToLowerInvariant();
}

static async Task<List<Firm>> LoadTargetFirmsAsync(SqlConnection con, int max)
{
    const string sql = @"
WITH MpiVerdict AS (
    SELECT e.MajorProjectsInventoryId AS MpiId,
           MAX(CASE COALESCE(NULLIF(JSON_VALUE(e.ResultJson,'$.honingPass.verdict'),''), NULLIF(JSON_VALUE(e.ResultJson,'$.verdict'),''))
                WHEN N'PURSUE_URGENT' THEN 5.0 WHEN N'PURSUE' THEN 3.0 WHEN N'MONITOR' THEN 1.0 ELSE 0.25 END) AS W
    FROM opportunities.MajorProjectEnrichment e
    WHERE e.ProviderName = N'ProjectBriefHoning' AND e.ResultJson IS NOT NULL
    GROUP BY e.MajorProjectsInventoryId
),
OrgMpi AS (
    SELECT x.OrgId, m.Id AS MpiId
    FROM opportunities.MajorProjectsInventory m
    CROSS APPLY (VALUES (m.ArchitectCanonicalOrgId),(m.GeneralContractorCanonicalOrgId),
                        (m.StructuralEngineerCanonicalOrgId),(m.ProponentCanonicalOrgId)) x(OrgId)
    WHERE m.RetiredAtUtc IS NULL AND x.OrgId IS NOT NULL
),
OrgImportance AS (
    SELECT om.OrgId, SUM(ISNULL(v.W, 0.25)) AS PursuitWeight
    FROM OrgMpi om LEFT JOIN MpiVerdict v ON v.MpiId = om.MpiId
    GROUP BY om.OrgId
),
Gaps AS (
    SELECT a.CanonicalOrgId, COUNT(*) AS Gaps
    FROM opportunities.IntelPersonAffiliation a
    JOIN opportunities.IntelPerson p ON p.Id = a.IntelPersonId AND p.RetiredAtUtc IS NULL
    WHERE a.RetiredAtUtc IS NULL AND NULLIF(LTRIM(RTRIM(p.Email)), '') IS NULL
    GROUP BY a.CanonicalOrgId
)
SELECT TOP (@max) co.Id, co.DisplayName, co.Website
FROM opportunities.CanonicalOrg co
JOIN Gaps g ON g.CanonicalOrgId = co.Id
JOIN OrgImportance oi ON oi.OrgId = co.Id
WHERE co.RetiredAtUtc IS NULL AND NULLIF(LTRIM(RTRIM(co.Website)), '') IS NOT NULL
ORDER BY oi.PursuitWeight DESC, g.Gaps DESC, co.Id;";
    await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 120 };
    cmd.Parameters.Add("@max", SqlDbType.Int).Value = max;
    var list = new List<Firm>();
    await using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
    while (await r.ReadAsync().ConfigureAwait(false))
        list.Add(new Firm(r.GetInt64(0), r.GetString(1), r.GetString(2)));
    return list;
}

static async Task<List<Person>> LoadGapPeopleAsync(SqlConnection con, long orgId)
{
    const string sql = @"
SELECT DISTINCT p.Id, p.DisplayName
FROM opportunities.IntelPersonAffiliation a
JOIN opportunities.IntelPerson p ON p.Id = a.IntelPersonId AND p.RetiredAtUtc IS NULL
WHERE a.RetiredAtUtc IS NULL AND a.CanonicalOrgId = @org
  AND NULLIF(LTRIM(RTRIM(p.Email)), '') IS NULL;";
    await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 60 };
    cmd.Parameters.Add("@org", SqlDbType.BigInt).Value = orgId;
    var list = new List<Person>();
    await using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
    while (await r.ReadAsync().ConfigureAwait(false))
        list.Add(new Person(r.GetInt64(0), r.GetString(1)));
    return list;
}

static async Task SetEmailAsync(SqlConnection con, long personId, string email, int confidence)
{
    // Only fills a currently-empty Email; never overwrites a verified/as-is address.
    const string sql = @"
UPDATE opportunities.IntelPerson
SET Email = @email, EmailSource = N'Hunter', EmailConfidence = @conf, EmailCheckedAtUtc = sysdatetimeoffset(), UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = @id AND NULLIF(LTRIM(RTRIM(Email)), '') IS NULL;";
    await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 60 };
    cmd.Parameters.Add("@email", SqlDbType.NVarChar, 256).Value = email;
    cmd.Parameters.Add("@conf", SqlDbType.TinyInt).Value = (byte)Math.Clamp(confidence, 0, 100);
    cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = personId;
    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
}

static async Task<HunterResult?> DomainSearchAsync(HttpClient http, string key, string domain)
{
    var url = $"https://api.hunter.io/v2/domain-search?domain={Uri.EscapeDataString(domain)}&limit=100&type=personal&api_key={key}";
    using var resp = await http.GetAsync(url).ConfigureAwait(false);
    if (!resp.IsSuccessStatusCode) return null;
    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
    if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
    var emails = new List<HunterEmail>();
    if (data.TryGetProperty("emails", out var arr) && arr.ValueKind == JsonValueKind.Array)
    {
        foreach (var e in arr.EnumerateArray())
        {
            var value = Str(e, "value");
            if (string.IsNullOrWhiteSpace(value)) continue;
            emails.Add(new HunterEmail(
                value!, Str(e, "type") ?? "", Int(e, "confidence"),
                Str(e, "first_name") ?? "", Str(e, "last_name") ?? "",
                Str(e, "position"), Str(e, "linkedin"), Str(e, "phone_number")));
        }
    }
    return new HunterResult(Str(data, "pattern") ?? "", emails);
}

static string? Str(JsonElement el, string name)
    => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
static int Int(JsonElement el, string name)
    => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;


internal sealed record Firm(long Id, string Name, string Website);
internal sealed record Person(long Id, string Name);
internal sealed record HunterResult(string Pattern, List<HunterEmail> Emails);
internal sealed record HunterEmail(
    string Value, string Type, int Confidence, string First, string Last,
    string? Position = null, string? Linkedin = null, string? Phone = null);
