#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Kor.Opportunities.Data.Briefs;

namespace Kor.Operations.App.BusinessDevelopment.Briefs;

/// <summary>
/// Canonical-template brief renderer: fills the KOR document design system
/// (tools/BdDocTemplate — slate #3F5364 + orange #FF5B35, hero band, mono
/// kickers, diamond bullets, editorial tables) from the brief data models and
/// prints to PDF via headless Edge. Org and Region briefs render here; the
/// remaining shapes delegate to the QuestPDF generator until converted. If
/// Edge is unavailable on the machine, every shape falls back to QuestPDF so
/// Generate Brief never breaks.
/// </summary>
public sealed class HtmlBriefPdfGenerator : IBriefPdfGenerator
{
    private readonly BriefPdfGenerator _fallback = new();

    public void WriteOpportunityBrief(OpportunityBriefData data, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!TryRenderHtmlPdf(BuildOpportunityHtml(data), outputPath))
        {
            _fallback.WriteOpportunityBrief(data, outputPath);
        }
    }

    public void WriteProjectBrief(ProjectBriefData data, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!TryRenderHtmlPdf(BuildProjectHtml(data), outputPath))
        {
            _fallback.WriteProjectBrief(data, outputPath);
        }
    }

    public void WritePersonBrief(PersonBriefData data, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!TryRenderHtmlPdf(BuildPersonHtml(data), outputPath))
        {
            _fallback.WritePersonBrief(data, outputPath);
        }
    }

    public void WriteSectorBrief(SectorBriefData data, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!TryRenderHtmlPdf(BuildSectorHtml(data), outputPath))
        {
            _fallback.WriteSectorBrief(data, outputPath);
        }
    }

    // ===== Opportunity =====

    private static string BuildOpportunityHtml(OpportunityBriefData d)
    {
        var b = new HtmlDoc($"Pursuit Brief — {BriefPdfGenerator.Nz(d.Name)}");
        var location = string.IsNullOrWhiteSpace(d.ProjectCity)
            ? BriefPdfGenerator.Nz(d.ProjectProvince)
            : $"{d.ProjectCity}, {BriefPdfGenerator.Nz(d.ProjectProvince)}";
        b.Hero("Pursuit Brief", BriefPdfGenerator.Nz(d.Name),
            "Warmest live target — recommended next move", new[]
            {
                ("Owner", BriefPdfGenerator.Nz(d.BuyerName)),
                ("Location", location),
                ("Sector", BriefPdfGenerator.Nz(d.PrimeProjectSector)),
                ("Confidence", d.PrimeConfidence.ToString("F2", CultureInfo.InvariantCulture)),
                ("Submission deadline", BriefPdfGenerator.FormatDeadline(d.SubmissionDeadlineUtc)),
                ("Estimated value", BriefPdfGenerator.FormatValue(d.EstimatedValue)),
            });

        b.BulletSection("Why this is our warmest target right now", BriefPdfGenerator.OppWarmthBullets(d));
        b.BulletSection("KOR's angle (relationship intelligence)", BriefPdfGenerator.OppAngleBullets(d));
        b.BulletSection("Get in front of them this week", BriefPdfGenerator.OppEventBullets(d));
        b.BulletSection("Recommended next steps", BriefPdfGenerator.OppNextStepBullets(d));
        if (d.Intel?.BuyerIntel is not null)
        {
            b.BulletSection($"About the buyer ({d.BuyerName ?? "buyer"})",
                BriefPdfGenerator.OppBuyerIntelBullets(d.Intel.BuyerIntel));
        }
        if (d.Intel?.ArchitectIntel is not null && !string.IsNullOrWhiteSpace(d.LikelyArchitectName))
        {
            b.BulletSection($"About the likely architect ({d.LikelyArchitectName})",
                BriefPdfGenerator.OppArchitectIntelBullets(d.Intel.ArchitectIntel));
        }
        return b.Finish();
    }

    // ===== Project =====

    private static string BuildProjectHtml(ProjectBriefData d)
    {
        var b = new HtmlDoc($"Project Brief — {BriefPdfGenerator.Nz(d.ProjectName)}");
        b.Hero("Project Brief", BriefPdfGenerator.Nz(d.ProjectName),
            "Pursuit prep for one forward-pipeline project", new[]
            {
                ("Stage", BriefPdfGenerator.Nz(d.Stage)),
                ("Location", BriefPdfGenerator.ProjectLocation(d)),
                ("Sector", BriefPdfGenerator.ProjectSector(d)),
                ("Estimated value", BriefPdfGenerator.FormatProjectValue(d)),
            });

        var description = !string.IsNullOrWhiteSpace(d.IntelDescription)
            ? d.IntelDescription!
            : (string.IsNullOrWhiteSpace(d.ProjectDescription)
                ? "No project description on file yet."
                : d.ProjectDescription!);
        b.ParagraphSection("Project description", description);
        b.BulletSection("Schedule", BriefPdfGenerator.ProjectScheduleBullets(d));
        if (!string.IsNullOrWhiteSpace(d.IntelStatus)) b.ParagraphSection("Status", d.IntelStatus!);
        b.BulletSection("Team & KOR angle", BriefPdfGenerator.ProjectTeamBullets(d));
        if (!string.IsNullOrWhiteSpace(d.IntelKorAngle)) b.ParagraphSection("KOR angle", d.IntelKorAngle!);
        if (d.MentionsInWorkHistory.Count > 0)
            b.BulletSection("On other portfolios", BriefPdfGenerator.ProjectMentionWorkBullets(d));
        if (d.RecentMentionSignals.Count > 0)
            b.BulletSection("Recent signals mentioning this project", BriefPdfGenerator.ProjectMentionSignalBullets(d));
        if (d.OpenActionsMentioning.Count > 0)
            b.BulletSection("KOR open actions mentioning this project", BriefPdfGenerator.ProjectMentionActionBullets(d));
        if (d.IntelKeyPeople.Count > 0)
            b.BulletSection("Key people on this project", BriefPdfGenerator.ProjectKeyPeopleBullets(d));
        if (d.IntelSignals.Count > 0)
            b.BulletSection("Project signals", BriefPdfGenerator.ProjectIntelSignalBullets(d));
        if (d.IntelActions.Count > 0)
            b.BulletSection("KOR actions on this project", BriefPdfGenerator.ProjectIntelActionBullets(d));
        if (d.IntelRisks.Count > 0)
            b.BulletSection("Project risks", BriefPdfGenerator.ProjectIntelRiskBullets(d));
        b.LinkSection("Source", d.SourceUrl);
        return b.Finish();
    }

    // ===== Person =====

    private static string BuildPersonHtml(PersonBriefData d)
    {
        var b = new HtmlDoc($"Person Brief — {BriefPdfGenerator.Nz(d.DisplayName)}");
        b.Hero("Person Brief", BriefPdfGenerator.Nz(d.DisplayName),
            "Contact prep for one named individual", new[]
            {
                ("Current title", BriefPdfGenerator.Nz(d.CurrentTitle)),
                ("Current employer", BriefPdfGenerator.Nz(d.CurrentEmployerName)),
                ("Last seen", d.LastSeenAtUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "not recorded"),
            });

        if (!string.IsNullOrWhiteSpace(d.Email) || !string.IsNullOrWhiteSpace(d.Phone)
            || !string.IsNullOrWhiteSpace(d.LinkedinUrl))
        {
            b.BulletSection("Contact", BriefPdfGenerator.PersonContactBullets(d));
        }
        if (!string.IsNullOrWhiteSpace(d.Notes)) b.ParagraphSection("Notes", d.Notes!);
        if (d.CurrentAffiliations.Count > 0)
            b.AffiliationSection("Current roles", d.CurrentAffiliations, includeEndDate: false);
        if (d.FormerAffiliations.Count > 0)
            b.AffiliationSection("Career history", d.FormerAffiliations, includeEndDate: true);
        if (d.RecentSignals.Count > 0)
            b.BulletSection("Recent activity", BriefPdfGenerator.PersonSignalBullets(d));
        if (d.OpenActions.Count > 0)
            b.BulletSection("KOR open actions targeting this person", BriefPdfGenerator.PersonActionBullets(d));
        return b.Finish();
    }

    // ===== Sector =====

    private static string BuildSectorHtml(SectorBriefData d)
    {
        var title = BriefPdfGenerator.SectorHeaderTitle(d.Request);
        var b = new HtmlDoc($"Sector Brief — {title}");
        b.Hero("Sector Brief", title,
            "Slice across the forward pipeline, live RFPs, and recent awards", new[]
            {
                ("Live RFPs", d.Counts.LiveRfpCount.ToString(CultureInfo.InvariantCulture)),
                ("Forward pipeline", d.Counts.ForwardPipelineCount.ToString(CultureInfo.InvariantCulture)),
                ("Recent awards", d.Counts.RecentAwardCount.ToString(CultureInfo.InvariantCulture)),
                ("Pipeline $", d.Counts.TotalForwardPipelineCostCad is { } v
                    ? v.ToString("C0", CultureInfo.CurrentCulture) : "—"),
            });

        b.BulletSection("Filter criteria", BriefPdfGenerator.SectorFilterBullets(d));
        if (d.LiveRfps.Count > 0)
            b.BulletSection("Live RFPs in this slice", BriefPdfGenerator.SectorLiveRfpBullets(d));
        if (d.ForwardProjects.Count > 0)
            b.BulletSection("Forward pipeline in this slice", BriefPdfGenerator.SectorForwardProjectBullets(d));
        if (d.RecentAwards.Count > 0)
            b.BulletSection("Recent awards in this slice (last 12 months)", BriefPdfGenerator.SectorRecentAwardBullets(d));
        if (d.TopArchitects.Count > 0)
            b.BulletSection("Top architects active in this slice", BriefPdfGenerator.SectorTopOrgBullets(d.TopArchitects));
        if (d.TopOwners.Count > 0)
            b.BulletSection("Top owners commissioning in this slice", BriefPdfGenerator.SectorTopOrgBullets(d.TopOwners));
        if (d.TopGcs.Count > 0)
            b.BulletSection("Top GCs in this slice", BriefPdfGenerator.SectorTopOrgBullets(d.TopGcs));
        if (d.TopStructuralCompetitors.Count > 0)
            b.BulletSection("Top structural competitors in this slice", BriefPdfGenerator.SectorTopOrgBullets(d.TopStructuralCompetitors));
        if (d.KorPortfolio.Count > 0)
            b.BulletSection("KOR's own track record in this slice", BriefPdfGenerator.SectorKorPortfolioBullets(d));
        if (d.RelevantSignals.Count > 0)
            b.BulletSection("Relevant Intel signals", BriefPdfGenerator.SectorIntelSignalBullets(d));
        return b.Finish();
    }

    public void WriteRegionBrief(RegionBriefData data, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!TryRenderHtmlPdf(BuildRegionHtml(data), outputPath))
        {
            _fallback.WriteRegionBrief(data, outputPath);
        }
    }

    public void WriteOrgBrief(OrgBriefData data, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!TryRenderHtmlPdf(BuildOrgHtml(data), outputPath))
        {
            _fallback.WriteOrgBrief(data, outputPath);
        }
    }

    // ===== Region =====

    private static string BuildRegionHtml(RegionBriefData d)
    {
        var scope = string.IsNullOrWhiteSpace(d.City) ? d.Province : $"{d.Province} — {d.City}";
        var b = new HtmlDoc($"Region Brief — {scope}");
        b.Hero("Region Brief", scope, null, new[]
        {
            ("Live prime RFPs", d.LivePrimeRfpCount.ToString(CultureInfo.InvariantCulture)),
            ("Forward pipeline", d.ForwardPipelineCount.ToString(CultureInfo.InvariantCulture)),
            ("Active major projects", d.ActiveMpiCount.ToString(CultureInfo.InvariantCulture)),
        });

        b.BulletSection("Top architects in this market",
            BriefPdfGenerator.FormatTopOrgs(d.TopArchitects, "in this market", korNote: true,
                empty: "No architects tied to projects in this market in our data yet."));
        b.BulletSection("Top owners / clients in this market",
            BriefPdfGenerator.FormatTopOrgs(d.TopOwners, "in this market", korNote: true, ownerNote: true,
                empty: "No owners tied to projects in this market in our data yet."));
        b.BulletSection("Top competitors in this market (structural)",
            BriefPdfGenerator.FormatTopOrgs(d.TopCompetitors, "as structural EOR here", korNote: false,
                empty: "No competitors flagged on projects in this market in our data yet."));
        b.BulletSection("Live prime RFPs (top 5)", BriefPdfGenerator.RegionLiveRfpsLines(d));
        b.BulletSection("Forward pipeline (planned / funded)", BriefPdfGenerator.RegionForwardLines(d));
        b.BulletSection("Upcoming events in this market", BriefPdfGenerator.RegionEventLines(d));

        if (d.Intel is not null)
        {
            b.BulletSection("Cross-org actionables in this region",
                BriefPdfGenerator.RegionIntelActionBullets(d.Intel));
            if (d.Intel.RecentLeadershipChanges.Count > 0)
            {
                b.BulletSection("Recent leadership changes in this region (last 90 days)",
                    BriefPdfGenerator.RegionLeadershipChangeBullets(d.Intel));
            }
            if (d.Intel.TopCapacityRisks.Count > 0)
            {
                b.BulletSection("Capacity-strain signals (competitor displacement opportunities)",
                    BriefPdfGenerator.RegionCapacityRiskBullets(d.Intel));
            }
        }

        return b.Finish();
    }

    // ===== Org =====

    private static string BuildOrgHtml(OrgBriefData d)
    {
        var enrichment = BriefPdfGenerator.ParseEnrichment(d.DataHoningEnrichmentJson);
        var b = new HtmlDoc($"Organization Brief — {d.DisplayName}");

        var facts = new List<(string, string)>
        {
            ("Website", string.IsNullOrWhiteSpace(d.Website) ? "(not on file)" : d.Website!),
            ("KOR projects", d.KorProjectsCount.ToString(CultureInfo.InvariantCulture)),
            ("Last KOR engagement", d.LastKorProjectAtUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "(none)"),
            ("Intelligence", d.IntelProvenanceLine),
        };
        if (!string.IsNullOrWhiteSpace(enrichment.HqCity)) facts.Add(("HQ", enrichment.HqCity!));
        if (enrichment.Sectors.Count > 0) facts.Add(("Sectors", string.Join(", ", enrichment.Sectors)));

        var lede = $"({d.Kind})" + (string.IsNullOrWhiteSpace(d.ResolvedFromNote) ? "" : " · " + d.ResolvedFromNote);
        b.Hero("Organization Brief", d.DisplayName, lede, facts);

        if (!string.IsNullOrWhiteSpace(d.Intel?.SynopsisParagraph1)
            || !string.IsNullOrWhiteSpace(d.Intel?.SynopsisParagraph2))
        {
            b.SynopsisBox(d.Intel!.SynopsisParagraph1, d.Intel.SynopsisParagraph2);
        }

        b.BulletSection("KOR's history with this organization", BriefPdfGenerator.OrgHistoryBullets(d));
        b.BulletSection("Recommended actions", BriefPdfGenerator.OrgActionBullets(d.Intel));
        b.BulletSection("Key people on file", BriefPdfGenerator.OrgIntelPeopleBullets(d.Intel));
        b.BulletSection("Recent signals", BriefPdfGenerator.OrgSignalBullets(d.Intel));
        b.BulletSection("Their recent work", BriefPdfGenerator.OrgRecentBullets(d));
        if (d.Intel is { Works.Count: > 0 })
        {
            b.BulletSection("Their portfolio (from research)", BriefPdfGenerator.OrgIntelWorkBullets(d.Intel));
        }

        if (d.Deltek is { } dk)
        {
            b.DeltekSection(dk);
        }
        else if (!string.IsNullOrWhiteSpace(d.DeltekNote))
        {
            b.NoteSection("KOR engagement history (Deltek)", d.DeltekNote!);
        }

        if (d.Intel is { Risks.Count: > 0 })
        {
            b.BulletSection("Risks / vulnerabilities", BriefPdfGenerator.OrgRiskBullets(d.Intel));
        }

        return b.Finish();
    }

    // ===== Headless-Edge printing =====

    private static bool TryRenderHtmlPdf(string html, string outputPath)
    {
        try
        {
            var edge = new[]
            {
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            }.FirstOrDefault(File.Exists);
            if (edge is null) return false;

            var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var scratch = Path.Combine(Path.GetTempPath(), $"kor-brief-{Guid.NewGuid():N}");
            Directory.CreateDirectory(scratch);
            var tmp = Path.Combine(scratch, "brief.html");
            File.WriteAllText(tmp, html, new UTF8Encoding(false));
            try
            {
                var target = Path.GetFullPath(outputPath);
                try { File.Delete(target); } catch { /* Best-effort cleanup: a locked stale PDF will make the render fail below. */ }
                var psi = new ProcessStartInfo
                {
                    FileName = edge,
                    // Isolated --user-data-dir forces a fresh headless instance; without
                    // it, msedge.exe hands off to the user's running Edge and exits
                    // before the PDF is written (silent fallback to QuestPDF).
                    Arguments = "--headless=new --disable-gpu --no-first-run --no-pdf-header-footer " +
                                $"--user-data-dir=\"{Path.Combine(scratch, "profile")}\" " +
                                $"--print-to-pdf=\"{target}\" \"file:///{tmp.Replace('\\', '/')}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                if (proc is null) return false;
                if (!proc.WaitForExit(60_000)) { try { proc.Kill(true); } catch { /* Best-effort cleanup: the timeout already returns a failed render. */ } return false; }

                // Edge can exit a beat before the file is flushed.
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (DateTime.UtcNow < deadline)
                {
                    if (File.Exists(target) && new FileInfo(target).Length > 0) return true;
                    System.Threading.Thread.Sleep(250);
                }
                return false;
            }
            finally
            {
                try { Directory.Delete(scratch, recursive: true); } catch { /* Best-effort cleanup of temp HTML/profile files after rendering. */ }
            }
        }
        catch
        {
            return false;
        }
    }

    // ===== The canonical template, brief-sized =====

    private sealed class HtmlDoc
    {
        private readonly StringBuilder _sb = new();
        private int _sectionNo;

        public HtmlDoc(string title)
        {
            _sb.Append("<title>").Append(E(title)).Append("</title>\n<style>\n").Append(Css).Append("\n</style>\n");
        }

        public void Hero(string eyebrow, string title, string? lede, IReadOnlyList<(string Label, string Value)> facts)
        {
            _sb.Append("<header class=\"hero\"><div class=\"hero__wrap\">");
            _sb.Append("<p class=\"hero__eyebrow\">").Append(E(eyebrow)).Append("</p>");
            _sb.Append("<h1>").Append(E(title)).Append("</h1>");
            if (!string.IsNullOrWhiteSpace(lede)) _sb.Append("<p class=\"hero__lede\">").Append(E(lede)).Append("</p>");
            if (facts.Count > 0)
            {
                _sb.Append("<div class=\"facts\">");
                foreach (var (label, value) in facts)
                {
                    _sb.Append("<div class=\"fact\"><span class=\"fact__l\">").Append(E(label))
                       .Append("</span><span class=\"fact__v\">").Append(E(value)).Append("</span></div>");
                }
                _sb.Append("</div>");
            }
            _sb.Append("</div></header><main class=\"doc\">");
        }

        public void SynopsisBox(string? p1, string? p2)
        {
            _sb.Append("<div class=\"box box--auto\"><p class=\"box__label\">Synopsis</p>");
            if (!string.IsNullOrWhiteSpace(p1)) _sb.Append("<p>").Append(E(p1)).Append("</p>");
            if (!string.IsNullOrWhiteSpace(p2)) _sb.Append("<p>").Append(E(p2)).Append("</p>");
            _sb.Append("</div>");
        }

        public void BulletSection(string heading, IEnumerable<string> bullets)
        {
            OpenSection(heading);
            _sb.Append("<ul class=\"plain\">");
            foreach (var line in bullets) _sb.Append("<li>").Append(E(line)).Append("</li>");
            _sb.Append("</ul></section>");
        }

        public void NoteSection(string heading, string note)
        {
            OpenSection(heading);
            _sb.Append("<p class=\"muted\"><em>").Append(E(note)).Append("</em></p></section>");
        }

        public void ParagraphSection(string heading, string paragraph)
        {
            OpenSection(heading);
            _sb.Append("<p>").Append(E(paragraph)).Append("</p></section>");
        }

        public void LinkSection(string heading, string? url)
        {
            OpenSection(heading);
            if (string.IsNullOrWhiteSpace(url))
            {
                _sb.Append("<p class=\"muted\"><em>No source URL on file.</em></p>");
            }
            else
            {
                _sb.Append("<p><a href=\"").Append(E(url)).Append("\">").Append(E(url)).Append("</a></p>");
            }
            _sb.Append("</section>");
        }

        public void AffiliationSection(string heading, IReadOnlyList<PersonAffiliationRow> rows, bool includeEndDate)
        {
            OpenSection(heading);
            _sb.Append("<div class=\"table-wrap\"><table><thead><tr><th>Org</th><th>Title</th><th>")
               .Append(includeEndDate ? "End" : "Department")
               .Append("</th></tr></thead><tbody>");
            foreach (var row in rows)
            {
                _sb.Append("<tr><td>").Append(E(row.OrgName))
                   .Append("</td><td>").Append(E(BriefPdfGenerator.Nz(row.Title)))
                   .Append("</td><td>").Append(E(includeEndDate
                        ? BriefPdfGenerator.Nz(row.EndDateApprox)
                        : BriefPdfGenerator.Nz(row.Department)))
                   .Append("</td></tr>");
            }
            _sb.Append("</tbody></table></div></section>");
        }

        public void DeltekSection(OrgBriefDeltekSection dk)
        {
            OpenSection("KOR engagement history (Deltek)");
            _sb.Append("<dl class=\"map\">");
            Fact("Deltek client", $"{dk.ClientName} ({dk.DeltekClientId})");
            Fact("Projects", dk.ProjectCount.ToString(CultureInfo.InvariantCulture));
            Fact("Lifetime fee", dk.LifetimeFee.ToString("C0", CultureInfo.CurrentCulture));
            Fact("Latest project", dk.LatestProjectName is null
                ? "(none)"
                : $"{dk.LatestProjectName} ({dk.LatestProjectStart:yyyy-MM})");
            Fact("Contacts on file", dk.ContactCount.ToString(CultureInfo.InvariantCulture));
            Fact("AR outstanding", $"{dk.ArOutstanding:C0} ({dk.Ar90Plus:C0} at 90+)");
            _sb.Append("</dl>");

            if (dk.RecentProjects.Count > 0)
            {
                _sb.Append("<div class=\"table-wrap\"><table><thead><tr>")
                   .Append("<th>Project</th><th>#</th><th>Opened</th><th>Status</th><th>Fee</th><th>Billed</th>")
                   .Append("</tr></thead><tbody>");
                foreach (var p in dk.RecentProjects)
                {
                    _sb.Append("<tr><td>").Append(E(p.Name))
                       .Append("</td><td>").Append(E(p.Wbs1))
                       .Append("</td><td>").Append(p.OpenDate?.ToString("yyyy-MM", CultureInfo.InvariantCulture) ?? "—")
                       .Append("</td><td>").Append(E(p.Status ?? "—"))
                       .Append("</td><td>").Append(p.Fee.ToString("C0", CultureInfo.CurrentCulture))
                       .Append("</td><td>").Append(p.FeeBilled.ToString("C0", CultureInfo.CurrentCulture))
                       .Append("</td></tr>");
                }
                _sb.Append("</tbody></table></div>");
            }
            if (dk.DegradedSections)
            {
                _sb.Append("<p class=\"muted\"><em>Some Deltek sections were unavailable when this brief was generated.</em></p>");
            }
            _sb.Append("</section>");

            void Fact(string label, string value)
                => _sb.Append("<div><dt>").Append(E(label)).Append("</dt><dd>").Append(E(value)).Append("</dd></div>");
        }

        public string Finish()
        {
            _sb.Append("<footer>KOR Structural — Confidential / Internal · generated ")
               .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
               .Append("</footer></main>");
            return _sb.ToString();
        }

        private void OpenSection(string heading)
        {
            _sectionNo++;
            _sb.Append("<section><p class=\"kicker\">")
               .Append(_sectionNo.ToString("00", CultureInfo.InvariantCulture))
               .Append("</p><h2>").Append(E(heading)).Append("</h2>");
        }

        private static string E(string? s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);

        // The BdDocTemplate design system, brief-sized (smaller type per 2026-07-03
        // direction) and print-first: light palette only, Letter, compact hero.
        private const string Css = """
:root{--ground:#FBFAF9;--surface:#FFFFFF;--surface-2:#F4F2F0;--ink:#1C2530;--ink-soft:#55636F;
--ink-faint:#8A97A2;--line:#E5E4E1;--line-strong:#D3D2CE;--slate:#3F5364;--slate-deep:#2C3A47;
--accent:#FF5B35;--accent-ink:#C6360F;--radius:8px;
--sans:"Segoe UI",system-ui,Arial,sans-serif;--mono:"Cascadia Code",Consolas,ui-monospace,monospace;}
*{box-sizing:border-box}
body{margin:0;background:#fff;color:var(--ink);font-family:var(--sans);font-size:12.5px;line-height:1.55;
-webkit-print-color-adjust:exact;print-color-adjust:exact}
.hero{background:radial-gradient(120% 140% at 88% -20%,rgba(255,91,53,.20),transparent 55%),
linear-gradient(180deg,var(--slate-deep),var(--slate));color:#EAF0F5;
padding:9mm 10mm 6mm;border-bottom:2.5px solid var(--accent)}
.hero__eyebrow{font-family:var(--mono);font-size:8px;letter-spacing:.22em;text-transform:uppercase;
color:#FFB59F;margin:0 0 .45rem}
.hero h1{font-size:21px;line-height:1.08;margin:0 0 .3rem;font-weight:700;letter-spacing:-.015em;color:#fff}
.hero__lede{font-size:10.5px;margin:0;color:#CBD8E2}
.facts{display:flex;flex-wrap:wrap;gap:.35rem .5rem;margin-top:.7rem}
.fact{background:rgba(255,255,255,.07);border:1px solid rgba(255,255,255,.13);border-radius:6px;
padding:.28rem .55rem;min-width:0;max-width:100%}
.fact__l{display:block;font-family:var(--mono);font-size:6.8px;letter-spacing:.14em;text-transform:uppercase;
color:#9FB3C4;margin-bottom:1px}
.fact__v{display:block;font-size:9.6px;color:#fff;word-break:break-word}
.doc{padding:5mm 10mm 8mm}
section{padding:3.2mm 0 1mm;border-top:1px solid var(--line)}
section:first-of-type{border-top:0}
.kicker{font-family:var(--mono);font-size:7.4px;letter-spacing:.16em;text-transform:uppercase;
color:var(--accent-ink);margin:0 0 .15rem;font-weight:600}
h2{font-size:13.5px;line-height:1.15;margin:0 0 .5rem;letter-spacing:-.01em;font-weight:700}
p{margin:0 0 .55rem}
.muted{color:var(--ink-soft)}
ul.plain{margin:0 0 .4rem;padding-left:0;list-style:none}
ul.plain li{position:relative;padding-left:1.15rem;margin:0 0 .32rem}
ul.plain li::before{content:"";position:absolute;left:.2rem;top:.5em;width:.32rem;height:.32rem;
border-radius:1.5px;background:var(--accent);transform:rotate(45deg)}
.box{border-radius:var(--radius);padding:.6rem .75rem;margin:.6rem 0;border:1px solid var(--line);
background:var(--surface-2)}
.box--auto{border-left:3px solid var(--accent)}
.box__label{font-family:var(--mono);font-size:7px;letter-spacing:.14em;text-transform:uppercase;
margin:0 0 .3rem;font-weight:700;color:var(--accent-ink)}
.box p{font-style:italic;color:var(--slate);margin-bottom:.35rem}
.box p:last-child{margin-bottom:0}
dl.map{display:grid;grid-template-columns:repeat(2,1fr);gap:.15rem .8rem;margin:.3rem 0 .55rem}
dl.map div{display:flex;gap:.5rem;align-items:baseline;padding:.22rem .1rem;border-bottom:1px solid var(--line)}
dl.map dt{font-family:var(--mono);font-size:7.6px;color:var(--accent-ink);font-weight:600;
min-width:6rem;text-transform:uppercase;letter-spacing:.06em}
dl.map dd{margin:0;font-size:10.6px;color:var(--ink-soft)}
.table-wrap{margin:.4rem 0}
table{border-collapse:collapse;width:100%;font-size:10.2px}
th,td{text-align:left;padding:.32rem .45rem;border-bottom:1px solid var(--line);vertical-align:top}
th{font-family:var(--mono);font-size:7px;letter-spacing:.1em;text-transform:uppercase;
color:var(--ink-faint);font-weight:600;border-bottom:1.5px solid var(--slate)}
td:first-child{font-weight:600}
footer{border-top:1px solid var(--line);margin-top:4mm;padding-top:2mm;color:var(--ink-faint);
font-family:var(--mono);font-size:7.6px}
@page{size:Letter;margin:10mm 0 12mm}
@media print{
h2{break-after:avoid}.kicker{break-after:avoid}
ul.plain li,tr,.box,dl.map div{break-inside:avoid}
.table-wrap{break-inside:auto}
}
""";
    }
}
