using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// A title block is a FORM of labelled fields, and a field's value is the text between its label
/// and the next label, in the label's column — beside the label when the value shares its line
/// (SCALE: 1/8" = 1'-0"), below it otherwise (SHEET TITLE over two 16 pt lines, down to SHEET NUMBER).
/// </summary>
/// <remarks>
/// Measured 2026-09-08 on 31130, 31138 and 31168, the three KOR-drafted sets: every title block
/// carries the same labels at 8.1 pt — SEAL, CONSULTANT, PROJECT TITLE, PROJECT NO:, DRAWN BY:,
/// CHK'D BY:, SCALE:, SHEET TITLE, SHEET NUMBER, REV: — in one column at the right edge. Before
/// this existed the sheet's title was guessed from the largest title-size text on the right edge,
/// which on 31138 was the architect's name, then the sheet number, then "Location:". Reading the
/// field under the label the drafter wrote is not a heuristic.
///
/// WHAT IT COVERS: the labels below, matched as a whole token or two at the start of a line in the
/// right fifth of the page, with the trailing colon ignored; a value beside the label on its line,
/// else the lines under it until the next label. Every value is the joined text of its lines.
/// WHAT IT DOES NOT: blocks without these labels (31065's pdfFactory set, 31202's San Diego block)
/// yield an empty dictionary and the callers fall back to what they did before; a label whose value
/// box is empty yields no entry; a value's line order is top to bottom by baseline.
/// </remarks>
public static class TitleBlockFields
{
    /// <summary>The field labels a KOR title block carries, longest first so "SHEET TITLE" beats "SHEET".</summary>
    public static readonly string[] Labels =
    {
        "PRIME CONSULTANT", "PROJECT TITLE", "PROJECT NUMBER", "PROJECT NO", "DRAWING TITLE", "SHEET TITLE",
        "SHEET NUMBER", "SHEET NO", "DESIGNED BY", "CHECKED BY", "CHK'D BY", "DRAWN BY", "ISSUED FOR",
        "FILE LOCATION", "CONSULTANT", "REVISIONS", "REVISION", "CLIENT", "SCALE", "DATE", "SEAL", "REV",
    };

    private const double RegionMinFx = 0.80;

    public static IReadOnlyDictionary<string, string> Read(VectorPageReader.PageContent? page)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (page is null || page.Words.Count == 0 || page.WidthPts <= 0) return fields;

        // Horizontal tokens only: a word set vertically along the sheet edge ("File Location" on
        // KOR blocks) is tall and narrow, and its centre lands on whatever baseline it crosses —
        // on 31138 and 31168 that was the sheet number's, and SHEET NUMBER read "Location: S2.01 File".
        var tokens = page.Words
            .Where(t => t.Cx / page.WidthPts >= RegionMinFx && t.Text.Trim().Length > 0
                        && !(t.Text.Trim().Length >= 3 && t.Height > 2.0 * t.Width))
            .ToList();
        if (tokens.Count == 0) return fields;

        // Lines by baseline: a token joins the line whose centre is within half its height.
        var lines = new List<List<VectorPageReader.TextToken>>();
        foreach (var t in tokens.OrderByDescending(t => t.Cy).ThenBy(t => t.Cx))
        {
            var line = lines.FirstOrDefault(l => Math.Abs(l[0].Cy - t.Cy) <= Math.Max(l[0].Height, t.Height) * 0.5);
            if (line is null) lines.Add(new List<VectorPageReader.TextToken> { t });
            else line.Add(t);
        }
        foreach (var l in lines) l.Sort((a, b) => a.Cx.CompareTo(b.Cx));

        // Label instances: at any position in a line, one or two tokens spelling a label.
        var labels = new List<(string Label, int Line, int From, int To, double MinX, double Cy)>();
        for (int li = 0; li < lines.Count; li++)
        {
            var l = lines[li];
            for (int i = 0; i < l.Count; i++)
            {
                string one = Clean(l[i].Text);
                string two = i + 1 < l.Count ? one + " " + Clean(l[i + 1].Text) : "";
                string? hit = Labels.FirstOrDefault(x => x.Equals(two, StringComparison.OrdinalIgnoreCase));
                int span = 2;
                if (hit is null) { hit = Labels.FirstOrDefault(x => x.Equals(one, StringComparison.OrdinalIgnoreCase)); span = 1; }
                if (hit is null) continue;
                labels.Add((hit, li, i, i + span - 1, l[i].MinX, l[i].Cy));
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
            // beside: the tokens after the label on its line, up to the next label on that line
            int stop = labels.Where(o => o.Line == lab.Line && o.From > lab.To).Select(o => o.From).DefaultIfEmpty(line.Count).Min();
            var beside = line.Skip(lab.To + 1).Take(stop - lab.To - 1).Select(t => t.Text.Trim()).Where(s => s.Length > 0).ToList();
            if (beside.Count > 0)
            {
                Set(fields, lab.Label, string.Join(" ", beside));
                continue;
            }
            // below: the lines under the label until the next LABEL LINE below it — the block is one
            // column, so a label anywhere across it ends the field above (SHEET NUMBER ends SHEET
            // TITLE; PROJECT NO ends PROJECT TITLE). Tokens in the label's column only.
            double floor = labels.Where(o => o.Cy < lab.Cy - 1).Select(o => o.Cy).DefaultIfEmpty(double.NegativeInfinity).Max();
            // the column ends where the next label on the label's own line begins (SHEET NUMBER | REV:)
            double right = labels.Where(o => o.Line == lab.Line && o.From > lab.To).Select(o => o.MinX).DefaultIfEmpty(lab.MinX + 260).Min();
            var below = new List<string>();
            for (int li = 0; li < lines.Count; li++)
            {
                var l = lines[li];
                if (!(l[0].Cy < lab.Cy - 1 && l[0].Cy > floor + 1)) continue;
                string text = string.Join(" ", l.Select((t, i) => (t, i))
                    .Where(x => !IsLabelToken(li, x.i) && x.t.MinX >= lab.MinX - 15 && x.t.MinX < right)
                    .Select(x => x.t.Text.Trim()).Where(s => s.Length > 0));
                if (text.Length > 0) below.Add(text);
            }
            if (below.Count > 0) Set(fields, lab.Label, string.Join(" ", below));
        }
        return fields;
    }

    private static string Clean(string s) => s.Trim().TrimEnd(':').Trim().ToUpperInvariant();

    private static void Set(Dictionary<string, string> fields, string label, string value)
    {
        // The first instance of a label wins; a block drawn twice (fake bold) repeats its labels.
        if (!fields.ContainsKey(label)) fields[label] = value;
    }
}
