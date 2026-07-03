#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Outline;
using UglyToad.PdfPig.Outline.Destinations;
using UglyToad.PdfPig.Writer;

namespace Kor.Operations.EngineeringTools.RebarChange
{
    /// <summary>
    /// Visual "spot the difference" markup: copies each changed drawing sheet and boxes the rebar
    /// call-outs that changed — on the BEFORE sheet the removed ones (red), on the AFTER sheet the
    /// added ones (green) — paired so flipping the pages is the before/after toggle. Vectors are
    /// preserved (PdfPig page copy), so the sealed drawing is untouched. This is the in-app port of
    /// the verified standalone 31065 deliverable.
    /// </summary>
    public static class RebarOverlayGenerator
    {
        private static readonly Regex SheetRe =
            new(@"\bS\d{1,2}(?:\.\d{1,2}){1,3}[A-Z]?\b", RegexOptions.Compiled);

        private static readonly (byte R, byte G, byte B) Red = (200, 30, 30);
        private static readonly (byte R, byte G, byte B) Green = (0, 140, 55);
        private static readonly (byte R, byte G, byte B) Navy = (31, 56, 100);

        public static byte[] Build(
            string beforePdfPath, string afterPdfPath,
            string projectName, string beforeLabel = "Before", string afterLabel = "After",
            UnitSystem unit = UnitSystem.Metric)
        {
            using var before = PdfDocument.Open(beforePdfPath);
            using var after = PdfDocument.Open(afterPdfPath);
            return BuildCore(before, after, projectName, beforeLabel, afterLabel, unit);
        }

        public static byte[] Build(
            byte[] beforePdf, byte[] afterPdf,
            string projectName, string beforeLabel = "Before", string afterLabel = "After",
            UnitSystem unit = UnitSystem.Metric)
        {
            using var before = PdfDocument.Open(beforePdf);
            using var after = PdfDocument.Open(afterPdf);
            return BuildCore(before, after, projectName, beforeLabel, afterLabel, unit);
        }

        private sealed record Hit(string Key, double Left, double Bottom, double Width, double Height);

        private sealed class PageModel
        {
            public int Num;
            public double W, H;
            public List<(string Token, double Height, double Cx, double Cy)> SheetHits = new();
            public List<Hit> Callouts = new();
        }

        private static byte[] BuildCore(
            PdfDocument before, PdfDocument after,
            string projectName, string beforeLabel, string afterLabel, UnitSystem unit)
        {
            var bPages = Read(before, unit);
            var aPages = Read(after, unit);
            var bMap = OwnSheet(bPages);
            var aMap = OwnSheet(aPages);

            var sheets = bMap.Keys.Union(aMap.Keys).Distinct().OrderBy(SortKey).ToList();

            // Per sheet: which call-out keys were added (on after) / removed (off before).
            var plan = new List<(string Sheet, HashSet<string> Added, HashSet<string> Removed)>();
            foreach (var s in sheets)
            {
                var bc = Counts(bMap, s);
                var ac = Counts(aMap, s);
                var added = ac.Keys.Where(k => ac[k] > bc.GetValueOrDefault(k)).ToHashSet();
                var removed = bc.Keys.Where(k => bc[k] > ac.GetValueOrDefault(k)).ToHashSet();
                if (added.Count > 0 || removed.Count > 0)
                    plan.Add((s, added, removed));
            }

            var builder = new PdfDocumentBuilder();
            var font = builder.AddStandard14Font(Standard14Font.HelveticaBold);

            // Decide every page BEFORE drawing anything, so the cover can print each sheet's page
            // number and the document outline can point at real destinations. Only a page that
            // actually carries a box is emitted — a sheet with additions only must not produce a
            // blank "removed in RED" before-page (and vice-versa), which reads as broken.
            var emissions = new List<(PageModel Pg, bool FromBefore, HashSet<string> Keys,
                (byte R, byte G, byte B) Color, string Sheet, string Label)>();
            foreach (var (sheet, added, removed) in plan)
            {
                if (removed.Count > 0 && bMap.TryGetValue(sheet, out var bp))
                    foreach (var pg in bp)
                    {
                        int boxes = pg.Callouts.Count(h => removed.Contains(h.Key));
                        int keys = pg.Callouts.Where(h => removed.Contains(h.Key)).Select(h => h.Key).Distinct().Count();
                        if (boxes > 0) emissions.Add((pg, true, removed, Red, sheet,
                            $"{sheet}  -  {beforeLabel}   (removed reinforcing in RED - {keys} call-out(s), {boxes} box(es))"));
                    }

                if (added.Count > 0 && aMap.TryGetValue(sheet, out var ap))
                    foreach (var pg in ap)
                    {
                        int boxes = pg.Callouts.Count(h => added.Contains(h.Key));
                        int keys = pg.Callouts.Where(h => added.Contains(h.Key)).Select(h => h.Key).Distinct().Count();
                        if (boxes > 0) emissions.Add((pg, false, added, Green, sheet,
                            $"{sheet}  -  {afterLabel}   (added reinforcing in GREEN - {keys} call-out(s), {boxes} box(es))"));
                    }
            }

            // Cover is page 1; emissions follow in order. First emitted page per sheet = its index entry.
            var pageOfSheet = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < emissions.Count; i++) pageOfSheet.TryAdd(emissions[i].Sheet, i + 2);

            BuildCover(builder, font, projectName, beforeLabel, afterLabel, plan, pageOfSheet);

            foreach (var e in emissions)
                DrawSheet(builder, e.FromBefore ? before : after, font, e.Pg, e.Keys, e.Color, e.Label);

            // Document outline: jump straight to any sheet's marked page from the viewer's bookmark
            // panel (Rory reads these in Bluebeam/Acrobat, where the panel is the navigation).
            var marks = new List<BookmarkNode>
            {
                new DocumentBookmarkNode("Cover - change index", 0,
                    new ExplicitDestination(1, ExplicitDestinationType.FitPage, new ExplicitDestinationCoordinates(null)),
                    Array.Empty<BookmarkNode>()),
            };
            for (int i = 0; i < emissions.Count; i++)
                marks.Add(new DocumentBookmarkNode(
                    $"{emissions[i].Sheet} - {(emissions[i].FromBefore ? "removed (RED)" : "added (GREEN)")}", 0,
                    new ExplicitDestination(i + 2, ExplicitDestinationType.FitPage, new ExplicitDestinationCoordinates(null)),
                    Array.Empty<BookmarkNode>()));
            builder.Bookmarks = new Bookmarks(marks);

            return builder.Build();
        }

        private static void DrawSheet(
            PdfDocumentBuilder builder, PdfDocument doc, PdfDocumentBuilder.AddedFont font,
            PageModel pg, HashSet<string> keys, (byte R, byte G, byte B) color, string label)
        {
            var page = builder.AddPage(doc, pg.Num);
            page.SetStrokeColor(color.R, color.G, color.B);
            const double pad = 3.0;
            foreach (var h in pg.Callouts.Where(h => keys.Contains(h.Key)))
            {
                page.DrawRectangle(
                    new PdfPoint(h.Left - pad, h.Bottom - pad),
                    h.Width + 2 * pad, h.Height + 2 * pad, 1.8, false);
            }
            page.SetTextAndFillColor(color.R, color.G, color.B);
            page.AddText(label, 15, new PdfPoint(40, pg.H - 46), font);
            page.ResetColor();
        }

        private static void BuildCover(
            PdfDocumentBuilder builder, PdfDocumentBuilder.AddedFont font,
            string projectName, string beforeLabel, string afterLabel,
            List<(string Sheet, HashSet<string> Added, HashSet<string> Removed)> plan,
            IReadOnlyDictionary<string, int> pageOfSheet)
        {
            const double Left = 50, RightX = 572; // 612pt page, ~40pt right margin
            var cv = builder.AddPage(612, 792);
            cv.SetTextAndFillColor(Navy.R, Navy.G, Navy.B);
            double y = DrawWrapped(cv, font,
                string.IsNullOrWhiteSpace(projectName) ? "Rebar Call-out Changes" : projectName,
                Left, 720, 20, RightX, 26);
            y = DrawWrapped(cv, font, $"Rebar call-out changes  -  {beforeLabel} to {afterLabel}", Left, y - 6, 13, RightX, 18);

            y -= 12;
            cv.SetTextAndFillColor(Green.R, Green.G, Green.B);
            y = DrawWrapped(cv, font, $"GREEN box = reinforcing ADDED by {afterLabel} (on the {afterLabel} sheet)", Left, y, 12, RightX, 16);
            cv.SetTextAndFillColor(Red.R, Red.G, Red.B);
            y = DrawWrapped(cv, font, $"RED box = reinforcing REMOVED since {beforeLabel} (on the {beforeLabel} sheet)", Left, y - 2, 12, RightX, 16);
            // ResetColor does not restore the text fill to black here, so set the body colour explicitly.
            cv.SetTextAndFillColor(Navy.R, Navy.G, Navy.B);
            y = DrawWrapped(cv, font,
                $"The sheets below are boxed on the actual drawings: removed (red) on {beforeLabel}, added (green) on {afterLabel}. The Change Report (.xlsx) is the complete sheet-by-sheet list.",
                Left, y - 4, 10, RightX, 14);
            y = DrawWrapped(cv, font,
                "A box marks EVERY occurrence of a changed call-out on its sheet, so a sheet can show more boxes than its added/removed count. Each page header states both counts.",
                Left, y - 2, 10, RightX, 14);

            int totalAdded = plan.Sum(p => p.Added.Count), totalRemoved = plan.Sum(p => p.Removed.Count);
            y = DrawWrapped(cv, font,
                $"{plan.Count} sheet(s) changed  -  {totalAdded} call-out(s) added, {totalRemoved} removed.",
                Left, y - 8, 11, RightX, 15);

            y -= 10;
            int shown = 0;
            foreach (var p in plan)
            {
                // Leave room for the "and N more" line — the index must never truncate silently.
                if (y < 56 && shown < plan.Count - 1)
                {
                    cv.AddText($"... and {plan.Count - shown} more sheet(s) - see the Change Report (.xlsx).",
                        10, new PdfPoint(55, y), font);
                    break;
                }
                string pn = pageOfSheet.TryGetValue(p.Sheet, out var n) ? $"p.{n}" : "";
                cv.AddText($"{p.Sheet}    +{p.Added.Count} added   -{p.Removed.Count} removed    {pn}",
                    10, new PdfPoint(55, y), font);
                y -= 15;
                shown++;
            }
        }

        // Word-wraps text to fit within [x, rightX] at the given font size, drawing each line and
        // returning the y below the last line. PdfPig has no layout engine, so we estimate advance
        // width from the Helvetica average (~0.5em) and wrap conservatively — nothing runs off the page.
        private static double DrawWrapped(
            PdfPageBuilder page, PdfDocumentBuilder.AddedFont font,
            string text, double x, double y, double size, double rightX, double leading)
        {
            int maxChars = Math.Max(8, (int)((rightX - x) / (size * 0.52)));
            foreach (var line in WrapWords(text, maxChars))
            {
                page.AddText(line, size, new PdfPoint(x, y), font);
                y -= leading;
            }
            return y;
        }

        private static IEnumerable<string> WrapWords(string text, int maxChars)
        {
            var sb = new StringBuilder();
            foreach (var w in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (sb.Length > 0 && sb.Length + 1 + w.Length > maxChars)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(w);
            }
            if (sb.Length > 0) yield return sb.ToString();
        }

        // ---- extraction (positioned, unit-aware) ----

        private static List<PageModel> Read(PdfDocument doc, UnitSystem unit)
        {
            var pages = new List<PageModel>();
            foreach (var page in doc.GetPages())
            {
                var words = page.GetWords().ToList();
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
        private static Dictionary<string, List<PageModel>> OwnSheet(List<PageModel> pages)
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

        private static Dictionary<string, int> Counts(Dictionary<string, List<PageModel>> map, string sheet)
        {
            var c = new Dictionary<string, int>();
            if (map.TryGetValue(sheet, out var pages))
                foreach (var pg in pages)
                    foreach (var h in pg.Callouts)
                        c[h.Key] = c.GetValueOrDefault(h.Key) + 1;
            return c;
        }

        // Assemble "##M@spacing" (metric) or "#n@spacing" (imperial) call-outs with their boxes,
        // walking words in reading order and merging the spacing token when it is separate.
        private static List<Hit> AssembleCallouts(List<Word> words, UnitSystem unit)
        {
            bool imp = unit == UnitSystem.Imperial;
            var full = imp ? new Regex(@"^#(\d{1,2})@(\d{1,2})[""″]?$") : new Regex(@"^(\d{2})M@(\d{2,4})$");
            var start = imp ? new Regex(@"^#(\d{1,2})@?$") : new Regex(@"^(\d{2})M@?$");
            var space = imp ? new Regex(@"^(\d{1,2})[""″]?$") : new Regex(@"^(\d{2,4})$");
            int smin = imp ? 3 : 75, smax = imp ? 48 : 750;

            var outp = new List<Hit>();
            for (int i = 0; i < words.Count; i++)
            {
                var t = words[i].Text;
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
                    var msp = space.Match(words[j].Text);
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

        private static string SortKey(string sheet) => Regex.Replace(sheet, @"\d+", m => m.Value.PadLeft(4, '0'));
    }
}
