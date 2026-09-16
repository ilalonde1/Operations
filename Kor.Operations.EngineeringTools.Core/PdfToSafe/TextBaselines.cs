using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.PdfToSafe;

/// <summary>
/// WORDS ARE ON ONE BASELINE WHEN THEIR BASELINES ARE CLOSE, NOT WHEN THEY ROUND TO THE SAME POINT (intake step 81,
/// 2026-09-15). Every line-of-text reader grouped tokens by <c>Math.Round(Cy)</c>: on 01379's S231.3 the title
/// "Concrete Outline - Level 23 Plan B - West Tower" has its "23" at 1201.506 pt and "Level … Tower" at 1201.478 pt,
/// 0.03 pt apart, on either side of 1201.5 - two lines, "Concrete Outline 23" and "Level Plan B West Tower", neither
/// naming a plan, and the sheet's second view was lost; its reinforcing view and its outline were written as one
/// file and the composer set that file on the grid by whichever copy of each axis name it took, 37.6 m off on
/// five storeys. S230.3 three pages earlier, the same title, rounded together by luck. A baseline is a physical
/// quantity; two words a thirtieth of a point apart are one line whatever integer they are nearest.
/// </summary>
public static class TextBaselines
{
    /// <summary>What share of the taller token's height two baselines may differ by and still be one line. A dash
    /// is drawn a quarter of a word's height above the word's baseline ("Plan B - West": 4.0 pt on a 14 pt line) and
    /// stays its own line at this, as it did under the rounding.</summary>
    public const double SameLineFraction = 0.25;

    /// <summary>
    /// The page's tokens in lines, top of the page first: each line is a run of tokens whose baselines lie within
    /// <see cref="SameLineFraction"/> of the taller height from the line's first token, and which are contiguous
    /// across within <paramref name="gapHeights"/> heights (a wider gap starts a new line at the same baseline).
    /// Tokens in a line are left to right.
    /// </summary>
    public static IEnumerable<IReadOnlyList<VectorPageReader.TextToken>> Lines(IEnumerable<VectorPageReader.TextToken> words, double gapHeights = 2.0)
    {
        ArgumentNullException.ThrowIfNull(words);
        var byBaseline = words.OrderByDescending(t => t.Cy).ToList();
        int i = 0;
        while (i < byBaseline.Count)
        {
            var first = byBaseline[i];
            var band = new List<VectorPageReader.TextToken> { first };
            int j = i + 1;
            while (j < byBaseline.Count)
            {
                var t = byBaseline[j];
                if (first.Cy - t.Cy > SameLineFraction * Math.Max(first.Height, t.Height)) break;
                band.Add(t);
                j++;
            }
            i = j;
            var run = new List<VectorPageReader.TextToken>();
            foreach (var t in band.OrderBy(t => t.MinX))
            {
                if (run.Count > 0 && t.MinX - run[^1].MaxX > gapHeights * Math.Max(t.Height, run.Max(r => r.Height)))
                {
                    yield return run;
                    run = new List<VectorPageReader.TextToken>();
                }
                run.Add(t);
            }
            if (run.Count > 0) yield return run;
        }
    }
}
