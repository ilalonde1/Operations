#nullable enable
using System.Text.RegularExpressions;
using Kor.Operations.NetworkOps.Core.Learning;

namespace Kor.Operations.NetworkOps.Core.Health;

/// <summary>One part of a PC as a tile on its page: what it is, two short lines, a fill bar for a drive, and the open
/// findings about it (their worst severity colours the tile; clicking it picks the first).</summary>
/// <param name="Kind">cpu | memory | gpu | drive | missing-drive | windows | bios</param>
/// <param name="FillPct">A drive's used space, 0-100; null for anything else.</param>
/// <param name="Worst">The worst open finding about this part, or null when nothing is open on it.</param>
public sealed record PcComponent(string Kind, string Title, string Line1, string Line2, double? FillPct, Severity? Worst,
    IReadOnlyList<string> RuleKeys, bool IsSystem = false);

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
            list.Add(Make("cpu", "CPU", ShortCpu(cpu), inv.Cores is > 0 ? $"{inv.Cores} cores" : "", null, open, _ => false));

        if (inv?.RamGB is > 0 || s.Memory.Count > 0)
        {
            var total = inv?.RamGB ?? s.Memory.Sum(m => m.SizeGB);
            var sticks = s.Memory.Where(m => m.SizeGB > 0).ToList();
            var layout = sticks.Count == 0 ? ""
                : (sticks.Select(m => m.SizeGB).Distinct().Count() == 1 ? $"{sticks.Count} × {sticks[0].SizeGB} GB" : $"{sticks.Count} sticks")
                  + (sticks.Max(m => m.ConfiguredMTs) is > 0 and var mts ? $" · {mts} MT/s" : "");
            list.Add(Make("memory", "Memory", $"{total} GB", layout, null, open, k => Family(k, MemoryRules)));
        }

        var gpus = s.DisplayAdapters.Where(a => a.Name is { Length: > 0 } n && !n.Contains("Remote Display", StringComparison.OrdinalIgnoreCase)
                                                 && !n.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)).ToList();
        // The discrete card is the one that matters (an Intel iGPU beside an NVIDIA card is the display path, not the workload).
        var gpu = gpus.FirstOrDefault(a => !a.Name!.StartsWith("Intel", StringComparison.OrdinalIgnoreCase)) ?? gpus.FirstOrDefault();
        if (gpu is not null)
            list.Add(Make("gpu", "Graphics", ShortGpu(gpu.Name!), gpu.Driver is { Length: > 0 } drv ? $"driver {drv}" : "", null, open, k => Family(k, GpuRules)));

        list.AddRange(DriveTiles(s, open));

        foreach (var m in s.MissingDisks.Where(d => d.InstanceId is not null && !d.InstanceId.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase)))
            list.Add(Make("missing-drive", s.OrphanDriveLetters.Count > 0 ? $"{string.Join(", ", s.OrphanDriveLetters.Select(l => l + ":"))}  Missing" : "Missing drive",
                m.Name ?? "a drive", "Windows no longer sees it", null, open, k => k == "disk-missing"));

        if (s.Os is { } os)
            list.Add(Make("windows", "Windows", $"{(os.Product?.Contains("Windows 10", StringComparison.OrdinalIgnoreCase) == true ? "10" : "11")} {os.DisplayVersion}".Trim(),
                os.Build > 0 ? $"build {os.Build}.{os.Ubr}" : "", null, open, k => Family(k, WindowsRules)));

        if (inv?.BiosVersion is { Length: > 0 } bios)
            list.Add(Make("bios", "BIOS", bios.Trim(), inv.BiosDate is { Length: > 0 } bd ? bd : "", null, open, k => Family(k, BiosRules)));

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
                    isSystem: d.System == true);
            }
            yield break;
        }
        // Before probe v10 the drive behind a letter is unknown: one tile per letter, its space only, no drive findings.
        foreach (var v in s.Volumes.Where(v => v.SizeGB > 0).OrderBy(v => v.Letter, StringComparer.Ordinal))
            yield return Make("drive", $"{v.Letter}:", $"{Drives.Size(v.SizeGB)}", v.Label ?? "", Math.Round((v.SizeGB - v.FreeGB) / v.SizeGB * 100), open,
                k => k == $"low-disk:{v.Letter.ToLowerInvariant()}", isSystem: v.Letter.Equals("C", StringComparison.OrdinalIgnoreCase));
    }

    private static PcComponent Make(string kind, string title, string line1, string line2, double? fill,
        IReadOnlyList<(string RuleKey, Severity Severity)> open, Func<string, bool> about, bool isSystem = false)
    {
        var mine = open.Where(f => about(f.RuleKey)).OrderByDescending(f => f.Severity).ToList();
        return new PcComponent(kind, title, line1, line2, fill, mine.Count > 0 ? mine[0].Severity : null, mine.Select(f => f.RuleKey).ToList(), isSystem);
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
