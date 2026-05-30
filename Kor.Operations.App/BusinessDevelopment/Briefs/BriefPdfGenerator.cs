#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Kor.Opportunities.Data.Briefs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Kor.Operations.App.BusinessDevelopment.Briefs;

/// <summary>
/// QuestPDF renderer for the three Pursuit Brief shapes. Brand-matches
/// <c>PursuitBriefPdfExporter</c>: Letter, Mulish, navy header band, section
/// strip + body. Stateless + thread-safe (register as Singleton).
/// </summary>
public sealed class BriefPdfGenerator : IBriefPdfGenerator
{
    private const string Navy = "#1E3A8A";
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
            });
            page.Footer().Element(PageFooter);
        });
    }

    private static void OppHeaderBand(IContainer container, OpportunityBriefData d)
    {
        container.Background(Navy).Padding(16).Column(column =>
        {
            column.Spacing(10);
            column.Item().Text("PURSUIT BRIEF")
                .FontSize(9).LetterSpacing(0.2f).FontColor("#BFD3FF");
            column.Item().Text(Nz(d.Name)).FontSize(18).Bold().FontColor(Colors.White);
            column.Item().Text("Warmest live target — recommended next move")
                .FontSize(10).Italic().FontColor("#DDE5FA");

            column.Item().PaddingTop(6).Table(table =>
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
            });
            page.Footer().Element(PageFooter);
        });
    }

    private static void RegionHeaderBand(IContainer container, RegionBriefData d)
    {
        var scope = string.IsNullOrWhiteSpace(d.City) ? d.Province : $"{d.Province} — {d.City}";
        container.Background(Navy).Padding(16).Column(column =>
        {
            column.Spacing(8);
            column.Item().Text("REGION BRIEF")
                .FontSize(9).LetterSpacing(0.2f).FontColor("#BFD3FF");
            column.Item().Text(scope).FontSize(20).Bold().FontColor(Colors.White);

            column.Item().PaddingTop(6).Table(table =>
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
            yield return $"{o.DisplayName} — {o.ProjectCount} project(s) {countSuffix}{korPart}.";
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
                column.Item().Element(c => Section(c, "KOR's history with this organization",
                    OrgHistoryBullets(d)));
                column.Item().Element(c => Section(c, "Their recent work",
                    OrgRecentBullets(d)));
                column.Item().Element(c => Section(c, "Key people",
                    OrgKeyPeopleBullets(enrichment)));
                column.Item().Element(c => Section(c, "Talking points for the visit",
                    OrgTalkingPointsBullets(d)));
            });
            page.Footer().Element(PageFooter);
        });
    }

    private static void OrgHeaderBand(IContainer container, OrgBriefData d, OrgEnrichment enrichment)
    {
        container.Background(Navy).Padding(16).Column(column =>
        {
            column.Spacing(8);
            column.Item().Text("ORGANIZATION BRIEF")
                .FontSize(9).LetterSpacing(0.2f).FontColor("#BFD3FF");
            column.Item().Text(d.DisplayName).FontSize(20).Bold().FontColor(Colors.White);
            column.Item().Text($"({d.Kind})")
                .FontSize(10).Italic().FontColor("#DDE5FA");

            column.Item().PaddingTop(6).Table(table =>
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

    private static IEnumerable<string> OrgKeyPeopleBullets(OrgEnrichment enrichment)
    {
        if (enrichment.KeyPeople.Count == 0)
        {
            yield return "No key people captured yet — flag for the next honing pass.";
            yield break;
        }
        foreach (var (name, title) in enrichment.KeyPeople)
        {
            yield return string.IsNullOrWhiteSpace(title) ? name : $"{name} — {title}";
        }
    }

    private static IEnumerable<string> OrgTalkingPointsBullets(OrgBriefData d)
    {
        yield return (d.KorProjectsCount > 0 || d.KorJointProjectCount > 0)
            ? "Lead with the shared history — reference the most recent KOR project together by name."
            : "Lead with KOR's relevant sector capability one-pager; this is a new-relationship visit.";

        if (d.RecentProjects.Count > 0)
        {
            yield return "Reference their recent project(s) above to show our team has done its homework.";
        }
        yield return "Surface one specific live or upcoming pursuit where KOR could be their structural partner.";
        yield return "Confirm decision-maker for structural selection on their typical projects.";
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
            column.Item().BorderBottom(1).BorderColor(Border).PaddingBottom(4)
                .Text(heading).FontSize(11.5f).Bold().FontColor(Navy);

            foreach (var bullet in bullets)
            {
                column.Item().Row(row =>
                {
                    row.ConstantItem(12).Text("•").FontColor(Muted);
                    row.RelativeItem().Text(bullet).FontSize(10).FontColor(Text);
                });
            }
        });
    }

    private static void HeaderFact(TableDescriptor table, string label, string value)
    {
        table.Cell().Padding(3).Column(column =>
        {
            column.Item().Text(label).FontSize(8).LetterSpacing(0.1f).FontColor("#9FB4F2");
            column.Item().Text(string.IsNullOrWhiteSpace(value) ? "—" : value)
                .FontSize(10).FontColor(Colors.White);
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
