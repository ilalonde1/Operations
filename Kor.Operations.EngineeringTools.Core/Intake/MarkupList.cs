using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.PdfToSafe;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// A MARK-UP IS A LIST OF INSTRUCTIONS (foundation §3.1, the engineer-to-drafter loop). Every
/// annotation with words becomes an item the drafter can work through: what it says, who said it,
/// what it asks (move, add, remove, align …), of what (a column, a gridline, a wall), which way and
/// how far, where on the grid it sits, and which member of the drawing it is nearest. A dimension
/// drawn as its own line beside an instruction is that instruction's distance. A tick is an
/// approval. Measured on 31168's Building C column diagram and 31065's MB-6 back-check, 2026-09-08.
/// </summary>
public static class MarkupList
{
    public enum Kind { Instruction, Measurement, Approval, Note, Shape }

    public sealed record Item(
        int Page, string? Sheet, string Author, Kind Kind, string Text,
        string? Action, string? Subject, string? Direction, double? DistanceMm, string? DistanceFrom,
        double XMm, double YMm, string GridRef, string? Nearest)
    {
        public string Line()
        {
            string what = Kind switch
            {
                Kind.Instruction when Action == "attend" => "ring drawn round something to attend to",
                Kind.Instruction => $"{Action}{(Subject is null ? "" : " " + Subject)}{(Direction is null ? "" : " " + Direction)}" +
                                    (DistanceMm is double d ? $" {d:0} mm ({d / 25.4:0.#} in{(DistanceFrom is null ? "" : ", " + DistanceFrom)})" : ""),
                Kind.Measurement => $"measures {DistanceMm:0} mm",
                Kind.Approval => "tick",
                Kind.Shape => "shape",
                _ => "note",
            };
            return $"p{Page}{(Sheet is null ? "" : " " + Sheet)}  [{Author}] {Kind.ToString().ToLowerInvariant()}: {what}  at {GridRef}" +
                   (Nearest is null ? "" : $", {Nearest}") + $"  — \"{Shorten(Text)}\"";
        }

        private static string Shorten(string s) => s.Length <= 90 ? s : s[..87] + "...";
    }

    /// <summary>An instruction's distance is the measurement drawn within this reach of it on the page (mm at the sheet's scale).</summary>
    public const double MeasurementReachMm = 3000;
    /// <summary>A member within this reach of an instruction is the member it is about.</summary>
    public const double MemberReachMm = 1524;
    /// <summary>Ink this small on the paper (a third of an inch) is a tick.</summary>
    public const double TickMaxPts = 24;
    /// <summary>Ink between these sizes, and no more than 1.6 times as long as wide, is a ring drawn round something to attend to.</summary>
    public const double RingMinPts = 40, RingMaxPts = 400, RingMaxAspect = 1.6;

    private static readonly Regex Verb = new(
        @"^\s*(?<action>move|shift|relocate|add|provide|remove|delete|omit|align|extend|shorten|lengthen|change|revise|increase|reduce|lower|raise|rotate|flip|confirm|check|show)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Subject = new(
        @"\b(?<subject>grid\s?lines?|grids?|columns?|cols?\.?|walls?|beams?|slabs?|footings?|openings?|piers?|dowels?|rebar|stairs?|steps?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Direction = new(
        @"\b(?<direction>up|down|left|right|north|south|east|west|inward|outward|in|out)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    /// <summary>A feet-and-inches token inside a sentence: 1'-11", 0'-6 1/2", 12'.</summary>
    private static readonly Regex FeetInchesToken = new(@"(?<![\d.])(?<token>\d+'\s*-?\s*(?:\d+(?:\s+\d+/\d+)?"")?)", RegexOptions.CultureInvariant);
    private static readonly Regex DecimalInches = new(@"(?<![\d.'-])(?<inches>\d+(?:\.\d+)?)\s*(?:""|in\b|inch(?:es)?\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Millimetres = new(@"(?<![\d.])(?<mm>\d{2,5})\s*mm\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The items on one sheet, from the record's mark-up notes against its drawing objects.</summary>
    public static IReadOnlyList<Item> Build(SheetRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        double mmPerPt = (record.ScaleDenominator ?? 1) * PdfToSafeConstants.PointsToMm;
        var items = new List<Item>();
        foreach (var note in record.Annotations.Count > 0 ? record.Annotations : record.Markup)
        {
            double x = note.Cx * mmPerPt, y = note.Cy * mmPerPt;
            var (kind, action, subject, direction, distance) = Classify(note);
            items.Add(new Item(record.PageNumber, record.SheetNumber, note.Author, kind, note.Text,
                action, subject, direction, distance, distance is null ? null : "in the text",
                x, y, GridReference(record.Geometry.GridAxes, x, y), Nearest(record.Geometry, x, y)));
        }

        // A DIMENSION DRAWN BESIDE AN INSTRUCTION IS ITS DISTANCE: "Move Gridline down 6.5"" and a
        // line annotation reading 0'-6 1/2" a few inches away, on every one of Building C's moves.
        var measurements = items.Where(i => i.Kind == Kind.Measurement && i.DistanceMm is not null).ToList();
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.Kind != Kind.Instruction || item.DistanceMm is not null || measurements.Count == 0) continue;
            var nearest = measurements
                .Select(m => (m, D: Math.Sqrt(Math.Pow(m.XMm - item.XMm, 2) + Math.Pow(m.YMm - item.YMm, 2))))
                .OrderBy(t => t.D).First();
            if (nearest.D <= MeasurementReachMm)
                items[i] = item with { DistanceMm = nearest.m.DistanceMm, DistanceFrom = "the line drawn beside it" };
        }
        return items;
    }

    /// <summary>What a note is and, for an instruction, what it asks.</summary>
    public static (Kind Kind, string? Action, string? Subject, string? Direction, double? DistanceMm) Classify(MarkupNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        string text = note.Text.Trim();
        // INK IS READ BY ITS SIZE ON THE PAPER, not by the character or two Bluebeam writes into it.
        // A tick is small (MB-6: 14 x 14 pt, "." and "/"); a ring drawn round a thing to fix is
        // round and a hand's width (127 x 144, "o"); a long stroke is a leader or an underline.
        if (Wordless(note))
        {
            if (note.Type is not ("Ink" or "Line" or "PolyLine")) return (Kind.Shape, null, null, null, null);
            double w = note.Width, h = note.Height, size = Math.Max(w, h);
            if (size <= TickMaxPts) return (Kind.Approval, null, null, null, null);
            if (note.Type == "Ink" && size >= RingMinPts && size <= RingMaxPts && size <= RingMaxAspect * Math.Min(w, h))
                return (Kind.Instruction, "attend", null, null, null);
            return (Kind.Shape, null, null, null, null);
        }

        double? dim = Distance(text);
        if (dim is not null && (note.Type.Equals("Line", StringComparison.OrdinalIgnoreCase) || note.Type.Equals("PolyLine", StringComparison.OrdinalIgnoreCase)
                                || DimensionStrings.Parse(text) is not null && !text.Any(char.IsLetter)))
            return (Kind.Measurement, null, null, null, dim);

        var verb = Verb.Match(text);
        if (!verb.Success) return (Kind.Note, null, null, null, null);
        string action = verb.Groups["action"].Value.ToLowerInvariant();
        var subject = Subject.Match(text);
        var direction = Direction.Match(text, verb.Length);
        return (Kind.Instruction, action,
            subject.Success ? Canonical(subject.Groups["subject"].Value) : null,
            direction.Success ? direction.Groups["direction"].Value.ToLowerInvariant() : null,
            dim);
    }

    /// <summary>
    /// No words: nothing at all, or the character or two Bluebeam writes into ink ("." "/" "-" "o").
    /// What such an annotation is comes from its size on the paper, not its text.
    /// </summary>
    public static bool Wordless(MarkupNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        string text = note.Text.Trim();
        return text.Length == 0
               || (text.Length <= 2 && !text.Any(char.IsLetterOrDigit))
               || (text.Length <= 2 && note.Type.Equals("Ink", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A length in the words: feet and inches as the drafter writes them, decimal inches with a mark, or millimetres.</summary>
    public static double? Distance(string text)
    {
        if (DimensionStrings.Parse(text) is { BareNumber: false } parsed) return parsed.Mm;
        var feet = FeetInchesToken.Match(text);
        if (feet.Success && DimensionStrings.Parse(feet.Groups["token"].Value) is { BareNumber: false } inSentence) return inSentence.Mm;
        var inches = DecimalInches.Match(text);
        if (inches.Success && double.TryParse(inches.Groups["inches"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
            return v * 25.4;
        var mm = Millimetres.Match(text);
        if (mm.Success && double.TryParse(mm.Groups["mm"].Value, out double m)) return m;
        return null;
    }

    private static string Canonical(string subject)
    {
        string s = subject.ToLowerInvariant().Replace(" ", "").TrimEnd('.');
        if (s.StartsWith("grid")) return "gridline";
        if (s.StartsWith("col")) return "column";
        return s.TrimEnd('s') switch { "stair" => "stair", var other => other };
    }

    /// <summary>"5/J": the nearest vertical axis and the nearest horizontal one, with how far off each when past a foot.</summary>
    public static string GridReference(IReadOnlyList<GridAxis> axes, double xMm, double yMm)
    {
        ArgumentNullException.ThrowIfNull(axes);
        var v = axes.Where(a => a.Vertical).OrderBy(a => Math.Abs(a.AtMm - xMm)).FirstOrDefault();
        var h = axes.Where(a => !a.Vertical).OrderBy(a => Math.Abs(a.AtMm - yMm)).FirstOrDefault();
        if (v is null && h is null) return $"({xMm / 1000:0.00}, {yMm / 1000:0.00}) m";
        static string Off(double d) => Math.Abs(d) <= 305 ? "" : $"{(d > 0 ? "+" : "-")}{Math.Abs(d) / 1000:0.0} m";
        string vs = v is null ? "?" : v.Name + Off(xMm - v.AtMm);
        string hs = h is null ? "?" : h.Name + Off(yMm - h.AtMm);
        return $"{vs}/{hs}";
    }

    private static string? Nearest(ExtractedGeometry g, double x, double y)
    {
        double best = double.MaxValue; string? what = null;
        foreach (var c in g.Columns)
        {
            double d = Math.Sqrt(Math.Pow(c.X - x, 2) + Math.Pow(c.Y - y, 2));
            if (d < best) { best = d; what = "column"; }
        }
        foreach (var w in g.Walls)
        {
            double mx = (w.Start.X + w.End.X) / 2, my = (w.Start.Y + w.End.Y) / 2;
            double d = Math.Sqrt(Math.Pow(mx - x, 2) + Math.Pow(my - y, 2));
            if (d < best) { best = d; what = "wall"; }
        }
        foreach (var f in g.Footings)
        {
            double d = Math.Sqrt(Math.Pow(f.Centre.X - x, 2) + Math.Pow(f.Centre.Y - y, 2));
            if (d < best) { best = d; what = $"footing {f.Mark}"; }
        }
        return what is null || best > MemberReachMm ? null : $"{what} {best / 1000:0.0} m away";
    }
}
