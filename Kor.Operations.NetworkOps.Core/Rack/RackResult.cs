#nullable enable
using System.Globalization;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;

namespace Kor.Operations.NetworkOps.Core.Rack;

/// <summary>The kinds of rack device, as stored in NetworkOps.Devices.Kind and shown on the page.</summary>
public static class RackKinds
{
    public const string Host = "Host";           // ESXi
    public const string Storage = "Storage";     // SAN / NAS
    public const string Ups = "UPS";
    public const string Backup = "Backup";       // Veeam
    public const string Network = "Network";     // UniFi site, core switch
    public const string Internet = "Internet";   // the line out, through the firewall
    public const string Workstation = "Workstation";

    public static bool IsRack(string? kind) => kind is not null && kind != Workstation;
}

/// <summary>
/// One read of one rack device, in the shape the learning layer already stores for PCs: facts (what it IS),
/// metrics (numbers over time), findings (what is wrong), and a one-line summary for the page. A device that
/// could not be read is Reachable = false with the reason, and its other findings are left exactly as they
/// were: not seeing a fault is not the fault being fixed.
/// </summary>
public sealed record RackResult(
    bool Reachable,
    string? Error,
    IReadOnlyDictionary<string, string> Facts,
    IReadOnlyList<MetricPoint> Metrics,
    IReadOnlyList<Finding> Findings,
    string Summary)
{
    public const string UnreachableRule = "rack.unreachable";

    public static RackResult Unreachable(string error) => new(false, error, new Dictionary<string, string>(), [], [], "not answering: " + error);
}

/// <summary>Small helpers every rack rule set uses.</summary>
internal sealed class RackBuilder
{
    public readonly SortedDictionary<string, string> Facts = new(StringComparer.Ordinal);
    public readonly List<MetricPoint> Metrics = [];
    public readonly List<Finding> Findings = [];

    public void Fact(string key, string? value) { if (!string.IsNullOrWhiteSpace(value)) Facts[key] = value.Trim(); }
    public void Metric(string metric, double value, string subject = "") => Metrics.Add(new MetricPoint(metric, subject, value));
    public void Raise(string rule, Severity severity, string title, string evidence) => Findings.Add(new Finding(rule, severity, title, evidence));

    public RackResult Done(string summary) => new(true, null, Facts, Metrics,
        Findings.GroupBy(f => f.RuleKey).Select(g => g.OrderByDescending(f => f.Severity).First()).ToList(), summary);

    public static string N(double v, int decimals = 0) => v.ToString("N" + decimals, CultureInfo.InvariantCulture);
}
