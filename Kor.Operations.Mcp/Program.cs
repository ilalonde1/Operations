using System.Reflection;
using Kor.Operations.Mcp.Ai;
using Kor.Operations.Mcp.Alerts;
using Kor.Operations.Mcp.Alerts.Rules.Cash;
using Kor.Operations.Mcp.Alerts.Rules.Legal;
using Kor.Operations.Mcp.Audit;
using Kor.Operations.Mcp.Auth;
using Kor.Operations.Mcp.Brief;
using Kor.Operations.Mcp.Collections;
using Kor.Operations.Mcp.CooCard;
using Kor.Operations.Mcp.Options;
using Kor.Operations.Mcp.Tools;
using Quartz;
using Serilog;

namespace Kor.Operations.Mcp;

public static class Program
{
    public static void Main(string[] args)
    {
        // Windows Services launch with CWD=C:\Windows\System32, so any relative
        // path (Serilog's "Logs/mcp-.log", appsettings file probes) resolves
        // against System32. Anchor everything to the install dir so logs land
        // next to the binary.
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

        var builder = WebApplication.CreateBuilder(args);

        // UseWindowsService is a no-op when run from console (dev),
        // and registers the service lifetime when launched by SCM (prod).
        builder.Host.UseWindowsService(o => o.ServiceName = "Kor.Operations.Mcp");

        builder.Host.UseSerilog((ctx, lc) => lc
            .ReadFrom.Configuration(ctx.Configuration)
            .Enrich.FromLogContext());

        builder.Services.Configure<McpOptions>(builder.Configuration.GetSection("Mcp"));

        // Deltek ODBC + Financials options - mirrors WPF FinancialsModule.cs.
        var deltekOdbcSection = builder.Configuration.GetSection("DeltekOdbc");
        var financialsSection = builder.Configuration.GetSection("Financials");
        var deltekOdbcOptions = new Kor.Operations.App.Options.DeltekOdbcOptions
        {
            Dsn = deltekOdbcSection["Dsn"] ?? "Deltek",
            User = deltekOdbcSection["User"] ?? "",
            Password = deltekOdbcSection["Password"] ?? "",
            Catalog = deltekOdbcSection["Catalog"] ?? "",
        };
        var financialsOptions = new Kor.Operations.App.Options.FinancialsOptions
        {
            BilledRevenueAccounts = financialsSection["BilledRevenueAccounts"] ?? "",
            BilledExpenseAccountRanges = financialsSection["BilledExpenseAccountRanges"] ?? "",
            BilledOtherIncomeAccountRanges = financialsSection["BilledOtherIncomeAccountRanges"] ?? "",
            BilledExpenseAccountExcludes = financialsSection["BilledExpenseAccountExcludes"] ?? "",
            BilledExpenseAccountIncludes = financialsSection["BilledExpenseAccountIncludes"] ?? "",
            BilledOtherIncomeAccountExcludes = financialsSection["BilledOtherIncomeAccountExcludes"] ?? "",
            BilledOtherIncomeAccountIncludes = financialsSection["BilledOtherIncomeAccountIncludes"] ?? "",
            BilledUsdToCadRate = financialsSection["BilledUsdToCadRate"] ?? "",
            BilledDefaultOrg = financialsSection["BilledDefaultOrg"] ?? "",
            PnLGlFlipSign = financialsSection["PnLGlFlipSign"] ?? "",
            PnLGlTableNameLike = financialsSection["PnLGlTableNameLike"] ?? "",
            PnLIncomeGroupTypes = financialsSection["PnLIncomeGroupTypes"] ?? "",
            PnLExpenseGroupTypes = financialsSection["PnLExpenseGroupTypes"] ?? "",
        };
        builder.Services.AddSingleton(deltekOdbcOptions);
        builder.Services.AddSingleton(financialsOptions);
        builder.Services.AddSingleton<Kor.Operations.Financials.BilledFinancialsService>();
        builder.Services.AddSingleton<Kor.Operations.Financials.GlProfitLossService>();
        builder.Services.AddSingleton<Kor.Operations.Financials.CashFinancialsService>();
        builder.Services.AddSingleton<Kor.Operations.Mcp.Tools.BilledPnLTool>();
        builder.Services.AddSingleton<Kor.Operations.Mcp.Tools.GlPnLTool>();
        builder.Services.AddSingleton<Kor.Operations.Mcp.Tools.CashTool>();

        builder.Services.AddSingleton<AuditLogger>();

        // QueryKorDataTool is registered explicitly because it has constructor
        // dependencies and is also called directly from AskService — DI gets
        // both call paths the same instance.
        builder.Services.AddSingleton<QueryKorDataTool>();
        builder.Services.AddSingleton<AskService>();

        // Alert system: rules + repository + runner + Quartz job.
        builder.Services.AddSingleton<AlertRepository>();
        builder.Services.AddSingleton<AlertRunner>();
        builder.Services.AddSingleton<CooBriefRepository>();
        builder.Services.AddSingleton<CooBriefGenerator>();
        builder.Services.AddSingleton<CooCardRepository>();
        builder.Services.AddSingleton<CooCardGenerator>();
        builder.Services.AddSingleton<CollectionsRepository>();
        builder.Services.AddSingleton<IAlertRule, ArAgingRule>();
        builder.Services.AddSingleton<IAlertRule, StaleCollectionsCaseRule>();
        builder.Services.AddSingleton<IAlertRule, LongRunningOpenCaseRule>();
        builder.Services.AddSingleton<IAlertRule, WrittenOffInvoicePaidRule>();
        builder.Services.AddSingleton<IAlertRule, LienExpiryRule>();

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

        app.MapGet("/coo-brief/latest", async (CooBriefRepository repo, CancellationToken ct) =>
        {
            var rows = await repo.GetLatestWeekAsync(ct).ConfigureAwait(false);
            return Results.Ok(rows);
        });

        app.MapGet("/coo-brief/history", async (int? weeks, CooBriefRepository repo, CancellationToken ct) =>
        {
            var rows = await repo.GetHistoryAsync(weeks ?? 4, ct).ConfigureAwait(false);
            return Results.Ok(rows);
        });

        app.MapPost("/coo-brief/{id:long}/acknowledge", async (long id, HttpContext http, CooBriefRepository repo, CancellationToken ct) =>
        {
            var upn = http.Items.TryGetValue("UserUpn", out var u) ? u as string : null;
            await repo.AcknowledgeAsync(id, upn ?? "unknown", ct).ConfigureAwait(false);
            return Results.NoContent();
        });

        app.MapPost("/coo-brief/run-now", async (CooBriefGenerator gen, CancellationToken ct) =>
        {
            var today = DateTime.Today;
            var daysSinceMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            var weekOf = today.AddDays(-daysSinceMonday);
            await gen.GenerateForWeekAsync(weekOf, ct).ConfigureAwait(false);
            return Results.NoContent();
        });

        app.MapGet("/coo-card/latest", async (CooCardRepository repo, CancellationToken ct) =>
        {
            var rows = await repo.GetLatestWeekAsync(ct).ConfigureAwait(false);
            return Results.Ok(rows);
        });

        app.MapPost("/coo-card/{id:long}/acknowledge", async (long id, HttpContext http, CooCardRepository repo, CancellationToken ct) =>
        {
            var upn = http.Items.TryGetValue("UserUpn", out var u) ? u as string : null;
            await repo.AcknowledgeAsync(id, upn ?? "unknown", ct).ConfigureAwait(false);
            return Results.NoContent();
        });

        app.MapPost("/coo-card/run-now", async (CooCardGenerator gen, CancellationToken ct) =>
        {
            var today = DateTime.Today;
            var daysSinceMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            var weekOf = today.AddDays(-daysSinceMonday);
            await gen.GenerateForWeekAsync(weekOf, ct).ConfigureAwait(false);
            return Results.NoContent();
        });

        app.MapGet("/collections", async (CollectionsRepository repo, CancellationToken ct) =>
        {
            var rows = await repo.GetAllAsync(ct).ConfigureAwait(false);
            return Results.Ok(rows);
        });

        app.MapGet("/collections/active", async (CollectionsRepository repo, CancellationToken ct) =>
        {
            var rows = await repo.GetActiveAsync(ct).ConfigureAwait(false);
            return Results.Ok(rows);
        });

        app.MapGet("/collections/active-invoices", async (CollectionsRepository repo, CancellationToken ct) =>
        {
            var rows = await repo.GetActiveCaseInvoicesAsync(ct).ConfigureAwait(false);
            return Results.Ok(rows);
        });

        app.MapGet("/collections/by-client/{clientId}", async (string clientId, CollectionsRepository repo, CancellationToken ct) =>
        {
            var row = await repo.GetActiveByClientAsync(clientId, ct).ConfigureAwait(false);
            return row is null ? Results.NotFound() : Results.Ok(row);
        });

        app.MapPost("/collections", async (OpenCollectionsCaseRequest req, HttpContext http, CollectionsRepository repo, CancellationToken ct) =>
        {
            var upn = http.Items.TryGetValue("UserUpn", out var u) ? u as string : null;
            try
            {
                var id = await repo.InsertAsync(
                    req.ClientID, req.Status, req.LegalAmount, req.Notes, req.Invoices ?? Array.Empty<InvoiceRef>(), req.LienExpiryDate, upn ?? "unknown", ct)
                    .ConfigureAwait(false);
                return Results.Ok(new { id });
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
            {
                return Results.Conflict(new { error = "An active collections case already exists for this client, or one of the invoices is already on another case." });
            }
        });

        app.MapGet("/collections/{id:long}", async (long id, CollectionsRepository repo, CancellationToken ct) =>
        {
            var detail = await repo.GetByIdAsync(id, ct).ConfigureAwait(false);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        app.MapGet("/collections/ar-by-client/{clientId}", async (string clientId, CollectionsRepository repo, CancellationToken ct) =>
        {
            var rows = await repo.GetOpenArByClientAsync(clientId, ct).ConfigureAwait(false);
            return Results.Ok(rows);
        });

        app.MapPut("/collections/{id:long}", async (long id, UpdateCollectionsCaseRequest req, HttpContext http, CollectionsRepository repo, CancellationToken ct) =>
        {
            var upn = http.Items.TryGetValue("UserUpn", out var u) ? u as string : null;
            await repo.UpdateAsync(id, req.Status, req.LegalAmount, req.Notes, req.Invoices ?? Array.Empty<InvoiceRef>(), req.LienExpiryDate, upn ?? "unknown", ct)
                .ConfigureAwait(false);
            return Results.NoContent();
        });

        // MCP wire endpoint. Kept available so future external clients
        // (Claude Desktop, Outlook add-in, etc.) can use the same tool
        // catalog without needing the WPF app's /ask shape.
        app.MapMcp();

        Log.Information("Kor.Operations.Mcp {Version} starting up.", version);

        // Surface the Quartz alert-runner next-fire time so a restart that
        // silently broke the cron registration shows up in the startup log
        // rather than waiting until Monday to find out.
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            try
            {
                var schedulerFactory = app.Services.GetRequiredService<ISchedulerFactory>();
                var scheduler = schedulerFactory.GetScheduler().GetAwaiter().GetResult();
                var trigger = scheduler.GetTrigger(new TriggerKey("alert-runner-weekly")).GetAwaiter().GetResult();
                var next = trigger?.GetNextFireTimeUtc();
                if (next is null)
                {
                    Log.Warning("Alert-runner trigger registered but has no next fire time.");
                }
                else
                {
                    Log.Information(
                        "Alert-runner trigger next fire: {NextUtc} UTC ({NextPacific} Pacific).",
                        next.Value.UtcDateTime,
                        TimeZoneInfo.ConvertTimeFromUtc(next.Value.UtcDateTime, TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time")));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to read alert-runner trigger next-fire time.");
            }
        });

        app.Run();
    }
}
