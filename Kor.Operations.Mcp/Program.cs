using System.Reflection;
using Kor.Operations.Mcp.Audit;
using Kor.Operations.Mcp.Auth;
using Kor.Operations.Mcp.Options;
using Serilog;

namespace Kor.Operations.Mcp;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // UseWindowsService is a no-op when run from console (dev),
        // and registers the service lifetime when launched by SCM (prod).
        builder.Host.UseWindowsService(o => o.ServiceName = "Kor.Operations.Mcp");

        builder.Host.UseSerilog((ctx, lc) => lc
            .ReadFrom.Configuration(ctx.Configuration)
            .Enrich.FromLogContext());

        builder.Services.Configure<McpOptions>(builder.Configuration.GetSection("Mcp"));
        builder.Services.AddSingleton<AuditLogger>();

        // MCP server registration. Stateless transport keeps each request
        // self-contained, which fits the "ad-hoc question + tool call"
        // shape of our usage and avoids per-client connection state on the
        // server. WithToolsFromAssembly auto-discovers every type marked
        // [McpServerToolType] in this assembly.
        builder.Services
            .AddMcpServer()
            .WithHttpTransport(o => o.Stateless = true)
            .WithToolsFromAssembly();

        var app = builder.Build();

        // Order matters: auth gates the request first; if accepted, audit
        // wraps the rest of the pipeline so we capture duration and status
        // for both /health (skipped internally) and MCP tool calls.
        app.UseMiddleware<BasicAuthMiddleware>();
        app.UseMiddleware<AuditMiddleware>();

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";

        // /health returns 200 with version + timestamp so the deploy
        // runbook can verify a redeploy by `curl https://.../health`.
        // Exempt from auth + audit so monitoring works without creds.
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            service = "Kor.Operations.Mcp",
            version,
            timestamp = DateTimeOffset.UtcNow.ToString("o"),
        }));

        // Map the MCP endpoint. The framework chooses the path; clients
        // configured with the service URL will discover the right path
        // through the standard MCP handshake.
        app.MapMcp();

        Log.Information("Kor.Operations.Mcp {Version} starting up.", version);
        app.Run();
    }
}
