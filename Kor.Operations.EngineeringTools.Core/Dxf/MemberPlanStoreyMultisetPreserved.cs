using System.Globalization;
using System.Text;

namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// Differential guard for rename-only member transforms.
///
/// Names are intentionally absent from the key: a stack merge is meant to change them. What must
/// not change is the multiset of member-kind, plan-position and storey assignments.
/// </summary>
public static class MemberPlanStoreyMultisetPreserved
{
    public sealed record Snapshot(
        IReadOnlyDictionary<AssignmentKey, int> Assignments,
        int ObjectCount);

    public sealed record Comparison(
        bool Preserved,
        string Message,
        IReadOnlyList<AssignmentDelta> Deltas);

    public sealed record AssignmentDelta(
        string Kind,
        string Storey,
        string Position,
        int Before,
        int After);

    public sealed record AssignmentKey(string Kind, string Position, string Storey);

    public static Snapshot Capture(E2kDocument document)
    {
        var pointsByObject = document.PlanPointsOfObjects();
        var storeysByObject = document.StoreysByObject();
        var counts = new Dictionary<AssignmentKey, int>();

        var contents = document.ReadContents();
        foreach (var obj in contents.Objects)
        {
            if (!pointsByObject.TryGetValue(obj.Name, out var points)) continue;
            if (!storeysByObject.TryGetValue(obj.Name, out var storeys)) continue;
            if (points.Count == 0 || storeys.Count == 0) continue;

            string position = PositionKey(points);
            foreach (string storey in storeys)
            {
                var key = new AssignmentKey(obj.Kind.ToUpperInvariant(), position, storey);
                counts[key] = counts.TryGetValue(key, out int had) ? had + 1 : 1;
            }
        }

        return new Snapshot(counts, contents.Objects.Count);
    }

    public static Comparison Compare(E2kDocument before, E2kDocument after)
        => Compare(Capture(before), Capture(after));

    public static Comparison Compare(string beforePath, string afterPath)
        => Compare(E2kDocument.Load(beforePath), E2kDocument.Load(afterPath));

    public static Comparison Compare(Snapshot before, E2kDocument after)
        => Compare(before, Capture(after));

    public static Comparison Compare(Snapshot before, Snapshot after)
    {
        var deltas = before.Assignments.Keys
            .Concat(after.Assignments.Keys)
            .Distinct()
            .Select(k => new AssignmentDelta(
                k.Kind,
                k.Storey,
                k.Position,
                before.Assignments.TryGetValue(k, out int oldCount) ? oldCount : 0,
                after.Assignments.TryGetValue(k, out int newCount) ? newCount : 0))
            .Where(d => d.Before != d.After)
            .OrderBy(d => d.Storey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Position, StringComparer.Ordinal)
            .ToList();

        if (deltas.Count == 0)
            return new Comparison(true, string.Empty, deltas);

        return new Comparison(false, Message(deltas), deltas);
    }

    public static void Assert(E2kDocument before, E2kDocument after)
        => ThrowIfChanged(Compare(before, after));

    public static void Assert(string beforePath, string afterPath)
        => ThrowIfChanged(Compare(beforePath, afterPath));

    public static void Assert(Snapshot before, E2kDocument after)
        => ThrowIfChanged(Compare(before, after));

    private static void ThrowIfChanged(Comparison comparison)
    {
        if (!comparison.Preserved)
            throw new InvalidOperationException(comparison.Message);
    }

    private static string Message(IReadOnlyList<AssignmentDelta> deltas)
    {
        var byStoreyKind = deltas
            .GroupBy(d => (d.Storey, d.Kind))
            .OrderBy(g => g.Key.Storey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Key.Kind, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        sb.Append("Member plan/storey multiset changed; this transform is not rename-only.");

        foreach (var group in byStoreyKind.Take(8))
        {
            int before = group.Sum(d => d.Before);
            int after = group.Sum(d => d.After);
            sb.AppendLine();
            sb.Append(group.Key.Storey);
            sb.Append(": ");
            sb.Append(KindLabel(group.Key.Kind));
            sb.Append(' ');
            sb.Append(before.ToString(CultureInfo.InvariantCulture));
            sb.Append(" -> ");
            sb.Append(after.ToString(CultureInfo.InvariantCulture));

            var lost = group.Where(d => d.Before > d.After).Take(3).Select(Example).ToList();
            var gained = group.Where(d => d.After > d.Before).Take(3).Select(Example).ToList();
            if (lost.Count > 0)
            {
                sb.Append("; lost ");
                sb.Append(string.Join(", ", lost));
            }
            if (gained.Count > 0)
            {
                sb.Append("; gained ");
                sb.Append(string.Join(", ", gained));
            }
        }

        if (byStoreyKind.Count > 8)
            sb.AppendLine().Append("...and ")
                .Append((byStoreyKind.Count - 8).ToString(CultureInfo.InvariantCulture))
                .Append(" more storey/kind group(s).");

        return sb.ToString();
    }

    private static string Example(AssignmentDelta delta)
    {
        int change = delta.After - delta.Before;
        return FriendlyPosition(delta.Position) + " (" + (change > 0 ? "+" : string.Empty)
            + change.ToString(CultureInfo.InvariantCulture) + ")";
    }

    private static string KindLabel(string kind) => kind switch
    {
        "COLUMN" => "columns",
        "PANEL" => "panels",
        "FLOOR" => "floors",
        _ => kind.ToLowerInvariant() + " objects",
    };

    private static string PositionKey(IReadOnlyList<(double X, double Y)> points)
    {
        var raw = points.Select(PointKey).ToList();
        if (raw.Count == 1) return raw[0];

        return Rotations(raw)
            .Concat(Rotations(raw.AsEnumerable().Reverse().ToList()))
            .Min(StringComparer.Ordinal)!;
    }

    private static IEnumerable<string> Rotations(IReadOnlyList<string> points)
    {
        for (int start = 0; start < points.Count; start++)
            yield return string.Join("|", Enumerable.Range(0, points.Count).Select(i => points[(start + i) % points.Count]));
    }

    private static string PointKey((double X, double Y) point)
        => Exact(point.X) + "," + Exact(point.Y);

    private static string Exact(double value)
        => value.ToString("G17", CultureInfo.InvariantCulture);

    private static string FriendlyPosition(string position)
    {
        var points = position.Split('|');
        if (points.Length == 1 || points.All(p => p == points[0]))
            return "(" + points[0].Replace(",", ", ") + ")";

        if (points.Length == 2)
            return "(" + points[0].Replace(",", ", ") + ") to (" + points[1].Replace(",", ", ") + ")";

        return "(" + points[0].Replace(",", ", ") + ") to (" + points[1].Replace(",", ", ") + "), ...";
    }
}
