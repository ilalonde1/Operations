#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    public enum TakeoffDiffStatus
    {
        Matched,
        Added,
        Removed
    }

    public sealed record TakeoffDiffLine(
        string Level,
        TakeoffElementType ElementType,
        string GradeCode,
        double ConcreteBeforeM3,
        double ConcreteAfterM3,
        double ConcreteDeltaM3,
        double FormworkBeforeM2,
        double FormworkAfterM2,
        double FormworkDeltaM2,
        double RebarBeforeKg,
        double RebarAfterKg,
        double RebarDeltaKg,
        TakeoffDiffStatus Status,
        string? Change = null,
        string? Category = null);

    public sealed record TakeoffDiff(
        IReadOnlyList<TakeoffDiffLine> Lines,
        double TotalConcreteDeltaM3,
        double TotalFormworkDeltaM2,
        double TotalRebarDeltaTonnes,
        IReadOnlyList<string> AddedLevels,
        IReadOnlyList<string> RemovedLevels,
        bool BasisMismatch);

    public static class TakeoffDiffService
    {
        public static TakeoffDiff Compare(
            IReadOnlyList<TakeoffLineResult> before,
            IReadOnlyList<TakeoffLineResult> after,
            string? basisBefore = null,
            string? basisAfter = null)
        {
            ArgumentNullException.ThrowIfNull(before);
            ArgumentNullException.ThrowIfNull(after);

            var beforeResolved = before.Where(l => !l.Unresolved).ToList();
            var afterResolved = after.Where(l => !l.Unresolved).ToList();

            var beforeGroups = GroupByKey(beforeResolved);
            var afterGroups = GroupByKey(afterResolved);
            var keys = beforeGroups.Keys
                .Union(afterGroups.Keys)
                .OrderBy(k => k.Level, StringComparer.Ordinal)
                .ThenBy(k => k.ElementType)
                .ThenBy(k => k.GradeCode, StringComparer.Ordinal)
                .ToList();

            var lines = keys
                .Select(k => BuildLine(k, beforeGroups, afterGroups))
                .ToList();

            var beforeLevels = beforeResolved.Select(l => l.Level).ToHashSet(StringComparer.Ordinal);
            var afterLevels = afterResolved.Select(l => l.Level).ToHashSet(StringComparer.Ordinal);

            var addedLevels = afterLevels
                .Except(beforeLevels, StringComparer.Ordinal)
                .OrderBy(l => l, StringComparer.Ordinal)
                .ToList();

            var removedLevels = beforeLevels
                .Except(afterLevels, StringComparer.Ordinal)
                .OrderBy(l => l, StringComparer.Ordinal)
                .ToList();

            return new TakeoffDiff(
                lines,
                lines.Sum(l => l.ConcreteDeltaM3),
                lines.Sum(l => l.FormworkDeltaM2),
                lines.Sum(l => l.RebarDeltaKg) / 1000.0,
                addedLevels,
                removedLevels,
                basisBefore is not null && basisAfter is not null && basisBefore != basisAfter);
        }

        private static Dictionary<TakeoffDiffKey, TakeoffDiffTotals> GroupByKey(IEnumerable<TakeoffLineResult> lines)
        {
            return lines
                .GroupBy(l => new TakeoffDiffKey(l.Level, l.ElementType, l.GradeCode))
                .ToDictionary(
                    g => g.Key,
                    g => new TakeoffDiffTotals(
                        g.Sum(l => l.ConcreteM3),
                        g.Sum(l => l.FormworkM2),
                        g.Sum(l => l.RebarKg),
                        g.Select(l => l.Change).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
                        g.Select(l => l.Category).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))));
        }

        private static TakeoffDiffLine BuildLine(
            TakeoffDiffKey key,
            IReadOnlyDictionary<TakeoffDiffKey, TakeoffDiffTotals> beforeGroups,
            IReadOnlyDictionary<TakeoffDiffKey, TakeoffDiffTotals> afterGroups)
        {
            var hasBefore = beforeGroups.TryGetValue(key, out var before);
            var hasAfter = afterGroups.TryGetValue(key, out var after);

            before ??= TakeoffDiffTotals.Zero;
            after ??= TakeoffDiffTotals.Zero;

            var status = (hasBefore, hasAfter) switch
            {
                (true, true) => TakeoffDiffStatus.Matched,
                (false, true) => TakeoffDiffStatus.Added,
                (true, false) => TakeoffDiffStatus.Removed,
                _ => TakeoffDiffStatus.Matched
            };

            return new TakeoffDiffLine(
                key.Level,
                key.ElementType,
                key.GradeCode,
                before.ConcreteM3,
                after.ConcreteM3,
                after.ConcreteM3 - before.ConcreteM3,
                before.FormworkM2,
                after.FormworkM2,
                after.FormworkM2 - before.FormworkM2,
                before.RebarKg,
                after.RebarKg,
                after.RebarKg - before.RebarKg,
                status,
                after.Change ?? before.Change,
                after.Category ?? before.Category);
        }

        private sealed record TakeoffDiffKey(string Level, TakeoffElementType ElementType, string GradeCode);

        private sealed record TakeoffDiffTotals(double ConcreteM3, double FormworkM2, double RebarKg, string? Change, string? Category)
        {
            public static TakeoffDiffTotals Zero { get; } = new(0, 0, 0, null, null);
        }
    }
}
