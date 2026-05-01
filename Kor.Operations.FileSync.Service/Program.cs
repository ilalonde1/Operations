#nullable enable
using Kor.Operations.FileSync.Service;
using Kor.Operations.FileSync.Service.Alerting;
using Kor.Operations.FileSync.Service.Authentication;
using Kor.Operations.FileSync.Service.ControlPlane;
using Kor.Operations.FileSync.Service.Jobs;
using Kor.Operations.FileSync.Service.Jobs.ConcreteTestReports;
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

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile("appsettings.Production.json", optional: true, reloadOnChange: true)
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

builder.Services.AddSingleton<IAlertNotifier, GraphMailAlertNotifier>();

builder.Services.AddSingleton<IControlPlaneStore, SqlControlPlaneStore>();

// NoOp is the registry's fallback for any seeded job that hasn't been
// ported yet (ConcreteTestReports / Move* / Rename* / Watcher).
// Real runners are added one per migration step and fronted by the
// JobRunnerRegistry so triggers always route to *something*.
builder.Services.AddSingleton<NoOpJobRunner>();
builder.Services.AddSingleton<IJobRunner, WeeklyPmDeadlinesRunner>();
builder.Services.AddSingleton<IJobRunner, ConcreteTestReportsRunner>();
builder.Services.AddSingleton<JobRunnerRegistry>();
builder.Services.AddSingleton<JobDispatcher>();

builder.Services.AddFileSyncScheduling();
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<TriggerPoller>();

var host = builder.Build();

try
{
    host.Run();
}
finally
{
    Log.CloseAndFlush();
}
