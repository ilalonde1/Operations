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
    private const int PursueAngleCap = 450;
    private const int MonitorWhyCap = 110;
    private const int DeadWhyCap = 140;
    private const int DuplicateWhyCap = 120;
    private const int TableNameCap = 60;
    private const int TableProponentCap = 30;

    public static BdReportDocument Build(
        SectorReportDefinition definition,
        SectorReportProse prose,
        IReadOnlyList<PursuitBriefRow> rows,
        DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(prose);
        ArgumentNullException.ThrowIfNull(rows);

        // The PS builders report only honed briefs; never-honed MPIs surface
        // on the dashboard as NotHoned, not in the report body.
        var honed = rows.Where(x => x.Verdict is not null).ToList();
        var urgent = honed.Where(x => x.IsUrgent).ToList();
        var pursue = honed.Where(x => x.Verdict == BdVerdicts.Pursue && !x.IsUrgent).ToList();
        var monitor = honed.Where(x => x.Verdict == BdVerdicts.Monitor).ToList();
        var discover = honed.Where(x => x.Verdict == BdVerdicts.Discover).ToList();
        var dead = honed.Where(x => x.Verdict == BdVerdicts.Dead).ToList();
        var duplicate = honed.Where(x => x.Verdict == BdVerdicts.Duplicate).ToList();

        var b = new BdReportDocumentBuilder(definition.ReportTitle);

        b.Italic($"{prose.IntroNote} Generated {generatedAtUtc:yyyy-MM-dd HH:mm} UTC from live KorOpportunitiesDb honing verdicts.");

        b.H2("Executive Summary");
        b.B($"{definition.Title} projects honed: ", honed.Count.ToString(CultureInfo.InvariantCulture));
        b.B("PURSUE_URGENT — IMMEDIATE action: ", urgent.Count.ToString(CultureInfo.InvariantCulture));
        b.B("PURSUE — open opportunities: ", pursue.Count.ToString(CultureInfo.InvariantCulture));
        b.B("MONITOR — locked but future phases open: ", monitor.Count.ToString(CultureInfo.InvariantCulture));
        b.B("DISCOVER — pre-procurement relationship-build: ", discover.Count.ToString(CultureInfo.InvariantCulture));
        b.B("DEAD — locked or delivered: ", dead.Count.ToString(CultureInfo.InvariantCulture));
        b.B("DUPLICATE — flagged for MPI consolidation: ", duplicate.Count.ToString(CultureInfo.InvariantCulture));

        foreach (var paragraph in prose.SummaryNarrative)
        {
            b.P(paragraph);
        }

        b.H2("URGENT — IMMEDIATE action required");
        if (urgent.Count == 0)
        {
            b.P("None in this honing pass.");
        }

        var rank = 1;
        foreach (var p in urgent)
        {
            b.H3($"{rank}. {p.ProjectName}");
            b.B("Id: ", p.MpiId.ToString(CultureInfo.InvariantCulture));
            b.B("Province: ", p.Province);
            b.B("Proponent: ", p.ProponentName ?? string.Empty);
            b.B("Cost: ", CostOf(p));
            b.B("Status: ", p.HoningStatus ?? string.Empty);
            b.P(Safe(p.KorAngle, UrgentAngleCap));
            b.P(string.Empty);
            rank++;
        }

        b.H2("PURSUE — Open opportunities (not yet urgent)");
        if (pursue.Count == 0)
        {
            b.P("None in this honing pass.");
        }

        foreach (var p in pursue)
        {
            b.B($"Id {p.MpiId}: ", $"{p.ProjectName} ({p.Province})");
            b.P($"Proponent: {p.ProponentName} | Cost: {CostOf(p)}");
            b.P(Safe(p.KorAngle, PursueAngleCap));
            b.Italic("Status: " + (p.HoningStatus ?? string.Empty));
            b.P(string.Empty);
        }

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
            });

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
            });

        b.H2("DEAD — Locked (Alliance / P3 / captive / Delivered)");
        AppendBucketTable(b, dead,
            new[] { "Id", "Project", "Province", "Why DEAD" },
            p => new[]
            {
                p.MpiId.ToString(CultureInfo.InvariantCulture),
                Safe(p.ProjectName, TableNameCap),
                p.Province,
                Safe(p.KorAngle, DeadWhyCap),
            });

        if (duplicate.Count > 0)
        {
            b.H2("DUPLICATE — Flagged for MPI consolidation");
            b.P("Honing identified these as same-project duplicates. Worth a consolidation migration before the next drain cycle.");
            AppendBucketTable(b, duplicate,
                new[] { "Id", "Project", "Proponent", "Why DUPLICATE" },
                p => new[]
                {
                    p.MpiId.ToString(CultureInfo.InvariantCulture),
                    Safe(p.ProjectName, TableNameCap),
                    Safe(p.ProponentName, TableProponentCap),
                    Safe(p.KorAngle, DuplicateWhyCap),
                });
        }

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
        Func<PursuitBriefRow, string[]> rowOf)
    {
        if (bucket.Count == 0)
        {
            b.P("None in this honing pass.");
            return;
        }

        b.Table(headers, bucket.Select(rowOf).ToList());
    }

    private static string CostOf(PursuitBriefRow p)
    {
        if (!string.IsNullOrWhiteSpace(p.EstimatedCostText))
        {
            return p.EstimatedCostText;
        }

        return p.EstimatedCostCad is { } cad
            ? "$" + cad.ToString("N0", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static string Safe(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= max ? value : value[..max];
    }
}
