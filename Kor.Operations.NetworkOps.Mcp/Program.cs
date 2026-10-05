using Kor.Operations.NetworkOps.Mcp;
using Kor.Operations.NetworkOps.Service;
using Kor.Operations.NetworkOps.Service.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The NetworkOps MCP server: live, structured NetworkOps context for a Claude session over stdio. A separate process
// from the monitoring service on purpose -- it reads the SAME database through the SAME NetworkOpsStore, so there is
// one source of truth and no new dependency inside the critical service. Run it on a machine that can reach
// KOR-APP01\SQLEXPRESS (KOR-1001 on the LAN) with KOR_NETWORKOPS_DB set to the networkops_app connection string.

var db = Environment.GetEnvironmentVariable("KOR_NETWORKOPS_DB");
if (string.IsNullOrWhiteSpace(db))
{
    await Console.Error.WriteLineAsync("KOR_NETWORKOPS_DB is not set: the NetworkOps MCP server reads the database directly and needs its connection string.");
    return 1;
}

var builder = Host.CreateApplicationBuilder(args);
// stdout is the MCP transport -- EVERY log line must go to stderr, or it corrupts the protocol.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.Configure<NetworkOpsOptions>(o => o.Db = db);
builder.Services.AddSingleton<NetworkOpsStore>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<NetworkOpsMcpTools>();

await builder.Build().RunAsync();
return 0;
