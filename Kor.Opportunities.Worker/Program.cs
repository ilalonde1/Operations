#nullable enable
using Kor.Opportunities.Data.Heartbeat;
using Kor.Opportunities.Worker.Logging;
using Kor.Opportunities.Worker.Options;
using Kor.Opportunities.Worker.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

            // reloadOnChange:false on appsettings — singletons (DB connection, future Quartz JobStore,
            // future Graph client) bind to these values at startup. Hot-reload would silently desync
            // them. Mirrors Kor.Operations.FileSync.Service convention.
            builder.Services
                .AddOptions<OpportunitiesWorkerOptions>()
                .Bind(builder.Configuration)
                .Validate(
                    o => !string.IsNullOrWhiteSpace(o.OpportunitiesDb),
                    "OpportunitiesDb connection string is required (set via KOR_OPPORTUNITIES_OPPORTUNITIESDB env var or appsettings).")
                .ValidateOnStart();

            // SqlHeartbeatStore takes the connection string directly (rather than an Options
            // type) so Kor.Opportunities.Data stays free of any host-specific Options class.
            builder.Services.AddSingleton<IHeartbeatStore>(sp =>
                new SqlHeartbeatStore(sp.GetRequiredService<IOptions<OpportunitiesWorkerOptions>>().Value.OpportunitiesDb));

            builder.Services.AddHostedService<HeartbeatBackgroundService>();

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
