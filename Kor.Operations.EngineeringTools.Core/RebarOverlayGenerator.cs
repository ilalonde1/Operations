#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
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
            public List<string> Tokens = new();
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

            BuildCover(builder, font, projectName, beforeLabel, afterLabel, plan);

            foreach (var (sheet, added, removed) in plan)
            {
                if (bMap.TryGetValue(sheet, out var bp))
                    foreach (var pg in bp)
                        DrawSheet(builder, before, font, pg, removed, Red,
                            $"{sheet}  -  {beforeLabel}   (removed reinforcing in RED)");

                if (aMap.TryGetValue(sheet, out var ap))
                    foreach (var pg in ap)
                        DrawSheet(builder, after, font, pg, added, Green,
                            $"{sheet}  -  {afterLabel}   (added reinforcing in GREEN)");
            }

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
            List<(string Sheet, HashSet<string> Added, HashSet<string> Removed)> plan)
        {
            var cv = builder.AddPage(612, 792);
            cv.SetTextAndFillColor(Navy.R, Navy.G, Navy.B);
            cv.AddText(string.IsNullOrWhiteSpace(projectName) ? "Rebar Call-out Changes" : projectName, 20, new PdfPoint(50, 720), font);
            cv.AddText($"Rebar call-out changes  -  {beforeLabel} to {afterLabel}", 13, new PdfPoint(50, 695), font);
            cv.ResetColor();
            cv.SetTextAndFillColor(Green.R, Green.G, Green.B);
            cv.AddText($"GREEN box = reinforcing ADDED by {afterLabel} (on the {afterLabel} sheet)", 12, new PdfPoint(50, 660), font);
            cv.SetTextAndFillColor(Red.R, Red.G, Red.B);
            cv.AddText($"RED box   = reinforcing REMOVED since {beforeLabel} (on the {beforeLabel} sheet)", 12, new PdfPoint(50, 640), font);
            cv.ResetColor();
            cv.AddText($"Each sheet appears twice: {beforeLabel} then {afterLabel} - flip to see the change.", 10, new PdfPoint(50, 615), font);

            double y = 585;
            foreach (var p in plan)
            {
                cv.AddText($"{p.Sheet}    +{p.Added.Count} added   -{p.Removed.Count} removed", 10, new PdfPoint(55, y), font);
                y -= 15;
                if (y < 40) break;
            }
        }

        // ---- extraction (positioned, unit-aware) ----

        private static List<PageModel> Read(PdfDocument doc, UnitSystem unit)
        {
            var pages = new List<PageModel>();
            foreach (var page in doc.GetPages())
            {
                var words = page.GetWords().ToList();
                string text = string.Join(" ", words.Select(w => w.Text));
                pages.Add(new PageModel
                {
                    Num = page.Number,
                    W = page.Width,
                    H = page.Height,
                    Tokens = SheetRe.Matches(text).Select(m => m.Value).ToList(),
                    Callouts = AssembleCallouts(words, unit),
                });
            }
            return pages;
        }

        // sheet -> owning pages (a page's own sheet = its rarest sheet token; index pages excluded).
        private static Dictionary<string, List<PageModel>> OwnSheet(List<PageModel> pages)
        {
            var index = new HashSet<int>();
            for (int i = 0; i < pages.Count; i++)
                if (pages[i].Tokens.Distinct().Count() > 30) index.Add(i);

            var freq = new Dictionary<string, int>();
            for (int i = 0; i < pages.Count; i++)
            {
                if (index.Contains(i)) continue;
                foreach (var s in pages[i].Tokens.Distinct())
                    freq[s] = freq.GetValueOrDefault(s) + 1;
            }

            var map = new Dictionary<string, List<PageModel>>();
            for (int i = 0; i < pages.Count; i++)
            {
                if (index.Contains(i) || pages[i].Tokens.Count == 0) continue;
                var uniq = pages[i].Tokens.Distinct().ToList();
                string own = uniq.OrderBy(s => freq.GetValueOrDefault(s)).ThenBy(s => uniq.IndexOf(s)).First();
                if (!map.TryGetValue(own, out var list)) map[own] = list = new List<PageModel>();
                list.Add(pages[i]);
            }
            return map;
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
