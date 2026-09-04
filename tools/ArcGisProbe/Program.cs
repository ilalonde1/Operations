// ArcGisProbe — run the ArcGIS adapter against a live layer WITHOUT the worker,
// and show what would actually be ingested.
//
// The point of a platform adapter is that a new city is a config row. The risk
// is that a config row is a guess. This proves one before it is enabled:
//
//   ArcGisProbe --source Victoria_DevelopmentApplications      (config from the DB)
//   ArcGisProbe --layer <url> --config <key=value file>        (config not yet in the DB)
//
// It prints the row-to-application collapse, the relevance-gate verdict for
// every application, and the newest ones — so the answer to "is this an
// applications layer or a zoning overlay?" is read off the output rather than
// assumed. Reads only; it writes nothing.
using System.Globalization;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Ingestion.Providers;
using Kor.Opportunities.Data.Sources;
using Microsoft.Extensions.Logging;

var args0 = args;

string? sourceName = null;
string? layerUrl = null;
string? configPath = null;
string? deltaPath = null;
var showCount = 15;

for (var i = 0; i < args0.Length; i++)
{
    switch (args0[i])
    {
        case "--source" when i + 1 < args0.Length:
            sourceName = args0[++i];
            break;
        case "--layer" when i + 1 < args0.Length:
            layerUrl = args0[++i];
            break;
        case "--config" when i + 1 < args0.Length:
            configPath = args0[++i];
            break;
        case "--delta" when i + 1 < args0.Length:
            deltaPath = args0[++i];
            break;
        case "--show" when i + 1 < args0.Length:
            showCount = int.Parse(args0[++i], CultureInfo.InvariantCulture);
            break;
        default:
            Console.Error.WriteLine($"Unknown argument: {args0[i]}");
            return 2;
    }
}

using var loggerFactory = LoggerFactory.Create(b => b
    .SetMinimumLevel(LogLevel.Information)
    .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; }));

OpportunitySource source;
IReadOnlyDictionary<string, string> config;

if (!string.IsNullOrWhiteSpace(sourceName))
{
    var cs = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB");
    if (string.IsNullOrWhiteSpace(cs))
    {
        Console.Error.WriteLine("Set KOR_OPPORTUNITIES_OPPORTUNITIESDB to use --source.");
        return 2;
    }

    var store = new SqlOpportunitySourceStore(cs);
    var found = await store.GetByNameAsync(sourceName, CancellationToken.None);
    if (found is null)
    {
        Console.Error.WriteLine($"No source named '{sourceName}'.");
        return 2;
    }

    source = found;
    config = await store.GetMappingsAsync(found.Id, CancellationToken.None);
    Console.WriteLine($"Source   : {source.Name}  (enabled={source.IsEnabled}, type={source.SourceType})");
}
else if (!string.IsNullOrWhiteSpace(layerUrl) && !string.IsNullOrWhiteSpace(configPath))
{
    source = new OpportunitySource
    {
        Id = Guid.NewGuid(),
        Name = "probe",
        SourceType = OpportunitySourceType.ArcGisFeatureService,
        BaseUrl = layerUrl,
        RequestTimeoutSeconds = 120,
    };

    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var line in File.ReadAllLines(configPath))
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            continue;
        }

        var eq = trimmed.IndexOf('=', StringComparison.Ordinal);
        if (eq > 0)
        {
            dict[trimmed[..eq].Trim()] = trimmed[(eq + 1)..].Trim();
        }
    }

    config = dict;
}
else
{
    Console.Error.WriteLine("Usage: ArcGisProbe --source <Name> | --layer <url> --config <file> [--show N]");
    return 2;
}

Console.WriteLine($"Layer    : {source.BaseUrl}");
Console.WriteLine();

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
var provider = new ArcGisFeatureOpportunityProvider(
    http,
    loggerFactory.CreateLogger<ArcGisFeatureOpportunityProvider>());

var candidates = await provider.FetchAsync(source, config, CancellationToken.None);

// --delta is the GAIN ARM of the relevance-gate differential that
// RelevanceGateDiff cannot run: opportunities.RelevanceGateRejects stores no
// description, so a vocabulary change aimed at planning prose is invisible
// there. Here the description is the live PURPOSE text, which is where the
// signal actually lives.
RelevanceVocabularyDelta? delta = null;
if (deltaPath is not null)
{
    var building = new List<string>();
    var professional = new List<string>();
    foreach (var raw in File.ReadAllLines(deltaPath))
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

    delta = new RelevanceVocabularyDelta(building, professional);
    Console.WriteLine($"Delta    : {delta.BuildingSignals.Count} candidate building term(s) from {deltaPath}");
    Console.WriteLine();
}

var kept = new List<OpportunityCandidate>();
var rejected = new List<(OpportunityCandidate Candidate, string Reason)>();
var newlyKept = new List<(OpportunityCandidate Candidate, string Term)>();
foreach (var c in candidates)
{
    var decision = StructuralRelevanceGate.Evaluate(c.Title, c.Description, c.Buyer);
    if (decision.Keep)
    {
        kept.Add(c);
        continue;
    }

    if (delta is not null)
    {
        var withDelta = StructuralRelevanceGate.Evaluate(c.Title, c.Description, c.Buyer, delta);
        if (withDelta.Keep)
        {
            var text = $"{c.Title} {c.Description}".ToLowerInvariant();
            newlyKept.Add((c, delta.FirstMatch(text) ?? "(unattributed)"));
            continue;
        }
    }

    rejected.Add((c, decision.RejectReason ?? "(no reason)"));
}

Console.WriteLine();
Console.WriteLine($"Applications : {candidates.Count}");
Console.WriteLine($"  gate KEEP  : {kept.Count}");
if (delta is not null)
{
    Console.WriteLine($"  delta KEEP : {newlyKept.Count}   <- newly kept by the candidate vocabulary");
}

Console.WriteLine($"  gate DROP  : {rejected.Count}");

var dated = candidates.Where(c => c.PostedDateUtc is not null).ToList();
if (dated.Count > 0)
{
    Console.WriteLine(
        $"  dated      : {dated.Count} of {candidates.Count}, " +
        $"{dated.Min(c => c.PostedDateUtc)!.Value:yyyy-MM-dd} -> {dated.Max(c => c.PostedDateUtc)!.Value:yyyy-MM-dd}");
}

Console.WriteLine();
Console.WriteLine($"NEWEST {showCount} KEPT");
Console.WriteLine(new string('-', 100));
foreach (var c in kept.OrderByDescending(c => c.PostedDateUtc ?? DateTimeOffset.MinValue).Take(showCount))
{
    Console.WriteLine($"{c.PostedDateUtc:yyyy-MM-dd}  {c.ExternalReference,-12}  {Cut(c.Title, 70)}");
    Console.WriteLine($"              {Cut(c.Location, 70)}");
    Console.WriteLine($"              {Cut(c.Description, 110)}");
    Console.WriteLine($"              {c.Url}");
    Console.WriteLine();
}

if (newlyKept.Count > 0)
{
    Console.WriteLine($"NEWLY KEPT BY THE DELTA ({newlyKept.Count} of {candidates.Count}) — LOOK AT THESE");
    Console.WriteLine(new string('-', 100));
    foreach (var g in newlyKept.GroupBy(n => n.Term).OrderByDescending(g => g.Count()))
    {
        Console.WriteLine($"  {g.Count(),4}  \"{g.Key}\"");
        foreach (var n in g.Take(showCount))
        {
            Console.WriteLine($"          {Cut(n.Candidate.Title, 88)}");
            Console.WriteLine($"            └ {Cut(n.Candidate.Description, 120)}");
        }

        Console.WriteLine();
    }
}

if (rejected.Count > 0)
{
    Console.WriteLine($"STILL REJECTED ({rejected.Count} of {candidates.Count})");
    Console.WriteLine(new string('-', 100));
    foreach (var g in rejected.GroupBy(r => r.Reason).OrderByDescending(g => g.Count()))
    {
        Console.WriteLine($"  {g.Count(),4}  {g.Key}");
        foreach (var r in g.Take(showCount))
        {
            Console.WriteLine($"          {Cut(r.Candidate.Title, 90)}");
            Console.WriteLine($"            └ {Cut(r.Candidate.Description, 130)}");
        }
    }
}

return 0;

static string Cut(string? s, int max)
{
    if (string.IsNullOrEmpty(s))
    {
        return "";
    }

    var flat = s.Replace('\r', ' ').Replace('\n', ' ');
    return flat.Length <= max ? flat : flat[..max] + "…";
}
