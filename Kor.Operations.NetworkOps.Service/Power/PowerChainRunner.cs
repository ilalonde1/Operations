#nullable enable
using Kor.Operations.NetworkOps.Core.Power;
using Kor.Operations.NetworkOps.Service.Alerting;
using Kor.Operations.NetworkOps.Service.Store;
using Kor.Operations.NetworkOps.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Operations.NetworkOps.Service.Power;

public sealed record ChainOutcome(bool Ok, string Summary, IReadOnlyList<string> Problems);

// Runs the rack shutdown (Core/Power/ShutdownPlan) against the live hosts, one step after another, and
// writes every step to NetworkOps.PowerEvents as it happens -- after an outage that table is the
// timeline, after a rehearsal it is the proof.
//
// DRY RUN changes nothing on any VM, host or Synology. It proves each step CAN be taken: every host
// answers on its pinned key, every VM in the plan is where the plan says, the final script lands and
// runs on each host, each Synology accepts a login (the UC3200 passes its own power-off check on both
// controllers), and the host API answers. The one thing it does touch is host .10's firewall, opened
// and closed for the seconds the Synology calls take -- because that is part of what is being proven.
//
// REAL: waves of clean guest shutdowns (hard power-off after the timeout), every host but the
// controller's powered off by its own script, then the handoff -- the script on the controller's host
// shuts this very VM down, then the Synologys (the SAN last), then that host. This service does not
// see the end; the script's log (/tmp/kor-chain.log and the host syslog) does.
internal sealed class PowerChainRunner(IOptions<NetworkOpsOptions> options, NetworkOpsStore store, IDigestSender alerts, ILogger<PowerChainRunner> log)
{
    private static readonly TimeSpan SshTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(90);
    private readonly SemaphoreSlim _one = new(1, 1);

    public async Task<ChainOutcome> RunAsync(bool dryRun, string reason, CancellationToken ct)
    {
        // A rehearsal never queues behind another; a REAL chain waits for a rehearsal in flight (minutes at most).
        if (!await _one.WaitAsync(dryRun ? TimeSpan.Zero : TimeSpan.FromMinutes(5), ct).ConfigureAwait(false))
            return new ChainOutcome(false, "a chain is already running", []);
        try { return await RunLockedAsync(dryRun, reason, ct).ConfigureAwait(false); }
        finally { _one.Release(); }
    }

    private async Task<ChainOutcome> RunLockedAsync(bool dryRun, string reason, CancellationToken ct)
    {
        var o = options.Value;
        var id = Guid.NewGuid();
        var mode = dryRun ? "DRY RUN" : "REAL";
        var failures = new List<string>();
        async Task Step(bool ok, string text)
        {
            if (!ok) failures.Add(text);
            log.Log(ok ? LogLevel.Information : LogLevel.Error, "Power chain {Mode}: {Text}", mode, text);
            try { await store.AddPowerEventAsync(id, "ChainStep", dryRun, ok, text).ConfigureAwait(false); }
            catch (Exception ex) { log.LogWarning(ex, "Could not record a chain step"); }   // the chain does not stop for its diary
        }

        await store.AddPowerEventAsync(id, "ChainStart", dryRun, true, $"{mode}: {reason}").ConfigureAwait(false);
        if (!dryRun)
        {
            using var cap = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await alerts.SendAlertAsync("[NetworkOps] RACK SHUTDOWN STARTED", $"{reason}\n\nThe chain is shutting the rack down now. Timeline: NetworkOps.PowerEvents; the last step logs on ESXi host {o.PowerChain.ControllerHost} (/tmp/kor-chain.log, syslog tag kor-chain).", cap.Token).ConfigureAwait(false); }
            catch (Exception ex) { log.LogWarning(ex, "Chain-start alert not sent"); }
        }

        // 1. What is actually running where.
        var inventory = new List<VmOnHost>();
        foreach (var host in o.PowerChain.Hosts)
        {
            try
            {
                using var sh = Connect(host);
                var (exit, output, error) = await sh.RunAsync(EsxiCommands.Inventory, CommandTimeout, ct).ConfigureAwait(false);
                var vms = EsxiCommands.ParseInventory(host, output);
                inventory.AddRange(vms);
                await Step(exit == 0 && vms.Count > 0, $"{host}: {vms.Count(v => v.PoweredOn)} of {vms.Count} VMs running ({string.Join(", ", vms.Where(v => v.PoweredOn).Select(v => v.Vm))}){(exit == 0 ? "" : $" -- exit {exit}: {error.Trim()}")}").ConfigureAwait(false);
            }
            catch (Exception ex) { await Step(false, $"{host}: cannot read its VMs: {ex.Message}").ConfigureAwait(false); }
        }

        var plan = ShutdownPlan.Build(o.PowerChain, inventory);
        foreach (var p in plan.Problems)
        {
            failures.Add("plan: " + p);
            await store.AddPowerEventAsync(id, "Plan", dryRun, false, p).ConfigureAwait(false);
        }

        // 2. The steps.
        foreach (var step in plan.Steps)
        {
            try
            {
                switch (step.Kind)
                {
                    case ChainStepKind.GuestShutdown:
                        await GuestShutdownWaveAsync(step, dryRun, o.PowerChain.GuestShutdownTimeoutSeconds, Step, ct).ConfigureAwait(false);
                        break;
                    case ChainStepKind.HostPowerOff:
                        await RunHostScriptAsync(step.Host, dryRun, vm: null, [], o.PowerChain.GuestShutdownTimeoutSeconds, Step, ct).ConfigureAwait(false);
                        break;
                    case ChainStepKind.Handoff:
                        var storage = new List<(StorageTarget, string)>();
                        foreach (var t in o.PowerChain.Storage)
                        {
                            var pw = Environment.GetEnvironmentVariable(t.PasswordVariable);
                            if (string.IsNullOrEmpty(pw)) await Step(false, $"{t.Name}: no password in {t.PasswordVariable}; it will not be shut down").ConfigureAwait(false);
                            else storage.Add((t, pw));
                        }
                        await RunHostScriptAsync(step.Host, dryRun, o.PowerChain.ControllerVm, storage, o.PowerChain.GuestShutdownTimeoutSeconds, Step, ct).ConfigureAwait(false);
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Carry on: stopping half-way leaves the rack on a battery that is about to go flat.
                await Step(false, $"{step.Describe}: FAILED: {ex.Message}").ConfigureAwait(false);
            }
        }

        var summary = failures.Count == 0
            ? $"{mode} complete: {plan.Steps.Count} steps, every one {(dryRun ? "proven" : "done")}"
            : $"{mode} finished with {failures.Count} problem(s): {string.Join("; ", failures)}";
        await store.AddPowerEventAsync(id, "ChainEnd", dryRun, failures.Count == 0, summary).ConfigureAwait(false);
        return new ChainOutcome(failures.Count == 0, summary, failures);
    }

    private async Task GuestShutdownWaveAsync(ChainStep step, bool dryRun, int timeoutSeconds, Func<bool, string, Task> record, CancellationToken ct)
    {
        foreach (var byHost in step.Vms.GroupBy(v => v.Host, StringComparer.OrdinalIgnoreCase))
        {
            using var sh = Connect(byHost.Key);
            if (dryRun)
            {
                foreach (var vm in byHost)
                {
                    var (_, state, _) = await sh.RunAsync(EsxiCommands.PowerState(vm.VmId), CommandTimeout, ct).ConfigureAwait(false);
                    await record(EsxiCommands.IsOn(state), $"{step.Describe}: {vm.Vm} (id {vm.VmId} on {vm.Host}) is {state.Trim()}; would shut it down").ConfigureAwait(false);
                }
                continue;
            }
            foreach (var vm in byHost)
            {
                var (exit, _, err) = await sh.RunAsync(EsxiCommands.GuestShutdown(vm.VmId), CommandTimeout, ct).ConfigureAwait(false);
                await record(exit == 0, $"{vm.Vm}: guest shutdown {(exit == 0 ? "requested" : $"refused ({err.Trim()}), will power off at the timeout")}").ConfigureAwait(false);
            }
            var waiting = byHost.ToList();
            var until = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (waiting.Count > 0 && DateTime.UtcNow < until)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                foreach (var vm in waiting.ToList())
                {
                    var (_, state, _) = await sh.RunAsync(EsxiCommands.PowerState(vm.VmId), CommandTimeout, ct).ConfigureAwait(false);
                    if (!EsxiCommands.IsOn(state)) { waiting.Remove(vm); await record(true, $"{vm.Vm}: off").ConfigureAwait(false); }
                }
            }
            foreach (var vm in waiting)
            {
                var (exit, _, err) = await sh.RunAsync(EsxiCommands.PowerOff(vm.VmId), CommandTimeout, ct).ConfigureAwait(false);
                await record(false, $"{vm.Vm}: still running after {timeoutSeconds} s, POWERED OFF {(exit == 0 ? "" : $"-- failed: {err.Trim()}")}").ConfigureAwait(false);
            }
        }
    }

    /// <summary>Lands the script and its config on a host (config through stdin: no password on any command line) and starts it.</summary>
    private async Task RunHostScriptAsync(string host, bool dryRun, string? vm, IReadOnlyList<(StorageTarget, string)> storage, int timeoutSeconds,
        Func<bool, string, Task> record, CancellationToken ct)
    {
        using var sh = Connect(host);
        var config = HostScript.Config(dryRun, vm, timeoutSeconds, powerOffHost: true, storage);
        await Must(sh.RunWithInputAsync(HostScript.WriteStdinTo(HostScript.ScriptPath), HostScript.Text, CommandTimeout, ct), "write the script").ConfigureAwait(false);
        await Must(sh.RunAsync(HostScript.ClearLog, CommandTimeout, ct), "clear the log").ConfigureAwait(false);
        await Must(sh.RunWithInputAsync(HostScript.WriteStdinTo(HostScript.ConfigPath), config, CommandTimeout, ct), "write the config").ConfigureAwait(false);
        await Must(sh.RunAsync(HostScript.Launch, CommandTimeout, ct), "start the script").ConfigureAwait(false);
        var what = vm is null ? "power the host off" : $"shut down {vm}, {string.Join(", ", storage.Select(s => s.Item1.Name))}, then the host";
        if (!dryRun)
        {
            await record(true, $"{host}: final script started ({what}); it runs on without this service").ConfigureAwait(false);
            if (vm is null) await Task.Delay(TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);   // let the host go before the next step
            return;
        }

        // A dry run finishes in seconds to a couple of minutes (Synology logins): read its log back.
        var until = DateTime.UtcNow.AddMinutes(4);
        var text = "";
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            text = (await sh.RunAsync(HostScript.ReadLog, CommandTimeout, ct).ConfigureAwait(false)).Output;
            if (text.Contains(HostScript.DoneMarker, StringComparison.Ordinal)) break;
        }
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(l => !l.EndsWith(HostScript.DoneMarker, StringComparison.Ordinal)).ToList();
        var done = text.Contains(HostScript.DoneMarker, StringComparison.Ordinal);
        var failed = lines.Where(l => l.Contains("FAILED", StringComparison.Ordinal) || l.Contains("BLOCKED", StringComparison.Ordinal) || l.Contains("REFUSED", StringComparison.Ordinal)).ToList();
        await record(done && failed.Count == 0, $"{host}: final script ({what}) {(done ? "ran to the end" : "DID NOT FINISH in 4 min")}: " + string.Join(" | ", lines.Select(l => l.Length > 20 ? l[20..] : l))).ConfigureAwait(false);
    }

    private static async Task Must(Task<(int Exit, string Output, string Error)> run, string what)
    {
        var (exit, _, error) = await run.ConfigureAwait(false);
        if (exit != 0) throw new InvalidOperationException($"could not {what}: exit {exit} {error.Trim()}");
    }

    private EsxiShell Connect(string host)
    {
        var o = options.Value;
        var pins = o.EsxiHostKeys.TryGetValue(host, out var p) ? p : [];
        return EsxiShell.Connect(host, o.EsxiKeyPath, pins, SshTimeout);
    }
}
