#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kor.Opportunities.Data.BdReports.Generators;

/// <summary>
/// THE sector report generator (BD-UI-Plan: one parameterized generator
/// driven by SectorReportDefinition config rows — new sectors are new config
/// rows, not new code). Section structure and truncation caps mirror the
/// shipped PowerShell builders in tools/BdReportBuilders. Pure function:
/// (definition, prose, rows, generatedAt) -> BdReportDocument; render with
/// DocxBuilder or HtmlPreviewBuilder.
/// </summary>
public static class SectorReportGenerator
{
    // Truncation caps from the PS builders' Safe() calls.
    private const int UrgentAngleCap = 600;
    // PURSUE is the act-on bucket; its honing rationale is the most decision-
    // relevant of the table buckets and the kor://mpi drill-down can't recover
    // truncated text (PursuitBrief carries no honing angle), so this cap is set
    // well above the lower-stakes MONITOR/DISCOVER caps to keep the "why" intact.
    private const int PursueWhyCap = 400;
    private const int MonitorWhyCap = 110;
    private const int DeadWhyCap = 140;
    private const int TableNameCap = 60;
    private const int TableProponentCap = 30;

    public static BdReportDocument Build(
        SectorReportDefinition definition,
        SectorReportProse prose,
        IReadOnlyList<PursuitBriefRow> rows,
        DateTimeOffset generatedAtUtc,
        IReadOnlyList<SectorIntelSignalRow>? marketSignals = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(prose);
        ArgumentNullException.ThrowIfNull(rows);
        marketSignals ??= Array.Empty<SectorIntelSignalRow>();

        // The PS builders report only honed briefs; never-honed MPIs surface
        // on the dashboard as NotHoned, not in the report body.
        // A row rated as a duplicate of another is not shown to the reader, so it is not counted either: "Projects
        // reviewed" must match the rows the report lists (2026-10-22).
        var honed = rows.Where(x => x.Verdict is not null && x.Verdict != BdVerdicts.Duplicate).ToList();
        var urgent = honed.Where(x => x.IsUrgent).ToList();
        var pursue = honed.Where(x => x.Verdict == BdVerdicts.Pursue && !x.IsUrgent).ToList();
        var monitor = honed.Where(x => x.Verdict == BdVerdicts.Monitor).ToList();
        var discover = honed.Where(x => x.Verdict == BdVerdicts.Discover).ToList();
        var dead = honed.Where(x => x.Verdict == BdVerdicts.Dead).ToList();

        var b = new BdReportDocumentBuilder(definition.ReportTitle);

        b.Italic($"{prose.IntroNote} As of {BdReportFormatting.ReaderDate(generatedAtUtc)}.");

        b.P(BdReportFormatting.EvidenceNote(rows));
        b.H2("Executive Summary");
        b.Kpis(
            new KpiItem(honed.Count.ToString(CultureInfo.InvariantCulture), "Projects reviewed"),
            new KpiItem(urgent.Count.ToString(CultureInfo.InvariantCulture), "Urgent", urgent.Count > 0 ? ChipTone.Urgent : ChipTone.Neutral),
            new KpiItem(pursue.Count.ToString(CultureInfo.InvariantCulture), "Pursue", ChipTone.Positive),
            new KpiItem(monitor.Count.ToString(CultureInfo.InvariantCulture), "Monitor", ChipTone.Caution),
            new KpiItem(discover.Count.ToString(CultureInfo.InvariantCulture), "Discover", ChipTone.Info));
        b.B("DEAD — locked or delivered: ", dead.Count.ToString(CultureInfo.InvariantCulture));

        foreach (var paragraph in prose.SummaryNarrative)
        {
            b.P(paragraph);
        }

        b.H2("URGENT — IMMEDIATE action required");
        if (urgent.Count == 0)
        {
            b.P("None right now.");
        }

        var rank = 1;
        foreach (var p in urgent)
        {
            b.H3($"{rank}. {p.ProjectName}", KorReportLinks.Mpi(p.MpiId));
            var facts = new List<string> { $"Id {p.MpiId}" };
            if (!string.IsNullOrWhiteSpace(p.Province))
            {
                facts.Add(p.Province);
            }

            if (!string.IsNullOrWhiteSpace(p.ProponentName))
            {
                facts.Add($"Proponent: {p.ProponentName}");
            }

            var cost = CostOf(p);
            if (!string.IsNullOrWhiteSpace(cost))
            {
                facts.Add($"Cost: {cost}");
            }

            if (!string.IsNullOrWhiteSpace(p.HoningStatus))
            {
                facts.Add($"Status: {p.HoningStatus}");
            }

            b.Italic(string.Join("  ·  ", facts));
            b.P(Safe(p.KorAngle, UrgentAngleCap));
            rank++;
        }

        b.H2("PURSUE — Open opportunities (not yet urgent)");
        AppendBucketTable(b, pursue,
            new[] { "Id", "Project", "Proponent", "Province", "Cost", "Why PURSUE" },
            p => new[]
            {
                p.MpiId.ToString(CultureInfo.InvariantCulture),
                Safe(p.ProjectName, 55),
                Safe(p.ProponentName, TableProponentCap),
                p.Province,
                CostOf(p),
                Safe(p.KorAngle, PursueWhyCap),
            },
            new[] { ColumnAlignment.Right, ColumnAlignment.Left, ColumnAlignment.Left, ColumnAlignment.Left, ColumnAlignment.Right, ColumnAlignment.Left });

        b.H2("MONITOR — Current phase locked, future phases open");
        AppendBucketTable(b, monitor,
            new[] { "Id", "Project", "Proponent", "Province", "Cost", "Why MONITOR" },
            p => new[]
            {
                p.MpiId.ToString(CultureInfo.InvariantCulture),
                Safe(p.ProjectName, 55),
                Safe(p.ProponentName, TableProponentCap),
                p.Province,
                CostOf(p),
                Safe(p.KorAngle, MonitorWhyCap),
            },
            new[] { ColumnAlignment.Right, ColumnAlignment.Left, ColumnAlignment.Left, ColumnAlignment.Left, ColumnAlignment.Right, ColumnAlignment.Left });

        if (marketSignals.Count > 0)
        {
            b.H2("MARKET SIGNALS");
            foreach (var signal in marketSignals)
            {
                b.B($"• {signal.SignalType}: ", Safe(MarketSignalBody(signal), 500));
            }
        }

        b.H2("DISCOVER — Pre-procurement relationship-build");
        AppendBucketTable(b, discover,
            new[] { "Id", "Project", "Proponent", "Province", "Cost" },
            p => new[]
            {
                p.MpiId.ToString(CultureInfo.InvariantCulture),
                Safe(p.ProjectName, TableNameCap),
                Safe(p.ProponentName, TableProponentCap),
                p.Province,
                CostOf(p),
            },
            new[] { ColumnAlignment.Right, ColumnAlignment.Left, ColumnAlignment.Left, ColumnAlignment.Left, ColumnAlignment.Right });

        b.H2("DEAD — Locked (Alliance / P3 / captive / Delivered)");
        AppendBucketTable(b, dead,
            new[] { "Id", "Project", "Province", "Why DEAD" },
            p => new[]
            {
                p.MpiId.ToString(CultureInfo.InvariantCulture),
                Safe(p.ProjectName, TableNameCap),
                p.Province,
                Safe(p.KorAngle, DeadWhyCap),
            },
            new[] { ColumnAlignment.Right, ColumnAlignment.Left, ColumnAlignment.Left, ColumnAlignment.Left });

        if (prose.Synthesis.Count > 0)
        {
            b.H2("Strategic Synthesis");
            foreach (var section in prose.Synthesis)
            {
                b.H3(section.Title);
                foreach (var paragraph in section.Paragraphs)
                {
                    b.P(paragraph);
                }
            }
        }

        if (prose.RecommendedActions.Count > 0)
        {
            b.H2("Recommended next actions");
            var n = 1;
            foreach (var action in prose.RecommendedActions)
            {
                b.P($"{n}. {action}");
                n++;
            }
        }

        return b.Build();
    }

    private static void AppendBucketTable(
        BdReportDocumentBuilder b,
        IReadOnlyList<PursuitBriefRow> bucket,
        string[] headers,
        Func<PursuitBriefRow, string[]> rowOf,
        IReadOnlyList<ColumnAlignment>? alignments = null)
    {
        if (bucket.Count == 0)
        {
            b.P("None right now.");
            return;
        }

        // Every bucket table has "Project" as its second column — drill-down
        // links anchor there.
        b.Table(
            headers,
            bucket.Select(rowOf).ToList(),
            alignments,
            bucket.Select(p => (IReadOnlyList<string?>)headers
                .Select((_, i) => i == 1 ? KorReportLinks.Mpi(p.MpiId) : null)
                .ToArray()).ToList());
    }

    private static string CostOf(PursuitBriefRow p) => BdReportFormatting.Cost(p.EstimatedCostText, p.EstimatedCostCad, string.Empty);

    private static string MarketSignalBody(SectorIntelSignalRow signal)
    {
        var parts = new List<string> { signal.DisplayName, signal.Subject };
        if (!string.IsNullOrWhiteSpace(signal.Detail))
        {
            parts.Add(signal.Detail);
        }

        return string.Join(" — ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static string Safe(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return BdReportFormatting.Abridge(value, max);
    }
}
