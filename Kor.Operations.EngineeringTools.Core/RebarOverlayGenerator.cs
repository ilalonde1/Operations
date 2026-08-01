#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Outline;
using UglyToad.PdfPig.Outline.Destinations;
using UglyToad.PdfPig.Writer;
using Hit = Kor.Operations.EngineeringTools.RebarChange.RebarPdfReader.Hit;
using PageModel = Kor.Operations.EngineeringTools.RebarChange.RebarPdfReader.PageModel;

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

        private static byte[] BuildCore(
            PdfDocument before, PdfDocument after,
            string projectName, string beforeLabel, string afterLabel, UnitSystem unit)
        {
            var bPages = RebarPdfReader.Read(before, unit);
            var aPages = RebarPdfReader.Read(after, unit);
            var bMap = RebarPdfReader.OwnSheet(bPages);
            var aMap = RebarPdfReader.OwnSheet(aPages);

            var sheets = bMap.Keys.Union(aMap.Keys).Distinct().OrderBy(SortKey).ToList();

            // Can't-read guard (same rule as the xlsx path): real sheets but ZERO reinforcing
            // call-outs read on BOTH issues means this set's annotation grammar wasn't recognised.
            // That is NOT "no changes" — refuse rather than emit a falsely-reassuring empty markup.
            int totalRead = bMap.Values.Concat(aMap.Values).Sum(pgs => pgs.Sum(p => p.Callouts.Count));
            if (sheets.Count >= 3 && totalRead == 0)
                throw new InvalidOperationException(
                    $"Compared {sheets.Count} sheets but read 0 reinforcing call-outs on either issue — " +
                    "this set's call-out style was not recognised, so changes cannot be detected. " +
                    "This is NOT a 'no change' result; no markup was produced.");

            // Per sheet: match call-out INSTANCES across the two issues by key AND position, and keep
            // only the unmatched ones. An unchanged call-out sits at the same coordinates in both
            // issues (a reissued annotation doesn't move), so it pairs off and is never boxed — even
            // when the same TEXT was added elsewhere on the sheet. This is what makes the box land on
            // the new PC5 row instead of on PC1's identical, unchanged cell: a count-diff knows the
            // key gained an instance; only the position knows WHICH instance is the new one.
            // Weight: each unmatched instance contributes its own lb (count × length × CSA mass) when
            // the call-out carries a quantity; instances with none are tallied unweighed, not guessed.
            const double MatchTolPt = 8.0;   // reissued-unchanged text does not move; well under one cell/annotation pitch
            var plan = new List<(string Sheet,
                List<(PageModel Pg, Hit H)> Added, List<(PageModel Pg, Hit H)> Removed,
                double NetLb, int Unweighed)>();
            foreach (var s in sheets)
            {
                var bHits = (bMap.TryGetValue(s, out var bpg) ? bpg : new List<PageModel>())
                    .SelectMany(pg => pg.Callouts.Select(h => (Pg: pg, H: h))).ToList();
                var aHits = (aMap.TryGetValue(s, out var apg) ? apg : new List<PageModel>())
                    .SelectMany(pg => pg.Callouts.Select(h => (Pg: pg, H: h))).ToList();

                var addedInst = new List<(PageModel Pg, Hit H)>();
                var removedInst = new List<(PageModel Pg, Hit H)>();
                foreach (var key in aHits.Select(x => x.H.Key).Union(bHits.Select(x => x.H.Key)).Distinct())
                {
                    var bl = bHits.Where(x => x.H.Key == key).ToList();
                    var al = aHits.Where(x => x.H.Key == key).ToList();
                    var usedB = new bool[bl.Count];
                    var pendingA = new List<(PageModel Pg, Hit H)>();

                    // Pass 1 — same place: an unchanged annotation doesn't move, so it pairs off here.
                    foreach (var a in al)
                    {
                        int best = -1; double bestD = MatchTolPt;
                        for (int i = 0; i < bl.Count; i++)
                        {
                            if (usedB[i]) continue;
                            double d = Math.Max(Math.Abs(bl[i].H.Left - a.H.Left), Math.Abs(bl[i].H.Bottom - a.H.Bottom));
                            if (d <= bestD) { bestD = d; best = i; }
                        }
                        if (best >= 0) usedB[best] = true;
                        else pendingA.Add(a);
                    }

                    // Pass 2 — moved-but-identical: an inserted row/relaid table SHIFTS the unchanged
                    // neighbours, which must not read as changes. Remaining same-key instances pair up
                    // nearest-first at any distance; only the COUNT EXCESS survives — and it is the
                    // instances farthest from any counterpart, i.e. the genuinely new/removed ones.
                    var freeB = Enumerable.Range(0, bl.Count).Where(i => !usedB[i]).ToList();
                    while (pendingA.Count > 0 && freeB.Count > 0)
                    {
                        double bestD = double.MaxValue; int ai = -1, bi = -1;
                        for (int a2 = 0; a2 < pendingA.Count; a2++)
                            foreach (var b2 in freeB)
                            {
                                double d = Math.Max(Math.Abs(bl[b2].H.Left - pendingA[a2].H.Left),
                                                    Math.Abs(bl[b2].H.Bottom - pendingA[a2].H.Bottom));
                                if (d < bestD) { bestD = d; ai = a2; bi = b2; }
                            }
                        pendingA.RemoveAt(ai);
                        freeB.Remove(bi);
                        usedB[bi] = true;
                    }

                    addedInst.AddRange(pendingA);
                    foreach (var i in freeB) removedInst.Add(bl[i]);
                }
                if (addedInst.Count == 0 && removedInst.Count == 0) continue;

                double netLb = 0; int unweighed = 0;
                foreach (var (_, h) in addedInst)
                    if (RebarBarListWeigher.KeyWeightLb(h.Key) is double lb) netLb += lb; else unweighed++;
                foreach (var (_, h) in removedInst)
                    if (RebarBarListWeigher.KeyWeightLb(h.Key) is double lb) netLb -= lb; else unweighed++;
                plan.Add((s, addedInst, removedInst, netLb, unweighed));
            }

            var builder = new PdfDocumentBuilder();
            var font = builder.AddStandard14Font(Standard14Font.HelveticaBold);

            // Decide every page BEFORE drawing anything, so the cover can print each sheet's page
            // number and the document outline can point at real destinations. Only a page that
            // actually carries a box is emitted — a sheet with additions only must not produce a
            // blank "removed in RED" before-page (and vice-versa), which reads as broken.
            var emissions = new List<(PageModel Pg, bool FromBefore, List<Hit> Hits,
                (byte R, byte G, byte B) Color, string Sheet, string Label)>();
            foreach (var (sheet, addedInst, removedInst, _, _) in plan)
            {
                foreach (var g in removedInst.GroupBy(x => x.Pg))
                {
                    var hits = g.Select(x => x.H).ToList();
                    int keys = hits.Select(h => h.Key).Distinct().Count();
                    emissions.Add((g.Key, true, hits, Red, sheet,
                        $"{sheet}  -  {beforeLabel}   (removed reinforcing in RED - {keys} call-out(s), {hits.Count} box(es))"));
                }
                foreach (var g in addedInst.GroupBy(x => x.Pg))
                {
                    var hits = g.Select(x => x.H).ToList();
                    int keys = hits.Select(h => h.Key).Distinct().Count();
                    emissions.Add((g.Key, false, hits, Green, sheet,
                        $"{sheet}  -  {afterLabel}   (added reinforcing in GREEN - {keys} call-out(s), {hits.Count} box(es))"));
                }
            }

            // Cover is page 1; emissions follow in order. First emitted page per sheet = its index entry.
            var pageOfSheet = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < emissions.Count; i++) pageOfSheet.TryAdd(emissions[i].Sheet, i + 2);

            BuildCover(builder, font, projectName, beforeLabel, afterLabel, plan, pageOfSheet);

            foreach (var e in emissions)
                DrawSheet(builder, e.FromBefore ? before : after, font, e.Pg, e.Hits, e.Color, e.Label);

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
            PageModel pg, IReadOnlyList<Hit> hits, (byte R, byte G, byte B) color, string label)
        {
            var page = builder.AddPage(doc, pg.Num);
            page.SetStrokeColor(color.R, color.G, color.B);
            const double pad = 3.0;
            // Boxes are the position-matched CHANGED instances only — an unchanged occurrence of the
            // same call-out text elsewhere on the sheet is deliberately not boxed.
            foreach (var h in hits)
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
            List<(string Sheet, List<(PageModel Pg, Hit H)> Added, List<(PageModel Pg, Hit H)> Removed,
                double NetLb, int Unweighed)> plan,
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
                "A box marks the SPECIFIC call-out instance that changed (matched by text AND position between the issues) - an identical, unchanged call-out elsewhere on the sheet is not boxed. Each page header states the counts.",
                Left, y - 2, 10, RightX, 14);

            int totalAdded = plan.Sum(p => p.Added.Count), totalRemoved = plan.Sum(p => p.Removed.Count);
            double netLb = plan.Sum(p => p.NetLb);
            int unweighed = plan.Sum(p => p.Unweighed);
            y = DrawWrapped(cv, font,
                $"{plan.Count} sheet(s) changed  -  {totalAdded} call-out(s) added, {totalRemoved} removed.",
                Left, y - 8, 11, RightX, 15);
            // The number this report exists for: the rebar weight this issue adds or removes, from the
            // quantity-bearing call-outs (count x length x CSA mass). Unweighable changes are declared.
            y = DrawWrapped(cv, font,
                $"Net weighable rebar change: {netLb:+#,##0;-#,##0;0} lb"
                + (unweighed > 0 ? $"  ({unweighed} changed call-out(s) carry no count/length and are NOT in the lb figure)" : ""),
                Left, y - 2, 11, RightX, 15);

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
                string lb = Math.Abs(p.NetLb) >= 0.5 ? $"   {p.NetLb:+#,##0;-#,##0} lb" : "";
                cv.AddText($"{p.Sheet}    +{p.Added.Count} added   -{p.Removed.Count} removed{lb}    {pn}",
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

        private static string SortKey(string sheet) => Regex.Replace(sheet, @"\d+", m => m.Value.PadLeft(4, '0'));
    }
}
