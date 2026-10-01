#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Kor.Operations.NetworkOps.Core.Health;

namespace Kor.Operations.NetworkOps.Core.Updates;

/// <summary>One update Windows Update has waiting on a machine (Probes/updates.ps1).</summary>
/// <param name="Security">In "Security Updates" or "Critical Updates".</param>
/// <param name="Severity">Microsoft's rating (Critical, Important, Moderate, Low), or empty when it gives none.</param>
/// <param name="Released">When Microsoft published it (LastDeploymentChangeTime).</param>
public sealed record PendingUpdate(string? Kb, string Title, string? Categories, bool Security, string? Severity, DateTime? Released,
    double SizeMB, bool Downloaded, bool NeedsReboot);

/// <summary>What one machine's Windows Update search found.</summary>
public sealed record UpdateScan(int ScanVersion, DateTime ScannedAt, string Computer, IReadOnlyList<PendingUpdate> Updates, bool RebootPending, int SearchMs)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString };

    /// <summary>Reads a scan -- a bare object or the one-element array the payload publishes; a one-update list may arrive as a bare object.</summary>
    public static UpdateScan Parse(string json)
    {
        var root = JsonNode.Parse(json) ?? throw new JsonException("empty update scan");
        var obj = (root is JsonArray a ? a[0] : root)?.AsObject() ?? throw new JsonException("empty update scan");
        var key = obj.Select(kv => kv.Key).FirstOrDefault(k => k.Equals(nameof(Updates), StringComparison.OrdinalIgnoreCase));
        if (key is not null && obj[key] is JsonObject single) { obj[key] = null; obj[key] = new JsonArray(single.DeepClone()); }
        var s = obj.Deserialize<UpdateScan>(Json) ?? throw new JsonException("empty update scan");
        return s with { Updates = (s.Updates ?? []).Where(u => u is not null).Select(u => u with { Severity = string.IsNullOrWhiteSpace(u.Severity) ? null : u.Severity }).ToList() };
    }
}

// When a machine's updates are due, as a finding the Command Center shows and the digest mails. Nothing here installs
// anything: patching is on demand (Ian, 2026-10-01: "not on a schedule"), from the Command Center's Updates view, on
// as many machines at once as are ticked. The finding is how he knows WHEN.
//
// Security updates only drive the severity; other software updates are listed, never alarmed on:
//   - inside the first 3 days of a security update            Info     (the hold: a bad Patch Tuesday update gets pulled)
//   - a security update out 3 days or more                    Warning  (install it this week)
//   - a security update out 14 days or more                   Critical
//   - an OUT-OF-BAND update Microsoft rates Critical            Critical at once -- not on a Patch Tuesday means it could not wait
// Patch Tuesday's own Critical-rated updates follow the 3/14-day rule: they arrive every month and are not an emergency.
public static class UpdateRules
{
    public const string Rule = "updates-due";
    public static readonly TimeSpan Hold = TimeSpan.FromDays(3);
    public static readonly TimeSpan Overdue = TimeSpan.FromDays(14);

    /// <summary>The scan job owns this finding: the health and rack sweeps must leave it alone (they never raise it).</summary>
    public static bool Owns(string ruleKey) => ruleKey == Rule;

    public static IReadOnlyList<Finding> Evaluate(UpdateScan scan, DateTime nowLocal)
    {
        if (scan.Updates.Count == 0) return [];
        var security = scan.Updates.Where(u => u.Security).ToList();
        var oldest = security.Where(u => u.Released is not null).Select(u => u.Released!.Value).DefaultIfEmpty().Min();
        var age = security.Count > 0 && oldest != default ? nowLocal.Date - oldest.Date : TimeSpan.Zero;
        var outOfBand = security.Where(u => u.Severity == "Critical" && u.Released is { } r && !IsPatchTuesday(r)).ToList();

        var severity = security.Count == 0 ? Severity.Info
            : outOfBand.Count > 0 || age >= Overdue ? Severity.Critical
            : age >= Hold ? Severity.Warning
            : Severity.Info;
        var title = security.Count == 0 ? "Updates available"
            : severity == Severity.Info ? "New security updates (held 3 days)"
            : outOfBand.Count > 0 ? "An urgent security update is waiting"
            : "Security updates are due";
        var evidence = (security.Count > 0 ? $"{security.Count} security, {scan.Updates.Count - security.Count} other; oldest security released {oldest:yyyy-MM-dd} ({(int)age.TotalDays} d)"
                                           : $"{scan.Updates.Count} non-security updates")
            + (outOfBand.Count > 0 ? $" | out-of-band Critical: {string.Join(", ", outOfBand.Select(Name))}" : "")
            + (scan.RebootPending ? " | a restart is already pending" : "")
            + " | " + string.Join("; ", scan.Updates.OrderByDescending(u => u.Security).ThenBy(u => u.Released).Take(6).Select(Name))
            + (scan.Updates.Count > 6 ? $"; +{scan.Updates.Count - 6} more" : "");
        return [new Finding(Rule, severity, title, evidence)];
    }

    /// <summary>The second Tuesday of the month: Microsoft's release day.</summary>
    public static DateTime PatchTuesday(int year, int month)
    {
        var first = new DateTime(year, month, 1);
        var firstTuesday = first.AddDays(((int)DayOfWeek.Tuesday - (int)first.DayOfWeek + 7) % 7);
        return firstTuesday.AddDays(7);
    }

    public static bool IsPatchTuesday(DateTime d) => d.Date == PatchTuesday(d.Year, d.Month);

    /// <summary>The morning after Patch Tuesday: the day the "what's new this month" notice goes out.</summary>
    public static bool IsDayAfterPatchTuesday(DateTime local) => local.Date == PatchTuesday(local.Year, local.Month).AddDays(1);

    private static string Name(PendingUpdate u) => u.Kb is { Length: > 0 } kb ? $"KB{kb} {Short(u.Title)}" : Short(u.Title);

    private static string Short(string t) => t.Length <= 70 ? t : t[..69] + "…";
}
