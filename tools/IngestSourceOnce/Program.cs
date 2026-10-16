// Runs ONE opportunity source through the Worker's own IngestionService, from this PC, once.
//
// Why: a new source is seeded IsEnabled = 0 until the Worker deploy that carries its
// provider (Worker CLAUDE.md). This loads it the same way the Worker would — the same
// provider, relevance gate, key derivation, dedup hash, observation rows, scoring and
// IngestionRuns record — without a deploy. It is not a second ingestion path: every
// write goes through IngestionService.IngestAsync.
//
// Prior art checked 2026-10-16: tools/ArcGisProbe (ArcGIS only, read-only),
// tools/ScraperProbe and DetailPageProbe (read-only), Kor.Opportunities.ApcImport (raw
// ADO, one-shot, flagged as a resolver bypass). None run IngestionService.
//
// Usage:
//   dotnet run --project tools/IngestSourceOnce -- <SourceName> [--dry-run]
//   --dry-run   fetch and print every candidate with the gate's verdict; write nothing.
//
// Supported source types: the providers whose constructor is (HttpClient, ILogger).
// Add one to Providers below when needed.
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Core.Scoring;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Data.HistoricalOpportunities;
using Kor.Opportunities.Data.Ingestion;
using Kor.Opportunities.Data.Ingestion.Providers;
using Kor.Opportunities.Data.Observations;
using Kor.Opportunities.Data.Opportunities;
using Kor.Opportunities.Data.Scoring;
using Kor.Opportunities.Data.Sources;
using Microsoft.Extensions.Logging;

if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
{
    Console.Error.WriteLine("usage: IngestSourceOnce <SourceName> [--dry-run]");
    return 2;
}

var sourceName = args[0];
var dryRun = args.Contains("--dry-run");
var cs = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB")
         ?? throw new InvalidOperationException("KOR_OPPORTUNITIES_OPPORTUNITIESDB env var missing.");

using var loggers = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

var sourceStore = new SqlOpportunitySourceStore(cs);
var source = await sourceStore.GetByNameAsync(sourceName, CancellationToken.None)
             ?? throw new InvalidOperationException($"No opportunity source named {sourceName}.");

IOpportunityProvider provider = source.SourceType switch
{
    OpportunitySourceType.BcHousingProjects => new BcHousingProjectsProvider(http, loggers.CreateLogger<BcHousingProjectsProvider>()),
    OpportunitySourceType.EngagementHq => new EngagementHqOpportunityProvider(http, loggers.CreateLogger<EngagementHqOpportunityProvider>()),
    _ => throw new InvalidOperationException($"{source.SourceType} is not wired into IngestSourceOnce yet."),
};

Console.WriteLine($"Source {source.Name} ({source.SourceType}), enabled={source.IsEnabled}, mode={(dryRun ? "DRY RUN — nothing written" : "COMMIT")}");

if (dryRun)
{
    var mappings = await sourceStore.GetMappingsAsync(source.Id, CancellationToken.None);
    var candidates = await provider.FetchAsync(source, mappings, CancellationToken.None);
    var kept = 0;
    foreach (var c in candidates)
    {
        var verdict = StructuralRelevanceGate.Evaluate(c.Title, c.Description, c.Buyer);
        kept += verdict.Keep ? 1 : 0;
        Console.WriteLine($"{(verdict.Keep ? "KEEP" : "DROP")} | {c.ExternalReference} | {c.Title} | {c.Location} | {(verdict.Keep ? "" : verdict.RejectReason)}");
    }

    Console.WriteLine($"{candidates.Count} candidate(s); gate keeps {kept}, drops {candidates.Count - kept}.");
    return 0;
}

var resolver = new CanonicalOrgResolver(new SqlCanonicalOrgStore(cs), loggers.CreateLogger<CanonicalOrgResolver>());
var service = new IngestionService(
    sourceStore,
    new SqlOpportunityObservationStore(cs),
    new SqlOpportunityStore(cs, resolver, loggers.CreateLogger<SqlOpportunityStore>()),
    new SqlHistoricalOpportunityStore(cs),
    new SqlHistoricalOpportunityObservationStore(cs),
    new RuleBasedOpportunityScoringService(new ScoringOptionsAccessor(new SqlScoringProfileStore(cs)), new NullDeltekClientFactsAccessor()),
    new SqlIngestionRunStore(cs),
    new SqlRelevanceGateRejectStore(cs, loggers.CreateLogger<SqlRelevanceGateRejectStore>()),
    loggers.CreateLogger<IngestionService>());

var result = await service.IngestAsync(provider, source, "IngestSourceOnce:" + Environment.UserName, CancellationToken.None);
Console.WriteLine($"Run {result.RunId}: success={result.Success} inserted={result.Inserted} duplicate={result.Duplicate} skipped={result.Skipped} failed={result.Failed} {result.ErrorSummary}");
return result.Success ? 0 : 1;
