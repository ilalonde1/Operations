#nullable enable
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Kor.Operations.NetworkOps.Core.Health;

namespace Kor.Operations.NetworkOps.Core.Bios;

/// <summary>What a PC is, for Lenovo's package rules: its BIOS ID (the first word of SMBIOSBIOSVersion -- a ThinkPad reports
/// "N4JET28W (1.18 )", Lenovo's rules name "N4JET28*"), Windows 11 or 10, and the CPU's address width.</summary>
public sealed record BiosFacts(string BiosId, bool Windows11, int AddressWidth = 64)
{
    public static string IdOf(string smbiosBiosVersion) => smbiosBiosVersion.Trim().Split(' ', 2)[0];
}

public enum Tri { False, True, Unknown }

/// <summary>Behind = Lenovo's package installs over this BIOS. Unknown = the package decides it by something this code does
/// not read (a device or firmware check), so nothing is claimed and nothing is flashed.</summary>
public enum BiosVerdict { NotApplicable, Current, Behind, Unknown }

/// <summary>
/// Lenovo's package rule language (the DetectInstall and Dependencies of a package descriptor), evaluated as Lenovo's
/// updaters do for the conditions a BIOS decision uses: And / Or / Not, _Bios (level wildcards), _OS, _CPUAddressWidth.
/// Anything else (_PnPID, _Firmware, _Driver, ...) is Unknown, and Unknown propagates: three-valued logic, so a rule is
/// only True or False when what decides it was actually read. The SAME rules run on the PC (Actions/update-bios.ps1);
/// BiosTests runs both over Lenovo's real descriptors and requires the same answer.
/// </summary>
public static class LenovoRules
{
    public static Tri Eval(XElement e, BiosFacts f) => e.Name.LocalName switch
    {
        "And" or "DetectInstall" or "Dependencies" => All(e.Elements().Select(c => Eval(c, f)).ToList()),
        "Or" => Any(e.Elements().Select(c => Eval(c, f)).ToList()),
        "Not" => Not(All(e.Elements().Select(c => Eval(c, f)).ToList())),
        "_Bios" => e.Elements("Level").Any(l => LevelMatches(f.BiosId, l.Value)) ? Tri.True : Tri.False,
        "_OS" => e.Elements("OS").Any(o => OsBase(o.Value) == (f.Windows11 ? "WIN11" : "WIN10")) ? Tri.True : Tri.False,
        "_CPUAddressWidth" => e.Elements("AddressWidth").Any(a => a.Value.Trim() == f.AddressWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)) ? Tri.True : Tri.False,
        _ => Tri.Unknown,
    };

    // An empty And is True (nothing required) -- but an empty DetectInstall is handled by the caller as "never installed".
    private static Tri All(IReadOnlyList<Tri> xs) => xs.Contains(Tri.False) ? Tri.False : xs.Contains(Tri.Unknown) ? Tri.Unknown : Tri.True;
    private static Tri Any(IReadOnlyList<Tri> xs) => xs.Contains(Tri.True) ? Tri.True : xs.Contains(Tri.Unknown) ? Tri.Unknown : Tri.False;
    private static Tri Not(Tri x) => x switch { Tri.True => Tri.False, Tri.False => Tri.True, _ => Tri.Unknown };

    /// <summary>"WIN10-PRO.*" -> "WIN10": the OS family (KOR's PCs are Pro or Enterprise, which every edition list includes).</summary>
    private static string OsBase(string os) => Regex.Match(os.Trim().ToUpperInvariant(), "^WIN\\d+").Value;

    /// <summary>Lenovo's level match: '*' any run of characters, '?' one; case-insensitive, whole string.</summary>
    public static bool LevelMatches(string biosId, string level)
        => Regex.IsMatch(biosId.Trim(), "^" + Regex.Escape(level.Trim()).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase);
}

/// <summary>One Lenovo BIOS package, as its descriptor (the XML a machine type's catalog points to) states it.</summary>
/// <param name="Severity">Lenovo's rating: 1 critical, 2 recommended, 3 optional.</param>
public sealed record LenovoBiosPackage(string Id, string Version, string Title, DateTime? Released, int Severity,
    XElement? DetectInstall, XElement? Dependencies, string Installer, string? Sha256, string DescriptorUrl)
{
    public static LenovoBiosPackage Parse(string xml, string descriptorUrl)
    {
        var p = XDocument.Parse(xml.TrimStart('﻿')).Root is { Name.LocalName: "Package" } root ? root : throw new FormatException("not a Lenovo package descriptor");
        var installer = p.Element("Files")?.Element("Installer")?.Element("File");
        var released = DateTime.TryParse((string?)p.Element("ReleaseDate"), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d : (DateTime?)null;
        return new LenovoBiosPackage(
            (string?)p.Attribute("id") ?? throw new FormatException("package has no id"),
            (string?)p.Attribute("version") ?? throw new FormatException("package has no version"),
            ((string?)p.Element("Title")?.Element("Desc") ?? "").Trim(),
            released,
            int.TryParse((string?)p.Element("Severity")?.Attribute("type"), out var sev) ? sev : 2,
            p.Element("DetectInstall"), p.Element("Dependencies"),
            ((string?)installer?.Element("Name") ?? throw new FormatException("package names no installer")).Trim(),
            ((string?)installer?.Element("CRC"))?.Trim(),
            descriptorUrl);
    }

    /// <summary>Lenovo's answer for this PC: already installed (DetectInstall), installs over it (Dependencies), neither, or cannot tell.</summary>
    public BiosVerdict Judge(BiosFacts f)
    {
        var installed = DetectInstall is { HasElements: true } di ? LenovoRules.Eval(di, f) : Tri.False;
        if (installed == Tri.True) return BiosVerdict.Current;
        if (installed == Tri.Unknown) return BiosVerdict.Unknown;
        return (Dependencies is null ? Tri.False : LenovoRules.Eval(Dependencies, f)) switch
        {
            Tri.True => BiosVerdict.Behind,
            Tri.Unknown => BiosVerdict.Unknown,
            _ => BiosVerdict.NotApplicable,
        };
    }
}

/// <summary>The BIOS package locations a machine type's catalog lists (category "BIOS" / "BIOS UEFI").</summary>
public static class LenovoCatalog
{
    public static string CatalogUrl(string machineType, bool windows11) => $"https://download.lenovo.com/catalog/{machineType.ToUpperInvariant()}_{(windows11 ? "Win11" : "Win10")}.xml";

    /// <summary>The first four characters of a Lenovo product name ("30DKS0QN00") are its machine type, or null.</summary>
    public static string? MachineTypeOf(string? manufacturer, string? machineType)
        => manufacturer is not null && manufacturer.Contains("LENOVO", StringComparison.OrdinalIgnoreCase)
           && machineType is { Length: >= 4 } mt && Regex.IsMatch(mt[..4], "^[0-9A-Za-z]{4}$") ? mt[..4].ToUpperInvariant() : null;

    public static IReadOnlyList<string> BiosLocations(string catalogXml)
        => XDocument.Parse(catalogXml.TrimStart('﻿')).Descendants("package")
            .Where(p => Regex.IsMatch((string?)p.Element("category") ?? "", "\\bBIOS\\b|\\bUEFI\\b", RegexOptions.IgnoreCase))
            .Select(p => ((string?)p.Element("location") ?? "").Trim()).Where(l => l.StartsWith("https://download.lenovo.com/", StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>
    /// The verdict across every BIOS package a catalog lists, in the order the fix walks them (update-bios.ps1): the first
    /// package saying Current or Behind decides; Unknown only when nothing decided and something could not be judged.
    /// </summary>
    public static (BiosVerdict Verdict, LenovoBiosPackage? Package) Judge(IReadOnlyList<LenovoBiosPackage> packages, BiosFacts f)
    {
        var unknown = false;
        foreach (var p in packages)
            switch (p.Judge(f))
            {
                case BiosVerdict.Current: return (BiosVerdict.Current, p);
                case BiosVerdict.Behind: return (BiosVerdict.Behind, p);
                case BiosVerdict.Unknown: unknown = true; break;
            }
        return (unknown ? BiosVerdict.Unknown : BiosVerdict.NotApplicable, null);
    }
}

/// <summary>
/// "A newer BIOS is available": raised on a Lenovo PC whose BIOS Lenovo's current package installs over -- the same answer
/// Lenovo's updater gives. Nothing is raised when the PC is current, newer, outside the package, or the package decides
/// by a check this code cannot read (Unknown). Severity: Lenovo's "critical" package is a Warning; recommended or optional
/// is Info (offered, never alarmed on).
/// </summary>
public static class BiosRules
{
    public const string Rule = "bios-behind";

    public static IReadOnlyList<Finding> Evaluate(InventoryInfo? inventory, bool windows11, IReadOnlyList<LenovoBiosPackage> packages)
    {
        if (inventory?.BiosVersion is not { Length: > 0 } bios) return [];
        var (verdict, p) = LenovoCatalog.Judge(packages, new BiosFacts(BiosFacts.IdOf(bios), windows11));
        if (verdict != BiosVerdict.Behind || p is null) return [];
        return [new Finding(Rule, p.Severity == 1 ? Severity.Warning : Severity.Info, "A newer BIOS is available",
            $"{bios.Trim()} -> {p.Version}{(p.Released is { } r ? $" (Lenovo, {r:yyyy-MM-dd})" : "")} | {p.Title} | package {p.Id}")];
    }
}
