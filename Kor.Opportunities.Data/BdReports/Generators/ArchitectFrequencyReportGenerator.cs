#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kor.Opportunities.Data.BdReports.Generators;

/// <summary>
/// Architect warm-intro priority list (BD-UI-Plan Phase B item 8), structure
/// mirrored from tools/BdReportBuilders/build-architect-freq-report.ps1.
/// IMPORTANT methodology change vs the PS builder: the original ranked by a
/// text-mining pull (.architect-freq.json) whose query was never saved and
/// whose counts are not reconstructible from any table. This generator ranks
/// from the STRUCTURED org graph instead — MajorProjectsInventory.
/// ArchitectCanonicalOrgId links + org-scoped Intel depth — same intent
/// (one architect relationship = N projects), fully auditable.
/// </summary>
public static class ArchitectFrequencyReportGenerator
{
    public static BdReportDocument Build(
        IReadOnlyList<ArchitectLeverageRow> architects,
        DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(architects);

        var b = new BdReportDocumentBuilder("KOR Structural — Architect Frequency Report (Warm-Intro Priority List)");

        b.Italic(
            "Architects ranked by linked active projects in KOR's BD pipeline (structured ArchitectCanonicalOrgId links + org-scoped intel depth). " +
            "The premise: in public AEC work, the prime consultant (typically architect) submits ONE team proposal with structural sub included. " +
            "One architect relationship = N projects. " +
            $"Generated {generatedAtUtc:yyyy-MM-dd HH:mm} UTC from live KorOpportunitiesDb.");

        b.H2($"Top {Math.Min(20, architects.Count)} highest-leverage architects");
        b.Table(
            new[] { "Architect", "Active projects", "$ Pipeline (M)", "Signals", "People", "Open actions", "KOR status" },
            architects.Take(20).Select(a => (IReadOnlyList<string>)new[]
            {
                a.DisplayName,
                a.ActiveProjects.ToString(CultureInfo.InvariantCulture),
                $"${a.PipelineCostCad / 1_000_000m:N0}M",
                a.Signals.ToString(CultureInfo.InvariantCulture),
                a.People.ToString(CultureInfo.InvariantCulture),
                a.OpenActions.ToString(CultureInfo.InvariantCulture),
                IsKorAligned(a.DisplayName) ? "KOR-aligned" : "New target",
            }).ToList());
        b.P("More linked active projects = more team slots KOR can ride on one relationship. KOR-aligned status indicates the architect appears in KOR's established relationship list (per memory).");

        b.H2("Top 10 architects — drill-down with projects");
        foreach (var a in architects.Take(10))
        {
            b.H3(a.DisplayName);
            b.B("Linked active projects: ", $"{a.ActiveProjects} (${a.PipelineCostCad / 1_000_000m:N0}M pipeline)");
            b.B("Intel depth: ", $"Signals: {a.Signals} | People: {a.People} | Open actions: {a.OpenActions}");
            b.B("KOR relationship: ", IsKorAligned(a.DisplayName)
                ? "KOR-aligned (per memory client list)"
                : "NEW target — no prior memory reference");
            b.P("Top projects in pipeline:");
            if (a.TopProjects.Count > 0)
            {
                foreach (var p in a.TopProjects)
                {
                    b.P($"  - {p}");
                }
            }
            else
            {
                b.P("  (No costed projects linked yet — see Org Dossier for intel detail.)");
            }

            b.P(string.Empty);
        }

        var korAligned = architects.Where(a => IsKorAligned(a.DisplayName)).Take(25).ToList();
        b.H2("KOR-aligned architects (warm-intro lever)");
        b.P($"{korAligned.Count} KOR-aligned architects are already in KOR's memory client list. These are immediate warm-intro candidates — KOR likely has a Principal-in-Charge relationship to leverage.");
        if (korAligned.Count > 0)
        {
            b.Table(
                new[] { "Architect", "Active projects", "Approach" },
                korAligned.Select(a => (IReadOnlyList<string>)new[]
                {
                    a.DisplayName,
                    a.ActiveProjects.ToString(CultureInfo.InvariantCulture),
                    "BMZ-legacy check + Principal-in-Charge outreach",
                }).ToList());
        }

        var newTargets = architects.Where(a => !IsKorAligned(a.DisplayName)).Take(15).ToList();
        b.H2("NEW high-leverage architects (cold-outreach campaign)");
        b.P($"{newTargets.Count} non-aligned architects do NOT appear in KOR's memory client list. These represent NEW relationship-building targets — combined {newTargets.Sum(a => a.ActiveProjects)} linked active projects of compounding pursuit potential.");
        if (newTargets.Count > 0)
        {
            b.Table(
                new[] { "Architect", "Active projects", "Approach" },
                newTargets.Select(a => (IReadOnlyList<string>)new[]
                {
                    a.DisplayName,
                    a.ActiveProjects.ToString(CultureInfo.InvariantCulture),
                    "Cold-outreach via LinkedIn / industry events / referrals",
                }).ToList());
        }

        b.H2("Strategic Synthesis");
        b.H3("Architect frequency is a KOR sub-strategy lever");
        b.P("In public AEC procurement, KOR wins as the structural sub on the architect's team. The architect submits one team proposal, the team is scored on collective expertise. Higher KOR P.Eng count + similar-project experience = higher team score = more wins.");

        b.H3("Top-5 single relationships unlocking compounding pipeline");
        b.P("Based on linked-project ranking, the 5 highest-leverage architect relationships to BUILD (or DEEPEN if KOR-aligned) are:");
        var rank = 1;
        foreach (var a in architects.Take(5))
        {
            b.P($"  {rank}. {a.DisplayName} — {a.ActiveProjects} linked active projects (${a.PipelineCostCad / 1_000_000m:N0}M)");
            rank++;
        }

        b.H3("Workflow recommendation");
        b.P("For each architect in top 10:");
        b.P("  1. Audit KOR's portfolio for prior projects with this architect (BMZ legacy included).");
        b.P("  2. Identify Principal-in-Charge — search firm's leadership team for the one most aligned with KOR's project sectors.");
        b.P("  3. Direct outreach via LinkedIn + email. Reference shared past projects (if any) or specific upcoming pursuit.");
        b.P("  4. Industry event presence — UDI Awards, NAIOP BC, AIBC events, BC Recreation and Parks Association, BC Construction Association events. Architects are accessible at these venues.");

        b.H2("Next steps");
        b.P("1. Build relationships with top 5 architects — concrete monthly outreach plan per architect.");
        b.P("2. For NEW architects (non-KOR-aligned) — pursue an introduction-via-shared-past-project angle. Most architects in BC have crossed paths with BMZ pre-2021.");
        b.P("3. Architect links grow as primes ingests land — re-generate this report after each drain cycle; unlinked projects don't rank here.");

        return b.Build();
    }

    // KOR memory client architects, ported verbatim from
    // build-architect-freq-report.ps1.
    private static readonly string[] KorArchitects =
    {
        "Chris Dikeakos", "CDA Architects", "Ciccozzi", "Bing Thom", "Revery",
        "IBI", "Arcadis", "Henriquez", "MCMP", "Acton Ostry", "GBL", "Perkins",
        "Yamamoto", "RH Architecture", "HCMA", "KMBR", "Iredale", "NSDA",
        "GGA-Architecture", "Gibbs Gage", "GEC", "S2 Architecture", "Workun Garrick",
        "DIALOG", "Stantec", "Kasian",
    };

    private static bool IsKorAligned(string name)
        => KorArchitects.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));
}
