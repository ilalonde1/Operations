#nullable enable
using System.Text.RegularExpressions;
using Kor.Operations.NetworkOps.Core.Health;

namespace Kor.Operations.NetworkOps.Core.Learning;

// What a PC IS, as stable key/value facts. Everything the learning layer knows about a machine's
// identity flows through here: its history ("the GPU driver changed on 30 Sep"), the fleet
// comparison ("4 of 4 PCs on this driver hang") and what fixed a fault ("cleared after the BIOS
// update"). Keys are stable and lower-case; values are exactly as the machine reports them.
//
// Deliberately NOT facts: things that change by the hour (logged-on user, free space, uptime) --
// those are Metrics. A fact that flapped would bury real changes in noise.
public static partial class Facts
{
    public const string Model = "hw.model";
    public const string Board = "hw.board";
    public const string Bios = "bios.version";
    public const string BiosDate = "bios.date";
    public const string Cpu = "cpu";
    public const string RamGb = "ram.gb";
    public const string OsBuild = "os.build";        // 26200.9550 -- changes with every cumulative update
    public const string OsRelease = "os.release";    // 25H2
    public const string OfficeBuild = "office.build";
    public const string OfficeChannel = "office.channel";
    public const string GpuName = "gpu.name";
    public const string GpuDriver = "gpu.driver";
    public const string AccessEngine = "access.engine";
    public const string AppPrefix = "app.";          // app.revit.2025 = 25.4.60.9, app.bluebeam = 21.11.0 ...

    /// <summary>
    /// The facts that can explain a fleet pattern. Serial numbers, BIOS dates and the like are
    /// unique per machine and would "explain" everything -- they are recorded, never correlated.
    /// </summary>
    public static bool IsCorrelatable(string key) =>
        key is Model or Board or Bios or Cpu or RamGb or OsRelease or OfficeBuild or OfficeChannel or GpuName or GpuDriver or AccessEngine
        || key.StartsWith(AppPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Null when the snapshot cannot vouch for the inventory (broken WMI, the block failed): absence
    /// of evidence must never supersede what is known -- a PC whose WMI broke did not lose its GPU.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? Extract(HealthSnapshot s)
    {
        if (s.WmiHealthy == false || s.Inventory is null) return null;
        var inv = s.Inventory;
        var f = new SortedDictionary<string, string>(StringComparer.Ordinal);
        void Put(string k, string? v) { if (!string.IsNullOrWhiteSpace(v)) f[k] = v.Trim(); }

        // Self-built PCs report placeholder system strings; the motherboard is their identity then.
        var placeholder = IsPlaceholder(inv.Manufacturer) || IsPlaceholder(inv.Model);
        Put(Model, placeholder ? $"{inv.BoardMaker} {inv.BoardProduct}".Trim() : $"{Pretty(inv.Manufacturer)} {inv.Model}".Trim());
        Put(Board, $"{inv.BoardMaker} {inv.BoardProduct}".Trim());
        Put(Bios, inv.BiosVersion);
        Put(BiosDate, inv.BiosDate);
        Put(Cpu, Regex.Replace(inv.Cpu ?? "", @"\((R|TM)\)|\s+CPU\s+@.*$", "", RegexOptions.IgnoreCase).Replace("  ", " "));
        if (inv.RamGB is > 0) Put(RamGb, inv.RamGB.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (s.Os is { Build: > 0 } os) { Put(OsBuild, $"{os.Build}.{os.Ubr}"); Put(OsRelease, os.DisplayVersion); }
        Put(OfficeBuild, inv.OfficeBuild);
        Put(OfficeChannel, inv.OfficeChannel);

        // The discrete card is the one that matters; Intel/Microsoft adapters are the fallback.
        var cards = s.DisplayAdapters.Where(a => !string.IsNullOrWhiteSpace(a.Name)).ToList();
        var gpu = cards.FirstOrDefault(a => !IsIntegrated(a.Name!)) ?? cards.FirstOrDefault(a => !a.Name!.Contains("Remote Display", StringComparison.OrdinalIgnoreCase));
        if (gpu is not null) { Put(GpuName, gpu.Name); Put(GpuDriver, gpu.Driver); }

        foreach (var (key, version) in AppFacts(inv.Apps ?? [])) Put(key, version);
        return f;
    }

    /// <summary>Key applications, one fact each, collapsed from the noisy uninstall list (update and content packs dropped).</summary>
    internal static IEnumerable<(string Key, string Version)> AppFacts(IEnumerable<AppVersion> apps)
    {
        var picked = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var a in apps)
        {
            var n = a.Name.Trim();
            var v = string.IsNullOrWhiteSpace(a.Version) ? "installed" : a.Version!.Trim();
            string? key = null;
            if (Rx.RevitYear().Match(n) is { Success: true } r && !Rx.RevitNoise().IsMatch(n)) key = $"{AppPrefix}revit.{r.Groups[1].Value}";
            else if (Rx.AutoCadYear().Match(n) is { Success: true } c) key = $"{AppPrefix}autocad.{c.Groups[1].Value}";
            else if (n.StartsWith("Bluebeam Revu", StringComparison.OrdinalIgnoreCase)) key = $"{AppPrefix}bluebeam";
            else if (Rx.CsiProduct().Match(n) is { Success: true } csi) key = $"{AppPrefix}{csi.Groups[1].Value.ToLowerInvariant()}.{csi.Groups[2].Value}";
            else if (n.StartsWith("Microsoft Access database engine", StringComparison.OrdinalIgnoreCase)) key = Rx.Year().Match(n) is { Success: true } y ? $"{AccessEngine}.{y.Value}" : AccessEngine;
            else if (n.StartsWith("Tekla", StringComparison.OrdinalIgnoreCase)) key = $"{AppPrefix}tekla";
            if (key is null) continue;
            // Two entries for one product (base + update): the higher version is what runs.
            if (!picked.TryGetValue(key, out var have) || CompareVersions(v, have) > 0) picked[key] = v;
        }
        return picked.Select(kv => (kv.Key, kv.Value));
    }

    private static int CompareVersions(string a, string b)
        => Version.TryParse(a, out var va) && Version.TryParse(b, out var vb) ? va.CompareTo(vb) : string.CompareOrdinal(a, b);

    private static bool IsPlaceholder(string? s)
        => string.IsNullOrWhiteSpace(s) || s.Contains("System manufacturer", StringComparison.OrdinalIgnoreCase)
           || s.Contains("System Product Name", StringComparison.OrdinalIgnoreCase) || s.Contains("To be filled", StringComparison.OrdinalIgnoreCase);

    private static bool IsIntegrated(string name)
        => name.StartsWith("Intel", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase);

    private static string? Pretty(string? maker) => maker?.Equals("LENOVO", StringComparison.OrdinalIgnoreCase) == true ? "Lenovo" : maker;

    private static partial class Rx
    {
        [GeneratedRegex(@"^(?:Autodesk )?Revit (20\d\d)\b", RegexOptions.IgnoreCase)] public static partial Regex RevitYear();
        [GeneratedRegex(@"MEP|Content|Fabrication|Update|Hotfix|\d{4}\.\d", RegexOptions.IgnoreCase)] public static partial Regex RevitNoise();
        [GeneratedRegex(@"^AutoCAD (20\d\d)\b", RegexOptions.IgnoreCase)] public static partial Regex AutoCadYear();
        [GeneratedRegex(@"^(ETABS|SAFE) (\d{2,4})\b")] public static partial Regex CsiProduct();
        [GeneratedRegex(@"20\d\d")] public static partial Regex Year();
    }
}

/// <summary>What changed in a PC's facts since the last sweep.</summary>
public sealed record FactChange(string Fact, string? OldValue, string? NewValue);

public static class FactDiff
{
    /// <param name="current">The facts on record now (not superseded).</param>
    /// <param name="observed">What the latest snapshot says. Null = the snapshot could not vouch: change nothing.</param>
    public static IReadOnlyList<FactChange> Compute(IReadOnlyDictionary<string, string> current, IReadOnlyDictionary<string, string>? observed)
    {
        if (observed is null) return [];
        var changes = new List<FactChange>();
        foreach (var (k, v) in observed)
            if (!current.TryGetValue(k, out var old)) changes.Add(new(k, null, v));
            else if (!string.Equals(old, v, StringComparison.Ordinal)) changes.Add(new(k, old, v));
        foreach (var (k, old) in current)
            if (!observed.ContainsKey(k)) changes.Add(new(k, old, null));   // e.g. an app uninstalled
        return changes;
    }
}
