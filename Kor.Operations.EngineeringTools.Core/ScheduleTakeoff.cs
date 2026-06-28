#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// The estimator's cross-reference: price vertical concrete (shear walls, columns) from the
    /// SCHEDULES rather than from plan poché pixels. A schedule states each member's size over a band
    /// of levels (a merged cell, e.g. "W1 = 12&quot; from L19 to L15"); a key plan or the schedule itself
    /// gives the member's plan length (walls) or that it exists (columns). This module expands those
    /// bands over the building's ordered level list and totals the concrete per level — exactly how a
    /// quantity surveyor reads a wall schedule, made deterministic and unit-testable. Vision only
    /// transcribes the table (Layer 2); the arithmetic here is pure.
    /// </summary>
    public static class ScheduleTakeoff
    {
        /// <summary>
        /// Canonical level key so labels that mean the same floor match regardless of how a given sheet
        /// spells them: upper-cased, single-spaced, zero-padding stripped ("LEVEL 08" → "L8"), and the
        /// "LEVEL"/"LVL"/"LEV" word folded to the "L" prefix before a number ("LEVEL 7" → "L7") or dropped
        /// before a letter prefix ("LEVEL P1" → "P1"). So "LEVEL 7", "L07" and "L7" all key to "L7", and
        /// "LEVEL P1" matches "P1".
        /// </summary>
        public static string NormalizeLevel(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            string n = Regex.Replace(s.Trim().ToUpperInvariant(), @"\s+", " ");
            n = Regex.Replace(n, @"0*(\d+)", "$1");                  // strip zero-padding in numbers
            n = Regex.Replace(n, @"^(LEVEL|LVL|LEV)\s+(?=\d)", "L"); // LEVEL 7 → L7
            n = Regex.Replace(n, @"^(LEVEL|LVL|LEV)\s+(?=[A-Z])", ""); // LEVEL P1 → P1
            return n;
        }

        // A schedule cell that gives a member's size over a contiguous band of levels (top→bottom as
        // the schedule is drawn). Walls use ThicknessIn; columns use WidthIn × DepthIn.
        public readonly record struct WallBand(string Mark, string LevelTop, string LevelBottom, double ThicknessIn);
        public readonly record struct ColumnBand(string Mark, string LevelTop, string LevelBottom, double WidthIn, double DepthIn);

        /// <summary>Vertical concrete priced at one level: the member footprint (plan area) and the
        /// storey height it rises through.</summary>
        public sealed record LevelFootprint(string Level, double FootprintSqFt, double StoreyIn)
        {
            public double ConcreteCuYd => FootprintSqFt > 0 && StoreyIn > 0
                ? FootprintSqFt * (StoreyIn / 12.0) / 27.0 : 0.0;
        }

        public sealed record ScheduleResult(
            IReadOnlyList<LevelFootprint> PerLevel,
            double TotalCuYd,
            int BandsApplied,
            int BandsSkipped,
            int MarksPriced);

        // Resolve a band's top/bottom labels to inclusive indices in the ordered level list.
        // FLOOR/BASE/FND/FOUNDATION resolve to the bottom of the list (a wall that runs "to floor").
        private static bool TryResolveBand(
            IReadOnlyDictionary<string, int> levelIdx, int levelCount, string top, string bottom, out int a, out int b)
        {
            a = ResolveLevel(levelIdx, levelCount, top);
            b = ResolveLevel(levelIdx, levelCount, bottom);
            if (a < 0 || b < 0) return false;
            if (a > b) (a, b) = (b, a);   // tolerate top/bottom given in either order
            return true;
        }

        private static int ResolveLevel(IReadOnlyDictionary<string, int> levelIdx, int levelCount, string label)
        {
            string n = NormalizeLevel(label);
            if (n is "FLOOR" or "BASE" or "FND" or "FOUNDATION") return levelCount - 1;
            return levelIdx.TryGetValue(n, out var i) ? i : -1;
        }

        private static Dictionary<string, int> IndexLevels(IReadOnlyList<string> levelsTopToBottom)
        {
            var idx = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < levelsTopToBottom.Count; i++)
            {
                string k = NormalizeLevel(levelsTopToBottom[i]);
                idx.TryAdd(k, i);   // first occurrence wins if a label repeats
            }
            return idx;
        }

        /// <summary>
        /// Core shear-wall concrete per level. Each wall mark's plan length (from the key plan) is held
        /// constant up the height; its thickness steps per the schedule bands. Only marks present in
        /// BOTH the key plan (a length) and the schedule (a thickness) are priced — a mark missing from
        /// either can't be measured and is left out rather than guessed.
        /// </summary>
        public static ScheduleResult ComputeWall(
            IReadOnlyList<string> levelsTopToBottom,
            IReadOnlyList<double> storeyInByLevel,
            IReadOnlyDictionary<string, double> lengthFtByMark,
            IEnumerable<WallBand> bands)
        {
            ArgumentNullException.ThrowIfNull(levelsTopToBottom);
            ArgumentNullException.ThrowIfNull(storeyInByLevel);
            ArgumentNullException.ThrowIfNull(lengthFtByMark);
            ArgumentNullException.ThrowIfNull(bands);
            if (storeyInByLevel.Count != levelsTopToBottom.Count)
                throw new ArgumentException("storeyInByLevel must have one entry per level.", nameof(storeyInByLevel));

            int n = levelsTopToBottom.Count;
            var levelIdx = IndexLevels(levelsTopToBottom);
            // thickness[mark][levelIndex]; 0 means the wall isn't present at that level.
            var thk = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            int applied = 0, skipped = 0;

            foreach (var band in bands)
            {
                string mk = (band.Mark ?? "").Trim();
                if (mk.Length == 0 || band.ThicknessIn <= 0 ||
                    !lengthFtByMark.ContainsKey(mk) ||                    // no length → can't price this mark
                    !TryResolveBand(levelIdx, n, band.LevelTop, band.LevelBottom, out int a, out int b))
                { skipped++; continue; }
                if (!thk.TryGetValue(mk, out var arr)) thk[mk] = arr = new double[n];
                for (int i = a; i <= b; i++) arr[i] = band.ThicknessIn;
                applied++;
            }

            var perLevel = new LevelFootprint[n];
            for (int i = 0; i < n; i++)
            {
                double footprint = 0;
                foreach (var (mk, arr) in thk)
                    if (arr[i] > 0 && lengthFtByMark.TryGetValue(mk, out var len) && len > 0)
                        footprint += len * (arr[i] / 12.0);   // ft × (in→ft) = sq ft of wall plan
                perLevel[i] = new LevelFootprint(levelsTopToBottom[i], footprint, storeyInByLevel[i]);
            }

            double total = perLevel.Sum(p => p.ConcreteCuYd);
            return new ScheduleResult(perLevel, total, applied, skipped, thk.Count);
        }

        /// <summary>
        /// Column concrete per level from a column schedule: each mark is one column at a fixed plan
        /// location; its W×D footprint applies on every level its band covers. Self-contained — the
        /// schedule gives the size directly, no key plan needed.
        /// </summary>
        public static ScheduleResult ComputeColumn(
            IReadOnlyList<string> levelsTopToBottom,
            IReadOnlyList<double> storeyInByLevel,
            IEnumerable<ColumnBand> bands)
        {
            ArgumentNullException.ThrowIfNull(levelsTopToBottom);
            ArgumentNullException.ThrowIfNull(storeyInByLevel);
            ArgumentNullException.ThrowIfNull(bands);
            if (storeyInByLevel.Count != levelsTopToBottom.Count)
                throw new ArgumentException("storeyInByLevel must have one entry per level.", nameof(storeyInByLevel));

            int n = levelsTopToBottom.Count;
            var levelIdx = IndexLevels(levelsTopToBottom);
            var area = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);   // sq ft per mark per level
            int applied = 0, skipped = 0;

            foreach (var band in bands)
            {
                string mk = (band.Mark ?? "").Trim();
                double w = band.WidthIn, d = band.DepthIn <= 0 ? band.WidthIn : band.DepthIn;  // square if only one dim read
                if (mk.Length == 0 || w <= 0 ||
                    !TryResolveBand(levelIdx, n, band.LevelTop, band.LevelBottom, out int a, out int b))
                { skipped++; continue; }
                if (!area.TryGetValue(mk, out var arr)) area[mk] = arr = new double[n];
                double sqft = (w * d) / 144.0;
                for (int i = a; i <= b; i++) arr[i] = sqft;
                applied++;
            }

            var perLevel = new LevelFootprint[n];
            for (int i = 0; i < n; i++)
            {
                double footprint = 0;
                foreach (var (_, arr) in area) footprint += arr[i];
                perLevel[i] = new LevelFootprint(levelsTopToBottom[i], footprint, storeyInByLevel[i]);
            }

            double total = perLevel.Sum(p => p.ConcreteCuYd);
            return new ScheduleResult(perLevel, total, applied, skipped, area.Count);
        }
    }
}
