#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kor.Opportunities.Data.MajorProjects;

namespace Kor.Operations.App.Opportunities;

/// <summary>
/// The pipeline's headline numbers and bar charts, computed from the PrimePipeline
/// rows. Moved here from the BD dashboard on 2026-10-13 ("clarity is king"): the
/// dashboard had the same pipeline sliced four ways plus five tiles on one screen.
/// They now live on the Pipeline window, behind one bar template, and this builder
/// is pure so the money rule (never sum a non-CAD value as CAD) is testable here.
/// </summary>
public static class PipelineCharts
{
    private static readonly CultureInfo Canadian = CultureInfo.GetCultureInfo("en-CA");

    /// <summary><paramref name="Display"/> is the tooltip text, pre-formatted, so one
    /// XAML template serves counts and dollar charts alike.</summary>
    public sealed record ChartBar(string Label, double Value, double Max, string Display);

    public sealed record Overview(
        string TotalPipelineDisplay,
        string OpenRfpsDisplay,
        string UpcomingProjectsDisplay,
        string PipelineValueDisplay,
        string PipelineValueNote,
        bool HasUnconvertedValue,
        IReadOnlyList<ChartBar> SectorBars,
        IReadOnlyList<ChartBar> MarketBars,
        IReadOnlyList<ChartBar> StageBars,
        IReadOnlyList<ChartBar> MarketValueBars)
    {
        public static readonly Overview Empty = Build(Array.Empty<PrimePipelineRow>());
    }

    public static Overview Build(IReadOnlyList<PrimePipelineRow> pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        // Brief 7 / BD-A08: a USD or unknown-currency value is never summed as CAD.
        var hasUnconvertedValue = pipeline.Any(r => r.HasUnconvertedValue);

        var sectorBars = BuildBars(pipeline
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Sector)
                ? "Other"
                : Canadian.TextInfo.ToTitleCase(r.Sector!.Trim().ToLowerInvariant()))
            .Select(g => (Label: g.Key, Count: g.Count())));

        var marketBars = BuildBars(pipeline
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Province) ? "" : r.Province!)
            .Select(g => (Label: g.Key, Count: g.Count())));

        var stageBars = BuildBars(pipeline
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Stage) ? "" : r.Stage!)
            .Select(g => (Label: g.Key, Count: g.Count())));

        var marketValueBars = hasUnconvertedValue
            ? Array.Empty<ChartBar>()
            : BuildValueBars(pipeline
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Province) ? "" : r.Province!)
                .Select(g => (Label: g.Key, Value: (double)(g.Sum(r => r.EstimatedValueCad ?? 0m) / 1_000_000m))));

        return new Overview(
            pipeline.Count.ToString("N0", Canadian),
            pipeline.Count(r => string.Equals(r.PipelineType, "Open RFP", StringComparison.OrdinalIgnoreCase)).ToString("N0", Canadian),
            pipeline.Count(r => string.Equals(r.PipelineType, "Pipeline Project", StringComparison.OrdinalIgnoreCase)).ToString("N0", Canadian),
            hasUnconvertedValue ? "Unavailable" : FormatCompactCad(pipeline.Sum(r => r.EstimatedValueCad ?? 0m)),
            hasUnconvertedValue
                ? "CAD totals unavailable: a valued row has non-CAD or unknown currency. No conversion applied."
                : "CAD known-value subtotal; missing amounts excluded. No currency conversion.",
            hasUnconvertedValue,
            sectorBars, marketBars, stageBars, marketValueBars);
    }

    // Large sums ($59B) overflowed a fixed-width tile and clipped to "$59,014,997,".
    public static string FormatCompactCad(decimal v)
    {
        if (v >= 1_000_000_000m) return string.Format(Canadian, "${0:0.0}B", v / 1_000_000_000m);
        if (v >= 1_000_000m) return string.Format(Canadian, "${0:0.0}M", v / 1_000_000m);
        if (v >= 1_000m) return string.Format(Canadian, "${0:0}K", v / 1_000m);
        return string.Format(Canadian, "{0:C0}", v);
    }

    private static ChartBar[] BuildBars(IEnumerable<(string Label, int Count)> groups)
    {
        var list = groups
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Label, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        var max = list.Length == 0 ? 0 : list.Max(g => g.Count);
        return list.Select(g => new ChartBar(g.Label, g.Count, max, g.Count.ToString("N0", Canadian))).ToArray();
    }

    private static ChartBar[] BuildValueBars(IEnumerable<(string Label, double Value)> groups)
    {
        var list = groups
            .OrderByDescending(g => g.Value)
            .ThenBy(g => g.Label, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        var max = list.Length == 0 ? 0 : list.Max(g => g.Value);
        return list.Select(g => new ChartBar(g.Label, g.Value, max, string.Format(Canadian, "${0:0.0}M", g.Value))).ToArray();
    }
}
