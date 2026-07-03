#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.QuantityTakeoff;

namespace Kor.Operations.EngineeringTools.RebarChange
{
    /// <summary>
    /// Compares the reinforcing call-outs of two drawing issues, sheet by sheet, and reports
    /// what changed. This is the slow manual "compare each rebar call-up" task, automated.
    /// </summary>
    public static class RebarChangeService
    {
        /// <summary>
        /// Compare two PDFs through the SAME positioned-word pipeline the overlay markup uses
        /// (<see cref="RebarPdfReader"/>: fake-bold dedupe, plan+intensity grammars, title-block sheet
        /// ownership) — so the xlsx ledger and the marked-up PDF can never disagree about what was
        /// read. Prefer this over the page-text overload whenever the source PDFs are available.
        /// </summary>
        public static RebarChangeResult ComparePdfs(
            string beforePdfPath, string afterPdfPath,
            string beforeLabel = "Before", string afterLabel = "After",
            UnitSystem unit = UnitSystem.Metric)
        {
            ArgumentNullException.ThrowIfNull(beforePdfPath);
            ArgumentNullException.ThrowIfNull(afterPdfPath);

            // Titles from the drawing index (text is fine for titles); AFTER's index wins, BEFORE fills gaps.
            var titles = RebarCalloutExtractor.BuildTitles(PdfPageTextReader.ReadPages(afterPdfPath));
            foreach (var kv in RebarCalloutExtractor.BuildTitles(PdfPageTextReader.ReadPages(beforePdfPath)))
                titles.TryAdd(kv.Key, kv.Value);

            Dictionary<string, SheetCallouts> Load(string path)
            {
                using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
                return RebarPdfReader.SheetCounts(RebarPdfReader.OwnSheet(RebarPdfReader.Read(doc, unit)))
                    .ToDictionary(kv => kv.Key,
                                  kv => new SheetCallouts(kv.Key, titles.GetValueOrDefault(kv.Key, ""), kv.Value));
            }

            return Diff(Load(beforePdfPath), Load(afterPdfPath), beforeLabel, afterLabel);
        }

        public static RebarChangeResult Compare(
            IReadOnlyList<string> beforePages,
            IReadOnlyList<string> afterPages,
            string beforeLabel = "Before",
            string afterLabel = "After",
            UnitSystem unit = UnitSystem.Metric)
        {
            ArgumentNullException.ThrowIfNull(beforePages);
            ArgumentNullException.ThrowIfNull(afterPages);

            var a = RebarCalloutExtractor.Extract(beforePages, unit).ToDictionary(s => s.Sheet);
            var b = RebarCalloutExtractor.Extract(afterPages, unit).ToDictionary(s => s.Sheet);
            return Diff(a, b, beforeLabel, afterLabel);
        }

        private static RebarChangeResult Diff(
            Dictionary<string, SheetCallouts> a,
            Dictionary<string, SheetCallouts> b,
            string beforeLabel, string afterLabel)
        {

            var sheets = a.Keys.Union(b.Keys).Distinct().OrderBy(SortKey).ToList();

            var changes = new List<RebarSheetChange>();
            int calloutsAdded = 0, calloutsRemoved = 0;

            foreach (var s in sheets)
            {
                bool inA = a.TryGetValue(s, out var sa);
                bool inB = b.TryGetValue(s, out var sb);
                var ca = inA ? sa!.Callouts : Empty;
                var cb = inB ? sb!.Callouts : Empty;

                var added = new List<string>();
                var removed = new List<string>();
                int net = 0;
                // Weight delta of this sheet's changes — the number the whole exercise exists for
                // ("this SI adds N lb of rebar"). Only quantity-bearing call-outs (count × length ×
                // CSA mass) are weighed; intensity/continuous changes are tallied unweighed, never guessed.
                double addedLb = 0, removedLb = 0; int unweighedChanges = 0;
                foreach (var k in cb.Keys.Union(ca.Keys).OrderBy(x => x))
                {
                    int d = cb.GetValueOrDefault(k) - ca.GetValueOrDefault(k);
                    if (d == 0) continue;
                    double? lbEach = RebarBarListWeigher.KeyWeightLb(k);
                    string w = lbEach is double lw ? $"  ({d * lw:+#,##0;-#,##0} lb)" : "";
                    if (d > 0) { added.Add($"+{d}x {k}{w}"); net += d; }
                    else { removed.Add($"{d}x {k}{w}"); net += d; }
                    if (lbEach is double each) { if (d > 0) addedLb += d * each; else removedLb += -d * each; }
                    else unweighedChanges += Math.Abs(d);
                }

                RebarChangeStatus status;
                if (!inA) status = RebarChangeStatus.NewSheet;
                else if (!inB) status = RebarChangeStatus.RemovedSheet;
                else if (added.Count > 0 || removed.Count > 0) status = RebarChangeStatus.Changed;
                else status = RebarChangeStatus.Unchanged;

                int beforeN = ca.Values.Sum();
                int afterN = cb.Values.Sum();
                calloutsAdded += Math.Max(0, afterN - beforeN);
                calloutsRemoved += Math.Max(0, beforeN - afterN);

                string title = (inB ? sb!.Title : sa!.Title) ?? "";
                changes.Add(new RebarSheetChange(s, title, status, beforeN, afterN, afterN - beforeN, added, removed,
                    AddedWeightLb: addedLb, RemovedWeightLb: removedLb, UnweighedChanges: unweighedChanges));
            }

            return new RebarChangeResult(
                changes,
                SheetsCompared: changes.Count,
                SheetsChanged: changes.Count(c => c.Status != RebarChangeStatus.Unchanged),
                ContentChanged: changes.Count(c => c.Status == RebarChangeStatus.Changed),
                NewSheets: changes.Count(c => c.Status == RebarChangeStatus.NewSheet),
                RemovedSheets: changes.Count(c => c.Status == RebarChangeStatus.RemovedSheet),
                CalloutsAdded: calloutsAdded,
                CalloutsRemoved: calloutsRemoved,
                BeforeLabel: beforeLabel,
                AfterLabel: afterLabel,
                TotalCalloutsRead: changes.Sum(c => c.BeforeCount + c.AfterCount),
                AddedWeightLb: changes.Sum(c => c.AddedWeightLb),
                RemovedWeightLb: changes.Sum(c => c.RemovedWeightLb),
                UnweighedChanges: changes.Sum(c => c.UnweighedChanges));
        }

        private static readonly IReadOnlyDictionary<string, int> Empty = new Dictionary<string, int>();

        // Natural-ish sort: "S2.06.2" before "S2.10.1" (zero-pad each numeric run).
        private static string SortKey(string sheet) =>
            Regex.Replace(sheet, @"\d+", m => m.Value.PadLeft(4, '0'));
    }
}
