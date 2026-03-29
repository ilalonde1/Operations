#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    internal sealed class GeometryExclusionState
    {
        public HashSet<int> Slabs { get; } = new();
        public HashSet<int> Lines { get; } = new();
        public HashSet<int> Columns { get; } = new();
        public HashSet<(byte R, byte G, byte B)> Colors { get; } = new();

        public void Clear() { Slabs.Clear(); Lines.Clear(); Columns.Clear(); Colors.Clear(); }

        public bool HasIndexExclusions => Slabs.Count > 0 || Lines.Count > 0 || Columns.Count > 0;

        public bool IsSlabExcluded(int i, IReadOnlyList<(byte R, byte G, byte B)> slabColors)
            => Slabs.Contains(i) || (slabColors.Count > i && Colors.Contains(slabColors[i]));

        public bool IsLineExcluded(int i, IReadOnlyList<(byte R, byte G, byte B)> lineColors)
            => Lines.Contains(i) || (lineColors.Count > i && Colors.Contains(lineColors[i]));

        public bool IsColumnExcluded(int i, IReadOnlyList<(byte R, byte G, byte B)> columnColors)
            => Columns.Contains(i) || (columnColors.Count > i && Colors.Contains(columnColors[i]));
    }
}
