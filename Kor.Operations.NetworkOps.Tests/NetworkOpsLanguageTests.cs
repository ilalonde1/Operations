#nullable enable
using System.Text.RegularExpressions;
using Kor.Operations.NetworkOps.Core.Learning;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.NetworkOps.Tests;

// THE CLASS (audit 2026-10-16, docs/KOR-NetworkOps-Audit-2026-10-16.md tier 3, per CLAUDE.md rule 11):
//   NETWORKOPS SPEAKS ONE LANGUAGE -- one way to write a plural, a dash, a date and a colour, no shouting, and every kind of
//   problem filed somewhere a person would look for it.
// Each check lists EVERY instance it finds (in the failure message and the test output) and holds a RATCHET: the count may
// only come down. When a fix lowers a count, lower its baseline to match in the same commit, so it can never creep back.
//
// WHAT IT COVERS: string literals in the user-facing NetworkOps code (App/NetworkOps .cs + the visible XAML attributes;
// Core Health/Rack/Learning/Network/Power/Updates/Actions/Bios .cs; Service Alerting .cs) for "(s)" plurals, "--" used as
// a dash, words in capitals that are not acronyms, and date format strings outside the one formatter; hex colours and
// FromRgb outside the palette (NetworkOpsStyles.xaml + NetworkOpsBrushes); every Knowledge rule family against the To-clear
// categories ("Other" is a filing failure) and catalog entries that match no family (dead).
// WHAT IT DOES NOT: text built at run time from pieces (a raw rule key interpolated into a sentence reads as "{x}" here and
// is not seen -- the digest's Cleared list is that case); text in PowerShell/Python probes and fix scripts; log lines and
// regex literals (skipped on purpose); labels written wholly in capitals (a casing choice, not emphasis); whether a
// sentence reads WELL; colours set in code by name (Brushes.Red). A SAME-CLASS FAULT IT WOULD NOT CATCH: a shouted word that
// is on the acronym list ("OK", "UPS ON BATTERY" keeps "UPS" legitimately and is caught only by "BATTERY").
public sealed class NetworkOpsLanguageTests(ITestOutputHelper output)
{
    // Ratchets: today's counts (2026-10-16). They may only come DOWN. Tier 3 of the audit takes each to zero.
    private const int PluralsBaseline = 7;   // 13 -> 7: the To-clear view's "machine(s)" (step 1)
    private const int DashesBaseline = 59;
    private const int ShoutingBaseline = 22;
    private const int DateFormatsBaseline = 34;
    private const int ColoursBaseline = 57;
    private const int OtherCategoryBaseline = 68;
    private const int DeadCategoryEntriesBaseline = 3;

    // Words of 4+ capitals that are names, not shouting.
    private static readonly HashSet<string> Acronyms = new(StringComparer.Ordinal)
    {
        "BIOS", "DHCP", "SNMP", "NTFS", "SATA", "WSUS", "LAPS", "JSON", "HTTP", "HTTPS", "VMDK", "UEFI", "TPM2", "SMART", "NVME",
        "CIFS", "IPMI", "SHA256", "WINRM", "ACPI", "DIMM", "GPIO", "CMOS", "AHCI", "RAID", "VLAN", "LACP", "SFTP", "MSRT",
        "UTC", "ESXI", "VMXNET3", "VMXNET", "iSCSI", "ISCSI", "SSID", "WLAN", "POE", "RSAT", "MSAL", "ENTRA", "KOR",
        // Names that are written in capitals: the Windows account, Windows error/registry names, vendors, products, ports.
        "SYSTEM", "WHEA", "USBSTOR", "LENOVO", "ETABS", "HDMI", "EXPO", "EEPROM", "DISM",
    };

    // Text nobody reads on a screen: a log line, or a regex.
    private static readonly Regex LogLine = new(@"\b(?:log|Log|_log|logger|Logger|Trace|Debug)\s*\.\s*\w+\s*\(|\bLog\w*\(", RegexOptions.Compiled);
    private static bool LooksLikeRegex(string literal) => literal.Contains(@"\d", StringComparison.Ordinal) || literal.Contains("(?", StringComparison.Ordinal) || literal.Contains(@"\s", StringComparison.Ordinal);

    private static readonly Regex Literal = new(@"@?\$?""(?:[^""\\]|\\.)*""", RegexOptions.Compiled);
    private static readonly Regex XamlText = new(@"\b(?:Text|Content|ToolTip|Header|Title)=""([^""{]*)""", RegexOptions.Compiled);
    private static readonly Regex Interpolation = new(@"\{[^{}]*\}", RegexOptions.Compiled);
    private static readonly Regex Shout = new(@"\b[A-Z]{4,}\b", RegexOptions.Compiled);
    private static readonly Regex DateFormat = new(@"yyyy-MM-dd|ddd d MMM|MMM d\b|\bd MMM|HH:mm", RegexOptions.Compiled);
    private static readonly Regex Hex = new(@"#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?\b|FromRgb\(|FromArgb\(", RegexOptions.Compiled);

    private static readonly string[] CoreFolders = ["Health", "Rack", "Learning", "Network", "Power", "Updates", "Actions", "Bios"];

    /// <summary>Every user-facing source file: (repo-relative path, lines).</summary>
    private static IEnumerable<(string File, string[] Lines)> Sources(bool xaml)
    {
        var root = RepoRoot();
        var dirs = new List<string> { Path.Combine(root, "Kor.Operations.App", "NetworkOps"), Path.Combine(root, "Kor.Operations.NetworkOps.Service", "Alerting") };
        dirs.AddRange(CoreFolders.Select(f => Path.Combine(root, "Kor.Operations.NetworkOps.Core", f)));
        foreach (var dir in dirs.Where(Directory.Exists))
            foreach (var f in Directory.GetFiles(dir, xaml ? "*.xaml" : "*.cs").OrderBy(f => f, StringComparer.Ordinal))
                yield return (Path.GetRelativePath(root, f), File.ReadAllLines(f));
    }

    private static string Code(string line)
    {
        // Drop a trailing // comment, but not one inside a string ("https://...").
        var inString = false;
        for (var i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\')) inString = !inString;
            if (!inString && line[i] == '/' && line[i + 1] == '/') return line[..i];
        }
        return line;
    }

    private static bool IsDocOrAttribute(string trimmed) => trimmed.StartsWith("///", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal);

    /// <summary>Each visible piece of text: C# string literals (outside comments, doc comments and attributes) and XAML text attributes.</summary>
    private static IEnumerable<(string Where, string Text)> Texts()
    {
        foreach (var (file, lines) in Sources(xaml: false))
            for (var i = 0; i < lines.Length; i++)
            {
                var t = lines[i].TrimStart();
                if (IsDocOrAttribute(t) || LogLine.IsMatch(lines[i])) continue;
                foreach (Match m in Literal.Matches(Code(lines[i])))
                    if (!LooksLikeRegex(m.Value)) yield return ($"{file}:{i + 1}", m.Value);
            }
        foreach (var (file, lines) in Sources(xaml: true))
            for (var i = 0; i < lines.Length; i++)
                foreach (Match m in XamlText.Matches(lines[i]))
                    yield return ($"{file}:{i + 1}", m.Groups[1].Value);
    }

    private void Ratchet(string what, IReadOnlyCollection<string> found, int baseline)
    {
        output.WriteLine($"{what}: {found.Count} (baseline {baseline})");
        foreach (var f in found) output.WriteLine("  " + f);
        Assert.True(found.Count <= baseline,
            $"{what}: {found.Count} found, the ratchet allows {baseline}. Every instance:\n" + string.Join("\n", found));
        if (found.Count < baseline)
            output.WriteLine($"  -> lower {what} baseline to {found.Count}");
    }

    [Fact]
    public void Plurals_are_written_out_not_with_a_bracketed_s()
        => Ratchet("Plurals", Texts().Where(t => t.Text.Contains("(s)", StringComparison.Ordinal)).Select(t => $"{t.Where}: {t.Text}").ToList(), PluralsBaseline);

    [Fact]
    public void A_dash_is_a_dash_not_two_hyphens()
        => Ratchet("Dashes", Texts().Where(t => t.Text.Contains(" -- ", StringComparison.Ordinal)).Select(t => $"{t.Where}: {t.Text}").ToList(), DashesBaseline);

    // Shouting = capitals used for emphasis INSIDE a sentence ("this port's link is DOWN"). A label written wholly in
    // capitals ("CLEAR IN ONE ACTION", "NOT ANSWERING") is a casing style, not emphasis, and is not counted here.
    [Fact]
    public void Nothing_shouts()
        => Ratchet("Shouting", Texts()
            .Where(t => Interpolation.Replace(t.Text, "").Any(char.IsLower))
            .Select(t => (t.Where, t.Text, Words: Shout.Matches(Interpolation.Replace(t.Text, "")).Select(m => m.Value).Where(w => !Acronyms.Contains(w)).ToList()))
            .Where(t => t.Words.Count > 0)
            .Select(t => $"{t.Where}: [{string.Join(" ", t.Words)}] {t.Text}").ToList(), ShoutingBaseline);

    [Fact]
    public void Dates_are_formatted_in_one_place()
    {
        var found = new List<string>();
        foreach (var (file, lines) in Sources(xaml: false))
            for (var i = 0; i < lines.Length; i++)
                if (!IsDocOrAttribute(lines[i].TrimStart()) && DateFormat.Match(Code(lines[i])) is { Success: true } m)
                    found.Add($"{file}:{i + 1}: {m.Value}   {lines[i].Trim()}");
        Ratchet("Date formats", found, DateFormatsBaseline);
    }

    [Fact]
    public void Colours_come_from_the_palette()
    {
        var found = new List<string>();
        foreach (var (file, lines) in Sources(xaml: true).Concat(Sources(xaml: false).Where(s => s.File.Contains($"{Path.DirectorySeparatorChar}NetworkOps{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                                                                              && s.File.StartsWith("Kor.Operations.App", StringComparison.Ordinal))))
        {
            if (file.EndsWith("NetworkOpsStyles.xaml", StringComparison.Ordinal)) continue;
            var inPalette = false;
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("class NetworkOpsBrushes", StringComparison.Ordinal)) inPalette = true;
                else if (inPalette && lines[i].StartsWith('}')) inPalette = false;
                if (inPalette || lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal) || lines[i].TrimStart().StartsWith("<!--", StringComparison.Ordinal)) continue;
                foreach (Match m in Hex.Matches(Code(lines[i])))
                    found.Add($"{file}:{i + 1}: {m.Value}");
            }
        }
        Ratchet("Colours", found, ColoursBaseline);
    }

    [Fact]
    public void Every_kind_of_problem_is_filed_where_a_person_would_look()
        => Ratchet("Other category", Knowledge.Families.Where(f => ToClearCategories.CategoryOf(f) == "Other").OrderBy(f => f, StringComparer.Ordinal).ToList(), OtherCategoryBaseline);

    [Fact]
    public void Every_category_entry_files_something()
    {
        // A catalog key no Knowledge family matches files nothing (a typo, or a rule that was renamed or never written).
        var families = Knowledge.Families.ToList();
        var dead = CategoryKeys().Where(k => !families.Any(f => f.Equals(k, StringComparison.OrdinalIgnoreCase) || f.StartsWith(k, StringComparison.OrdinalIgnoreCase)))
            .Select(k => $"'{k}' matches no rule family").ToList();
        Ratchet("Dead category entries", dead, DeadCategoryEntriesBaseline);
    }

    /// <summary>The catalog's keys, read from the source (the catalog is private, and reflection on a tuple array is noisier than this).</summary>
    private static IEnumerable<string> CategoryKeys()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "Kor.Operations.NetworkOps.Core", "Learning", "ToClearCategories.cs"));
        var catalog = src[src.IndexOf("Catalog =", StringComparison.Ordinal)..src.IndexOf("];", src.IndexOf("Catalog =", StringComparison.Ordinal), StringComparison.Ordinal)];
        return Regex.Matches(catalog, @"\[([^\[\]]*)\]\)").SelectMany(m => Regex.Matches(m.Groups[1].Value, @"""([^""]+)""").Select(k => k.Groups[1].Value));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.NetworkOps.Agent"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found above " + AppContext.BaseDirectory);
    }
}
