#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    internal static class F2kWriter
    {
        internal static void WriteDesignStrips(
            StreamWriter sw,
            IReadOnlyList<DesignStrip> strips,
            CultureInfo ic)
        {
            if (strips.Count == 0) return;
            sw.WriteLine("TABLE:  \"DESIGN STRIPS\"");
            foreach (var s in strips)
            {
                string dir = s.IsAlongX ? "A" : "B";
                sw.WriteLine(
                    $"   Name={s.Name}   Direction={dir}" +
                    $"   X1={s.X1.ToString("F1", ic)}   Y1={s.Y1.ToString("F1", ic)}" +
                    $"   X2={s.X2.ToString("F1", ic)}   Y2={s.Y2.ToString("F1", ic)}" +
                    $"   WidthLeft={s.HalfWidth.ToString("F1", ic)}" +
                    $"   WidthRight={s.HalfWidth.ToString("F1", ic)}");
            }
            sw.WriteLine();
        }

        internal static void WriteGridLines(
            StreamWriter sw,
            IReadOnlyList<StructuralGridLine> gridLines,
            CultureInfo ic)
        {
            if (gridLines.Count == 0) return;

            sw.WriteLine("TABLE:  \"GRID DEFINITIONS\"");
            sw.WriteLine("   CoordSys=GLOBAL   GridSysType=Cartesian   XDirLabel=1   YDirLabel=A   \"Bubble Size\"=1500   ResetToDefault=No");
            sw.WriteLine();

            sw.WriteLine("TABLE:  \"GRID LINES\"");
            foreach (var g in gridLines)
            {
                string axisDir = g.IsAlongX ? "X" : "Y";
                sw.WriteLine($"   CoordSys=GLOBAL   AxisDir={axisDir}   GridID={g.Label}" +
                    $"   Ordinate={g.OrdMm.ToString("F1", ic)}" +
                    $"   LineColor=Gray8Dark   Visible=Yes   BubbleLoc=End");
            }
            sw.WriteLine();
        }

        internal static void WriteColumnSections(
            StreamWriter sw,
            IReadOnlyList<string> columnPointNames,
            IReadOnlyList<(string SecName, double W, double D)> sections,
            CultureInfo ic)
        {
            if (columnPointNames.Count == 0) return;

            var uniqueSecs = sections
                .Select(s => (s.SecName, s.W, s.D))
                .Distinct()
                .OrderBy(s => s.SecName)
                .ToList();

            sw.WriteLine("TABLE:  \"COLUMN SECTION DEFINITIONS\"");
            foreach (var (name, w, d) in uniqueSecs)
                sw.WriteLine($"   Name={name}   Shape=Rectangular" +
                    $"   Width={w.ToString("0.###", ic)}   Depth={d.ToString("0.###", ic)}");
            sw.WriteLine();

            sw.WriteLine("TABLE:  \"POINT ASSIGNMENTS - COLUMN BELOW\"");
            for (int i = 0; i < columnPointNames.Count && i < sections.Count; i++)
                sw.WriteLine($"   UniqueName={columnPointNames[i]}   SecName={sections[i].SecName}");
            sw.WriteLine();
        }

        internal static void WriteBeamSections(
            StreamWriter sw,
            IReadOnlyList<(string Id, string J1, string J2, double LenMm, int LineIdx)> lineSegs,
            string?[] lineSecNames,
            CultureInfo ic)
        {
            var secToSegments = new Dictionary<string, (double W, double D, List<string> SegIds)>();
            foreach (var (id, _, _, _, lineIdx) in lineSegs)
            {
                if (lineIdx >= lineSecNames.Length || lineSecNames[lineIdx] is not string secName) continue;
                var parts = secName.TrimStart('B').Split('x');
                if (parts.Length != 2 ||
                    !double.TryParse(parts[0], out double w) ||
                    !double.TryParse(parts[1], out double d)) continue;
                if (!secToSegments.ContainsKey(secName))
                    secToSegments[secName] = (w, d, new List<string>());
                secToSegments[secName].SegIds.Add(id);
            }

            if (secToSegments.Count == 0) return;

            sw.WriteLine("TABLE:  \"BEAM PROPERTY DEFINITIONS\"");
            foreach (var (name, (w, d, _)) in secToSegments.OrderBy(kv => kv.Key))
                sw.WriteLine($"   Name={name}   Shape=Rectangular" +
                    $"   Width={w.ToString("0.###", ic)}   Depth={d.ToString("0.###", ic)}");
            sw.WriteLine();

            sw.WriteLine("TABLE:  \"LINE ASSIGNMENTS - SECTION PROPERTIES\"");
            foreach (var (name, (_, _, segIds)) in secToSegments.OrderBy(kv => kv.Key))
                foreach (var segId in segIds)
                    sw.WriteLine($"   UniqueName={segId}   SecName={name}");
            sw.WriteLine();
        }
    }
}
