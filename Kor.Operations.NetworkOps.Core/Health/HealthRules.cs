#nullable enable
namespace Kor.Operations.NetworkOps.Core.Health;

public enum Severity { Info = 0, Warning = 1, Critical = 2 }

/// <param name="RuleKey">Stable identity of the finding on this device -- the dedup key. One
/// active finding per (device, RuleKey); the same key raised again updates it, not duplicates it.</param>
public sealed record Finding(string RuleKey, Severity Severity, string Title, string Evidence);

// The rules that turn one workstation's health snapshot into findings. Each rule is a fault
// class somebody lost time to in 2026, not a generic threshold (NinjaOne's entire policy was
// CPU/disk/SMART/Spooler/WMI and it called KOR-207 healthy while Revit could not install).
// Thresholds are the ones the incidents justify; each is named where it is used.
//
// Pure: a snapshot in, findings out. Held by fixtures captured from real machines
// (Kor.Operations.NetworkOps.Tests/Fixtures/health).
public static class HealthRules
{
    public static IReadOnlyList<Finding> Evaluate(HealthSnapshot s)
    {
        var f = new List<Finding>();

        // WMI broken: most other reads fail, so say that first and plainly (KOR-213, 2026-09-28).
        if (s.WmiHealthy == false)
            f.Add(new("wmi-broken", Severity.Critical, "WMI is broken",
                $"core CIM classes fail ('Invalid class'); {s.ProbeErrors.Count} probe blocks could not read"));

        // A data drive that vanished (KOR-206-N's D:, 2026-09-28). USB sticks that were unplugged are not drives that failed.
        var vanished = s.MissingDisks.Where(d => d.InstanceId is not null && !d.InstanceId.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase)).ToList();
        if (vanished.Count > 0)
            f.Add(new("disk-missing", Severity.Critical, "A drive has disappeared",
                string.Join("; ", vanished.Select(d => d.Name)) +
                (s.OrphanDriveLetters.Count > 0 ? $" | letters with no volume: {string.Join(",", s.OrphanDriveLetters)}" : "")));

        // A drive going bad: any bad block or NTFS corruption, or repeated controller resets.
        var e = s.Events14d;
        var badBlocks = e?.DiskBadBlock?.Count ?? 0;
        var resets = e?.DiskResets?.Count ?? 0;
        var ntfs = e?.NtfsCorruption?.Count ?? 0;
        var unhealthy = s.PhysicalDisks.Where(d => d.Health is not null && !d.Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase)).ToList();
        if (badBlocks > 0 || ntfs > 0 || resets >= 3 || unhealthy.Count > 0)
            f.Add(new("disk-failing", Severity.Critical, "A drive is failing",
                $"14 d: {badBlocks} bad-block, {resets} controller resets, {ntfs} NTFS corruption" +
                (unhealthy.Count > 0 ? $" | unhealthy: {string.Join("; ", unhealthy.Select(d => $"{d.Name} {d.Health}"))}" : "")));

        // GPU hangs (LiveKernelEvent 0x141 = the video engine timed out and was reset).
        // Probe v3 counts DISTINCT resets: 5 in 14 days is a pattern, 25 is a machine the user is fighting
        // (measured 2026-09-29: 104N 52, SPARE2 46, 213-N 43, 308 10, 206-N 2). Probe v2 counted raw WER
        // log entries, ~100 per reset, so its thresholds were 10 / 500 and its numbers read two orders too high.
        var gpu = e?.GpuHang?.Count ?? 0;
        var distinct = s.ProbeVersion >= 3;
        var (warnAt, criticalAt) = distinct ? (5, 25) : (10, 500);
        if (gpu >= warnAt)
        {
            var cards = s.DisplayAdapters.Where(a => a.Name is not null && !a.Name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase)).Select(a => $"{a.Name} {a.Driver}").ToList();
            var noDiscrete = cards.Count > 0 && cards.All(c => c.StartsWith("Intel", StringComparison.OrdinalIgnoreCase) || c.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase));
            f.Add(new("gpu-hangs", gpu >= criticalAt ? Severity.Critical : Severity.Warning, "Graphics driver keeps hanging",
                (distinct ? $"{gpu} GPU resets in 14 d" : $"{gpu} video-engine timeout log entries in 14 d (not resets)") +
                $" | adapters: {(cards.Count > 0 ? string.Join("; ", cards) : "none reported")}" +
                (noDiscrete ? " | no discrete card visible: check it is present and seated" : "")));
        }

        var shutdowns = e?.UnexpectedShutdown?.Count ?? 0;
        if (shutdowns >= 2)
            f.Add(new("unexpected-shutdowns", Severity.Warning, "Machine keeps losing power or crashing",
                $"{shutdowns} unclean shutdowns (Kernel-Power 41) in 14 d"));

        var whea = e?.Whea?.Count ?? 0;
        if (whea >= 3)
            f.Add(new("hardware-errors", Severity.Warning, "Hardware is reporting errors",
                $"{whea} WHEA events in 14 d, last {e?.Whea?.Last:yyyy-MM-dd HH:mm}"));

        // An application crashing over and over -- Revit on 207, Bluebeam on 308, opushutil on the
        // Access-Engine machines. 5 in 14 days is a pattern, not bad luck. One finding per process.
        foreach (var c in s.AppCrashes14d.Where(c => c.Count >= 5).OrderByDescending(c => c.Count))
            f.Add(new($"crash-loop:{c.Process.ToLowerInvariant()}", Severity.Warning, $"{c.Process} keeps crashing",
                $"{c.Count} crashes in 14 d, last {c.Last:yyyy-MM-dd HH:mm}"));

        foreach (var st in s.OutlookIndex60d.Where(st => st.Count >= 2))
            f.Add(new($"outlook-index:{Path.GetFileName(st.Store).ToLowerInvariant()}", Severity.Info, "Outlook search can't index a mailbox file",
                $"{Path.GetFileName(st.Store)}: {st.Count} failures in 60 d (a rebuild did not fix this on 206-N; treat as a store problem)"));

        // The Access Database Engine's 2020 down-level MSO DLLs hijack Click-to-Run Office
        // (opushutil 0xc06d007f). 16.0.5495 is the patched level (KB5002623, 2026-08-05).
        if (s.Office?.DownlevelMso is { } down && Version.TryParse(down, out var dv) && dv < new Version(16, 0, 5495, 0))
            f.Add(new("office-access-engine-stale", Severity.Warning, "Access Database Engine DLLs are out of date",
                $"down-level mso20win32client.dll {down} vs Click-to-Run {s.Office.C2rMso}; update the redistributable, never delete the folder"));

        foreach (var v in s.Volumes.Where(v => v.SizeGB > 0))
        {
            var pct = v.FreeGB / v.SizeGB * 100;
            if (pct < 10)
                f.Add(new($"low-disk:{v.Letter.ToLowerInvariant()}", pct < 5 ? Severity.Critical : Severity.Warning, $"Drive {v.Letter}: is nearly full",
                    $"{v.FreeGB:0.#} GB free of {v.SizeGB:0.#} GB ({pct:0.#}%)"));
        }

        // Patched but never rebooted: harmless for a day, a problem after three.
        var p = s.PendingReboot;
        if (p is not null && (p.ComponentServicing || p.WindowsUpdate) && (s.Os?.UptimeHours ?? 0) > 72)
            f.Add(new("reboot-overdue", Severity.Warning, "Updates are waiting on a reboot",
                $"reboot pending, up {s.Os!.UptimeHours / 24} days"));

        if (s.Update?.LastInstall is { } last && (s.CollectedAt - last).TotalDays > 35)
            f.Add(new("not-patched", Severity.Warning, "No updates installed in over a month",
                $"last install {last:yyyy-MM-dd}: {s.Update.LastInstallTitle}"));

        if (s.Update is { MicrosoftUpdate: false })
            f.Add(new("microsoft-update-off", Severity.Info, "Office security updates are not being offered",
                "Microsoft Update is not registered, so Office MSI patches (Access Database Engine) never arrive"));

        // A BIOS fan curve that holds the fans high at idle (KOR-305, KOR-206-N).
        if (s.CoolingMode is { } mode && System.Text.RegularExpressions.Regex.IsMatch(mode, "Performance|Full Speed", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            f.Add(new("fan-profile-loud", Severity.Info, "Fans are set to a loud profile",
                $"BIOS cooling mode is '{mode}'; 'Balance mode' is the quiet setting"));

        // Real data on a drive nothing backs up (13 months of KOR-206-N's work lived only on D:).
        var outside = s.DataOutsideSystemDrive.Where(d => d.UsedGB >= 1).ToList();
        if (outside.Count > 0)
            f.Add(new("unbacked-data", Severity.Warning, "Data on a drive that isn't backed up",
                string.Join("; ", outside.Select(d => $"{d.Letter}: {d.UsedGB:0.#} GB used"))));

        // Old ScreenConnect generations left running as SYSTEM after the MSP moved on.
        var sc = s.RemoteTools.Count(t => t.Name.StartsWith("ScreenConnect", StringComparison.OrdinalIgnoreCase));
        if (sc > 1)
            f.Add(new("remote-tools-stale", Severity.Info, "Old remote-access agents are still installed",
                $"{sc} ScreenConnect instances; only the current generation is used"));

        if (s.Residue is { } r && (r.NewformaProfiles > 0 || (r.NewformaInstalled?.Count ?? 0) > 0))
            f.Add(new("newforma-residue", Severity.Info, "Newforma leftovers",
                $"{r.NewformaProfiles} profile folders, installed: {string.Join(", ", r.NewformaInstalled ?? [])}"));

        // A probe block failed for a reason other than broken WMI: the snapshot is incomplete.
        if (s.WmiHealthy != false && s.ProbeErrors.Count > 0)
            f.Add(new("probe-incomplete", Severity.Info, "Part of the health check couldn't read",
                string.Join(" | ", s.ProbeErrors)));

        return f;
    }
}
