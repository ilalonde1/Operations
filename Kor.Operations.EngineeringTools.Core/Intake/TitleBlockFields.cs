using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// A title block is a FORM of labelled fields, and a field's value is the text between its label
/// and the next label, in the label's column — beside the label when the value shares its line
/// (SCALE: 1/8" = 1'-0"), below it otherwise (SHEET TITLE over two 16 pt lines, down to SHEET NUMBER).
/// SHEET TITLE (also labelled TITLE or DRAWING TITLE) reads beside its label or down its column to
/// the next label, joining value lines in the block's reading coordinates.
/// </summary>
/// <remarks>
/// Measured 2026-09-08 on 31130, 31138 and 31168, the three KOR-drafted sets: every title block
/// carries the same labels at 8.1 pt — SEAL, CONSULTANT, PROJECT TITLE, PROJECT NO:, DRAWN BY:,
/// CHK'D BY:, SCALE:, SHEET TITLE, SHEET NUMBER, REV: — in one column at the right edge. Before
/// this existed the sheet's title was guessed from the largest title-size text on the right edge,
/// which on 31138 was the architect's name, then the sheet number, then "Location:". Reading the
/// field under the label the drafter wrote is not a heuristic.
///
/// WHAT IT COVERS: the labels below, matched as a whole token or two anywhere in a line in the
/// right fifth of the page, with the trailing colon ignored; a value beside the label on its line,
/// else the lines under it until the next label. Every value is the joined text of its lines.
/// WHAT IT DOES NOT: blocks without these labels (31065's pdfFactory set, 31202's San Diego block)
/// yield an empty dictionary and the callers fall back to what they did before; a label whose value
/// box is empty yields no entry. Rotation direction is not encoded in TextToken: a rotated strip is
/// read bottom-up, as on 01589-01 p7; top-down lettering cannot be distinguished from these boxes.
/// </remarks>
public static class TitleBlockFields
{
    /// <summary>Office form labels; a two-word label always beats either of its individual words.</summary>
    public static readonly string[] Labels =
    {
        "PRIME CONSULTANT", "PROJECT TITLE", "PROJECT NUMBER", "PROJECT NO", "DRAWING TITLE", "SHEET TITLE",
        "SHEET NUMBER", "SHEET NO", "DESIGNED BY", "CHECKED BY", "CHK'D BY", "DRAWN BY", "ISSUED FOR",
        // A field ends at the next office form label, not just the KOR template's labels:
        // 30878-02 p35: PROJ. # / DRAWING NUMBER; 30912-01 p20: DRAWING NO;
        // 01379-01 p77: JOB NO. / JOB TITLE; 01589-01 p7: JOB # / ISSUES.
        // DRAWING #, PROJECT #, JOB NUMBER, PLOT DATE, FILE and SHEET are the companion
        // spellings supplied by the title-block brief, not individually measured on those pages.
        "DRAWING NUMBER", "DRAWING NO", "DRAWING #", "PROJ. #", "PROJECT #", "JOB NO", "JOB NUMBER",
        "JOB TITLE", "JOB #", "PLOT DATE", "FILE LOCATION", "CONSULTANT", "REVISIONS", "REVISION", "ISSUES",
        "CHECKED", "DRAWN", "CLIENT", "SCALE", "DATE", "SEAL", "REV", "TITLE", "FILE", "SHEET",
    };

    private const double RegionMinFx = 0.80;

    public static IReadOnlyDictionary<string, string> Read(VectorPageReader.PageContent? page) => Read(page, out _);

    /// <summary>
    /// The fields, and the centres of every token the reader consumed — each label's own tokens and
    /// the tokens that became a kept value — so the ledger can call those words read and the rest
    /// of the title block unread, rather than the whole block one or the other (audit F10).
    /// </summary>
    public static IReadOnlyDictionary<string, string> Read(VectorPageReader.PageContent? page, out IReadOnlySet<(double X, double Y)> consumedTokens)
    {
        var consumed = new HashSet<(double X, double Y)>();
        consumedTokens = consumed;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (page is null || page.Words.Count == 0 || page.WidthPts <= 0) return fields;

        var tokens = ReadingTokens(page, RegionMinFx, out bool rotated);
        if (tokens.Count == 0) return fields;
        (double X, double Y) OriginalCentre(VectorPageReader.TextToken t) => rotated ? (-t.Cy, t.Cx) : (t.Cx, t.Cy);

        var lines = ReadingLines(tokens);

        // Label instances: at any position in a line, one or two tokens spelling a label.
        var labels = new List<(string Label, int Line, int From, int To, double MinX, double Cy)>();
        for (int li = 0; li < lines.Count; li++)
        {
            var l = lines[li];
            for (int i = 0; i < l.Count; i++)
            {
                string one = Clean(l[i].Text);
                string two = i + 1 < l.Count ? one + " " + Clean(l[i + 1].Text) : "";
                string? hit = Labels.FirstOrDefault(x => Clean(x) == two);
                int span = 2;
                if (hit is null) { hit = Labels.FirstOrDefault(x => Clean(x) == one); span = 1; }
                if (hit is null) continue;
                // 01379-01 p77: match JOB/PROJECT TITLE as a pair wherever it stands, before TITLE;
                // DRAWING TITLE is the sheet's title, while JOB/PROJECT TITLE keep their identities.
                labels.Add((hit is "TITLE" or "DRAWING TITLE" ? "SHEET TITLE" : hit, li, i, i + span - 1, l[i].MinX, l[i].Cy));
                i += span - 1;
            }
        }
        if (labels.Count == 0) return fields;

        // A label's own tokens are never part of any value.
        var labelTokens = new HashSet<(int Line, int Index)>();
        foreach (var lab in labels)
            for (int i = lab.From; i <= lab.To; i++) labelTokens.Add((lab.Line, i));
        bool IsLabelToken(int line, int index) => labelTokens.Contains((line, index));

        foreach (var lab in labels)
        {
            var line = lines[lab.Line];
            for (int i = lab.From; i <= lab.To; i++) consumed.Add(OriginalCentre(line[i]));
            // beside: the tokens after the label on its line, up to the next label on that line
            int stop = labels.Where(o => o.Line == lab.Line && o.From > lab.To).Select(o => o.From).DefaultIfEmpty(line.Count).Min();
            var besideTokens = line.Skip(lab.To + 1).Take(stop - lab.To - 1).Where(t => t.Text.Trim().Length > 0).ToList();
            if (besideTokens.Count > 0)
            {
                if (Set(fields, lab.Label, string.Join(" ", besideTokens.Select(t => t.Text.Trim()))))
                    foreach (var t in besideTokens) consumed.Add(OriginalCentre(t));
                continue;
            }
            // below: the lines under the label until the next LABEL LINE below it — the block is one
            // column, so a label anywhere across it ends the field above (SHEET NUMBER ends SHEET
            // TITLE; PROJECT NO ends PROJECT TITLE). Tokens in the label's column only.
            // the column ends where the next label on the label's own line begins (SHEET NUMBER | REV:)
            double right = labels.Where(o => o.Line == lab.Line && o.From > lab.To).Select(o => o.MinX).DefaultIfEmpty(lab.MinX + 260).Min();
            // 30912-01 p20: a neighbouring column already labelled above this field bounds it too;
            // a short CHECKED BY column must not inherit the title column's words via the 260 pt default.
            right = labels.Where(o => o.Cy >= lab.Cy - 1 && o.MinX > lab.MinX + 15 && o.MinX < right)
                .Select(o => o.MinX).DefaultIfEmpty(right).Min();
            // and the field ends at the next label BELOW IN THAT COLUMN — a REV label in the next
            // column does not cut the SHEET TITLE off (audit F11, 2026-09-08)
            double floor = labels.Where(o => o.Cy < lab.Cy - 1 && o.MinX >= lab.MinX - 15 && o.MinX < right).Select(o => o.Cy).DefaultIfEmpty(double.NegativeInfinity).Max();
            var valueLines = new List<List<VectorPageReader.TextToken>>();
            for (int li = 0; li < lines.Count; li++)
            {
                var l = lines[li];
                var used = l.Select((t, i) => (t, i))
                    .Where(x => !IsLabelToken(li, x.i) && x.t.Cy < lab.Cy - 1 && x.t.Cy > floor + 1
                        && x.t.MinX >= lab.MinX - 15 && x.t.MinX < right && x.t.Text.Trim().Length > 0)
                    .Select(x => x.t).ToList();
                if (used.Count == 0) continue;
                valueLines.Add(used);
            }
            // 30912-01 p20: a lone digit on its own baseline in the rightmost fifth, BETWEEN
            // title lines, is a revision mark. Keep a final lone number: it may finish the title.
            var belowTokens = valueLines.Where((l, i) => !(lab.Label == "SHEET TITLE" && i > 0 && i < valueLines.Count - 1
                    && l.Count == 1 && l[0].Text.Trim().Length == 1 && char.IsDigit(l[0].Text.Trim()[0])
                    && l[0].Cx >= lab.MinX + 0.8 * (right - lab.MinX)))
                .SelectMany(l => l).ToList();
            if (belowTokens.Count > 0 && Set(fields, lab.Label, string.Join(" ", belowTokens.Select(t => t.Text.Trim()))))
                foreach (var t in belowTokens) consumed.Add(OriginalCentre(t));
        }
        return fields;
    }

    // Periods and a trailing colon punctuate a form label (JOB NO. / PROJ. #); they are not its identity.
    private static string Clean(string s) => s.Trim().TrimEnd(':').Replace(".", "").Trim().ToUpperInvariant();

    /// <summary>The right-edge words in one reading coordinate system, also used by TitleText's fallback.</summary>
    /// <param name="dropUpright">Leave out the words drawn up the page (a rotated stamp, "SSI 021 2024-02-05 12:10 PM"): the
    /// field reader's default. THE TITLE READER KEEPS THEM (2026-09-15, run 19): KOR's own upright strip writes the sheet
    /// title up the page beside upright labels - 30941's "LEVEL B4 RAFT FOUNDATION PLAN" - and dropping it here left the
    /// stamp as the title ("SSI PM") on 85 sets' storeys.</param>
    internal static List<VectorPageReader.TextToken> ReadingTokens(VectorPageReader.PageContent page, double regionMinFx, out bool rotated, bool dropUpright = true)
    {
        var band = page.Words.Where(t => t.Cx / page.WidthPts >= RegionMinFx && t.Text.Trim().Length > 0).ToList();
        // 01589-01 p7: several tall label runs sharing an X band, spread over Y, describe a rotated
        // block. Requiring three distinct labels avoids rotating a horizontal block for its lone
        // vertical FILE LOCATION. Box shape distinguishes this from ordinary left-aligned labels.
        var uprightLabels = band.Where(t => t.Width > 0 && t.Height > 2 * t.Width
            && Labels.Any(l => Clean(l) == Clean(t.Text))).ToList();
        rotated = uprightLabels.Any(a =>
        {
            var column = uprightLabels.Where(t => Math.Abs(t.MinX - a.MinX) <= Math.Max(t.Width, a.Width)).ToList();
            return column.Select(t => Clean(t.Text)).Distinct().Count() >= 3
                && column.Max(t => t.Cy) - column.Min(t => t.Cy) > 3 * column.Max(t => t.Width);
        });
        bool swap = rotated;
        return page.Words.Where(t => t.Cx / page.WidthPts >= regionMinFx && t.Text.Trim().Length > 0)
            // Bottom-up along Y, then left-to-right across X: inverse centres are (-Cy, Cx).
            .Select(t => swap ? new VectorPageReader.TextToken(t.Text, t.Cy, -t.Cx, t.MinY, -t.MaxX, t.MaxY, -t.MinX) : t)
            .Where(t => !dropUpright || !(t.Text.Trim().Length >= 3 && t.Height > 2 * t.Width)).ToList();
    }

    internal static List<List<VectorPageReader.TextToken>> ReadingLines(IReadOnlyList<VectorPageReader.TextToken> tokens)
    {
        // 30926-01 p18: a short glyph belongs beside the nearest word on its baseline, not after
        // an arbitrary first token of a page-wide line. Establish word baselines before punctuation,
        // using the tallest word as the reference; punctuation cannot move that reference.
        // The supplied 3.4 pt / 12.5 pt pair already passes the old test: its reported failure needs
        // an earlier line seed or different boxes, neither of which the brief supplies.
        var lines = new List<List<VectorPageReader.TextToken>>();
        foreach (var t in tokens.OrderByDescending(t => t.Text.Any(char.IsLetterOrDigit))
                     .ThenByDescending(t => t.Height).ThenByDescending(t => t.Cy).ThenBy(t => t.Cx))
        {
            var line = lines.Where(l => Math.Abs(l[0].Cy - t.Cy) <= Math.Max(l[0].Height, t.Height) * 0.5)
                .OrderBy(l => Math.Abs(l[0].Cy - t.Cy)).ThenBy(l => Math.Abs(l[0].Cx - t.Cx)).FirstOrDefault();
            if (line is null) lines.Add(new List<VectorPageReader.TextToken> { t });
            else line.Add(t);
        }
        lines = lines.OrderByDescending(l => l[0].Cy).ToList();
        foreach (var line in lines) line.Sort((a, b) => a.Cx.CompareTo(b.Cx));
        return lines;
    }

    /// <summary>The first instance of a label wins; a block drawn twice (fake bold) repeats its labels. True when this one was kept.</summary>
    private static bool Set(Dictionary<string, string> fields, string label, string value)
    {
        if (fields.ContainsKey(label)) return false;
        fields[label] = value;
        return true;
    }
}
