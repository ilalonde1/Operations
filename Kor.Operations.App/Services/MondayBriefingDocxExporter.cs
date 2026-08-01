#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Kor.Operations.Services;

/// <summary>
/// Renders a KOR Monday Briefing to a .docx — narrative-style document
/// rather than a tabular spreadsheet. Reads better when printed or attached
/// to an email. The Excel export (MondayBriefingExporter) covers the
/// spreadsheet/filterable view; this is the briefing-memo view.
/// </summary>
internal sealed class MondayBriefingDocxExporter
{
    // Brand-ish colors. Kept simple — no logo, no header bar; the doc is
    // about content density and printability rather than marketing polish.
    private const string BrandSlate    = "1F3A5F";   // section / heading
    private const string AccentOrange  = "C2410C";   // recommendation accent + watch
    private const string MutedText     = "6B7280";   // metadata
    private const string SeverityHigh  = "C62828";
    private const string SeverityMed   = "EF6C00";
    private const string SeverityLow   = "1565C0";

    private const string Font = "Calibri";

    internal void Export(string path, IReadOnlyList<BriefDto> brief, IReadOnlyList<AlertDto> alerts)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        var body = new Body();
        main.Document = new Document(body);

        // 0.7" all-around page margins; default Letter size; reasonable for printing.
        var sectionProps = new SectionProperties(
            new PageSize { Width = 12240U, Height = 15840U }, // Letter, twips
            new PageMargin { Top = 1008, Bottom = 1008, Left = 1080, Right = 1080, Header = 720, Footer = 720, Gutter = 0 });

        AddTitleBlock(body, brief, alerts);
        AddStrategicBrief(body, brief);
        AddActionItems(body, alerts);

        body.Append(sectionProps);
        main.Document.Save();
    }

    // ── Title / subtitle ──────────────────────────────────────────────────

    private static void AddTitleBlock(Body body, IReadOnlyList<BriefDto> brief, IReadOnlyList<AlertDto> alerts)
    {
        var weekOf = brief.FirstOrDefault()?.weekOf ?? MostRecentMonday(DateTime.Today);
        var inputTokens  = brief.Sum(b => b.inputTokens);
        var outputTokens = brief.Sum(b => b.outputTokens);
        var unacked = alerts.Count(a => a.acknowledgedAt is null);

        body.Append(StyledParagraph(
            $"KOR Monday Briefing — Week of {weekOf:yyyy-MM-dd}",
            sizeHalfPt: 36, bold: true, colorHex: BrandSlate, alignCenter: true, spaceAfter: 120));

        body.Append(StyledParagraph(
            $"Generated {DateTime.Now:yyyy-MM-dd HH:mm} · {brief.Count} brief sections · {unacked} unacknowledged action items · " +
            $"{inputTokens:N0} input / {outputTokens:N0} output tokens",
            sizeHalfPt: 18, italic: true, colorHex: MutedText, alignCenter: true, spaceAfter: 360));

        // Subtle horizontal rule.
        body.Append(HorizontalRule());
    }

    // ── Strategic Brief ───────────────────────────────────────────────────

    private static void AddStrategicBrief(Body body, IReadOnlyList<BriefDto> brief)
    {
        body.Append(StyledParagraph(
            "STRATEGIC BRIEF",
            sizeHalfPt: 26, bold: true, colorHex: BrandSlate, spaceBefore: 360, spaceAfter: 240));

        if (brief.Count == 0)
        {
            body.Append(StyledParagraph(
                "No brief generated yet for this week. Run /coo-brief/run-now or wait for Monday's scheduled run.",
                sizeHalfPt: 22, italic: true, colorHex: MutedText));
            return;
        }

        foreach (var b in brief.OrderBy(b => SectionOrder(b.section)))
        {
            // Section heading (e.g. "Financial Health").
            body.Append(StyledParagraph(
                FriendlySectionName(b.section),
                sizeHalfPt: 22, bold: true, colorHex: BrandSlate, spaceBefore: 240, spaceAfter: 80));

            // Headline (bold, slightly larger).
            body.Append(StyledParagraph(
                b.headline,
                sizeHalfPt: 22, bold: true, spaceAfter: 120));

            // Body — main paragraph, justified for readability.
            body.Append(StyledParagraph(
                b.body,
                sizeHalfPt: 22, spaceAfter: 120, justify: true));

            // Recommendation in its own accented block, if present.
            if (!string.IsNullOrWhiteSpace(b.recommendation))
            {
                body.Append(StyledParagraph(
                    "RECOMMENDATION",
                    sizeHalfPt: 18, bold: true, colorHex: AccentOrange, spaceAfter: 40));
                body.Append(StyledParagraph(
                    b.recommendation!,
                    sizeHalfPt: 22, italic: false, spaceAfter: 200, leftIndentTwips: 360));
            }

            // Footer line: tokens + tool calls. Small + muted.
            body.Append(StyledParagraph(
                $"AI cost: {b.inputTokens:N0} input / {b.outputTokens:N0} output tokens · {b.toolCalls} tool call(s)",
                sizeHalfPt: 16, italic: true, colorHex: MutedText, spaceAfter: 160));

            body.Append(HorizontalRule());
        }
    }

    // ── Action Items ──────────────────────────────────────────────────────

    private static void AddActionItems(Body body, IReadOnlyList<AlertDto> alerts)
    {
        body.Append(StyledParagraph(
            "ACTION ITEMS",
            sizeHalfPt: 26, bold: true, colorHex: BrandSlate, spaceBefore: 360, spaceAfter: 240));

        if (alerts.Count == 0)
        {
            body.Append(StyledParagraph(
                "No active alerts.",
                sizeHalfPt: 22, italic: true, colorHex: MutedText));
            return;
        }

        var groups = alerts
            .OrderBy(a => a.section)
            .ThenBy(a => SeverityOrder(a.severity))
            .ThenByDescending(a => a.generatedAt)
            .GroupBy(a => a.section);

        foreach (var group in groups)
        {
            body.Append(StyledParagraph(
                $"{FriendlySectionName(group.Key)}  ({group.Count()})",
                sizeHalfPt: 22, bold: true, colorHex: BrandSlate, spaceBefore: 200, spaceAfter: 100));

            foreach (var a in group)
            {
                var sevColor = SeverityColor(a.severity);

                // Title line: severity inline (color-coded, uppercase) + headline.
                var titlePara = new Paragraph();
                titlePara.Append(SpacingProps(spaceAfter: 40));
                titlePara.Append(InlineRun($"{a.severity.ToUpperInvariant()} ", bold: true, colorHex: sevColor, sizeHalfPt: 22));
                titlePara.Append(InlineRun(a.title, bold: true, sizeHalfPt: 22));
                body.Append(titlePara);

                // Body paragraph.
                body.Append(StyledParagraph(
                    a.body,
                    sizeHalfPt: 20, spaceAfter: 80, leftIndentTwips: 360, justify: true));

                // Metadata footer.
                var meta = $"Subject: {a.subject ?? "(none)"} · Generated {a.generatedAt:yyyy-MM-dd HH:mm}";
                if (a.acknowledgedAt is not null)
                {
                    meta += $" · Acknowledged {a.acknowledgedAt:yyyy-MM-dd HH:mm}" +
                            (string.IsNullOrWhiteSpace(a.acknowledgedBy) ? "" : $" by {a.acknowledgedBy}");
                }

                body.Append(StyledParagraph(
                    meta,
                    sizeHalfPt: 16, italic: true, colorHex: MutedText, leftIndentTwips: 360, spaceAfter: 200));
            }

            body.Append(HorizontalRule());
        }
    }

    // ── OpenXml helpers ───────────────────────────────────────────────────

    private static Paragraph StyledParagraph(
        string text,
        int sizeHalfPt = 22,
        bool bold = false,
        bool italic = false,
        string? colorHex = null,
        bool alignCenter = false,
        bool justify = false,
        int spaceBefore = 0,
        int spaceAfter = 80,
        int leftIndentTwips = 0)
    {
        var p = new Paragraph();

        var pPr = new ParagraphProperties();
        if (alignCenter) pPr.Append(new Justification { Val = JustificationValues.Center });
        else if (justify) pPr.Append(new Justification { Val = JustificationValues.Both });

        if (leftIndentTwips > 0)
            pPr.Append(new Indentation { Left = leftIndentTwips.ToString() });

        if (spaceBefore > 0 || spaceAfter > 0)
        {
            pPr.Append(new SpacingBetweenLines
            {
                Before = spaceBefore.ToString(),
                After = spaceAfter.ToString(),
            });
        }
        p.Append(pPr);

        p.Append(InlineRun(text, bold: bold, italic: italic, colorHex: colorHex, sizeHalfPt: sizeHalfPt));
        return p;
    }

    private static Run InlineRun(string text, bool bold = false, bool italic = false, string? colorHex = null, int sizeHalfPt = 22)
    {
        var run = new Run();

        var rPr = new RunProperties();
        rPr.Append(new RunFonts { Ascii = Font, HighAnsi = Font });
        rPr.Append(new FontSize { Val = sizeHalfPt.ToString() });
        if (bold) rPr.Append(new Bold());
        if (italic) rPr.Append(new Italic());
        if (!string.IsNullOrEmpty(colorHex))
            rPr.Append(new Color { Val = colorHex });
        run.Append(rPr);

        // Preserve internal whitespace + line breaks in body / recommendation
        // text so multi-line AI output renders readably.
        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) run.Append(new Break());
            run.Append(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
        }
        return run;
    }

    private static ParagraphProperties SpacingProps(int spaceBefore = 0, int spaceAfter = 80)
    {
        var pPr = new ParagraphProperties();
        if (spaceBefore > 0 || spaceAfter > 0)
        {
            pPr.Append(new SpacingBetweenLines
            {
                Before = spaceBefore.ToString(),
                After = spaceAfter.ToString(),
            });
        }
        return pPr;
    }

    private static Paragraph HorizontalRule()
    {
        var p = new Paragraph();
        var pPr = new ParagraphProperties();
        pPr.Append(new ParagraphBorders(
            new BottomBorder
            {
                Val = BorderValues.Single,
                Size = 6,
                Space = 1,
                Color = "D1D5DB",
            }));
        pPr.Append(new SpacingBetweenLines { Before = "120", After = "120" });
        p.Append(pPr);
        return p;
    }

    // ── Mapping helpers ───────────────────────────────────────────────────

    private static DateTime MostRecentMonday(DateTime date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.Date.AddDays(-daysSinceMonday);
    }

    private static int SectionOrder(string section) => section switch
    {
        "FinancialHealth"   => 0,
        "PortfolioHealth"   => 1,
        "ClientStrategy"    => 2,
        "BdMarket"          => 3,
        "OperationsTalent"  => 4,
        "WatchItems"        => 5,
        "CashAndFinancials" => 0,
        _ => 99,
    };

    private static int SeverityOrder(string severity) => severity.ToUpperInvariant() switch
    {
        "HIGH"   => 0,
        "MEDIUM" => 1,
        "LOW"    => 2,
        _ => 99,
    };

    private static string SeverityColor(string severity) => severity.ToUpperInvariant() switch
    {
        "HIGH"   => SeverityHigh,
        "MEDIUM" => SeverityMed,
        "LOW"    => SeverityLow,
        _ => MutedText,
    };

    private static string FriendlySectionName(string section) => section switch
    {
        "FinancialHealth"   => "Financial Health",
        "PortfolioHealth"   => "Portfolio Health",
        "ClientStrategy"    => "Client Strategy",
        "BdMarket"          => "BD / Market",
        "OperationsTalent"  => "Operations & Talent",
        "WatchItems"        => "Watch Items",
        "CashAndFinancials" => "Cash & Financials",
        "ProjectHealth"     => "Project Health",
        "Clients"           => "Clients",
        "BusinessDevelopment" => "Business Development",
        _ => section,
    };
}
