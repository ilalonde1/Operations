#nullable enable
namespace Kor.Opportunities.Data.BdReports.Generators;

/// <summary>
/// KOR BD report typography and palette. Base sizes inherit the shipped
/// PowerShell builders (tools/BdReportBuilders: Heading 1 16pt bold /
/// Heading 2 12pt bold / Heading 3 10.5pt bold / Normal 10pt, Word-default
/// Calibri); the 2026-06-12 UX pass adds the KOR brand palette (slate
/// headings + accent rules, matched to KorTheme.xaml / Round 49 of
/// PursuitBriefPdfExporter), denser 8.5pt tables with shaded header rows,
/// and the KPI/chip primitives. OpenXml font sizes are half-points,
/// paragraph spacing is twentieths of a point, page margins are twips,
/// border sizes are eighths of a point.
/// </summary>
public static class KorReportStyles
{
    public const string FontFamily = "Calibri";

    // KOR brand palette (hex without '#'; KorTheme.xaml parity).
    public const string BrandColor = "3F5364";       // Brand.Primary slate — headings, KPI values
    public const string AccentColor = "FF5B35";      // Brand.Accent — heading rules, KPI card accent
    public const string MutedTextColor = "6B7280";   // secondary text (PursuitBriefPdfExporter Muted)
    public const string CardBorderColor = "D5DAE0";  // KPI card + table cell hairlines
    public const string ZebraFillColor = "F4F6F8";   // even data rows

    // Verdict/status chip palette — IDENTICAL to the BD dashboard sector-card
    // pills (BdReportsWindow.xaml) so a verdict never changes color between
    // the cards and the report pane.
    public const string ChipUrgentColor = "C0392B";
    public const string ChipPositiveColor = "1E8449";
    public const string ChipCautionColor = "B7950B";
    public const string ChipInfoColor = "2471A3";
    public const string ChipNeutralColor = "85929E";

    public static string ChipColor(ChipTone tone) => tone switch
    {
        ChipTone.Urgent => ChipUrgentColor,
        ChipTone.Positive => ChipPositiveColor,
        ChipTone.Caution => ChipCautionColor,
        ChipTone.Info => ChipInfoColor,
        _ => ChipNeutralColor,
    };

    // Half-points.
    public const int Heading1SizeHalfPoints = 32;  // 16pt
    public const int Heading2SizeHalfPoints = 24;  // 12pt
    public const int Heading3SizeHalfPoints = 21;  // 10.5pt
    public const int NormalSizeHalfPoints = 20;    // 10pt
    public const int NoteSizeHalfPoints = 18;      // 9pt (Italic primitive)
    public const int TableSizeHalfPoints = 17;     // 8.5pt (denser per 2026-06-12 UX pass)
    public const int KpiValueSizeHalfPoints = 40;  // 20pt — the "big number"
    public const int KpiLabelSizeHalfPoints = 15;  // 7.5pt uppercase label
    public const int ChipSizeHalfPoints = 16;      // 8pt chip text

    // Twentieths of a point. Heading whitespace opened up vs the PS builders
    // (UX pass: generous space before sections so the page breathes).
    public const int Heading1SpaceBefore = 0;      // 0pt
    public const int Heading1SpaceAfter = 160;     // 8pt
    public const int Heading2SpaceBefore = 320;    // 16pt
    public const int Heading2SpaceAfter = 80;      // 4pt
    public const int Heading3SpaceBefore = 160;    // 8pt
    public const int Heading3SpaceAfter = 40;      // 2pt
    public const int NormalSpaceAfter = 80;        // 4pt

    // Eighths of a point (OpenXml border size units).
    public const int Heading1RuleSize = 12;        // 1.5pt accent rule under the title
    public const int Heading2RuleSize = 6;         // 0.75pt accent rule under sections
    public const int KpiAccentRuleSize = 24;       // 3pt KPI card top accent

    // Twips (PS builders set margins in points: 36 top/bottom, 54 left/right).
    public const int PageMarginTopBottom = 720;    // 36pt = 0.5"
    public const int PageMarginLeftRight = 1080;   // 54pt = 0.75"

    public const string TableBorderColor = "999999";
}
