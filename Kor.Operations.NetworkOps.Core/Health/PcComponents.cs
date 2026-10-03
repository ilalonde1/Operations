#nullable enable
using System.Text.RegularExpressions;
using Kor.Operations.NetworkOps.Core.Learning;

namespace Kor.Operations.NetworkOps.Core.Health;

/// <summary>One part of a PC as a tile on its page: what it is, two short lines, a fill bar for a drive, and the open
/// findings about it (their worst severity colours the tile; clicking it picks the first).</summary>
/// <param name="Kind">cpu | memory | gpu | drive | missing-drive | windows | bios</param>
/// <param name="FillPct">A drive's used space, 0-100; null for anything else.</param>
/// <param name="Worst">The worst open finding about this part, or null when nothing is open on it.</param>
/// <param name="Lights">A row of status lights instead of a fill bar -- a switch's ports in front-panel order: 0 down, 1 up,
/// 2 up with errors.</param>
/// <param name="Detail">More than two lines hold -- a switch port's full list of what is attached -- for the tile's hover.</param>
/// <param name="Info">Everything known about the part, as label/value rows: what clicking a healthy tile shows (Ian,
/// 2026-10-02: "why don't the tiles click anywhere ... what does clicking on it allow?").</param>
public sealed record PcComponent(string Kind, string Title, string Line1, string Line2, double? FillPct, Severity? Worst,
    IReadOnlyList<string> RuleKeys, bool IsSystem = false, IReadOnlyList<int>? Lights = null, string? Detail = null,
    IReadOnlyList<PartInfo>? Info = null);

/// <summary>One row of what is known about a part: "Model" = "ST2000DM006-2DM164".</summary>
public sealed record PartInfo(string Label, string Value);

/// <summary>
/// The PC's key parts, from its last full check, each with the open findings that are about it -- Ian, 2026-10-02: "something
/// more graphical representing the key components of the PC as opposed to a small text line at the top". A failing drive
/// colours ITS tile, so where the problem is shows before a word is read.
///
/// Which finding is about which part is decided HERE, once, by rule key (the page only draws). A finding no part owns
/// (crashing apps, mailboxes, backups) stays in the list below and colours nothing.
/// </summary>
public static class PcComponents
{
    // Finding families by the part they are about. Drives are matched per drive, by model name or letter, below.
    private static readonly string[] MemoryRules = ["memory-layout", "memory-pressure-rising"];
    private static readonly string[] GpuRules = ["gpu-hangs", "gpu-hangs-rising"];
    private static readonly string[] WindowsRules = ["updates-due", "reboot-overdue", "not-restarted", "not-patched", "os-unsupported", "microsoft-update-off", "wmi-broken"];
    private static readonly string[] BiosRules = ["bios-behind", "fan-profile-loud", "wake-not-ready"];

    public static IReadOnlyList<PcComponent> Of(HealthSnapshot s, IReadOnlyList<(string RuleKey, Severity Severity)> open)
    {
        var list = new List<PcComponent>();
        var inv = s.Inventory;

        if (inv?.Cpu is { Length: > 0 } cpu)
            list.Add(Make("cpu", "CPU", ShortCpu(cpu), inv.Cores is > 0 ? $"{inv.Cores} cores" : "", null, open, _ => false,
                info: Rows(("Processor", cpu), ("Cores", inv.Cores is > 0 ? $"{inv.Cores}" : null), ("Machine", Join(inv.Manufacturer, inv.Model)),
                    ("Board", Join(inv.BoardMaker, inv.BoardProduct)))));

        if (inv?.RamGB is > 0 || s.Memory.Count > 0)
        {
            var total = inv?.RamGB ?? s.Memory.Sum(m => m.SizeGB);
            var sticks = s.Memory.Where(m => m.SizeGB > 0).ToList();
            var layout = sticks.Count == 0 ? ""
                : (sticks.Select(m => m.SizeGB).Distinct().Count() == 1 ? $"{sticks.Count} × {sticks[0].SizeGB} GB" : $"{sticks.Count} sticks")
                  + (sticks.Max(m => m.ConfiguredMTs) is > 0 and var mts ? $" · {mts} MT/s" : "");
            list.Add(Make("memory", "Memory", $"{total} GB", layout, null, open, k => Family(k, MemoryRules), info: MemoryInfo(s, total)));
        }

        var gpus = s.DisplayAdapters.Where(a => a.Name is { Length: > 0 } n && !n.Contains("Remote Display", StringComparison.OrdinalIgnoreCase)
                                                 && !n.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)).ToList();
        // The discrete card is the one that matters (an Intel iGPU beside an NVIDIA card is the display path, not the workload).
        var gpu = gpus.FirstOrDefault(a => !a.Name!.StartsWith("Intel", StringComparison.OrdinalIgnoreCase)) ?? gpus.FirstOrDefault();
        if (gpu is not null)
            list.Add(Make("gpu", "Graphics", ShortGpu(gpu.Name!), gpu.Driver is { Length: > 0 } drv ? $"driver {drv}" : "", null, open, k => Family(k, GpuRules),
                info: gpus.Select(a => new PartInfo(a.Name!.Trim(), (a.Driver is { Length: > 0 } dv ? $"driver {dv}" : "driver unknown")
                                                                    + (a.ErrorCode is > 0 and var e ? $" · Windows reports error {e}" : ""))).ToList()));

        list.AddRange(DriveTiles(s, open));

        foreach (var m in s.MissingDisks.Where(d => d.InstanceId is not null && !d.InstanceId.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase)))
            list.Add(Make("missing-drive", s.OrphanDriveLetters.Count > 0 ? $"{string.Join(", ", s.OrphanDriveLetters.Select(l => l + ":"))}  Missing" : "Missing drive",
                m.Name ?? "a drive", "Windows no longer sees it", null, open, k => k == "disk-missing",
                info: Rows(("Drive", m.Name), ("Letters it had", s.OrphanDriveLetters.Count > 0 ? string.Join(", ", s.OrphanDriveLetters.Select(l => l + ":")) : null),
                    ("Device id", m.InstanceId), ("State", "Installed before, not present now -- unplugged, or dead"))));

        if (s.Os is { } os)
            list.Add(Make("windows", "Windows", $"{(os.Product?.Contains("Windows 10", StringComparison.OrdinalIgnoreCase) == true ? "10" : "11")} {os.DisplayVersion}".Trim(),
                os.Build > 0 ? $"build {os.Build}.{os.Ubr}" : "", null, open, k => Family(k, WindowsRules),
                info: Rows(("Edition", os.Product), ("Version", os.DisplayVersion), ("Build", os.Build > 0 ? $"{os.Build}.{os.Ubr}" : null),
                    ("Last restart", os.LastBoot is { } lb ? $"{lb.ToLocalTime():yyyy-MM-dd HH:mm} ({Up(os.UptimeHours)} ago)" : null),
                    ("Restart pending", s.PendingReboot is { } pr && (pr.ComponentServicing || pr.WindowsUpdate || pr.FileRename)
                        ? string.Join(", ", new[] { pr.WindowsUpdate ? "Windows Update" : null, pr.ComponentServicing ? "component servicing" : null,
                            pr.FileRename ? "file renames" : null }.Where(x => x is not null)) : s.PendingReboot is not null ? "no" : null),
                    ("Last update installed", s.Update?.LastInstall is { } li ? $"{li.ToLocalTime():yyyy-MM-dd}" + (s.Update.LastInstallTitle is { Length: > 0 } t ? $" · {t}" : "") : null),
                    ("Microsoft Update", s.Update is null ? null : s.Update.MicrosoftUpdate ? "on" : "off"))));

        if (inv?.BiosVersion is { Length: > 0 } bios)
            list.Add(Make("bios", "BIOS", bios.Trim(), inv.BiosDate is { Length: > 0 } bd ? bd : "", null, open, k => Family(k, BiosRules),
                info: Rows(("Version", bios), ("Dated", inv.BiosDate), ("Machine", Join(inv.Manufacturer, inv.Model)), ("Machine type", inv.MachineType),
                    ("Serial", inv.Serial), ("Fan profile", s.CoolingMode))));

        return list;
    }

    private static IEnumerable<PcComponent> DriveTiles(HealthSnapshot s, IReadOnlyList<(string RuleKey, Severity Severity)> open)
    {
        var known = s.PhysicalDisks.Where(d => d.Name is not null && (d.Letters is not null || d.System is not null)).ToList();
        if (known.Count > 0)
        {
            // Probe v10: one tile per physical drive, system first, then by letter.
            foreach (var d in known.OrderByDescending(d => d.System == true).ThenBy(d => d.Letters ?? "~", StringComparer.Ordinal))
            {
                var letters = Drives.LettersOf(d);
                var vols = s.Volumes.Where(v => letters.Contains(v.Letter.ToUpperInvariant() + ":")).ToList();
                double? fill = vols.Sum(v => v.SizeGB) is > 0 and var size ? Math.Round((size - vols.Sum(v => v.FreeGB)) / size * 100) : null;
                var title = (letters.Count > 0 ? string.Join(" ", letters) + "  " : "")
                            + (d.System == true ? "System" : letters.Count > 0 ? "Data" : d.UnmountedGB is >= 1 ? "Not mounted" : "No letter");
                var model = d.Name!.ToLowerInvariant();
                var unhealthy = d.Health is not null && !d.Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase);
                yield return Make("drive", title, Drives.Kind(d), d.Name!, fill, open,
                    k => k == $"disk-errors:{model}" || k == $"disk-wearing:{model}" || k == $"disk-aging:{model}" || k == $"disk-unmounted:{model}"
                         || letters.Any(l => k == $"low-disk:{l.TrimEnd(':').ToLowerInvariant()}")
                         || (k == "disk-failing" && unhealthy),
                    isSystem: d.System == true, info: DriveInfo(s, d, vols));
            }
            yield break;
        }
        // Before probe v10 the drive behind a letter is unknown: one tile per letter, its space only, no drive findings.
        foreach (var v in s.Volumes.Where(v => v.SizeGB > 0).OrderBy(v => v.Letter, StringComparer.Ordinal))
            yield return Make("drive", $"{v.Letter}:", $"{Drives.Size(v.SizeGB)}", v.Label ?? "", Math.Round((v.SizeGB - v.FreeGB) / v.SizeGB * 100), open,
                k => k == $"low-disk:{v.Letter.ToLowerInvariant()}", isSystem: v.Letter.Equals("C", StringComparison.OrdinalIgnoreCase),
                info: Rows(("Space", $"{v.FreeGB:0} GB free of {v.SizeGB:0} GB"), ("Label", v.Label),
                    ("Drive", "Which drive this is on shows after the PC's next check (probe v10)")));
    }

    private static PcComponent Make(string kind, string title, string line1, string line2, double? fill,
        IReadOnlyList<(string RuleKey, Severity Severity)> open, Func<string, bool> about, bool isSystem = false, IReadOnlyList<PartInfo>? info = null)
    {
        var mine = open.Where(f => about(f.RuleKey)).OrderByDescending(f => f.Severity).ToList();
        return new PcComponent(kind, title, line1, line2, fill, mine.Count > 0 ? mine[0].Severity : null, mine.Select(f => f.RuleKey).ToList(), isSystem,
            Info: info ?? []);
    }

    /// <summary>Label/value rows with the unknown ones left out -- a row reading "Serial: " tells nobody anything.</summary>
    internal static IReadOnlyList<PartInfo> Rows(params (string Label, string? Value)[] rows)
        => rows.Where(r => !string.IsNullOrWhiteSpace(r.Value)).Select(r => new PartInfo(r.Label, r.Value!.Trim())).ToList();

    private static string? Join(string? a, string? b) => string.Join(" ", new[] { a?.Trim(), b?.Trim() }.Where(x => !string.IsNullOrEmpty(x))) is { Length: > 0 } j ? j : null;

    private static string Up(int hours) => hours >= 48 ? $"{hours / 24} days" : $"{hours} hours";

    private static IReadOnlyList<PartInfo> MemoryInfo(HealthSnapshot s, int? total)
    {
        var rows = new List<PartInfo>(Rows(("Installed", total is > 0 ? $"{total} GB" : null),
            ("Slots in use", s.MemorySlots is > 0 ? $"{s.Memory.Count(m => m.SizeGB > 0)} of {s.MemorySlots}" : null)));
        foreach (var m in s.Memory.Where(m => m.SizeGB > 0))
            rows.Add(new PartInfo(m.Slot ?? m.Bank ?? "Stick",
                string.Join(" · ", new[] { $"{m.SizeGB} GB", m.Maker?.Trim(), m.Part?.Trim(),
                    m.ConfiguredMTs > 0 ? (m.RatedMTs > m.ConfiguredMTs ? $"{m.ConfiguredMTs} MT/s (rated {m.RatedMTs})" : $"{m.ConfiguredMTs} MT/s") : null }
                    .Where(x => !string.IsNullOrWhiteSpace(x)))));
        return rows;
    }

    private static IReadOnlyList<PartInfo> DriveInfo(HealthSnapshot s, PhysicalDiskInfo d, IReadOnlyList<VolumeInfo> vols)
    {
        var rel = s.DiskReliability.FirstOrDefault(r => r.Name is not null && r.Name.Equals(d.Name, StringComparison.OrdinalIgnoreCase));
        var rows = new List<PartInfo>(Rows(
            ("Model", d.Name), ("Kind", Drives.Kind(d)), ("Bus", d.Bus),
            ("Role", d.System == true ? "Windows boots from it" : null),
            // Windows' own verdict, named as such: KOR-208-N's drive said "Healthy" with 3,904 uncorrected read errors.
            ("Windows says", d.Health is null ? null
                : (d.Operational is { Length: > 0 } op && !op.Equals("OK", StringComparison.OrdinalIgnoreCase) ? $"{d.Health} ({op})" : d.Health)
                  + (rel?.ReadErrorsUncorrected is > 0 && d.Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase) ? " -- the read errors below say otherwise" : "")),
            ("Not mounted", d.UnmountedGB is >= 1 ? $"{d.UnmountedGB:0} GB of data with no letter -- Windows is not showing it" : null),
            ("Serial", rel?.Serial),
            ("Temperature", rel?.TemperatureC is > 0 ? $"{rel.TemperatureC} °C" + (rel.TemperatureMaxC is > 0 ? $" (max {rel.TemperatureMaxC} °C)" : "") : null),
            ("Wear", rel?.WearPct is >= 0 && d.Media?.Contains("SSD", StringComparison.OrdinalIgnoreCase) == true ? $"{rel.WearPct}% used" : null),
            ("Powered on", rel?.PowerOnHours is > 0 ? $"{rel.PowerOnHours:N0} hours ({rel.PowerOnHours / 8766.0:0.#} years)" : null),
            ("Read errors", rel?.ReadErrorsUncorrected is > 0 ? $"{rel.ReadErrorsUncorrected:N0} uncorrected" : rel?.ReadErrors is not null ? $"{rel.ReadErrors:N0}" : null)));
        foreach (var v in vols)
            rows.Add(new PartInfo($"{v.Letter.ToUpperInvariant()}:", $"{v.FreeGB:0} GB free of {v.SizeGB:0} GB" + (v.Label is { Length: > 0 } l ? $" · {l}" : "")));
        return rows;
    }

    private static bool Family(string ruleKey, string[] families) => families.Contains(FixLearning.FamilyOf(ruleKey));

    /// <summary>"13th Gen Intel(R) Core(TM) i7-13700" -> "Core i7-13700"; "Intel(R) Xeon(R) W-2245 CPU @ 3.90GHz" -> "Xeon W-2245".</summary>
    public static string ShortCpu(string cpu)
    {
        var c = Regex.Replace(cpu, @"\((R|TM)\)|\bCPU\b|@.*$|\d+(st|nd|rd|th) Gen\b|\bIntel\b|\bAMD\b|\bProcessor\b", "", RegexOptions.IgnoreCase);
        return Regex.Replace(c, @"\s+", " ").Trim();
    }

    /// <summary>"NVIDIA T400 4GB" stays; "NVIDIA RTX A2000 12GB" stays; "Intel(R) UHD Graphics 770" -> "UHD Graphics 770".</summary>
    public static string ShortGpu(string gpu) => Regex.Replace(Regex.Replace(gpu, @"\((R|TM)\)|^Intel\s+", "", RegexOptions.IgnoreCase), @"\s+", " ").Trim();
}
