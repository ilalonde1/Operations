// Proves the on-demand synthesizer's cost behavior:
//   1. Classify the defence set - shows FRESH/QUIET/DUE + the token verdict.
//   2. EnsureFreshAsync - only DUE rows spend tokens; the run prints the tally.
// Usage: dotnet run --project tools/BdSynthesisSmoke [-- <mpiId> <mpiId> ...]
using System.Net.Http;
using Kor.Opportunities.Data.BdReports;

var cs = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB")
      ?? Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB", EnvironmentVariableTarget.User)
      ?? Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB", EnvironmentVariableTarget.Machine);
if (string.IsNullOrWhiteSpace(cs)) { Console.Error.WriteLine("KOR_OPPORTUNITIES_OPPORTUNITIESDB not set."); return 2; }

var apiKey = Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY")
          ?? Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY", EnvironmentVariableTarget.User)
          ?? Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY", EnvironmentVariableTarget.Machine)
          ?? "";

if (args.Length >= 2 && args[0] == "sector")
{
    return await SectorRun.RunAsync(args[1]);
}

if (args.Length >= 3 && args[0] == "emit")
{
    return await SectorEmit.RunAsync(args[1], args[2]);
}

// ensure mode: refresh a given id set on demand (used by the PS report builders
// as a pre-step so the dossier they pull reflects on-demand-fresh verdicts).
if (args.Length >= 2 && args[0] == "ensure")
{
    var eids = args.Skip(1).Select(long.Parse).ToList();
    var ecs = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB")
           ?? Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB", EnvironmentVariableTarget.User)
           ?? Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB", EnvironmentVariableTarget.Machine) ?? "";
    var ekey = Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY")
           ?? Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY", EnvironmentVariableTarget.User)
           ?? Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY", EnvironmentVariableTarget.Machine) ?? "";
    using var ehttp = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
    var es = new OnDemandHoningSynthesizer(ecs, ehttp, ekey);
    var er = await es.EnsureFreshAsync(eids, CancellationToken.None);
    Console.WriteLine($"ensure: total={er.Total} fresh={er.Fresh} quiet={er.Quiet} due={er.Due} synthesized={er.Synthesized} ~tokens={er.TokensSpentApprox}");
    return 0;
}

var ids = args.Length > 0
    ? args.Select(long.Parse).ToList()
    : new List<long> { 7161, 7162, 7163, 7164, 7165, 7166, 6442, 6443 };

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
var synth = new OnDemandHoningSynthesizer(cs, http, apiKey);

Console.WriteLine($"Classifying {ids.Count} project(s) (zero tokens)...\n");
var classified = await synth.ClassifyAsync(ids, CancellationToken.None);
foreach (var c in classified)
{
    var cost = c.Class == OnDemandHoningSynthesizer.Freshness.Due ? "-> WILL SPEND" : "-> 0 tokens";
    Console.WriteLine($"  {c.MpiId,6}  {c.Class,-6} verdict={c.Verdict ?? "(none)"}  age={c.AgeDays}d/ttl={c.TtlDays}d  newSignals={c.NewSignalCount}  {cost}");
}

Console.WriteLine($"\nApiKey present: {(string.IsNullOrWhiteSpace(apiKey) ? "NO (DUE rows will be counted, not called)" : "yes")}");
Console.WriteLine("Running EnsureFreshAsync...\n");
var res = await synth.EnsureFreshAsync(ids, CancellationToken.None);
Console.WriteLine($"  total={res.Total}  fresh={res.Fresh}(0tok)  quiet={res.Quiet}(0tok)  due={res.Due}  synthesized={res.Synthesized}  ~tokens={res.TokensSpentApprox}");
Console.WriteLine($"\nCost proof: {res.Fresh + res.Quiet} of {res.Total} projects served for ZERO tokens.");
return 0;
