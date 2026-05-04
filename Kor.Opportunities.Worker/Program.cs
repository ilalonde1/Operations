#nullable enable
using System;
using System.Net.Http;
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
            builder.Services.AddSingleton<IIngestionRunStore>(sp => new SqlIngestionRunStore(Cs(sp)));
            builder.Services.AddSingleton<IIngestionTriggerStore>(sp => new SqlIngestionTriggerStore(Cs(sp)));
            builder.Services.AddSingleton<IScoringProfileStore>(sp => new SqlScoringProfileStore(Cs(sp)));
            builder.Services.AddSingleton<IScoringOptionsAccessor, ScoringOptionsAccessor>();
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

            builder.Services.AddSingleton<IIngestionService, IngestionService>();
            builder.Services.AddSingleton<IIngestionDispatcher, IngestionDispatcher>();

            // Quartz - one trigger per source. CanadaBuys runs on the configured cron.
            builder.Services.AddQuartz(q =>
            {
                var jobKey = new JobKey("CanadaBuysIngestionJob");
                q.AddJob<CanadaBuysIngestionJob>(opts => opts.WithIdentity(jobKey));

                q.AddTrigger(t =>
                {
                    var cron = builder.Configuration["CanadaBuysCronSchedule"] ?? "0 0 0/2 * * ?";
                    t.ForJob(jobKey)
                     .WithIdentity("CanadaBuysIngestionTrigger")
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
            });
            builder.Services.AddQuartzHostedService(opts =>
            {
                opts.WaitForJobsToComplete = true;
            });

            builder.Services.AddHostedService<HeartbeatBackgroundService>();
            builder.Services.AddHostedService<SourceBootstrapHostedService>();
            builder.Services.AddHostedService<IngestionTriggerPollerBackgroundService>();

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
