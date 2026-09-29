#nullable enable
using Kor.Operations.NetworkOps.Core.Health;

namespace Kor.Operations.NetworkOps.Core.Learning;

public sealed record SeriesPoint(DateTime AtUtc, double Value);

/// <summary>A PC's metric history, keyed by (metric, subject). The current sweep's points are included.</summary>
public sealed class MetricHistory
{
    private readonly Dictionary<(string, string), List<SeriesPoint>> _series = new();

    public void Add(string metric, string subject, DateTime atUtc, double value)
    {
        if (!_series.TryGetValue((metric, subject), out var list)) _series[(metric, subject)] = list = new();
        list.Add(new(atUtc, value));
    }

    public IReadOnlyList<SeriesPoint> Get(string metric, string subject)
        => _series.TryGetValue((metric, subject), out var l) ? l.OrderBy(p => p.AtUtc).ToList() : [];

    public IEnumerable<string> Subjects(string metric) => _series.Keys.Where(k => k.Item1 == metric).Select(k => k.Item2);
}

// Findings raised BEFORE the fault: a drive that will fill, a mailbox file heading for Outlook's
// 50 GB ceiling, an SSD wearing out, a disk that has started throwing errors, a count that is
// climbing, a battery that is going. Trends use the Theil-Sen estimator (median of pairwise
// slopes) so one clean-up or one bad day cannot swing a projection.
public static class Predictions
{
    public const int OstLimitGb = 50;          // Outlook's default ceiling for an OST
    public const double HoursPerYear = 8766;

    public static IReadOnlyList<Finding> Evaluate(HealthSnapshot s, MetricHistory h)
    {
        var f = new List<Finding>();
        var now = s.CollectedAt;

        // ---- a drive filling up: projected days until 0 GB free
        foreach (var letter in h.Subjects(Metrics.DiskFreeGb))
        {
            // A clean-up is not an outlier, it is a new starting point: project from the last jump in
            // free space, not across it (otherwise freeing 30 GB reads as "growing" for weeks).
            var pts = SinceLastJump(Window(h.Get(Metrics.DiskFreeGb, letter), now, days: 21), minJump: 2.0, minJumpFraction: 0.05);
            if (!Trendable(pts, minPoints: 3, minDays: 3)) continue;
            var perDay = TheilSenPerDay(pts);
            var free = pts[^1].Value;
            if (perDay >= -0.05 || free <= 0) continue;                // not shrinking by any meaningful amount
            var days = free / -perDay;
            if (days > 60) continue;
            f.Add(new($"disk-filling:{letter.ToLowerInvariant()}", days <= 14 ? Severity.Critical : Severity.Warning,
                $"Drive {letter}: will be full in about {Math.Max(1, Math.Round(days))} days",
                $"{free:0.#} GB free, losing {-perDay:0.##} GB a day over the last {Span(pts):0} days"));
        }

        // ---- mailbox files near Outlook's 50 GB ceiling -- by size now, or by where the growth is heading
        foreach (var ms in s.MailStores.Where(m => m.Name.EndsWith(".ost", StringComparison.OrdinalIgnoreCase)))
        {
            var subject = $@"{ms.Profile}\{ms.Name}";
            var pts = Window(h.Get(Metrics.MailStoreGb, subject), now, days: 60);
            var perDay = Trendable(pts, 3, 7) ? TheilSenPerDay(pts) : 0;
            var daysToLimit = perDay > 0.001 ? (OstLimitGb - ms.GB) / perDay : double.PositiveInfinity;
            if (ms.GB >= 40 || daysToLimit <= 90)
            {
                var sev = ms.GB >= 47 || daysToLimit <= 14 ? Severity.Critical : Severity.Warning;
                var growth = perDay > 0.001 ? $", growing {perDay * 30:0.#} GB a month" + (double.IsFinite(daysToLimit) ? $" -- limit in about {Math.Max(1, Math.Round(daysToLimit))} days" : "") : "";
                f.Add(new($"mailbox-near-limit:{subject.ToLowerInvariant()}", sev, "Outlook mailbox file is nearing its 50 GB limit",
                    $"{subject}: {ms.GB:0.#} GB of {OstLimitGb} GB{growth}"));
            }
        }

        // ---- drives: wear, new errors, age
        var media = s.PhysicalDisks.Where(d => d.Name is not null).GroupBy(d => d.Name!).ToDictionary(g => g.Key, g => g.First().Media);
        foreach (var d in s.DiskReliability.Where(d => d.Name is not null))
        {
            var name = d.Name!;
            if (d.WearPct is >= 80)
                f.Add(new($"disk-wearing:{name.ToLowerInvariant()}", d.WearPct >= 90 ? Severity.Critical : Severity.Warning,
                    "An SSD is wearing out", $"{name}: {d.WearPct}% of its rated write endurance used"));
            else if (d.WearPct is > 0)
            {
                var pts = Window(h.Get(Metrics.SsdWearPct, name), now, days: 90);
                if (Trendable(pts, 3, 14))
                {
                    var perDay = TheilSenPerDay(pts);
                    if (perDay > 0 && (100 - d.WearPct.Value) / perDay <= 365)
                        f.Add(new($"disk-wearing:{name.ToLowerInvariant()}", Severity.Warning, "An SSD will wear out within a year",
                            $"{name}: {d.WearPct}% used, rising {perDay * 30:0.#}% a month"));
                }
            }

            if (d.ReadErrorsUncorrected is > 0)
                f.Add(new($"disk-errors:{name.ToLowerInvariant()}", Severity.Critical, "A drive has unrecoverable read errors",
                    $"{name}: {d.ReadErrorsUncorrected} uncorrected read errors -- copy its data off now"));
            else
            {
                var rise = Rise(h.Get(Metrics.DiskReadErrors, name), now) + Rise(h.Get(Metrics.DiskWriteErrors, name), now);
                if (rise > 0)
                    f.Add(new($"disk-errors:{name.ToLowerInvariant()}", Severity.Warning, "A drive has started logging errors",
                        $"{name}: {rise} new read/write errors in the last week"));
            }

            if (d.PowerOnHours is >= 43_800 && media.TryGetValue(name, out var mt) && string.Equals(mt, "HDD", StringComparison.OrdinalIgnoreCase))
                f.Add(new($"disk-aging:{name.ToLowerInvariant()}", Severity.Info, "An old spinning disk is still in service",
                    $"{name}: {d.PowerOnHours / HoursPerYear:0.#} years powered on -- past the usual life of a hard drive"));
        }

        // ---- counts that are climbing: the same fault getting worse week on week
        // Distinct resets (probe v3). The v2 raw-entry series (gpu.hangs.14d) is no longer fed and is not
        // judged: its stale tail must not raise a second "getting worse" beside the real one.
        Worsening(f, h, now, Metrics.GpuResets14d, "", 3, "gpu-hangs-rising", "Graphics hangs are increasing");
        Worsening(f, h, now, Metrics.AppHangs14d, "", 5, "app-hangs-rising", "Programs are freezing more often");
        Worsening(f, h, now, Metrics.ResourceExhaustion14d, "", 1, "memory-pressure-rising", "The PC is running out of memory more often");
        Worsening(f, h, now, Metrics.DiskResets14d, "", 3, "disk-resets-rising", "Drive resets are increasing");
        Worsening(f, h, now, Metrics.Whea14d, "", 3, "hardware-errors-rising", "Hardware errors are increasing");
        foreach (var proc in h.Subjects(Metrics.AppCrashes14d))
            Worsening(f, h, now, Metrics.AppCrashes14d, proc, 5, $"crashes-rising:{proc}", $"{proc} is crashing more often");

        // ---- slower to start than it used to be
        var boots = h.Get(Metrics.BootMedianMs, "");
        if (boots.Count > 0 && Before(boots, now, 14) is { } then && boots[^1].Value >= 60_000 && boots[^1].Value >= then.Value * 1.5)
            f.Add(new("boot-slowing", Severity.Info, "The PC is taking longer to start",
                $"median boot {boots[^1].Value / 1000:0} s, up from {then.Value / 1000:0} s two weeks ago"));

        if (s.Battery is { HealthPct: < 70 } b)
            f.Add(new("battery-worn", b.HealthPct < 50 ? Severity.Critical : Severity.Warning, "The laptop battery is worn",
                $"holds {b.HealthPct}% of its original charge ({b.FullChargeMWh:n0} of {b.DesignMWh:n0} mWh)"));

        // Windows 10 left support on 14 Oct 2025: no more security updates.
        if (s.Os is { Build: > 0 and < 22000 } os)
            f.Add(new("os-unsupported", Severity.Warning, "Windows 10 no longer gets security updates",
                $"build {os.Build}.{os.Ubr} ({os.Product}) -- support ended 14 Oct 2025"));

        if (s.Os is { UptimeHours: > 30 * 24 } up)
            f.Add(new("not-restarted", Severity.Info, "Hasn't been restarted in over a month", $"up {up.UptimeHours / 24} days"));

        return f;
    }

    private static void Worsening(List<Finding> f, MetricHistory h, DateTime now, string metric, string subject, double floor, string key, string title)
    {
        var pts = h.Get(metric, subject);
        if (pts.Count == 0 || Before(pts, now, 7) is not { } weekAgo) return;
        var cur = pts[^1].Value;
        if (cur >= floor && cur - weekAgo.Value >= Math.Max(3, floor / 2) && cur >= weekAgo.Value * 2)
            f.Add(new(key, Severity.Warning, title, $"{weekAgo.Value:0} → {cur:0} in the rolling 14-day count over the last week"));
    }

    /// <summary>The latest point at least <paramref name="daysAgo"/> days old (within a 3-day tolerance), or null.</summary>
    internal static SeriesPoint? Before(IReadOnlyList<SeriesPoint> pts, DateTime now, int daysAgo)
        => pts.Where(p => (now - p.AtUtc).TotalDays >= daysAgo && (now - p.AtUtc).TotalDays <= daysAgo + 3).OrderByDescending(p => p.AtUtc).FirstOrDefault();

    /// <summary>How much a cumulative counter rose over the last 7 days (0 if it did not, or was reset).</summary>
    internal static double Rise(IReadOnlyList<SeriesPoint> pts, DateTime now)
    {
        var recent = pts.Where(p => (now - p.AtUtc).TotalDays <= 7).OrderBy(p => p.AtUtc).ToList();
        if (recent.Count < 2) return 0;
        var r = recent[^1].Value - recent[0].Value;
        return r > 0 ? r : 0;   // a drop means the counter reset (disk replaced), not negative errors
    }

    /// <summary>The points after the last upward jump larger than both thresholds (a clean-up, a bigger drive).</summary>
    internal static List<SeriesPoint> SinceLastJump(List<SeriesPoint> pts, double minJump, double minJumpFraction)
    {
        for (var i = pts.Count - 1; i > 0; i--)
        {
            var rise = pts[i].Value - pts[i - 1].Value;
            if (rise > minJump && rise > Math.Abs(pts[i - 1].Value) * minJumpFraction) return pts.Skip(i).ToList();
        }
        return pts;
    }

    internal static List<SeriesPoint> Window(IReadOnlyList<SeriesPoint> pts, DateTime now, int days)
        => pts.Where(p => (now - p.AtUtc).TotalDays <= days && p.AtUtc <= now.AddMinutes(1)).OrderBy(p => p.AtUtc).ToList();

    internal static bool Trendable(IReadOnlyList<SeriesPoint> pts, int minPoints, double minDays)
        => pts.Count >= minPoints && Span(pts) >= minDays;

    private static double Span(IReadOnlyList<SeriesPoint> pts) => (pts[^1].AtUtc - pts[0].AtUtc).TotalDays;

    /// <summary>Theil-Sen slope, per day: the median of the slopes between every pair of points.</summary>
    internal static double TheilSenPerDay(IReadOnlyList<SeriesPoint> pts)
    {
        var slopes = new List<double>();
        for (var i = 0; i < pts.Count; i++)
            for (var j = i + 1; j < pts.Count; j++)
            {
                var dt = (pts[j].AtUtc - pts[i].AtUtc).TotalDays;
                if (dt > 0.01) slopes.Add((pts[j].Value - pts[i].Value) / dt);
            }
        if (slopes.Count == 0) return 0;
        slopes.Sort();
        var mid = slopes.Count / 2;
        return slopes.Count % 2 == 1 ? slopes[mid] : (slopes[mid - 1] + slopes[mid]) / 2;
    }
}
