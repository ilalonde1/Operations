#nullable enable
using System.Globalization;

namespace Kor.Operations.NetworkOps.Core.Health;

/// <summary>
/// A drive in the words a person uses: "the data drive D: (2 TB hard drive)", "the system drive C: (512 GB NVMe SSD)".
/// Ian, 2026-10-02, on "ST2000DM006-2DM164: 3904 uncorrected read errors": "which disk is this? His system disk? His
/// secondary disk?" A finding names the drive this way; the model stays in the evidence for whoever orders the replacement.
/// Needs probe v10 (letters + system per physical drive); before it, the drive is named by its model alone, as it was.
/// </summary>
public static class Drives
{
    public static PhysicalDiskInfo? Find(HealthSnapshot s, string? name)
        => name is null ? null : s.PhysicalDisks.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The letters on a drive ("C:", "D:, E:"), or null when the probe did not say.</summary>
    public static IReadOnlyList<string> LettersOf(PhysicalDiskInfo d)
        => (d.Letters ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(l => l.ToUpperInvariant() + ":").ToList();

    /// <summary>"the system drive C:" / "the data drive D:" / "a drive with no letter", or null before probe v10.</summary>
    public static string? Role(PhysicalDiskInfo d)
    {
        if (d.Letters is null && d.System is null) return null;
        var letters = LettersOf(d);
        var where = letters.Count > 0 ? " " + string.Join(", ", letters) : "";
        return d.System == true ? $"the system drive{where}" : letters.Count > 0 ? $"the data drive{where}" : "a drive with no letter";
    }

    /// <summary>"2 TB hard drive", "512 GB NVMe SSD": the size as the box says it (decimal), and the kind.</summary>
    public static string Kind(PhysicalDiskInfo d)
    {
        var kind = string.Equals(d.Media, "HDD", StringComparison.OrdinalIgnoreCase) ? "hard drive"
            : string.Equals(d.Bus, "NVMe", StringComparison.OrdinalIgnoreCase) ? "NVMe SSD"
            : string.Equals(d.Media, "SSD", StringComparison.OrdinalIgnoreCase) ? "SSD"
            : string.Equals(d.Bus, "USB", StringComparison.OrdinalIgnoreCase) ? "USB drive" : "drive";
        return d.SizeGB > 0 ? $"{Size(d.SizeGB)} {kind}" : kind;
    }

    /// <summary>Windows' GiB as the label on the drive reads (decimal, rounded to a marketed size): 1863 -> "2 TB", 477 -> "512 GB".</summary>
    public static string Size(double gib)
    {
        var gb = gib * 1.073741824;
        if (gb >= 900) return (Math.Round(gb / 1000 * 2, MidpointRounding.AwayFromZero) / 2).ToString("0.#", CultureInfo.InvariantCulture) + " TB";
        // The NEAREST marketed size (the first within reach read 512 GB as 480 -- caught by DrivesTests).
        var nearest = new[] { 64, 120, 128, 240, 250, 256, 480, 500, 512 }.MinBy(m => Math.Abs(gb - m));
        return Math.Abs(gb - nearest) / nearest < 0.05 ? $"{nearest} GB" : $"{Math.Round(gb):0} GB";
    }

    /// <summary>"the data drive D: (2 TB hard drive)", or the model name alone before probe v10.</summary>
    public static string Describe(HealthSnapshot s, string name)
        => Find(s, name) is { } d && Role(d) is { } role ? $"{role} ({Kind(d)})" : name;

    /// <summary>Sentence-start form of <see cref="Describe"/>: "The data drive D: (2 TB hard drive)".</summary>
    public static string Capitalised(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>A finding's title about one drive: "The data drive D: has unrecoverable read errors", or <paramref name="fallback"/>
    /// ("A drive has unrecoverable read errors") when the probe could not say which drive it is.</summary>
    public static string Title(HealthSnapshot s, string name, string predicate, string fallback)
        => Find(s, name) is { } d && Role(d) is { } role ? $"{Capitalised(role)} {predicate}" : fallback;

    /// <summary>The model with its kind for the evidence: "ST2000DM006-2DM164 (2 TB hard drive)", or the model alone.</summary>
    public static string Model(HealthSnapshot s, string name) => Find(s, name) is { } d ? $"{name} ({Kind(d)})" : name;

    /// <summary>The physical drive a letter lives on, when the probe said (v10).</summary>
    public static PhysicalDiskInfo? OfLetter(HealthSnapshot s, string letter)
        => s.PhysicalDisks.FirstOrDefault(d => LettersOf(d).Contains(letter.TrimEnd(':').ToUpperInvariant() + ":"));
}
