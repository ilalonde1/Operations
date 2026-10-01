#nullable enable
using System.Text.Json;
using Kor.Operations.NetworkOps.Core.Actions;
using Kor.Operations.NetworkOps.Service.Agents;
using Kor.Operations.NetworkOps.Service.Store;
using Kor.Operations.NetworkOps.Transport;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Operations.NetworkOps.Service.Sweep;

// Runs the fixes the Command Center asks for. NetworkOps.Actions is the queue AND the audit: the API writes a
// Requested row (who, which fix, its input); this claims it within seconds, runs the catalog's script ON the
// machine as SYSTEM (through its agent, or the one-shot SCM channel), records the output, and then queues a re-check of the
// machine, so the page shows whether the finding actually cleared. Only FixCatalog's fixes run. A fix left
// Running by a stopped service is marked failed at startup, never re-run: a fix is not idempotent.
internal sealed class ActionRunner(NetworkOpsStore store, MachineRunner runner, AgentInstaller installer, IOptions<NetworkOpsOptions> options, ILogger<ActionRunner> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            var abandoned = await store.AbandonRunningActionsAsync(ct);
            if (abandoned > 0) log.LogWarning("{Count} fix(es) were running when the service stopped: marked failed, not re-run", abandoned);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Could not close abandoned fixes"); }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(4));
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            NetworkOpsStore.ClaimedAction? a;
            try { a = await store.ClaimActionAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogWarning(ex, "Fix poll failed"); continue; }
            if (a is null) continue;
            // Each fix on its own task: a 40-minute SFC on one PC must not hold up a restart on another.
            _ = Task.Run(() => RunAsync(a, ct), CancellationToken.None);
        }
    }

    private async Task RunAsync(NetworkOpsStore.ClaimedAction a, CancellationToken ct)
    {
        try
        {
            if (AgentInstaller.IsAgentKind(a.Kind))
            {
                if (IsRack(a.DeviceName)) throw new InvalidOperationException("the agent is for PCs; the rack is read by the rack sweep");
                log.LogWarning("AGENT {Id} {Kind} on {Host} requested by {By}", a.ActionId, a.Kind, a.DeviceName, a.RequestedBy);
                var (ok, detail) = await installer.RunAsync(a.Kind, a.DeviceId, a.DeviceName, a.RequestedBy, ct);
                await store.CompleteActionAsync(a.ActionId, ok, detail, null);
                log.LogWarning("AGENT {Id} {Kind} on {Host}: {Detail}", a.ActionId, a.Kind, a.DeviceName, detail);
                return;
            }

            var fix = FixCatalog.Get(a.Kind) ?? throw new InvalidOperationException($"'{a.Kind}' is not in the fix catalog");
            using var req = JsonDocument.Parse(a.RequestJson);
            var param = req.RootElement.TryGetProperty("param", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            if (FixCatalog.Invalid(fix, param) is { } why) throw new InvalidOperationException(why);
            var host = HostOf(a.DeviceName) ?? throw new InvalidOperationException($"{a.DeviceName} is not a machine fixes can run on");

            log.LogWarning("FIX {Id} {Fix} on {Host} requested by {By}", a.ActionId, fix.Id, host, a.RequestedBy);
            var run = await runner.RunAsync(host, FixCatalog.Script(fix, param), TimeSpan.FromSeconds(fix.TimeoutSeconds), wantsIdle: false, ct);
            if (run.Status != OnTargetStatus.Ok)
            {
                await store.CompleteActionAsync(a.ActionId, false, $"{run.Status}: {run.Error}", run.OutputJson);
                return;
            }
            var result = ResultLine(run.OutputJson) ?? "ran; see the output";
            await store.CompleteActionAsync(a.ActionId, true, result, run.OutputJson);
            log.LogWarning("FIX {Id} {Fix} on {Host}: {Result}", a.ActionId, fix.Id, host, result);

            // Re-check, so the page shows whether it cleared. Not after a restart: the PC is going down, and the
            // hourly sweep sees it when it is back.
            if (fix.Id != "restart-pc")
            {
                if (IsRack(a.DeviceName)) await store.QueueRackCheckAsync(a.DeviceName, $"fix {a.ActionId}", CancellationToken.None);
                else await store.QueueCheckAsync(a.DeviceName, $"fix {a.ActionId}", CancellationToken.None);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogError(ex, "FIX {Id} failed", a.ActionId);
            await store.CompleteActionAsync(a.ActionId, false, ex.Message, null);
        }
    }

    /// <summary>The machine a fix runs on: a PC by its name; a rack device only if it is a Windows server (the others have no SCM).</summary>
    private string? HostOf(string deviceName)
    {
        var rack = options.Value.Rack.FirstOrDefault(d => d.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase));
        return rack is null ? deviceName : rack.Collector == "WindowsServer" ? rack.Address : null;
    }

    private bool IsRack(string deviceName) => options.Value.Rack.Any(d => d.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase));

    /// <summary>The fix's own verdict (every catalog script returns an object with Result); the first line of anything else.</summary>
    internal static string? ResultLine(string? outputJson)
    {
        if (string.IsNullOrWhiteSpace(outputJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(outputJson);
            foreach (var e in doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray() : new[] { doc.RootElement }.AsEnumerable())
            {
                if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("Result", out var r)) return r.ToString();
                if (e.ValueKind == JsonValueKind.String) return e.GetString();
            }
            return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() == 0 ? "ran; no output" : null;
        }
        catch (JsonException) { return outputJson.Length > 300 ? outputJson[..300] : outputJson; }
    }
}
