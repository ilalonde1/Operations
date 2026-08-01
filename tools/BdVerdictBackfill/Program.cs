#nullable enable
// BdVerdictBackfill — one-shot, BD-Audit-2026-06-09 follow-up (Codex
// verification round 2).
//
// Honing rows ingested before HONING-OUTPUT-CONTRACT.md carry their verdict
// only as leading FREE TEXT inside korAngle / status / description ("PURSUE —
// see ID 4570", "DEAD — Stantec integrated team"), not in the structured
// honingPass.verdict field every reader queries via
// COALESCE(JSON_VALUE($.honingPass.verdict), JSON_VALUE($.verdict)).
//
// Source of truth is the DB ResultJson itself — NOT the Desktop *-final.json
// report bakes, which are derivative regex extractions of a SUBSET of these
// rows (e.g. .residential-final.json has 461 entries vs 1,594 honed rows in
// the DB) and reference pre-m117 ids.
//
// For each ProjectBriefHoning row on an ACTIVE MPI whose structured verdict
// is missing: find the verdict with a LEADING-ANCHOR regex (PURSUE_URGENT
// tried before PURSUE; anchored at the start of korAngle/status/description
// so narrative mentions like "downgraded from MONITOR" cannot false-match),
// then inject honingPass.verdict + honingPass.verdictSource (provenance)
// into the JSON and UPDATE the row. LastRefreshAtUtc is deliberately NOT
// touched — this is a re-shape of existing research, not a refresh.
//
// Dry-run by default; pass --commit to write.
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

var commit = args.Contains("--commit", StringComparer.OrdinalIgnoreCase);

var cs = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB")
    ?? throw new InvalidOperationException("KOR_OPPORTUNITIES_OPPORTUNITIESDB env var missing");

// Leading-anchor: optional quotes/whitespace, then the verdict token at the
// very start of the field, followed by a non-word boundary (— : . , space or
// end). PURSUE_URGENT listed first so PURSUE cannot shadow it.
var leadingVerdict = new Regex(
    @"^\s*[""']?\s*(PURSUE_URGENT|PURSUE|MONITOR|DEAD|DISCOVER|DUPLICATE)\b",
    RegexOptions.Compiled);

var rows = new List<(long EnrichmentId, long MpiId, string Json)>();
await using (var con = new SqlConnection(cs))
{
    await con.OpenAsync().ConfigureAwait(false);
    await using var cmd = new SqlCommand(@"
SELECT e.Id, e.MajorProjectsInventoryId, e.ResultJson
FROM opportunities.MajorProjectEnrichment e
JOIN opportunities.MajorProjectsInventory m ON m.Id = e.MajorProjectsInventoryId
WHERE e.ProviderName = N'ProjectBriefHoning'
  AND e.ResultJson IS NOT NULL
  AND m.RetiredAtUtc IS NULL
  AND COALESCE(JSON_VALUE(e.ResultJson, '$.honingPass.verdict'),
               JSON_VALUE(e.ResultJson, '$.verdict')) IS NULL
ORDER BY e.Id;", con) { CommandTimeout = 120 };
    await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
    while (await reader.ReadAsync().ConfigureAwait(false))
    {
        rows.Add((reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2)));
    }
}

Console.WriteLine($"Rows with honing JSON but no structured verdict (active MPIs): {rows.Count} ({(commit ? "COMMIT" : "dry-run")})");

static string? FieldOf(JsonNode? node, string name)
    => node is JsonObject o && o.TryGetPropertyValue(name, out var v) && v is JsonValue val
       && val.TryGetValue<string>(out var s) ? s : null;

var byVerdict = new Dictionary<string, int>(StringComparer.Ordinal);
var noMatch = 0; var parseFail = 0; var updated = 0;
var samples = new List<string>();

await using (var con = new SqlConnection(cs))
{
    await con.OpenAsync().ConfigureAwait(false);

    foreach (var row in rows)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(row.Json); }
        catch (JsonException) { parseFail++; continue; }
        if (root is not JsonObject rootObj) { parseFail++; continue; }

        var honingPass = rootObj["honingPass"] as JsonObject;

        // Candidate fields in confidence order: honingPass first, then root.
        var candidates = new[]
        {
            FieldOf(honingPass, "korAngle"), FieldOf(honingPass, "status"), FieldOf(honingPass, "description"),
            FieldOf(rootObj, "korAngle"), FieldOf(rootObj, "status"), FieldOf(rootObj, "description"),
        };

        string? verdict = null;
        string? matchedIn = null;
        string[] names = { "honingPass.korAngle", "honingPass.status", "honingPass.description", "korAngle", "status", "description" };
        for (var i = 0; i < candidates.Length; i++)
        {
            if (candidates[i] is null) continue;
            var m = leadingVerdict.Match(candidates[i]!);
            if (m.Success)
            {
                verdict = m.Groups[1].Value;
                matchedIn = names[i];
                break;
            }
        }

        if (verdict is null)
        {
            noMatch++;
            continue;
        }

        byVerdict[verdict] = byVerdict.GetValueOrDefault(verdict) + 1;
        if (samples.Count < 12)
        {
            var src = candidates[Array.IndexOf(names, matchedIn)] ?? "";
            samples.Add($"  MPI {row.MpiId}: {verdict,-13} from {matchedIn}: \"{src[..Math.Min(70, src.Length)]}\"");
        }

        if (commit)
        {
            if (honingPass is null)
            {
                honingPass = new JsonObject();
                rootObj["honingPass"] = honingPass;
            }

            honingPass["verdict"] = verdict;
            honingPass["verdictSource"] = "backfill-regex-2026-06-10 (leading-anchor over korAngle/status/description; BD-Audit follow-up)";

            await using var upd = new SqlCommand(@"
UPDATE opportunities.MajorProjectEnrichment
SET ResultJson = @json, UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = @id;", con);
            upd.Parameters.AddWithValue("@json", rootObj.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
            upd.Parameters.AddWithValue("@id", row.EnrichmentId);
            await upd.ExecuteNonQueryAsync().ConfigureAwait(false);
            updated++;
        }
    }
}

Console.WriteLine();
Console.WriteLine("Verdicts found (leading-anchor):");
foreach (var kv in byVerdict.OrderByDescending(k => k.Value))
{
    Console.WriteLine($"  {kv.Key,-14} {kv.Value}");
}

Console.WriteLine($"  (no leading verdict token) {noMatch}");
Console.WriteLine($"  (unparseable JSON)         {parseFail}");
Console.WriteLine();
Console.WriteLine(commit
    ? $"Updated {updated} rows."
    : "Dry-run — nothing written. Samples:");
if (!commit)
{
    foreach (var s in samples) Console.WriteLine(s);
}

return parseFail > 0 ? 1 : 0;
