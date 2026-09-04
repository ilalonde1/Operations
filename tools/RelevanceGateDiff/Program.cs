// RelevanceGateDiff — score the whole corpus with and without a proposed
// vocabulary change, and diff every verdict.
//
// WHY: StructuralRelevanceGate is shared by every source. Adding a word to it
// changes what BC Bid, Bonfire, bids&tenders, APC and CanadaBuys ingest, and
// nothing in the system would say so. Repo rule 11: where a change is supposed
// to alter one property and nothing else, run it both ways and diff everything
// else. This is that harness for the gate.
//
//   RelevanceGateDiff --vocab <file>            # one term per line, # comments
//   RelevanceGateDiff --vocab <file> --show 40  # more examples per term
//
// ── WHAT IT COMPARES ──────────────────────────────────────────────────────────
//   REGRESSION arm  every row in opportunities.OpportunityObservations (the
//                   3,803 the gate has KEPT), scored on Title + Description.
//                   Asserts none flips keep -> reject.
//   GAIN arm        every row in opportunities.RelevanceGateRejects (13,521),
//                   with the term that caused each flip named, and examples
//                   printed so a human can LOOK at them.
//
// ── WHAT IT DOES NOT COMPARE, AND CANNOT ─────────────────────────────────────
//   1. RelevanceGateRejects stores Title, Buyer and Url — NOT Description. The
//      gain arm therefore scores titles only. Both arms see identical input, so
//      the diff still isolates the vocabulary change exactly; but the absolute
//      gain is a FLOOR, not the true number. Rows whose description would have
//      carried the signal are invisible here.
//   2. It says nothing about whether a newly-kept row is a good lead — only
//      that the gate's verdict moved. That is what the printed examples are for.
//   3. It cannot see a fault present in BOTH arms: a term already in the shipped
//      vocabulary that is wrong stays wrong and shows up as no diff at all.
//
//   A same-class fault it would NOT catch: adding a term that is correct on this
//   corpus but wrong on a source not represented in it. Every enabled source has
//   rows in one of the two tables today; a source added later does not.
//
// Read-only. It writes nothing to the database.
using System.Data;
using System.Globalization;
using Kor.Opportunities.Core.Ingestion;
using Microsoft.Data.SqlClient;

var vocabPath = (string?)null;
var showCount = 12;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--vocab" when i + 1 < args.Length:
            vocabPath = args[++i];
            break;
        case "--show" when i + 1 < args.Length:
            showCount = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            return 2;
    }
}

if (vocabPath is null || !File.Exists(vocabPath))
{
    Console.Error.WriteLine("Usage: RelevanceGateDiff --vocab <file> [--show N]");
    Console.Error.WriteLine("  <file>: one candidate term per line; '#' comments; 'pro:' prefix = professional signal.");
    return 2;
}

var cs = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB");
if (string.IsNullOrWhiteSpace(cs))
{
    Console.Error.WriteLine("Set KOR_OPPORTUNITIES_OPPORTUNITIESDB.");
    return 2;
}

var building = new List<string>();
var professional = new List<string>();
foreach (var raw in File.ReadAllLines(vocabPath))
{
    var line = raw.Trim();
    if (line.Length == 0 || line.StartsWith('#'))
    {
        continue;
    }

    if (line.StartsWith("pro:", StringComparison.OrdinalIgnoreCase))
    {
        professional.Add(line[4..].Trim());
    }
    else
    {
        building.Add(line);
    }
}

var delta = new RelevanceVocabularyDelta(building, professional);

Console.WriteLine($"Candidate vocabulary: {delta.BuildingSignals.Count} building + {delta.ProfessionalSignals.Count} professional term(s)");
Console.WriteLine($"  {string.Join(", ", delta.BuildingSignals)}");
if (delta.ProfessionalSignals.Count > 0)
{
    Console.WriteLine($"  pro: {string.Join(", ", delta.ProfessionalSignals)}");
}

Console.WriteLine();

// ── REGRESSION ARM ───────────────────────────────────────────────────────────
const string keptSql = @"
SELECT ob.Title, ob.Description, ob.Buyer, s.Name AS SourceName
FROM opportunities.OpportunityObservations ob
JOIN opportunities.OpportunitySources s ON s.Id = ob.OpportunitySourceId;";

var keptTotal = 0;
var keptFlipped = new List<string>();
var keptAlreadyRejected = 0;
var keptNowRejected = new List<(string Reason, string Title)>();

await foreach (var r in ReadAsync(cs, keptSql))
{
    keptTotal++;
    var before = StructuralRelevanceGate.Evaluate(r.Title, r.Description, r.Buyer);
    var after = StructuralRelevanceGate.Evaluate(r.Title, r.Description, r.Buyer, delta);

    if (!before.Keep)
    {
        // We hold this row, but today's gate would refuse it — the cost side of
        // any tightening, and the only place an exclusion change shows up.
        keptAlreadyRejected++;
        keptNowRejected.Add((before.RejectReason ?? "(none)", $"[{r.SourceName}] {Cut(r.Title, 78)}"));
    }

    if (before.Keep && !after.Keep)
    {
        keptFlipped.Add($"{r.SourceName}: {Cut(r.Title, 90)}");
    }
}

Console.WriteLine("REGRESSION ARM — rows the gate currently keeps (Title + Description)");
Console.WriteLine(new string('-', 100));
Console.WriteLine($"  scored              : {keptTotal}");
Console.WriteLine($"  keep -> REJECT      : {keptFlipped.Count}   <- must be 0");
foreach (var f in keptFlipped.Take(showCount))
{
    Console.WriteLine($"    ! {f}");
}

// KEEP-DRIFT. The delta above can only ADD keep-signals, so it can never make the
// regression arm move — which means an EXCLUSION change is invisible to it. This
// counts rows we already hold that TODAY'S gate would refuse, which is the only
// way to see the cost of tightening. It was a bare number until 2026-09-04, when
// 450 Saanich plumbing, tree and fireplace permits turned out to be in the
// opportunity table because "dwelling" had just been added as a building signal
// and every trade permit description says "SINGLE FAMILY DWELLING".
Console.WriteLine();
Console.WriteLine($"  KEEP-DRIFT — rows we hold that today's gate would now REFUSE: {keptAlreadyRejected}");
foreach (var g in keptNowRejected.GroupBy(k => k.Reason).OrderByDescending(g => g.Count()))
{
    Console.WriteLine($"    {g.Count(),5}  {g.Key}");
    foreach (var e in g.Take(Math.Min(showCount, 4)))
    {
        Console.WriteLine($"           {e.Title}");
    }
}

Console.WriteLine();

// ── GAIN ARM ─────────────────────────────────────────────────────────────────
const string rejectSql = @"
SELECT Title, CAST(NULL AS nvarchar(max)) AS Description, Buyer, SourceName, RejectReason, RejectCount
FROM opportunities.RelevanceGateRejects;";

var rejTotal = 0;
var rejFlipped = 0;
var byTerm = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
var byOldReason = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
var bySource = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

await foreach (var r in ReadAsync(cs, rejectSql))
{
    rejTotal++;
    var before = StructuralRelevanceGate.Evaluate(r.Title, null, r.Buyer);
    var after = StructuralRelevanceGate.Evaluate(r.Title, null, r.Buyer, delta);

    if (before.Keep || !after.Keep)
    {
        continue;
    }

    rejFlipped++;
    var term = delta.FirstMatch((r.Title ?? string.Empty).ToLowerInvariant()) ?? "(unattributed)";

    if (!byTerm.TryGetValue(term, out var list))
    {
        list = new List<string>();
        byTerm[term] = list;
    }

    list.Add($"[{r.SourceName}] {Cut(r.Title, 88)}");

    var reason = r.RejectReason ?? "(none)";
    byOldReason[reason] = byOldReason.GetValueOrDefault(reason) + 1;
    bySource[r.SourceName ?? "(none)"] = bySource.GetValueOrDefault(r.SourceName ?? "(none)") + 1;
}

// ── DRIFT ARM ────────────────────────────────────────────────────────────────
// The differential above cannot see a change baked into the gate ITSELF — both
// arms carry it. RelevanceGateRejects stores the verdict the gate gave AT THE
// TIME, so comparing today's gate against that stored reason measures exactly
// that: what the shipped gate now does differently from the one that wrote the
// row. This is how the street-address guard was measured.
var driftChanged = new Dictionary<string, (int Now, int Kept, List<string> Examples)>(StringComparer.OrdinalIgnoreCase);

await foreach (var r in ReadAsync(cs, rejectSql))
{
    var stored = r.RejectReason;
    if (string.IsNullOrWhiteSpace(stored))
    {
        continue;
    }

    var now = StructuralRelevanceGate.Evaluate(r.Title, null, r.Buyer);
    var nowReason = now.Keep ? "(now KEPT)" : now.RejectReason ?? "(none)";
    if (string.Equals(nowReason, stored, StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    if (!driftChanged.TryGetValue(stored, out var bucket))
    {
        bucket = (0, 0, new List<string>());
    }

    bucket.Now++;
    if (now.Keep)
    {
        bucket.Kept++;
    }

    if (bucket.Examples.Count < showCount)
    {
        bucket.Examples.Add($"{nowReason,-38} {Cut(r.Title, 66)}");
    }

    driftChanged[stored] = bucket;
}

Console.WriteLine("DRIFT ARM — where today's gate disagrees with the verdict stored on the row");
Console.WriteLine(new string('-', 100));
if (driftChanged.Count == 0)
{
    Console.WriteLine("  none — the shipped gate reproduces every stored reject reason.");
}

foreach (var (stored, bucket) in driftChanged.OrderByDescending(k => k.Value.Now))
{
    Console.WriteLine($"  was \"{stored}\" → {bucket.Now} row(s) now differ, of which {bucket.Kept} are now KEPT");
    foreach (var e in bucket.Examples)
    {
        Console.WriteLine($"        {e}");
    }

    Console.WriteLine();
}

Console.WriteLine("GAIN ARM — rows the gate has rejected (Title ONLY; the table stores no description)");
Console.WriteLine(new string('-', 100));
Console.WriteLine($"  scored              : {rejTotal}");
Console.WriteLine($"  reject -> KEEP      : {rejFlipped}  ({(rejTotal == 0 ? 0 : 100.0 * rejFlipped / rejTotal):F1}% of the reject corpus)");
Console.WriteLine();

Console.WriteLine("  by the reason they were rejected under today's gate:");
foreach (var (reason, n) in byOldReason.OrderByDescending(k => k.Value))
{
    Console.WriteLine($"    {n,6}  {reason}");
}

Console.WriteLine();
Console.WriteLine("  by source:");
foreach (var (src, n) in bySource.OrderByDescending(k => k.Value).Take(20))
{
    Console.WriteLine($"    {n,6}  {src}");
}

Console.WriteLine();
Console.WriteLine("  BY TERM — look at these. A term whose examples are not building work is a bad term.");
Console.WriteLine(new string('-', 100));
foreach (var (term, examples) in byTerm.OrderByDescending(k => k.Value.Count))
{
    Console.WriteLine($"  {examples.Count,6}  \"{term}\"");
    foreach (var e in examples.Take(showCount))
    {
        Console.WriteLine($"            {e}");
    }

    Console.WriteLine();
}

return keptFlipped.Count == 0 ? 0 : 1;

static string Cut(string? s, int max)
{
    if (string.IsNullOrEmpty(s))
    {
        return "";
    }

    var flat = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
    return flat.Length <= max ? flat : flat[..max] + "…";
}

static async IAsyncEnumerable<Row> ReadAsync(string connectionString, string sql)
{
    await using var con = new SqlConnection(connectionString);
    await con.OpenAsync();
    await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 300 };
    await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);

    var iTitle = reader.GetOrdinal("Title");
    var iDesc = reader.GetOrdinal("Description");
    var iBuyer = reader.GetOrdinal("Buyer");
    var iSource = reader.GetOrdinal("SourceName");
    var iReason = HasColumn(reader, "RejectReason");

    while (await reader.ReadAsync())
    {
        yield return new Row(
            reader.IsDBNull(iTitle) ? null : reader.GetString(iTitle),
            reader.IsDBNull(iDesc) ? null : reader.GetString(iDesc),
            reader.IsDBNull(iBuyer) ? null : reader.GetString(iBuyer),
            reader.IsDBNull(iSource) ? null : reader.GetString(iSource),
            iReason >= 0 && !reader.IsDBNull(iReason) ? reader.GetString(iReason) : null);
    }
}

static int HasColumn(SqlDataReader reader, string name)
{
    for (var i = 0; i < reader.FieldCount; i++)
    {
        if (string.Equals(reader.GetName(i), name, StringComparison.OrdinalIgnoreCase))
        {
            return i;
        }
    }

    return -1;
}

internal sealed record Row(string? Title, string? Description, string? Buyer, string? SourceName, string? RejectReason);
