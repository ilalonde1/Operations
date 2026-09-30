#nullable enable
namespace Kor.Operations.NetworkOps.Core.Power;

/// <summary>One VM as an ESXi host reports it (vim-cmd): the host's address, the VM's name, and whether it runs.</summary>
public sealed record VmOnHost(string Host, int VmId, string Vm, bool PoweredOn);

public sealed class StorageTarget
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string Account { get; set; } = "";
    /// <summary>Name of the KOR_NETWORKOPS_* machine variable holding this box's password (never the password itself).</summary>
    public string PasswordVariable { get; set; } = "";
    /// <summary>True for the box the VMs live on: it is always shut down last.</summary>
    public bool HoldsTheVms { get; set; }

    /// <summary>A dual-controller DSM UC box (the UC3200): shut down as one cluster, never one controller.</summary>
    public bool DualController { get; set; }
}

/// <summary>The rack shutdown, as configuration (appsettings.json, section PowerChain).</summary>
public sealed class ShutdownPlanOptions
{
    /// <summary>Guest-shutdown waves, in order. VMs in one wave shut down together; the next wave waits for them.</summary>
    public List<List<string>> Waves { get; set; } = [];

    /// <summary>The VM NetworkOps itself runs on: shut down last, by a script on its own host.</summary>
    public string ControllerVm { get; set; } = "";
    public string ControllerHost { get; set; } = "";

    /// <summary>Every ESXi host in the rack.</summary>
    public List<string> Hosts { get; set; } = [];

    /// <summary>Synology boxes, shut down by the controller host's script after the controller VM is off, in this order.</summary>
    public List<StorageTarget> Storage { get; set; } = [];

    /// <summary>Name prefixes of VMs the plan leaves alone (vSphere's own vCLS agents).</summary>
    public List<string> IgnorePrefixes { get; set; } = ["vCLS-"];

    public int GuestShutdownTimeoutSeconds { get; set; } = 240;
}

public enum ChainStepKind { GuestShutdown, HostPowerOff, Handoff }

/// <param name="Vms">For GuestShutdown: the wave's VMs, each with its host and id.</param>
public sealed record ChainStep(ChainStepKind Kind, string Host, IReadOnlyList<VmOnHost> Vms, string Describe);

public static class ShutdownPlan
{
    /// <summary>
    /// Everything that makes the plan unsafe to run against this inventory. Empty = safe. Each problem is a
    /// sentence an operator can act on. This runs daily against the live hosts (PowerChainCheckJob) and
    /// before every real chain, so a VM added, renamed or moved is caught while the lights are on -- not
    /// discovered at 2 a.m. as the VM the chain did not know about.
    /// </summary>
    public static IReadOnlyList<string> Problems(ShutdownPlanOptions o, IReadOnlyList<VmOnHost> inventory)
    {
        var p = new List<string>();
        bool Ignored(string vm) => o.IgnorePrefixes.Any(x => vm.StartsWith(x, StringComparison.OrdinalIgnoreCase));

        if (o.Hosts.Count == 0) p.Add("no ESXi hosts are configured");
        if (string.IsNullOrWhiteSpace(o.ControllerVm) || string.IsNullOrWhiteSpace(o.ControllerHost)) p.Add("the controller VM and its host must both be named");
        else if (!o.Hosts.Contains(o.ControllerHost, StringComparer.OrdinalIgnoreCase)) p.Add($"the controller's host {o.ControllerHost} is not in the host list");

        var planned = o.Waves.SelectMany(w => w).ToList();
        foreach (var dup in planned.GroupBy(v => v, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            p.Add($"{dup.Key} is in more than one wave");
        if (planned.Contains(o.ControllerVm, StringComparer.OrdinalIgnoreCase))
            p.Add($"{o.ControllerVm} is the controller: it must not be in a wave (its own host shuts it down last)");

        var byName = inventory.GroupBy(v => v.Vm, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        foreach (var dup in byName.Where(kv => kv.Value.Count > 1))
            p.Add($"{dup.Key} is registered on more than one host ({string.Join(", ", dup.Value.Select(v => v.Host))}): the plan cannot tell which to shut down");
        foreach (var vm in planned.Where(v => !byName.ContainsKey(v)))
            p.Add($"{vm} is in the plan but no host has a VM of that name (renamed or removed?)");

        if (byName.TryGetValue(o.ControllerVm, out var ctl))
        {
            if (!ctl.Any(c => c.Host.Equals(o.ControllerHost, StringComparison.OrdinalIgnoreCase)))
                p.Add($"{o.ControllerVm} is on {string.Join(", ", ctl.Select(c => c.Host))}, not on {o.ControllerHost}: the final script would run on the wrong host");
        }
        else if (!string.IsNullOrWhiteSpace(o.ControllerVm)) p.Add($"the controller {o.ControllerVm} was not found on any host");

        foreach (var h in inventory.Select(v => v.Host).Distinct(StringComparer.OrdinalIgnoreCase).Where(h => !o.Hosts.Contains(h, StringComparer.OrdinalIgnoreCase)))
            p.Add($"host {h} reported VMs but is not in the host list");

        foreach (var vm in inventory.Where(v => v.PoweredOn && !Ignored(v.Vm)
                                               && !planned.Contains(v.Vm, StringComparer.OrdinalIgnoreCase)
                                               && !v.Vm.Equals(o.ControllerVm, StringComparison.OrdinalIgnoreCase)))
            p.Add($"{vm.Vm} is running on {vm.Host} but is in no wave: it would be powered off hard when its host goes down");

        if (o.Storage.Count == 0) p.Add("no storage boxes are configured");
        else
        {
            var holders = o.Storage.Where(s => s.HoldsTheVms).ToList();
            if (holders.Count != 1) p.Add($"exactly one storage box must hold the VMs ({holders.Count} are marked)");
            else if (!ReferenceEquals(o.Storage[^1], holders[0])) p.Add($"{holders[0].Name} holds the VMs and must be the LAST storage box shut down");
            foreach (var s in o.Storage.Where(s => string.IsNullOrWhiteSpace(s.Address) || string.IsNullOrWhiteSpace(s.Account) || string.IsNullOrWhiteSpace(s.PasswordVariable)))
                p.Add($"storage box '{s.Name}' needs an address, an account and a password variable");
        }
        return p;
    }

    /// <summary>
    /// The steps, in order: any running VM the plan does not know (so it still gets a clean shutdown, not a
    /// hard power-off with its host), then each wave's running VMs, then every host except the one the
    /// controller is actually on, then the handoff to that host.
    ///
    /// It never refuses: in a real outage a plan that has drifted still has to run, and it runs as safely
    /// as the drift allows. The drift is reported as Problems, which the daily rehearsal turns into an
    /// alert while the lights are on. If the controller has moved host, the chain follows it -- powering
    /// off the host it moved to before the handoff would kill the chain half-way.
    /// </summary>
    public static ChainPlan Build(ShutdownPlanOptions o, IReadOnlyList<VmOnHost> inventory)
    {
        var problems = Problems(o, inventory);
        bool Ignored(string vm) => o.IgnorePrefixes.Any(x => vm.StartsWith(x, StringComparison.OrdinalIgnoreCase));
        var byName = inventory.GroupBy(v => v.Vm, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var planned = o.Waves.SelectMany(w => w).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var controller = byName.GetValueOrDefault(o.ControllerVm);
        var controllerHost = controller?.Host ?? o.ControllerHost;

        var steps = new List<ChainStep>();
        var unplanned = inventory.Where(v => v.PoweredOn && !Ignored(v.Vm) && !planned.Contains(v.Vm)
                                             && !v.Vm.Equals(o.ControllerVm, StringComparison.OrdinalIgnoreCase)).ToList();
        if (unplanned.Count > 0)
            steps.Add(new ChainStep(ChainStepKind.GuestShutdown, "", unplanned, $"VMs not in the plan: shut down {Names(unplanned)}"));
        var done = unplanned.Select(v => v.Vm).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (wave, i) in o.Waves.Select((w, i) => (w, i)))
        {
            var running = wave.Where(n => byName.ContainsKey(n) && done.Add(n)).Select(n => byName[n])
                              .Where(v => v.PoweredOn && !v.Vm.Equals(o.ControllerVm, StringComparison.OrdinalIgnoreCase)).ToList();
            if (running.Count > 0)
                steps.Add(new ChainStep(ChainStepKind.GuestShutdown, "", running, $"wave {i + 1}: shut down {Names(running)}"));
        }
        foreach (var h in o.Hosts.Where(h => !h.Equals(controllerHost, StringComparison.OrdinalIgnoreCase)))
            steps.Add(new ChainStep(ChainStepKind.HostPowerOff, h, [], $"power off host {h}"));
        steps.Add(new ChainStep(ChainStepKind.Handoff, controllerHost, controller is null ? [] : [controller],
            $"hand off to {controllerHost}: shut down {o.ControllerVm}, then {string.Join(", then ", o.Storage.Select(s => s.Name))}, then power off the host"));
        return new ChainPlan(steps, problems);
    }

    private static string Names(IEnumerable<VmOnHost> vms) => string.Join(", ", vms.Select(v => v.Vm));
}

public sealed record ChainPlan(IReadOnlyList<ChainStep> Steps, IReadOnlyList<string> Problems);
