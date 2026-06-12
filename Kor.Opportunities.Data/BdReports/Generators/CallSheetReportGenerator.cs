#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kor.Opportunities.Data.BdReports.Generators;

/// <summary>
/// Cross-cutting weekly call sheet (BD-UI-Plan Phase B item 1), structure
/// mirrored from tools/BdReportBuilders/build-callsheet.ps1: top-3 most
/// time-sensitive, remaining URGENT items, the open PURSUE pool, strategic
/// relationship prose (config, refreshed with honing), and a live per-sector
/// status table. Data sections are live; the PS builder's hand-picked top-3
/// becomes the top-3 of the ranked urgent pool (urgency flag, then estimated
/// cost — time-sensitivity is not a structured field).
/// </summary>
public static class CallSheetReportGenerator
{
    private const int TopAngleCap = 600;
    private const int UrgentAngleCap = 450;
    private const int PursueTableCap = 25;

    public static BdReportDocument Build(
        IReadOnlyList<PursuitBriefRow> pool,
        IReadOnlyList<SectorVerdictSummary> sectorSummaries,
        DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(sectorSummaries);

        var urgent = pool.Where(x => x.IsUrgent).ToList();
        var pursue = pool.Where(x => !x.IsUrgent).ToList();
        var top = urgent.Take(3).ToList();
        var remaining = urgent.Skip(3).ToList();

        var b = new BdReportDocumentBuilder("KOR — This Week's BD Call Sheet");

        b.Italic(
            "Cross-cutting action sheet pulled across all BD sectors. PURSUE_URGENT + PURSUE items ranked by urgency then project value. " +
            $"This is what to act on THIS WEEK. Generated {generatedAtUtc:yyyy-MM-dd HH:mm} UTC from live KorOpportunitiesDb honing verdicts.");

        var pipelineCad = pool.Sum(p => p.EstimatedCostCad ?? 0);
        b.Kpis(
            new KpiItem(urgent.Count.ToString(CultureInfo.InvariantCulture), "Urgent this week", urgent.Count > 0 ? ChipTone.Urgent : ChipTone.Neutral),
            new KpiItem(pursue.Count.ToString(CultureInfo.InvariantCulture), "Open pursue", ChipTone.Positive),
            new KpiItem(CadCompact(pipelineCad), "Pipeline (CAD)"),
            new KpiItem(sectorSummaries.Count.ToString(CultureInfo.InvariantCulture), "Sectors reporting"));

        b.H2($"THE {top.Count} most time-sensitive — call/email THIS WEEK");
        var rank = 1;
        foreach (var p in top)
        {
            b.H3($"{rank}. {p.ProjectName} ({CostOf(p)})");
            b.Chips(new ChipItem(p.Verdict ?? "NO VERDICT", ChipTones.ForVerdict(p.Verdict)));
            b.B("Id: ", p.MpiId.ToString(CultureInfo.InvariantCulture));
            b.B("Province: ", p.Province);
            b.B("Proponent: ", p.ProponentName ?? string.Empty);
            b.B("Status: ", p.HoningStatus ?? string.Empty);
            b.P(Safe(p.KorAngle, TopAngleCap));
            b.P(string.Empty);
            rank++;
        }

        if (top.Count == 0)
        {
            b.P("No URGENT items in the current honing data.");
        }

        if (remaining.Count > 0)
        {
            b.H2($"PURSUE_URGENT — additional {remaining.Count} items");
            foreach (var p in remaining)
            {
                b.H3($"{p.ProjectName} ({CostOf(p)})");
                b.P($"MPI {p.MpiId} — {p.Province}{(string.IsNullOrWhiteSpace(p.MunicipalityName) ? string.Empty : ", " + p.MunicipalityName)}. {Safe(p.KorAngle, UrgentAngleCap)}");
            }
        }

        b.H2($"PURSUE pool — top {Math.Min(pursue.Count, PursueTableCap)} of {pursue.Count} open opportunities");
        if (pursue.Count == 0)
        {
            b.P("No open PURSUE items in the current honing data.");
        }
        else
        {
            b.Table(
                new[] { "Id", "Project", "Province", "Proponent", "Cost" },
                pursue.Take(PursueTableCap).Select(p => (IReadOnlyList<string>)new[]
                {
                    p.MpiId.ToString(CultureInfo.InvariantCulture),
                    Safe(p.ProjectName, 60),
                    p.Province,
                    Safe(p.ProponentName, 30),
                    CostOf(p),
                }).ToList(),
                new[] { ColumnAlignment.Right, ColumnAlignment.Left, ColumnAlignment.Left, ColumnAlignment.Left, ColumnAlignment.Right });
        }

        b.H2("Strategic Relationship Targets (compounding leverage)");
        foreach (var target in StrategicTargets)
        {
            b.H3(target.Title);
            foreach (var paragraph in target.Paragraphs)
            {
                b.P(paragraph);
            }
        }

        b.H2("Status of all BD sectors");
        b.Table(
            new[] { "Sector", "Honed", "URGENT", "PURSUE", "MONITOR", "DISCOVER" },
            sectorSummaries.Select(s => (IReadOnlyList<string>)new[]
            {
                s.SectorTitle,
                $"{s.Total - s.NoVerdict}/{s.Total}",
                s.PursueUrgent.ToString(CultureInfo.InvariantCulture),
                s.Pursue.ToString(CultureInfo.InvariantCulture),
                s.Monitor.ToString(CultureInfo.InvariantCulture),
                s.Discover.ToString(CultureInfo.InvariantCulture),
            }).ToList(),
            new[] { ColumnAlignment.Left, ColumnAlignment.Center, ColumnAlignment.Right, ColumnAlignment.Right, ColumnAlignment.Right, ColumnAlignment.Right });

        b.H2("Where to find more detail");
        b.P("Every sector report generates on demand from the BD Reports tile (this window). Per-project intel: Org Dossier for enriched canonical orgs, Project Brief for any MPI with honing-pass data, Region Brief for BC + AB rolled-up category views.");

        return b.Build();
    }

    // Ported from build-callsheet.ps1 "Strategic Relationship Targets"
    // (2026-06-08 honing sweep). Config prose — refresh with the next sweep.
    private static readonly IReadOnlyList<SynthesisSection> StrategicTargets = new SynthesisSection[]
    {
        new(
            "MST Development Corp (Musqueam-Squamish-Tsleil-Waututh)",
            new[]
            {
                "Single highest-leverage Indigenous BD relationship. Controls Jericho Lands (Id 6911), Maplewood Innovation District (Id 4882), Tsleil-Waututh North Shore Innovation District (Id 6913). Billion-dollar multi-tower phased developments. One MST relationship = recurring across Phase 1-N.",
            }),
        new(
            "TELUS Living — Manasweeta Bhatia",
            new[]
            {
                "40 sites under evaluation, 20 in planning/construction. Repurposing 2,300+ institutional properties. Single TELUS Vancouver HQ relationship unlocks national pipeline. KOR mid-rise concrete is direct fit.",
            }),
        new(
            "Graham Design Builders LP — Alex Trifunov",
            new[]
            {
                "$3.4B+ active BC healthcare pipeline. Pre-construction Manager Vancouver office is the entry. Dawson Creek Hospital structural NOT publicly named — live KOR intel gap.",
            }),
        new(
            "CSF (Conseil scolaire francophone)",
            new[]
            {
                "Single province-wide francophone SD with multiple PURSUE projects (Beausoleil + École Élémentaire Beausoleil + École Beausoleil K-12). One relationship = entire CSF BC pipeline.",
            }),
        new(
            "CSU CPDC — California Seismic Pipeline",
            new[]
            {
                "Lisa Salgado at CSULB (Peterson Hall $259M) is the entry. CSU Chancellor's Office Capital Planning + Design + Construction controls seismic replacement across 23 California campuses. Per-campus PM entry → CPDC master gate.",
            }),
    };

    private static string CadCompact(decimal cad) => cad switch
    {
        >= 1_000_000_000m => $"${cad / 1_000_000_000m:F1}B",
        >= 1_000_000m => $"${cad / 1_000_000m:N0}M",
        _ => $"${cad:N0}",
    };

    private static string CostOf(PursuitBriefRow p)
    {
        if (!string.IsNullOrWhiteSpace(p.EstimatedCostText))
        {
            return p.EstimatedCostText;
        }

        return p.EstimatedCostCad is { } cad
            ? "$" + cad.ToString("N0", CultureInfo.InvariantCulture)
            : "cost unknown";
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
