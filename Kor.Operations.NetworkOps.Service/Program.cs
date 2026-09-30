#nullable enable
// Kor.Operations.NetworkOps.Service -- the always-on half of KOR NetworkOps
// (docs/KOR-NetworkOps-Design-2026-09-28.md, Phase 2). Runs on KOR-APP01 as a Windows service:
// hourly census, twice-daily health sweep, nightly maintenance, a heartbeat, and a digest of
// what changed. Built on the FileSync pattern: one scheduling catalog, one dispatch path that
// records every run, its own SQL store, Graph mail.
//
//   Kor.Operations.NetworkOps.Service                    run as a service (or console)
//   Kor.Operations.NetworkOps.Service run-once <Job>     run one job now and exit (proof runs)
using Kor.Operations.NetworkOps.Service;
using Kor.Operations.NetworkOps.Service.Alerting;
using Kor.Operations.NetworkOps.Service.Jobs;
using Kor.Operations.NetworkOps.Service.Store;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Serilog;

var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KorOperations", "NetworkOps", "logs");
Directory.CreateDirectory(logDir);
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Quartz", Serilog.Events.LogEventLevel.Information)
    .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(Path.Combine(logDir, "networkops-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    // A service inherits services.exe's environment, read at BOOT: a KOR_NETWORKOPS_* machine variable
    // set later is invisible until APP01 restarts. Take any the process is missing from the machine
    // store itself, so a setting applies on a service restart, not a server reboot.
    foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Machine))
        if (e.Key is string k && k.StartsWith("KOR_NETWORKOPS_", StringComparison.OrdinalIgnoreCase) && Environment.GetEnvironmentVariable(k) is null)
            Environment.SetEnvironmentVariable(k, e.Value as string);

    var runOnce = args.Length >= 2 && args[0].Equals("run-once", StringComparison.OrdinalIgnoreCase) ? args[1] : null;

    var builder = Host.CreateApplicationBuilder(args);
    builder.Configuration.Sources.Clear();
    builder.Configuration
        .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false, reloadOnChange: false)
        .AddEnvironmentVariables(prefix: "KOR_NETWORKOPS_");
    builder.Logging.ClearProviders();
    builder.Logging.AddSerilog(Log.Logger, dispose: false);
    builder.Services.AddWindowsService(o => o.ServiceName = "Kor.Operations.NetworkOps");

    builder.Services.AddOptions<NetworkOpsOptions>().Bind(builder.Configuration)
        .Validate(o => !string.IsNullOrWhiteSpace(o.Db), "Db connection string required (KOR_NETWORKOPS_DB).")
        .Validate(o => !o.AlertsEnabled || (!string.IsNullOrWhiteSpace(o.TenantId) && !string.IsNullOrWhiteSpace(o.ClientId) && !string.IsNullOrWhiteSpace(o.ClientSecret)),
            "AlertsEnabled needs KOR_NETWORKOPS_TENANTID, _CLIENTID and _CLIENTSECRET.")
        .ValidateOnStart();

    // Graph for the digest. Built lazily: with alerts off and no credentials set, nothing here is touched.
    builder.Services.AddSingleton(sp =>
    {
        var o = sp.GetRequiredService<IOptions<NetworkOpsOptions>>().Value;
        var cca = ConfidentialClientApplicationBuilder.Create(string.IsNullOrWhiteSpace(o.ClientId) ? Guid.Empty.ToString() : o.ClientId)
            .WithClientSecret(string.IsNullOrWhiteSpace(o.ClientSecret) ? "unset" : o.ClientSecret)
            .WithTenantId(string.IsNullOrWhiteSpace(o.TenantId) ? "common" : o.TenantId)
            .Build();
        return new GraphServiceClient(new AppOnlyAuthenticationProvider(cca));
    });
    builder.Services.AddSingleton<IDigestSender, DigestSender>();
    builder.Services.AddSingleton<NetworkOpsStore>();
    builder.Services.AddSingleton<JobDispatcher>();
    builder.Services.AddSingleton<Kor.Operations.NetworkOps.Service.Sweep.HealthSweeper>();
    builder.Services.AddSingleton<Kor.Operations.NetworkOps.Service.Power.PowerState>();
    builder.Services.AddSingleton<Kor.Operations.NetworkOps.Service.Power.PowerChainRunner>();

    if (runOnce is null)
    {
        builder.Services.AddNetworkOpsScheduling();
        builder.Services.AddHostedService<HeartbeatService>();
        builder.Services.AddHostedService<Kor.Operations.NetworkOps.Service.Sweep.TriggerPoller>();
        builder.Services.AddHostedService<Kor.Operations.NetworkOps.Service.Api.ApiHost>();
        builder.Services.AddHostedService<Kor.Operations.NetworkOps.Service.Power.PowerWatchService>();
    }
    else
    {
        foreach (var s in SchedulingCatalog.All) builder.Services.AddSingleton(s.JobType);
    }

    using var host = builder.Build();
    var opts = host.Services.GetRequiredService<IOptions<NetworkOpsOptions>>().Value;   // runs ValidateOnStart's checks now
    Log.Information("NetworkOps {Version} on {Host}: alerts {Alerts}, {Jobs} scheduled jobs", JobDispatcher.Version, Environment.MachineName,
        opts.AlertsEnabled ? "ON" : "off (digests written to " + DigestSender.DigestDirectory + ")", SchedulingCatalog.All.Count);

    // The learning layer needs migration 002. Say so plainly and stop, rather than fail mid-sweep.
    if (!await host.Services.GetRequiredService<NetworkOpsStore>().LearningSchemaPresentAsync(CancellationToken.None))
    {
        Log.Fatal("KorNetworkOps is missing the learning-layer tables: run db/KorNetworkOps/002_LearningLayerAndCommandCenter.sql as sa, then start the service again.");
        return 2;
    }

    if (runOnce is not null)
    {
        var entry = SchedulingCatalog.All.FirstOrDefault(s => s.Name.Equals(runOnce, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"No job '{runOnce}'. Jobs: {string.Join(", ", SchedulingCatalog.All.Select(s => s.Name))}");
        var job = (INetworkOpsJob)host.Services.GetRequiredService(entry.JobType);
        await host.Services.GetRequiredService<JobDispatcher>().RunAsync(job, CancellationToken.None);
        await Kor.Operations.NetworkOps.Transport.OnTargetChannel.DrainAsync();
        return 0;
    }

    await host.RunAsync();
    // One-shot services still being released by remote SCMs: let them finish (bounded) so a
    // restart never strands a KorRun service on a workstation.
    await Task.WhenAny(Kor.Operations.NetworkOps.Transport.OnTargetChannel.DrainAsync(), Task.Delay(TimeSpan.FromSeconds(45)));
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "NetworkOps stopped");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
