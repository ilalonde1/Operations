#nullable enable
using System;
using System.Linq;
using System.Net.Http;
using Kor.Operations.Data;
using Kor.Operations.Data.Deltek;
using Kor.Opportunities.Core.Deltek;
using Kor.Opportunities.Core.Ingestion.EmailAdapters;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Scoring;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Data.Heartbeat;
using Kor.Opportunities.Data.Ingestion;
using Kor.Opportunities.Data.Ingestion.Providers;
using Kor.Opportunities.Data.Intel;
using Kor.Opportunities.Data.Observations;
using Kor.Opportunities.Data.Opportunities;
using Kor.Opportunities.Data.Projects;
using Kor.Opportunities.Data.Scoring;
using Kor.Opportunities.Data.Sources;
using Kor.Opportunities.Worker.Logging;
using Kor.Opportunities.Worker.Options;
using Kor.Opportunities.Worker.Services;
using Kor.Opportunities.Worker.Services.Research;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using Quartz;
using Quartz.Impl.Matchers;
using Serilog;

namespace Kor.Opportunities.Worker;

internal static class Program
{
    private static IAsyncPolicy<HttpResponseMessage> GetTransientHttpRetryPolicy(string serviceName, Microsoft.Extensions.Logging.ILogger logger)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => (int)r.StatusCode == 429)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: (attempt, outcome, _) =>
                {
                    var defaultDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt))
                        + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
                    var ra = outcome.Result?.Headers?.RetryAfter;
                    if (ra is not null)
                    {
                        if (ra.Delta is { } delta && delta > defaultDelay) return delta;
                        if (ra.Date is { } date)
                        {
                            var fromDate = date - DateTimeOffset.UtcNow;
                            if (fromDate > defaultDelay) return fromDate;
                        }
                    }
                    return defaultDelay;
                },
                onRetryAsync: (outcome, delay, attempt, _) =>
                {
                    var status = outcome.Result?.StatusCode is { } sc
                        ? ((int)sc).ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : outcome.Exception?.GetType().Name ?? "unknown";
                    logger.LogWarning(
                        "HTTP retry {Service}: attempt={Attempt} status={Status} delay={DelayMs}ms",
                        serviceName, attempt, status, (int)delay.TotalMilliseconds);
                    return Task.CompletedTask;
                });
    }

    private static IAsyncPolicy<HttpResponseMessage> RetryPolicy(IServiceProvider sp, string serviceName) =>
        GetTransientHttpRetryPolicy(
            serviceName,
            sp.GetRequiredService<ILoggerFactory>().CreateLogger("Polly." + serviceName));

    public static void Main(string[] args)
    {
        var serilogLogger = SerilogBootstrap.CreateLogger();
        Log.Logger = serilogLogger;

        try
        {
            var builder = Host.CreateApplicationBuilder(args);

            builder.Configuration
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddJsonFile("appsettings.Production.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables(prefix: "KOR_OPPORTUNITIES_");

            var startupOptions = new OpportunitiesWorkerOptions();
            builder.Configuration.Bind(startupOptions);
            var startupAnthropicKey = !string.IsNullOrWhiteSpace(startupOptions.AnthropicApiKey)
                ? startupOptions.AnthropicApiKey
                : Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY");
            var startupGraphConfigured =
                !string.IsNullOrWhiteSpace(startupOptions.GraphEmailTenantId)
                && !string.IsNullOrWhiteSpace(startupOptions.GraphEmailClientId)
                && !string.IsNullOrWhiteSpace(startupOptions.GraphEmailClientSecret)
                && !string.IsNullOrWhiteSpace(startupOptions.GraphEmailUserEmail);
            using var probeLoggerFactory = LoggerFactory.Create(lb => lb.AddSerilog(serilogLogger, dispose: false));
            var deltekAvailable = DeltekCapabilityProbe.TryPing(
                startupOptions.DeltekDsn,
                startupOptions.DeltekUser,
                startupOptions.DeltekPassword,
                probeLoggerFactory.CreateLogger("Startup.DeltekProbe"),
                out var deltekDetail);
            if (deltekAvailable)
            {
                Log.Information("Deltek capability: AVAILABLE  {Detail}", deltekDetail);
            }
            else
            {
                Log.Warning(
                    "Deltek capability: UNAVAILABLE  {Detail}. Won-project accessor will return empty lists.",
                    deltekDetail);
            }

            builder.Services.AddWindowsService(o => o.ServiceName = "Kor.Opportunities.Worker");

            builder.Logging.ClearProviders();
            builder.Logging.AddSerilog(serilogLogger, dispose: false);

            // reloadOnChange:false on appsettings — singletons (DB connection, Quartz JobStore,
            // future Graph client) bind to these values at startup. Hot-reload would silently desync
            // them. Mirrors Kor.Operations.FileSync.Service convention.
            builder.Services
                .AddOptions<OpportunitiesWorkerOptions>()
                .Bind(builder.Configuration)
                .Validate(
                    o => !string.IsNullOrWhiteSpace(o.OpportunitiesDb),
                    "OpportunitiesDb connection string is required (set via KOR_OPPORTUNITIES_OPPORTUNITIESDB env var or appsettings).")
                .ValidateOnStart();
            builder.Services
                .AddOptions<BdResearchExecutorOptions>()
                .Bind(builder.Configuration.GetSection("BdResearchExecutor"))
                .PostConfigure(o => o.EligibleKinds = o.EligibleKinds.Distinct().ToArray());
            builder.Services
                .AddOptions<BdProjectResearchExecutorOptions>()
                .Bind(builder.Configuration.GetSection("BdProjectResearchExecutor"))
                .PostConfigure(o => o.EligibleStages = o.EligibleStages.Distinct().ToArray());
            builder.Services
                .AddOptions<BdPersonResearchExecutorOptions>()
                .Bind(builder.Configuration.GetSection("BdPersonResearchExecutor"));

            // Stores take the connection string directly (rather than an Options type) so
            // Kor.Opportunities.Data stays free of any host-specific Options class.
            string Cs(IServiceProvider sp) =>
                sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>().Value.OpportunitiesDb;

            builder.Services.AddSingleton<IHeartbeatStore>(sp => new SqlHeartbeatStore(Cs(sp)));
            builder.Services.AddSingleton<IOpportunityStore>(sp => new SqlOpportunityStore(
                Cs(sp),
                sp.GetRequiredService<Kor.Opportunities.Data.Awards.CanonicalOrgResolver>(),
                sp.GetService<ILogger<SqlOpportunityStore>>()));
            builder.Services.AddSingleton<IOpportunitySourceStore>(sp => new SqlOpportunitySourceStore(Cs(sp)));
            builder.Services.AddSingleton<IOpportunityObservationStore>(sp => new SqlOpportunityObservationStore(Cs(sp)));
builder.Services.AddSingleton<Kor.Opportunities.Data.HistoricalOpportunities.IHistoricalOpportunityStore>(sp =>
    new Kor.Opportunities.Data.HistoricalOpportunities.SqlHistoricalOpportunityStore(Cs(sp)));
builder.Services.AddSingleton<Kor.Opportunities.Data.HistoricalOpportunities.IHistoricalOpportunityDocumentStore>(sp =>
    new Kor.Opportunities.Data.HistoricalOpportunities.SqlHistoricalOpportunityDocumentStore(Cs(sp)));
builder.Services.AddSingleton<Kor.Opportunities.Data.Ingestion.Scraping.BcBidHistoricalEnrichmentService>();
builder.Services.AddSingleton<Kor.Opportunities.Data.HistoricalOpportunities.BcBidHistoricalDocumentDownloadService>();
builder.Services.AddSingleton<BdResearchExecutorService>();
builder.Services.AddSingleton<IResearchExecutorService, AnthropicResearchExecutorService>();
builder.Services.AddSingleton<IResearchPromptCatalog, FileSystemResearchPromptCatalog>();
builder.Services.AddSingleton<IAwardProgramResearchPromptCatalog, FileSystemAwardProgramResearchPromptCatalog>();
builder.Services.AddSingleton<Kor.Opportunities.Data.AwardPrograms.IAwardProgramStore>(sp =>
    new Kor.Opportunities.Data.AwardPrograms.SqlAwardProgramStore(Cs(sp)));
builder.Services.AddSingleton<IProjectIntelExtractor, ProjectBriefExtractor>();
builder.Services.AddSingleton<IProjectIntelExtractor, ProjectBriefHoningExtractor>();
builder.Services.AddSingleton<DefaultProjectIntelExtractor>();
builder.Services.AddSingleton<ProjectIntelExtractorRegistry>();
builder.Services.AddSingleton<ProjectIntelPersistenceService>(sp =>
    new ProjectIntelPersistenceService(
        Cs(sp),
        sp.GetRequiredService<ILogger<ProjectIntelPersistenceService>>()));
builder.Services.AddSingleton<IMajorProjectEnrichmentTrackingStore>(sp =>
    new SqlMajorProjectEnrichmentTrackingStore(
        Cs(sp),
        sp.GetRequiredService<ProjectIntelExtractorRegistry>(),
        sp.GetRequiredService<ProjectIntelPersistenceService>(),
        sp.GetRequiredService<ILogger<SqlMajorProjectEnrichmentTrackingStore>>()));
builder.Services.AddSingleton<IProjectResearchPromptCatalog, FileSystemProjectResearchPromptCatalog>();
builder.Services.AddSingleton<BdProjectResearchExecutorService>();

// R91c — Person refresh chokepoint. Parallels the org and project
// chokepoints; uses the existing IntelPersistenceService to write
// IntelPerson + IntelPersonAffiliation + IntelSignal + IntelAction
// drafts tagged with SourceProviderName = "PersonBrief-{personId}".
builder.Services.AddSingleton<PersonBriefExtractor>();
builder.Services.AddSingleton<Kor.Opportunities.Data.People.IPersonRefreshChokepoint>(sp =>
    new Kor.Opportunities.Data.People.SqlPersonRefreshChokepoint(
        Cs(sp),
        sp.GetRequiredService<PersonBriefExtractor>(),
        sp.GetRequiredService<IntelPersistenceService>(),
        sp.GetRequiredService<ILogger<Kor.Opportunities.Data.People.SqlPersonRefreshChokepoint>>()));
builder.Services.AddSingleton<IPersonResearchPromptCatalog, FileSystemPersonResearchPromptCatalog>();
builder.Services.AddSingleton<BdPersonResearchExecutorService>();

builder.Services.AddHttpClient<Kor.Opportunities.Data.Awards.AwardAgentEnrichmentService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(120);
})
.AddPolicyHandler((sp, _) => RetryPolicy(sp, "AwardAgentEnrichment"));
builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.AwardAgentEnrichmentService>(sp =>
{
    var options = sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>().Value;
    var apiKey = !string.IsNullOrWhiteSpace(options.AnthropicApiKey)
        ? options.AnthropicApiKey
        : (Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY") ?? "");
    var http = sp.GetRequiredService<IHttpClientFactory>()
        .CreateClient(nameof(Kor.Opportunities.Data.Awards.AwardAgentEnrichmentService));
    var store = sp.GetRequiredService<Kor.Opportunities.Data.Awards.IOpportunityAwardStore>();
    var logger = sp.GetRequiredService<ILogger<Kor.Opportunities.Data.Awards.AwardAgentEnrichmentService>>();
    return new Kor.Opportunities.Data.Awards.AwardAgentEnrichmentService(
        store,
        http,
        apiKey,
        options.AgentEnrichmentModel,
        logger);
});
            builder.Services.AddSingleton<Kor.Opportunities.Data.HistoricalOpportunities.IHistoricalOpportunityObservationStore>(sp =>
                new Kor.Opportunities.Data.HistoricalOpportunities.SqlHistoricalOpportunityObservationStore(Cs(sp)));
            builder.Services.AddSingleton<IIngestionRunStore>(sp => new SqlIngestionRunStore(Cs(sp)));
            builder.Services.AddSingleton<Kor.Opportunities.Core.Ingestion.IRelevanceGateRejectStore>(sp =>
                new SqlRelevanceGateRejectStore(
                    Cs(sp),
                    sp.GetService<ILogger<SqlRelevanceGateRejectStore>>()));
            builder.Services.AddSingleton<IJobRunStore>(sp => new SqlJobRunStore(Cs(sp)));
            builder.Services.AddSingleton<IJobScheduleStore>(sp => new SqlJobScheduleStore(Cs(sp)));
            builder.Services.AddSingleton<IIngestionTriggerStore>(sp => new SqlIngestionTriggerStore(
                Cs(sp),
                sp.GetRequiredService<ILogger<SqlIngestionTriggerStore>>()));
            builder.Services.AddSingleton<IBdResearchTriggerStore>(sp =>
                new SqlBdResearchTriggerStore(
                    sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>().Value.OpportunitiesDb,
                    sp.GetService<ILogger<SqlBdResearchTriggerStore>>()));
            builder.Services.AddSingleton<IBdPersonResearchTriggerStore>(sp =>
                new SqlBdPersonResearchTriggerStore(
                    sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>().Value.OpportunitiesDb,
                    sp.GetService<ILogger<SqlBdPersonResearchTriggerStore>>()));
builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.IOpportunityAwardStore>(sp =>
    new Kor.Opportunities.Data.Awards.SqlOpportunityAwardStore(
        Cs(sp),
        sp.GetRequiredService<Kor.Opportunities.Data.Awards.CanonicalOrgResolver>(),
        sp.GetService<ILogger<Kor.Opportunities.Data.Awards.SqlOpportunityAwardStore>>()));
builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.IVendorSiteCrawlStore>(sp =>
    new Kor.Opportunities.Data.Awards.SqlVendorSiteCrawlStore(Cs(sp)));
builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.ICanonicalOrgStore>(sp =>
    new Kor.Opportunities.Data.Awards.SqlCanonicalOrgStore(Cs(sp)));
builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.CanonicalOrgResolver>();
// Intel extractors — single source of truth in IntelExtractorBootstrap.
// Adding a new extractor goes there, not here.
foreach (var ex in IntelExtractorBootstrap.GetDefaultExtractors())
{
    var captured = ex;
    builder.Services.AddSingleton<IIntelExtractor>(_ => captured);
}
builder.Services.AddSingleton<DefaultIntelExtractor>();
builder.Services.AddSingleton<IntelExtractorRegistry>();
builder.Services.AddSingleton<IntelPersistenceService>(sp => new IntelPersistenceService(
    Cs(sp),
    sp.GetRequiredService<ILogger<IntelPersistenceService>>()));
builder.Services.AddSingleton<IntelReadService>(sp => new IntelReadService(Cs(sp)));
builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.IEnrichmentTrackingStore>(sp =>
    new Kor.Opportunities.Data.Awards.SqlEnrichmentTrackingStore(
        Cs(sp),
        sp.GetRequiredService<IntelExtractorRegistry>(),
        sp.GetRequiredService<IntelPersistenceService>(),
        sp.GetRequiredService<ILogger<Kor.Opportunities.Data.Awards.SqlEnrichmentTrackingStore>>()));
builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.EnrichmentDispatcher>();

builder.Services.AddHttpClient<Kor.Opportunities.Data.Awards.BcRegistryProvider>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(20);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("KOR-Operations-BD-Enrichment/1.0 (+ilalonde@korstructural.com)");
})
.AddPolicyHandler((sp, _) => RetryPolicy(sp, "BcRegistry"));

builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.IEnrichmentProvider>(sp =>
    sp.GetRequiredService<Kor.Opportunities.Data.Awards.BcRegistryProvider>());

builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.INewsStore>(sp =>
    new Kor.Opportunities.Data.Awards.SqlNewsStore(Cs(sp)));

builder.Services.AddHttpClient(nameof(Kor.Opportunities.Data.Awards.NewsFeedPollService), c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("KOR-Operations-BD-NewsBot/1.0 (+ilalonde@korstructural.com)");
})
.AddPolicyHandler((sp, _) => RetryPolicy(sp, "NewsFeedPoll"));
builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.NewsFeedPollService>(sp =>
{
    var options = sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>().Value;
    var http = sp.GetRequiredService<IHttpClientFactory>()
        .CreateClient(nameof(Kor.Opportunities.Data.Awards.NewsFeedPollService));
    return new Kor.Opportunities.Data.Awards.NewsFeedPollService(
        http,
        sp.GetRequiredService<Kor.Opportunities.Data.Awards.INewsStore>(),
        sp.GetRequiredService<ILogger<Kor.Opportunities.Data.Awards.NewsFeedPollService>>(),
        options.IngestionMaxBytesPerResponse,
        options.NewsFeedMaxItemsPerFeed);
});

builder.Services.AddHttpClient<Kor.Opportunities.Data.Awards.NewsMentionClassifier>(c =>
{
    c.Timeout = TimeSpan.FromMinutes(2);
})
.AddPolicyHandler((sp, _) => RetryPolicy(sp, "NewsMentionClassifier"));

builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.NewsMentionClassifier>(sp =>
{
    var newsStore = sp.GetRequiredService<Kor.Opportunities.Data.Awards.INewsStore>();
    var resolver = sp.GetRequiredService<Kor.Opportunities.Data.Awards.CanonicalOrgResolver>();
    var http = sp.GetRequiredService<IHttpClientFactory>()
        .CreateClient(nameof(Kor.Opportunities.Data.Awards.NewsMentionClassifier));
    var options = sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>().Value;
    var apiKey = !string.IsNullOrWhiteSpace(options.AnthropicApiKey)
        ? options.AnthropicApiKey
        : (Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY") ?? string.Empty);
    // Bound options value (appsettings or KOR_OPPORTUNITIES_AGENTENRICHMENTMODEL
    // env var) — uniform with AwardAgentEnrichmentService instead of a raw
    // env-var read that ignored appsettings.
    var model = options.AgentEnrichmentModel;
    var logger = sp.GetRequiredService<ILogger<Kor.Opportunities.Data.Awards.NewsMentionClassifier>>();
    return new Kor.Opportunities.Data.Awards.NewsMentionClassifier(
        newsStore,
        resolver,
        http,
        apiKey,
        model,
        logger);
});

builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.IBuildingPermitStore>(sp =>
    new Kor.Opportunities.Data.Awards.SqlBuildingPermitStore(Cs(sp)));

builder.Services.AddHttpClient(nameof(Kor.Opportunities.Data.Awards.VancouverOpenDataPermitAdapter), c =>
{
    c.Timeout = TimeSpan.FromMinutes(5);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("KOR-Operations-BD-Permits/1.0 (+ilalonde@korstructural.com)");
})
.AddPolicyHandler((sp, _) => RetryPolicy(sp, "VancouverPermits"));
builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.VancouverOpenDataPermitAdapter>(sp =>
{
    var options = sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>().Value;
    var http = sp.GetRequiredService<IHttpClientFactory>()
        .CreateClient(nameof(Kor.Opportunities.Data.Awards.VancouverOpenDataPermitAdapter));
    return new Kor.Opportunities.Data.Awards.VancouverOpenDataPermitAdapter(
        http,
        sp.GetRequiredService<Kor.Opportunities.Data.Awards.IBuildingPermitStore>(),
        sp.GetRequiredService<Kor.Opportunities.Data.Awards.CanonicalOrgResolver>(),
        sp.GetRequiredService<ILogger<Kor.Opportunities.Data.Awards.VancouverOpenDataPermitAdapter>>(),
        options.IngestionMaxBytesPerResponse,
        options.VancouverPermitsMaxRowsPerRun);
});

builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.BuildingPermitsImportService>();
builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.IKorPursuitStore>(sp =>
    new Kor.Opportunities.Data.Awards.SqlKorPursuitStore(Cs(sp)));
builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.IKorClientBdIntelligenceStore>(sp =>
    new Kor.Opportunities.Data.Awards.SqlKorClientBdIntelligenceStore(Cs(sp)));
builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.VendorSiteCrawlService>();
builder.Services.AddHttpClient(nameof(Kor.Opportunities.Data.Awards.VendorSiteExtractionService), c =>
{
    c.Timeout = TimeSpan.FromMinutes(2);
})
.AddPolicyHandler((sp, _) => RetryPolicy(sp, "VendorSiteExtraction"));
builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.VendorSiteExtractionService>(sp =>
{
    var crawlStore = sp.GetRequiredService<Kor.Opportunities.Data.Awards.IVendorSiteCrawlStore>();
    var awardStore = sp.GetRequiredService<Kor.Opportunities.Data.Awards.IOpportunityAwardStore>();
    var http = sp.GetRequiredService<IHttpClientFactory>()
        .CreateClient(nameof(Kor.Opportunities.Data.Awards.VendorSiteExtractionService));
    var options = sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>().Value;
    var apiKey = !string.IsNullOrWhiteSpace(options.AnthropicApiKey)
        ? options.AnthropicApiKey
        : (Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY") ?? string.Empty);
    // Bound options value (appsettings or KOR_OPPORTUNITIES_AGENTENRICHMENTMODEL
    // env var) — uniform with AwardAgentEnrichmentService instead of a raw
    // env-var read that ignored appsettings.
    var model = options.AgentEnrichmentModel;
    var logger = sp.GetRequiredService<ILogger<Kor.Opportunities.Data.Awards.VendorSiteExtractionService>>();
    return new Kor.Opportunities.Data.Awards.VendorSiteExtractionService(
        crawlStore,
        awardStore,
        http,
        apiKey,
        model,
        logger);
});
            builder.Services.AddSingleton<Kor.Opportunities.Data.Bids.IOpportunityBidStore>(sp =>
                new Kor.Opportunities.Data.Bids.SqlOpportunityBidStore(Cs(sp)));
            builder.Services.AddSingleton<IScoringProfileStore>(sp => new SqlScoringProfileStore(Cs(sp)));
            builder.Services.AddSingleton<IScoringOptionsAccessor, ScoringOptionsAccessor>();
            // Scoring remains rules-only in the Worker; won-project history uses
            // a separate capability-detected accessor below.
            builder.Services.AddSingleton<IDeltekClientFactsAccessor, NullDeltekClientFactsAccessor>();
            if (deltekAvailable)
            {
                builder.Services.AddMemoryCache();
                builder.Services.AddSingleton<DeltekKorWonProjectAccessor>(sp =>
                {
                    var options = sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>().Value;
                    var dsn = string.IsNullOrWhiteSpace(options.DeltekDsn) ? "Deltek" : options.DeltekDsn;
                    var factory = new VpOdbcDsnFactory(
                        dsn,
                        options.DeltekUser,
                        options.DeltekPassword,
                        () => new Dictionary<string, string>());
                    return new DeltekKorWonProjectAccessor(factory, options.DeltekCatalog);
                });
                builder.Services.AddSingleton<IKorWonProjectAccessor>(sp =>
                    new CachingKorWonProjectAccessor(
                        sp.GetRequiredService<DeltekKorWonProjectAccessor>(),
                        sp.GetRequiredService<IMemoryCache>()));
                builder.Services.AddSingleton<IKorPursuitDeltekAccessor>(sp =>
                {
                    var options = sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>().Value;
                    var dsn = string.IsNullOrWhiteSpace(options.DeltekDsn) ? "Deltek" : options.DeltekDsn;
                    var factory = new VpOdbcDsnFactory(
                        dsn,
                        options.DeltekUser,
                        options.DeltekPassword,
                        () => new Dictionary<string, string>());
                    return new DeltekKorPursuitDeltekAccessor(factory, options.DeltekCatalog);
                });
            }
            else
            {
                builder.Services.AddSingleton<IKorWonProjectAccessor, NullKorWonProjectAccessor>();
                builder.Services.AddSingleton<IKorPursuitDeltekAccessor, NullKorPursuitDeltekAccessor>();
            }
            builder.Services.AddSingleton<IOpportunityScoringService, RuleBasedOpportunityScoringService>();

            // Ingestion: dispatcher fans out to a provider keyed by SourceType. Add new
            // providers here as we add sources (RSS, JSON APIs, IMAP, etc.).
            builder.Services.AddHttpClient(nameof(GenericCsvOpportunityProvider), c =>
            {
                // Source-side timeout is enforced inside the provider via a linked
                // CancellationTokenSource — keep the HttpClient default so a sane fallback exists.
                c.Timeout = TimeSpan.FromSeconds(120);
            })
            .AddPolicyHandler((sp, _) => RetryPolicy(sp, "GenericCsvOpportunity"));
            builder.Services.AddSingleton<GenericCsvOpportunityProvider>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>().Value;
                var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GenericCsvOpportunityProvider));
                var logger = sp.GetRequiredService<ILogger<GenericCsvOpportunityProvider>>();
                return new GenericCsvOpportunityProvider(
                    http,
                    logger,
                    options.IngestionMaxBytesPerResponse,
                    options.GenericCsvMaxRowsPerRun);
            });
            builder.Services.AddSingleton<IOpportunityProvider>(sp =>
                sp.GetRequiredService<GenericCsvOpportunityProvider>());
            builder.Services.AddHttpClient(nameof(GenericCsvAwardProvider), c =>
            {
                c.Timeout = TimeSpan.FromSeconds(120);
            })
            .AddPolicyHandler((sp, _) => RetryPolicy(sp, "GenericCsvAward"));
            builder.Services.AddSingleton<GenericCsvAwardProvider>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>().Value;
                var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GenericCsvAwardProvider));
                var logger = sp.GetRequiredService<ILogger<GenericCsvAwardProvider>>();
                return new GenericCsvAwardProvider(
                    http,
                    logger,
                    options.IngestionMaxBytesPerResponse,
                    options.GenericCsvMaxRowsPerRun);
            });
            builder.Services.AddSingleton<IAwardProvider>(sp =>
                sp.GetRequiredService<GenericCsvAwardProvider>());
            builder.Services.AddHttpClient(nameof(GenericJsonAwardProvider), c =>
            {
                c.Timeout = TimeSpan.FromSeconds(120);
            })
            .AddPolicyHandler((sp, _) => RetryPolicy(sp, "GenericJsonAward"));
            builder.Services.AddSingleton<GenericJsonAwardProvider>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>().Value;
                var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GenericJsonAwardProvider));
                var logger = sp.GetRequiredService<ILogger<GenericJsonAwardProvider>>();
                return new GenericJsonAwardProvider(
                    http,
                    logger,
                    options.IngestionMaxBytesPerResponse);
            });
            builder.Services.AddSingleton<IAwardProvider>(sp =>
                sp.GetRequiredService<GenericJsonAwardProvider>());
            builder.Services.AddHttpClient(nameof(GenericJsonOpportunityProvider), c =>
            {
                c.Timeout = TimeSpan.FromSeconds(120);
            })
            .AddPolicyHandler((sp, _) => RetryPolicy(sp, "GenericJsonOpportunity"));
            builder.Services.AddSingleton<GenericJsonOpportunityProvider>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>().Value;
                var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GenericJsonOpportunityProvider));
                var logger = sp.GetRequiredService<ILogger<GenericJsonOpportunityProvider>>();
                return new GenericJsonOpportunityProvider(
                    http,
                    logger,
                    options.IngestionMaxBytesPerResponse,
                    options.GenericJsonMaxItemsPerRun);
            });
            builder.Services.AddSingleton<IOpportunityProvider>(sp =>
                sp.GetRequiredService<GenericJsonOpportunityProvider>());
            builder.Services.AddHttpClient(nameof(AbMajorProjectsInventoryProvider), c =>
            {
                c.Timeout = TimeSpan.FromMinutes(5);
            })
            .AddPolicyHandler((sp, _) => RetryPolicy(sp, "AbMajorProjectsInventory"));
            builder.Services.AddSingleton<AbMajorProjectsInventoryProvider>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>().Value;
                var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(AbMajorProjectsInventoryProvider));
                var logger = sp.GetRequiredService<ILogger<AbMajorProjectsInventoryProvider>>();
                return new AbMajorProjectsInventoryProvider(
                    http,
                    Cs(sp),
                    sp.GetRequiredService<Kor.Opportunities.Data.Awards.CanonicalOrgResolver>(),
                    logger,
                    options.IngestionMaxBytesPerResponse);
            });
            builder.Services.AddSingleton<IOpportunityProvider>(sp =>
                sp.GetRequiredService<AbMajorProjectsInventoryProvider>());
            builder.Services.AddHttpClient(nameof(BcMajorProjectsInventoryProvider), c =>
            {
                c.Timeout = TimeSpan.FromMinutes(5);
            })
            .AddPolicyHandler((sp, _) => RetryPolicy(sp, "BcMajorProjectsInventory"));
            builder.Services.AddSingleton<BcMajorProjectsInventoryProvider>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>().Value;
                var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(BcMajorProjectsInventoryProvider));
                var logger = sp.GetRequiredService<ILogger<BcMajorProjectsInventoryProvider>>();
                return new BcMajorProjectsInventoryProvider(
                    http,
                    Cs(sp),
                    sp.GetRequiredService<Kor.Opportunities.Data.Awards.CanonicalOrgResolver>(),
                    logger,
                    options.IngestionMaxBytesPerResponse);
            });
            builder.Services.AddSingleton<IOpportunityProvider>(sp =>
                sp.GetRequiredService<BcMajorProjectsInventoryProvider>());
            builder.Services.AddHttpClient(nameof(CaSocrataMajorProjectsInventoryProvider), c =>
            {
                c.Timeout = TimeSpan.FromMinutes(5);
            })
            .AddPolicyHandler((sp, _) => RetryPolicy(sp, "CaSocrataMajorProjectsInventory"));
            builder.Services.AddSingleton<CaSocrataMajorProjectsInventoryProvider>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>().Value;
                var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(CaSocrataMajorProjectsInventoryProvider));
                var logger = sp.GetRequiredService<ILogger<CaSocrataMajorProjectsInventoryProvider>>();
                return new CaSocrataMajorProjectsInventoryProvider(
                    http,
                    Cs(sp),
                    sp.GetRequiredService<Kor.Opportunities.Data.Awards.CanonicalOrgResolver>(),
                    logger,
                    options.IngestionMaxBytesPerResponse,
                    defaultAppToken: options.CaSocrataAppToken);
            });
            builder.Services.AddSingleton<IOpportunityProvider>(sp =>
                sp.GetRequiredService<CaSocrataMajorProjectsInventoryProvider>());
            builder.Services.AddHttpClient(nameof(CeqanetMajorProjectsInventoryProvider), c =>
            {
                c.Timeout = TimeSpan.FromMinutes(5);
            })
            .AddPolicyHandler((sp, _) => RetryPolicy(sp, "CeqanetMajorProjectsInventory"));
            builder.Services.AddSingleton<CeqanetMajorProjectsInventoryProvider>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>().Value;
                var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(CeqanetMajorProjectsInventoryProvider));
                var logger = sp.GetRequiredService<ILogger<CeqanetMajorProjectsInventoryProvider>>();
                return new CeqanetMajorProjectsInventoryProvider(
                    http,
                    Cs(sp),
                    sp.GetRequiredService<Kor.Opportunities.Data.Awards.CanonicalOrgResolver>(),
                    logger,
                    options.IngestionMaxBytesPerResponse);
            });
            builder.Services.AddSingleton<IOpportunityProvider>(sp =>
                sp.GetRequiredService<CeqanetMajorProjectsInventoryProvider>());
            builder.Services.AddHttpClient<RssOpportunityProvider>(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(120);
            })
            .AddPolicyHandler((sp, _) => RetryPolicy(sp, "RssOpportunity"));
            builder.Services.AddSingleton<IOpportunityProvider>(sp =>
                sp.GetRequiredService<RssOpportunityProvider>());
            builder.Services.AddHttpClient<CivicInfoHtmlOpportunityProvider>(c =>
            {
                c.Timeout = TimeSpan.FromMinutes(3);
            })
            .AddPolicyHandler((sp, _) => RetryPolicy(sp, "CivicInfoHtml"));
            builder.Services.AddSingleton<IOpportunityProvider>(sp =>
                sp.GetRequiredService<CivicInfoHtmlOpportunityProvider>());

            // Playwright platform - single browser pool shared across all
            // Playwright-driven scrapers.
            builder.Services.AddSingleton<Kor.Opportunities.Data.Ingestion.Scraping.PlaywrightBrowserPool>();

            // BC Bid scraper. Singleton (stateless after construction); registered
            // as IOpportunityProvider so IngestionDispatcher picks it up via SourceType.
            builder.Services.AddSingleton<Kor.Opportunities.Data.Ingestion.Scraping.BcBidCredentials>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>().Value;
                return new Kor.Opportunities.Data.Ingestion.Scraping.BcBidCredentials
                {
                    Username = options.BcBidUsername,
                    Password = options.BcBidPassword,
                };
            });
            builder.Services.AddSingleton<Kor.Opportunities.Data.Ingestion.Scraping.BcBidScraper>();
            builder.Services.AddSingleton<Kor.Opportunities.Core.Ingestion.IOpportunityProvider>(
                sp => sp.GetRequiredService<Kor.Opportunities.Data.Ingestion.Scraping.BcBidScraper>());
            builder.Services.AddSingleton<Kor.Opportunities.Data.Ingestion.Scraping.BcBidHistoricalScraper>();
            builder.Services.AddSingleton<Kor.Opportunities.Core.Ingestion.IOpportunityProvider>(
                sp => sp.GetRequiredService<Kor.Opportunities.Data.Ingestion.Scraping.BcBidHistoricalScraper>());
            builder.Services.AddSingleton<Kor.Opportunities.Data.Ingestion.Scraping.BcBidAwardsScraper>();
            builder.Services.AddSingleton<Kor.Opportunities.Core.Ingestion.IAwardProvider>(
                sp => sp.GetRequiredService<Kor.Opportunities.Data.Ingestion.Scraping.BcBidAwardsScraper>());
            builder.Services.AddSingleton<Kor.Opportunities.Data.Ingestion.Scraping.BcBidUnverifiedBidResultsScraper>(sp =>
                new Kor.Opportunities.Data.Ingestion.Scraping.BcBidUnverifiedBidResultsScraper(
                    sp.GetRequiredService<Kor.Opportunities.Data.Ingestion.Scraping.PlaywrightBrowserPool>(),
                    sp.GetRequiredService<ILogger<Kor.Opportunities.Data.Ingestion.Scraping.BcBidUnverifiedBidResultsScraper>>(),
                    sp.GetRequiredService<Kor.Opportunities.Data.Ingestion.Scraping.BcBidCredentials>(),
                    sp.GetRequiredService<Kor.Opportunities.Data.Bids.IOpportunityBidStore>(),
                    sp.GetRequiredService<Kor.Opportunities.Data.Awards.CanonicalOrgResolver>()));
            builder.Services.AddSingleton<Kor.Opportunities.Core.Ingestion.IAwardProvider>(
                sp => sp.GetRequiredService<Kor.Opportunities.Data.Ingestion.Scraping.BcBidUnverifiedBidResultsScraper>());
            builder.Services.AddSingleton<Kor.Opportunities.Data.Ingestion.Scraping.BidsAndTendersScraper>();
            builder.Services.AddSingleton<Kor.Opportunities.Core.Ingestion.IOpportunityProvider>(
                sp => sp.GetRequiredService<Kor.Opportunities.Data.Ingestion.Scraping.BidsAndTendersScraper>());
            builder.Services.AddSingleton<Kor.Opportunities.Data.Ingestion.Scraping.BidsAndTendersAwardsScraper>();
            builder.Services.AddSingleton<Kor.Opportunities.Core.Ingestion.IAwardProvider>(
                sp => sp.GetRequiredService<Kor.Opportunities.Data.Ingestion.Scraping.BidsAndTendersAwardsScraper>());
            builder.Services.AddSingleton<Kor.Opportunities.Data.Ingestion.Scraping.AlbertaPurchasingScraper>();
            builder.Services.AddSingleton<Kor.Opportunities.Core.Ingestion.IOpportunityProvider>(
                sp => sp.GetRequiredService<Kor.Opportunities.Data.Ingestion.Scraping.AlbertaPurchasingScraper>());
            builder.Services.AddSingleton<Kor.Opportunities.Data.Ingestion.Scraping.AlbertaPurchasingAwardsScraper>();
            builder.Services.AddSingleton<Kor.Opportunities.Core.Ingestion.IAwardProvider>(
                sp => sp.GetRequiredService<Kor.Opportunities.Data.Ingestion.Scraping.AlbertaPurchasingAwardsScraper>());
            // Round 61b: APC InterestedFirms continuous enrichment - shared
            // extractor + per-canonical store. Both the Worker job and the
            // one-shot ApcInterestBackfill tool resolve to the same extractor.
            builder.Services.AddSingleton<Kor.Opportunities.Data.Ingestion.Scraping.ApcInterestExtractor>();
            builder.Services.AddSingleton<Kor.Opportunities.Data.Ingestion.Scraping.BcBidPlanTakerExtractor>();
            builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.IOpportunityInterestedFirmStore>(
                sp =>
                {
                    var opts = sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>().Value;
                    return new Kor.Opportunities.Data.Awards.SqlOpportunityInterestedFirmStore(opts.OpportunitiesDb!);
                });
            builder.Services.AddHttpClient<SamGovOpportunityProvider>(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(180);
            })
            .AddPolicyHandler((sp, _) => RetryPolicy(sp, "SamGov"));
            builder.Services.AddSingleton<IOpportunityProvider>(sp =>
            {
                var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(SamGovOpportunityProvider));
                var logger = sp.GetRequiredService<ILogger<SamGovOpportunityProvider>>();
                var opts = sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>().Value;
                return new SamGovOpportunityProvider(http, logger, opts.SamGovApiKey, opts.SamGovPostedDaysLookback);
            });
            builder.Services.AddHttpClient(nameof(GraphEmailOpportunityProvider), c =>
            {
                c.Timeout = TimeSpan.FromMinutes(2);
            })
            .AddTransientHttpErrorPolicy(p => p
                .OrResult(r => (int)r.StatusCode == 429)
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))));
            builder.Services.AddSingleton<GenericEmailFormatAdapter>();
            builder.Services.AddSingleton<IEmailFormatAdapter>(sp =>
                sp.GetRequiredService<GenericEmailFormatAdapter>());
            builder.Services.AddSingleton<EmailFormatAdapterRegistry>();
            builder.Services.AddSingleton<IOpportunityProvider>(sp =>
            {
                var http = sp.GetRequiredService<IHttpClientFactory>();
                var opts = sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>();
                var registry = sp.GetRequiredService<EmailFormatAdapterRegistry>();
                var logger = sp.GetRequiredService<ILogger<GraphEmailOpportunityProvider>>();

                GraphEmailRuntimeOptions Snapshot() => new(
                    TenantId: opts.Value.GraphEmailTenantId,
                    ClientId: opts.Value.GraphEmailClientId,
                    ClientSecret: opts.Value.GraphEmailClientSecret,
                    UserEmail: opts.Value.GraphEmailUserEmail,
                    MailFolderName: opts.Value.GraphEmailMailFolderName,
                    ProcessedFolderName: opts.Value.GraphEmailProcessedFolderName,
                    MarkAsReadInsteadOfMove: opts.Value.GraphEmailMarkAsReadInsteadOfMove,
                    MaxEmailsPerRun: opts.Value.GraphEmailMaxEmailsPerRun,
                    SmokeTestMode: opts.Value.GraphEmailSmokeTestMode);

                return new GraphEmailOpportunityProvider(http, Snapshot, registry, logger);
            });

            builder.Services.AddSingleton<IIngestionService, IngestionService>();
            builder.Services.AddSingleton<IIngestionDispatcher, IngestionDispatcher>();
            builder.Services.AddSingleton<AwardIngestionService>();
            builder.Services.AddSingleton<EnabledTriggerListener>();
            builder.Services.AddSingleton<LastReportPathListener>();

            /*
             * Quartz cadence design:
             * - Minute offsets keep Playwright, Anthropic, RSS, email, and procurement HTTP jobs from piling up.
             * - Heavy Playwright jobs are BcBid historical/detail/document and vendor crawl; Anthropic jobs are
             *   award enrichment, vendor extraction, and news classification.
             * - High-cadence triggers skip missed ticks after deploy/restart to avoid catch-up storms.
             * - Hourly/daily/2-hour triggers fire once on recovery, then resume their normal cadence.
             * - Environment cron overrides still win; these defaults are only the baseline production schedule.
             */
builder.Services.AddQuartz(q =>
{
    var awardAgentJobKey = new JobKey("AwardAgentEnrichmentJob");
    q.AddJob<Kor.Opportunities.Worker.Services.AwardAgentEnrichmentJob>(
        opts => opts.WithIdentity(awardAgentJobKey));

    q.AddTrigger(t =>
    {
        // Default cadence: hourly at :07. The job self-skips without Anthropic.
        // Batch 3 × 24h = ~72 rows/day = ~$2/day worst-case when enabled.
        // Tune via AwardAgentEnrichmentCronSchedule env var.
        var cron = builder.Configuration["AwardAgentEnrichmentCronSchedule"] ?? "0 7 * * * ?";
        t.ForJob(awardAgentJobKey)
       .WithIdentity("AwardAgentEnrichmentTrigger")
       .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionFireAndProceed());
  });

  var vendorSiteCrawlKey = new JobKey("VendorSiteCrawlJob");
  q.AddJob<Kor.Opportunities.Worker.Services.VendorSiteCrawlJob>(opts => opts.WithIdentity(vendorSiteCrawlKey));

  q.AddTrigger(t =>
  {
      // Default cadence: every 15 min at :05/:20/:35/:50, offset from award enrichment :07 ticks
      // so Playwright runs don't collide with the award-enrichment HTTP calls.
      var cron = builder.Configuration["VendorSiteCrawlCronSchedule"] ?? "0 5/15 * * * ?";
      t.ForJob(vendorSiteCrawlKey)
       .WithIdentity("VendorSiteCrawlTrigger")
       .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionDoNothing());
  });

  var vendorSiteExtractionKey = new JobKey("VendorSiteExtractionJob");
  q.AddJob<Kor.Opportunities.Worker.Services.VendorSiteExtractionJob>(opts => opts.WithIdentity(vendorSiteExtractionKey));

  q.AddTrigger(t =>
  {
      // Default cadence: every 5 min at :02/:07/:12/... offset from crawl :05/:20/:35/:50
      // so the extraction catches a fresh crawl on its next tick.
      var cron = builder.Configuration["VendorSiteExtractionCronSchedule"] ?? "0 2/5 * * * ?";
      t.ForJob(vendorSiteExtractionKey)
       .WithIdentity("VendorSiteExtractionTrigger")
       .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionDoNothing());
  });

  var enrichmentDispatchKey = new JobKey("EnrichmentDispatchJob");
  q.AddJob<Kor.Opportunities.Worker.Services.EnrichmentDispatchJob>(opts => opts.WithIdentity(enrichmentDispatchKey));

  q.AddTrigger(t =>
  {
      // Default cadence: every 10 min at :09/:19/:29/:39/:49/:59
      // (offset from existing enrichment jobs so they don't all pile up at :07).
      var cron = builder.Configuration["EnrichmentDispatchCronSchedule"] ?? "0 9/10 * * * ?";
      t.ForJob(enrichmentDispatchKey)
       .WithIdentity("EnrichmentDispatchTrigger")
       .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionDoNothing());
  });

  var newsFeedKey = new JobKey("NewsFeedPollJob");
  q.AddJob<Kor.Opportunities.Worker.Services.NewsFeedPollJob>(opts => opts.WithIdentity(newsFeedKey));

  q.AddTrigger(t =>
  {
      // Default cadence: every 30 min at :12/:42, offset from other jobs.
      var cron = builder.Configuration["NewsFeedPollCronSchedule"] ?? "0 12/30 * * * ?";
      t.ForJob(newsFeedKey)
       .WithIdentity("NewsFeedPollTrigger")
       .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionDoNothing());
  });

  var newsClassifyKey = new JobKey("NewsMentionClassifyJob");
  q.AddJob<Kor.Opportunities.Worker.Services.NewsMentionClassifyJob>(opts => opts.WithIdentity(newsClassifyKey));

  q.AddTrigger(t =>
  {
      // Default cadence: every 5 min at :03/:08/:13, offset from feed poll.
      var cron = builder.Configuration["NewsClassificationCronSchedule"] ?? "0 3/5 * * * ?";
      t.ForJob(newsClassifyKey)
       .WithIdentity("NewsMentionClassifyTrigger")
       .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionDoNothing());
  });

  var permitsKey = new JobKey("BuildingPermitsImportJob");
  q.AddJob<Kor.Opportunities.Worker.Services.BuildingPermitsImportJob>(opts => opts.WithIdentity(permitsKey));

  q.AddTrigger(t =>
  {
      // Default: 06:30 Pacific daily (offset from SamGov's 06:00 tick).
      // Open data refreshes overnight.
      var cron = builder.Configuration["BuildingPermitsCronSchedule"] ?? "0 30 6 * * ?";
      t.ForJob(permitsKey)
       .WithIdentity("BuildingPermitsImportTrigger")
       .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionFireAndProceed());
  });

  var korProjectSignalKey = new JobKey("CanonicalOrgKorProjectSignalRefreshJob");
  q.AddJob<Kor.Opportunities.Worker.Services.CanonicalOrgKorProjectSignalRefreshJob>(opts => opts.WithIdentity(korProjectSignalKey));

  var dataRetirementKey = new JobKey("DataRetirementJob");
  q.AddJob<Kor.Opportunities.Worker.Services.DataRetirementJob>(opts => opts.WithIdentity(dataRetirementKey));

  q.AddTrigger(t =>
  {
      // Default: 04:30 Pacific daily, before the 05:00 KOR-project signal refresh.
      var cron = builder.Configuration["DataRetirementCronSchedule"] ?? "0 30 4 * * ?";
      t.ForJob(dataRetirementKey)
       .WithIdentity("DataRetirementTrigger")
       .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionFireAndProceed());
  });

  var intelExtractionCatchUpKey = new JobKey("IntelExtractionCatchUpJob");
  q.AddJob<Kor.Opportunities.Worker.Jobs.IntelExtractionCatchUpJob>(opts => opts.WithIdentity(intelExtractionCatchUpKey));

  q.AddTrigger(t =>
  {
      // Default: 03:30 UTC daily, after the main overnight enrichment writers.
      var cron = builder.Configuration["IntelExtractionCatchUpCronSchedule"] ?? "0 30 3 * * ?";
      t.ForJob(intelExtractionCatchUpKey)
       .WithIdentity("IntelExtractionCatchUpTrigger")
       .WithCronSchedule(
           cron,
           cb => cb.InTimeZone(TimeZoneInfo.Utc).WithMisfireHandlingInstructionFireAndProceed());
  });

  // R81: Intel retirement job — soft-deletes stale Intel* rows (not seen
  // in 180 days) and resolved IntelActions (Done/Dismissed for 30+ days).
  // Runs at 02:00 UTC, 90 minutes before the catch-up job (which would
  // otherwise re-extract retired rows).
  var intelRetirementKey = new JobKey("IntelRetirementJob");
  q.AddJob<Kor.Opportunities.Worker.Jobs.IntelRetirementJob>(opts => opts.WithIdentity(intelRetirementKey));

  q.AddTrigger(t =>
  {
      var cron = builder.Configuration["IntelRetirementCronSchedule"] ?? "0 0 2 * * ?";
      t.ForJob(intelRetirementKey)
       .WithIdentity("IntelRetirementTrigger")
       .WithCronSchedule(
           cron,
           cb => cb.InTimeZone(TimeZoneInfo.Utc).WithMisfireHandlingInstructionFireAndProceed());
  });

  // CanonicalOrgDedupJob is retired/no-op — real merges run through the
  // supervised tools/BdCanonicalDedup CLI. The weekly trigger was removed
  // (audit 2026-07-01 n2): a scheduled no-op wrote JobRuns rows and showed as
  // a healthy job in the Admin grid, misleading operational dashboards. The
  // job class stays registered (durable) so a manually enqueued legacy trigger
  // still fires the visible skip message instead of erroring.
  var canonicalDedupKey = new JobKey("CanonicalOrgDedupJob");
  q.AddJob<Kor.Opportunities.Worker.Services.CanonicalOrgDedupJob>(opts => opts.WithIdentity(canonicalDedupKey).StoreDurably());

  var dataHealthAuditKey = new JobKey("DataHealthAuditJob");
  q.AddJob<Kor.Opportunities.Worker.Services.DataHealthAuditJob>(opts => opts.WithIdentity(dataHealthAuditKey));

  q.AddTrigger(t =>
  {
      // Default: Sundays at 07:00 Pacific, after the weekly dedup and ingest jobs settle.
      var cron = builder.Configuration["DataHealthAuditCronSchedule"] ?? "0 0 7 ? * SUN";
      t.ForJob(dataHealthAuditKey)
       .WithIdentity("DataHealthAuditTrigger")
       .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionFireAndProceed());
  });

  var jobTriggerPollerKey = new JobKey(nameof(Kor.Opportunities.Worker.Services.JobTriggerPollerJob));
  q.AddJob<Kor.Opportunities.Worker.Services.JobTriggerPollerJob>(opts => opts.WithIdentity(jobTriggerPollerKey));

  q.AddTrigger(t =>
  {
      var cron = builder.Configuration["JobTriggerPollerCronSchedule"] ?? "0/15 * * * * ?";
      t.ForJob(jobTriggerPollerKey)
       .WithIdentity("JobTriggerPollerTrigger")
       .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionFireAndProceed());
  });

  // Round 61b: APC InterestedFirms continuous enrichment. Cadence: every hour
  // at :17 past so it never lines up with the list-scrape passes.
  var apcInterestEnrichmentKey = new JobKey(nameof(Kor.Opportunities.Worker.Services.ApcInterestEnrichmentJob));
  q.AddJob<Kor.Opportunities.Worker.Services.ApcInterestEnrichmentJob>(opts => opts.WithIdentity(apcInterestEnrichmentKey));
  q.AddTrigger(t =>
  {
      var cron = builder.Configuration["ApcInterestEnrichmentCronSchedule"] ?? "0 17 0/1 * * ?";
      t.ForJob(apcInterestEnrichmentKey)
       .WithIdentity("ApcInterestEnrichmentTrigger")
       .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionFireAndProceed());
  });

  var bcBidPlanTakerEnrichmentKey = new JobKey(nameof(Kor.Opportunities.Worker.Services.BcBidPlanTakerEnrichmentJob));
  q.AddJob<Kor.Opportunities.Worker.Services.BcBidPlanTakerEnrichmentJob>(opts => opts.WithIdentity(bcBidPlanTakerEnrichmentKey));
  q.AddTrigger(t =>
  {
      var cron = builder.Configuration["BcBidPlanTakerEnrichmentCronSchedule"] ?? "0 43 0/2 * * ?";
      t.ForJob(bcBidPlanTakerEnrichmentKey)
       .WithIdentity("BcBidPlanTakerEnrichmentTrigger")
       .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionFireAndProceed());
  });

  var bdDeltekLinkDryRunKey = new JobKey("BdDeltekLinkDryRunJob");
  q.AddJob<Kor.Opportunities.Worker.Services.BdDeltekLinkDryRunJob>(opts => opts.WithIdentity(bdDeltekLinkDryRunKey));

  q.AddTrigger(t =>
  {
      // Default: 04:00 Pacific daily. Dry-run only; reviewers apply approved
      // pairs manually before the 05:00 KOR-project signal refresh if needed.
      var cron = builder.Configuration["BdDeltekLinkDryRunCronSchedule"] ?? "0 0 4 * * ?";
      t.ForJob(bdDeltekLinkDryRunKey)
       .WithIdentity("BdDeltekLinkDryRunTrigger")
       .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionFireAndProceed());
  });

  q.AddTrigger(t =>
  {
      // Default: 05:00 Pacific daily, before SamGov's 06:00 and permits' 06:30 ticks.
      var cron = builder.Configuration["CanonicalOrgKorProjectSignalRefreshCronSchedule"] ?? "0 0 5 * * ?";
      t.ForJob(korProjectSignalKey)
       .WithIdentity("CanonicalOrgKorProjectSignalRefreshTrigger")
       .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionFireAndProceed());
  });

  var bdResearchQueueBuilderKey = new JobKey("BdResearchQueueBuilderJob");
  q.AddJob<Kor.Opportunities.Worker.Services.BdResearchQueueBuilderJob>(opts => opts.WithIdentity(bdResearchQueueBuilderKey));

  q.AddTrigger(t =>
  {
      // Default: 21:30 Pacific daily — the ported dev-box nightly-refresh
      // cadence; batches are ready for the evening's drain sessions and the
      // QueueRefreshReport row is fresh for the 6am morning email.
      var cron = builder.Configuration["BdResearchQueueBuilderCronSchedule"] ?? "0 30 21 * * ?";
      t.ForJob(bdResearchQueueBuilderKey)
       .WithIdentity("BdResearchQueueBuilderTrigger")
       .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionFireAndProceed());
  });

  var bdResearchExecutorKey = new JobKey("BdResearchExecutorJob");
  q.AddJob<Kor.Opportunities.Worker.Jobs.BdResearchExecutorJob>(opts => opts.WithIdentity(bdResearchExecutorKey));

  q.AddTrigger(t =>
  {
      // Default: 07:00 Pacific daily. 1 hour after BdResearchQueueBuilderJob (06:00 Pacific),
      // so any queue-builder freshness improvements are visible by the time the executor picks
      // its batch. The executor self-throttles via MaxOrgsPerRun (default 3) and StalenessDays
      // (default 21)  see BdResearchExecutorOptions for cost controls.
      var cron = builder.Configuration["BdResearchExecutorCronSchedule"] ?? "0 0 7 * * ?";
      t.ForJob(bdResearchExecutorKey)
       .WithIdentity("BdResearchExecutorTrigger")
       .WithCronSchedule(
           cron,
           cb => cb.InTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"))
                   .WithMisfireHandlingInstructionFireAndProceed());
  });

  var bdProjectResearchExecutorKey = new JobKey("BdProjectResearchExecutorJob");
  q.AddJob<Kor.Opportunities.Worker.Jobs.BdProjectResearchExecutorJob>(
      opts => opts.WithIdentity(bdProjectResearchExecutorKey));

  q.AddTrigger(t =>
  {
      // Default: 07:30 Pacific daily, offset 30 minutes after the org executor.
      var cron = builder.Configuration["BdProjectResearchExecutorCronSchedule"] ?? "0 30 7 * * ?";
      t.ForJob(bdProjectResearchExecutorKey)
       .WithIdentity("BdProjectResearchExecutorTrigger")
       .WithCronSchedule(
           cron,
           cb => cb.InTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"))
                   .WithMisfireHandlingInstructionFireAndProceed());
  });

  var bdPersonResearchExecutorKey = new JobKey("BdPersonResearchExecutorJob");
  q.AddJob<Kor.Opportunities.Worker.Jobs.BdPersonResearchExecutorJob>(
      opts => opts.WithIdentity(bdPersonResearchExecutorKey));

  q.AddTrigger(t =>
  {
      // Default: 08:00 Pacific daily, 30 minutes after the project executor.
      var cron = builder.Configuration["BdPersonResearchExecutorCronSchedule"] ?? "0 0 8 * * ?";
      t.ForJob(bdPersonResearchExecutorKey)
       .WithIdentity("BdPersonResearchExecutorTrigger")
       .WithCronSchedule(
           cron,
           cb => cb.InTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"))
                   .WithMisfireHandlingInstructionFireAndProceed());
  });

  var korPursuitDeltekSyncKey = new JobKey("KorPursuitDeltekSyncJob");
  q.AddJob<Kor.Opportunities.Worker.Services.KorPursuitDeltekSyncJob>(opts => opts.WithIdentity(korPursuitDeltekSyncKey));

  q.AddTrigger(t =>
  {
      // Default: 05:30 Pacific daily, after won-project signal and before SamGov.
      var cron = builder.Configuration["KorPursuitDeltekSyncCronSchedule"] ?? "0 30 5 * * ?";
      t.ForJob(korPursuitDeltekSyncKey)
       .WithIdentity("KorPursuitDeltekSyncTrigger")
       .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionFireAndProceed());
  });

  var awardProgramFinderKey = new JobKey("AwardProgramFinderJob");
  q.AddJob<Kor.Opportunities.Worker.Services.AwardProgramFinderJob>(opts => opts.WithIdentity(awardProgramFinderKey));

  q.AddTrigger(t =>
  {
      // Weekly recognition-awards catalog refresh. Self-throttles by freshness
      // so manual re-fires do not spend tokens unnecessarily.
      var cron = builder.Configuration["AwardProgramFinderCronSchedule"] ?? "0 0 5 ? * MON";
      t.ForJob(awardProgramFinderKey)
       .WithIdentity("AwardProgramFinderTrigger")
       .WithCronSchedule(
           cron,
           cb => cb.InTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"))
                   .WithMisfireHandlingInstructionFireAndProceed());
  });

  var abMajorProjectsInventoryKey = new JobKey("AbMajorProjectsInventoryJob");
  q.AddJob<Kor.Opportunities.Worker.Services.AbMajorProjectsInventoryJob>(opts => opts.WithIdentity(abMajorProjectsInventoryKey));

  q.AddTrigger(t =>
  {
      // Default: Sundays at 03:30 Pacific. The source updates monthly; weekly catch-up is idempotent.
      var cron = builder.Configuration["AbMajorProjectsInventoryCronSchedule"] ?? "0 30 3 ? * SUN";
      t.ForJob(abMajorProjectsInventoryKey)
       .WithIdentity("AbMajorProjectsInventoryTrigger")
       .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionFireAndProceed());
  });

  var bcMajorProjectsInventoryKey = new JobKey("BcMajorProjectsInventoryJob");
  q.AddJob<Kor.Opportunities.Worker.Services.BcMajorProjectsInventoryJob>(opts => opts.WithIdentity(bcMajorProjectsInventoryKey));

  q.AddTrigger(t =>
  {
      // Default: Sundays at 04:00 Pacific, offset from AB. DataBC updates quarterly; weekly catch-up is idempotent.
      var cron = builder.Configuration["BcMajorProjectsInventoryCronSchedule"] ?? "0 0 4 ? * SUN";
      t.ForJob(bcMajorProjectsInventoryKey)
       .WithIdentity("BcMajorProjectsInventoryTrigger")
       .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionFireAndProceed());
  });

  var caMajorProjectsInventoryKey = new JobKey("CaMajorProjectsInventoryJob");
  q.AddJob<Kor.Opportunities.Worker.Services.CaMajorProjectsInventoryJob>(opts => opts.WithIdentity(caMajorProjectsInventoryKey));

  q.AddTrigger(t =>
  {
      // Default: Sundays at 04:30 Pacific, offset from AB/BC. Endpoint runs are idempotent.
      var cron = builder.Configuration["CaMajorProjectsInventoryCronSchedule"] ?? "0 30 4 ? * SUN";
      t.ForJob(caMajorProjectsInventoryKey)
       .WithIdentity("CaMajorProjectsInventoryTrigger")
       .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionFireAndProceed());
  });

    var bcBidHistDocJobKey = new JobKey("BcBidHistoricalDocumentDownloadJob");
    q.AddJob<Kor.Opportunities.Worker.Services.BcBidHistoricalDocumentDownloadJob>(
        opts => opts.WithIdentity(bcBidHistDocJobKey));

    q.AddTrigger(t =>
    {
        // Default: every 10 minutes, offset from the enrichment cron so Playwright
        // browser usage doesn't pile up.
        var cron = builder.Configuration["BcBidHistoricalDocumentCronSchedule"] ?? "0 2/10 * * * ?";
        t.ForJob(bcBidHistDocJobKey)
         .WithIdentity("BcBidHistoricalDocumentDownloadTrigger")
         .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionDoNothing());
    });

                var jobKey = new JobKey("CanadaBuysIngestionJob");
                q.AddJob<CanadaBuysIngestionJob>(opts => opts.WithIdentity(jobKey));

                q.AddTrigger(t =>
                {
                    var cron = builder.Configuration["CanadaBuysCronSchedule"] ?? "0 0 0/2 * * ?";
                    t.ForJob(jobKey)
                     .WithIdentity("CanadaBuysIngestionTrigger")
                     .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionFireAndProceed());
                });

                // CanadaBuys "newTenderNotice" delta feed — fires every 2h at :15
                // to align with the source's 2-hour publish cadence (06:15 Eastern start).
                var canadaBuysNewJobKey = new JobKey("CanadaBuysNewIngestionJob");
                q.AddJob<CanadaBuysNewIngestionJob>(opts => opts.WithIdentity(canadaBuysNewJobKey));

                q.AddTrigger(t =>
                {
                    var cron = builder.Configuration["CanadaBuysNewCronSchedule"] ?? "0 15 0/2 * * ?";
                    t.ForJob(canadaBuysNewJobKey)
                     .WithIdentity("CanadaBuysNewIngestionTrigger")
                     .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionFireAndProceed());
                });

                var samGovJobKey = new JobKey("SamGovIngestionJob");
                q.AddJob<SamGovIngestionJob>(opts => opts.WithIdentity(samGovJobKey));

                q.AddTrigger(t =>
                {
                    var cron = builder.Configuration["SamGovCronSchedule"] ?? "0 0 6 * * ?";
                    t.ForJob(samGovJobKey)
                     .WithIdentity("SamGovIngestionTrigger")
                     .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionFireAndProceed());
                });

                var graphEmailJobKey = new JobKey("GraphEmailIngestionJob");
                q.AddJob<GraphEmailIngestionJob>(opts => opts.WithIdentity(graphEmailJobKey));

                q.AddTrigger(t =>
                {
                    var cron = builder.Configuration["GraphEmailCronSchedule"] ?? "0 0/15 * * * ?";
                    t.ForJob(graphEmailJobKey)
                     .WithIdentity("GraphEmailIngestionTrigger")
                     .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionDoNothing());
                });

                // BC Bid Historical archive enrichment — visits each row's DetailUrl,
                // extracts Commodities/Amendments/EstValue/Description, queues docs.
                var bcBidHistEnrichJobKey = new JobKey("BcBidHistoricalEnrichmentJob");
                q.AddJob<BcBidHistoricalEnrichmentJob>(opts => opts.WithIdentity(bcBidHistEnrichJobKey));

                q.AddTrigger(t =>
                {
                    // Default: every 5 minutes. With BcBidHistoricalEnrichmentBatchSize=25
                    // that's ~300 rows/hour; ~9,884-row archive backfills in ~30 hours.
                    var cron = builder.Configuration["BcBidHistoricalEnrichmentCronSchedule"] ?? "0 */5 * * * ?";
                    t.ForJob(bcBidHistEnrichJobKey)
                     .WithIdentity("BcBidHistoricalEnrichmentTrigger")
                     .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionDoNothing());
                });

                // 2026-06-11: daily 6am "quick eye on the whole system" email —
                // new opps, verdict movement, ingest activity, failures,
                // coverage, latest drain-trigger report.
                var morningReportJobKey = new JobKey("BdMorningReportJob");
                q.AddJob<Kor.Opportunities.Worker.Services.Reporting.BdMorningReportJob>(opts => opts.WithIdentity(morningReportJobKey));

                q.AddTrigger(t =>
                {
                    var cron = builder.Configuration["MorningReportCronSchedule"] ?? "0 0 6 * * ?";
                    t.ForJob(morningReportJobKey)
                     .WithIdentity("BdMorningReportTrigger")
                     .WithCronSchedule(cron, cb => cb.WithMisfireHandlingInstructionFireAndProceed());
                });

                q.AddTriggerListener<EnabledTriggerListener>(EverythingMatcher<TriggerKey>.AllTriggers());
                q.AddJobListener<LastReportPathListener>(EverythingMatcher<JobKey>.AllJobs());
            });
            builder.Services.AddSingleton<Kor.Opportunities.Worker.Services.Reporting.GraphMailSender>();
            builder.Services.AddHostedService<JobScheduleRegistryHostedService>();
            builder.Services.AddHostedService<JobScheduleNextFireUpdaterHostedService>();
            builder.Services.AddHostedService<JobRunLoggingListenerHostedService>();
            builder.Services.AddQuartzHostedService(opts =>
            {
                opts.WaitForJobsToComplete = true;
            });

            builder.Services.AddHostedService<HeartbeatBackgroundService>();
            builder.Services.AddHostedService<SourceBootstrapHostedService>();
            builder.Services.AddHostedService<IngestionTriggerPollerBackgroundService>();
            builder.Services.AddHostedService<BdResearchTriggerPollerBackgroundService>();
            builder.Services.AddHostedService<BdPersonResearchTriggerPollerBackgroundService>();
            builder.Services.AddHostedService<OpportunitySourceCronScheduler>();

            using var host = builder.Build();
            Log.Information("=== Kor.Opportunities.Worker capabilities ===");
            Log.Information("  Deltek ODBC: {Status}", deltekAvailable ? "AVAILABLE" : "unavailable");
            Log.Information("  Anthropic: {Status}", string.IsNullOrWhiteSpace(startupAnthropicKey) ? "no key" : "configured");
            Log.Information("  Graph email: {Status}", startupGraphConfigured ? "configured" : "no creds");
            Log.Information("  SAM.gov: {Status}", string.IsNullOrWhiteSpace(startupOptions.SamGovApiKey) ? "no key" : "configured");
            Log.Information("===========================================");
            host.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Kor.Opportunities.Worker host terminated unexpectedly.");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
