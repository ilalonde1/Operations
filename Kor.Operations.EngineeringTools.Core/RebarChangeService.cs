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
                foreach (var k in cb.Keys.Union(ca.Keys).OrderBy(x => x))
                {
                    int d = cb.GetValueOrDefault(k) - ca.GetValueOrDefault(k);
                    if (d > 0) { added.Add($"+{d}x {k}"); net += d; }
                    else if (d < 0) { removed.Add($"{d}x {k}"); net += d; }
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
                changes.Add(new RebarSheetChange(s, title, status, beforeN, afterN, afterN - beforeN, added, removed));
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
                AfterLabel: afterLabel);
        }

        private static readonly IReadOnlyDictionary<string, int> Empty = new Dictionary<string, int>();

        // Natural-ish sort: "S2.06.2" before "S2.10.1" (zero-pad each numeric run).
        private static string SortKey(string sheet) =>
            Regex.Replace(sheet, @"\d+", m => m.Value.PadLeft(4, '0'));
    }
}
