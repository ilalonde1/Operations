#nullable enable
using System;
using System.Net.Http;
using Kor.Opportunities.Core.Ingestion.EmailAdapters;
using Kor.Opportunities.Core.Ingestion;
using Kor.Opportunities.Core.Scoring;
using Kor.Opportunities.Data.Heartbeat;
using Kor.Opportunities.Data.Ingestion;
using Kor.Opportunities.Data.Ingestion.Providers;
using Kor.Opportunities.Data.Observations;
using Kor.Opportunities.Data.Opportunities;
using Kor.Opportunities.Data.Scoring;
using Kor.Opportunities.Data.Sources;
using Kor.Opportunities.Worker.Logging;
using Kor.Opportunities.Worker.Options;
using Kor.Opportunities.Worker.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Quartz;
using Serilog;

namespace Kor.Opportunities.Worker;

internal static class Program
{
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

            // Stores take the connection string directly (rather than an Options type) so
            // Kor.Opportunities.Data stays free of any host-specific Options class.
            string Cs(IServiceProvider sp) =>
                sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>().Value.OpportunitiesDb;

            builder.Services.AddSingleton<IHeartbeatStore>(sp => new SqlHeartbeatStore(Cs(sp)));
            builder.Services.AddSingleton<IOpportunityStore>(sp => new SqlOpportunityStore(Cs(sp)));
            builder.Services.AddSingleton<IOpportunitySourceStore>(sp => new SqlOpportunitySourceStore(Cs(sp)));
            builder.Services.AddSingleton<IOpportunityObservationStore>(sp => new SqlOpportunityObservationStore(Cs(sp)));
builder.Services.AddSingleton<Kor.Opportunities.Data.HistoricalOpportunities.IHistoricalOpportunityStore>(sp =>
    new Kor.Opportunities.Data.HistoricalOpportunities.SqlHistoricalOpportunityStore(Cs(sp)));
builder.Services.AddSingleton<Kor.Opportunities.Data.HistoricalOpportunities.IHistoricalOpportunityDocumentStore>(sp =>
    new Kor.Opportunities.Data.HistoricalOpportunities.SqlHistoricalOpportunityDocumentStore(Cs(sp)));
builder.Services.AddSingleton<Kor.Opportunities.Data.Ingestion.Scraping.BcBidHistoricalEnrichmentService>();
builder.Services.AddSingleton<Kor.Opportunities.Data.HistoricalOpportunities.BcBidHistoricalDocumentDownloadService>();
builder.Services.AddHttpClient<Kor.Opportunities.Data.Awards.AwardAgentEnrichmentService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(120);
});
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
            builder.Services.AddSingleton<IIngestionTriggerStore>(sp => new SqlIngestionTriggerStore(Cs(sp)));
builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.IOpportunityAwardStore>(sp =>
    new Kor.Opportunities.Data.Awards.SqlOpportunityAwardStore(
        Cs(sp),
        sp.GetRequiredService<Kor.Opportunities.Data.Awards.CanonicalOrgResolver>()));
builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.IVendorSiteCrawlStore>(sp =>
    new Kor.Opportunities.Data.Awards.SqlVendorSiteCrawlStore(Cs(sp)));
builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.ICanonicalOrgStore>(sp =>
    new Kor.Opportunities.Data.Awards.SqlCanonicalOrgStore(Cs(sp)));
builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.CanonicalOrgResolver>();
builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.IEnrichmentTrackingStore>(sp =>
    new Kor.Opportunities.Data.Awards.SqlEnrichmentTrackingStore(Cs(sp)));
builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.EnrichmentDispatcher>();
builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.IKorPursuitStore>(sp =>
    new Kor.Opportunities.Data.Awards.SqlKorPursuitStore(Cs(sp)));
builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.IKorClientBdIntelligenceStore>(sp =>
    new Kor.Opportunities.Data.Awards.SqlKorClientBdIntelligenceStore(Cs(sp)));
builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.VendorSiteCrawlService>();
builder.Services.AddHttpClient(nameof(Kor.Opportunities.Data.Awards.VendorSiteExtractionService), c =>
{
    c.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddSingleton<Kor.Opportunities.Data.Awards.VendorSiteExtractionService>(sp =>
{
    var crawlStore = sp.GetRequiredService<Kor.Opportunities.Data.Awards.IVendorSiteCrawlStore>();
    var awardStore = sp.GetRequiredService<Kor.Opportunities.Data.Awards.IOpportunityAwardStore>();
    var http = sp.GetRequiredService<IHttpClientFactory>()
        .CreateClient(nameof(Kor.Opportunities.Data.Awards.VendorSiteExtractionService));
    var apiKey = Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY") ?? string.Empty;
    var model = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_AGENTENRICHMENTMODEL");
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
            // The Worker has no Deltek ODBC access - fall back to the null accessor
            // so scoring runs the rules-only path. CanadaBuys-ingested rows almost
            // never have DeltekClientId set anyway, so the divergence vs. App-side
            // scoring is essentially zero. Manual+BD-driven rows are scored on the
            // App side via DeltekClientFactsAccessor.
            builder.Services.AddSingleton<IDeltekClientFactsAccessor, NullDeltekClientFactsAccessor>();
            builder.Services.AddSingleton<IOpportunityScoringService, RuleBasedOpportunityScoringService>();

            // Ingestion: dispatcher fans out to a provider keyed by SourceType. Add new
            // providers here as we add sources (RSS, JSON APIs, IMAP, etc.).
            builder.Services.AddHttpClient<GenericCsvOpportunityProvider>(c =>
            {
                // Source-side timeout is enforced inside the provider via a linked
                // CancellationTokenSource — keep the HttpClient default so a sane fallback exists.
                c.Timeout = TimeSpan.FromSeconds(120);
            });
            builder.Services.AddSingleton<IOpportunityProvider>(sp =>
                sp.GetRequiredService<GenericCsvOpportunityProvider>());
            builder.Services.AddHttpClient<GenericJsonOpportunityProvider>(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(120);
            });
            builder.Services.AddSingleton<IOpportunityProvider>(sp =>
                sp.GetRequiredService<GenericJsonOpportunityProvider>());
            builder.Services.AddHttpClient<RssOpportunityProvider>(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(120);
            });
            builder.Services.AddSingleton<IOpportunityProvider>(sp =>
                sp.GetRequiredService<RssOpportunityProvider>());
            builder.Services.AddHttpClient<CivicInfoHtmlOpportunityProvider>(c =>
            {
                c.Timeout = TimeSpan.FromMinutes(3);
            });
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
            builder.Services.AddSingleton<Kor.Opportunities.Data.Ingestion.Scraping.BcBidUnverifiedBidResultsScraper>();
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
            builder.Services.AddHttpClient<SamGovOpportunityProvider>(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(180);
            });
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

            // Quartz - one trigger per source. CanadaBuys runs on the configured cron.
builder.Services.AddQuartz(q =>
{
    var awardAgentJobKey = new JobKey("AwardAgentEnrichmentJob");
    q.AddJob<Kor.Opportunities.Worker.Services.AwardAgentEnrichmentJob>(
        opts => opts.WithIdentity(awardAgentJobKey));

    q.AddTrigger(t =>
    {
        // Job is OFF by default (AwardAgentEnrichmentEnabled=false skips at top of Execute).
        // Cron only fires when explicitly enabled. Default cadence: hourly at :07.
        // Batch 3 × 24h = ~72 rows/day = ~$2/day worst-case when enabled.
        // Tune via AwardAgentEnrichmentCronSchedule env var.
        var cron = builder.Configuration["AwardAgentEnrichmentCronSchedule"] ?? "0 7 * * * ?";
        t.ForJob(awardAgentJobKey)
       .WithIdentity("AwardAgentEnrichmentTrigger")
       .WithCronSchedule(cron);
  });

  var vendorSiteCrawlKey = new JobKey("VendorSiteCrawlJob");
  q.AddJob<Kor.Opportunities.Worker.Services.VendorSiteCrawlJob>(opts => opts.WithIdentity(vendorSiteCrawlKey));

  q.AddTrigger(t =>
  {
      // Off by default (VendorSiteCrawlEnabled=false skips at top of Execute).
      // Default cadence: every 15 min at :05/:20/:35/:50, offset from award enrichment :07 ticks
      // so Playwright runs don't collide with the award-enrichment HTTP calls.
      var cron = builder.Configuration["VendorSiteCrawlCronSchedule"] ?? "0 5/15 * * * ?";
      t.ForJob(vendorSiteCrawlKey)
       .WithIdentity("VendorSiteCrawlTrigger")
       .WithCronSchedule(cron);
  });

  var vendorSiteExtractionKey = new JobKey("VendorSiteExtractionJob");
  q.AddJob<Kor.Opportunities.Worker.Services.VendorSiteExtractionJob>(opts => opts.WithIdentity(vendorSiteExtractionKey));

  q.AddTrigger(t =>
  {
      // Off by default (VendorSiteExtractionEnabled=false skips at top of Execute).
      // Default cadence: every 5 min at :02/:07/:12/... offset from crawl :05/:20/:35/:50
      // so the extraction catches a fresh crawl on its next tick.
      var cron = builder.Configuration["VendorSiteExtractionCronSchedule"] ?? "0 2/5 * * * ?";
      t.ForJob(vendorSiteExtractionKey)
       .WithIdentity("VendorSiteExtractionTrigger")
       .WithCronSchedule(cron);
  });

  var enrichmentDispatchKey = new JobKey("EnrichmentDispatchJob");
  q.AddJob<Kor.Opportunities.Worker.Services.EnrichmentDispatchJob>(opts => opts.WithIdentity(enrichmentDispatchKey));

  q.AddTrigger(t =>
  {
      // Off by default. Default cadence: every 10 min at :09/:19/:29/:39/:49/:59
      // (offset from existing enrichment jobs so they don't all pile up at :07).
      var cron = builder.Configuration["EnrichmentDispatchCronSchedule"] ?? "0 9/10 * * * ?";
      t.ForJob(enrichmentDispatchKey)
       .WithIdentity("EnrichmentDispatchTrigger")
       .WithCronSchedule(cron);
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
         .WithCronSchedule(cron);
    });

                var jobKey = new JobKey("CanadaBuysIngestionJob");
                q.AddJob<CanadaBuysIngestionJob>(opts => opts.WithIdentity(jobKey));

                q.AddTrigger(t =>
                {
                    var cron = builder.Configuration["CanadaBuysCronSchedule"] ?? "0 0 0/2 * * ?";
                    t.ForJob(jobKey)
                     .WithIdentity("CanadaBuysIngestionTrigger")
                     .WithCronSchedule(cron);
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
                     .WithCronSchedule(cron);
                });

                var samGovJobKey = new JobKey("SamGovIngestionJob");
                q.AddJob<SamGovIngestionJob>(opts => opts.WithIdentity(samGovJobKey));

                q.AddTrigger(t =>
                {
                    var cron = builder.Configuration["SamGovCronSchedule"] ?? "0 0 6 * * ?";
                    t.ForJob(samGovJobKey)
                     .WithIdentity("SamGovIngestionTrigger")
                     .WithCronSchedule(cron);
                });

                var graphEmailJobKey = new JobKey("GraphEmailIngestionJob");
                q.AddJob<GraphEmailIngestionJob>(opts => opts.WithIdentity(graphEmailJobKey));

                q.AddTrigger(t =>
                {
                    var cron = builder.Configuration["GraphEmailCronSchedule"] ?? "0 0/15 * * * ?";
                    t.ForJob(graphEmailJobKey)
                     .WithIdentity("GraphEmailIngestionTrigger")
                     .WithCronSchedule(cron);
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
                     .WithCronSchedule(cron);
                });
            });
            builder.Services.AddQuartzHostedService(opts =>
            {
                opts.WaitForJobsToComplete = true;
            });

            builder.Services.AddHostedService<HeartbeatBackgroundService>();
            builder.Services.AddHostedService<SourceBootstrapHostedService>();
            builder.Services.AddHostedService<IngestionTriggerPollerBackgroundService>();
            builder.Services.AddHostedService<OpportunitySourceCronScheduler>();

            using var host = builder.Build();
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
