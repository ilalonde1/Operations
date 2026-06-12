#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kor.Opportunities.Data.BdReports.Generators;

/// <summary>
/// Live pursuit dossiers over the completed relationship graph (the
/// -PursuitGaps campaign payoff): every PURSUE/PURSUE_URGENT MPI with its
/// resolved architect / structural-engineer / GC CanonicalOrg edges. Exec
/// graph-coverage summary, urgent team-state table, architect-anchored
/// clusters (one relationship = N pursuits), top-cost drill-down dossiers,
/// and the remaining-gaps worklist. All content is data-driven — no
/// hand-authored synthesis to drift from the graph.
/// </summary>
public static class PursuitDossierReportGenerator
{
    private const int DrillDownCount = 15;
    private const int GapTableCount = 20;

    public static BdReportDocument Build(
        IReadOnlyList<PursuitDossierRow> pursuits,
        DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(pursuits);

        var urgent = pursuits.Where(p => p.IsUrgent).ToList();
        var withArchitect = pursuits.Where(p => p.HasArchitectEdge).ToList();
        var withIncumbentSe = pursuits.Where(p => p.HasIncumbentSe).ToList();
        var korSeated = pursuits.Where(p => p.StructuralEngineerIsKor).ToList();
        var gaps = pursuits.Where(p => !p.HasArchitectEdge).ToList();
        var pipelineCad = pursuits.Sum(p => p.EstimatedCostCad ?? 0);

        var b = new BdReportDocumentBuilder("KOR Structural — Pursuit Dossiers (Live Relationship Graph)");

        b.Italic(
            "One dossier per active pursuit, read live from the org graph: who designs it, who holds the structural seat, who builds it, and how warm KOR's path in is. " +
            "On public AEC work KOR wins as the architect's structural sub — the architect edge IS the route to the pursuit. " +
            $"Generated {generatedAtUtc:yyyy-MM-dd HH:mm} UTC from live KorOpportunitiesDb.");

        b.H2("Executive Summary");
        b.Kpis(
            new KpiItem(pursuits.Count.ToString(CultureInfo.InvariantCulture), "Active pursuits"),
            new KpiItem(urgent.Count.ToString(CultureInfo.InvariantCulture), "Urgent", urgent.Count > 0 ? ChipTone.Urgent : ChipTone.Neutral),
            new KpiItem(CadCompact(pipelineCad), "Pipeline (CAD)"),
            new KpiItem(PctCompact(withArchitect.Count, pursuits.Count), "Architect edge"),
            new KpiItem(gaps.Count.ToString(CultureInfo.InvariantCulture), "Graph gaps", gaps.Count > 0 ? ChipTone.Caution : ChipTone.Positive));
        b.B("Architect edge resolved: ", PctDisplay(withArchitect.Count, pursuits.Count));
        b.B("Incumbent structural engineer known: ", withIncumbentSe.Count.ToString(CultureInfo.InvariantCulture));
        if (korSeated.Count > 0)
        {
            b.B("KOR already holds the structural seat: ", korSeated.Count.ToString(CultureInfo.InvariantCulture));
        }

        b.B("General contractor known: ", pursuits.Count(p => p.GeneralContractorOrgId is not null).ToString(CultureInfo.InvariantCulture));

        if (urgent.Count > 0)
        {
            b.H2("URGENT pursuits — team state");
            b.P("Act-now pool with everything the graph knows about each team. A named architect with intel depth = a warm path that already exists; an empty Architect cell = route-in unknown.");
            b.Table(
                new[] { "Id", "Pursuit", "Cost", "Architect", "Structural Engineer", "GC" },
                urgent.Select(p => (IReadOnlyList<string>)new[]
                {
                    p.MpiId.ToString(CultureInfo.InvariantCulture),
                    Safe(p.ProjectName, 45),
                    p.CostDisplay,
                    Safe(p.ArchitectName, 35),
                    SeDisplay(p, 30),
                    Safe(p.GeneralContractorName, 25),
                }).ToList(),
                new[] { ColumnAlignment.Right, ColumnAlignment.Left, ColumnAlignment.Right, ColumnAlignment.Left, ColumnAlignment.Left, ColumnAlignment.Left });
        }

        var clusters = withArchitect
            .GroupBy(p => p.ArchitectOrgId!.Value)
            .Where(g => g.Count() >= 2)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Sum(p => p.EstimatedCostCad ?? 0))
            .ToList();
        if (clusters.Count > 0)
        {
            b.H2("Architect-anchored clusters — one relationship, several pursuits");
            b.P("Architects linked to 2+ active pursuits. A single sustained relationship here compounds across every row in the cluster — these are the highest-leverage doors in the pipeline.");
            foreach (var cluster in clusters)
            {
                var anchor = cluster.First();
                b.H3($"{anchor.ArchitectName} — {cluster.Count()} pursuits, {CadDisplay(cluster.Sum(p => p.EstimatedCostCad ?? 0))}");
                b.B("Org intel on file: ", IntelDepthDisplay(anchor));
                b.Table(
                    new[] { "Id", "Pursuit", "Cost", "Province", "Verdict" },
                    cluster
                        .OrderByDescending(p => p.EstimatedCostCad ?? decimal.MinValue)
                        .Select(p => (IReadOnlyList<string>)new[]
                        {
                            p.MpiId.ToString(CultureInfo.InvariantCulture),
                            Safe(p.ProjectName, 45),
                            p.CostDisplay,
                            p.Province,
                            p.Verdict ?? string.Empty,
                        }).ToList(),
                    new[] { ColumnAlignment.Right, ColumnAlignment.Left, ColumnAlignment.Right, ColumnAlignment.Left, ColumnAlignment.Left });
            }
        }

        if (pursuits.Count > 0)
        {
            b.H2($"Top {Math.Min(DrillDownCount, pursuits.Count)} pursuits — dossiers");
        }

        var rank = 1;
        foreach (var p in pursuits
                     .OrderByDescending(x => x.EstimatedCostCad ?? decimal.MinValue)
                     .Take(DrillDownCount))
        {
            b.H3($"{rank}. {p.ProjectName}");
            var dossierChips = new List<ChipItem> { new(p.Verdict ?? "NO VERDICT", ChipTones.ForVerdict(p.Verdict)) };
            if (p.StructuralEngineerIsKor)
            {
                dossierChips.Add(new ChipItem("KOR SEAT", ChipTone.Positive));
            }

            if (!p.HasArchitectEdge)
            {
                dossierChips.Add(new ChipItem("NO ARCHITECT EDGE", ChipTone.Neutral));
            }

            b.Chips(dossierChips.ToArray());
            b.B("Project Id: ", p.MpiId.ToString(CultureInfo.InvariantCulture));
            b.B("Cost: ", p.CostDisplay);
            b.B("Location: ", Location(p));
            if (!string.IsNullOrWhiteSpace(p.Stage))
            {
                b.B("Stage: ", p.Stage);
            }

            if (!string.IsNullOrWhiteSpace(p.ProponentName))
            {
                b.B("Proponent: ", p.ProponentName);
            }

            b.B("Architect: ", p.HasArchitectEdge
                ? $"{p.ArchitectName} ({IntelDepthDisplay(p)})"
                : "UNRESOLVED — no graph edge yet");
            if (p.StructuralEngineerIsKor)
            {
                b.B("Structural seat: ", "KOR — already ours, defend it");
            }
            else if (p.HasIncumbentSe)
            {
                b.B("Incumbent structural engineer: ", $"{p.StructuralEngineerName} — displacement pursuit");
            }

            if (!string.IsNullOrWhiteSpace(p.GeneralContractorName))
            {
                b.B("General contractor: ", p.GeneralContractorName);
            }

            if (!string.IsNullOrWhiteSpace(p.KorAngle))
            {
                b.P(Safe(p.KorAngle, 600));
            }

            b.P(string.Empty);
            rank++;
        }

        if (gaps.Count > 0)
        {
            b.H2("Graph gaps — pursuits with no architect edge");
            b.P("The route into these pursuits is unknown: no architect link in the graph. They are the remaining -PursuitGaps research worklist, cost-descending — every edge resolved here is resolved forever.");
            b.Table(
                new[] { "Id", "Pursuit", "Cost", "Province", "Proponent" },
                gaps
                    .OrderByDescending(p => p.EstimatedCostCad ?? decimal.MinValue)
                    .Take(GapTableCount)
                    .Select(p => (IReadOnlyList<string>)new[]
                    {
                        p.MpiId.ToString(CultureInfo.InvariantCulture),
                        Safe(p.ProjectName, 45),
                        p.CostDisplay,
                        p.Province,
                        Safe(p.ProponentName, 35),
                    }).ToList(),
                new[] { ColumnAlignment.Right, ColumnAlignment.Left, ColumnAlignment.Right, ColumnAlignment.Left, ColumnAlignment.Left });
            if (gaps.Count > GapTableCount)
            {
                b.P($"...and {gaps.Count - GapTableCount} more below the cost cut-off.");
            }
        }

        b.H2("Methodology");
        b.P(
            "Pursuit pool = active MajorProjectsInventory rows whose ProjectBriefHoning verdict is PURSUE or PURSUE_URGENT. " +
            "Team edges are CanonicalOrg foreign keys on the MPI row (Architect / StructuralEngineer / GeneralContractor), resolved by the -PursuitGaps graph-completion campaign and maintained by ingest; retired orgs read as gaps, never as stale names. " +
            "Architect intel depth = org-scoped IntelSignal + IntelPersonAffiliation + open IntelAction counts, the same recipe as the Architect Frequency report.");
        b.Italic("Live from KorOpportunitiesDb via IBdReportService.GetPursuitDossiersAsync — dashboard, DOCX and AI answers share this query.");

        return b.Build();
    }

    private static string SeDisplay(PursuitDossierRow p, int max)
    {
        if (p.StructuralEngineerIsKor)
        {
            return "KOR (ours)";
        }

        return p.StructuralEngineerName is null ? string.Empty : Safe(p.StructuralEngineerName, max);
    }

    private static string IntelDepthDisplay(PursuitDossierRow p) =>
        $"{p.ArchitectPeople} people, {p.ArchitectSignals} signals, {p.ArchitectOpenActions} open actions";

    private static string Location(PursuitDossierRow p) =>
        string.IsNullOrWhiteSpace(p.MunicipalityName) ? p.Province : $"{p.MunicipalityName}, {p.Province}";

    private static string CadDisplay(decimal cad) => cad switch
    {
        >= 1_000_000_000m => $"${cad / 1_000_000_000m:F1}B CAD",
        >= 1_000_000m => $"${cad / 1_000_000m:N0}M CAD",
        _ => $"${cad:N0} CAD",
    };

    private static string CadCompact(decimal cad) => cad switch
    {
        >= 1_000_000_000m => $"${cad / 1_000_000_000m:F1}B",
        >= 1_000_000m => $"${cad / 1_000_000m:N0}M",
        _ => $"${cad:N0}",
    };

    private static string PctCompact(int part, int total)
        => total == 0 ? "—" : $"{(int)Math.Round(100.0 * part / total)}%";

    private static string PctDisplay(int part, int total)
    {
        var pct = total == 0 ? 0 : (int)Math.Round(100.0 * part / total);
        return $"{part} of {total} ({pct}%)";
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
