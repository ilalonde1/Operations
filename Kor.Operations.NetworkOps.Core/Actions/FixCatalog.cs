#nullable enable
using System.Text.RegularExpressions;
using Kor.Operations.NetworkOps.Core.Learning;

namespace Kor.Operations.NetworkOps.Core.Actions;

/// <param name="Id">Stable id: recorded in NetworkOps.Actions.Kind, which fix learning reads.</param>
/// <param name="Disruptive">Interrupts the person at the PC (a restart). Refused while someone is ACTIVE unless confirmed.</param>
/// <param name="Families">Finding families it is offered for; "*" = any finding (the escape hatch).</param>
/// <param name="ParamLabel">Non-null when the fix needs one input (a service name, a script).</param>
public sealed record FixAction(string Id, string Title, string Explain, bool Disruptive, int TimeoutSeconds, IReadOnlyList<string> Families, string? ParamLabel = null);

/// <summary>What the page may run, and nothing else. Each script is embedded (Actions/*.ps1), runs ON the PC as
/// SYSTEM through the same one-shot SCM channel as the health probe, and returns plain values. Every run is a row in
/// NetworkOps.Actions (who, what, when, output), so "what has cleared it on this fleet" learns from the page's own
/// fixes. A finding is re-checked after its fix, so the page shows whether it actually cleared.</summary>
public static class FixCatalog
{
    public const string RunCommand = "run-command";
    public const string StartService = "start-service";
    public const string InstallUpdates = "install-updates";
    public const string InstallUpdatesRestart = "install-updates-restart";

    public static bool IsUpdateInstall(string id) => id is InstallUpdates or InstallUpdatesRestart;

    public static readonly IReadOnlyList<FixAction> All =
    [
        new("restart-pc", "Restart the PC",
            "Restarts in 5 minutes with a warning on screen, so whoever is there can save their work. Clears pending updates, stuck drivers and leaking processes.",
            Disruptive: true, TimeoutSeconds: 120,
            ["not-restarted", "reboot-overdue", "not-patched", "crash-loop", "gpu-hangs", "outlook-index", "probe-incomplete", "memory-pressure-rising", "app-hangs-rising"]),
        new("free-disk-space", "Free up disk space",
            "Clears Windows' and every user's temporary files older than 2 days, the downloaded-updates cache and the delivery-optimization cache, then compacts the component store. No user documents are touched.",
            Disruptive: false, TimeoutSeconds: 1800, ["low-disk", "disk-filling", "server.disk-full"]),
        new("repair-wmi", "Repair WMI",
            "Verifies the WMI repository, salvages it if it is inconsistent, and restarts the service (KOR-213's fix, Sep 2026).",
            Disruptive: false, TimeoutSeconds: 300, ["wmi-broken", "probe-incomplete"]),
        new("repair-windows", "Repair Windows system files",
            "DISM /RestoreHealth then SFC /scannow: repairs corrupted Windows components. Takes 15-40 minutes; the PC stays usable.",
            Disruptive: false, TimeoutSeconds: 3000, ["wmi-broken", "crash-loop", "unexpected-shutdowns", "probe-incomplete", "hardware-errors"]),
        new("turn-on-microsoft-update", "Turn on Microsoft Update",
            "Opts Windows Update into Microsoft Update, so Office and other Microsoft products get their patches too (done fleet-wide 28 Sep 2026).",
            Disruptive: false, TimeoutSeconds: 120, ["microsoft-update-off"]),
        new("enable-wake-on-lan", "Turn on Wake-on-LAN",
            "Turns Fast Startup off and sets the wired network card to wake on a magic packet (and the BIOS too, on Lenovo), so the Wake button can start it from shutdown. Nothing restarts; the card settings apply at its next restart or shutdown.",
            Disruptive: false, TimeoutSeconds: 180, ["wake-not-ready"]),
        new(InstallUpdates, "Install updates (no restart)",
            "Installs what Windows Update has waiting, now, as SYSTEM: security and other software updates, never drivers, previews or feature upgrades. Does not restart: the machine shows 'restart pending' until someone restarts it.",
            Disruptive: false, TimeoutSeconds: 5400, [Updates.UpdateRules.Rule, "not-patched"]),
        new(InstallUpdatesRestart, "Install updates and restart if needed",
            "The same install, then -- only if an update needs it -- a restart in 5 minutes with a warning on screen, so whoever is there can save.",
            Disruptive: true, TimeoutSeconds: 5400, [Updates.UpdateRules.Rule, "not-patched"]),
        new(StartService, "Start the stopped service",
            "Starts the service and sets it to restart itself if it fails again (3 x 60 s) -- the MCP server and Certify on APP01 stayed down for days without that.",
            Disruptive: false, TimeoutSeconds: 180, ["server.service-stopped"], ParamLabel: "Service name"),
        new(RunCommand, "Run a PowerShell command…",
            "Runs your PowerShell as SYSTEM on this machine and shows the output. Audited: the script, who ran it and what it returned are kept.",
            Disruptive: false, TimeoutSeconds: 600, ["*"], ParamLabel: "PowerShell to run as SYSTEM"),
    ];

    public static FixAction? Get(string id) => All.FirstOrDefault(a => a.Id == id);

    /// <summary>The fixes offered for a finding, most specific first; the escape hatch last.</summary>
    public static IReadOnlyList<FixAction> For(string ruleKey)
    {
        var family = FixLearning.FamilyOf(ruleKey);
        return All.Where(a => a.Families.Contains(family)).Concat(All.Where(a => a.Families.Contains("*"))).ToList();
    }

    /// <summary>The input a fix gets from its finding, when the finding already names it (server.service-stopped:Certify.Service).</summary>
    public static string? ParamFromFinding(FixAction a, string ruleKey)
        => a.Id == StartService && ruleKey.IndexOf(':') is var i and > 0 ? ruleKey[(i + 1)..] : null;

    /// <summary>Why the input is refused, or null. A service name is a bare identifier: it is pasted into a script.</summary>
    public static string? Invalid(FixAction a, string? param) => a.Id switch
    {
        StartService when param is null || !Regex.IsMatch(param, @"^[A-Za-z0-9_.\-]{1,80}$") => "a service name is letters, digits, dot, dash or underscore",
        RunCommand when string.IsNullOrWhiteSpace(param) => "there is no script to run",
        RunCommand when param!.Length > 20000 => "the script is longer than 20,000 characters",
        _ when a.ParamLabel is null && !string.IsNullOrEmpty(param) => "this fix takes no input",
        _ => null,
    };

    /// <summary>The script that runs on the machine: the fix's body, with its input bound as a single-quoted PowerShell string.</summary>
    public static string Script(FixAction a, string? param)
    {
        if (a.Id == RunCommand) return param!;   // the operator's own script, as typed (audited)
        // The two update installs are one script; the restart variant only sets its switch.
        if (a.Id == InstallUpdatesRestart) return "$RestartIfNeeded = $true\n" + Script(Get(InstallUpdates)!, null);
        using var s = typeof(FixCatalog).Assembly.GetManifestResourceStream($"Actions.{a.Id}.ps1")
            ?? throw new InvalidOperationException($"no embedded script for {a.Id}");
        using var r = new StreamReader(s);
        var body = r.ReadToEnd();
        return param is null ? body : $"$Param = '{param.Replace("'", "''")}'\n{body}";
    }
}
