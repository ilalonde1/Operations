#nullable enable
using Kor.Operations.FileSync.Service;
using Kor.Operations.FileSync.Service.Alerting;
using Kor.Operations.FileSync.Service.Authentication;
using Kor.Operations.FileSync.Service.ControlPlane;
using Kor.Operations.FileSync.Service.Jobs;
using Kor.Operations.FileSync.Service.Jobs.ConcreteTestReports;
using Kor.Operations.FileSync.Service.Jobs.MoveReportsToEor;
using Kor.Operations.FileSync.Service.Jobs.MoveReportsToToSend;
using Kor.Operations.FileSync.Service.Jobs.RenameReportsUploads;
using Kor.Operations.FileSync.Service.Jobs.Watcher;
using Kor.Operations.FileSync.Service.Jobs.WeeklyPmDeadlines;
using Kor.Operations.FileSync.Service.Logging;
using Kor.Operations.FileSync.Service.Options;
using Kor.Operations.FileSync.Service.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Kiota.Abstractions.Authentication;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// reloadOnChange:false on purpose -- the Graph creds (TenantId/ClientId/
// ClientSecret/DriveId) are bound into singletons (IConfidentialClientApplication,
// GraphServiceClient, GraphFacade) at process start. Hot-reloading appsettings
// would change IOptionsMonitor.CurrentValue but NOT rebuild those singletons,
// so a "rotated" secret would silently keep using the old one until restart.
// Better to fail honestly than to lie about hot-reload.
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.Production.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables(prefix: "KOR_FILESYNC_");

builder.Services.AddWindowsService(o => o.ServiceName = "Kor.Operations.FileSync");

var serilogLogger = SerilogBootstrap.CreateLogger();
Log.Logger = serilogLogger;
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(serilogLogger, dispose: true);

builder.Services
    .AddOptions<FileSyncOptions>()
    .Bind(builder.Configuration)
    .Validate(o => !string.IsNullOrWhiteSpace(o.TenantId), "TenantId required (KOR_FILESYNC_TENANTID).")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId), "ClientId required (KOR_FILESYNC_CLIENTID).")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ClientSecret), "ClientSecret required (KOR_FILESYNC_CLIENTSECRET).")
    .Validate(o => !string.IsNullOrWhiteSpace(o.DriveId), "DriveId required (KOR_FILESYNC_DRIVEID). Without this every job's first Graph call will 404.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.KorTransmittalsDb), "KorTransmittalsDb connection string required (KOR_FILESYNC_KORTRANSMITTALSDB).")
    .ValidateOnStart();

builder.Services.AddSingleton<IConfidentialClientApplication>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<FileSyncOptions>>().Value;
    return ConfidentialClientApplicationBuilder
        .Create(opts.ClientId)
        .WithClientSecret(opts.ClientSecret)
        .WithAuthority(new Uri($"https://login.microsoftonline.com/{opts.TenantId}"))
        .Build();
});

builder.Services.AddSingleton<IAuthenticationProvider, AppOnlyGraphAuthenticationProvider>();
builder.Services.AddSingleton(sp => new GraphServiceClient(sp.GetRequiredService<IAuthenticationProvider>()));
builder.Services.AddSingleton<Kor.Operations.Graph.IGraphFacade>(sp =>
{
    var graph = sp.GetRequiredService<GraphServiceClient>();
    var opts = sp.GetRequiredService<IOptions<FileSyncOptions>>().Value;
    return new Kor.Operations.Graph.GraphFacade(graph, opts.DriveId);
});

builder.Services.AddSingleton<IAlertNotifier, GraphMailAlertNotifier>();

builder.Services.AddSingleton<IControlPlaneStore, SqlControlPlaneStore>();

// NoOp is the registry's fallback for any seeded job that hasn't been
// ported yet (ConcreteTestReports / Move* / Rename* / Watcher).
// Real runners are added one per migration step and fronted by the
// JobRunnerRegistry so triggers always route to *something*.
builder.Services.AddSingleton<NoOpJobRunner>();
builder.Services.AddSingleton<IJobRunner, WeeklyPmDeadlinesRunner>();
builder.Services.AddSingleton<IJobRunner, ConcreteTestReportsRunner>();
builder.Services.AddSingleton<IJobRunner, MoveReportsToEorRunner>();
builder.Services.AddSingleton<IJobRunner, MoveReportsToToSendRunner>();
builder.Services.AddSingleton<IJobRunner, RenameReportsUploadsRunner>();
builder.Services.AddSingleton<IJobRunner, WatcherSyncRunner>();
builder.Services.AddSingleton<IWatcherState, WatcherState>();
builder.Services.AddSingleton<JobRunnerRegistry>();
builder.Services.AddSingleton<JobDispatcher>();

builder.Services.AddFileSyncScheduling();
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<TriggerPoller>();
builder.Services.AddHostedService<WatcherHostedService>();

var host = builder.Build();

// Resolve options once and log a redacted snapshot. Operators rotating secrets
// or DriveId can confirm the new values landed by checking these lines on
// startup -- before this, the only signal was a silent drift later.
{
    var startupOpts = host.Services.GetRequiredService<IOptions<FileSyncOptions>>().Value;
    var startupLog = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    static string Tail(string? s) => string.IsNullOrEmpty(s) ? "(empty)" : s.Length <= 4 ? "***" + s : "***" + s[^4..];
    startupLog.LogInformation(
        "Graph credentials snapshot (singletons; restart required to change): TenantId={TenantTail} ClientId={ClientTail} ClientSecret={SecretTail} DriveId={DriveTail}",
        Tail(startupOpts.TenantId), Tail(startupOpts.ClientId), Tail(startupOpts.ClientSecret), Tail(startupOpts.DriveId));
}

try
{
    host.Run();
}
finally
{
    Log.CloseAndFlush();
}
