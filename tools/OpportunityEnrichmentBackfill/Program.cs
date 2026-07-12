#nullable enable
// Fill-only enrichment backfill for existing LIVE opportunities.
//
// Populates the fields the Phase-1 ingestion change now sets on NEW opps, but
// for rows already in the DB: derives Discipline via the production
// DisciplineClassifier (fed by each opp's retained raw payload), and recovers
// buyer contact from the raw payload of feed sources (CanadaBuys, SAM.gov).
//
// SAFETY:
//   * DRY-RUN BY DEFAULT — writes nothing unless --apply is passed.
//   * FILL-ONLY, enforced in the SQL WHERE clause itself (AND Discipline = 0 /
//     AND BuyerContactEmail IS NULL), so a run can never overwrite a value that
//     is already set — including a human-classified Discipline. Re-running is a
//     no-op on already-filled rows.
//   * No deletes, no key changes, no schema changes.
//
// Usage:
//   dotnet run --project tools/OpportunityEnrichmentBackfill                 (dry-run report)
//   dotnet run --project tools/OpportunityEnrichmentBackfill -- --apply      (fill-only write)
//   optional: --db "<connection string>"  (else KOR_OPPORTUNITIES_OPPORTUNITIESDB)

using System.Data;
using System.Text.Json;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

var apply = false;
string? db = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB");
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--apply": apply = true; break;
        case "--db": db = args[++i]; break;
        default: Console.Error.WriteLine($"Unknown arg '{args[i]}'"); return 2;
    }
}
if (string.IsNullOrWhiteSpace(db))
{
    Console.Error.WriteLine("Missing connection string. Set KOR_OPPORTUNITIES_OPPORTUNITIESDB or pass --db.");
    return 2;
}

const string loadSql = @"
SELECT o.Id, o.Name, o.Discipline, o.OpportunityKey,
       o.BuyerContactName, o.BuyerContactEmail, o.BuyerContactPhone,
       obs.RawJson, obs.Description
FROM opportunities.Opportunities o
OUTER APPLY (
    SELECT TOP 1 x.RawJson, x.Description
    FROM opportunities.OpportunityObservations x
    WHERE x.OpportunityId = o.Id
    ORDER BY x.IsActive DESC, x.IngestedAtUtc DESC
) obs
WHERE o.Status IN (0,1)   -- New / Reviewing (live)
ORDER BY o.Id;";

var rows = new List<Row>();
await using (var con = new SqlConnection(db))
{
    await con.OpenAsync();
    await using var cmd = new SqlCommand(loadSql, con) { CommandTimeout = 120 };
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
    {
        rows.Add(new Row(
            r.GetInt64(0),
            r.GetString(1),
            (OpportunityDiscipline)r.GetInt32(2),
            r.GetString(3),
            r.IsDBNull(4) ? null : r.GetString(4),
            r.IsDBNull(5) ? null : r.GetString(5),
            r.IsDBNull(6) ? null : r.GetString(6),
            r.IsDBNull(7) ? null : r.GetString(7),
            r.IsDBNull(8) ? null : r.GetString(8)));
    }
}

Console.WriteLine($"Enrichment backfill — {(apply ? "APPLY (fill-only writes)" : "DRY-RUN (no writes)")} — {rows.Count:N0} live opportunities\n");

var discCounts = new Dictionary<OpportunityDiscipline, int>();
int discWouldSet = 0, emailWouldSet = 0, nameWouldSet = 0, phoneWouldSet = 0;
var discSamples = new List<string>();
var contactSamples = new List<string>();

await using (var wcon = new SqlConnection(db))
{
    if (apply) await wcon.OpenAsync();

    foreach (var row in rows)
    {
        // Discipline: classify from Name + the ingested Description (the real
        // source text — present for BC Bid, Bonfire, CivicInfo, etc.), falling back
        // to RawJson. Source-agnostic: no detail-page scrape needed.
        var text = !string.IsNullOrWhiteSpace(row.Description) ? row.Description : row.RawJson;
        if (text is { Length: > 8000 }) text = text[..8000];
        var newDisc = DisciplineClassifier.Classify(null, row.Name, text);
        var discFill = row.Discipline == OpportunityDiscipline.Unknown && newDisc != OpportunityDiscipline.Unknown;
        if (discFill)
        {
            discWouldSet++;
            discCounts[newDisc] = discCounts.GetValueOrDefault(newDisc) + 1;
            if (discSamples.Count < 12) discSamples.Add($"    [{newDisc,-11}] #{row.Id} {Trim(row.Name, 90)}");
        }

        // Contact: recover from the raw payload of feed sources.
        var (cName, cEmail, cPhone) = ExtractContact(row.OpportunityKey, row.RawJson);
        var emailFill = string.IsNullOrWhiteSpace(row.BuyerContactEmail) && !string.IsNullOrWhiteSpace(cEmail);
        var nameFill = string.IsNullOrWhiteSpace(row.BuyerContactName) && !string.IsNullOrWhiteSpace(cName);
        var phoneFill = string.IsNullOrWhiteSpace(row.BuyerContactPhone) && !string.IsNullOrWhiteSpace(cPhone);
        if (emailFill) emailWouldSet++;
        if (nameFill) nameWouldSet++;
        if (phoneFill) phoneWouldSet++;
        if (emailFill && contactSamples.Count < 10)
            contactSamples.Add($"    #{row.Id} {Trim(cEmail!, 45),-45} {Trim(row.Name, 55)}");

        if (apply && (discFill || emailFill || nameFill || phoneFill))
        {
            // Each SET carries its own fill-only guard in the WHERE so a write can
            // never clobber an already-set value, even on a re-run.
            if (discFill)
                await ExecAsync(wcon, "UPDATE opportunities.Opportunities SET Discipline=@v WHERE Id=@id AND Discipline=0",
                    ("@v", SqlDbType.Int, (int)newDisc), ("@id", SqlDbType.BigInt, row.Id));
            if (emailFill)
                await ExecAsync(wcon, "UPDATE opportunities.Opportunities SET BuyerContactEmail=@v WHERE Id=@id AND BuyerContactEmail IS NULL",
                    ("@v", SqlDbType.NVarChar, Trunc(cEmail!, 255)), ("@id", SqlDbType.BigInt, row.Id));
            if (nameFill)
                await ExecAsync(wcon, "UPDATE opportunities.Opportunities SET BuyerContactName=@v WHERE Id=@id AND BuyerContactName IS NULL",
                    ("@v", SqlDbType.NVarChar, Trunc(cName!, 120)), ("@id", SqlDbType.BigInt, row.Id));
            if (phoneFill)
                await ExecAsync(wcon, "UPDATE opportunities.Opportunities SET BuyerContactPhone=@v WHERE Id=@id AND BuyerContactPhone IS NULL",
                    ("@v", SqlDbType.NVarChar, Trunc(cPhone!, 40)), ("@id", SqlDbType.BigInt, row.Id));
        }
    }
}

Console.WriteLine("DISCIPLINE — would be set on rows currently Unknown:");
Console.WriteLine($"  Total that would change: {discWouldSet:N0} of {rows.Count:N0}");
foreach (var kv in discCounts.OrderByDescending(k => k.Value))
    Console.WriteLine($"    {kv.Key,-12}: {kv.Value:N0}");
Console.WriteLine($"  (remaining {rows.Count - discWouldSet:N0} stay Unknown — no text/code signal yet; Phase 2 detail-fetch will reach them)");
if (discSamples.Count > 0)
{
    Console.WriteLine("  samples:");
    discSamples.ForEach(Console.WriteLine);
}

Console.WriteLine("\nBUYER CONTACT — would be set on rows currently blank (feed sources):");
Console.WriteLine($"  Email would set: {emailWouldSet:N0}   Name: {nameWouldSet:N0}   Phone: {phoneWouldSet:N0}");
if (contactSamples.Count > 0)
{
    Console.WriteLine("  email samples:");
    contactSamples.ForEach(Console.WriteLine);
}

Console.WriteLine(apply
    ? "\nAPPLIED (fill-only). Re-running is a no-op on filled rows."
    : "\nDRY-RUN complete — nothing was written. Re-run with --apply to fill (fill-only, non-destructive).");
return 0;

static string Trim(string s, int n) => s.Length <= n ? s : s[..n] + "…";
static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];

static async Task ExecAsync(SqlConnection con, string sql, params (string, SqlDbType, object)[] ps)
{
    await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 60 };
    foreach (var (name, type, val) in ps) cmd.Parameters.Add(name, type).Value = val;
    await cmd.ExecuteNonQueryAsync();
}

// Recover buyer contact from a source's retained raw payload. Feed sources only;
// scraper sources (listing-only) carry no contact in RawJson today (Phase 2).
static (string? Name, string? Email, string? Phone) ExtractContact(string key, string? rawJson)
{
    if (string.IsNullOrWhiteSpace(rawJson)) return (null, null, null);
    try
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return (null, null, null);

        // SAM.gov: pointOfContact array of {fullName,email,phone}
        if (key.StartsWith("SAMGOV", StringComparison.OrdinalIgnoreCase)
            && root.TryGetProperty("pointOfContact", out var poc) && poc.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in poc.EnumerateArray())
            {
                var email = Str(c, "email");
                if (!string.IsNullOrWhiteSpace(email))
                    return (Str(c, "fullName"), email, Str(c, "phone"));
            }
        }

        // CanadaBuys (and generic CSV): {header: value} with bilingual header keys.
        string? name = null, mail = null, phone = null;
        foreach (var p in root.EnumerateObject())
        {
            var n = p.Name.ToLowerInvariant();
            var v = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
            if (string.IsNullOrWhiteSpace(v)) continue;
            if (mail is null && n.Contains("contactinfoemail")) mail = v;
            else if (name is null && n.Contains("contactinfoname")) name = v;
            else if (phone is null && n.Contains("contactinfophone")) phone = v;
        }
        return (name, mail, phone);
    }
    catch { return (null, null, null); }

    static string? Str(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

internal sealed record Row(
    long Id, string Name, OpportunityDiscipline Discipline, string OpportunityKey,
    string? BuyerContactName, string? BuyerContactEmail, string? BuyerContactPhone,
    string? RawJson, string? Description);
