#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kor.Opportunities.Data.BdReports.Generators;

/// <summary>
/// Prime consultant identification report (BD-UI-Plan Phase B item 8),
/// structure mirrored from build-prime-consultant-report.ps1: exec summary,
/// KOR-aligned primes, top-30 by cost, firm frequency heatmap, top-10
/// drill-down, unknown-primes action table, synthesis. Data is live from
/// PrimeConsultantResearch enrichments (numeric cost ordering — audit M12).
/// </summary>
public static class PrimeConsultantReportGenerator
{
    public static BdReportDocument Build(
        IReadOnlyList<PrimeConsultantRow> projects,
        DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(projects);

        var withPrime = projects.Where(p => p.HasPrime).ToList();
        var withoutPrime = projects.Where(p => !p.HasPrime).ToList();
        var korAligned = projects.Where(p => p.KnownToKor).ToList();

        var b = new BdReportDocumentBuilder("KOR Structural — Prime Consultant Identification Report");

        b.Italic(
            "Prime consultant intelligence for KOR's BD pipeline. In public AEC work, the Prime Consultant (typically the architect) submits ONE team proposal to the owner — including KOR as structural sub. " +
            $"Knowing the prime = knowing who to approach BEFORE the RFP issues. Sourced from the bc-ab-primes research drains ({projects.Count} projects researched). " +
            $"Generated {generatedAtUtc:yyyy-MM-dd HH:mm} UTC from live KorOpportunitiesDb.");

        b.H2("Executive Summary");
        b.B("Projects researched: ", projects.Count.ToString(CultureInfo.InvariantCulture));
        b.B("Prime consultant identified: ", PctDisplay(withPrime.Count, projects.Count));
        b.B("Prime unknown after research: ", PctDisplay(withoutPrime.Count, projects.Count));
        b.B("KOR-aligned primes (warm-intro available): ", korAligned.Count.ToString(CultureInfo.InvariantCulture));
        b.P("Prime identification success rate varies by project type: high for public flagships (press coverage, permit applications), lower for early-stage private developments. Unknown primes typically require FOIP request or direct owner outreach.");

        b.H2("KOR-ALIGNED PRIMES — warm-intro available NOW");
        if (korAligned.Count > 0)
        {
            b.P("These projects have a Prime Consultant where KOR already has a relationship (per memory). Direct outreach to existing KOR contact at the firm. Highest-leverage immediate action.");
            b.Table(
                new[] { "Id", "Project", "Prime Firm", "Cost", "Province" },
                korAligned.Select(p => (IReadOnlyList<string>)new[]
                {
                    p.MpiId.ToString(CultureInfo.InvariantCulture),
                    Safe(p.ProjectName, 50),
                    Safe(p.PrimeFirm, 40),
                    p.CostDisplay,
                    p.Province,
                }).ToList());
        }
        else
        {
            b.P("No KOR-aligned primes identified in this research batch. (KOR-aligned status depends on Sonnet's read of KOR's memory client list — see honing brief details for nuance.)");
        }

        b.H2("Top 30 identified primes — by project cost");
        b.Table(
            new[] { "Id", "Project", "Prime Consultant", "Cost" },
            withPrime.Take(30).Select(p => (IReadOnlyList<string>)new[]
            {
                p.MpiId.ToString(CultureInfo.InvariantCulture),
                Safe(p.ProjectName, 45),
                Safe(p.PrimeFirm, 40),
                p.CostDisplay,
            }).ToList());

        b.H2("Prime Consultant Heatmap — most-frequent firms across pipeline");
        var frequency = withPrime
            .GroupBy(p => p.PrimeFirm!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .ToList();
        b.Table(
            new[] { "Prime Consultant Firm", "Projects", "Sample MPI Ids" },
            frequency.Select(g => (IReadOnlyList<string>)new[]
            {
                Safe(g.Key, 55),
                g.Count().ToString(CultureInfo.InvariantCulture),
                string.Join(", ", g.Take(4).Select(p => p.MpiId)),
            }).ToList());

        b.H2("Top 10 — drill-down with project details");
        var rank = 1;
        foreach (var p in withPrime.Take(10))
        {
            b.H3($"{rank}. {p.ProjectName}");
            b.B("Project Id: ", p.MpiId.ToString(CultureInfo.InvariantCulture));
            b.B("Province: ", p.Province);
            b.B("Proponent: ", p.ProponentName ?? string.Empty);
            b.B("Cost: ", p.CostDisplay);
            b.B("Prime Consultant: ", p.PrimeFirm ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(p.PrincipalInCharge))
            {
                b.B("Principal-in-Charge: ", p.PrincipalInCharge);
            }

            if (!string.IsNullOrWhiteSpace(p.ContactEmail))
            {
                b.B("Contact Email: ", p.ContactEmail);
            }

            if (p.Confidence is { } confidence)
            {
                b.B("Confidence: ", confidence.ToString("0.##", CultureInfo.InvariantCulture));
            }

            if (p.KnownToKor)
            {
                b.B("KOR Relationship: ", "KOR-aligned per memory — warm-intro available");
            }

            if (!string.IsNullOrWhiteSpace(p.KorAngle))
            {
                b.P(Safe(p.KorAngle, 500));
            }

            b.P(string.Empty);
            rank++;
        }

        if (withoutPrime.Count > 0)
        {
            b.H2("Unknown primes — recommended next moves");
            b.P("For these projects, Sonnet research could not identify a publicly-disclosed prime consultant. Typically requires FOIP request (BC) / FOIP request (AB) / direct owner contact / building permit application search.");
            b.Table(
                new[] { "Id", "Project", "Cost", "Province", "Recommended Action" },
                withoutPrime.Take(20).Select(p => (IReadOnlyList<string>)new[]
                {
                    p.MpiId.ToString(CultureInfo.InvariantCulture),
                    Safe(p.ProjectName, 50),
                    p.CostDisplay,
                    p.Province,
                    "FOIP request / owner direct outreach",
                }).ToList());
        }

        b.H2("Strategic Synthesis");
        b.H3("Most-leveraged prime relationships");
        b.P("Per the heatmap above, firms appearing 3+ times across KOR's pipeline = highest-leverage relationships to build/deepen. One sustained Principal-in-Charge relationship at these firms unlocks recurring KOR structural sub positioning across multiple projects.");
        b.H3("EllisDon-as-prime pattern");
        b.P("EllisDon Design Build appears as Prime on multiple major BC healthcare projects (Royal Columbian Phases 2 & 3, RIH Phase 2 Completion). This validates the Strategic Relationships report finding that EllisDon BC office (Murphy / Husband / Enns / MacDonald / McFarlane) is a cross-sector compounding target — already known healthcare engagement now confirmed at prime level.");
        b.H3("Public-PD architects as scoreable team leaders");
        b.P("Hariri Pontarini, Diamond Schmitt, KPMB, Bing Thom/Revery, Acton Ostry — these are the public-facing scoreable architect names KOR's team-sub pitch must align with. Position KOR's P.Eng count + similar-project experience as the structural complement these firms need to win publicly.");
        b.H3("Unknown primes are not necessarily dead");
        b.P("Unknown-prime projects often represent EARLIEST-stage opportunities — the prime hasn't been selected yet because the owner is still in master-planning. These are DISCOVER candidates — relationship-build with the OWNER (not the prime, who doesn't exist yet).");

        b.H2("Recommended next actions");
        b.P("1. KOR-aligned primes table — immediate outreach via existing KOR relationship at named firms.");
        b.P("2. EllisDon BC office — re-engage on Royal Columbian, RIH, future BC healthcare. 5 named contacts already in pipeline.");
        b.P("3. Hariri Pontarini + Adamson + Stantec on SFU Medicine — initiate structural-sub positioning via Stantec (prime team includes Stantec captive structural; opportunity may be on Phase 2 or future SFU pursuits).");
        b.P("4. Iredale Architecture (Build Canada Homes consortium) — DASH stream BD opportunity. KOR-aligned per memory.");
        b.P("5. Top 20 unknown-prime projects — FOIP request to BC/AB municipalities + Health Authorities + School Districts for development permit / RFI documentation.");
        b.P("6. Audit the prime heatmap quarterly — firms appearing on 5+ KOR pipeline projects are top-tier relationship priorities.");

        b.H2("Methodology");
        b.P($"Sourced from {projects.Count} Sonnet-researched briefs via the bc-ab-primes drain queue (PROMPT.md at tools/BdQueueDrainPrompts/bc-ab-primes/PROMPT.md). Each brief required: named prime consultant firm with sourceUrl, named Principal-in-Charge where discoverable, KOR relationship status (BMZ legacy check included), KOR pursuit verdict, 12-month engagement timeline. Briefs without prime identification document the search trail.");
        b.Italic("Live from MajorProjectEnrichment.ResultJson where ProviderName = 'PrimeConsultantResearch'. Drill-down per project via Project Brief in the BD workspace.");

        return b.Build();
    }

    private static string PctDisplay(int part, int total)
    {
        var pct = total == 0 ? 0 : (int)Math.Round(100.0 * part / total);
        return $"{part} ({pct}%)";
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
