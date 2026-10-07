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

        // A data drive that vanished (KOR-206-N's D:, 2026-09-28). USB sticks that were unplugged are not drives that
        // failed; nor are VMware's phantom virtual-SATA nodes -- a VM's SATA controller enumerates its unpopulated ports
        // as ghost "VMware Virtual SATA Hard Drive" devices with no volume (BK01, 7 of them, raised Critical in error
        // on 2026-10-07; its real disks are SAS + iSCSI and all Healthy).
        var vanished = s.MissingDisks.Where(d => d.InstanceId is not null
            && !d.InstanceId.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase)
            && !d.InstanceId.Contains("VEN_VMWARE&PROD_VIRTUAL_SATA", StringComparison.OrdinalIgnoreCase)).ToList();
        if (vanished.Count > 0)
            f.Add(new("disk-missing", Severity.Critical, "A drive has disappeared",
                string.Join("; ", vanished.Select(d => d.Name)) +
                (s.OrphanDriveLetters.Count > 0 ? $" | letters with no volume: {string.Join(",", s.OrphanDriveLetters)}" : "")));

        // A drive that is still there but whose data Windows no longer shows (probe v11): its data partition has no letter and
        // no folder mount. KOR-208-N, 2026-10-02 15:05: the D: hard drive reset, logged 306 bad blocks, and its 1,863 GB
        // partition lost its volume and letter -- the drive stayed, so "A drive has disappeared" never fired.
        foreach (var d in s.PhysicalDisks.Where(d => d.Name is not null && d.System != true && d.UnmountedGB is >= 1))
            f.Add(new($"disk-unmounted:{d.Name!.ToLowerInvariant()}", Severity.Critical, "A data drive's volume is no longer mounted",
                $"{Drives.Model(s, d.Name)}: {d.UnmountedGB:N0} GB of data partition with no letter" +
                (s.OrphanDriveLetters.Count > 0 ? $" | letters left with nothing behind them: {string.Join(", ", s.OrphanDriveLetters)}" : "") +
                " | copy the data off with a recovery tool before anything writes to it"));

        // A drive going bad: any bad block or NTFS corruption, or repeated controller resets.
        var e = s.Events14d;
        var badBlocks = e?.DiskBadBlock?.Count ?? 0;
        var resets = e?.DiskResets?.Count ?? 0;
        var ntfs = e?.NtfsCorruption?.Count ?? 0;
        var unhealthy = s.PhysicalDisks.Where(d => d.Health is not null && !d.Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase)).ToList();
        if (badBlocks > 0 || ntfs > 0 || resets >= 3 || unhealthy.Count > 0)
            f.Add(new("disk-failing", Severity.Critical, "A drive is failing",
                $"14 d: {badBlocks} bad-block, {resets} controller resets, {ntfs} NTFS corruption" +
                (unhealthy.Count > 0 ? $" | unhealthy: {string.Join("; ", unhealthy.Select(d => $"{(Drives.Role(d) is { } r ? r + " " : "")}{d.Name} {d.Health}"))}" : "")));

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
            // v8 dates each reset by its report's creation; the newest and the ones left out go in the evidence.
            var dated = (e?.GpuHangReports ?? []).Where(r => r.Created is not null).Select(r => r.Created!.Value).ToList();
            f.Add(new("gpu-hangs", gpu >= criticalAt ? Severity.Critical : Severity.Warning, "Graphics driver keeps hanging",
                (distinct ? $"{gpu} GPU resets in 14 d" : $"{gpu} video-engine timeout log entries in 14 d (not resets)") +
                (dated.Count > 0 ? $", newest {dated.Max():yyyy-MM-dd HH:mm}" : "") +
                (e?.GpuHangStaleReports is > 0 and var stale ? $" ({stale} older reports still queued, not counted)" : "") +
                $" | adapters: {(cards.Count > 0 ? string.Join("; ", cards) : "none reported")}" +
                (noDiscrete ? " | no discrete card visible: check it is present and seated" : "")));
        }

        if (MemoryLayout(s) is { } mem)
            f.Add(new("memory-layout", Severity.Info, "Memory is not running at full speed", mem));

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
            // Low on space in EITHER relative OR absolute terms. Percentage alone missed a ~100 GB system drive at 20 GB
            // free (BK01's C:, 20.6% -- Ninja flagged it, we did not). The absolute floor is gated by a loose % so a big
            // drive that is merely 20% free is left alone: on a 500 GB drive 25 GB is ~5%, so the floor only ever sharpens
            // small or system drives, never adds noise to large ones. Same thresholds as the server disk rule
            // (Core/Rack/ServerRules), so a filling drive reads identically whether a box is read as a server or an agent.
            var crit = pct < 5 || (v.FreeGB < 5 && pct < 25);
            var warn = pct < 10 || (v.FreeGB < 25 && pct < 40);
            if (crit || warn)
                f.Add(new($"low-disk:{v.Letter.ToLowerInvariant()}", crit ? Severity.Critical : Severity.Warning,
                    Drives.OfLetter(s, v.Letter) is { } pd && Drives.Role(pd) is { } role ? $"{Drives.Capitalised(role)} is nearly full" : $"Drive {v.Letter}: is nearly full",
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

        // No keyboard, no mouse, and a display that switches off when idle: nothing can ever wake it, so a monitor
        // plugged in later stays black while remote sessions work (KOR-210, 2026-10-01).
        if (s.Console is { Keyboards: 0, Mice: 0, DisplayOffAfterSeconds: > 0 } con)
            f.Add(new("display-sleeps-headless", Severity.Info, "Its display goes dark and nothing can wake it",
                $"no keyboard or mouse attached, and Windows turns the display off after {con.DisplayOffAfterSeconds / 60} min: a monitor plugged in later shows nothing"));

        // A headless PC with a real graphics card but NO monitor and NO dummy plug: the card is driving no display, so
        // over KOR Remote (which mirrors the physical screen) the console comes up blank or at a fallback size, and the
        // GPU can sit idle instead of accelerating. A monitor -- or an HDMI/DisplayPort dummy plug -- gives it a real
        // desktop and keeps the card engaged. Gated on probe v12, the first that reports each adapter's live resolution
        // (0 = driving nothing); older probes carry no resolution, so they must not raise it.
        if (s.ProbeVersion >= 12 && s.Console is { Keyboards: 0, Mice: 0 })
        {
            static bool Real(DisplayAdapterInfo a) => a.Name is not null
                && !a.Name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase)
                && !a.Name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase);
            var hasGpu = s.DisplayAdapters.Any(Real);
            var driving = s.DisplayAdapters.Any(a => (a.Width ?? 0) > 0
                && (a.Name is null || !a.Name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase)));
            if (hasGpu && !driving)
                f.Add(new("headless-no-display", Severity.Info, "Headless with no monitor or dummy plug",
                    "no keyboard or mouse, and the graphics card is driving no display: over KOR Remote the console comes up blank or at a fallback size, and the GPU can sit idle. A monitor -- or an HDMI/DisplayPort dummy plug (match the card's port; most workstation cards are DisplayPort) -- gives it a real desktop and keeps the card engaged"));
        }

        if (WakeProblems(s.Wake) is { Count: > 0 } wake)
            f.Add(new("wake-not-ready", Severity.Info, "A magic packet won't wake it", string.Join("; ", wake)));

        // A BIOS fan curve that holds the fans high at idle (KOR-305, KOR-206-N).
        if (s.CoolingMode is { } mode && System.Text.RegularExpressions.Regex.IsMatch(mode, "Performance|Full Speed", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            f.Add(new("fan-profile-loud", Severity.Info, "Fans are set to a loud profile",
                $"BIOS cooling mode is '{mode}'; the quiet setting is 'Best Experience' on a P340, 'Balance mode' on a P350/P360"));

        // Real data on a drive nothing backs up (13 months of KOR-206-N's work lived only on D:). An iSCSI/SAN volume is
        // the storage tier, not a loose local drive at risk -- on a backup server it IS the repository (BK01's E:/F:
        // Synology iSCSI, the Veeam target), so it is not "unbacked data" and is skipped (Ian, 2026-10-07).
        var outside = s.DataOutsideSystemDrive
            .Where(d => d.UsedGB >= 1)
            .Where(d => !string.Equals(Drives.OfLetter(s, d.Letter)?.Bus, "iSCSI", StringComparison.OrdinalIgnoreCase))
            .ToList();
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

    /// <summary>
    /// Null when the memory runs as it should; otherwise one line saying how it doesn't. Two things cost
    /// real speed: channels holding different amounts (the surplus runs single-channel), and modules
    /// running well below their rating (on DDR5, two modules on one channel does that by itself).
    /// 206-N, 2026-09-29: 32 GB on channel A, 64 GB on channel B, rated 4800, running 3600.
    /// </summary>
    internal static string? MemoryLayout(HealthSnapshot s)
    {
        var mods = s.Memory.Where(m => m.SizeGB > 0).ToList();
        if (mods.Count == 0) return null;
        var problems = new List<string>();

        // Channel = the slot name up to "-DIMM" ("Controller0-ChannelA-DIMM1" -> "Controller0-ChannelA").
        // Boards that don't name channels are left alone rather than guessed at.
        string? ChannelOf(string? slot)
        {
            if (slot is null) return null;
            var i = slot.IndexOf("-DIMM", StringComparison.OrdinalIgnoreCase);
            return i > 0 && slot.Contains("Channel", StringComparison.OrdinalIgnoreCase) ? slot[..i] : null;
        }
        var channels = mods.Select(m => (Channel: ChannelOf(m.Slot), m.SizeGB)).ToList();
        if (channels.All(c => c.Channel is not null))
        {
            var perChannel = channels.GroupBy(c => c.Channel!).Select(g => (Name: g.Key, GB: g.Sum(c => c.SizeGB))).OrderBy(c => c.Name).ToList();
            if (perChannel.Count > 1 && perChannel.Select(c => c.GB).Distinct().Count() > 1)
            {
                var surplus = perChannel.Max(c => c.GB) - perChannel.Min(c => c.GB);
                problems.Add($"channels unbalanced ({string.Join(", ", perChannel.Select(c => $"{ShortChannel(c.Name)} {c.GB} GB"))}): {surplus} GB runs single-channel");
            }
            else if (perChannel.Count == 1 && (s.MemorySlots ?? 0) >= 2)
                problems.Add("every module is on one channel: all of it runs single-channel");
        }
        else if (mods.Count == 1 && (s.MemorySlots ?? 0) >= 2)
            problems.Add("a single module in a multi-slot board: it runs single-channel");

        var rated = mods.Where(m => m.RatedMTs > 0).Select(m => m.RatedMTs).DefaultIfEmpty(0).Min();
        var running = mods.Where(m => m.ConfiguredMTs > 0).Select(m => m.ConfiguredMTs).DefaultIfEmpty(0).Min();
        if (rated > 0 && running > 0 && running < rated * 0.85)
            problems.Add($"rated {rated}, running {running} MT/s");

        if (problems.Count == 0) return null;
        var sizes = string.Join(" + ", mods.GroupBy(m => m.SizeGB).OrderByDescending(g => g.Key).Select(g => $"{g.Count()} x {g.Key} GB"));
        return $"{sizes} = {mods.Sum(m => m.SizeGB)} GB in {mods.Count} of {s.MemorySlots?.ToString() ?? "?"} slots | {string.Join(" | ", problems)}";
    }

    /// <summary>
    /// Why a magic packet would not wake this PC from shutdown; empty when nothing is known to stop it. Fast Startup
    /// first: it was on 29 of 29 PCs on 2026-10-01 and is the common blocker. The BIOS is judged only where it can be
    /// read (Lenovo); elsewhere it is unknown, not wrong -- a failed wake test is how that one shows.
    /// </summary>
    internal static List<string> WakeProblems(WakeInfo? w)
    {
        var p = new List<string>();
        if (w is null) return p;
        if (w.FastStartup == 1) p.Add("Fast Startup is on (shutdown is a hybrid hibernation the network card does not wake from)");
        if (w.Wired is not { } nic) { p.Add("no wired network card"); return p; }
        if (!nic.MagicPacket) p.Add($"{nic.Description}: Wake on Magic Packet is off");
        if (!nic.Armed) p.Add($"{nic.Description}: not allowed to wake the computer");
        if (nic.Pme is { } pme && pme.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) p.Add($"{nic.Description}: its wake signal (PME) is disabled");
        if (w.LenovoWakeOnLan is { } bios && bios.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) p.Add("BIOS Wake on LAN is Disabled");
        return p;
    }

    private static string ShortChannel(string c)
    {
        var i = c.IndexOf("Channel", StringComparison.OrdinalIgnoreCase);
        return i >= 0 ? c[i..].Replace("Channel", "channel ", StringComparison.OrdinalIgnoreCase) : c;
    }
}
