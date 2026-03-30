#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    internal static class EtabsE2kExporter
    {
        public static void Export(
            string outputPath,
            ExtractedGeometry geom,
            Dictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings = null)
            => Export(outputPath,
                new[] { (geom, "Story 1", 0.0) },
                colorSettings);

        public static void Export(
            string outputPath,
            IReadOnlyList<(ExtractedGeometry Geom, string StoryName, double ElevationMm)> stories,
            Dictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings = null)
        {
            if (stories.Count == 0) return;

            var orderedStories = stories.OrderByDescending(s => s.ElevationMm).ToList();
            double totalWeight = 0.0, sumX = 0.0, sumY = 0.0;
            foreach (var (geom, _, _) in orderedStories)
            {
                foreach (var pts in geom.Slabs)
                {
                    double w = PolygonProcessor.PolygonAreaMm2(pts);
                    var (pcx, pcy) = PolygonProcessor.PolygonAreaCentroid(pts);
                    sumX += pcx * w;
                    sumY += pcy * w;
                    totalWeight += w;
                }
                foreach (var (x, y) in geom.Columns) { sumX += x; sumY += y; totalWeight += 1.0; }
                foreach (var pts in geom.Lines)
                {
                    double w = PolygonProcessor.PathLength(pts);
                    var (pcx, pcy) = PolygonProcessor.Centroid(pts);
                    sumX += pcx * w;
                    sumY += pcy * w;
                    totalWeight += w;
                }
            }
            if (totalWeight == 0.0) return;
            double cx = sumX / totalWeight, cy = sumY / totalWeight;

            List<(double X, double Y)> Ctr(List<(double X, double Y)> pts) => pts.Select(p => (p.X - cx, p.Y - cy)).ToList();
            bool Ok(double x, double y) => !double.IsNaN(x) && !double.IsInfinity(x) && !double.IsNaN(y) && !double.IsInfinity(y);
            List<(double X, double Y)> FilterPts(List<(double X, double Y)> raw)
            {
                var result = new List<(double X, double Y)>();
                foreach (var p in raw)
                    if (Ok(p.X, p.Y) && (result.Count == 0 || PolygonProcessor.Distance(result[^1], p) >= PdfToSafeConstants.MinVertexDistanceMm))
                        result.Add(p);
                return result;
            }

            var ic = CultureInfo.InvariantCulture;
            string GradeFor((byte R, byte G, byte B) color) =>
                colorSettings != null && colorSettings.TryGetValue(color, out var s) && !string.IsNullOrWhiteSpace(s.GradeCode)
                    ? s.GradeCode : PdfToSafeConstants.DefaultGradeCode;
            string PropNameFor((byte R, byte G, byte B) color)
            {
                double thickness = colorSettings != null && colorSettings.TryGetValue(color, out var s) && s.ThicknessMm > 0
                    ? s.ThicknessMm : PdfToSafeConstants.DefaultThicknessMm;
                return $"S{thickness.ToString("0.###", ic).Replace('.', '_')}{GradeFor(color)}";
            }

            var pointCoords = new List<(string Name, double X, double Y, string StoryName)>();
            var areas = new List<(string Id, List<string> PtNames, string StoryName, string PropName, (byte R, byte G, byte B) Color)>();
            var lineSegs = new List<(string Id, string J1, string J2, string StoryName)>();
            var columnPointNames = new List<(string Name, string StoryName)>();

            for (int storyIndex = 0; storyIndex < orderedStories.Count; storyIndex++)
            {
                var (geometry, storyName, _) = orderedStories[storyIndex];
                var xSlabs = new List<List<(double X, double Y)>>();
                var xSlabColors = new List<(byte R, byte G, byte B)>();
                var xLines = new List<List<(double X, double Y)>>();

                for (int i = 0; i < geometry.Slabs.Count; i++)
                {
                    var pts = FilterPts(Ctr(geometry.Slabs[i]));
                    if (pts.Count >= 3)
                    {
                        xSlabs.Add(pts);
                        xSlabColors.Add(i < geometry.SlabColors.Count ? geometry.SlabColors[i] : ((byte)0, (byte)0, (byte)0));
                    }
                }
                for (int i = 0; i < geometry.Lines.Count; i++)
                {
                    var pts = FilterPts(Ctr(geometry.Lines[i]));
                    if (pts.Count >= 2)
                        xLines.Add(pts);
                }
                (xSlabs, xSlabColors) = PolygonProcessor.ProcessSlabs(xSlabs, xSlabColors);

                var pointMap = new Dictionary<(long, long), string>();
                int pointCounter = 0;
                string Pt(double xMm, double yMm)
                {
                    var key = ((long)Math.Round(xMm * 10), (long)Math.Round(yMm * 10));
                    if (!pointMap.TryGetValue(key, out string? name))
                    {
                        name = $"s{storyIndex + 1}_J{++pointCounter}";
                        pointMap[key] = name;
                        pointCoords.Add((name, key.Item1 / 10000.0, key.Item2 / 10000.0, storyName));
                    }
                    return name;
                }

                int areaCounter = 0;
                for (int i = 0; i < xSlabs.Count; i++)
                    areas.Add(($"s{storyIndex + 1}_A{++areaCounter}", xSlabs[i].Select(p => Pt(p.X, p.Y)).ToList(), storyName, PropNameFor(xSlabColors[i]), xSlabColors[i]));

                int lineCounter = 0;
                foreach (var pts in xLines)
                {
                    for (int i = 0; i < pts.Count - 1; i++)
                    {
                        string j1 = Pt(pts[i].X, pts[i].Y);
                        string j2 = Pt(pts[i + 1].X, pts[i + 1].Y);
                        if (j1 != j2)
                            lineSegs.Add(($"s{storyIndex + 1}_L{++lineCounter}", j1, j2, storyName));
                    }
                }

                foreach (var (colX, colY) in geometry.Columns)
                {
                    double px = colX - cx, py = colY - cy;
                    if (!Ok(px, py)) continue;
                    columnPointNames.Add((Pt(px, py), storyName));
                }
            }

            using var sw = new StreamWriter(outputPath, false, Encoding.ASCII);
            sw.WriteLine("$ ETABS v2023 TEXT FILE");
            sw.WriteLine("$   Generated by Kor Operations - Structural PDF Import");
            sw.WriteLine();
            sw.WriteLine("$ CONTROLS");
            sw.WriteLine("  ACTIVEDOF  UX UY UZ RX RY RZ");
            sw.WriteLine("  UNITSYS  KN M C");
            sw.WriteLine();
            sw.WriteLine("$ STORIES - IN SEQUENCE FROM TOP");
            for (int i = 0; i < orderedStories.Count; i++)
            {
                double heightM = i < orderedStories.Count - 1
                    ? (orderedStories[i].ElevationMm - orderedStories[i + 1].ElevationMm) / 1000.0
                    : 3.0;
                sw.WriteLine($"  STORY \"{orderedStories[i].StoryName}\"  HEIGHT {heightM.ToString("0.###", ic)}  MASTERSTORY \"Yes\"  ABOVEGRADE \"Yes\"");
            }
            sw.WriteLine("  STORY \"Base\"  HEIGHT 0  MASTERSTORY \"Yes\"  ABOVEGRADE \"Yes\"");
            sw.WriteLine();
            sw.WriteLine("$ DIAPHRAGM NAMES");
            sw.WriteLine("  DIAPHRAGM \"D1\"  TYPE RIGID");
            sw.WriteLine();
            sw.WriteLine("$ POINT COORDINATES");
            foreach (var (name, x, y, _) in pointCoords)
                sw.WriteLine($"  POINT \"{name}\"  {x.ToString("0.####", ic)}  {y.ToString("0.####", ic)}");
            sw.WriteLine();
            sw.WriteLine("$ AREA CONNECTIVITIES");
            foreach (var (id, ptNames, _, _, _) in areas)
                sw.WriteLine($"  AREA \"{id}\"  FLOOR  {string.Join(" ", ptNames.Select(p => $"\"{p}\""))}");
            sw.WriteLine();
            sw.WriteLine("$ LINE CONNECTIVITIES");
            foreach (var (id, j1, j2, _) in lineSegs)
                sw.WriteLine($"  LINE \"{id}\"  BEAM  \"{j1}\"  \"{j2}\"");
            sw.WriteLine();

            var usedGrades = areas.Select(a => GradeFor(a.Color)).Distinct().OrderBy(g => g).ToList();
            if (usedGrades.Count == 0) usedGrades.Add(PdfToSafeConstants.DefaultConcreteGrade);
            sw.WriteLine("$ MATERIAL PROPERTIES");
            foreach (var grade in usedGrades)
            {
                var (e, _, fc) = StructuralMaterialDatabase.GetGrade(grade);
                sw.WriteLine($"  MATERIAL \"{grade}\"  CONCRETE  W 24  FC {(fc * 1000).ToString("0", ic)}  FY 0  EMOD {(e * 1000).ToString("0", ic)}  POIS 0.17  THERM 1E-05");
            }
            sw.WriteLine();

            var uniqueSlabProps = areas
                .Select(a =>
                {
                    var settings = colorSettings != null && colorSettings.TryGetValue(a.Color, out var s)
                        ? s : new SlabColorSettings();
                    return (a.PropName, ThicknessM: (settings.ThicknessMm > 0 ? settings.ThicknessMm : PdfToSafeConstants.DefaultThicknessMm) / 1000.0,
                        Grade: !string.IsNullOrWhiteSpace(settings.GradeCode) ? settings.GradeCode : PdfToSafeConstants.DefaultGradeCode);
                })
                .Distinct()
                .OrderBy(p => p.PropName)
                .ToList();

            sw.WriteLine("$ WALL/SLAB/DECK SECTION PROPERTIES");
            foreach (var (propName, thicknessM, grade) in uniqueSlabProps)
                sw.WriteLine($"  SLABPROP \"{propName}\"  SLAB  MAT \"{grade}\"  THICK {thicknessM.ToString("0.###", ic)}");
            sw.WriteLine();

            sw.WriteLine("$ POINT OBJECT DATA");
            foreach (var (name, _, _, storyName) in pointCoords)
                sw.WriteLine($"  POINTOBJ \"{name}\"  STORY \"{storyName}\"");
            sw.WriteLine();

            sw.WriteLine("$ AREA OBJECT DATA");
            foreach (var (id, _, storyName, propName, _) in areas)
                sw.WriteLine($"  AREAOBJ \"{id}\"  STORY \"{storyName}\"  TYPE \"Floor\"  SECTION \"{propName}\"  DIAPH \"D1\"");
            sw.WriteLine();

            sw.WriteLine("$ LINE OBJECT DATA");
            foreach (var (id, _, _, storyName) in lineSegs)
                sw.WriteLine($"  LINEOBJ \"{id}\"  STORY \"{storyName}\"  TYPE \"Beam\"  SECTION \"BEAM_DEFAULT\"  ANG 0");
            sw.WriteLine();

            sw.WriteLine("$ POINT ASSIGNMENTS");
            foreach (var (name, sName) in columnPointNames)
                sw.WriteLine($"  POINTASSIGN  \"{name}\"  STORY \"{sName}\"  RESTRAINT \"Pinned\"");
            sw.WriteLine();

            bool hasSdlLoads = areas.Any(a => colorSettings != null && colorSettings.TryGetValue(a.Color, out var s) && s.SdlKPa > 0);
            bool hasLiveLoads = areas.Any(a => colorSettings != null && colorSettings.TryGetValue(a.Color, out var s) && s.LiveKPa > 0);
            sw.WriteLine("$ LOAD PATTERNS");
            sw.WriteLine("  LOADPAT \"DEAD\"  TYPE \"Dead\"  SELFWEIGHT 1");
            if (hasSdlLoads) sw.WriteLine("  LOADPAT \"SDL\"  TYPE \"Super Dead\"  SELFWEIGHT 0");
            if (hasLiveLoads) sw.WriteLine("  LOADPAT \"LIVE\"  TYPE \"Live\"  SELFWEIGHT 0");
            sw.WriteLine();

            sw.WriteLine("$ AREA LOADS");
            foreach (var (id, _, storyName, _, color) in areas)
            {
                if (colorSettings == null || !colorSettings.TryGetValue(color, out var settings))
                    continue;
                if (settings.SdlKPa > 0)
                    sw.WriteLine($"  AREALOAD  \"{id}\"  STORY \"{storyName}\"  LOADPAT \"SDL\"  DIR \"Gravity\"  UNIFF {settings.SdlKPa.ToString("0.###", ic)}");
                if (settings.LiveKPa > 0)
                    sw.WriteLine($"  AREALOAD  \"{id}\"  STORY \"{storyName}\"  LOADPAT \"LIVE\"  DIR \"Gravity\"  UNIFF {settings.LiveKPa.ToString("0.###", ic)}");
            }
            sw.WriteLine();
            sw.WriteLine("$ END OF MODEL FILE");
        }
    }
}
