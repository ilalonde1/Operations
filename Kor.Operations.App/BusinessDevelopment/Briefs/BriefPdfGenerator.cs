#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Kor.Opportunities.Data.Briefs;
using Kor.Opportunities.Data.Intel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Kor.Operations.App.BusinessDevelopment.Briefs;

/// <summary>
/// QuestPDF renderer for the three Pursuit Brief shapes. Brand-matched to
/// KOR's slate primary (<c>#3F5364</c> — same as
/// <c>Themes/KorTheme.xaml::Brand.Primary.Brush</c>): Letter, Mulish, slate
/// header band, section strip + body. Stateless + thread-safe (register as
/// Singleton).
/// </summary>
public sealed class BriefPdfGenerator : IBriefPdfGenerator
{
    // Round 49: brand-matched to KorTheme.xaml. The old "#1E3A8A" was generic
    // Tailwind navy and looked like a stock template — see ss1 from 2026-05-30.
    private const string Brand = "#3F5364";          // KOR slate primary (header band fill + section heading)
    private const string BrandAccent = "#FF5B35";    // KOR orange accent
    private const string BrandEyebrow = "#C4CCD3";   // light slate tint for the eyebrow label on the band
    private const string BrandSubtle = "#D9DEE3";    // lighter slate for the italic subtitle line
    private const string BrandFactLabel = "#9FA9B3"; // muted slate for the small fact labels inside the band
    private const string Text = "#111827";
    private const string Muted = "#6B7280";
    private const string Border = "#E5E7EB";
    private const string Pale = "#F8FAFC";
    private const string Footer = "KOR Structural — Confidential / Internal";

    public void WriteOpportunityBrief(OpportunityBriefData data, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(data);
        EnsureDirectory(outputPath);
        Document.Create(c => ComposeOpportunity(c, data)).GeneratePdf(outputPath);
    }

    public void WriteProjectBrief(ProjectBriefData data, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(data);
        EnsureDirectory(outputPath);
        Document.Create(c => ComposeProject(c, data)).GeneratePdf(outputPath);
    }

    public void WriteRegionBrief(RegionBriefData data, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(data);
        EnsureDirectory(outputPath);
        Document.Create(c => ComposeRegion(c, data)).GeneratePdf(outputPath);
    }

    public void WriteOrgBrief(OrgBriefData data, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(data);
        EnsureDirectory(outputPath);
        Document.Create(c => ComposeOrg(c, data)).GeneratePdf(outputPath);
    }

    // === Opportunity brief ===

    private static void ComposeOpportunity(IDocumentContainer container, OpportunityBriefData d)
    {
        container.Page(page =>
        {
            ConfigurePage(page);
            page.Content().Column(column =>
            {
                column.Spacing(12);
                column.Item().Element(c => OppHeaderBand(c, d));
                column.Item().Element(c => Section(c, "Why this is our warmest target right now",
                    OppWarmthBullets(d)));
                column.Item().Element(c => Section(c, "KOR's angle (relationship intelligence)",
                    OppAngleBullets(d)));
                column.Item().Element(c => Section(c, "Get in front of them this week",
                    OppEventBullets(d)));
                column.Item().Element(c => Section(c, "Recommended next steps",
                    OppNextStepBullets(d)));
                if (d.Intel?.BuyerIntel is not null)
                {
                    column.Item().Element(c => Section(c, $"About the buyer ({d.BuyerName ?? "buyer"})",
                        OppBuyerIntelBullets(d.Intel.BuyerIntel)));
                }
                if (d.Intel?.ArchitectIntel is not null && !string.IsNullOrWhiteSpace(d.LikelyArchitectName))
                {
                    column.Item().Element(c => Section(c, $"About the likely architect ({d.LikelyArchitectName})",
                        OppArchitectIntelBullets(d.Intel.ArchitectIntel)));
                }
            });
            page.Footer().Element(PageFooter);
        });
    }

    private static void OppHeaderBand(IContainer container, OpportunityBriefData d)
    {
        // Round 49: tighter padding + tighter spacing + smaller title so the
        // header band stops eating ~25-30% of the page (see ss1).
        container.Background(Brand).Padding(10).Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("PURSUIT BRIEF")
                .FontSize(8).LetterSpacing(0.2f).FontColor(BrandEyebrow);
            column.Item().Text(Nz(d.Name)).FontSize(15).Bold().FontColor(Colors.White);
            column.Item().Text("Warmest live target — recommended next move")
                .FontSize(9).Italic().FontColor(BrandSubtle);

            column.Item().PaddingTop(3).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });
                HeaderFact(table, "Owner", Nz(d.BuyerName));
                HeaderFact(table, "Location",
                    string.IsNullOrWhiteSpace(d.ProjectCity)
                        ? Nz(d.ProjectProvince)
                        : $"{d.ProjectCity}, {Nz(d.ProjectProvince)}");
                HeaderFact(table, "Sector", Nz(d.PrimeProjectSector));
                HeaderFact(table, "Confidence",
                    d.PrimeConfidence.ToString("F2", CultureInfo.InvariantCulture));
                HeaderFact(table, "Submission deadline", FormatDeadline(d.SubmissionDeadlineUtc));
                HeaderFact(table, "Estimated value", FormatValue(d.EstimatedValue));
            });
        });
    }

    private static IEnumerable<string> OppWarmthBullets(OpportunityBriefData d)
    {
        yield return $"Prime-consultant building RFP — the architect's team is being assembled now, before the structural seat is set. Classifier confidence {d.PrimeConfidence.ToString("F2", CultureInfo.InvariantCulture)}.";
        yield return "Sector AND geography both match KOR's wheelhouse — exactly the type of work we win in this market.";
        yield return "Live in active procurement — actionable window for an architect intro.";
    }

    private static IEnumerable<string> OppAngleBullets(OpportunityBriefData d)
    {
        yield return d.OwnerKorProjectsCount > 0
            ? $"Owner relationship: KOR has {d.OwnerKorProjectsCount} prior project(s) with {d.BuyerName} — warm-call territory; reference the relationship directly."
            : $"Owner relationship: no prior KOR project with {d.BuyerName} on file — this is a new-owner pursuit; lean on the architect path.";

        if (d.OwnerPipelineProjectCount > 0)
        {
            yield return $"We track {d.OwnerPipelineProjectCount} project(s) for this owner in our pipeline data — we know their procurement cadence.";
        }

        if (!string.IsNullOrWhiteSpace(d.LikelyArchitectName))
        {
            yield return d.KorArchitectJointProjectCount > 0
                ? $"Likely prime consultant: {d.LikelyArchitectName} — has done {d.LikelyArchitectOwnerProjectCount} project(s) for this owner, and KOR has teamed with them on {d.KorArchitectJointProjectCount} past project(s). The warmest path in."
                : $"Likely prime consultant: {d.LikelyArchitectName} — has done {d.LikelyArchitectOwnerProjectCount} project(s) for this owner. KOR has not yet formally teamed with them; introduce the structural offering this week.";
        }
        else
        {
            yield return "Likely prime consultant: not yet identified in our data — research the architect on this owner's recent builds before the deadline.";
        }
    }

    private static IEnumerable<string> OppEventBullets(OpportunityBriefData d)
    {
        if (d.MatchedEvent is { } ev)
        {
            var date = ev.StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "(date TBC)";
            yield return $"{ev.Name} — {date}, {Nz(ev.City)} ({Nz(ev.Market)}).";
            if (!string.IsNullOrWhiteSpace(ev.SectorsThemes))
            {
                yield return $"Why this event: themes ({ev.SectorsThemes}) line up with the pursuit's sector; audience: {Nz(ev.Audience)}.";
            }
            if (!string.IsNullOrWhiteSpace(ev.TargetsPresent))
            {
                yield return $"Targets expected in the room: {ev.TargetsPresent}.";
            }
            if (!string.IsNullOrWhiteSpace(ev.RegistrationUrl))
            {
                yield return $"Register / details: {ev.RegistrationUrl}";
            }
        }
        else
        {
            yield return "No directly-matching upcoming event found — go direct architect outreach instead.";
        }
    }

    private static IEnumerable<string> OppNextStepBullets(OpportunityBriefData d)
    {
        if (!string.IsNullOrWhiteSpace(d.LikelyArchitectName))
        {
            yield return $"Reach out to {d.LikelyArchitectName} this week with KOR's relevant capability brief; propose teaming on the live RFP.";
        }
        yield return "If attending the event, brief the team beforehand and target specific introductions in the room.";
        yield return "Confirm owner's procurement timeline + submission requirements; align KOR resourcing for the deadline.";
    }

    private static IEnumerable<string> OppBuyerIntelBullets(OrgIntelBundle intel)
    {
        if (intel.Actions.Count + intel.People.Count + intel.Signals.Count == 0)
        {
            yield return "No intel on file for this buyer.";
            yield break;
        }

        foreach (var a in intel.Actions)
        {
            yield return FormatIntelBullet(FormatActionBody(a), a.Confidence, a.Freshness, a.RefreshedAtUtc);
        }

        foreach (var p in intel.People)
        {
            yield return FormatIntelBullet(FormatPersonBody(p), p.Confidence, p.Freshness, p.RefreshedAtUtc);
        }

        foreach (var s in intel.Signals)
        {
            yield return FormatIntelBullet(FormatSignalBody(s), s.Confidence, s.Freshness, s.RefreshedAtUtc);
        }
    }

    private static IEnumerable<string> OppArchitectIntelBullets(OrgIntelBundle intel)
    {
        if (intel.Actions.Count + intel.Risks.Count + intel.Signals.Count == 0)
        {
            yield return "No intel on file for this architect yet  likely a cold lead.";
            yield break;
        }

        foreach (var a in intel.Actions)
        {
            yield return FormatIntelBullet(FormatActionBody(a), a.Confidence, a.Freshness, a.RefreshedAtUtc);
        }

        foreach (var r in intel.Risks)
        {
            yield return FormatIntelBullet(FormatRiskBody(r), r.Confidence, r.Freshness, r.RefreshedAtUtc);
        }

        foreach (var s in intel.Signals)
        {
            yield return FormatIntelBullet(FormatSignalBody(s), s.Confidence, s.Freshness, s.RefreshedAtUtc);
        }
    }

    // === Project brief ===

    private static void ComposeProject(IDocumentContainer container, ProjectBriefData d)
    {
        container.Page(page =>
        {
            ConfigurePage(page);
            page.Content().Column(column =>
            {
                column.Spacing(12);
                column.Item().Element(c => ProjectHeaderBand(c, d));
                column.Item().Element(c => ParagraphSection(c, "Project description",
                    string.IsNullOrWhiteSpace(d.ProjectDescription)
                        ? "No project description on file yet."
                        : d.ProjectDescription!));
                column.Item().Element(c => Section(c, "Schedule",
                    ProjectScheduleBullets(d)));
                column.Item().Element(c => Section(c, "Team & KOR angle",
                    ProjectTeamBullets(d)));
                column.Item().Element(c => ProjectSourceSection(c, d));
            });
            page.Footer().Element(PageFooter);
        });
    }

    private static void ProjectHeaderBand(IContainer container, ProjectBriefData d)
    {
        container.Background(Brand).Padding(10).Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("PROJECT BRIEF")
                .FontSize(8).LetterSpacing(0.2f).FontColor(BrandEyebrow);
            column.Item().Text(Nz(d.ProjectName)).FontSize(15).Bold().FontColor(Colors.White);
            column.Item().Text("Pursuit prep for one forward-pipeline project")
                .FontSize(9).Italic().FontColor(BrandSubtle);

            column.Item().PaddingTop(3).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });
                HeaderFact(table, "Stage", Nz(d.Stage));
                HeaderFact(table, "Location", ProjectLocation(d));
                HeaderFact(table, "Sector", ProjectSector(d));
                HeaderFact(table, "Estimated value", FormatProjectValue(d));
            });
        });
    }

    private static IEnumerable<string> ProjectScheduleBullets(ProjectBriefData d)
    {
        yield return $"{d.StartYear?.ToString(CultureInfo.InvariantCulture) ?? "?"} - {d.CompletionYear?.ToString(CultureInfo.InvariantCulture) ?? "?"}";
        if (!string.IsNullOrWhiteSpace(d.ScheduleNotes))
        {
            yield return d.ScheduleNotes!;
        }
    }

    private static IEnumerable<string> ProjectTeamBullets(ProjectBriefData d)
    {
        foreach (var line in ProjectLinkedOrgBullets("Proponent", d.ProponentName, d.ProponentSummary, structuralCallout: false))
        {
            yield return line;
        }
        foreach (var line in ProjectLinkedOrgBullets("Architect", d.ArchitectName, d.ArchitectSummary, structuralCallout: false))
        {
            yield return line;
        }
        foreach (var line in ProjectLinkedOrgBullets("Structural Engineer", d.StructuralEngineerName, d.StructuralSummary, structuralCallout: true))
        {
            yield return line;
        }
        foreach (var line in ProjectLinkedOrgBullets("GC", d.GeneralContractorName, d.GeneralContractorSummary, structuralCallout: false))
        {
            yield return line;
        }
    }

    private static IEnumerable<string> ProjectLinkedOrgBullets(
        string roleLabel,
        string? sourceName,
        LinkedOrgSummary? summary,
        bool structuralCallout)
    {
        if (summary is null)
        {
            yield return $"{roleLabel}: {Nz(sourceName)} — Not on file.";
            if (structuralCallout && !string.IsNullOrWhiteSpace(sourceName))
            {
                yield return $"KOR's competitor on this pursuit. Consider angle: {sourceName} vs KOR.";
            }
            yield break;
        }

        var refreshed = summary.LastRefreshAtUtc.HasValue
            ? summary.LastRefreshAtUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "not refreshed";
        yield return $"{roleLabel}: {summary.DisplayName} — {summary.IntelPersonCount} people, {summary.OpenActionCount} open actions, {summary.RecentSignalCount} recent signals; last refreshed {refreshed}.";
        if (structuralCallout)
        {
            yield return $"KOR's competitor on this pursuit. Consider angle: {summary.DisplayName} vs KOR.";
        }
    }

    // === Region brief ===

    private static void ComposeRegion(IDocumentContainer container, RegionBriefData d)
    {
        container.Page(page =>
        {
            ConfigurePage(page);
            page.Content().Column(column =>
            {
                column.Spacing(12);
                column.Item().Element(c => RegionHeaderBand(c, d));

                column.Item().Element(c => Section(c, "Top architects in this market",
                    FormatTopOrgs(d.TopArchitects, "in this market", korNote: true,
                        empty: "No architects tied to projects in this market in our data yet.")));

                column.Item().Element(c => Section(c, "Top owners / clients in this market",
                    FormatTopOrgs(d.TopOwners, "in this market", korNote: true, ownerNote: true,
                        empty: "No owners tied to projects in this market in our data yet.")));

                column.Item().Element(c => Section(c, "Top competitors in this market (structural)",
                    FormatTopOrgs(d.TopCompetitors, "as structural EOR here", korNote: false,
                        empty: "No competitors flagged on projects in this market in our data yet.")));

                column.Item().Element(c => Section(c, "Live prime RFPs (top 5)",
                    RegionLiveRfpsLines(d)));

                column.Item().Element(c => Section(c, "Forward pipeline (planned / funded)",
                    RegionForwardLines(d)));

                column.Item().Element(c => Section(c, "Upcoming events in this market",
                    RegionEventLines(d)));

                if (d.Intel is not null)
                {
                    column.Item().Element(c => Section(c, "Cross-org actionables in this region",
                        RegionIntelActionBullets(d.Intel)));
                    if (d.Intel.RecentLeadershipChanges.Count > 0)
                    {
                        column.Item().Element(c => Section(c, "Recent leadership changes in this region (last 90 days)",
                            RegionLeadershipChangeBullets(d.Intel)));
                    }
                    if (d.Intel.TopCapacityRisks.Count > 0)
                    {
                        column.Item().Element(c => Section(c, "Capacity-strain signals (competitor displacement opportunities)",
                            RegionCapacityRiskBullets(d.Intel)));
                    }
                }
            });
            page.Footer().Element(PageFooter);
        });
    }

    private static void RegionHeaderBand(IContainer container, RegionBriefData d)
    {
        var scope = string.IsNullOrWhiteSpace(d.City) ? d.Province : $"{d.Province} — {d.City}";
        container.Background(Brand).Padding(10).Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("REGION BRIEF")
                .FontSize(8).LetterSpacing(0.2f).FontColor(BrandEyebrow);
            column.Item().Text(scope).FontSize(16).Bold().FontColor(Colors.White);

            column.Item().PaddingTop(3).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });
                HeaderFact(table, "Live prime RFPs", d.LivePrimeRfpCount.ToString(CultureInfo.InvariantCulture));
                HeaderFact(table, "Forward pipeline", d.ForwardPipelineCount.ToString(CultureInfo.InvariantCulture));
                HeaderFact(table, "Active major projects", d.ActiveMpiCount.ToString(CultureInfo.InvariantCulture));
            });
        });
    }

    private static IEnumerable<string> FormatTopOrgs(IReadOnlyList<RegionTopOrg> orgs, string countSuffix,
        bool korNote, string empty, bool ownerNote = false)
    {
        if (orgs.Count == 0)
        {
            yield return empty;
            yield break;
        }
        foreach (var o in orgs)
        {
            var korPart = korNote
                ? (ownerNote
                    ? (o.KorJointCount > 0 ? $"; {o.KorJointCount} KOR project(s) on record" : "; no prior KOR project on file")
                    : (o.KorJointCount > 0 ? $"; KOR has teamed with them {o.KorJointCount}x" : "; no KOR joint history yet"))
                : string.Empty;
            yield return $"{RegionOrgDisplayName(o)} — {o.ProjectCount} project(s) {countSuffix}{korPart}.";
        }
    }

    private static IEnumerable<string> RegionLiveRfpsLines(RegionBriefData d)
    {
        if (d.LiveRfps.Count == 0)
        {
            yield return "No live prime-consultant RFPs in this market right now.";
            yield break;
        }
        foreach (var r in d.LiveRfps)
        {
            yield return $"{r.Name} — {r.BuyerName} ({Nz(r.PrimeProjectSector)}) — deadline {FormatDeadline(r.SubmissionDeadlineUtc)}.";
        }
    }

    private static IEnumerable<string> RegionForwardLines(RegionBriefData d)
    {
        if (d.ForwardProjects.Count == 0)
        {
            yield return "No forward-pipeline projects in this market in our data yet.";
            yield break;
        }
        foreach (var p in d.ForwardProjects)
        {
            yield return $"{p.ProjectName} — {Nz(p.ProponentName)} ({Nz(p.Stage)}) — {FormatValue(p.EstimatedCostCad)}.";
        }
    }

    private static IEnumerable<string> RegionEventLines(RegionBriefData d)
    {
        if (d.UpcomingEvents.Count == 0)
        {
            yield return "No matching upcoming events in our database.";
            yield break;
        }
        foreach (var e in d.UpcomingEvents)
        {
            var date = e.StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "(date TBC)";
            yield return $"{e.Name} — {date} — {Nz(e.City)}.";
        }
    }

    private static IEnumerable<string> RegionIntelActionBullets(RegionIntelRollup intel)
    {
        if (intel.TopActions.Count == 0)
        {
            yield return "No cross-org actionables on file yet.";
            yield break;
        }

        foreach (var a in intel.TopActions)
        {
            var body = HumanizeActionType(a.ActionType) + ": " + a.Recommendation;
            if (!string.IsNullOrWhiteSpace(a.TargetPersonName))
            {
                body += " (target: " + a.TargetPersonName + ")";
            }
            if (!string.IsNullOrWhiteSpace(a.TimingNotes))
            {
                body += "  Timing: " + a.TimingNotes;
            }

            yield return FormatIntelBullet(body, a.Confidence, a.Freshness, a.RefreshedAtUtc);
        }
    }

    private static IEnumerable<string> RegionLeadershipChangeBullets(RegionIntelRollup intel)
    {
        foreach (var s in intel.RecentLeadershipChanges)
        {
            var body = s.Subject;
            if (!string.IsNullOrWhiteSpace(s.Detail))
            {
                body += "  " + s.Detail;
            }
            if (!string.IsNullOrWhiteSpace(s.OccurredAtApprox))
            {
                body += " [" + s.OccurredAtApprox + "]";
            }

            yield return FormatIntelBullet(body, s.Confidence, s.Freshness, s.RefreshedAtUtc);
        }
    }

    private static IEnumerable<string> RegionCapacityRiskBullets(RegionIntelRollup intel)
    {
        foreach (var r in intel.TopCapacityRisks)
        {
            var body = $"{r.OrgDisplayName}: {r.Description}";
            if (!string.IsNullOrWhiteSpace(r.MitigationNotes))
            {
                body += " (Mitigation: " + r.MitigationNotes + ")";
            }

            yield return FormatIntelBullet(body, r.Confidence, r.Freshness, r.RefreshedAtUtc);
        }
    }

    // === Org brief ===

    private static void ComposeOrg(IDocumentContainer container, OrgBriefData d)
    {
        container.Page(page =>
        {
            ConfigurePage(page);
            page.Content().Column(column =>
            {
                column.Spacing(12);
                var enrichment = ParseEnrichment(d.DataHoningEnrichmentJson);
                column.Item().Element(c => OrgHeaderBand(c, d, enrichment));
                if (!string.IsNullOrWhiteSpace(d.Intel?.SynopsisParagraph1)
                    || !string.IsNullOrWhiteSpace(d.Intel?.SynopsisParagraph2))
                {
                    column.Item().Element(c => OrgSynopsisBlock(c, d.Intel));
                }
                column.Item().Element(c => Section(c, "KOR's history with this organization",
                    OrgHistoryBullets(d)));
                column.Item().Element(c => Section(c, "Recommended actions",
                    OrgActionBullets(d.Intel)));
                column.Item().Element(c => Section(c, "Key people on file",
                    OrgIntelPeopleBullets(d.Intel)));
                column.Item().Element(c => Section(c, "Recent signals",
                    OrgSignalBullets(d.Intel)));
                column.Item().Element(c => Section(c, "Their recent work",
                    OrgRecentBullets(d)));
                if (d.Intel is { Works.Count: > 0 })
                {
                    column.Item().Element(c => Section(c, "Their portfolio (from research)",
                        OrgIntelWorkBullets(d.Intel)));
                }
                if (d.Deltek is not null)
                {
                    ComposeOrgDeltekSection(column, d.Deltek);
                }
                else if (!string.IsNullOrWhiteSpace(d.DeltekNote))
                {
                    column.Item().PaddingTop(6).Text(text =>
                    {
                        text.Span("KOR engagement history (Deltek): ").FontSize(10).Bold().FontColor(Brand);
                        text.Span(d.DeltekNote).FontSize(10).Italic().FontColor(Colors.Grey.Darken2);
                    });
                }
                if (d.Intel is { Risks.Count: > 0 })
                {
                    column.Item().Element(c => Section(c, "Risks / vulnerabilities",
                        OrgRiskBullets(d.Intel)));
                }
            });
            page.Footer().Element(PageFooter);
        });
    }

    private static void OrgSynopsisBlock(IContainer container, OrgIntelBundle? intel)
    {
        container.Column(column =>
        {
            column.Spacing(6);
            if (!string.IsNullOrWhiteSpace(intel?.SynopsisParagraph1))
            {
                column.Item().Text(intel.SynopsisParagraph1!).FontSize(11).Italic().FontColor(Brand);
            }
            if (!string.IsNullOrWhiteSpace(intel?.SynopsisParagraph2))
            {
                column.Item().Text(intel.SynopsisParagraph2!).FontSize(11).Italic().FontColor(Brand);
            }
        });
    }

    private static void OrgHeaderBand(IContainer container, OrgBriefData d, OrgEnrichment enrichment)
    {
        container.Background(Brand).Padding(10).Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("ORGANIZATION BRIEF")
                .FontSize(8).LetterSpacing(0.2f).FontColor(BrandEyebrow);
            column.Item().Text(d.DisplayName).FontSize(16).Bold().FontColor(Colors.White);
            column.Item().Text($"({d.Kind})")
                .FontSize(9).Italic().FontColor(BrandSubtle);

            column.Item().PaddingTop(3).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });
                HeaderFact(table, "Website", string.IsNullOrWhiteSpace(d.Website) ? "(not on file)" : d.Website!);
                HeaderFact(table, "KOR projects", d.KorProjectsCount.ToString(CultureInfo.InvariantCulture));
                HeaderFact(table, "Last KOR engagement",
                    d.LastKorProjectAtUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "(none)");
                if (!string.IsNullOrWhiteSpace(enrichment.HqCity))
                {
                    HeaderFact(table, "HQ", enrichment.HqCity!);
                }
                if (enrichment.Sectors.Count > 0)
                {
                    HeaderFact(table, "Sectors", string.Join(", ", enrichment.Sectors));
                }
            });
        });
    }

    private static void ComposeOrgDeltekSection(ColumnDescriptor column, OrgBriefDeltekSection dk)
    {
        column.Item().BorderBottom(1).BorderColor(BrandAccent).PaddingBottom(4)
            .Text("KOR engagement history (Deltek)").FontSize(11.5f).Bold().FontColor(Brand);

        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(110);
                columns.RelativeColumn();
            });

            DeltekFactRow(table, "Client", $"{dk.DeltekClientId}  {Nz(dk.ClientName)}");
            if (dk.ProjectCount > 0)
            {
                DeltekFactRow(table, "Lifetime billed",
                    $"{dk.LifetimeFee.ToString("C0", CultureInfo.CurrentCulture)} across {dk.ProjectCount:N0} project(s)");
            }
            if (dk.LatestProjectStart.HasValue)
            {
                var latest = string.IsNullOrWhiteSpace(dk.LatestProjectName) ? "latest engagement" : dk.LatestProjectName!;
                DeltekFactRow(table, "Latest",
                    $"{latest} (opened {dk.LatestProjectStart.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})");
            }
            DeltekFactRow(table, "Contacts", dk.ContactCount.ToString("N0", CultureInfo.InvariantCulture));
            if (dk.ArOutstanding > 0)
            {
                DeltekFactRow(table, "AR outstanding",
                    $"{dk.ArOutstanding.ToString("C0", CultureInfo.CurrentCulture)} (90+ days: {dk.Ar90Plus.ToString("C0", CultureInfo.CurrentCulture)})");
            }
        });

        if (dk.RecentProjects.Count > 0)
        {
            column.Item().PaddingTop(8).Text("Recent projects")
                .FontSize(8).LetterSpacing(0.1f).FontColor(Muted);
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.ConstantColumn(58);
                    columns.ConstantColumn(58);
                    columns.ConstantColumn(56);
                    columns.ConstantColumn(56);
                });

                DeltekProjectHeader(table, "Project");
                DeltekProjectHeader(table, "Opened");
                DeltekProjectHeader(table, "Status");
                DeltekProjectHeader(table, "Fee");
                DeltekProjectHeader(table, "Billed");

                foreach (var p in dk.RecentProjects)
                {
                    DeltekProjectCell(table, p.Name);
                    DeltekProjectCell(table, p.OpenDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "");
                    DeltekProjectCell(table, p.Status ?? "");
                    DeltekProjectCell(table, p.Fee > 0 ? p.Fee.ToString("C0", CultureInfo.CurrentCulture) : "");
                    DeltekProjectCell(table, p.FeeBilled > 0 ? p.FeeBilled.ToString("C0", CultureInfo.CurrentCulture) : "");
                }
            });
        }

        if (dk.DegradedSections)
        {
            column.Item().PaddingTop(6)
                .Text("Some Deltek sections were unavailable for this client.")
                .FontSize(9).Italic().FontColor(Muted);
        }
    }

    private static void DeltekFactRow(TableDescriptor table, string label, string value)
    {
        table.Cell().PaddingVertical(2).Text(label)
            .FontSize(8).LetterSpacing(0.1f).FontColor(Muted);
        table.Cell().PaddingVertical(2).Text(string.IsNullOrWhiteSpace(value) ? "—" : value)
            .FontSize(10).FontColor(Text);
    }

    private static void DeltekProjectHeader(TableDescriptor table, string text)
    {
        table.Cell().BorderBottom(1).BorderColor(BrandAccent).Padding(3)
            .Text(text).FontSize(8).Bold().FontColor(Brand);
    }

    private static void DeltekProjectCell(TableDescriptor table, string? text)
    {
        table.Cell().BorderBottom(1).BorderColor(Border).Padding(3)
            .Text(string.IsNullOrWhiteSpace(text) ? "—" : text).FontSize(8.5f).FontColor(Text);
    }

    private static IEnumerable<string> OrgHistoryBullets(OrgBriefData d)
    {
        if (d.KorProjectsCount > 0)
        {
            var lastNote = d.LastKorProjectAtUtc.HasValue
                ? $" (last engagement: {d.LastKorProjectAtUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})"
                : string.Empty;
            yield return $"Deltek-tracked KOR projects with this org: {d.KorProjectsCount}{lastNote} — warm-call territory.";
        }
        else
        {
            yield return "No Deltek-tracked KOR project with this org on file — this is a cold/new relationship; open with the capability brief.";
        }

        if (d.KorJointProjectCount > 0)
        {
            yield return $"Joint major-project record: KOR was the structural EOR on {d.KorJointProjectCount} project(s) where this org appears as architect / owner / GC.";
            foreach (var p in d.KorJointProjects)
            {
                yield return $"    {p.ProjectName} ({p.CompletionYear?.ToString(CultureInfo.InvariantCulture) ?? "year n/a"}, {Nz(p.Sector)}, {Nz(p.Province)})";
            }
        }
        else
        {
            yield return "No joint major-project record where KOR was the structural EOR with this org — there is an opening to be on their next team.";
        }
    }

    private static IEnumerable<string> OrgRecentBullets(OrgBriefData d)
    {
        if (d.RecentProjects.Count == 0)
        {
            yield return "No recent major projects in our pipeline data for this org.";
            yield break;
        }
        foreach (var p in d.RecentProjects)
        {
            yield return $"{p.ProjectName} ({p.CompletionYear?.ToString(CultureInfo.InvariantCulture) ?? "year n/a"}, {Nz(p.Sector)}, {Nz(p.Province)})";
        }
    }

    private static IEnumerable<string> OrgActionBullets(OrgIntelBundle? intel)
    {
        if (intel is null || intel.Actions.Count == 0)
        {
            yield return "No recommended actions on file yet.";
            yield break;
        }

        foreach (var a in intel.Actions)
        {
            var body = HumanizeActionType(a.ActionType) + ": " + a.Recommendation;
            if (!string.IsNullOrWhiteSpace(a.TargetPersonName))
            {
                body += " (target: " + a.TargetPersonName + ")";
            }
            if (!string.IsNullOrWhiteSpace(a.TimingNotes))
            {
                body += "  Timing: " + a.TimingNotes;
            }

            yield return FormatIntelBullet(body, a.Confidence, a.Freshness, a.RefreshedAtUtc);
        }
    }

    private static IEnumerable<string> OrgIntelPeopleBullets(OrgIntelBundle? intel)
    {
        if (intel is null || intel.People.Count == 0)
        {
            yield return "No key people on file yet.";
            yield break;
        }

        foreach (var p in intel.People)
        {
            var body = (!p.IsCurrent ? "(former) " : string.Empty) + p.DisplayName;
            if (!string.IsNullOrWhiteSpace(p.Title))
            {
                body += "  " + p.Title;
            }

            yield return FormatIntelBullet(body, p.Confidence, p.Freshness, p.RefreshedAtUtc);
        }
    }

    private static IEnumerable<string> OrgSignalBullets(OrgIntelBundle? intel)
    {
        if (intel is null || intel.Signals.Count == 0)
        {
            yield return "No recent signals tracked.";
            yield break;
        }

        foreach (var s in intel.Signals)
        {
            var body = HumanizeSignalType(s.SignalType) + ": " + s.Subject;
            if (!string.IsNullOrWhiteSpace(s.Detail))
            {
                body += "  " + s.Detail;
            }

            yield return FormatIntelBullet(body, s.Confidence, s.Freshness, s.RefreshedAtUtc);
        }
    }

    private static IEnumerable<string> OrgIntelWorkBullets(OrgIntelBundle? intel)
    {
        if (intel is null || intel.Works.Count == 0)
        {
            yield break;
        }

        foreach (var w in intel.Works)
        {
            var body = w.ProjectName;
            if (!string.IsNullOrWhiteSpace(w.Role))
            {
                body += "  " + w.Role;
            }
            if (!string.IsNullOrWhiteSpace(w.YearApprox))
            {
                body += " (" + w.YearApprox + ")";
            }

            yield return FormatIntelBullet(body, w.Confidence, w.Freshness, w.RefreshedAtUtc);
        }
    }

    private static IEnumerable<string> OrgRiskBullets(OrgIntelBundle? intel)
    {
        if (intel is null || intel.Risks.Count == 0)
        {
            yield break;
        }

        foreach (var r in intel.Risks)
        {
            var body = HumanizeRiskType(r.RiskType) + ": " + r.Description;
            if (!string.IsNullOrWhiteSpace(r.MitigationNotes))
            {
                body += " (Mitigation: " + r.MitigationNotes + ")";
            }

            yield return FormatIntelBullet(body, r.Confidence, r.Freshness, r.RefreshedAtUtc);
        }
    }

    // === Layout primitives ===

    private static void ConfigurePage(PageDescriptor page)
    {
        page.Size(PageSizes.Letter);
        page.Margin(34);
        page.DefaultTextStyle(TextStyle.Default.FontFamily("Mulish").FontSize(10).FontColor(Text));
    }

    private static void Section(IContainer container, string heading, IEnumerable<string> bullets)
    {
        container.Column(column =>
        {
            column.Spacing(6);
            column.Item().BorderBottom(1).BorderColor(BrandAccent).PaddingBottom(4)
                .Text(heading).FontSize(11.5f).Bold().FontColor(Brand);

            foreach (var bullet in bullets)
            {
                column.Item().Row(row =>
                {
                    row.ConstantItem(12).Text("•").FontColor(BrandAccent);
                    row.RelativeItem().Text(bullet).FontSize(10).FontColor(Text);
                });
            }
        });
    }

    private static void ParagraphSection(IContainer container, string heading, string paragraph)
    {
        container.Column(column =>
        {
            column.Spacing(6);
            column.Item().BorderBottom(1).BorderColor(BrandAccent).PaddingBottom(4)
                .Text(heading).FontSize(11.5f).Bold().FontColor(Brand);
            column.Item().Text(paragraph).FontSize(10).FontColor(Text);
        });
    }

    private static void ProjectSourceSection(IContainer container, ProjectBriefData data)
    {
        container.Column(column =>
        {
            column.Spacing(6);
            column.Item().BorderBottom(1).BorderColor(BrandAccent).PaddingBottom(4)
                .Text("Source").FontSize(11.5f).Bold().FontColor(Brand);
            if (string.IsNullOrWhiteSpace(data.SourceUrl))
            {
                column.Item().Text("No source URL on file.").FontSize(10).FontColor(Text);
            }
            else
            {
                column.Item().Text(t =>
                {
                    t.Hyperlink(data.SourceUrl!, data.SourceUrl!).FontSize(10).FontColor(Brand).Underline(true);
                });
            }
        });
    }

    private static void HeaderFact(TableDescriptor table, string label, string value)
    {
        // Round 49: smaller fact rows so the band can be shorter overall.
        table.Cell().Padding(2).Column(column =>
        {
            column.Item().Text(label).FontSize(7).LetterSpacing(0.1f).FontColor(BrandFactLabel);
            column.Item().Text(string.IsNullOrWhiteSpace(value) ? "—" : value)
                .FontSize(9).FontColor(Colors.White);
        });
    }

    private static void PageFooter(IContainer container)
    {
        container.BorderTop(1).BorderColor(Border).PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text(Footer).FontSize(8).Italic().FontColor(Muted);
            row.ConstantItem(60).AlignRight().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(8).FontColor(Muted));
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        });
    }

    // === Helpers ===

    private static string Nz(string? s) => string.IsNullOrWhiteSpace(s) ? "(unspecified)" : s!;

    private static string RegionOrgDisplayName(RegionTopOrg org)
        => string.IsNullOrWhiteSpace(org.ClendorClientId) ? org.DisplayName : "* " + org.DisplayName;

    private static string FormatDeadline(DateTimeOffset? deadline)
    {
        if (!deadline.HasValue) return "not specified";
        var d = deadline.Value;
        var days = (int)Math.Round((d - DateTimeOffset.Now).TotalDays);
        return $"{d:yyyy-MM-dd} ({days} day(s))";
    }

    private static string FormatValue(decimal? value)
    {
        return value.HasValue && value.Value > 0
            ? "CAD " + value.Value.ToString("N0", CultureInfo.InvariantCulture)
            : "not stated";
    }

    private static string FormatProjectValue(ProjectBriefData data)
        => data.EstimatedCostCad.HasValue && data.EstimatedCostCad.Value > 0
            ? "CAD " + data.EstimatedCostCad.Value.ToString("N0", CultureInfo.InvariantCulture)
            : string.IsNullOrWhiteSpace(data.EstimatedCostText) ? "not stated" : data.EstimatedCostText!;

    private static string ProjectLocation(ProjectBriefData data)
    {
        var cityPart = string.IsNullOrWhiteSpace(data.City) ? data.Province : $"{data.Province} / {data.City}";
        return string.IsNullOrWhiteSpace(data.Region) ? cityPart : $"{cityPart} / {data.Region}";
    }

    private static string ProjectSector(ProjectBriefData data)
    {
        if (string.IsNullOrWhiteSpace(data.Sector))
        {
            return Nz(data.SubSector);
        }

        return string.IsNullOrWhiteSpace(data.SubSector)
            ? data.Sector!
            : $"{data.Sector} / {data.SubSector}";
    }

    private static string FormatIntelBullet(
        string body,
        IntelConfidence confidence,
        IntelFreshness freshness,
        DateTimeOffset refreshedAtUtc)
    {
        var sb = new StringBuilder();
        if (confidence == IntelConfidence.Low)
        {
            sb.Append("(unverified) ");
        }

        sb.Append(body);
        if (freshness == IntelFreshness.Stale)
        {
            sb.Append($" (as of {refreshedAtUtc:yyyy-MM}, stale)");
        }
        else if (freshness == IntelFreshness.Aged)
        {
            sb.Append($" (as of {refreshedAtUtc:yyyy-MM})");
        }

        return sb.ToString();
    }

    private static string FormatActionBody(IntelActionRow a)
    {
        var body = HumanizeActionType(a.ActionType) + ": " + a.Recommendation;
        if (!string.IsNullOrWhiteSpace(a.TargetPersonName))
        {
            body += " (target: " + a.TargetPersonName + ")";
        }
        if (!string.IsNullOrWhiteSpace(a.TimingNotes))
        {
            body += "  Timing: " + a.TimingNotes;
        }

        return body;
    }

    private static string FormatPersonBody(IntelPersonRow p)
    {
        var body = (!p.IsCurrent ? "(former) " : string.Empty) + p.DisplayName;
        if (!string.IsNullOrWhiteSpace(p.Title))
        {
            body += "  " + p.Title;
        }

        return body;
    }

    private static string FormatSignalBody(IntelSignalRow s)
    {
        var body = HumanizeSignalType(s.SignalType) + ": " + s.Subject;
        if (!string.IsNullOrWhiteSpace(s.Detail))
        {
            body += "  " + s.Detail;
        }

        return body;
    }

    private static string FormatRiskBody(IntelRiskRow r)
    {
        var body = HumanizeRiskType(r.RiskType) + ": " + r.Description;
        if (!string.IsNullOrWhiteSpace(r.MitigationNotes))
        {
            body += " (Mitigation: " + r.MitigationNotes + ")";
        }

        return body;
    }

    // R81: Humanizers moved to Kor.Opportunities.Data.Intel.IntelTypeHumanizer
    // (single source of truth shared with DOCX brief and Dossier converter).
    private static string HumanizeSignalType(string type) =>
        Kor.Opportunities.Data.Intel.IntelTypeHumanizer.SignalType(type);

    private static string HumanizeRiskType(string type) =>
        Kor.Opportunities.Data.Intel.IntelTypeHumanizer.RiskType(type);

    private static string HumanizeActionType(string type) =>
        Kor.Opportunities.Data.Intel.IntelTypeHumanizer.ActionType(type);

    private static void EnsureDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static OrgEnrichment ParseEnrichment(string? json)
    {
        var enrichment = new OrgEnrichment();
        if (string.IsNullOrWhiteSpace(json)) return enrichment;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return enrichment;

            if (root.TryGetProperty("hqCity", out var hq) && hq.ValueKind == JsonValueKind.String)
            {
                enrichment.HqCity = hq.GetString();
            }
            if (root.TryGetProperty("sectors", out var sectors) && sectors.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in sectors.EnumerateArray())
                {
                    if (s.ValueKind == JsonValueKind.String)
                    {
                        var v = s.GetString();
                        if (!string.IsNullOrWhiteSpace(v)) enrichment.Sectors.Add(v!);
                    }
                }
            }
            if (root.TryGetProperty("keyPeople", out var people) && people.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in people.EnumerateArray())
                {
                    if (p.ValueKind != JsonValueKind.Object) continue;
                    var name = p.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                    var title = p.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        enrichment.KeyPeople.Add((name!, title));
                    }
                }
            }
        }
        catch
        {
            // Malformed enrichment shouldn't break a brief.
        }
        return enrichment;
    }

    private sealed class OrgEnrichment
    {
        public string? HqCity { get; set; }
        public List<string> Sectors { get; } = new();
        public List<(string Name, string? Title)> KeyPeople { get; } = new();
    }
}
