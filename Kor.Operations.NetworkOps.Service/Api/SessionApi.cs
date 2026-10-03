#nullable enable
using Kor.Operations.NetworkOps.Core.Learning;
using Kor.Operations.NetworkOps.Core.Prompts;
using Kor.Operations.NetworkOps.Service.Store;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Operations.NetworkOps.Service.Api;

// What a Claude session (netops on the person's PC) uses to work a machine WITHOUT ever touching it from that PC: the
// session runs in a terminal on Ian's PC, usually over the VPN, and every read goes here instead -- APP01 is on the same
// network as every PC and server, and runs the script through the machine's agent (about a second) or its own network
// route. Same group, same Entra sign-in and MFA as the Command Center: running a script here is exactly the Command
// Center's "Run a PowerShell command…", so it is audited the same way (NetworkOps.Actions, kind run-command).
internal static class SessionApi
{
    public const int DefaultTimeoutSeconds = 90;
    public const int MaxTimeoutSeconds = 300;
    public const int MaxScriptChars = 20_000;

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/devices/{name}/run", async (string name, RemoteRunRequest body, HttpContext h, NetworkOpsStore s, Agents.MachineRunner runner,
            IOptions<NetworkOpsOptions> o, ILoggerFactory loggers, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Script)) return Results.BadRequest(new { error = "there is no script to run" });
            if (body.Script.Length > MaxScriptChars) return Results.BadRequest(new { error = $"the script is longer than {MaxScriptChars:N0} characters" });
            if (await s.DeviceByNameAsync(name, ct) is not { } dev) return Results.NotFound(new { error = $"NetworkOps has no machine called {name}" });
            if (Sweep.ActionRunner.HostOf(o.Value, dev.Name) is not { } host)
                return Results.BadRequest(new { error = $"{dev.Name} is not a Windows server APP01 can run scripts on" });

            var timeout = TimeSpan.FromSeconds(Math.Clamp(body.TimeoutSeconds ?? DefaultTimeoutSeconds, 10, MaxTimeoutSeconds));
            var by = ApiAccess.UserOf(h.User);
            var request = System.Text.Json.JsonSerializer.Serialize(new { param = body.Script, via = "netops run (Claude session)", purpose = body.Purpose, promptRun = body.PromptRunId });
            var actionId = await s.StartActionAsync(dev.DeviceId, Core.Actions.FixCatalog.RunCommand, by, request, ct);
            var route = runner.ViaAgent(host) ? "agent" : "network";
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Transport.OnTargetRun run;
            try { run = await runner.RunAsync(host, body.Script, timeout, wantsIdle: false, ct); }
            catch (OperationCanceledException)
            {
                // The request aborted or the service is stopping mid-run: close the action, or it sits "Running" forever
                // (session-run rows are not the queued-fix rows the abandoned-fix sweep reopens at startup).
                await s.CompleteActionAsync(actionId, false, "aborted (the request was cancelled or the service stopped)", null);
                throw;
            }
            catch (Exception ex)
            {
                await s.CompleteActionAsync(actionId, false, ex.Message, null);
                return Results.Ok(new RemoteRunResult(actionId, dev.Name, false, route, (int)clock.ElapsedMilliseconds, null, ex.Message));
            }
            var ok = run.Status == Transport.OnTargetStatus.Ok;
            await s.CompleteActionAsync(actionId, ok, ok ? Sweep.ActionRunner.ResultLine(run.OutputJson) ?? "ran" : $"{run.Status}: {run.Error}", run.OutputJson);
            loggers.CreateLogger("NetworkOps.Api.Session").LogInformation("RUN {Id} on {Host} for {By} via {Route}: {Status} in {Ms} ms{Purpose}",
                actionId, dev.Name, by, route, run.Status, clock.ElapsedMilliseconds, body.Purpose is null ? "" : $" ({body.Purpose})");
            return Results.Ok(new RemoteRunResult(actionId, dev.Name, ok, route, (int)clock.ElapsedMilliseconds, run.OutputJson, ok ? null : $"{run.Status}: {run.Error}"));
        });

        api.MapGet("/devices/{name}/last-check", async (string name, NetworkOpsStore s, CancellationToken ct) =>
        {
            if (await s.DeviceByNameAsync(name, ct) is not { } dev) return Results.NotFound(new { error = $"NetworkOps has no machine called {name}" });
            return await s.LastCheckAsync(dev.DeviceId, ct) is { } last
                ? Results.Ok(new LastCheckView(dev.Name, last.Probe, last.AtUtc, last.Json))
                : Results.NotFound(new { error = $"no stored check for {dev.Name} yet" });
        });

        // Banked knowledge: accepted cards (what prompts carry); ?all=true adds proposed and rejected ones.
        api.MapGet("/knowledge", async (string? search, bool? all, NetworkOpsStore s, CancellationToken ct) =>
            (await s.KnowledgeCardsAsync(acceptedOnly: all != true, ct)).Where(c => Matches(c, search)).ToList());
    }

    /// <summary>Every word of the search appears somewhere in the card (title, applies-to, symptom, cause, check, fix, tags).</summary>
    internal static bool Matches(KnowledgeCard c, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        var text = string.Join(" ", c.Title, c.AppliesTo, c.Symptom, c.Cause, c.Check, c.Fix, c.Tags, c.SourceDevice);
        return search.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
    }
}
