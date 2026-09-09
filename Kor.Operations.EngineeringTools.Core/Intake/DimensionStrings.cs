using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// A dimension string is a length the drafter wrote; between two grid axes it is also a claim the
/// drawing makes about itself, and the axes' measured spacing at the sheet's scale is the check.
/// The record carries every dimension typed with its value; the ones that sit on a span of grid
/// axes carry which span and whether the two agree.
/// </summary>
/// <remarks>
/// WHAT IT COVERS: feet-and-inches strings (12'-6", 12'-6 1/2", 0'-8"), bare inches (8", 6 1/2")
/// and bare three-to-five-digit numbers as millimetres; horizontal text between vertical axes and
/// tall (rotated) text between horizontal axes; the tightest pair of axes the text sits between
/// whose gap matches the value within <see cref="AgreeMm"/>, else the adjacent pair it sits between.
/// WHAT IT DOES NOT: a bare number that is a bar mark, a level or a count — it is typed as a
/// millimetre dimension only when a span of axes agrees with it; a dimension whose extension lines
/// are not the grid (a wall length, an opening); the dimension line itself, which is not read.
/// </remarks>
public static class DimensionStrings
{
    /// <summary>A written length agrees with the axes' spacing within this: an inch of drafting.</summary>
    public const double AgreeMm = 25.4;

    public sealed record Dimension(int WordIndex, string Text, double ValueMm, double XMm, double YMm, bool Vertical, bool BareNumber,
        string? SpansFrom, string? SpansTo, double? AxisGapMm)
    {
        /// <summary>The text sits between two grid axes whose spacing matches its value.</summary>
        public bool Agrees => AxisGapMm is double g && Math.Abs(g - ValueMm) <= AgreeMm;
        /// <summary>The text sits between two adjacent grid axes and says something else than their spacing.</summary>
        public bool Disagrees => AxisGapMm is not null && !Agrees;
    }

    private static readonly Regex FeetInches = new(@"^\s*(\d+)'\s*-?\s*(?:(\d+)(?:\s+(\d+)/(\d+))?"")?\s*$", RegexOptions.Compiled);
    private static readonly Regex Inches = new(@"^\s*(\d+)(?:\s+(\d+)/(\d+))?""\s*$", RegexOptions.Compiled);
    private static readonly Regex Bare = new(@"^\s*(\d{3,5})\s*$", RegexOptions.Compiled);

    /// <summary>The length a dimension string states, in millimetres, or null when it is not one.</summary>
    public static (double Mm, bool BareNumber)? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = FeetInches.Match(text);
        if (m.Success)
        {
            double inches = double.Parse(m.Groups[1].Value) * 12;
            if (m.Groups[2].Success) inches += double.Parse(m.Groups[2].Value);
            if (m.Groups[3].Success) inches += double.Parse(m.Groups[3].Value) / double.Parse(m.Groups[4].Value);
            return (inches * 25.4, false);
        }
        m = Inches.Match(text);
        if (m.Success)
        {
            double inches = double.Parse(m.Groups[1].Value);
            if (m.Groups[2].Success) inches += double.Parse(m.Groups[2].Value) / double.Parse(m.Groups[3].Value);
            return (inches * 25.4, false);
        }
        m = Bare.Match(text);
        if (m.Success) return (double.Parse(m.Groups[1].Value), true);
        return null;
    }

    /// <summary>Every dimension string on the page, with the axis span it sits on where one matches. Words in furniture are not dimensions.</summary>
    public static IReadOnlyList<Dimension> Read(VectorPageReader.PageContent content, IReadOnlyList<GridAxis> axes, double mmPerPt, SheetFurniture.Set? furniture = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(axes);
        var xs = axes.Where(a => a.Vertical).OrderBy(a => a.AtMm).ToList();
        var ys = axes.Where(a => !a.Vertical).OrderBy(a => a.AtMm).ToList();
        var result = new List<Dimension>();
        for (int i = 0; i < content.Words.Count; i++)
        {
            var w = content.Words[i];
            if (Parse(w.Text) is not { } parsed) continue;
            if (furniture is not null && furniture.IsFurniture(w.Cx, w.Cy)) continue;
            double width = w.MaxX - w.MinX, height = w.MaxY - w.MinY;
            bool vertical = height > 2 * width;
            double x = w.Cx * mmPerPt, y = w.Cy * mmPerPt;
            var along = vertical ? ys : xs;
            double at = vertical ? y : x;
            var (from, to, gap) = Span(along, at, parsed.Mm);
            result.Add(new Dimension(i, w.Text.Trim(), parsed.Mm, x, y, vertical, parsed.BareNumber, from, to, gap));
        }
        return result;
    }

    /// <summary>
    /// The axes the text sits between: the tightest pair (i ≤ below, j ≥ above) whose gap matches
    /// the value, else the adjacent pair, else nothing when the text is outside the grid.
    /// </summary>
    private static (string? From, string? To, double? GapMm) Span(List<GridAxis> along, double at, double valueMm)
    {
        if (along.Count < 2) return (null, null, null);
        int below = -1;
        for (int k = 0; k < along.Count; k++) if (along[k].AtMm <= at) below = k;
        int above = below + 1;
        if (below < 0 || above >= along.Count) return (null, null, null);
        // widening pairs around the text, nearest first
        for (int reach = 1; reach < along.Count; reach++)
            for (int a = below; a >= 0; a--)
            {
                int b = a + reach;
                if (b < above || b >= along.Count) continue;
                double gap = along[b].AtMm - along[a].AtMm;
                if (Math.Abs(gap - valueMm) <= AgreeMm) return (along[a].Name, along[b].Name, gap);
            }
        return (along[below].Name, along[above].Name, along[above].AtMm - along[below].AtMm);
    }
}
