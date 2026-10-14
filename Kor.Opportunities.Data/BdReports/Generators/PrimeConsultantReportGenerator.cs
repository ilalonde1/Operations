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
        var korAligned = projects.Where(p => p.HasPrime && p.KnownToKor == true).ToList();
        var complete = projects.Count(p => p.ResearchComplete);
        // A payload that names no firm but carries an explicit flag and a note saying why
        // (paused, unfunded, duplicate) is a NEGATIVE FINDING, not a partial payload. On
        // 2026-10-13 that was 99 of 148 rows, and this report called them "unusable".
        var negative = projects.Count(p => p.PrimeNotIdentifiedByResearch);
        var partial = projects.Count - complete - negative;

        var b = new BdReportDocumentBuilder("KOR Structural — Prime Consultant Identification Report");

        b.P(BdReportFormatting.SourceProvenance);

        b.Italic(
            "Prime consultant intelligence for KOR's BD pipeline. In public AEC work, the Prime Consultant (typically the architect) submits ONE team proposal to the owner — including KOR as structural sub. " +
            $"Retrieved {projects.Count} research records: {complete} name a firm with a relationship assessment, {negative} record a negative finding (no prime identified, with the reason where known), {partial} are partial or unusable. Retrieval alone does not establish completed research. " +
            $"Generated {generatedAtUtc:yyyy-MM-dd HH:mm} UTC from live KorOpportunitiesDb.");

        b.H2("Executive Summary");
        b.B("Research records retrieved: ", projects.Count.ToString(CultureInfo.InvariantCulture));
        b.B("Usable firm and relationship assessments: ", complete.ToString(CultureInfo.InvariantCulture));
        b.B("Negative findings (research found no prime; reason recorded where known): ", negative.ToString(CultureInfo.InvariantCulture));
        b.B("Partial or unusable research: ", partial.ToString(CultureInfo.InvariantCulture));
        b.B("Prime consultant identified: ", PctDisplay(withPrime.Count, projects.Count));
        b.B("Prime not identified in available payload: ", PctDisplay(withoutPrime.Count, projects.Count));
        b.B("Relationship assessment unknown: ", projects.Count(p => p.KnownToKor is null).ToString(CultureInfo.InvariantCulture));
        b.B("Primes reported known to KOR: ", korAligned.Count.ToString(CultureInfo.InvariantCulture));
        b.P("Prime identification success rate varies by project type: high for public flagships (press coverage, permit applications), lower for early-stage private developments. Unknown primes typically require FOIP request or direct owner outreach.");

        b.H2("Primes reported known to KOR — confirm relationship before outreach");
        if (korAligned.Count > 0)
        {
            b.P("These research payloads report a KOR relationship. A current contact or available introduction has not been established by this report.");
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

            b.B("KOR Relationship: ", p.KnownToKor switch
            {
                true => "Reported known to KOR; current introduction availability unverified",
                false => "Research explicitly reports not known to KOR",
                null => "Unknown / not assessed in the research payload",
            });

            if (!string.IsNullOrWhiteSpace(p.KorAngle))
            {
                b.P(Safe(p.KorAngle, 500));
            }

            b.P(string.Empty);
            rank++;
        }

        if (withoutPrime.Count > 0)
        {
            b.H2("No prime identified — what the research recorded");
            b.P("For these projects the research names no prime firm. Where the payload says why (procurement paused, unfunded, a duplicate, not yet disclosed) that note is shown; a payload with no assessment at all is marked unusable. Neither case establishes that no prime has been selected.");
            b.Table(
                new[] { "Id", "Project", "Cost", "Province", "Research note" },
                withoutPrime.Take(20).Select(p => (IReadOnlyList<string>)new[]
                {
                    p.MpiId.ToString(CultureInfo.InvariantCulture),
                    Safe(p.ProjectName, 50),
                    p.CostDisplay,
                    p.Province,
                    p.HasUsableResearch
                        ? Safe(p.ResearchNote ?? "Negative finding; no reason recorded", 160)
                        : "Unusable payload: no assessment recorded; re-research",
                }).ToList());
        }

        b.H2("Strategic Synthesis");
        b.Italic("Historical guidance from the 2026-06-09 analyst sweep; named projects, contacts and recommendations below are not verified by this report.");
        b.H3("Most-leveraged prime relationships");
        b.P("Per the heatmap above, firms appearing 3+ times across KOR's pipeline = highest-leverage relationships to build/deepen. One sustained Principal-in-Charge relationship at these firms unlocks recurring KOR structural sub positioning across multiple projects.");
        b.H3("EllisDon-as-prime pattern");
        b.P("EllisDon Design Build appears as Prime on multiple major BC healthcare projects (Royal Columbian Phases 2 & 3, RIH Phase 2 Completion). This validates the Strategic Relationships report finding that EllisDon BC office (Murphy / Husband / Enns / MacDonald / McFarlane) is a cross-sector compounding target — already known healthcare engagement now confirmed at prime level.");
        b.H3("Public-PD architects as scoreable team leaders");
        b.P("Hariri Pontarini, Diamond Schmitt, KPMB, Bing Thom/Revery, Acton Ostry — these are the public-facing scoreable architect names KOR's team-sub pitch must align with. Position KOR's P.Eng count + similar-project experience as the structural complement these firms need to win publicly.");
        b.H3("Unknown primes are not necessarily dead");
        b.P("An unknown prime is a research gap. Neither project stage nor whether a prime exists can be inferred from that gap.");

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

        return BdReportFormatting.Abridge(value, max);
    }
}
