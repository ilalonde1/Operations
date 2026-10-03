#nullable enable
using System.Globalization;
using System.Text;
using Kor.Operations.NetworkOps.Core.Health;

namespace Kor.Operations.NetworkOps.Core.Learning;

/// <summary>A finding that opened or cleared in the window (ClearedUtc set = it has cleared, possibly in the same window).</summary>
public sealed record ChangeFinding(string Device, string RuleKey, Severity Severity, string Title, string Evidence, DateTime FirstSeenUtc, DateTime? ClearedUtc);

/// <summary>A fix or run requested in the window, and how it ended.</summary>
public sealed record ChangeAction(string Device, long ActionId, string Kind, string RequestedBy, DateTime RequestedUtc, string Status, string? Detail);

/// <summary>
/// What changed across the PCs and the rack since a moment -- the morning brief, from the database (GET /api/changes,
/// `netops changes`). Until 2026-10-02 the overnight brief was a diff of two `netops findings` dumps done by hand; Ian: "ALL
/// THIS MUST BE CODE AND DB BASED". Opened = first seen in the window; Cleared = cleared in the window having opened
/// before it; a finding that came and went inside the window is in Opened with its ClearedUtc set.
/// </summary>
public sealed record ChangesView(DateTime SinceUtc, DateTime AtUtc, IReadOnlyList<ChangeFinding> Opened, IReadOnlyList<ChangeFinding> Cleared,
    IReadOnlyList<ChangeAction> Actions, int OpenCritical, int OpenWarning, int OpenInfo)
{
    /// <summary>The brief as a person reads it: the counts now, then what is new (worst first), what cleared, what was done.</summary>
    public string Brief()
    {
        var sb = new StringBuilder();
        var since = SinceUtc.ToLocalTime().ToString("ddd d MMM HH:mm", CultureInfo.InvariantCulture);
        sb.AppendLine($"Since {since}: {Opened.Count} new, {Cleared.Count} cleared, {Actions.Count} fixes or runs.");
        sb.AppendLine($"Open now: {OpenCritical} critical, {OpenWarning} need attention, {OpenInfo} info.");

        var stillOpen = Opened.Where(f => f.ClearedUtc is null).OrderByDescending(f => f.Severity).ThenBy(f => f.Device, StringComparer.OrdinalIgnoreCase).ToList();
        if (stillOpen.Count > 0)
        {
            sb.AppendLine().AppendLine("NEW, still open:");
            foreach (var f in stillOpen) sb.AppendLine($"  [{Word(f.Severity)}] {f.Device}: {f.Title} -- {Short(f.Evidence)}");
        }
        var cameAndWent = Opened.Where(f => f.ClearedUtc is not null).ToList();
        if (cameAndWent.Count > 0)
        {
            sb.AppendLine().AppendLine("Came and went:");
            foreach (var f in cameAndWent.OrderBy(f => f.Device, StringComparer.OrdinalIgnoreCase)) sb.AppendLine($"  {f.Device}: {f.Title}");
        }
        if (Cleared.Count > 0)
        {
            sb.AppendLine().AppendLine("CLEARED:");
            foreach (var f in Cleared.OrderByDescending(f => f.Severity).ThenBy(f => f.Device, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"  {f.Device}: {f.Title} (open since {f.FirstSeenUtc.ToLocalTime():d MMM})");
        }
        if (Actions.Count > 0)
        {
            sb.AppendLine().AppendLine("DONE (fixes and runs):");
            foreach (var g in Actions.GroupBy(a => (a.Kind, a.Status)).OrderBy(g => g.Key.Kind, StringComparer.Ordinal))
                sb.AppendLine($"  {g.Key.Kind} {g.Key.Status}: {g.Count()} -- {string.Join(", ", g.Select(a => a.Device).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))}");
            var failed = Actions.Where(a => a.Status is not ("Done" or "Running" or "Requested")).ToList();
            foreach (var a in failed) sb.AppendLine($"  ! {a.Device} {a.Kind} {a.Status}: {Short(a.Detail ?? "")}");
        }
        return sb.ToString();
    }

    private static string Word(Severity s) => s switch { Severity.Critical => "CRITICAL", Severity.Warning => "attention", _ => "info" };
    private static string Short(string s) => s.Length <= 110 ? s : s[..109] + "…";
}
