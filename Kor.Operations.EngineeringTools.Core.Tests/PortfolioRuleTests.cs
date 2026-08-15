using System.Globalization;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// The rules, checked against the models KOR engineers have actually built.
///
/// Every threshold in this tool was once set from two buildings, and every one of them passed a
/// green suite while being wrong. Measuring them against the portfolio on 2026-08-14 found the
/// wall ceiling rejecting 4,681 real wall sections, the column cap silently discarding 207 blade
/// columns, and the recorded reason for the slenderness limit false of all but one job.
///
/// That measurement was a one-off run by hand, which means it decays the moment anyone edits a
/// rule. This makes it a check: the values in KorStandards must still admit the overwhelming
/// majority of what engineers draw, or someone has moved a rule away from the evidence.
///
/// Bounded so it can live in a normal test run: a fixed pseudo-random sample rather than all
/// 1,449 files, seeded so it is the same sample every time and a failure can be reproduced.
/// Skipped when the share is unreachable; skipped when the rules database is not configured.
/// </summary>
public class PortfolioRuleTests
{
    private const string ProjectsRoot = @"\\Kor-fs01\Projects\Projects";

    /// <summary>Our own output round-tripped through ETABS is never evidence about what an engineer draws.</summary>
    private static readonly Regex OurOwn = new(@"""K[A-Z]\d+""", RegexOptions.Compiled);

    private readonly ITestOutputHelper _out;
    public PortfolioRuleTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Set KOR_PORTFOLIO_CHECK=1 to run these.
    ///
    /// They walk every directory under the projects share to find the models, which over SMB is
    /// minutes rather than seconds and would triple the length of an ordinary test run. The walk
    /// is local disk on the file server, so this belongs on a schedule there rather than on a
    /// developer's machine — but it lives here, with the rules it checks, so it cannot drift away
    /// from them.
    /// </summary>
    private const string RunEnvironmentVariable = "KOR_PORTFOLIO_CHECK";

    private static bool Enabled =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RunEnvironmentVariable));

    private sealed record Sample(List<double> WallThickness, List<(double Short, double Long)> Columns, int Models);

    // Measured once per test run, not once per test: the walk is the whole cost.
    private static readonly Lazy<Sample?> Corpus = new(() => Walk(120), isThreadSafe: true);

    private static Sample? Measure(int take, ITestOutputHelper log)
    {
        var s = Corpus.Value;
        if (s is not null)
            log.WriteLine($"portfolio sample: {s.Models} engineer models read, " +
                          $"{s.WallThickness.Count} wall sections, {s.Columns.Count} column sections");
        return s;
    }

    private static Sample? Walk(int take)
    {
        if (!Directory.Exists(ProjectsRoot)) return null;

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(ProjectsRoot, "*.e2k", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }

        if (files.Count == 0) return null;

        // Same sample every run: stride the sorted list rather than taking the first N, so it is
        // spread across the whole share instead of whichever folder sorts first.
        int stride = Math.Max(1, files.Count / take);
        var chosen = files.Where((_, i) => i % stride == 0).Take(take).ToList();

        var thickness = new List<double>();
        var columns = new List<(double, double)>();
        int read = 0;

        foreach (string path in chosen)
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch { continue; }
            if (OurOwn.IsMatch(text)) continue;
            read++;

            // Everything is normalised to inches: 190 of the 1,126 models measured are not in
            // inches, and reading them as though they were makes one model in six an outlier.
            double unit = UnitInInches(text);

            foreach (Match m in Regex.Matches(text,
                @"(?m)^\s*SHELLPROP\s+""[^""]+""\s+PROPTYPE\s+""Wall"".*?THICKNESS\s+([\d.]+)"))
                if (TryNum(m.Groups[1].Value, out double t) && t > 0) thickness.Add(t * unit);

            foreach (Match m in Regex.Matches(text,
                @"(?m)^\s*FRAMESECTION\s+""[^""]+"".*?SHAPE\s+""Concrete Rectangular"".*?\sD\s+([\d.]+).*?\sB\s+([\d.]+)"))
                if (TryNum(m.Groups[1].Value, out double d) && TryNum(m.Groups[2].Value, out double b) && d > 0 && b > 0)
                    columns.Add((Math.Min(d, b) * unit, Math.Max(d, b) * unit));
        }

        return new Sample(thickness, columns, read);
    }

    private static bool TryNum(string s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private static double UnitInInches(string text)
    {
        var m = Regex.Match(text, @"(?m)^\s*UNITS\s+""?([A-Za-z]+)""?");
        return (m.Success ? m.Groups[1].Value.ToUpperInvariant() : "IN") switch
        {
            "FT" => 12.0,
            "MM" => 1.0 / 25.4,
            "CM" => 1.0 / 2.54,
            "M" => 1000.0 / 25.4,
            _ => 1.0,
        };
    }

    private static IReadOnlyDictionary<string, RuleSetting>? Rules()
    {
        string? connection = Environment.GetEnvironmentVariable(RuleSettings.ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connection)) return null;
        var settings = RuleSettings.Load(connection);
        return settings.Count == 0 ? null : settings;
    }

    /// <summary>
    /// The wall thickness bounds must still admit what engineers draw. Set at 4"-60" from a
    /// measurement in which 99.2% of 36,761 real wall sections fall inside; a rule moved away from
    /// that shows up here as coverage falling off.
    /// </summary>
    [Fact]
    public void WallThicknessBoundsStillAdmitWhatEngineersDraw()
    {
        if (!Enabled) return;
        var rules = Rules();
        var sample = Measure(120, _out);
        if (rules is null || sample is null || sample.WallThickness.Count < 500) return;

        double lo = rules.ValueOr("dxf.min-wall-thickness", 4);
        double hi = rules.ValueOr("dxf.max-wall-thickness", 60);

        int inside = sample.WallThickness.Count(t => t >= lo && t <= hi);
        double share = 100.0 * inside / sample.WallThickness.Count;

        _out.WriteLine($"wall thickness {lo:0}\"-{hi:0}\": {inside} of {sample.WallThickness.Count} inside ({share:0.0}%)");

        Assert.True(share >= 95.0,
            $"The wall thickness rule admits only {share:0.0}% of {sample.WallThickness.Count} wall sections " +
            $"engineers actually drew ({lo:0}\"-{hi:0}\"). It was set at 99.2%. Either the rule has been moved " +
            "away from the evidence, or what the company draws has changed and the rule should follow it.");
    }

    /// <summary>
    /// The same for column size. Set at 6"-132", admitting 99.2% of 7,538 real column sections;
    /// the old 96" ceiling admitted 97.3% and discarded 207 blade columns without a word.
    /// </summary>
    [Fact]
    public void ColumnSizeBoundsStillAdmitWhatEngineersDraw()
    {
        if (!Enabled) return;
        var rules = Rules();
        var sample = Measure(120, _out);
        if (rules is null || sample is null || sample.Columns.Count < 200) return;

        double lo = rules.ValueOr("dxf.min-column-size", 6);
        double hi = rules.ValueOr("dxf.max-column-size", 132);

        int inside = sample.Columns.Count(c => c.Short >= lo && c.Long <= hi);
        double share = 100.0 * inside / sample.Columns.Count;

        _out.WriteLine($"column size short>={lo:0}\", long<={hi:0}\": {inside} of {sample.Columns.Count} inside ({share:0.0}%)");

        Assert.True(share >= 95.0,
            $"The column size rule admits only {share:0.0}% of {sample.Columns.Count} column sections engineers " +
            $"actually drew (short >= {lo:0}\", long <= {hi:0}\"). It was set at 99.2%.");
    }

    /// <summary>
    /// The slenderness limit is NOT a claim about what a column may be — 17.9% of real column
    /// sections exceed 3:1, and blade columns to 16.5:1 are drawn deliberately. It governs
    /// wall-layer footprints only. This records that, so nobody re-derives the limit from column
    /// sections and moves it: the first reason written for this number did exactly that and was
    /// false of every job but one.
    /// </summary>
    [Fact]
    public void SlendernessLimitIsNotAClaimAboutRealColumns()
    {
        if (!Enabled) return;
        var rules = Rules();
        var sample = Measure(120, _out);
        if (rules is null || sample is null || sample.Columns.Count < 200) return;

        double limit = rules.ValueOr("dxf.max-column-aspect", 3.0);
        int beyond = sample.Columns.Count(c => c.Short > 0 && c.Long / c.Short > limit);

        _out.WriteLine($"columns more slender than {limit:0.0}:1 — {beyond} of {sample.Columns.Count} " +
                       $"({100.0 * beyond / sample.Columns.Count:0.0}%), which is expected and not a fault");

        Assert.True(beyond > 0,
            "No column in the sample exceeds the slenderness limit, which contradicts the portfolio measurement " +
            "that found 17.9% beyond it. Either the sample is unrepresentative or the limit has been raised to " +
            "cover blade columns — which would convert wall-layer piers into frame elements and lose their shear.");
    }
}
