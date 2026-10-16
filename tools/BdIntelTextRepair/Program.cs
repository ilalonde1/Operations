// BdIntelTextRepair — repair intel text whose UTF-8 bytes were decoded as Windows-1252, and the natural
// keys that were hashed from that garbled text.
//
// WHY THIS EXISTS. On 2026-06-14, 19:00-19:09, 436 honing-orgs result files on KOR-APP01 were rewritten
// by a Windows PowerShell 5.1 pass (read as code page 1252, written back with a byte-order mark). Their
// garbled text ("â€”" for "—") was ingested into CanonicalOrgEnrichment.ResultJson (491 payloads) and the
// intel tables. Migration 355 repaired 492 IntelSignal subjects on 2026-10-15 but not their NaturalKey,
// which is SHA1 over the normalised subject — and 457 of those 492 already had a clean twin from a later
// research pass, so 355 turned them into visible duplicates. This tool finishes the job.
//
//   BdIntelTextRepair              dry run: writes output/plan-<stamp>.csv, changes nothing
//   BdIntelTextRepair --commit     applies the plan in ONE transaction; any row that does not match
//                                  what was planned rolls the whole run back
//   --db <connection string>       default: KOR_OPPORTUNITIES_OPPORTUNITIESDB
//
// Every run that commits also writes output/rollback-<stamp>.jsonl with the full old value and old key
// of every row it changed.
//
// WHAT IT DOES, PER ROW.
//   Plain text columns (payloads, narrative paragraphs, notes, retired reasons): repair the text.
//   Keyed tables (IntelSignal, IntelAction, IntelWork, IntelPersonAffiliation): compute the key the
//   persistence service would compute from the CLEAN text, using IntelNaturalKey itself.
//     * stored key already equals it -> text-only repair if the text is garbled, else nothing;
//     * stored key equals the key of the GARBLED text (current text if still garbled, or the clean
//       text garbled back) -> PROVEN to be this fault, then:
//         - no other row holds the clean key        -> repair text and take the clean key;
//         - a live row holds it, this row is live   -> retire this row as that row's duplicate
//                                                      (text repaired, key kept: the index is unique);
//         - the holder is retired, or this row is   -> repair text, keep the key;
//     * neither -> held and listed: the key came from something else, and guessing is how 355 broke.
//   Identity rows (IntelPerson.DisplayName, CanonicalOrg.DisplayName) are LISTED, never written: their
//   keys belong to usp_ResolveOrCreateIntelPerson and the canonical resolver.
//
// WHAT IT DOES NOT COVER.
//   * Text decoded twice ("Ã¢â‚¬â€") is repaired one level only.
//   * Columns outside the list below. The 2026-10-15 scan covered every NVARCHAR column of 100+ chars
//     in the Intel* tables, CanonicalOrg and CanonicalOrgEnrichment; other schemas were not scanned.
//   * A garbled row whose duplicate twin was itself retired by hand stays live with its old key.
//   * It does not merge Corroborations from a retired duplicate into its twin.
//   * It does not touch UpdatedAtUtc, deliberately: IntelExtractionCatchUpJob re-extracts payloads
//     updated in the last 7 days, and must not be woken by a repair.
#nullable enable
using System.Data;
using System.Text;
using System.Text.Json;
using Kor.Opportunities.Data.Intel;
using Microsoft.Data.SqlClient;

var commit = false;
string? db = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--commit":
            commit = true;
            break;
        case "--db" when i + 1 < args.Length:
            db = args[++i];
            break;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            return 1;
    }
}

db ??= Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB");
if (string.IsNullOrWhiteSpace(db))
{
    Console.Error.WriteLine("Set KOR_OPPORTUNITIES_OPPORTUNITIESDB or pass --db.");
    return 1;
}

var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
var outDir = ResolveOutputDirectory();
Directory.CreateDirectory(outDir);

await using var con = new SqlConnection(db);
await con.OpenAsync();

var plan = new List<PlanItem>();
const string RetireReasonFormat = "Duplicate of Id {0}: identical once UTF-8 text decoded as Windows-1252 was repaired (BdIntelTextRepair 2026-10-15).";

// ---- 1. plain text columns ------------------------------------------------------------------------
var plainColumns = new (string Table, string Column, bool IsJson)[]
{
    ("CanonicalOrgEnrichment", "ResultJson", true),
    ("CanonicalOrgEnrichment", "ErrorMessage", false),
    ("IntelNarrative", "ParagraphText", false),
    ("IntelAction", "TimingNotes", false),
    ("IntelAction", "TargetPersonName", false),
    ("IntelPerson", "Notes", false),
    ("IntelPerson", "RetiredReason", false),
    ("IntelPersonAffiliation", "Notes", false),
    ("IntelPersonAffiliation", "Department", false),
    ("IntelPersonAffiliation", "RetiredReason", false),
    ("IntelProject", "RetiredReason", false),
    ("IntelSignal", "Detail", false),
    ("IntelSignal", "RetiredReason", false),
    ("IntelWork", "Notes", false),
};

foreach (var (table, column, isJson) in plainColumns)
{
    foreach (var (id, text) in await ReadCandidatesAsync(con, table, column))
    {
        if (!Utf8Misdecode.TryRepair(text, out var clean)) continue;
        if (isJson && ParsesAsJson(text) && !ParsesAsJson(clean))
        {
            plan.Add(new PlanItem(table, id, "held-json-invalid-after-repair", column, text, clean));
            continue;
        }

        plan.Add(new PlanItem(table, id, "repair-text", column, text, clean));
    }
}

// ---- 2. keyed tables ------------------------------------------------------------------------------
await PlanKeyedAsync(con, plan, "IntelSignal", "Subject",
    "SELECT Id, CanonicalOrgId, SignalType, Subject, NaturalKey, CASE WHEN RetiredAtUtc IS NULL THEN 0 ELSE 1 END FROM opportunities.IntelSignal",
    r => (r.GetInt64(0), r.GetString(3), r.GetString(4), r.GetInt32(5) == 1, KeyFor(r.GetInt64(1), r.GetString(2))));
static Func<string, string> KeyFor(long orgId, string type) =>
    text => IntelNaturalKey.Compute(orgId.ToString(), type, FirstN(IntelNaturalKey.Normalize(text), 200));

await PlanKeyedAsync(con, plan, "IntelAction", "Recommendation",
    "SELECT Id, CanonicalOrgId, ActionType, Recommendation, NaturalKey, CASE WHEN RetiredAtUtc IS NULL THEN 0 ELSE 1 END FROM opportunities.IntelAction",
    r => (r.GetInt64(0), r.GetString(3), r.GetString(4), r.GetInt32(5) == 1, KeyFor(r.GetInt64(1), r.GetString(2))));

await PlanKeyedAsync(con, plan, "IntelWork", "ProjectName",
    "SELECT Id, CanonicalOrgId, ProjectName, Role, NaturalKey, CASE WHEN RetiredAtUtc IS NULL THEN 0 ELSE 1 END FROM opportunities.IntelWork",
    r =>
    {
        var orgId = r.GetInt64(1);
        var role = r.IsDBNull(3) ? string.Empty : r.GetString(3);
        return (r.GetInt64(0), r.GetString(2), r.GetString(4), r.GetInt32(5) == 1,
            text => IntelNaturalKey.Compute(orgId.ToString(), IntelNaturalKey.Normalize(text), role));
    });

await PlanKeyedAsync(con, plan, "IntelPersonAffiliation", "Title",
    "SELECT Id, IntelPersonId, CanonicalOrgId, Title, NaturalKey, CASE WHEN RetiredAtUtc IS NULL THEN 0 ELSE 1 END FROM opportunities.IntelPersonAffiliation",
    r =>
    {
        var personId = r.GetInt64(1);
        var orgId = r.GetInt64(2);
        return (r.GetInt64(0), r.IsDBNull(3) ? string.Empty : r.GetString(3), r.GetString(4), r.GetInt32(5) == 1,
            text => IntelNaturalKey.Compute(personId.ToString(), orgId.ToString(), IntelNaturalKey.Normalize(text)));
    });

// ---- 3. identity rows: listed, never written --------------------------------------------------------
foreach (var (table, column) in new[] { ("IntelPerson", "DisplayName"), ("CanonicalOrg", "DisplayName") })
{
    foreach (var (id, text) in await ReadCandidatesAsync(con, table, column))
    {
        if (Utf8Misdecode.TryRepair(text, out var clean))
        {
            plan.Add(new PlanItem(table, id, "held-identity-row", column, text, clean,
                Note: "identity key owned by the resolver; repair by hand through the owning tool"));
        }
    }
}

// ---- report -----------------------------------------------------------------------------------------
var planPath = Path.Combine(outDir, $"plan-{stamp}.csv");
await WritePlanCsvAsync(planPath, plan);
Console.WriteLine($"{(commit ? "COMMIT" : "DRY RUN")} - plan: {planPath}");
Console.WriteLine();
Console.WriteLine("table | column | action | rows");
foreach (var g in plan.GroupBy(p => (p.Table, p.Column, p.Action)).OrderBy(g => g.Key.Table).ThenBy(g => g.Key.Column).ThenBy(g => g.Key.Action))
{
    Console.WriteLine($"{g.Key.Table} | {g.Key.Column} | {g.Key.Action} | {g.Count()}");
}

Console.WriteLine($"total plan rows: {plan.Count}; that change data: {plan.Count(p => p.Writes)}");

if (!commit)
{
    Console.WriteLine();
    Console.WriteLine("Nothing written. Re-run with --commit to apply.");
    return 0;
}

// ---- commit: one transaction, set-based per (table, column, action) -----------------------------
var rollbackPath = Path.Combine(outDir, $"rollback-{stamp}.jsonl");
await using (var rb = new StreamWriter(rollbackPath, false, new UTF8Encoding(false)))
{
    foreach (var p in plan.Where(p => p.Writes))
    {
        await rb.WriteLineAsync(JsonSerializer.Serialize(new { p.Table, p.Id, p.Column, p.Action, Old = p.OldText, p.OldKey }));
    }
}

Console.WriteLine($"rollback file: {rollbackPath}");

await using var tx = (SqlTransaction)await con.BeginTransactionAsync();
try
{
    foreach (var g in plan.Where(p => p.Writes).GroupBy(p => (p.Table, p.Column, p.Action)))
    {
        var expected = g.Count();
        var affected = await ApplyGroupAsync(con, tx, g.Key.Table, g.Key.Column, g.Key.Action, g.ToList());
        Console.WriteLine($"applied {g.Key.Table}.{g.Key.Column} {g.Key.Action}: {affected} of {expected}");
        if (affected != expected)
        {
            throw new InvalidOperationException(
                $"{g.Key.Table}.{g.Key.Column} {g.Key.Action}: {affected} rows matched, {expected} planned. A row changed since the plan was read.");
        }
    }

    await tx.CommitAsync();
    Console.WriteLine("COMMITTED.");
    return 0;
}
catch (Exception ex)
{
    await tx.RollbackAsync();
    Console.Error.WriteLine($"ROLLED BACK, nothing changed: {ex.Message}");
    return 2;
}

// =====================================================================================================

static async Task<List<(long Id, string Text)>> ReadCandidatesAsync(SqlConnection con, string table, string column)
{
    // A garbled UTF-8 lead byte reads as U+00C2..U+00F4; BIN2 makes the LIKE range a code-point range.
    var sql = $@"SELECT Id, [{column}] FROM opportunities.[{table}]
WHERE [{column}] COLLATE Latin1_General_100_BIN2 LIKE N'%[' + NCHAR(194) + N'-' + NCHAR(244) + N']%';";
    var rows = new List<(long, string)>();
    await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 600 };
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
    {
        rows.Add((r.GetInt64(0), r.GetString(1)));
    }

    return rows;
}

static async Task PlanKeyedAsync(
    SqlConnection con, List<PlanItem> plan, string table, string column, string sql,
    Func<SqlDataReader, (long Id, string Text, string StoredKey, bool Retired, Func<string, string> KeyOf)> read)
{
    var rows = new List<(long Id, string Text, string StoredKey, bool Retired, Func<string, string> KeyOf)>();
    await using (var cmd = new SqlCommand(sql, con) { CommandTimeout = 600 })
    await using (var r = await cmd.ExecuteReaderAsync())
    {
        while (await r.ReadAsync()) rows.Add(read(r));
    }

    var holder = new Dictionary<string, (long Id, bool Retired)>(StringComparer.OrdinalIgnoreCase);
    foreach (var row in rows) holder[row.StoredKey] = (row.Id, row.Retired);

    var notProvable = 0;
    foreach (var row in rows)
    {
        var garbledNow = Utf8Misdecode.TryRepair(row.Text, out var clean);
        var cleanKey = row.KeyOf(clean);
        if (string.Equals(row.StoredKey, cleanKey, StringComparison.OrdinalIgnoreCase))
        {
            if (garbledNow) plan.Add(new PlanItem(table, row.Id, "repair-text", column, row.Text, clean));
            continue;
        }

        var garbled = garbledNow ? row.Text : Utf8Misdecode.Garble(row.Text);
        if (garbled is null || garbled == clean || !string.Equals(row.StoredKey, row.KeyOf(garbled), StringComparison.OrdinalIgnoreCase))
        {
            // The key came from something other than this fault. Only worth listing when the text is garbled.
            if (garbledNow) plan.Add(new PlanItem(table, row.Id, "held-key-not-provable", column, row.Text, clean, row.StoredKey, cleanKey));
            else notProvable++;
            continue;
        }

        if (holder.TryGetValue(cleanKey, out var twin) && twin.Id != row.Id)
        {
            if (!twin.Retired && !row.Retired)
            {
                plan.Add(new PlanItem(table, row.Id, "retire-duplicate", column, row.Text, clean, row.StoredKey, null, twin.Id,
                    string.Format(RetireReasonFormat, twin.Id)));
            }
            else if (garbledNow)
            {
                plan.Add(new PlanItem(table, row.Id, "repair-text", column, row.Text, clean, row.StoredKey, null, twin.Id,
                    row.Retired ? "already retired; key kept" : "clean twin is retired; key kept"));
            }
            else
            {
                // Text already clean (migration 355), key still the garbled one, and one side is retired.
                plan.Add(new PlanItem(table, row.Id, row.Retired ? "none-row-already-retired" : "held-twin-retired", column, row.Text, clean,
                    row.StoredKey, cleanKey, twin.Id, "key kept: the unique index holds the clean key on the other row"));
            }

            continue;
        }

        plan.Add(new PlanItem(table, row.Id, "repair-and-rekey", column, row.Text, clean, row.StoredKey, cleanKey));
        holder[cleanKey] = (row.Id, row.Retired);
    }

    Console.WriteLine($"{table}: {rows.Count} rows read; {notProvable} clean rows whose key differs for another reason (left alone).");
}

static async Task<int> ApplyGroupAsync(SqlConnection con, SqlTransaction tx, string table, string column, string action, List<PlanItem> items)
{
    await using (var create = new SqlCommand(@"
IF OBJECT_ID('tempdb..#fix') IS NOT NULL DROP TABLE #fix;
CREATE TABLE #fix (Id BIGINT NOT NULL PRIMARY KEY, OldText NVARCHAR(MAX) NOT NULL, NewText NVARCHAR(MAX) NOT NULL,
                   OldKey NVARCHAR(128) NULL, NewKey NVARCHAR(128) NULL, NewNorm NVARCHAR(1000) NULL, Reason NVARCHAR(400) NULL);", con, tx))
    {
        await create.ExecuteNonQueryAsync();
    }

    var dt = new DataTable();
    dt.Columns.Add("Id", typeof(long));
    dt.Columns.Add("OldText", typeof(string));
    dt.Columns.Add("NewText", typeof(string));
    dt.Columns.Add("OldKey", typeof(string));
    dt.Columns.Add("NewKey", typeof(string));
    dt.Columns.Add("NewNorm", typeof(string));
    dt.Columns.Add("Reason", typeof(string));
    foreach (var p in items)
    {
        var norm = table == "IntelWork" && column == "ProjectName" ? IntelNaturalKey.Normalize(p.NewText) : null;
        dt.Rows.Add(p.Id, p.OldText, p.NewText, (object?)p.OldKey ?? DBNull.Value, (object?)p.NewKey ?? DBNull.Value,
            (object?)norm ?? DBNull.Value, (object?)(action == "retire-duplicate" ? p.Note : null) ?? DBNull.Value);
    }

    using (var bulk = new SqlBulkCopy(con, SqlBulkCopyOptions.Default, tx) { DestinationTableName = "#fix", BulkCopyTimeout = 600 })
    {
        await bulk.WriteToServerAsync(dt);
    }

    var normSet = table == "IntelWork" && column == "ProjectName" ? ", t.NormalizedProjectName = f.NewNorm" : string.Empty;
    var sql = action switch
    {
        "repair-text" => $@"UPDATE t SET t.[{column}] = f.NewText{normSet}
FROM opportunities.[{table}] t JOIN #fix f ON f.Id = t.Id
WHERE t.[{column}] COLLATE Latin1_General_100_BIN2 = f.OldText COLLATE Latin1_General_100_BIN2;",
        "repair-and-rekey" => $@"UPDATE t SET t.[{column}] = f.NewText, t.NaturalKey = f.NewKey{normSet}
FROM opportunities.[{table}] t JOIN #fix f ON f.Id = t.Id
WHERE t.NaturalKey = f.OldKey AND t.[{column}] COLLATE Latin1_General_100_BIN2 = f.OldText COLLATE Latin1_General_100_BIN2;",
        "retire-duplicate" => $@"UPDATE t SET t.[{column}] = f.NewText{normSet}, t.RetiredAtUtc = SYSDATETIMEOFFSET(), t.RetiredReason = f.Reason
FROM opportunities.[{table}] t JOIN #fix f ON f.Id = t.Id
WHERE t.NaturalKey = f.OldKey AND t.RetiredAtUtc IS NULL AND t.[{column}] COLLATE Latin1_General_100_BIN2 = f.OldText COLLATE Latin1_General_100_BIN2;",
        _ => throw new InvalidOperationException($"No writer for action '{action}'."),
    };
    await using var cmd = new SqlCommand(sql, con, tx) { CommandTimeout = 600 };
    return await cmd.ExecuteNonQueryAsync();
}

static bool ParsesAsJson(string s)
{
    try
    {
        using var _ = JsonDocument.Parse(s);
        return true;
    }
    catch (JsonException)
    {
        return false;
    }
}

static string FirstN(string s, int n) => s.Length <= n ? s : s.Substring(0, n);

static async Task WritePlanCsvAsync(string path, List<PlanItem> plan)
{
    await using var w = new StreamWriter(path, false, new UTF8Encoding(true));
    await w.WriteLineAsync("Table,Id,Action,Column,Old,New,OldKey,NewKey,TwinId,Note");
    foreach (var p in plan)
    {
        await w.WriteLineAsync(string.Join(",",
            Csv(p.Table), p.Id.ToString(), Csv(p.Action), Csv(p.Column), Csv(Preview(p.OldText)), Csv(Preview(p.NewText)),
            Csv(p.OldKey), Csv(p.NewKey), p.TwinId?.ToString() ?? string.Empty, Csv(p.Note)));
    }
}

static string Preview(string s)
{
    var flat = s.Replace('\r', ' ').Replace('\n', ' ');
    return flat.Length <= 160 ? flat : flat.Substring(0, 160) + "...";
}

static string Csv(string? s) => s is null ? string.Empty : "\"" + s.Replace("\"", "\"\"") + "\"";

static string ResolveOutputDirectory()
{
    var probe = new DirectoryInfo(Path.GetDirectoryName(typeof(PlanItem).Assembly.Location) ?? Directory.GetCurrentDirectory());
    while (probe is not null)
    {
        if (Directory.Exists(Path.Combine(probe.FullName, ".git")))
        {
            return Path.Combine(probe.FullName, "tools", "BdIntelTextRepair", "output");
        }

        probe = probe.Parent;
    }

    return Path.Combine(Directory.GetCurrentDirectory(), "output");
}

internal sealed record PlanItem(
    string Table, long Id, string Action, string Column, string OldText, string NewText,
    string? OldKey = null, string? NewKey = null, long? TwinId = null, string? Note = null)
{
    public bool Writes => Action is "repair-text" or "repair-and-rekey" or "retire-duplicate";
}
