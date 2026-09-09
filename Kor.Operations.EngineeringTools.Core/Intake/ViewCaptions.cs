using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.Intake;

/// <summary>
/// A view's scale is the ratio in the caption under it. On a sheet whose title block says AS NOTED,
/// every view carries its own caption — the view number, the sheet reference and the ratio on one
/// baseline ("3 / S3.11  1/8" = 1'-0"") — and a ladder or a plan belongs to the caption nearest
/// below it.
/// </summary>
/// <remarks>
/// Measured 2026-09-08 (`takeoff elev-scan`): 31138 p53 (SHEAR WALL ELEVATIONS, AS NOTED) carries
/// "S3.11 1/8" = 1'-0"" twice at fy 0.04 under two ladders at fx 0.50 and 0.72; p57 five captions
/// under five views; 31130 p53 three under three wall elevations, besides the title block's own.
/// The captions carry no SCALE label, so `SheetScaleReader.ScaleNotesAnywhere` found none of them.
/// WHAT IT COVERS: a ratio-shaped run of tokens on one baseline — imperial "N/D" = 1'-0"" and
/// metric "1 : N" — anywhere on the sheet outside the title block, with its position. WHAT IT
/// DOES NOT: a caption whose ratio the word extractor split into pieces this reader does not
/// rejoin (it rejoins the "=" form only), and which VIEW a caption belongs to — the caller pairs
/// a caption with what sits above it.
/// </remarks>
public static class ViewCaptions
{
    public sealed record Caption(string Note, double XPts, double YPts);

    private static readonly Regex Imperial = new(@"^\d+(?:/\d+)?""$", RegexOptions.Compiled);
    private static readonly Regex OneFoot = new(@"^1'-0""?$", RegexOptions.Compiled);
    private static readonly Regex MetricOne = new(@"^1\s*:\s*\d{2,4}$", RegexOptions.Compiled);
    private const double TitleRegionMinFx = 0.80;

    /// <summary>Every view caption on the page, outside the title block's fifth of the width.</summary>
    public static IReadOnlyList<Caption> Read(VectorPageReader.PageContent page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var captions = new List<Caption>();
        var words = page.Words;
        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            if (w.Cx / page.WidthPts >= TitleRegionMinFx) continue;
            string t = w.Text.Trim();
            if (MetricOne.IsMatch(t))
            {
                captions.Add(new Caption(Regex.Replace(t, @"\s+", ""), w.Cx, w.Cy));
                continue;
            }
            if (!OneFoot.IsMatch(t)) continue;
            // the imperial form: <fraction"> = 1'-0", the two left neighbours on the same baseline
            var left = words.Where(o => Math.Abs(o.Cy - w.Cy) <= 4 && o.Cx < w.Cx && w.MinX - o.MaxX <= 40).OrderByDescending(o => o.Cx).Take(2).ToList();
            if (left.Count == 2 && left[0].Text.Trim() == "=" && Imperial.IsMatch(left[1].Text.Trim()))
                captions.Add(new Caption($"{left[1].Text.Trim()} = 1'-0\"", (left[1].Cx + w.Cx) / 2, w.Cy));
            else if (left.Count >= 1 && Imperial.IsMatch(left[0].Text.Trim()))
                captions.Add(new Caption($"{left[0].Text.Trim()} = 1'-0\"", (left[0].Cx + w.Cx) / 2, w.Cy));
        }
        return captions;
    }

    /// <summary>
    /// The caption a thing at (<paramref name="xPts"/>, above <paramref name="bottomPts"/>) belongs
    /// to: the nearest one below its bottom, by horizontal distance; null when none is below it.
    /// When every caption on the sheet states one ratio, that ratio, wherever the thing sits.
    /// </summary>
    public static string? For(IReadOnlyList<Caption> captions, double xPts, double bottomPts)
    {
        ArgumentNullException.ThrowIfNull(captions);
        if (captions.Count == 0) return null;
        var notes = captions.Select(c => c.Note).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (notes.Count == 1) return notes[0];
        var below = captions.Where(c => c.YPts < bottomPts).OrderBy(c => Math.Abs(c.XPts - xPts)).ThenByDescending(c => c.YPts).FirstOrDefault();
        return below?.Note;
    }
}
