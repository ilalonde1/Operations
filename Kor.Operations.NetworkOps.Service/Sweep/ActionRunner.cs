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
internal sealed class ActionRunner(NetworkOpsStore store, MachineRunner runner, AgentInstaller installer, Mesh.MeshInstaller mesh,
    Updates.UpdateScanner updates, AgentHub agents, IOptions<NetworkOpsOptions> options, ILogger<ActionRunner> log) : BackgroundService
{
    // Update installs each pull hundreds of MB through the office's internet line: a batch of 30 runs a few at a time.
    private readonly SemaphoreSlim _installs = new(Math.Max(1, options.Value.UpdateInstallParallel));

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

            if (a.Kind == Mesh.MeshInstaller.InstallKind)
            {
                // Remote control: PCs into "KOR PCs", Windows rack servers (by their Address) into "KOR Servers".
                var server = IsRack(a.DeviceName);
                var target = HostOf(a.DeviceName) ?? throw new InvalidOperationException($"{a.DeviceName} is not a Windows machine remote control can be installed on");
                log.LogWarning("MESH {Id} install on {Host} requested by {By}", a.ActionId, target, a.RequestedBy);
                var (meshOk, meshDetail) = await mesh.RunAsync(a.DeviceId, target, server, ct);
                await store.CompleteActionAsync(a.ActionId, meshOk, meshDetail, null);
                log.LogWarning("MESH {Id} install on {Host}: {Detail}", a.ActionId, target, meshDetail);
                return;
            }

            if (a.Kind == MagicPacket.Kind)
            {
                var (woke, detail) = await WakeAsync(a, ct);
                await store.CompleteActionAsync(a.ActionId, woke, detail, null);
                log.LogWarning("WAKE {Id} {Host}: {Detail}", a.ActionId, a.DeviceName, detail);
                if (woke) await store.QueueCheckAsync(a.DeviceName, $"wake {a.ActionId}", CancellationToken.None);
                return;
            }

            var fix = FixCatalog.Get(a.Kind) ?? throw new InvalidOperationException($"'{a.Kind}' is not in the fix catalog");
            using var req = JsonDocument.Parse(a.RequestJson);
            var param = req.RootElement.TryGetProperty("param", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            if (FixCatalog.Invalid(fix, param) is { } why) throw new InvalidOperationException(why);
            if (fix.Target == FixCatalog.Esxi)
            {
                await RunOnEsxiAsync(a, fix, param!, ct);
                return;
            }
            var host = HostOf(a.DeviceName) ?? throw new InvalidOperationException($"{a.DeviceName} is not a machine fixes can run on");

            log.LogWarning("FIX {Id} {Fix} on {Host} requested by {By}", a.ActionId, fix.Id, host, a.RequestedBy);
            var install = FixCatalog.IsUpdateInstall(fix.Id);
            if (install) await _installs.WaitAsync(ct);
            OnTargetRun run;
            try { run = await runner.RunAsync(host, FixCatalog.Script(fix, param), TimeSpan.FromSeconds(fix.TimeoutSeconds), wantsIdle: false, ct); }
            finally { if (install) _installs.Release(); }
            if (run.Status != OnTargetStatus.Ok)
            {
                await store.CompleteActionAsync(a.ActionId, false, $"{run.Status}: {run.Error}", run.OutputJson);
                return;
            }
            var result = ResultLine(run.OutputJson) ?? "ran; see the output";

            // After an install, search again at once: the Updates view shows what is left (or "restart pending") without
            // waiting for the next scheduled search. BEFORE the run is marked Done, so "Done" never sits beside the finding
            // the install just cleared (SPARE8, 2026-10-01: read in the 20 s between the two, it looked like a stale finding).
            if (install)
            {
                try { await updates.ScanAsync([a.DeviceId], patchTuesdayNotice: false, CancellationToken.None); }
                catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Re-scan after install {Id} failed", a.ActionId); }
            }
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
        catch (OperationCanceledException)
        {
            // Shutdown mid-fix. This task is fire-and-forget (unawaited), so letting the cancel escape would be an
            // UNOBSERVED task exception. The action is left Running and reopened on next start (AbandonRunningActionsAsync).
        }
    }

    /// <summary>
    /// Wake-on-LAN: a magic packet from APP01 to the PC's wired MAC (the net.mac fact its health probe recorded), then
    /// up to 6 minutes for it to come back -- its agent calling in, or SMB answering. "Done" means it woke, not that a
    /// packet was sent: a packet nobody answers is the finding (BIOS or Fast Startup), and the run says so.
    /// </summary>
    private async Task<(bool Woke, string Detail)> WakeAsync(NetworkOpsStore.ClaimedAction a, CancellationToken ct)
    {
        if (IsRack(a.DeviceName)) return (false, "Wake is for PCs: the rack is never shut down");
        var facts = await store.CurrentFactsAsync(a.DeviceId, ct);
        if (MagicPacket.ParseMac(facts.GetValueOrDefault(Kor.Operations.NetworkOps.Core.Learning.Facts.WiredMac)) is not { } mac)
            return (false, "no wired MAC on record for it yet: it needs one health check while it is on (probe v6)");
        if (await AnswersAsync(a.DeviceName, ct)) return (true, "it was already on");

        var macText = facts[Kor.Operations.NetworkOps.Core.Learning.Facts.WiredMac];
        var broadcasts = options.Value.WakeBroadcasts.Select(System.Net.IPAddress.Parse).ToList();
        log.LogWarning("WAKE {Id} {Host} ({Mac}) requested by {By}", a.ActionId, a.DeviceName, macText, a.RequestedBy);
        var started = DateTime.UtcNow;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            await WakeSender.SendAsync(mac, broadcasts, ct);
            var until = DateTime.UtcNow.AddMinutes(3);
            while (DateTime.UtcNow < until)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                if (await AnswersAsync(a.DeviceName, ct))
                    return (true, $"woke: magic packet to {macText}, answering after {(int)(DateTime.UtcNow - started).TotalSeconds} s");
            }
        }
        return (false, $"magic packet sent twice to {macText}, no answer in 6 minutes: check Wake-on-LAN in its BIOS (and that it has power and a network cable)");
    }

    private async Task<bool> AnswersAsync(string host, CancellationToken ct)
    {
        if (agents.IsConnected(host)) return true;
        try
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            using var cap = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cap.CancelAfter(TimeSpan.FromSeconds(2));
            await tcp.ConnectAsync(host, 445, cap.Token);
            return true;
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or OperationCanceledException && !ct.IsCancellationRequested) { return false; }
    }

    /// <summary>The machine a fix runs on: a PC by its name; a rack device only if it is a Windows server (the others have no SCM).
    /// "Mesh" servers (FS01, RDS01) are Windows too, read through remote control only because APP01's account is not an
    /// administrator there yet: a fix is attempted and fails with that reason rather than being hidden.</summary>
    private string? HostOf(string deviceName) => HostOf(options.Value, deviceName);

    internal static string? HostOf(NetworkOpsOptions o, string deviceName)
    {
        var rack = o.Rack.FirstOrDefault(d => d.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase));
        return rack is null ? deviceName : rack.AppCanRunOn ? rack.Address : null;
    }

    /// <summary>The ESXi host a fix targeting "esxi" runs on (a rack device read by the Esxi collector), or null.</summary>
    internal static RackDevice? EsxiHostOf(NetworkOpsOptions o, string deviceName)
        => o.Rack.FirstOrDefault(d => d.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase) && d.Collector == "Esxi");

    /// <summary>THE answer to "can this fix run on this device" -- for the API's refusal and the runner alike: a Windows fix
    /// where APP01 can run PowerShell, an ESXi fix on an ESXi host.</summary>
    internal static bool CanRun(NetworkOpsOptions o, string deviceName, FixAction fix)
        => fix.Target == FixCatalog.Esxi ? EsxiHostOf(o, deviceName) is not null : HostOf(o, deviceName) is not null;

    /// <summary>
    /// An ESXi fix: its Python script copied to the host and run in hostd over the key APP01 reads the hosts with (the same
    /// route as Rack/esxi-health.py), its one argument validated by FixCatalog.Invalid before it gets here. Audited like any
    /// fix; the host is read again straight after, so the page shows whether it cleared.
    /// </summary>
    private async Task RunOnEsxiAsync(NetworkOpsStore.ClaimedAction a, FixAction fix, string param, CancellationToken ct)
    {
        var o = options.Value;
        var d = EsxiHostOf(o, a.DeviceName) ?? throw new InvalidOperationException($"{a.DeviceName} is not an ESXi host");
        log.LogWarning("FIX {Id} {Fix} on ESXi {Host} ({Param}) requested by {By}", a.ActionId, fix.Id, d.Address, param, a.RequestedBy);
        string output;
        int exit;
        string error;
        using (var sh = EsxiShell.Connect(d.Address, o.EsxiKeyPath, d.HostKeys.Count > 0 ? d.HostKeys : o.EsxiHostKeys.GetValueOrDefault(d.Address) ?? [], TimeSpan.FromSeconds(20)))
        {
            var path = $"/tmp/kor-fix-{fix.Id}.py";
            var (w, _, we) = await sh.RunWithInputAsync($"cat > {path}", FixCatalog.Script(fix, null), TimeSpan.FromSeconds(30), ct);
            if (w != 0) throw new InvalidOperationException("could not copy the fix to the host: " + we.Trim());
            (exit, output, error) = await sh.RunAsync($"python {path} '{param}'", TimeSpan.FromSeconds(fix.TimeoutSeconds), ct);
        }
        if (exit != 0)
        {
            await store.CompleteActionAsync(a.ActionId, false, $"the fix failed on the host (exit {exit}): {error.Trim()}", output);
            return;
        }
        var result = ResultLine(output) ?? "ran; see the output";
        await store.CompleteActionAsync(a.ActionId, true, result, output);
        log.LogWarning("FIX {Id} {Fix} on ESXi {Host}: {Result}", a.ActionId, fix.Id, d.Address, result);
        await store.QueueRackCheckAsync(a.DeviceName, $"fix {a.ActionId}", CancellationToken.None);
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
