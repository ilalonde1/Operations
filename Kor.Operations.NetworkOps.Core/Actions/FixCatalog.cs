#nullable enable
using System.Text.RegularExpressions;
using Kor.Operations.NetworkOps.Core.Learning;

namespace Kor.Operations.NetworkOps.Core.Actions;

/// <param name="Id">Stable id: recorded in NetworkOps.Actions.Kind, which fix learning reads.</param>
/// <param name="Disruptive">Interrupts the person at the PC (a restart). Refused while someone is ACTIVE unless confirmed.</param>
/// <param name="Families">Finding families it CLEARS -- the finding is gone at the re-check after it runs; "*" = any finding
/// (the escape hatch). The worklist's one click and "Clear in one action" use only these (see <see cref="FixAction.Helps"/>).</param>
/// <param name="ParamLabel">Non-null when the fix needs one input (a service name, a script).</param>
/// <param name="Target">Where it runs: "windows" (a PC or Windows server, PowerShell as SYSTEM through the SCM channel),
/// "esxi" (an ESXi host, a Python script over the key APP01 reads the hosts with), or "mesh" (a PowerShell script on the
/// device's MeshCentral agent -- the way to reach a workgroup/off-domain box like BK01 that the SCM channel cannot).</param>
public sealed record FixAction(string Id, string Title, string Explain, bool Disruptive, int TimeoutSeconds, IReadOnlyList<string> Families, string? ParamLabel = null,
    string Target = FixCatalog.Windows)
{
    /// <summary>Families it may HELP but cannot clear: a restart relieves a crash loop, but the finding is a 14-day count and
    /// stays open. Offered on the device page after the fixes that clear, marked as such; never "the fix" on the worklist and
    /// never counted as clearing anything (audit 2026-10-16, 1.5: "Restart the PC · all 18 -- clears 22 findings" claimed a
    /// restart cleared crash, hang and indexing counts).</summary>
    public IReadOnlyList<string> Helps { get; init; } = [];
}

/// <summary>What the page may run, and nothing else. Each script is embedded (Actions/*.ps1), runs ON the PC as
/// SYSTEM through the same one-shot SCM channel as the health probe, and returns plain values. Every run is a row in
/// NetworkOps.Actions (who, what, when, output), so "what has cleared it on this fleet" learns from the page's own
/// fixes. A finding is re-checked after its fix, so the page shows whether it actually cleared.</summary>
public static class FixCatalog
{
    public const string RunCommand = "run-command";
    public const string RunCommandMesh = "run-command-mesh";
    public const string StartService = "start-service";
    public const string RestartMeshAgent = "restart-mesh-agent";
    public const string FreeDiskSpace = "free-disk-space";
    public const string InstallUpdates = "install-updates";
    public const string InstallUpdatesRestart = "install-updates-restart";
    public const string UpdateBios = "update-bios";
    public const string RemoveStaleDatastore = "remove-stale-datastore";
    public const string Windows = "windows";
    public const string Esxi = "esxi";
    /// <summary>Run via the device's MeshCentral agent -- reaches workgroup/off-domain boxes (BK01) the SCM channel cannot.</summary>
    public const string MeshAgent = "mesh";
    public const string CheckBiosUpdate = "check-bios-update";
    // Dispatched by ActionRunner to AgentInstaller (not scripted here, so it carries no embedded Actions/*.ps1).
    // MUST equal the Service's AgentInstaller.InstallKind -- FixCatalogTests asserts the two strings are the same.
    public const string UpdateAgent = "install-agent";

    public static bool IsUpdateInstall(string id) => id is InstallUpdates or InstallUpdatesRestart;

    public static readonly IReadOnlyList<FixAction> All =
    [
        // A restart CLEARS "not restarted" and a pending reboot. It only HELPS the rolling event counts (crashes, hangs,
        // indexing, rising pressure -- the finding stays until the count falls) and an incomplete probe. "not-patched" is
        // cleared by installing updates, not by restarting.
        new("restart-pc", "Restart the PC",
            "Restarts in 5 minutes with a warning on screen, so whoever is there can save their work. Clears pending updates, stuck drivers and leaking processes.",
            Disruptive: true, TimeoutSeconds: 120, ["not-restarted", "reboot-overdue"])
        {
            Helps = ["crash-loop", "gpu-hangs", "outlook-index", "probe-incomplete", "memory-pressure-rising", "app-hangs-rising"],
        },
        new(FreeDiskSpace, "Free up disk space",
            "Clears Windows' and every user's temporary files older than 2 days, the downloaded-updates cache and the delivery-optimization cache, then compacts the component store. No user documents are touched.",
            Disruptive: false, TimeoutSeconds: 1800, ["low-disk", "disk-filling", "server.disk-full"]),
        new("repair-wmi", "Repair WMI",
            "Verifies the WMI repository, salvages it if it is inconsistent, and restarts the service (KOR-213's fix, Sep 2026).",
            Disruptive: false, TimeoutSeconds: 300, ["wmi-broken", "probe-incomplete"]),
        new("repair-windows", "Repair Windows system files",
            "DISM /RestoreHealth then SFC /scannow: repairs corrupted Windows components. Takes 15-40 minutes; the PC stays usable.",
            Disruptive: false, TimeoutSeconds: 3000, ["wmi-broken"])
        {
            // Repairs what corruption causes; the crash, shutdown and hardware-error counts stay until they age out.
            Helps = ["crash-loop", "unexpected-shutdowns", "probe-incomplete", "hardware-errors"],
        },
        new("turn-on-microsoft-update", "Turn on Microsoft Update",
            "Opts Windows Update into Microsoft Update, so Office and other Microsoft products get their patches too (done fleet-wide 28 Sep 2026).",
            Disruptive: false, TimeoutSeconds: 120, ["microsoft-update-off"]),
        new("display-never-off", "Keep the display on",
            "Sets 'Turn off the display' to Never, for a PC that runs with no keyboard or mouse: a monitor plugged in later shows the screen at once. Sleep and the screen lock are untouched.",
            Disruptive: false, TimeoutSeconds: 60, ["display-sleeps-headless"]),
        new("quiet-fans", "Set the fans to the quiet profile",
            "Sets the Lenovo BIOS cooling profile to its quiet setting ('Best Experience' on a P340, 'Balance mode' on a P350/P360), read from the PC's own list. It takes effect at the next restart; nothing restarts now.",
            Disruptive: false, TimeoutSeconds: 60, ["fan-profile-loud"]),
        new("enable-wake-on-lan", "Turn on Wake-on-LAN",
            "Turns Fast Startup off and sets the wired network card to wake on a magic packet (and the BIOS too, on Lenovo), so the Wake button can start it from shutdown. Nothing restarts; the card settings apply at its next restart or shutdown.",
            Disruptive: false, TimeoutSeconds: 180, ["wake-not-ready"]),
        new(CheckBiosUpdate, "Check the BIOS update",
            "Finds Lenovo's BIOS package for this PC, downloads it and verifies it (Lenovo's checksum and signature, power, BIOS password, BitLocker) -- and stops there. Nothing is flashed or restarted.",
            Disruptive: false, TimeoutSeconds: 600, [Bios.BiosRules.Rule]),
        new(UpdateBios, "Update the BIOS",
            "Installs Lenovo's current BIOS for this PC, exactly as Lenovo's own updater would (checksum and Lenovo signature verified, BitLocker suspended for one restart), then restarts in 5 minutes with a warning on screen. The BIOS is written during that restart: it must not be turned off.",
            Disruptive: true, TimeoutSeconds: 1800, [Bios.BiosRules.Rule]),
        new(InstallUpdates, "Install updates (no restart)",
            "Installs what Windows Update has waiting, now, as SYSTEM: security and other software updates, never drivers, previews or feature upgrades. Does not restart: the machine shows 'restart pending' until someone restarts it.",
            Disruptive: false, TimeoutSeconds: 5400, [Updates.UpdateRules.Rule, "not-patched"]),
        new("read-sql-setup-logs", "Read why a SQL Server update failed",
            "Reads SQL Server Setup's own logs from the last 14 days (Windows Update only gives a generic code) and each instance's patch level. Read-only: changes nothing.",
            Disruptive: false, TimeoutSeconds: 120, [Updates.UpdateRules.Rule]),
        new(InstallUpdatesRestart, "Install updates and restart if needed",
            "The same install, then -- only if an update needs it -- a restart in 5 minutes with a warning on screen, so whoever is there can save.",
            Disruptive: true, TimeoutSeconds: 5400, [Updates.UpdateRules.Rule, "not-patched"]),
        new(RemoveStaleDatastore, "Remove the leftover Veeam datastore",
            "Unmounts the datastore Veeam left behind after an instant recovery -- only if it is a VeeamBackup_* NFS mount, the host reports it inaccessible, and no VM is registered on it (all read from the host first). Nothing else is touched; the host's list is read again to confirm.",
            Disruptive: false, TimeoutSeconds: 120, ["esxi.stale-mount"], ParamLabel: "Datastore", Target: Esxi),
        new(StartService, "Start the stopped service",
            "Starts the service and sets it to restart itself if it fails again (3 x 60 s) -- the MCP server and Certify on APP01 stayed down for days without that.",
            Disruptive: false, TimeoutSeconds: 180, ["server.service-stopped"], ParamLabel: "Service name"),
        new(RestartMeshAgent, "Restart the remote-control agent",
            "Force-restarts this PC's MeshCentral (KOR Remote) agent -- kills the agent process and starts the service again -- for when the agent is connected but a Connect will not render because its desktop session is wedged. Briefly drops any Mesh session to this PC; the user's own work is untouched. Runs as SYSTEM through the audited SCM channel, not through the agent, so it reaches domain PCs -- a workgroup box (BK01) reachable only via Mesh cannot be fixed this way.",
            Disruptive: false, TimeoutSeconds: 120, ["mesh-silent"]),
        new(UpdateAgent, "Update the agent",
            "Reinstalls the NetworkOps agent from the version APP01 ships -- upgrading an out-of-date agent, or restoring one that stopped calling in. Silent: nothing restarts, and the agent calls back within a minute. Runs through the same audited channel as every other action.",
            Disruptive: false, TimeoutSeconds: 180, ["agent-outdated", "agent-silent"]),
        new(RunCommand, "Run a PowerShell command…",
            "Runs your PowerShell as SYSTEM on this machine and shows the output. Audited: the script, who ran it and what it returned are kept.",
            Disruptive: false, TimeoutSeconds: 600, ["*"], ParamLabel: "PowerShell to run as SYSTEM"),
        // Invoked by id (not offered per-finding): the generic way to run anything on a device's Mesh agent -- a workgroup
        // box like BK01 the SCM channel cannot reach. Specific mesh fixes (with their own families) come on top of this.
        new(RunCommandMesh, "Run a PowerShell command via the Mesh agent…",
            "Runs your PowerShell through this device's MeshCentral agent and shows the output -- the way to reach a workgroup/off-domain box (like BK01) the SCM channel cannot. Audited like every other action.",
            Disruptive: false, TimeoutSeconds: 600, [], ParamLabel: "PowerShell to run on the Mesh agent", Target: MeshAgent),
    ];

    public static FixAction? Get(string id) => All.FirstOrDefault(a => a.Id == id);

    /// <summary>The fixes offered for a finding, most specific first; the escape hatch last.</summary>
    /// <summary>Every fix offered for a finding: those that clear it, then those that may help, then the escape hatch.</summary>
    public static IReadOnlyList<FixAction> For(string ruleKey)
    {
        var family = FixLearning.FamilyOf(ruleKey);
        return All.Where(a => a.Families.Contains(family) && Fits(a, ruleKey))
            .Concat(All.Where(a => a.Helps.Contains(family) && Fits(a, ruleKey)))
            .Concat(All.Where(a => a.Families.Contains("*"))).ToList();
    }

    /// <summary>THE fix for a finding -- the worklist's one click, and what "Clear in one action" groups by: the first fix, in
    /// catalog order, that CLEARS it. Never one that only helps, never the run-a-command hatch. Null: no one-click fix.</summary>
    public static FixAction? PrimaryFor(string ruleKey)
    {
        var family = FixLearning.FamilyOf(ruleKey);
        return All.FirstOrDefault(a => a.Families.Contains(family) && Fits(a, ruleKey));
    }

    /// <summary>Whether running this fix clears the finding (false: it only helps, or it is the escape hatch).</summary>
    public static bool Clears(FixAction a, string ruleKey) => a.Families.Contains(FixLearning.FamilyOf(ruleKey)) && Fits(a, ruleKey);

    /// <summary>A fix that works on only part of what its family covers. Free-disk-space cleans C: (Windows' and every user's
    /// temp, the update caches): it is offered for a full C:, never for a full D: it cannot touch (audit 2026-10-16, 1.5).</summary>
    private static bool Fits(FixAction a, string ruleKey)
        => a.Id != FreeDiskSpace || (ruleKey.IndexOf(':') is var i and > 0 ? ruleKey[(i + 1)..].Trim().TrimEnd(':', '\\').ToUpperInvariant() : "C") == "C";

    /// <summary>The input a fix gets from its finding, when the finding already names it (server.service-stopped:Certify.Service).</summary>
    public static string? ParamFromFinding(FixAction a, string ruleKey)
        => a.Id is StartService or RemoveStaleDatastore && ruleKey.IndexOf(':') is var i and > 0 ? ruleKey[(i + 1)..] : null;

    /// <summary>Why the input is refused, or null. A service name is a bare identifier: it is pasted into a script.</summary>
    public static string? Invalid(FixAction a, string? param) => a.Id switch
    {
        StartService when param is null || !Regex.IsMatch(param, @"^[A-Za-z0-9_.\-]{1,80}$") => "a service name is letters, digits, dot, dash or underscore",
        // Passed to the host as one shell argument: a datastore name with nothing a shell could read as more.
        RemoveStaleDatastore when param is null || !Regex.IsMatch(param, @"^[A-Za-z0-9_.\-]{1,100}$") => "a datastore name is letters, digits, dot, dash or underscore",
        (RunCommand or RunCommandMesh) when string.IsNullOrWhiteSpace(param) => "there is no script to run",
        (RunCommand or RunCommandMesh) when param!.Length > 20000 => "the script is longer than 20,000 characters",
        _ when a.ParamLabel is null && !string.IsNullOrEmpty(param) => "this fix takes no input",
        _ => null,
    };

    /// <summary>Why a disruptive fix is refused for want of confirmation, or null. EVERY disruptive fix needs it: presence only
    /// shapes the wording, it never waives it. One rule for every road that queues a fix (the page, "Fix on all N", the MCP
    /// tool), checked BEFORE anything is queued -- the MCP tool gated on presence alone, so on a server (no presence) it queued
    /// the fix and ActionRunner refused it a moment later, after the caller had read "Queued" as started (27 Oct, FS01).</summary>
    public static string? Unconfirmed(FixAction a, string device, string? presenceState, string? presence, bool confirmed)
        => !a.Disruptive || confirmed ? null
            : presenceState == "Active"
                ? $"someone is using {device} right now ({presence}): confirm to {a.Title.ToLowerInvariant()}"
                : $"{a.Title} will interrupt {device}: confirm to go ahead";

    /// <summary>The script that runs on the machine: the fix's body, with its input bound as a single-quoted PowerShell string.</summary>
    public static string Script(FixAction a, string? param)
    {
        if (a.Id is RunCommand or RunCommandMesh) return param!;   // the operator's own script, as typed (audited)
        // The two update installs are one script; the restart variant only sets its switch.
        if (a.Id == InstallUpdatesRestart) return "$RestartIfNeeded = $true\n" + Script(Get(InstallUpdates)!, null);
        // The BIOS check is the update with $DryRun: one script, so the check proves exactly what the update would run.
        if (a.Id == CheckBiosUpdate) return "$DryRun = $true\n" + Script(Get(UpdateBios)!, null);
        // An ESXi fix is a Python script (the host's argument is passed on its command line, not bound in here).
        if (a.Target == Esxi)
        {
            using var py = typeof(FixCatalog).Assembly.GetManifestResourceStream($"Actions.{a.Id}.py")
                ?? throw new InvalidOperationException($"no embedded script for {a.Id}");
            return new StreamReader(py).ReadToEnd();
        }
        using var s = typeof(FixCatalog).Assembly.GetManifestResourceStream($"Actions.{a.Id}.ps1")
            ?? throw new InvalidOperationException($"no embedded script for {a.Id}");
        using var r = new StreamReader(s);
        var body = r.ReadToEnd();
        return param is null ? body : $"$Param = '{param.Replace("'", "''")}'\n{body}";
    }
}
