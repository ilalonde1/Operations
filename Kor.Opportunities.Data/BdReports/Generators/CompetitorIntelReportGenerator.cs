#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kor.Opportunities.Data.BdReports.Generators;

/// <summary>
/// Competitor intelligence report (BD-UI-Plan Phase B item 8), structure
/// mirrored from tools/BdReportBuilders/build-competitor-intel.ps1. The
/// ranking recipe is the builder's own (verified): org-scoped IntelSignal +
/// IntelPersonAffiliation + IntelAction counts on CanonicalOrg.Kind =
/// Competitor. The original BC/AB-vs-US split came from a lost pull and is
/// not reconstructible ($.market exists on too few CompetitorSignals rows),
/// so the live report ships ONE ranked footprint table; the US pattern
/// reading stays in the synthesis prose.
/// </summary>
public static class CompetitorIntelReportGenerator
{
    public static BdReportDocument Build(
        IReadOnlyList<CompetitorFootprintRow> competitors,
        DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(competitors);

        var b = new BdReportDocumentBuilder("KOR Structural — Competitor Intelligence Report");

        b.Italic(
            "Structural engineering competitors mapped across KOR's BD pipeline. Where they're entrenched + which projects they hold = where to focus KOR's differentiation. " +
            "Sources: IntelSignal references + IntelPersonAffiliation + IntelAction on Competitor-kind canonical orgs. " +
            $"Generated {generatedAtUtc:yyyy-MM-dd HH:mm} UTC from live KorOpportunitiesDb.");

        b.H2($"Competitor footprint — top {competitors.Count} by intel references");
        b.P("These are KOR's tracked rivals across the BC + AB + US West Coast structural-engineering market. Tracking their footprint via Intel references gives KOR a flip-opportunity heatmap.");
        b.Table(
            new[] { "Competitor", "Signals", "People", "Actions", "Total", "Linked projects" },
            competitors.Select(c => (IReadOnlyList<string>)new[]
            {
                c.DisplayName,
                c.Signals.ToString(CultureInfo.InvariantCulture),
                c.People.ToString(CultureInfo.InvariantCulture),
                c.Actions.ToString(CultureInfo.InvariantCulture),
                c.Total.ToString(CultureInfo.InvariantCulture),
                c.LinkedProjects.ToString(CultureInfo.InvariantCulture),
            }).ToList(),
            new[] { ColumnAlignment.Left, ColumnAlignment.Right, ColumnAlignment.Right, ColumnAlignment.Right, ColumnAlignment.Right, ColumnAlignment.Right },
            competitors.Select(c => (IReadOnlyList<string?>)new[]
            {
                KorReportLinks.Org(c.CanonicalOrgId), null, null, null, null, null,
            }).ToList());

        b.H2("Top 5 rivals — drill-down");
        foreach (var c in competitors.Take(5))
        {
            b.H3(c.DisplayName, KorReportLinks.Org(c.CanonicalOrgId));
            b.B("Footprint: ", $"{c.Total} Intel refs (Signals: {c.Signals} | People: {c.People} | Actions: {c.Actions})");
            b.P("Projects confirmed as structural engineer:");
            if (c.TopProjects.Count > 0)
            {
                foreach (var p in c.TopProjects)
                {
                    b.P($"  - {p}");
                }
            }
            else
            {
                b.P("  (No structurally-linked projects in the org graph yet — see Org Dossier for narrative intel.)");
            }

            b.P(string.Empty);
        }

        b.H2("Strategic synthesis");

        b.H3("Where rivals are entrenched (DEAD signals)");
        b.P("Per Yurkovich-class error catch: projects where a competitor is named as incumbent are flagged DEAD in KOR's honed briefs. Examples:");
        b.P("  - Richmond Hospital Yurkovich Pavilion = Entuitive incumbent");
        b.P("  - Cowichan Hospital = Bush, Bohlman & Partners (Alliance)");
        b.P("  - Cariboo Memorial Hospital + several others = Stantec (captive in-house)");
        b.P("  - CFB Cold Lake FFCP = Stantec under EllisDon design-build");

        b.H3("BC concrete tower competitive zone");
        b.P("Glotman Simpson + Fast + Epp + RJC + AME + ASPECT + Tarpley dominate BC high-rise concrete + mid-rise residential market. KOR competes directly here. Mass timber + concrete hybrid (TELUS Ocean model) is KOR's emerging differentiator — Fast + Epp + ASPECT + Equilibrium + StructureCraft are the mass-timber specialists.");

        b.H3("Healthcare market = Stantec + DIALOG captive");
        b.P("BC + AB healthcare is heavily Stantec (captive in-house structural) + DIALOG (captive in-house structural). KOR flip-opportunities are limited to Alliance-mode projects where structural is procured separately, or D-B teams where Stantec/DIALOG aren't the prime architect.");

        b.H3("Long-span specialty — Fast + Epp dominant");
        b.P("Fast + Epp is the dominant mass-timber long-span specialist in BC (Brock Commons, Earth Sciences, multiple aquatic + arena projects). StructureCraft also significant for engineered timber. KOR competes in this zone via mass-timber capability buildout.");

        b.H3("US West Coast pattern recognition");
        b.P("The US rivals in the ranking reflect KOR's LA + SD market presence. Top rivals (JMS Engineering, R+A Engineering, WHM Structural, Skyline Engineering, Degenkolb, AHBL, Miyamoto, Holmes Structures) indicate KOR's competitive set in California + emerging WA/OR. Degenkolb + Miyamoto are seismic specialists (relevant for KOR's seismic schools + California portfolio).");

        b.H2("Recommended next actions");
        b.P("1. Audit DEAD-marked projects for competitor names — these are the lock signals to track.");
        b.P("2. Monitor Glotman Simpson + Fast + Epp + RJC pipelines — biggest BC direct rivals. Any pipeline news = competitive intelligence.");
        b.P("3. Differentiation strategy — Mass timber + concrete hybrid + long-span specialty = KOR's positioning vs Fast + Epp / ASPECT / Equilibrium.");
        b.P("4. Healthcare entry strategy — given Stantec + DIALOG captive lock, focus on Alliance/non-Stantec D-B teams + project-specific Alliance reformations.");
        b.P("5. US West Coast — track Degenkolb (seismic specialty), Miyamoto (international + seismic), AHBL (multi-disciplinary). Identify their entrenched accounts vs flip targets.");

        b.Italic("Drill-down per competitor via Org Dossier in the BD workspace.");

        return b.Build();
    }
}
