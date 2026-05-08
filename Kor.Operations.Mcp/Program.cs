using System.Reflection;
using Kor.Operations.Mcp.Ai;
using Kor.Operations.Mcp.Alerts;
using Kor.Operations.Mcp.Alerts.Rules.Cash;
using Kor.Operations.Mcp.Audit;
using Kor.Operations.Mcp.Auth;
using Kor.Operations.Mcp.Options;
using Kor.Operations.Mcp.Tools;
using Quartz;
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

        // QueryKorDataTool is registered explicitly because it has constructor
        // dependencies and is also called directly from AskService — DI gets
        // both call paths the same instance.
        builder.Services.AddSingleton<QueryKorDataTool>();
        builder.Services.AddSingleton<AskService>();

        // Alert system: rules + repository + runner + Quartz job.
        builder.Services.AddSingleton<AlertRepository>();
        builder.Services.AddSingleton<AlertRunner>();
        builder.Services.AddSingleton<IAlertRule, ArAgingRule>();

        builder.Services.AddQuartz(q =>
        {
            var jobKey = new JobKey("alert-runner");
            q.AddJob<AlertJob>(opts => opts.WithIdentity(jobKey));

            // Mondays 06:00 Pacific. Pacific is configurable via the
            // Mcp:AlertCronSchedule config key; default is the literal cron.
            var cron = builder.Configuration["Mcp:AlertCronSchedule"] ?? "0 0 6 ? * MON";
            q.AddTrigger(t => t
                .ForJob(jobKey)
                .WithIdentity("alert-runner-weekly")
                .WithCronSchedule(cron, x => x.InTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"))));
        });
        builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);

        // Anthropic HTTP client. Long timeout because the LLM loop can chain
        // multiple tool calls before producing a final answer.
        builder.Services.AddHttpClient("anthropic", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
        });

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

        // /ask — primary entry for the WPF AI panel. Plain English in,
        // plain English out. Server holds the Anthropic key, runs the LLM
        // loop, dispatches tool calls (currently just query_kor_data), and
        // returns the final answer + token usage so the caller can show cost.
        app.MapPost("/ask", async (AskRequest req, HttpContext http, AskService svc, CancellationToken ct) =>
        {
            // BasicAuthMiddleware stashes the verified UPN on HttpContext.Items.
            // Server overwrites whatever the client may have sent so per-user
            // concurrency + audit always reflects the authenticated identity.
            var upn = http.Items.TryGetValue("UserUpn", out var u) ? u as string : null;
            var resp = await svc.AskAsync(req with { UserUpn = upn }, ct).ConfigureAwait(false);
            return Results.Ok(resp);
        });

        app.MapGet("/alerts/active", async (AlertRepository repo, CancellationToken ct) =>
        {
            var alerts = await repo.GetActiveAsync(ct).ConfigureAwait(false);
            return Results.Ok(alerts);
        });

        app.MapGet("/alerts/recent", async (int? days, AlertRepository repo, CancellationToken ct) =>
        {
            var alerts = await repo.GetRecentAsync(days ?? 14, ct).ConfigureAwait(false);
            return Results.Ok(alerts);
        });

        app.MapPost("/alerts/{id:long}/acknowledge", async (long id, HttpContext http, AlertRepository repo, CancellationToken ct) =>
        {
            var upn = http.Items.TryGetValue("UserUpn", out var u) ? u as string : null;
            await repo.AcknowledgeAsync(id, upn ?? "unknown", ct).ConfigureAwait(false);
            return Results.NoContent();
        });

        app.MapPost("/alerts/run-now", async (AlertRunner runner, CancellationToken ct) =>
        {
            await runner.RunAllAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        });

        // MCP wire endpoint. Kept available so future external clients
        // (Claude Desktop, Outlook add-in, etc.) can use the same tool
        // catalog without needing the WPF app's /ask shape.
        app.MapMcp();

        Log.Information("Kor.Operations.Mcp {Version} starting up.", version);
        app.Run();
    }
}
