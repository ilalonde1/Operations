#nullable enable
namespace Kor.Opportunities.Data.BdReports.Generators;

/// <summary>
/// KOR BD report typography, parity with the shipped PowerShell builders
/// (tools/BdReportBuilders: Heading 1 16pt bold / Heading 2 12pt bold /
/// Heading 3 10.5pt bold / Normal 10pt, Word-default Calibri, page margins
/// top+bottom 36pt, left+right 54pt). OpenXml font sizes are half-points,
/// paragraph spacing is twentieths of a point, page margins are twips.
/// </summary>
public static class KorReportStyles
{
    public const string FontFamily = "Calibri";

    // Half-points.
    public const int Heading1SizeHalfPoints = 32;  // 16pt
    public const int Heading2SizeHalfPoints = 24;  // 12pt
    public const int Heading3SizeHalfPoints = 21;  // 10.5pt
    public const int NormalSizeHalfPoints = 20;    // 10pt
    public const int NoteSizeHalfPoints = 18;      // 9pt (Italic primitive)
    public const int TableSizeHalfPoints = 18;     // 9pt (MakeTable primitive)

    // Twentieths of a point (paragraph spacing per the PS builders).
    public const int Heading1SpaceBefore = 0;      // 0pt
    public const int Heading1SpaceAfter = 120;     // 6pt
    public const int Heading2SpaceBefore = 200;    // 10pt
    public const int Heading2SpaceAfter = 60;      // 3pt
    public const int Heading3SpaceBefore = 120;    // 6pt
    public const int Heading3SpaceAfter = 40;      // 2pt
    public const int NormalSpaceAfter = 80;        // 4pt

    // Twips (PS builders set margins in points: 36 top/bottom, 54 left/right).
    public const int PageMarginTopBottom = 720;    // 36pt = 0.5"
    public const int PageMarginLeftRight = 1080;   // 54pt = 0.75"

    public const string TableBorderColor = "999999";
}
