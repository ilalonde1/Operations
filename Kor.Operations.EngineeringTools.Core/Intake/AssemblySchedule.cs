using System.Globalization;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// AN ASSEMBLY SCHEDULE IS A LEGEND OF CARDS, AND A CARD IS READ WHOLE (intake step 32).
///
/// An architect's set states its wall and floor types on schedule sheets laid out as cards: a
/// heading "CODE - NAME" with the code drawn again in its own symbol beside it, the build-up as
/// one line per layer under it, a ratings block (F.R.R., S.T.C.) with the value provided and the
/// code it references, and remarks. 31170's A005 (walls) carries 19 such cards and A006 (floors)
/// the slab assemblies, and everything the structural model needs to know about a wall the plan
/// only tags is here: "C16 - 16" C.I.P WALL — 457mm (16") TYP. CONCRETE WALL … AS PER STRUCTURAL
/// DRAWING", against "S8.1 - STEEL STUD PARTY WALL — 2 layers 15.9mm type-X G.W.B., 41x92 steel
/// studs @ 400". The tags on the plans are these codes.
///
/// This reads EVERY line of every card — the layers, both ratings, the references, the remarks —
/// because the intake's purpose is to pull once what any downstream tool could need. What the
/// structural model takes from it, the material and the thickness, is derived from the words by
/// a vocabulary (<see cref="StructuralWords"/>, <see cref="PartitionWords"/>) that a KorStandards
/// row extends without a build.
///
/// WHAT IT COVERS: a card whose heading is "CODE - NAME" with the bare code beside it; the card's
/// box bounded by the next heading across and the next row of headings down; layers as the lines
/// beginning with a dash or a number; ratings as label rows (F.R.R., S.T.C.) with the provided
/// value and reference read from the same row band; remarks as what is left. WHAT IT DOES NOT: a
/// tabular (row-per-type) schedule, which <see cref="MarkRowScheduleReader"/> reads; a card whose
/// heading has no dash; a rating stated somewhere other than under its label; and which wall on the
/// plan carries which code, which is the tag reader's.
/// </summary>
public static class AssemblySchedule
{
    /// <summary>What an assembly is made of, as far as a structural model cares.</summary>
    public enum Material { Unknown, Concrete, Masonry, Stud }

    /// <summary>One card: everything it states.</summary>
    public sealed record Assembly(
        string Kind,                               // WALL, FLOOR, ROOF … from the sheet's title, else the card's name
        string Code,                               // C16, S8.1, F12L
        string Name,                               // 16" C.I.P WALL
        IReadOnlyList<string> Layers,              // the build-up, top to bottom as printed
        IReadOnlyDictionary<string, string> Ratings,   // F.R.R. → 2HR, S.T.C. → 55
        IReadOnlyList<string> References,          // V.B.B.L. 2019/ DIV-B / …, ULC U483 (F.R.R)
        IReadOnlyList<string> Remarks,
        Material Material,
        double? ThicknessMm,
        int Page)
    {
        /// <summary>Concrete and masonry stand in a structural model; studs and gypsum do not.</summary>
        public bool IsStructural => Material is Material.Concrete or Material.Masonry;
    }

    /// <summary>Words that make an assembly structural. Compiled defaults; KorStandards row <c>dxf.assembly.structural-words</c>.</summary>
    public static readonly IReadOnlyList<string> StructuralWords =
        ["CONCRETE", "C.I.P", "CIP", "CAST-IN-PLACE", "CAST IN PLACE", "SHOTCRETE", "PRECAST", "CMU", "MASONRY", "BLOCK WALL", "CONC."];

    /// <summary>Words that make an assembly a partition or a finish. Row <c>dxf.assembly.partition-words</c>.</summary>
    public static readonly IReadOnlyList<string> PartitionWords =
        ["STEEL STUD", "WOOD STUD", "METAL STUD", "STUDS", "STUD WALL", "GYPSUM", "G.W.B", "GWB", "DRYWALL", "LINER PANEL", "FURRING", "PARTITION"];

    /// <summary>Masonry words, told apart from concrete for the section the model gives the wall.</summary>
    private static readonly string[] MasonryWords = ["CMU", "MASONRY", "BLOCK WALL"];

    /// <summary>"C16 - 16" C.I.P WALL": a short code, a dash, a name.</summary>
    private static readonly Regex Heading = new(@"^(?<code>[A-Za-z]{1,4}[.\-]?\d{1,3}(?:[.\-][A-Za-z0-9]{1,4})?(?:hi|lo)?)\s+(?:[-–]\s+)?(?<name>[A-Za-z0-9\[\(].*)$", RegexOptions.Compiled);

    /// <summary>A layer line: "- 457mm (16") TYP. CONCRETE WALL" or "5. 152mm (6") C.I.P".</summary>
    private static readonly Regex LayerLine = new(@"^(?:[-–•]\s*|\d{1,2}\.\s+)(?<text>\S.*)$", RegexOptions.Compiled);

    /// <summary>A rating label: F.R.R., S.T.C., STC, FRR, and the like — letters and dots, ending the token.</summary>
    private static readonly Regex RatingLabel = new(@"^(?<label>[A-Z](?:\.?[A-Z]){1,4}\.?)$", RegexOptions.Compiled);

    /// <summary>A thickness stated in the words: 457mm, 305 mm, 16", 5/8".</summary>
    private static readonly Regex Millimetres = new(@"(?<![\d.])(?<mm>\d{2,4})\s*mm", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Inches = new(@"(?<![\d/.])(?<in>\d{1,2}(?:\.\d{1,2})?(?:\s+\d/\d{1,2})?)\s*(?:""|”|″)", RegexOptions.Compiled);

    private static readonly string[] ColumnHeads = ["PROVIDED", "REFERENCE CODE", "REFERENCE", "REMARKS", "NOTE:", "NOTES:"];

    /// <summary>
    /// Every card in a set: the schedule sheets found the way <see cref="SetStoreys"/> finds elevation
    /// sheets (bookmark, then the title block's field, then the title text), each read once.
    /// </summary>
    public static IReadOnlyList<Assembly> ReadSet(string pdfPath) => ReadSet(pdfPath, StructuralWords, PartitionWords);

    /// <summary>As above, with the material vocabulary (step 32): the compiled defaults, or the KorStandards rows dxf.assembly.structural-words / dxf.assembly.partition-words.</summary>
    public static IReadOnlyList<Assembly> ReadSet(string pdfPath, IReadOnlyList<string> structuralWords, IReadOnlyList<string> partitionWords)
    {
        ArgumentNullException.ThrowIfNull(pdfPath);
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        var facts = DocumentFacts.From(doc);
        var all = new List<Assembly>();
        for (int page = 1; page <= facts.Pages; page++)
        {
            VectorPageReader.PageContent content;
            try { content = VectorPageReader.ReadPage(doc.GetPage(page)); } catch { continue; }
            if (content.Words.Count == 0) continue;
            string? bookmark = facts.Bookmarks.TryGetValue(page, out var bt) ? bt : null;
            var fields = TitleBlockFields.Read(content);
            string? fieldTitle = fields.TryGetValue("SHEET TITLE", out var ft) ? ft : fields.TryGetValue("DRAWING TITLE", out ft) ? ft : null;
            string? title = bookmark ?? fieldTitle;
            if (title is null || !title.Contains("SCHEDULE", StringComparison.OrdinalIgnoreCase)) continue;
            all.AddRange(Read(content, title, page, structuralWords, partitionWords));
        }
        return all;
    }

    /// <summary>With a trace, every line the reader saw and whether it read as a heading (the CLI's --trace).</summary>
    public static Action<string>? Trace;

    /// <summary>The cards on one sheet, left to right then top to bottom. Empty when the sheet lays out no card.</summary>
    public static IReadOnlyList<Assembly> Read(VectorPageReader.PageContent page, string? sheetTitle, int pageNumber, IReadOnlyList<string>? structuralWords = null, IReadOnlyList<string>? partitionWords = null)
    {
        ArgumentNullException.ThrowIfNull(page);

        // the sheet's lines of text, as the extractor lays them out: words on one baseline, left to right
        var lines = TextLines(page);
        if (Trace is not null)
            foreach (var l in lines.OrderBy(l => l.Y).ThenBy(l => l.X).Take(40))
                Trace($"p{pageNumber} ({l.X:0},{l.Y:0}) {(Heading.IsMatch(l.Text) ? "HEADING? " : "")}{l.Text}");

        // headings: "CODE - NAME", with the bare code standing beside it in its own symbol. A heading
        // WITH its dash is a card whether or not the symbol agrees — 31170's A005 draws "C13 - 13"
        // C.I.P WALL" with the symbol C12 beside it, a copied card whose symbol was not updated, and
        // that is a finding to report to the architect, not a card to drop. A heading with no dash
        // needs the symbol to confirm it.
        var headings = new List<(int Index, string Code, string Name, string? SymbolSays)>();
        for (int i = 0; i < lines.Count; i++)
        {
            var m = Heading.Match(lines[i].Text);
            if (!m.Success) continue;
            string code = m.Groups["code"].Value;
            bool dashed = Regex.IsMatch(lines[i].Text, @"^\S+\s+[-–]\s");
            var symbol = lines.Where(l => l != lines[i] && Math.Abs(l.Y - lines[i].Y) <= 6 && l.X < lines[i].X && lines[i].X - l.X <= 80
                                          && Regex.IsMatch(l.Text.Trim(), @"^[A-Za-z]{1,4}[.\-]?\d{1,3}(?:[.\-][A-Za-z0-9]{1,4})?(?:hi|lo)?$"))
                              .Select(l => l.Text.Trim()).FirstOrDefault();
            bool confirmed = symbol is not null && string.Equals(symbol, code, StringComparison.OrdinalIgnoreCase);
            if (confirmed || dashed) headings.Add((i, code, m.Groups["name"].Value.Trim(), confirmed ? null : symbol ?? ""));
        }
        // a sheet is a card legend when at least one card is confirmed by its symbol; on such a sheet a
        // dashed heading without its symbol is still a card (C13). A dashed row on a sheet with no
        // confirmed card is a table row — "DW1 - 4-30M3200 @ 400 DOWELS" on 31065 — and not a card.
        if (!headings.Any(h => h.SymbolSays is null)) return Array.Empty<Assembly>();
        headings = headings.Select(h => h.SymbolSays == "" ? (h.Index, h.Code, h.Name, (string?)null) : h).ToList();

        string kind = KindOf(sheetTitle);
        // rows of cards: headings within a text height of one another are one row; a card is far
        // taller than that, so the next row's headings are a clear gap down the sheet
        var rows = new List<List<(int Index, string Code, string Name, string? SymbolSays)>>();
        foreach (var h in headings.OrderBy(h => lines[h.Index].Y))
        {
            if (rows.Count > 0 && lines[h.Index].Y - lines[rows[^1][0].Index].Y <= 30) rows[^1].Add(h);
            else rows.Add(new List<(int, string, string, string?)> { h });
        }
        var rowTops = rows.Select(g => g.Min(h => lines[h.Index].Y)).ToList();
        // the sheet's columns: every x a heading starts at, within a symbol's width being one column
        var columnsX = new List<double>();
        foreach (double x in headings.Select(h => lines[h.Index].X).OrderBy(x => x))
            if (columnsX.Count == 0 || x - columnsX[^1] > 40) columnsX.Add(x);
        if (Trace is not null)
            for (int r = 0; r < rows.Count; r++)
                Trace($"p{pageNumber} row {r} top {rowTops[r]:0}: " + string.Join("  ", rows[r].OrderBy(h => lines[h.Index].X).Select(h => $"{h.Code}@x{lines[h.Index].X:0},y{lines[h.Index].Y:0}")));

        var cards = new List<Assembly>();
        for (int r = 0; r < rows.Count; r++)
        {
            var inRow = rows[r].OrderBy(h => lines[h.Index].X).ToList();
            double top = rowTops[r] - 8;
            double bottom = r + 1 < rows.Count ? rowTops[r + 1] - 8 : page.HeightPts;
            for (int c = 0; c < inRow.Count; c++)
            {
                // a legend is a fixed grid: a card's right edge is the NEXT COLUMN of the sheet, whether or
                // not this row fills it — an empty column let C12 on 31170's last row stretch across it
                // and take the overflow of the card above
                double left = lines[inRow[c].Index].X - 60;                       // the code's symbol sits left of the heading
                double nextColumn = columnsX.Where(x => x > lines[inRow[c].Index].X + 20).DefaultIfEmpty(page.WidthPts + 60).Min();
                double right = nextColumn - 60;
                var box = lines.Where(l => l.X >= left && l.X < right && l.Y > top && l.Y < bottom && l.Y > lines[inRow[c].Index].Y + 2)
                               .OrderBy(l => l.Y).ThenBy(l => l.X).ToList();
                var card = Card(kind, inRow[c].Code, inRow[c].Name, box, pageNumber, structuralWords, partitionWords);
                if (inRow[c].SymbolSays is string says)
                    card = card with { Remarks = card.Remarks.Append($"DRAWING: the symbol beside this card reads {says}, not {inRow[c].Code}").ToList() };
                cards.Add(card);
            }
        }
        return cards;
    }

    private static Assembly Card(string kind, string code, string name, List<TextLine> box, int pageNumber, IReadOnlyList<string>? structuralWords = null, IReadOnlyList<string>? partitionWords = null)
    {
        var layers = new List<string>();
        var ratings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var references = new List<string>();
        var remarks = new List<string>();

        // the ratings first, so a value printed a hair above its label is not read as a remark first
        var taken = new HashSet<TextLine>();
        foreach (var lbl in box)
        {
            var label = RatingLabel.Match(lbl.Text.Trim());
            if (!label.Success) continue;
            taken.Add(lbl);
            // the value provided and the reference sit on the label's row band, to its right
            var band = box.Where(b => b != lbl && Math.Abs(b.Y - lbl.Y) <= 6 && b.X > lbl.X).OrderBy(b => b.X).ToList();
            // a rating states a number (2HR, 1HR., 55); a word beside the label is a reference or a remark
            var value = band.FirstOrDefault(b => !ColumnHeads.Contains(b.Text.Trim(), StringComparer.OrdinalIgnoreCase) && b.Text.Trim().Length <= 12 && b.Text.Any(char.IsDigit) && !RatingLabel.IsMatch(b.Text.Trim()));
            if (value is not null) { ratings[label.Groups["label"].Value] = value.Text.Trim(); taken.Add(value); }
            foreach (var b in band)
                if (b != value && b.Text.Trim().Length > 12 && !ColumnHeads.Contains(b.Text.Trim(), StringComparer.OrdinalIgnoreCase) && !references.Contains(b.Text.Trim()))
                { references.Add(b.Text.Trim()); taken.Add(b); }
        }

        for (int i = 0; i < box.Count; i++)
        {
            if (taken.Contains(box[i])) continue;
            string t = box[i].Text.Trim();
            if (t.Length == 0 || string.Equals(t, code, StringComparison.OrdinalIgnoreCase)) continue;
            if (ColumnHeads.Any(h => string.Equals(t, h, StringComparison.OrdinalIgnoreCase))) continue;

            var layer = LayerLine.Match(t);
            if (layer.Success) { layers.Add(layer.Groups["text"].Value.Trim()); continue; }
            if (t.StartsWith("NOTE", StringComparison.OrdinalIgnoreCase) || remarks.Count > 0 || layers.Count > 0)
                remarks.Add(t);
        }

        var material = MaterialOf(name, layers, structuralWords, partitionWords);
        return new Assembly(kind, code, name, layers, ratings, references, remarks, material, ThicknessOf(name, layers, material), pageNumber);
    }

    /// <summary>The name decides first; the layers only when the name says nothing.</summary>
    public static Material MaterialOf(string name, IReadOnlyList<string> layers, IReadOnlyList<string>? structuralWords = null, IReadOnlyList<string>? partitionWords = null)
    {
        var fromName = MaterialIn(name, structuralWords, partitionWords);
        if (fromName != Material.Unknown) return fromName;
        foreach (var layer in layers)
        {
            var m = MaterialIn(layer, structuralWords, partitionWords);
            if (m != Material.Unknown) return m;
        }
        return Material.Unknown;
    }

    private static Material MaterialIn(string text, IReadOnlyList<string>? structuralWords = null, IReadOnlyList<string>? partitionWords = null)
    {
        string u = text.ToUpperInvariant();
        bool structural = (structuralWords ?? StructuralWords).Any(w => u.Contains(w, StringComparison.Ordinal));
        bool partition = (partitionWords ?? PartitionWords).Any(w => u.Contains(w, StringComparison.Ordinal));
        if (structural && !partition) return MasonryWords.Any(w => u.Contains(w, StringComparison.Ordinal)) ? Material.Masonry : Material.Concrete;
        if (partition && !structural) return Material.Stud;
        // both in one line ("C.I.P wall, G.W.B. furring"): the structural word names the wall
        if (structural) return MasonryWords.Any(w => u.Contains(w, StringComparison.Ordinal)) ? Material.Masonry : Material.Concrete;
        return Material.Unknown;
    }

    /// <summary>
    /// The thickness in the name ("16" C.I.P WALL"), else the THICKEST layer that carries the
    /// material — 2" concrete pavers sit on a 22" concrete slab, and the slab is the assembly.
    /// </summary>
    public static double? ThicknessOf(string name, IReadOnlyList<string> layers, Material material)
    {
        if (ThicknessIn(name) is double fromName) return fromName;
        double? best = null;
        foreach (var layer in layers)
            if (material != Material.Unknown && MaterialIn(layer) == material && ThicknessIn(layer) is double t && (best is null || t > best))
                best = t;
        return best;
    }

    private static double? ThicknessIn(string text)
    {
        // "@ 400mm (16") O.C." is a spacing, not a thickness: nothing on centre is a thickness
        text = Regex.Replace(text, @"@\s*[^@]*?\bO\.?C\.?\b", " ", RegexOptions.IgnoreCase);
        var mm = Millimetres.Match(text);
        if (mm.Success && double.TryParse(mm.Groups["mm"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return v;
        var inch = Inches.Match(text);
        if (inch.Success)
        {
            string s = inch.Groups["in"].Value.Trim();
            double whole = 0, frac = 0;
            var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0) double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out whole);
            if (parts.Length > 1 && parts[1].Contains('/'))
            {
                var f = parts[1].Split('/');
                if (f.Length == 2 && double.TryParse(f[0], out double n) && double.TryParse(f[1], out double d) && d > 0) frac = n / d;
            }
            return (whole + frac) * 25.4;
        }
        return null;
    }

    /// <summary>WALL from "SCHEDULES - WALLS", FLOOR from "… FLOORS"; the last word of the title, singular.</summary>
    public static string KindOf(string? sheetTitle)
    {
        if (string.IsNullOrWhiteSpace(sheetTitle)) return "ASSEMBLY";
        var words = Regex.Split(sheetTitle.ToUpperInvariant(), @"[^A-Z]+").Where(w => w.Length > 0).ToList();
        string last = words.LastOrDefault(w => w is not ("SCHEDULE" or "SCHEDULES")) ?? "ASSEMBLY";
        return last.EndsWith('S') && last.Length > 3 ? last[..^1] : last;
    }

    private sealed record TextLine(double X, double Y, string Text);

    /// <summary>
    /// Words on one baseline, a space apart, joined left to right into the line the drafter typed.
    /// A row is words whose vertical extents overlap — a dash or a hyphen sits lower than the
    /// letters beside it (3 pt on 31170's A005) and a fixed bucket put every "-" on its own line,
    /// so no heading had its dash and no layer its bullet.
    /// </summary>
    private static List<TextLine> TextLines(VectorPageReader.PageContent page)
    {
        var lines = new List<TextLine>();
        var rows = new List<List<VectorPageReader.TextToken>>();
        foreach (var w in page.Words.OrderBy(w => w.Cy))
        {
            var row = rows.LastOrDefault();
            if (row is not null && w.Cy - row.Max(r => r.Cy) <= Math.Max(4.0, 0.6 * Math.Max(w.Height, row[0].Height))) row.Add(w);
            else rows.Add(new List<VectorPageReader.TextToken> { w });
        }
        foreach (var row in rows)
        {
            var sorted = row.OrderBy(w => w.MinX).ToList();
            var current = new List<VectorPageReader.TextToken> { sorted[0] };
            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i].MinX - current[^1].MaxX <= 14) current.Add(sorted[i]);
                else { lines.Add(Join(current)); current = new List<VectorPageReader.TextToken> { sorted[i] }; }
            }
            lines.Add(Join(current));
        }
        return lines;

        // y is turned page-DOWN here (PdfPig's runs up from the bottom), so a card reads top to bottom
        TextLine Join(List<VectorPageReader.TextToken> ws)
            => new(ws[0].MinX, page.HeightPts - ws.Average(w => w.Cy), string.Join(" ", ws.Select(w => w.Text.Trim())));
    }
}
