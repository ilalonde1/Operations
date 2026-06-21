#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Kor.Opportunities.Data.Briefs;
using Kor.Opportunities.Data.Intel;

namespace Kor.Operations.App.BusinessDevelopment.Briefs;

/// <summary>
/// OpenXML-based .docx renderer for the three Pursuit Brief shapes. Stateless +
/// thread-safe — register as a Singleton. Uses direct character formatting (font
/// size in half-points, bold, italic) rather than a styles part, so the document
/// renders consistently without a template dependency.
/// </summary>
public sealed class BriefGenerator : IBriefGenerator
{
    // OpenXml font sizes are expressed in half-points.
    private const int TitlePt = 64;     // 32 pt
    private const int SubtitlePt = 28;  // 14 pt
    private const int HeadingPt = 28;   // 14 pt
    private const int BodyPt = 22;      // 11 pt
    private const int FooterPt = 18;    //  9 pt

    private const string BrandAccent = "FF5B35"; // KOR orange accent
    private const string FooterText = "KOR Structural — Confidential / Internal";

    public void WriteOpportunityBrief(OpportunityBriefData data, string outputPath)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path is required.", nameof(outputPath));

        EnsureDirectory(outputPath);
        using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        var body = main.Document.Body!;

        AppendTitle(body, "Pursuit Brief");
        AppendSubtitle(body, "Warmest live target — recommended next move");
        AppendHeading(body, data.Name);
        AppendBody(body, $"Owner: {Nz(data.BuyerName)}  |  Location: {Nz(data.ProjectCity)}, {Nz(data.ProjectProvince)}  |  Sector: {Nz(data.PrimeProjectSector)}");
        AppendBody(body, "Submission deadline: " + FormatDeadline(data.SubmissionDeadlineUtc));
        AppendBody(body, "Estimated value: " + FormatValue(data.EstimatedValue));

        AppendHeading(body, "Why this is our warmest target right now");
        AppendBullet(body, $"Prime-consultant building RFP (the architect's team is being assembled now, before the structural seat is set) — classifier confidence {data.PrimeConfidence.ToString("F2", CultureInfo.InvariantCulture)}.");
        AppendBullet(body, "Sector AND geography both match KOR's wheelhouse — exactly the type of work we win in this market.");
        AppendBullet(body, "Live in active procurement — actionable window for an architect intro.");

        AppendHeading(body, "KOR's angle (relationship intelligence)");
        if (data.OwnerKorProjectsCount > 0)
        {
            AppendBullet(body, $"Owner relationship: KOR has {data.OwnerKorProjectsCount} prior project(s) with {data.BuyerName} — warm-call territory; reference the relationship directly.");
        }
        else
        {
            AppendBullet(body, $"Owner relationship: no prior KOR project with {data.BuyerName} on file — this is a new-owner pursuit; lean on the architect path.");
        }
        if (data.OwnerPipelineProjectCount > 0)
        {
            AppendBullet(body, $"We track {data.OwnerPipelineProjectCount} project(s) for this owner in our pipeline data — we know their procurement cadence.");
        }
        if (!string.IsNullOrWhiteSpace(data.LikelyArchitectName))
        {
            if (data.KorArchitectJointProjectCount > 0)
            {
                AppendBullet(body, $"Likely prime consultant: {data.LikelyArchitectName} — has done {data.LikelyArchitectOwnerProjectCount} project(s) for this owner, and KOR has teamed with them on {data.KorArchitectJointProjectCount} past project(s). The warmest path in.");
            }
            else
            {
                AppendBullet(body, $"Likely prime consultant: {data.LikelyArchitectName} — has done {data.LikelyArchitectOwnerProjectCount} project(s) for this owner. KOR has not yet formally teamed with them; introduce the structural offering this week.");
            }
        }
        else
        {
            AppendBullet(body, "Likely prime consultant: not yet identified in our data — research the architect on this owner's recent builds before the deadline.");
        }

        AppendHeading(body, "Get in front of them this week");
        if (data.MatchedEvent is { } ev)
        {
            var date = ev.StartDate.HasValue ? ev.StartDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "(date TBC)";
            AppendBullet(body, $"{ev.Name} — {date}, {Nz(ev.City)} ({Nz(ev.Market)}).");
            if (!string.IsNullOrWhiteSpace(ev.SectorsThemes))
            {
                AppendBullet(body, $"Why this event: themes ({ev.SectorsThemes}) line up with the pursuit's sector; audience: {Nz(ev.Audience)}.");
            }
            if (!string.IsNullOrWhiteSpace(ev.TargetsPresent))
            {
                AppendBullet(body, $"Targets expected in the room: {ev.TargetsPresent}.");
            }
            if (!string.IsNullOrWhiteSpace(ev.RegistrationUrl))
            {
                AppendBullet(body, $"Register / details: {ev.RegistrationUrl}");
            }
        }
        else
        {
            AppendBullet(body, "No directly-matching upcoming event found — go direct architect outreach instead.");
        }

        AppendHeading(body, "Recommended next steps");
        if (!string.IsNullOrWhiteSpace(data.LikelyArchitectName))
        {
            AppendBullet(body, $"Reach out to {data.LikelyArchitectName} this week with KOR's relevant capability brief; propose teaming on the live RFP.");
        }
        AppendBullet(body, "If attending the event, brief the team beforehand and target specific introductions in the room.");
        AppendBullet(body, "Confirm owner's procurement timeline + submission requirements; align KOR resourcing for the deadline.");

        if (data.Intel?.BuyerIntel is not null)
        {
            AppendHeading(body, $"About the buyer ({data.BuyerName ?? "buyer"})");
            AppendOpportunityBuyerIntel(body, data.Intel.BuyerIntel);
        }
        if (data.Intel?.ArchitectIntel is not null && !string.IsNullOrWhiteSpace(data.LikelyArchitectName))
        {
            AppendHeading(body, $"About the likely architect ({data.LikelyArchitectName})");
            AppendOpportunityArchitectIntel(body, data.Intel.ArchitectIntel);
        }

        AppendFooter(body);
    }

    public void WriteSectorBrief(SectorBriefData data, string outputPath)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is required.", nameof(outputPath));

        EnsureDirectory(outputPath);
        using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        var body = main.Document.Body!;

        AppendTitle(body, "Sector Brief");
        AppendSubtitle(body, "Slice across the forward pipeline, live RFPs, and recent awards");
        AppendHeading(body, SectorHeaderTitle(data.Request));

        AppendLabelValueTable(body, new List<(string Label, string Value)>
        {
            ("Live RFPs", data.Counts.LiveRfpCount.ToString(CultureInfo.InvariantCulture)),
            ("Forward pipeline", data.Counts.ForwardPipelineCount.ToString(CultureInfo.InvariantCulture)),
            ("Recent awards", data.Counts.RecentAwardCount.ToString(CultureInfo.InvariantCulture)),
            ("Pipeline $",
                data.Counts.TotalForwardPipelineCostCad is { } v
                    ? v.ToString("C0", CultureInfo.CurrentCulture)
                    : ""),
        });

        AppendHeading(body, "Filter criteria");
        foreach (var line in SectorFilterBulletsForWord(data))
        {
            AppendBullet(body, line);
        }

        if (data.LiveRfps.Count > 0)
        {
            AppendHeading(body, "Live RFPs in this slice");
            foreach (var line in SectorLiveRfpBullets(data))
            {
                AppendBullet(body, line);
            }
        }

        if (data.ForwardProjects.Count > 0)
        {
            AppendHeading(body, "Forward pipeline in this slice");
            foreach (var line in SectorForwardProjectBullets(data))
            {
                AppendBullet(body, line);
            }
        }

        if (data.RecentAwards.Count > 0)
        {
            AppendHeading(body, "Recent awards in this slice (last 12 months)");
            foreach (var line in SectorRecentAwardBullets(data))
            {
                AppendBullet(body, line);
            }
        }

        if (data.TopArchitects.Count > 0)
        {
            AppendHeading(body, "Top architects active in this slice");
            foreach (var line in SectorTopOrgBullets(data.TopArchitects))
            {
                AppendBullet(body, line);
            }
        }

        if (data.TopOwners.Count > 0)
        {
            AppendHeading(body, "Top owners commissioning in this slice");
            foreach (var line in SectorTopOrgBullets(data.TopOwners))
            {
                AppendBullet(body, line);
            }
        }

        if (data.TopGcs.Count > 0)
        {
            AppendHeading(body, "Top GCs in this slice");
            foreach (var line in SectorTopOrgBullets(data.TopGcs))
            {
                AppendBullet(body, line);
            }
        }

        if (data.TopStructuralCompetitors.Count > 0)
        {
            AppendHeading(body, "Top structural competitors in this slice");
            foreach (var line in SectorTopOrgBullets(data.TopStructuralCompetitors))
            {
                AppendBullet(body, line);
            }
        }

        if (data.KorPortfolio.Count > 0)
        {
            AppendHeading(body, "KOR's own track record in this slice");
            foreach (var line in SectorKorPortfolioBullets(data))
            {
                AppendBullet(body, line);
            }
        }

        if (data.RelevantSignals.Count > 0)
        {
            AppendHeading(body, "Relevant Intel signals");
            foreach (var line in SectorIntelSignalBullets(data))
            {
                AppendBullet(body, line);
            }
        }

        AppendFooter(body);
    }

    // Tiny dup of the PDF helper so Word doesn't reach into a private
    // static on the PDF class. Same logic, identical strings.
    private static string SectorHeaderTitle(SectorBriefRequest r)
    {
        var geo = string.IsNullOrWhiteSpace(r.City) ? r.Province : $"{r.City}, {r.Province}";
        string sectorBit;
        if (r.SectorBuckets.Count == 0 && r.StructuralTypes.Count == 0 && string.IsNullOrWhiteSpace(r.ExtraKeyword))
        {
            sectorBit = "All sectors";
        }
        else
        {
            var bits = new List<string>();
            if (r.SectorBuckets.Count > 0) bits.Add(string.Join(" + ", r.SectorBuckets));
            if (r.StructuralTypes.Count > 0) bits.Add(string.Join(" + ", r.StructuralTypes));
            if (!string.IsNullOrWhiteSpace(r.ExtraKeyword)) bits.Add($"{r.ExtraKeyword}");
            sectorBit = string.Join(" / ", bits);
        }
        if (r.IndigenousOnly)
        {
            var nation = string.IsNullOrWhiteSpace(r.IndigenousNationFilter)
                ? "Indigenous"
                : $"Indigenous ({r.IndigenousNationFilter})";
            sectorBit = sectorBit + "    " + nation;
        }
        return sectorBit + "  " + geo;
    }

    private static IEnumerable<string> SectorFilterBulletsForWord(SectorBriefData d)
    {
        var r = d.Request;
        yield return $"Geography: {(string.IsNullOrWhiteSpace(r.City) ? r.Province : r.City + ", " + r.Province)}";
        if (r.SectorBuckets.Count > 0)
            yield return $"Sectors: {string.Join(", ", r.SectorBuckets)}";
        if (r.StructuralTypes.Count > 0)
            yield return $"Structural types: {string.Join(", ", r.StructuralTypes)}";
        if (r.IndigenousOnly)
            yield return r.IndigenousNationFilter is { Length: > 0 } n
                ? $"Indigenous lens, nation filter: {n}"
                : "Indigenous lens (all nations)";
        if (!string.IsNullOrWhiteSpace(r.ExtraKeyword))
            yield return $"Extra keyword: \"{r.ExtraKeyword}\"";
    }

    private static IEnumerable<string> SectorLiveRfpBullets(SectorBriefData d)
    {
        foreach (var r in d.LiveRfps)
        {
            var deadline = r.SubmissionDeadlineUtc?.ToString("yyyy-MM-dd",
                CultureInfo.InvariantCulture) ?? "no deadline";
            var sector = string.IsNullOrWhiteSpace(r.PrimeProjectSector) ? "" : $"  {r.PrimeProjectSector}";
            yield return $"{r.Name} ({r.BuyerName})  submit by {deadline}{sector}";
        }
    }

    private static IEnumerable<string> SectorForwardProjectBullets(SectorBriefData d)
    {
        foreach (var p in d.ForwardProjects)
        {
            var v = p.EstimatedCostCad is { } cad
                ? cad.ToString("C0", CultureInfo.CurrentCulture)
                : "";
            var prop = string.IsNullOrWhiteSpace(p.ProponentName) ? "" : $"  {p.ProponentName}";
            var stage = string.IsNullOrWhiteSpace(p.Stage) ? "" : $" [{p.Stage}]";
            yield return $"{p.ProjectName}{prop}{stage}  {v}";
        }
    }

    private static IEnumerable<string> SectorRecentAwardBullets(SectorBriefData d)
    {
        foreach (var a in d.RecentAwards)
        {
            var v = a.AwardedValueCad is { } cad
                ? cad.ToString("C0", CultureInfo.CurrentCulture)
                : "";
            var when = a.AwardedAtUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
            yield return $"{a.ProjectName} ({a.BuyerName})  {a.AwardedToName ?? "(no winner)"}  {v} on {when}";
        }
    }

    private static IEnumerable<string> SectorTopOrgBullets(IReadOnlyList<SectorTopOrg> orgs)
    {
        foreach (var o in orgs)
        {
            var joint = o.KorJointCount > 0
                ? $"  {o.KorJointCount} joint with KOR"
                : "";
            yield return $"{o.DisplayName} ({o.Kind})  {o.ProjectCount} project{(o.ProjectCount == 1 ? "" : "s")}{joint}";
        }
    }

    private static IEnumerable<string> SectorKorPortfolioBullets(SectorBriefData d)
    {
        foreach (var k in d.KorPortfolio)
        {
            var v = k.EstimatedCostCad is { } cad
                ? cad.ToString("C0", CultureInfo.CurrentCulture)
                : "";
            var city = string.IsNullOrWhiteSpace(k.City) ? "" : $"  {k.City}";
            var stage = string.IsNullOrWhiteSpace(k.Stage) ? "" : $" [{k.Stage}]";
            yield return $"{k.ProjectName}{city}{stage}  {v}";
        }
    }

    private static IEnumerable<string> SectorIntelSignalBullets(SectorBriefData d)
    {
        foreach (var s in d.RelevantSignals)
        {
            var when = s.OccurredAtApprox
                ?? s.LastSeenAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            yield return $"{when}  {s.SignalType}: {s.Subject} (via {s.OrgDisplayName})";
        }
    }

    public void WriteProjectBrief(ProjectBriefData data, string outputPath)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path is required.", nameof(outputPath));

        EnsureDirectory(outputPath);
        using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        var body = main.Document.Body!;

        AppendTitle(body, "Project Brief");
        AppendSubtitle(body, "Pursuit prep for one forward-pipeline project");
        AppendHeading(body, data.ProjectName);
        AppendLabelValueTable(body, new List<(string Label, string Value)>
        {
            ("Stage", Nz(data.Stage)),
            ("Location", ProjectLocation(data)),
            ("Sector", ProjectSector(data)),
            ("Estimated value", FormatProjectValue(data)),
        });

        var description = !string.IsNullOrWhiteSpace(data.IntelDescription)
            ? data.IntelDescription!
            : (string.IsNullOrWhiteSpace(data.ProjectDescription)
                ? "No project description on file yet."
                : data.ProjectDescription!);
        AppendHeading(body, "Project description");
        AppendBody(body, description);

        AppendHeading(body, "Schedule");
        AppendBody(body, $"{data.StartYear?.ToString(CultureInfo.InvariantCulture) ?? "?"} - {data.CompletionYear?.ToString(CultureInfo.InvariantCulture) ?? "?"}");
        if (!string.IsNullOrWhiteSpace(data.ScheduleNotes))
        {
            AppendBody(body, data.ScheduleNotes!);
        }

        if (!string.IsNullOrWhiteSpace(data.IntelStatus))
        {
            AppendHeading(body, "Status");
            AppendBody(body, data.IntelStatus!);
        }

        AppendHeading(body, "Team & KOR angle");
        AppendLinkedOrgBlock(body, "Proponent", data.ProponentName, data.ProponentSummary, structuralCallout: false);
        AppendLinkedOrgBlock(body, "Architect", data.ArchitectName, data.ArchitectSummary, structuralCallout: false);
        AppendLinkedOrgBlock(body, "Structural Engineer", data.StructuralEngineerName, data.StructuralSummary, structuralCallout: true);
        AppendLinkedOrgBlock(body, "GC", data.GeneralContractorName, data.GeneralContractorSummary, structuralCallout: false);

        if (!string.IsNullOrWhiteSpace(data.IntelKorAngle))
        {
            AppendHeading(body, "KOR angle");
            AppendBody(body, data.IntelKorAngle!);
        }

        if (data.MentionsInWorkHistory.Count > 0)
        {
            AppendHeading(body, "On other portfolios");
            foreach (var m in data.MentionsInWorkHistory)
            {
                var bits = new List<string>();
                if (!string.IsNullOrWhiteSpace(m.Role)) bits.Add(m.Role);
                if (!string.IsNullOrWhiteSpace(m.YearApprox)) bits.Add(m.YearApprox);
                var meta = bits.Count == 0 ? "" : "  " + string.Join(", ", bits);
                AppendBullet(body, $"{m.OrgDisplayName} ({m.Kind}){meta}");
            }
        }

        if (data.RecentMentionSignals.Count > 0)
        {
            AppendHeading(body, "Recent signals mentioning this project");
            foreach (var s in data.RecentMentionSignals)
            {
                var when = s.OccurredAtApprox ?? s.LastSeenAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                AppendBullet(body, $"{when}  {s.SignalType}: {s.Subject} (via {s.OrgDisplayName})");
            }
        }

        if (data.OpenActionsMentioning.Count > 0)
        {
            AppendHeading(body, "KOR open actions mentioning this project");
            foreach (var a in data.OpenActionsMentioning)
            {
                var text = $"{a.ActionType}: {a.Recommendation}";
                if (!string.IsNullOrWhiteSpace(a.TimingNotes))
                {
                    text += " [" + a.TimingNotes + "]";
                }
                text += $" (via {a.OrgDisplayName})";
                AppendBullet(body, text);
            }
        }

        if (data.IntelKeyPeople.Count > 0)
        {
            AppendHeading(body, "Key people on this project");
            foreach (var p in data.IntelKeyPeople)
            {
                AppendBullet(body, $"{p.DisplayName} ({p.Side})  {Nz(p.Title)}{(p.OrgName is null ? "" : "  " + p.OrgName)}");
            }
        }

        if (data.IntelSignals.Count > 0)
        {
            AppendHeading(body, "Project signals");
            foreach (var s in data.IntelSignals)
            {
                var when = s.OccurredAtApprox ?? s.LastSeenAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                AppendBullet(body, $"{when}  {s.SignalType}: {s.Subject}");
            }
        }

        if (data.IntelActions.Count > 0)
        {
            AppendHeading(body, "KOR actions on this project");
            foreach (var a in data.IntelActions)
            {
                var text = $"{a.ActionType}: {a.Recommendation}";
                var targets = new List<string>();
                if (!string.IsNullOrWhiteSpace(a.TargetPersonName)) targets.Add(a.TargetPersonName!);
                if (!string.IsNullOrWhiteSpace(a.TargetOrgName)) targets.Add(a.TargetOrgName!);
                if (targets.Count > 0) text += " (target: " + string.Join(" / ", targets) + ")";
                if (!string.IsNullOrWhiteSpace(a.TimingNotes)) text += " [" + a.TimingNotes + "]";
                AppendBullet(body, text);
            }
        }

        if (data.IntelRisks.Count > 0)
        {
            AppendHeading(body, "Project risks");
            foreach (var r in data.IntelRisks)
            {
                AppendBullet(body, $"{r.RiskType}: {r.Description}{(r.MitigationNotes is null ? "" : "  mitigation: " + r.MitigationNotes)}");
            }
        }

        AppendHeading(body, "Source");
        if (string.IsNullOrWhiteSpace(data.SourceUrl))
        {
            AppendBody(body, "No source URL on file.");
        }
        else
        {
            AppendHyperlinkBody(main, body, data.SourceUrl!);
        }

        AppendFooter(body);
    }

    public void WritePersonBrief(PersonBriefData data, string outputPath)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path is required.", nameof(outputPath));

        EnsureDirectory(outputPath);
        using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        var body = main.Document.Body!;

        AppendTitle(body, "Person Brief");
        AppendSubtitle(body, "Contact prep for one named individual");
        AppendHeading(body, data.DisplayName);
        AppendBody(body, $"Current title: {Nz(data.CurrentTitle)}");
        AppendBody(body, $"Current employer: {Nz(data.CurrentEmployerName)}");
        AppendBody(body, "Last seen: " + (data.LastSeenAtUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "not recorded"));

        if (!string.IsNullOrWhiteSpace(data.Email)
            || !string.IsNullOrWhiteSpace(data.Phone)
            || !string.IsNullOrWhiteSpace(data.LinkedinUrl))
        {
            AppendHeading(body, "Contact");
            if (!string.IsNullOrWhiteSpace(data.Email)) AppendBody(body, data.Email!);
            if (!string.IsNullOrWhiteSpace(data.Phone)) AppendBody(body, data.Phone!);
            if (!string.IsNullOrWhiteSpace(data.LinkedinUrl)) AppendHyperlinkBody(main, body, data.LinkedinUrl!);
        }

        if (!string.IsNullOrWhiteSpace(data.Notes))
        {
            AppendHeading(body, "Notes");
            AppendBody(body, data.Notes!);
        }

        if (data.CurrentAffiliations.Count > 0)
        {
            AppendHeading(body, "Current roles");
            AppendTable(body,
                new[] { "Org", "Title", "Department" },
                data.CurrentAffiliations
                    .Select(a => (IReadOnlyList<string>)new[] { a.OrgName, Nz(a.Title), Nz(a.Department) })
                    .ToList());
        }

        if (data.FormerAffiliations.Count > 0)
        {
            AppendHeading(body, "Career history");
            AppendTable(body,
                new[] { "Org", "Title", "End" },
                data.FormerAffiliations
                    .Select(a => (IReadOnlyList<string>)new[] { a.OrgName, Nz(a.Title), Nz(a.EndDateApprox) })
                    .ToList());
        }

        if (data.RecentSignals.Count > 0)
        {
            AppendHeading(body, "Recent activity");
            foreach (var s in data.RecentSignals)
            {
                AppendBullet(body, $"{s.OccurredAtApprox ?? "?"}  {s.SignalType}: {s.Subject} (via {s.OrgName})");
            }
        }

        if (data.OpenActions.Count > 0)
        {
            AppendHeading(body, "KOR open actions targeting this person");
            foreach (var a in data.OpenActions)
            {
                var text = $"{a.ActionType}: {a.Recommendation}";
                if (!string.IsNullOrWhiteSpace(a.TimingNotes))
                {
                    text += " [" + a.TimingNotes + "]";
                }
                text += $" (re {a.OrgName})";
                AppendBullet(body, text);
            }
        }

        AppendFooter(body);
    }

    public void WriteRegionBrief(RegionBriefData data, string outputPath)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path is required.", nameof(outputPath));

        EnsureDirectory(outputPath);
        using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        var body = main.Document.Body!;

        var scope = string.IsNullOrWhiteSpace(data.City) ? data.Province : $"{data.Province} — {data.City}";
        AppendTitle(body, "Region Brief");
        AppendSubtitle(body, scope);

        AppendHeading(body, "Market overview");
        AppendBody(body, $"Live prime-consultant RFPs: {data.LivePrimeRfpCount}  |  Forward pipeline (planned/funded): {data.ForwardPipelineCount}  |  Active major projects tracked: {data.ActiveMpiCount}");

        AppendHeading(body, "Top architects in this market");
        if (data.TopArchitects.Count == 0)
        {
            AppendBullet(body, "No architects tied to projects in this market in our data yet.");
        }
        else
        {
            foreach (var a in data.TopArchitects)
            {
                var korNote = a.KorJointCount > 0 ? $"; KOR has teamed with them {a.KorJointCount}x" : "; no KOR joint history yet";
                AppendBullet(body, $"{RegionOrgDisplayName(a)} — {a.ProjectCount} project(s) here{korNote}.");
            }
        }

        AppendHeading(body, "Top owners / clients in this market");
        if (data.TopOwners.Count == 0)
        {
            AppendBullet(body, "No owners tied to projects in this market in our data yet.");
        }
        else
        {
            foreach (var o in data.TopOwners)
            {
                var korNote = o.KorJointCount > 0 ? $"; {o.KorJointCount} KOR project(s) on record" : "; no prior KOR project on file";
                AppendBullet(body, $"{RegionOrgDisplayName(o)} — {o.ProjectCount} project(s) here{korNote}.");
            }
        }

        AppendHeading(body, "Top competitors in this market (structural)");
        if (data.TopCompetitors.Count == 0)
        {
            AppendBullet(body, "No competitors flagged on projects in this market in our data yet.");
        }
        else
        {
            foreach (var c in data.TopCompetitors)
            {
                AppendBullet(body, $"{RegionOrgDisplayName(c)} — {c.ProjectCount} project(s) as structural EOR here.");
            }
        }

        AppendHeading(body, "Live prime RFPs (top 5)");
        if (data.LiveRfps.Count == 0)
        {
            AppendBullet(body, "No live prime-consultant RFPs in this market right now.");
        }
        else
        {
            foreach (var r in data.LiveRfps)
            {
                AppendBullet(body, $"{r.Name} — {r.BuyerName} ({Nz(r.PrimeProjectSector)}) — deadline {FormatDeadline(r.SubmissionDeadlineUtc)}.");
            }
        }

        AppendHeading(body, "Forward pipeline (planned / funded)");
        if (data.ForwardProjects.Count == 0)
        {
            AppendBullet(body, "No forward-pipeline projects in this market in our data yet.");
        }
        else
        {
            foreach (var p in data.ForwardProjects)
            {
                AppendBullet(body, $"{p.ProjectName} — {Nz(p.ProponentName)} ({Nz(p.Stage)}) — {FormatValue(p.EstimatedCostCad)}.");
            }
        }

        AppendHeading(body, "Upcoming events in this market");
        if (data.UpcomingEvents.Count == 0)
        {
            AppendBullet(body, "No matching upcoming events in our database.");
        }
        else
        {
            foreach (var e in data.UpcomingEvents)
            {
                var date = e.StartDate.HasValue ? e.StartDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "(date TBC)";
                AppendBullet(body, $"{e.Name} — {date} — {Nz(e.City)}.");
            }
        }

        if (data.Intel is not null)
        {
            AppendHeading(body, "Cross-org actionables in this region");
            if (data.Intel.TopActions.Count == 0)
            {
                AppendBullet(body, "No cross-org actionables on file yet.");
            }
            else
            {
                foreach (var a in data.Intel.TopActions)
                {
                    var text = HumanizeActionType(a.ActionType) + ": " + a.Recommendation;
                    if (!string.IsNullOrWhiteSpace(a.TargetPersonName))
                    {
                        text += " (target: " + a.TargetPersonName + ")";
                    }
                    if (!string.IsNullOrWhiteSpace(a.TimingNotes))
                    {
                        text += "  Timing: " + a.TimingNotes;
                    }

                    AppendBullet(body, FormatIntelBullet(text, a.Confidence, a.Freshness, a.RefreshedAtUtc));
                }
            }

            if (data.Intel.RecentLeadershipChanges.Count > 0)
            {
                AppendHeading(body, "Recent leadership changes in this region (last 90 days)");
                foreach (var s in data.Intel.RecentLeadershipChanges)
                {
                    var text = s.Subject;
                    if (!string.IsNullOrWhiteSpace(s.Detail))
                    {
                        text += "  " + s.Detail;
                    }
                    if (!string.IsNullOrWhiteSpace(s.OccurredAtApprox))
                    {
                        text += " [" + s.OccurredAtApprox + "]";
                    }

                    AppendBullet(body, FormatIntelBullet(text, s.Confidence, s.Freshness, s.RefreshedAtUtc));
                }
            }

            if (data.Intel.TopCapacityRisks.Count > 0)
            {
                AppendHeading(body, "Capacity-strain signals (competitor displacement opportunities)");
                foreach (var r in data.Intel.TopCapacityRisks)
                {
                    var text = $"{r.OrgDisplayName}: {r.Description}";
                    if (!string.IsNullOrWhiteSpace(r.MitigationNotes))
                    {
                        text += " (Mitigation: " + r.MitigationNotes + ")";
                    }

                    AppendBullet(body, FormatIntelBullet(text, r.Confidence, r.Freshness, r.RefreshedAtUtc));
                }
            }
        }

        AppendFooter(body);
    }

    public void WriteOrgBrief(OrgBriefData data, string outputPath)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path is required.", nameof(outputPath));

        EnsureDirectory(outputPath);
        using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        var body = main.Document.Body!;

        AppendTitle(body, "Organization Brief");
        AppendSubtitle(body, $"{data.DisplayName}  ({data.Kind})");
        if (!string.IsNullOrWhiteSpace(data.ResolvedFromNote))
        {
            AppendItalicBody(body, data.ResolvedFromNote!);
        }

        var enrichment = ParseEnrichment(data.DataHoningEnrichmentJson);
        var websiteText = string.IsNullOrWhiteSpace(data.Website) ? "(not on file)" : data.Website!;
        AppendBody(body, $"Website: {websiteText}");
        if (!string.IsNullOrWhiteSpace(enrichment.HqCity))
        {
            AppendBody(body, $"HQ / primary city: {enrichment.HqCity}");
        }
        if (enrichment.Sectors.Count > 0)
        {
            AppendBody(body, $"Sectors they work in: {string.Join(", ", enrichment.Sectors)}");
        }

        if (!string.IsNullOrWhiteSpace(data.Intel?.SynopsisParagraph1))
        {
            AppendItalicBody(body, data.Intel.SynopsisParagraph1!);
        }
        if (!string.IsNullOrWhiteSpace(data.Intel?.SynopsisParagraph2))
        {
            AppendItalicBody(body, data.Intel.SynopsisParagraph2!);
        }

        AppendHeading(body, "KOR's history with this organization");
        if (data.KorProjectsCount > 0)
        {
            var lastNote = data.LastKorProjectAtUtc.HasValue
                ? $" (last engagement: {data.LastKorProjectAtUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})"
                : string.Empty;
            AppendBullet(body, $"Deltek-tracked KOR projects with this org: {data.KorProjectsCount}{lastNote} — warm-call territory.");
        }
        else
        {
            AppendBullet(body, "No Deltek-tracked KOR project with this org on file — this is a cold/new relationship; open with the capability brief.");
        }
        if (data.KorJointProjectCount > 0)
        {
            AppendBullet(body, $"Joint major-project record: KOR has been the structural EOR on {data.KorJointProjectCount} project(s) where this org appears as architect/owner/GC.");
            foreach (var p in data.KorJointProjects)
            {
                AppendBullet(body, $"   • {p.ProjectName} ({p.CompletionYear?.ToString(CultureInfo.InvariantCulture) ?? "year n/a"}, {Nz(p.Sector)}, {Nz(p.Province)})");
            }
        }
        else
        {
            AppendBullet(body, "No joint major-project record where KOR was the structural EOR with this org — there's an opening to be on their next team.");
        }

        AppendHeading(body, "Recommended actions");
        if (data.Intel is null || data.Intel.Actions.Count == 0)
        {
            AppendBullet(body, "No recommended actions on file yet.");
        }
        else
        {
            foreach (var a in data.Intel.Actions)
            {
                var text = HumanizeActionType(a.ActionType) + ": " + a.Recommendation;
                if (!string.IsNullOrWhiteSpace(a.TargetPersonName))
                {
                    text += " (target: " + a.TargetPersonName + ")";
                }
                if (!string.IsNullOrWhiteSpace(a.TimingNotes))
                {
                    text += "  Timing: " + a.TimingNotes;
                }

                AppendBullet(body, FormatIntelBullet(text, a.Confidence, a.Freshness, a.RefreshedAtUtc));
            }
        }

        AppendHeading(body, "Key people on file");
        if (data.Intel is null || data.Intel.People.Count == 0)
        {
            AppendBullet(body, "No key people on file yet.");
        }
        else
        {
            foreach (var p in data.Intel.People)
            {
                var text = (!p.IsCurrent ? "(former) " : string.Empty) + p.DisplayName;
                if (!string.IsNullOrWhiteSpace(p.Title))
                {
                    text += "  " + p.Title;
                }

                AppendBullet(body, FormatIntelBullet(text, p.Confidence, p.Freshness, p.RefreshedAtUtc));
            }
        }

        AppendHeading(body, "Recent signals");
        if (data.Intel is null || data.Intel.Signals.Count == 0)
        {
            AppendBullet(body, "No recent signals tracked.");
        }
        else
        {
            foreach (var s in data.Intel.Signals)
            {
                var text = HumanizeSignalType(s.SignalType) + ": " + s.Subject;
                if (!string.IsNullOrWhiteSpace(s.Detail))
                {
                    text += "  " + s.Detail;
                }

                AppendBullet(body, FormatIntelBullet(text, s.Confidence, s.Freshness, s.RefreshedAtUtc));
            }
        }

        AppendHeading(body, "Their recent work");
        if (data.RecentProjects.Count == 0)
        {
            AppendBullet(body, "No recent major projects in our pipeline data for this org.");
        }
        else
        {
            foreach (var p in data.RecentProjects)
            {
                AppendBullet(body, $"{p.ProjectName} ({p.CompletionYear?.ToString(CultureInfo.InvariantCulture) ?? "year n/a"}, {Nz(p.Sector)}, {Nz(p.Province)})");
            }
        }

        if (data.Intel is { Works.Count: > 0 })
        {
            AppendHeading(body, "Their portfolio (from research)");
            foreach (var w in data.Intel.Works)
            {
                var text = w.ProjectName;
                if (!string.IsNullOrWhiteSpace(w.Role))
                {
                    text += "  " + w.Role;
                }
                if (!string.IsNullOrWhiteSpace(w.YearApprox))
                {
                    text += " (" + w.YearApprox + ")";
                }

                AppendBullet(body, FormatIntelBullet(text, w.Confidence, w.Freshness, w.RefreshedAtUtc));
            }
        }

        if (data.Deltek is { } dk)
        {
            AppendOrgDeltekSection(body, dk);
        }
        else if (!string.IsNullOrWhiteSpace(data.DeltekNote))
        {
            AppendHeading(body, "KOR engagement history (Deltek)");
            AppendBullet(body, data.DeltekNote!);
        }

        if (data.Intel is { Risks.Count: > 0 })
        {
            AppendHeading(body, "Risks / vulnerabilities");
            foreach (var r in data.Intel.Risks)
            {
                var text = HumanizeRiskType(r.RiskType) + ": " + r.Description;
                if (!string.IsNullOrWhiteSpace(r.MitigationNotes))
                {
                    text += " (Mitigation: " + r.MitigationNotes + ")";
                }

                AppendBullet(body, FormatIntelBullet(text, r.Confidence, r.Freshness, r.RefreshedAtUtc));
            }
        }

        AppendFooter(body);
    }

    // === OpenXML helpers ===

    private static void AppendTitle(Body body, string text)
    {
        body.AppendChild(BuildPara(text, TitlePt, bold: true, italic: false, JustificationValues.Center, bullet: false));
        body.AppendChild(new Paragraph());
    }

    private static void AppendSubtitle(Body body, string text)
    {
        body.AppendChild(BuildPara(text, SubtitlePt, bold: false, italic: true, JustificationValues.Center, bullet: false));
        body.AppendChild(new Paragraph());
    }

    private static void AppendHeading(Body body, string text)
    {
        body.AppendChild(new Paragraph());
        body.AppendChild(BuildPara(text, HeadingPt, bold: true, italic: false, align: null, bullet: false));
        AppendAccentRule(body);
    }

    private static void AppendBody(Body body, string text)
    {
        body.AppendChild(BuildPara(text, BodyPt, bold: false, italic: false, align: null, bullet: false));
    }

    private static void AppendItalicBody(Body body, string text)
    {
        body.AppendChild(BuildPara(text, BodyPt, bold: false, italic: true, align: null, bullet: false));
    }

    private static void AppendHyperlinkBody(MainDocumentPart main, Body body, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            AppendBody(body, url);
            return;
        }

        var rel = main.AddHyperlinkRelationship(uri, isExternal: true);
        var hyperlink = new Hyperlink(
            new Run(
                new RunProperties(
                    new FontSize { Val = BodyPt.ToString(CultureInfo.InvariantCulture) },
                    new Color { Val = BrandAccent },
                    new Underline { Val = UnderlineValues.Single }),
                new Text(url) { Space = SpaceProcessingModeValues.Preserve }))
        {
            Id = rel.Id,
            History = true,
        };

        body.AppendChild(new Paragraph(hyperlink));
    }

    private static void AppendOpportunityBuyerIntel(Body body, OrgIntelBundle intel)
    {
        if (intel.Actions.Count + intel.People.Count + intel.Signals.Count == 0)
        {
            AppendBullet(body, "No intel on file for this buyer.");
            return;
        }

        foreach (var a in intel.Actions)
        {
            AppendBullet(body, FormatIntelBullet(FormatActionBody(a), a.Confidence, a.Freshness, a.RefreshedAtUtc));
        }
        foreach (var p in intel.People)
        {
            AppendBullet(body, FormatIntelBullet(FormatPersonBody(p), p.Confidence, p.Freshness, p.RefreshedAtUtc));
        }
        foreach (var s in intel.Signals)
        {
            AppendBullet(body, FormatIntelBullet(FormatSignalBody(s), s.Confidence, s.Freshness, s.RefreshedAtUtc));
        }
    }

    private static void AppendOpportunityArchitectIntel(Body body, OrgIntelBundle intel)
    {
        if (intel.Actions.Count + intel.Risks.Count + intel.Signals.Count == 0)
        {
            AppendBullet(body, "No intel on file for this architect yet  likely a cold lead.");
            return;
        }

        foreach (var a in intel.Actions)
        {
            AppendBullet(body, FormatIntelBullet(FormatActionBody(a), a.Confidence, a.Freshness, a.RefreshedAtUtc));
        }
        foreach (var r in intel.Risks)
        {
            AppendBullet(body, FormatIntelBullet(FormatRiskBody(r), r.Confidence, r.Freshness, r.RefreshedAtUtc));
        }
        foreach (var s in intel.Signals)
        {
            AppendBullet(body, FormatIntelBullet(FormatSignalBody(s), s.Confidence, s.Freshness, s.RefreshedAtUtc));
        }
    }

    private static void AppendLinkedOrgBlock(
        Body body,
        string roleLabel,
        string? sourceName,
        LinkedOrgSummary? summary,
        bool structuralCallout)
    {
        if (summary is null)
        {
            AppendBullet(body, $"{roleLabel}: {Nz(sourceName)} — Not on file.");
            if (structuralCallout && !string.IsNullOrWhiteSpace(sourceName))
            {
                AppendItalicBody(body, $"KOR's competitor on this pursuit. Consider angle: {sourceName} vs KOR.");
            }

            return;
        }

        var refreshed = summary.LastRefreshAtUtc.HasValue
            ? summary.LastRefreshAtUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "not refreshed";
        AppendBullet(
            body,
            $"{roleLabel}: {summary.DisplayName} — {summary.IntelPersonCount} people, {summary.OpenActionCount} open actions, {summary.RecentSignalCount} recent signals; last refreshed {refreshed}.");

        if (structuralCallout)
        {
            AppendItalicBody(body, $"KOR's competitor on this pursuit. Consider angle: {summary.DisplayName} vs KOR.");
        }
    }

    private static void AppendBullet(Body body, string text)
    {
        body.AppendChild(BuildPara(text, BodyPt, bold: false, italic: false, align: null, bullet: true));
    }

    private static void AppendFooter(Body body)
    {
        body.AppendChild(new Paragraph());
        body.AppendChild(BuildPara(FooterText, FooterPt, bold: false, italic: true, JustificationValues.Center, bullet: false));
    }

    private static void AppendOrgDeltekSection(Body body, OrgBriefDeltekSection dk)
    {
        AppendHeading(body, "KOR engagement history (Deltek)");

        var facts = new List<(string Label, string Value)>
        {
            ("Client", $"{dk.DeltekClientId}  {Nz(dk.ClientName)}"),
        };
        if (dk.ProjectCount > 0)
        {
            facts.Add(("Lifetime billed",
                $"{dk.LifetimeFee.ToString("C0", CultureInfo.CurrentCulture)} across {dk.ProjectCount:N0} project(s)"));
        }
        if (dk.LatestProjectStart.HasValue)
        {
            var latest = string.IsNullOrWhiteSpace(dk.LatestProjectName) ? "latest engagement" : dk.LatestProjectName!;
            facts.Add(("Latest",
                $"{latest} (opened {dk.LatestProjectStart.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})"));
        }
        facts.Add(("Contacts", dk.ContactCount.ToString("N0", CultureInfo.InvariantCulture)));
        if (dk.ArOutstanding > 0)
        {
            facts.Add(("AR outstanding",
                $"{dk.ArOutstanding.ToString("C0", CultureInfo.CurrentCulture)} (90+ days: {dk.Ar90Plus.ToString("C0", CultureInfo.CurrentCulture)})"));
        }
        AppendLabelValueTable(body, facts);

        if (dk.RecentProjects.Count > 0)
        {
            AppendBody(body, "Recent projects");
            var rows = new List<IReadOnlyList<string>>();
            foreach (var p in dk.RecentProjects)
            {
                rows.Add(new[]
                {
                    p.Name,
                    p.OpenDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                    p.Status ?? "",
                    p.Fee > 0 ? p.Fee.ToString("C0", CultureInfo.CurrentCulture) : "",
                    p.FeeBilled > 0 ? p.FeeBilled.ToString("C0", CultureInfo.CurrentCulture) : "",
                });
            }
            AppendTable(body, new[] { "Project", "Opened", "Status", "Fee", "Billed" }, rows);
        }

        if (dk.DegradedSections)
        {
            body.AppendChild(BuildPara("Some Deltek sections were unavailable for this client.",
                BodyPt, bold: false, italic: true, align: null, bullet: false));
        }
    }

    private static void AppendLabelValueTable(Body body, IReadOnlyList<(string Label, string Value)> rows)
    {
        var table = CreateTable();
        foreach (var (label, value) in rows)
        {
            table.AppendChild(new TableRow(
                BuildCell(label, bold: true, widthDxa: "2200"),
                BuildCell(value, bold: false, widthDxa: "7200")));
        }
        body.AppendChild(table);
    }

    private static void AppendTable(Body body, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var table = CreateTable();
        var headerRow = new TableRow();
        foreach (var header in headers)
        {
            headerRow.AppendChild(BuildCell(header, bold: true, accentBottom: true));
        }
        table.AppendChild(headerRow);

        foreach (var row in rows)
        {
            var tableRow = new TableRow();
            foreach (var cell in row)
            {
                tableRow.AppendChild(BuildCell(cell, bold: false));
            }
            table.AppendChild(tableRow);
        }

        body.AppendChild(table);
    }

    private static Table CreateTable()
        => new(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "E5E7EB" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "E5E7EB" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "E5E7EB" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = "E5E7EB" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "E5E7EB" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "E5E7EB" })));

    private static TableCell BuildCell(string? text, bool bold, string? widthDxa = null, bool accentBottom = false)
    {
        var props = new TableCellProperties();
        if (!string.IsNullOrWhiteSpace(widthDxa))
        {
            props.AppendChild(new TableCellWidth { Width = widthDxa, Type = TableWidthUnitValues.Dxa });
        }
        if (accentBottom)
        {
            props.AppendChild(new TableCellBorders(
                new BottomBorder { Val = BorderValues.Single, Size = 6, Color = BrandAccent }));
        }

        return new TableCell(props, BuildPara(string.IsNullOrWhiteSpace(text) ? "—" : text!, BodyPt, bold, italic: false, align: null, bullet: false));
    }

    private static void AppendAccentRule(Body body)
    {
        var props = new ParagraphProperties(
            new ParagraphBorders(
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = BrandAccent }));
        body.AppendChild(new Paragraph(props, new Run(new Text(""))));
    }

    private static Paragraph BuildPara(string text, int sizeHalf, bool bold, bool italic, JustificationValues? align, bool bullet)
    {
        var pProps = new ParagraphProperties();
        if (align.HasValue)
        {
            pProps.AppendChild(new Justification { Val = align.Value });
        }
        if (bullet)
        {
            pProps.AppendChild(new Indentation { Left = "360" });
        }

        var rProps = new RunProperties();
        rProps.AppendChild(new FontSize { Val = sizeHalf.ToString(CultureInfo.InvariantCulture) });
        if (bold) rProps.AppendChild(new Bold());
        if (italic) rProps.AppendChild(new Italic());

        var paragraph = new Paragraph(pProps);
        if (bullet)
        {
            var bulletProps = new RunProperties(
                new FontSize { Val = sizeHalf.ToString(CultureInfo.InvariantCulture) },
                new Color { Val = BrandAccent });
            paragraph.AppendChild(new Run(bulletProps, new Text("•  ") { Space = SpaceProcessingModeValues.Preserve }));
            paragraph.AppendChild(new Run(rProps, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
            return paragraph;
        }

        paragraph.AppendChild(new Run(rProps, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        return paragraph;
    }

    // === Formatting + parsing helpers ===

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
    // (single source of truth shared with PDF brief and Dossier converter).
    private static string HumanizeSignalType(string type) =>
        Kor.Opportunities.Data.Intel.IntelTypeHumanizer.SignalType(type);

    private static string HumanizeRiskType(string type) =>
        Kor.Opportunities.Data.Intel.IntelTypeHumanizer.RiskType(type);

    private static string HumanizeActionType(string type) =>
        Kor.Opportunities.Data.Intel.IntelTypeHumanizer.ActionType(type);

    private static void EnsureDirectory(string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    // Parses the DataHoning enrichment JSON blob for header metadata only.
    // Defensive — returns empty enrichment on any malformed input.
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
        }
        catch
        {
            // Defensive — malformed enrichment shouldn't break the brief.
        }

        return enrichment;
    }

    private sealed class OrgEnrichment
    {
        public string? HqCity { get; set; }
        public List<string> Sectors { get; } = new();
    }
}
