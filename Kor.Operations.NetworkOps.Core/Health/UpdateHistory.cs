#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kor.Operations.NetworkOps.Core.Health;

/// <summary>
/// Which Windows-Update history entries are genuine OS patches vs drivers/other.
/// A real Windows/Office security or quality update carries a KB number in its title
/// ("2026-10 Cumulative Update ... (KB5127648)", "Servicing Stack ... (KBxxxxxxx)",
/// ".NET ... (KBxxxxxxx)", "Update for Windows (KB5124010)", MSRT "(KB890830)").
/// A driver is "Vendor - Category - version" with no KB
/// ("Silicon Laboratories Inc. - Ports - 11.2.0.167", "NVIDIA - Display - 31.0.15.3699").
/// A feature update carries no KB ("Windows 11, version 25H2", "Feature update to Windows 11, version 24H2") and is a
/// patch too: it brings the machine to a fully patched new release (audit 2026-10-16, tier 2 -- it read as "other").
/// Defender signatures and Store apps are already dropped upstream in the probe.
/// COVERS: KB-tagged Microsoft/Office updates and Windows feature updates; excludes driver/vendor entries.
/// DOES NOT: classify an out-of-band whose title carries neither a KB nor "Windows .. version"; and it classifies by
/// TITLE only, so a renamed/odd title is a same-class fault it misses.
/// </summary>
public static class UpdateHistory
{
    private static readonly Regex Kb = new(@"\bKB\d{5,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FeatureUpdate = new(@"\bWindows (10|11|Server)\b.*\bversion \d{2}H\d\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsOsPatch(string? title) => title is not null && (Kb.IsMatch(title) || FeatureUpdate.IsMatch(title));

    public static UpdateHistoryItem? LastPatch(IReadOnlyList<UpdateHistoryItem>? recent)
        => recent?.Where(h => IsOsPatch(h.Title)).OrderByDescending(h => h.Date).FirstOrDefault();

    public static UpdateHistoryItem? LastOther(IReadOnlyList<UpdateHistoryItem>? recent)
        => recent?.Where(h => !IsOsPatch(h.Title)).OrderByDescending(h => h.Date).FirstOrDefault();
}
