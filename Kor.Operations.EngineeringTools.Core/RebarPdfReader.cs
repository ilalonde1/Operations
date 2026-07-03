#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace Kor.Operations.EngineeringTools.RebarChange
{
    /// <summary>
    /// THE reinforcing reader for PDF drawing sets: positioned words (fake-bold double-draws removed)
    /// → assembled call-outs with their boxes → pages grouped under their OWN sheet number (title-block
    /// rule). Extracted from the overlay generator so the PDF markup and the change/weight report read
    /// the drawings through ONE pipeline — two extraction paths had two sheet-ownership rules and two
    /// tokenizers, and their reports disagreed on counts. The reports must tell one story.
    /// </summary>
    public static class RebarPdfReader
    {
        private static readonly Regex SheetRe =
            new(@"\bS\d{1,2}(?:\.\d{1,2}){1,3}[A-Z]?\b", RegexOptions.Compiled);

        /// <summary>One assembled reinforcing call-out and its bounding box (PDF points).</summary>
        public sealed record Hit(string Key, double Left, double Bottom, double Width, double Height);

        public sealed class PageModel
        {
            public int Num;
            public double W, H;
            public List<(string Token, double Height, double Cx, double Cy)> SheetHits = new();
            public List<Hit> Callouts = new();
        }

        public static List<PageModel> Read(PdfDocument doc, UnitSystem unit)
        {
            var pages = new List<PageModel>();
            foreach (var page in doc.GetPages())
            {
                // Fake-bold double-draws out first: a bolded-but-unchanged call-out must not read
                // as a second occurrence (it made the diff box unchanged schedule cells as "added").
                var words = PdfWordDedupe.Filter(page.GetWords());
                var hits = new List<(string, double, double, double)>();
                foreach (var w in words)
                    foreach (Match m in SheetRe.Matches(w.Text))
                    {
                        var bb = w.BoundingBox;
                        hits.Add((m.Value, bb.Height, (bb.Left + bb.Right) / 2.0, (bb.Bottom + bb.Top) / 2.0));
                    }
                pages.Add(new PageModel
                {
                    Num = page.Number,
                    W = page.Width,
                    H = page.Height,
                    SheetHits = hits,
                    Callouts = AssembleCallouts(words, unit),
                });
            }
            return pages;
        }

        // sheet -> owning pages. Own sheet = the title-block number (largest sheet token in the
        // bottom-right title block, else the largest on the page). Robust on details sheets, where
        // the own number recurs in every detail bubble so a frequency rule wrongly picks a
        // cross-reference. Index/cover pages (many distinct tokens) are excluded.
        public static Dictionary<string, List<PageModel>> OwnSheet(List<PageModel> pages)
        {
            var map = new Dictionary<string, List<PageModel>>();
            foreach (var pg in pages)
            {
                if (pg.SheetHits.Count == 0) continue;
                if (pg.SheetHits.Select(h => h.Token).Distinct().Count() > 30) continue; // index/cover
                var own = OwnFor(pg);
                if (own is null) continue;
                if (!map.TryGetValue(own, out var list)) map[own] = list = new List<PageModel>();
                list.Add(pg);
            }
            return map;
        }

        private static string? OwnFor(PageModel pg)
        {
            var inBlock = pg.SheetHits.Where(h => h.Cx > 0.72 * pg.W && h.Cy < 0.28 * pg.H).ToList();
            var pool = inBlock.Count > 0 ? inBlock : pg.SheetHits;
            return pool.OrderByDescending(h => h.Height).ThenByDescending(h => h.Cx).First().Token;
        }

        /// <summary>Per-sheet call-out multiset (key → occurrence count) for a whole set — the input
        /// the change/weight comparison consumes, from the same read the markup boxes come from.</summary>
        public static Dictionary<string, Dictionary<string, int>> SheetCounts(
            Dictionary<string, List<PageModel>> map)
        {
            var bySheet = new Dictionary<string, Dictionary<string, int>>();
            foreach (var (sheet, pages) in map)
            {
                var c = new Dictionary<string, int>();
                foreach (var pg in pages)
                    foreach (var h in pg.Callouts)
                        c[h.Key] = c.GetValueOrDefault(h.Key) + 1;
                bySheet[sheet] = c;
            }
            return bySheet;
        }

        // Assemble call-outs from positioned words, walking in reading order:
        //   • PLAN form "36-15M4700 [@ 125]" (count-size-LENGTH, the weighable one)
        //   • intensity form "15M @ 200" (metric) / "#5 @ 12" (imperial)
        // Glued punctuation is trimmed once for every grammar so "2-15M6000," still reads.
        public static List<Hit> AssembleCallouts(IReadOnlyList<Word> words, UnitSystem unit)
        {
            bool imp = unit == UnitSystem.Imperial;
            var full = imp ? new Regex(@"^#(\d{1,2})@(\d{1,2})[""″]?$") : new Regex(@"^(\d{2})M@(\d{2,4})$");
            var start = imp ? new Regex(@"^#(\d{1,2})@?$") : new Regex(@"^(\d{2})M@?$");
            var space = imp ? new Regex(@"^(\d{1,2})[""″]?$") : new Regex(@"^(\d{2,4})$");
            int smin = imp ? 3 : 75, smax = imp ? 48 : 750;

            // Metric plan spacings only — never the unit-dependent intensity spacing pattern, which in
            // an imperial run would misread the metric mm spacing that plan call-outs always use.
            var planSpace = new Regex(@"^(\d{2,4})$");

            var outp = new List<Hit>();
            for (int i = 0; i < words.Count; i++)
            {
                var t = RebarPlanCallout.TrimWordToken(words[i].Text);

                // PLAN call-out: the qty-size-LENGTH token is the anchor — it alone identifies (and
                // weighs) the call-out; the spacing joins when it follows as "@" + "125" (or glued).
                // Checked before the intensity forms; the glued mm length keeps the grammars disjoint.
                if (RebarPlanCallout.TryParseWordToken(t, out bool gluedSpacing) is { } pc)
                {
                    var box = words[i].BoundingBox;
                    if (!gluedSpacing)
                        for (int j = i + 1; j <= Math.Min(i + 2, words.Count - 1); j++)
                        {
                            if (words[j].Text == "@") continue;
                            var msp2 = planSpace.Match(RebarPlanCallout.TrimWordToken(words[j].Text));
                            if (msp2.Success && int.TryParse(msp2.Groups[1].Value, out int sp)
                                && sp >= RebarPlanCallout.SpacingMinMm && sp <= RebarPlanCallout.SpacingMaxMm)
                            {
                                pc = pc with { SpacingMm = sp };
                                var b2 = words[j].BoundingBox;
                                box = new PdfRectangle(
                                    Math.Min(box.Left, b2.Left), Math.Min(box.Bottom, b2.Bottom),
                                    Math.Max(box.Right, b2.Right), Math.Max(box.Top, b2.Top));
                            }
                            break;
                        }
                    outp.Add(new Hit(pc.Key, box.Left, box.Bottom, box.Width, box.Height));
                    continue;
                }

                var mf = full.Match(t);
                if (mf.Success)
                {
                    AddHit(outp, imp, int.Parse(mf.Groups[1].Value), int.Parse(mf.Groups[2].Value), smin, smax, words[i].BoundingBox);
                    continue;
                }
                var ms = start.Match(t);
                if (!ms.Success) continue;
                for (int j = i + 1; j <= Math.Min(i + 2, words.Count - 1); j++)
                {
                    if (words[j].Text == "@") continue;
                    var msp = space.Match(RebarPlanCallout.TrimWordToken(words[j].Text));
                    if (msp.Success)
                    {
                        var b1 = words[i].BoundingBox;
                        var b2 = words[j].BoundingBox;
                        var union = new PdfRectangle(
                            Math.Min(b1.Left, b2.Left), Math.Min(b1.Bottom, b2.Bottom),
                            Math.Max(b1.Right, b2.Right), Math.Max(b1.Top, b2.Top));
                        AddHit(outp, imp, int.Parse(ms.Groups[1].Value), int.Parse(msp.Groups[1].Value), smin, smax, union);
                    }
                    break;
                }
            }
            return outp;
        }

        private static void AddHit(List<Hit> outp, bool imp, int size, int spacing, int smin, int smax, PdfRectangle box)
        {
            if (spacing < smin || spacing > smax) return;
            string key = imp ? $"#{size}@{spacing}" : $"{size}M@{spacing}";
            outp.Add(new Hit(key, box.Left, box.Bottom, box.Width, box.Height));
        }
    }
}
