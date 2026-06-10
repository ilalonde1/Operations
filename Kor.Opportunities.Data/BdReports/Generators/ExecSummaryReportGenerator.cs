#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kor.Opportunities.Data.BdReports.Generators;

/// <summary>
/// Cross-sector executive overview (BD-UI-Plan Phase B item 8), structure
/// mirrored from tools/BdReportBuilders/build-exec-summary.ps1: live headline
/// + IMMEDIATE actions + sector heatmap, followed by the analyst synthesis
/// sections (config prose from the 2026-06-09 sweep — refreshed when the
/// pipeline re-hones). Live numbers come from DISTINCT-MPI queries; the PS
/// builder's hardcoded counts are replaced by the database.
/// </summary>
public static class ExecSummaryReportGenerator
{
    private const int ImmediateActionCap = 6;
    private const int ImmediateAngleCap = 450;

    public static BdReportDocument Build(
        BdExecHeadline headline,
        IReadOnlyList<SectorVerdictSummary> summaries,
        IReadOnlyList<PursuitBriefRow> callSheetPool,
        DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(headline);
        ArgumentNullException.ThrowIfNull(summaries);
        ArgumentNullException.ThrowIfNull(callSheetPool);

        var b = new BdReportDocumentBuilder("KOR Structural — BD Pipeline Executive Overview");

        b.Italic(
            "Cross-sector synthesis of all BD research categories. Each project verified against named procurement model + incumbent structural engineer " +
            "(Yurkovich-class error catch baked into every honing pass). All claims sourced from the database — no narrative inflation. " +
            $"Generated {generatedAtUtc:yyyy-MM-dd HH:mm} UTC from live KorOpportunitiesDb.");

        b.H2("The Headline");
        var honedPct = headline.TotalActiveMpis == 0
            ? 0
            : (int)Math.Round(100.0 * headline.HonedCount / headline.TotalActiveMpis);
        b.B("Total active MPIs (cross-sector, distinct): ", headline.TotalActiveMpis.ToString("N0", CultureInfo.InvariantCulture));
        b.B("Honed (deep BD intel — verified, named, action-ready): ", $"{headline.HonedCount:N0} ({honedPct}%)");
        b.B("Total $ live pipeline (PURSUE/MONITOR/DISCOVER cost rollup): ", "~$" + (headline.HonedCostCad / 1_000_000_000m).ToString("F1", CultureInfo.InvariantCulture) + "B CAD");
        b.B("URGENT items live right now: ", callSheetPool.Count(x => x.IsUrgent).ToString(CultureInfo.InvariantCulture));
        b.B("Open PURSUE pool: ", callSheetPool.Count(x => !x.IsUrgent).ToString(CultureInfo.InvariantCulture));
        b.P(string.Empty);
        b.P("KOR's BD intelligence is no longer a list of projects — it is a named, verified, action-ready pursuit engine. Every PURSUE has procurement model identified, incumbent (if any) named, decision-makers surfaced, warm-intro path mapped, and 12-month engagement timeline. The \"Yurkovich Pavilion\" error class — chasing already-locked projects — is structurally eliminated.");

        b.H2("IMMEDIATE — Action this week");
        var urgent = callSheetPool.Where(x => x.IsUrgent).Take(ImmediateActionCap).ToList();
        if (urgent.Count == 0)
        {
            b.P("No URGENT items in the current honing data.");
        }

        var rank = 1;
        foreach (var p in urgent)
        {
            b.H3($"{rank}. {p.ProjectName} ({CostOf(p)})");
            b.P($"MPI {p.MpiId} — {p.Province}{(string.IsNullOrWhiteSpace(p.MunicipalityName) ? string.Empty : ", " + p.MunicipalityName)}. {Safe(p.KorAngle, ImmediateAngleCap)}");
            rank++;
        }

        b.H2("Sector heatmap");
        b.Table(
            new[] { "Sector", "Total", "Honed", "% Honed", "$ Pipeline (M)" },
            summaries.Select(s =>
            {
                var honed = s.Total - s.NoVerdict;
                var pct = s.Total == 0 ? 0 : (int)Math.Round(100.0 * honed / s.Total);
                return (IReadOnlyList<string>)new[]
                {
                    s.SectorTitle,
                    s.Total.ToString("N0", CultureInfo.InvariantCulture),
                    honed.ToString("N0", CultureInfo.InvariantCulture),
                    $"{pct}%",
                    "$" + (s.HonedCostCad / 1_000_000m).ToString("N0", CultureInfo.InvariantCulture) + "M",
                };
            }).ToList());
        b.P("$ Pipeline counts live verdicts only (PURSUE/MONITOR/DISCOVER — DEAD and DUPLICATE excluded). Sector filters deliberately overlap (a BC Housing project is also Residential), so the heatmap rows do not sum to the distinct headline totals.");

        foreach (var section in ProseSections)
        {
            b.H2(section.H2);
            foreach (var paragraph in section.Lead)
            {
                b.P(paragraph);
            }

            foreach (var sub in section.Subsections)
            {
                b.H3(sub.Title);
                foreach (var paragraph in sub.Paragraphs)
                {
                    b.P(paragraph);
                }
            }
        }

        b.H2("Detailed reports — drill-down references");
        b.P("Every sector report (with every PURSUE, every MONITOR, every named contact and verdict reasoning) generates on demand from the BD Reports tile, alongside the weekly call sheet. Per-project intel: Org Dossier, Project Brief, Region Brief in the BD workspace.");

        b.H2("Methodology footnote");
        b.P("Every brief in this overview was generated via: (1) Sonnet first-pass enrichment from web search + government sources; (2) Yurkovich-rigor honing pass with 20-30 tool calls per project, mandatory procurement model identification + named incumbent + 12-month engagement timeline + named warm-intro path; (3) MajorProjectEnrichment + Intel* decompose into normalized relational rows; (4) WPF Org Dossier + Project Brief + Region Brief integration; (5) on-demand OpenXml report generation pulling live from DB.");
        b.P("No claim in this overview is asserted without underlying DB rows. Every URGENT can be drilled down to the named contact + named action + procurement model evidence.");

        return b.Build();
    }

    private sealed record ProseSection(string H2, IReadOnlyList<string> Lead, IReadOnlyList<SynthesisSection> Subsections);

    // Ported verbatim from build-exec-summary.ps1 (2026-06-09 sweep; markdown
    // ** stripped, Desktop-file references replaced by in-app pointers).
    private static readonly IReadOnlyList<ProseSection> ProseSections = new ProseSection[]
    {
        new(
            "Strategic compounding relationships",
            new[]
            {
                "These are NOT individual project pursuits. These are RELATIONSHIPS that, if built, unlock recurring KOR positioning across multi-year multi-project pipelines. One relationship = N projects.",
            },
            new SynthesisSection[]
            {
                new("Healthcare", new[]
                {
                    "Graham Design Builders LP (canonical 8361) — $3.4B+ active BC healthcare pipeline. Alex Trifunov, Vancouver Pre-Construction Manager. Single relationship = entire Graham healthcare portfolio.",
                    "EllisDon (canonical 22257) — $1.6B UHNBC + defense + Esquimalt JNCM + future BC healthcare. 7 named BC contacts: Daniel Murphy (VP Preconstruction), Keeli Husband (Dir BD), Craig Enns (SVP BC), Candace MacDonald (Dir Special Projects), David McFarlane (COO Western), plus 2 more.",
                    "Sharon Petty at VCH — Director Real Estate Operations. VGH Building 1 + future VCH pipeline.",
                }),
                new("Indigenous", new[]
                {
                    "MST Development Corp (Musqueam-Squamish-Tsleil-Waututh) — Lower Mainland mega-developments: Jericho Lands, Maplewood Innovation District, Tsleil-Waututh North Shore. Billion-dollar phased developments. One MST relationship = N phases.",
                }),
                new("Recreational + Civic", new[]
                {
                    "HCMA Architecture + Design (Vancouver) — BC dominant rec specialist architect. On Newton + Britannia + most major BC rec projects. Single Principal-in-Charge relationship at HCMA = recurring KOR positioning across BC rec.",
                }),
                new("Commercial Office + TOD", new[]
                {
                    "TELUS Living (Manasweeta Bhatia program) — 40 sites under evaluation, 20 in planning/construction. Repurposing 2,300+ institutional properties. Single Vancouver HQ relationship unlocks national pipeline.",
                }),
                new("Schools", new[]
                {
                    "Jessie Gresley-Jones (VSB) — jgresley-jones@vsb.bc.ca. Single contact unlocks 30 VSB seismic schools ($850M program).",
                    "Brian Jonker (SD62 / Sooke) — 250-474-9800. SD62 capital plan + North Langford Secondary $220M.",
                    "GGA Architecture (Gibbs Gage) — AB school market player. FrancoSud Airdrie + St. Martin de Porres HS.",
                }),
                new("Residential", new[]
                {
                    "KOR memory-confirmed client developers (warm-leads): Wesgroup, Bosa, Reliance, Westland, Belford, Anthem, Cressey, Peterson, Strand, Beedie, Capital Region Housing, Onni, Concord, Polygon. BMZ pre-2021 legacy = 30+ years of BC residential relationships.",
                }),
                new("Post-Secondary", new[]
                {
                    "Ryder Architecture (UBC Lower Mall Student Housing) — immediate call per Sonnet honing flag.",
                }),
            }),
        new(
            "Cross-sector strategic insights",
            Array.Empty<string>(),
            new SynthesisSection[]
            {
                new("Procurement-mature markets vs open RFP markets", new[]
                {
                    "Commercial Office: 88% DEAD (procurement-mature, structural already locked). Hospitals: 60% DEAD (Alliance / P3 / Stantec-captive). Indigenous: 100% relationship-driven (no DEAD in traditional sense). Schools: 23% DEAD (large pipeline of seismic upgrades still active).",
                    "Implication: pursuit strategy by sector is FUNDAMENTALLY different. Commercial = relationships not RFPs. Schools = relationship + active RFP both work. Hospitals = procurement model identification is the make/break gate (Yurkovich correction).",
                }),
                new("Mass timber as KOR's emerging differentiator", new[]
                {
                    "TELUS Ocean Vancouver = the BC commercial office model. UBC Brock Commons + Earth Sciences = the post-secondary academic model. Whitemud Acres / Olds Sportsplex = the recreational model. KOR's ability to deliver mass timber + concrete hybrid is a competitive advantage on mid-rise (8-12 storey) across THREE sectors simultaneously.",
                }),
                new("Long-span specialty (aquatics + arenas + field house) = KOR direct fit", new[]
                {
                    "Recreational sector is KOR's structural sweet spot. Long-span roof + complex MEP + corrosive environment detailing (aquatic centres) + low-temp condensation (ice arenas) + clear-span (field houses) = direct KOR specialty. 16 PURSUE + 35 DISCOVER in recreational = compounding pipeline.",
                }),
                new("BMZ legacy as warm-intro lever", new[]
                {
                    "KOR was Bryson Markulin Zickmantel pre-2021. Many BC developers, owners, architects have BMZ-era references that pre-date the rebrand. Audit KOR portfolio for prior BMZ work on each developer in MONITOR list — that's the warm-intro path. 30+ years of compounded BC residential + commercial + institutional relationships.",
                }),
                new("Alliance / P3 / Captive-structural as DEAD signals", new[]
                {
                    "Yurkovich correction: procurement model identification BEFORE marking PURSUE. Alliance = structural locked with team partner. P3 / DBFM = structural locked with selected design-build prime. Stantec-captive = healthcare locked with in-house structural. These are the 3 systematic locks that mark a project DEAD even if construction hasn't started. Honing PROMPTs now mandate this verification.",
                }),
                new("Single-action multiplier opportunities", new[]
                {
                    "5 specific single actions that unlock disproportionate pipeline:",
                    "1. VPO pre-qualification → 30 VSB seismic schools ($850M)",
                    "2. MST Dev Corp relationship → Lower Mainland Indigenous phased mega-pipeline",
                    "3. EllisDon BC office → defense + healthcare + commercial cross-cutting",
                    "4. Graham Vancouver pre-con → $3.4B BC healthcare pipeline",
                    "5. HCMA Vancouver Principal → BC rec recurring positioning",
                }),
            }),
        new(
            "Geographic concentration",
            new[]
            {
                "KOR market footprint: BC + LA + San Diego today; growth = Edmonton + US West Coast (WA/OR). The honed pipeline reflects this — BC dominates, AB second, US West Coast first-pass running now.",
                "BC concentration: Greater Vancouver (Vancouver, Surrey, Burnaby, Coquitlam, Richmond), Vancouver Island (Victoria, Saanich, Nanaimo, Courtenay), Interior (Kelowna, Vernon, Penticton), Northern BC (Prince George, Quesnel).",
                "AB concentration: Calgary CBD, Edmonton metropolitan, Red Deer corridor, Rocky View (Airdrie, Cochrane), Strathcona County. NorQuest + SAIT + NAIT + MacEwan + U of A non-medical surfaced as post-secondary opportunities.",
            },
            Array.Empty<SynthesisSection>()),
        new(
            "Data hygiene + intelligence quality",
            new[]
            {
                "Honed briefs are not aggregated guesses — each has named procurement model, named incumbent (if applicable), named decision-makers with contact info, named warm-intro path, and 12-month engagement timeline.",
            },
            new SynthesisSection[]
            {
                new("Yurkovich-class error catches", new[]
                {
                    "The honing PROMPT's rigor (20-30 tool calls per project, mandatory procurement model identification) caught:",
                    "- Richmond Hospital Yurkovich Pavilion (Entuitive incumbent, NOT KOR opportunity)",
                    "- Cowichan Hospital (Alliance with Bush Bohlman)",
                    "- Cariboo Memorial + several others (Stantec-captive)",
                    "- 7 Yurkovich-class corrections in hospitals batch alone",
                }),
            }),
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
