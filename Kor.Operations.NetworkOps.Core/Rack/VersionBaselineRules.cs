#nullable enable
using System.Globalization;
using System.Text.RegularExpressions;
using Kor.Operations.NetworkOps.Core.Health;

namespace Kor.Operations.NetworkOps.Core.Rack;

/// <summary>One product's security/support baseline (db/KorNetworkOps NetworkOps.VersionBaseline, migration 013): the
/// minimum build that is safe to run, when the vendor ends support, and when a human last reviewed the row. The Veeam
/// row is auto-refreshed from KB2680; the rest are seeded and reviewed by hand (baseline.stale nags when a row goes cold).</summary>
public sealed record VersionBaselineRow(string Product, string? MinSecureBuild, DateTime? EndOfSupportUtc, DateTime LastReviewedUtc, string? Reason = null, string? Source = null);

/// <summary>
/// CLASS 1. Every collector records a version as a fact; this is the thing that COMPARES it to a secure baseline and an
/// end-of-support date. Pure over the version facts the sweeps already store (esxi.version, veeam.version, os.build +
/// os.caption, fw.model) plus the baseline rows from the store. Run once per device in the rack sweep (Evaluate), with the
/// table's own freshness checked once per sweep (BaselineHealth).
///
/// COVERS: VMware ESXi, Veeam B&amp;R, Windows Server and pfSense Plus -- each compared by build number (ESXi, Windows) or
/// dotted version (Veeam, pfSense). Below the min secure build => version.insecure (Critical, carries the CVE); past the
/// end-of-support date => version.end-of-support (Warning); one of those four products with NO baseline row =>
/// version.no-baseline (Info, so a new product is reviewed not skipped); any row not reviewed in StaleDays, or an empty
/// table => baseline.stale (so the list cannot rot silently -- the explicit requirement).
/// DOES NOT COVER: Synology DSM, the EdgeSwitch and the printers -- their versions are recorded but NOT yet baseline-checked
/// (their build formats need their own extractor; a fast follow adds them). An unpublished 0-day (no row yet), a product
/// whose version we never record, or a baseline value that is itself wrong.
/// A SAME-CLASS FAULT IT WOULD MISS: a DSM/switch/printer running a vulnerable firmware -- there is no probe for it yet, so
/// nothing (not even baseline.stale) fires; and a Veeam CVE whose fixed build KB2680 has not published when the sweep runs,
/// which surfaces only as baseline.stale "go review", not as a clean pass.
/// </summary>
public static class VersionBaselineRules
{
    /// <summary>A baseline row older than this (by LastReviewed) is called stale, so a human re-checks the secure build.</summary>
    public const int StaleDays = 45;

    /// <summary>Per-device version findings: compare each recognised version fact to its baseline row.</summary>
    public static IReadOnlyList<Finding> Evaluate(IReadOnlyDictionary<string, string> facts, IReadOnlyList<VersionBaselineRow> baseline, DateTime nowUtc)
    {
        var byProduct = new Dictionary<string, VersionBaselineRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in baseline) byProduct[r.Product] = r;   // last row per product wins (the auto-refresh updates in place)
        var findings = new List<Finding>();
        foreach (var probe in Probes)
        {
            if (probe(facts) is not { } hit) continue;
            var (product, shown, cmp) = hit;
            if (!byProduct.TryGetValue(product, out var row))
            {
                findings.Add(new Finding("version.no-baseline", Severity.Info, $"No version baseline for {product}",
                    $"NetworkOps records {shown} but has no secure-build / end-of-support row to check it against -- add one to NetworkOps.VersionBaseline"));
                continue;
            }
            if (row.EndOfSupportUtc is { } eos && eos < nowUtc)
                findings.Add(new Finding("version.end-of-support", Severity.Warning, $"{product} is past end of support",
                    $"{shown}; support ended {eos:yyyy-MM-dd} -- no more security fixes from the vendor{(row.Reason is { } r1 ? " -- " + r1 : "")}"));
            if (row.MinSecureBuild is { } min && IsBelow(cmp, min))
                findings.Add(new Finding("version.insecure", Severity.Critical, $"{product} is below its minimum secure build",
                    $"{shown}; the secure build is {min}{(row.Reason is { } r2 ? " -- " + r2 : "")}"));
        }
        return findings;
    }

    /// <summary>The baseline table's OWN freshness, once per sweep (attach to one always-present device). Empty table, or a
    /// row not reviewed in StaleDays, means the secure-build list may be out of date and nothing is really being checked.</summary>
    public static Finding? BaselineHealth(IReadOnlyList<VersionBaselineRow> baseline, DateTime nowUtc)
    {
        if (baseline.Count == 0)
            return new Finding("baseline.stale", Severity.Warning, "No version baseline is loaded",
                "run db/KorNetworkOps/013_VersionBaseline.sql -- until then no product is checked against a secure build");
        var stale = baseline.Where(r => (nowUtc - r.LastReviewedUtc).TotalDays > StaleDays).Select(r => r.Product).ToList();
        return stale.Count == 0 ? null
            : new Finding("baseline.stale", Severity.Info, $"{stale.Count} version-baseline row(s) not reviewed in {StaleDays} days",
                "the secure-build list may be out of date -- re-check: " + string.Join(", ", stale));
    }

    private static bool IsBelow(string have, string min)
    {
        if (long.TryParse(have, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)
            && long.TryParse(min, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m)) return h < m;   // bare build numbers (ESXi)
        if (Version.TryParse(have, out var hv) && Version.TryParse(min, out var mv)) return hv < mv;               // dotted (Veeam / Windows / pfSense)
        return false;   // cannot compare -> do NOT claim insecure (a false positive is worse than the gap, which baseline.stale surfaces)
    }

    private static readonly Regex Esxi = new(@"(\d+\.\d+)(?:\.\d+)?\s+build-(\d+)", RegexOptions.Compiled);
    private static readonly Regex Dotted = new(@"\d+(?:\.\d+)+", RegexOptions.Compiled);
    private static readonly Regex WinServer = new(@"Windows Server \d{4}", RegexOptions.Compiled);
    private static readonly Regex PfSense = new(@"pfSense.*?(\d+\.\d+(?:\.\d+)?)-RELEASE", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Each version-bearing fact, turned into (product key that matches a baseline row, text to show, the comparable build/version).
    private static readonly Func<IReadOnlyDictionary<string, string>, (string Product, string Shown, string Cmp)?>[] Probes =
    [
        f => Get(f, "esxi.version") is { } v && Esxi.Match(v) is { Success: true } m
            ? ($"VMware ESXi {m.Groups[1].Value}", v, m.Groups[2].Value) : null,
        f => Get(f, "veeam.version") is { } v && Dotted.Match(v) is { Success: true } m
            ? ("Veeam Backup & Replication", "Veeam " + v, m.Value) : null,
        f => Get(f, "os.build") is { } b && Get(f, "os.caption") is { } cap && WinServer.Match(cap) is { Success: true } m
            ? (m.Value, $"{cap} (build {b})", b) : null,
        f => Get(f, "fw.model") is { } fm && PfSense.Match(fm) is { Success: true } m
            ? ("pfSense Plus", fm, m.Groups[1].Value) : null,
    ];

    private static string? Get(IReadOnlyDictionary<string, string> f, string key) => f.TryGetValue(key, out var v) ? v : null;
}
