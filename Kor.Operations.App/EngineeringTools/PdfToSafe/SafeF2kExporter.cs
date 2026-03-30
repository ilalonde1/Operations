#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    internal static class SafeF2kExporter
    {
        public static void Export(
            ExtractedGeometry geometry,
            string outputPath,
            HashSet<int>? excludedSlabs = null,
            HashSet<int>? excludedLines = null,
            HashSet<int>? excludedColumns = null,
            HashSet<(byte R, byte G, byte B)>? excludedColors = null,
            Dictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings = null,
            ExportSettings? settings = null)
        {
            settings ??= new ExportSettings();
            double totalWeight = 0.0, sumX = 0.0, sumY = 0.0;
            foreach (var pts in geometry.Slabs)
            { double w = PolygonProcessor.PolygonAreaMm2(pts); var (pcx, pcy) = PolygonProcessor.PolygonAreaCentroid(pts); sumX += pcx * w; sumY += pcy * w; totalWeight += w; }
            foreach (var (x, y) in geometry.Columns) { sumX += x; sumY += y; totalWeight += 1.0; }
            foreach (var pts in geometry.Lines)
            { double w = PolygonProcessor.PathLength(pts); var (pcx, pcy) = PolygonProcessor.Centroid(pts); sumX += pcx * w; sumY += pcy * w; totalWeight += w; }
            if (totalWeight == 0.0) return;
            double cx = sumX / totalWeight, cy = sumY / totalWeight;

            List<(double X, double Y)> Ctr(List<(double X, double Y)> pts) =>
                pts.Select(p => (p.X - cx, p.Y - cy)).ToList();

            const double minSeg = PdfToSafeConstants.MinVertexDistanceMm;
            bool Ok(double x, double y) =>
                !double.IsNaN(x) && !double.IsInfinity(x) &&
                !double.IsNaN(y) && !double.IsInfinity(y);

            List<(double X, double Y)> FilterPts(List<(double X, double Y)> raw)
            {
                var result = new List<(double X, double Y)>();
                foreach (var p in raw)
                {
                    if (!Ok(p.X, p.Y)) continue;
                    if (result.Count == 0 || PolygonProcessor.Distance(result[^1], p) >= minSeg)
                        result.Add(p);
                }
                return result;
            }

            var xSlabs = new List<List<(double X, double Y)>>();
            var xSlabColors = new List<(byte R, byte G, byte B)>();
            var xLines = new List<List<(double X, double Y)>>();
            var xColumns = new List<(double X, double Y)>();

            for (int i = 0; i < geometry.Slabs.Count; i++)
            {
                if (excludedSlabs?.Contains(i) == true) continue;
                if (excludedColors != null && i < geometry.SlabColors.Count && excludedColors.Contains(geometry.SlabColors[i])) continue;
                var pts = FilterPts(Ctr(geometry.Slabs[i]));
                if (pts.Count >= 3)
                {
                    xSlabs.Add(pts);
                    xSlabColors.Add(i < geometry.SlabColors.Count ? geometry.SlabColors[i] : ((byte)0, (byte)0, (byte)0));
                }
            }
            for (int i = 0; i < geometry.Lines.Count; i++)
            {
                if (excludedLines?.Contains(i) == true) continue;
                if (excludedColors != null && i < geometry.LineColors.Count && excludedColors.Contains(geometry.LineColors[i])) continue;
                var pts = FilterPts(Ctr(geometry.Lines[i]));
                if (pts.Count >= 2) xLines.Add(pts);
            }
            for (int i = 0; i < geometry.Columns.Count; i++)
            {
                if (excludedColumns?.Contains(i) == true) continue;
                if (excludedColors != null && i < geometry.ColumnColors.Count && excludedColors.Contains(geometry.ColumnColors[i])) continue;
                var (colX, colY) = geometry.Columns[i];
                double px = colX - cx, py = colY - cy;
                if (Ok(px, py)) xColumns.Add((px, py));
            }

            (xSlabs, xSlabColors) = PolygonProcessor.ProcessSlabs(xSlabs, xSlabColors);
            var openingPairs = PolygonProcessor.DetectOpenings(xSlabs);
            var childIndices = new HashSet<int>(openingPairs.Select(p => p.ChildIndex));
            var annotationThicknesses = ThicknessAnnotationParser.AssignToSlabs(
                xSlabs,
                geometry.TextAnnotations.Select(a => (a.Text, a.X - cx, a.Y - cy)).ToList());

            var pointOrder = new List<(string Name, double X, double Y)>();
            var pointMap = new Dictionary<(long, long), string>();
            int ptCounter = 0;
            var ic = CultureInfo.InvariantCulture;

            string Pt(double xMm, double yMm)
            {
                var key = ((long)Math.Round(xMm * 10), (long)Math.Round(yMm * 10));
                if (!pointMap.TryGetValue(key, out string? name))
                {
                    name = $"P{++ptCounter}";
                    pointMap[key] = name;
                    pointOrder.Add((name, key.Item1 / 10.0, key.Item2 / 10.0));
                }
                return name;
            }

            string GradeFor((byte R, byte G, byte B) color)
            {
                if (colorSettings != null && colorSettings.TryGetValue(color, out var s) &&
                    !string.IsNullOrWhiteSpace(s.GradeCode))
                    return s.GradeCode;
                return PdfToSafeConstants.DefaultGradeCode;
            }

            (string PropName, double ThicknessMm, string GradeCode) SlabPropertyFor((byte R, byte G, byte B) color, int slabIdx)
            {
                string grade = GradeFor(color);
                double thickness = PdfToSafeConstants.DefaultThicknessMm;
                if (slabIdx >= 0 && slabIdx < annotationThicknesses.Length && annotationThicknesses[slabIdx].HasValue)
                    thickness = annotationThicknesses[slabIdx]!.Value;
                else if (colorSettings != null && colorSettings.TryGetValue(color, out var s) && s.ThicknessMm > 0)
                    thickness = s.ThicknessMm;
                var tStr = thickness.ToString("0.###", ic).Replace('.', '_');
                return ($"S{tStr}{grade}", thickness, grade);
            }

            var areas = new List<(string Id, List<string> PtNames, List<(double X, double Y)> Coords, string PropName, double ThicknessMm, string GradeCode, (byte R, byte G, byte B) Color)>();
            var lineSegs = new List<(string Id, string J1, string J2, double LenMm)>();

            int aIdx = 0, lIdx = 0;
            var slabIndexToAreaId = new Dictionary<int, string>();
            for (int i = 0; i < xSlabs.Count; i++)
            {
                if (childIndices.Contains(i)) continue;
                var pts = xSlabs[i];
                var names = pts.Select(p => Pt(p.X, p.Y)).ToList();
                var color = xSlabColors[i];
                var prop = SlabPropertyFor(color, i);
                string areaId = $"A{++aIdx}";
                slabIndexToAreaId[i] = areaId;
                areas.Add((areaId, names, pts, prop.PropName, prop.ThicknessMm, prop.GradeCode, color));
            }
            foreach (var pts in xLines)
            {
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    string j1 = Pt(pts[i].X, pts[i].Y);
                    string j2 = Pt(pts[i + 1].X, pts[i + 1].Y);
                    if (j1 != j2) lineSegs.Add(($"L{++lIdx}", j1, j2, PolygonProcessor.Distance(pts[i], pts[i + 1])));
                }
            }
            var columnPointNames = new List<string>();
            foreach (var (px, py) in xColumns)
                columnPointNames.Add(Pt(px, py));

            using var sw = new StreamWriter(outputPath, false, Encoding.ASCII);

            sw.WriteLine("$ Generated by Kor Operations - Structural PDF Import");
            sw.WriteLine();
            sw.WriteLine("TABLE:  \"PROGRAM CONTROL\"");
            sw.WriteLine("   ProgramName=SAFE   Version=23.0.0   CurrUnits=\"N, mm, C\"   MergeTol=1   ModelDatum=0");
            sw.WriteLine();

            var usedGrades = areas.Select(a => a.GradeCode).Distinct().OrderBy(g => g).ToList();
            if (usedGrades.Count == 0) usedGrades.Add(PdfToSafeConstants.DefaultConcreteGrade);

            sw.WriteLine("TABLE:  \"MATERIAL PROPERTIES - GENERAL\"");
            foreach (var g in usedGrades)
                sw.WriteLine($"   Material={g}   Type=Concrete   SymType=Isotropic   Grade={g}   Color=Blue");
            sw.WriteLine();
            sw.WriteLine("TABLE:  \"MATERIAL PROPERTIES - BASIC MECHANICAL PROPERTIES\"");
            foreach (var g in usedGrades)
            {
                var (e, gMod, _) = StructuralMaterialDatabase.GetGrade(g);
                sw.WriteLine($"   Material={g}   DensityType=Mass   UnitWeight=2.355E-05   UnitMass=2.4E-09   E1={e}   G12={gMod}   U12=0.17   A1=1E-05");
            }
            sw.WriteLine();
            sw.WriteLine("TABLE:  \"MATERIAL PROPERTIES - CONCRETE DATA\"");
            foreach (var g in usedGrades)
            {
                var (_, _, fc) = StructuralMaterialDatabase.GetGrade(g);
                sw.WriteLine($"   Material={g}   Fc={fc}   LtWtConc=No   IsUserFr=No   SSCurveOpt=Simple   SSHysType=Kinematic");
            }
            sw.WriteLine();

            var rebarSizes = StructuralMaterialDatabase.GetRebarSizes(settings.DesignCode);
            if (rebarSizes.Count > 0)
            {
                sw.WriteLine("TABLE:  \"REINFORCING BAR SIZES\"");
                foreach (var r in rebarSizes)
                    sw.WriteLine($"   Name={r.Name}   Diameter={r.DiameterMm.ToString("0.###", ic)}   Area={r.AreaMm2.ToString("0.###", ic)}");
                sw.WriteLine();
            }

            var uniqueSlabProps = areas
                .Select(a => (a.PropName, a.ThicknessMm, Grade: a.GradeCode))
                .Distinct()
                .OrderBy(p => p.PropName)
                .ToList();

            bool hasSdlLoads = areas.Any(a =>
                colorSettings != null &&
                colorSettings.TryGetValue(a.Color, out var s) &&
                s.SdlKPa > 0);
            bool hasLiveLoads = areas.Any(a =>
                colorSettings != null &&
                colorSettings.TryGetValue(a.Color, out var s) &&
                s.LiveKPa > 0);

            sw.WriteLine("TABLE:  \"LOAD PATTERN DEFINITIONS\"");
            sw.WriteLine("   Name=DEAD   Type=Dead   SelfWtMult=1");
            if (hasSdlLoads) sw.WriteLine("   Name=SDL   Type=SuperDead   SelfWtMult=0");
            if (hasLiveLoads) sw.WriteLine("   Name=LIVE   Type=Live   SelfWtMult=0");
            if (settings.IncludePtLoads) sw.WriteLine("   Name=LBALC   Type=Other   SelfWtMult=0");
            sw.WriteLine();

            if (!string.IsNullOrEmpty(settings.LoadCombCode))
            {
                var combos = StructuralMaterialDatabase.BuildLoadCombinations(settings.LoadCombCode, hasSdlLoads, hasLiveLoads, settings.IncludePtLoads);
                if (combos.Count > 0)
                {
                    sw.WriteLine("TABLE:  \"LOAD COMBINATION DEFINITIONS\"");
                    foreach (var (name, _) in combos)
                        sw.WriteLine($"   Name={name}   Type=LinearAdd");
                    sw.WriteLine();

                    sw.WriteLine("TABLE:  \"LOAD COMBINATION CASES\"");
                    foreach (var (name, cases) in combos)
                        foreach (var (pat, sf) in cases)
                            sw.WriteLine($"   Name={name}   LoadPat={pat}   SF={sf.ToString("0.##", ic)}");
                    sw.WriteLine();
                }
            }

            sw.WriteLine("TABLE:  \"SLAB PROPERTY DEFINITIONS\"");
            foreach (var (propName, thicknessMm, grade) in uniqueSlabProps)
            {
                sw.WriteLine($"   Name={propName}   \"Modeling Type\"=Shell-Thick   \"Property Type\"=Slab   Material={grade}   \"Slab Thickness\"={thicknessMm.ToString("0.###", ic)} _");
                sw.WriteLine("        \"f11 Modifier\"=1   \"f22 Modifier\"=1   \"f12 Modifier\"=1 _");
                sw.WriteLine("        \"m11 Modifier\"=1   \"m22 Modifier\"=1   \"m12 Modifier\"=1 _");
                sw.WriteLine("        \"v13 Modifier\"=1   \"v23 Modifier\"=1 _");
                sw.WriteLine("        \"Mass Modifier\"=1   \"Weight Modifier\"=1   Orthotropic?=No");
            }
            sw.WriteLine();

            sw.WriteLine("TABLE:  \"POINT OBJECT CONNECTIVITY\"");
            foreach (var (name, xMm, yMm) in pointOrder)
                sw.WriteLine($"   UniqueName={name}   \"Is Auto Point\"=No   IsSpecial=No   X={xMm.ToString("F1", ic)}   Y={yMm.ToString("F1", ic)}   Z=0");
            sw.WriteLine();

            if (areas.Count > 0)
            {
                sw.WriteLine("TABLE:  \"FLOOR OBJECT CONNECTIVITY\"");
                foreach (var (id, ptNames, coords, _, _, _, _) in areas)
                {
                    double perim = 0;
                    double area2 = 0;
                    for (int j = 0; j < coords.Count; j++)
                    {
                        var a = coords[j];
                        var b = coords[(j + 1) % coords.Count];
                        perim += Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                        area2 += a.X * b.Y - b.X * a.Y;
                    }
                    double areaVal = Math.Abs(area2) / 2.0;

                    var sb = new StringBuilder();
                    sb.Append($"   \"Unique Name\"={id}");
                    for (int j = 0; j < ptNames.Count; j++)
                        sb.Append($"   UniquePt{j + 1}={ptNames[j]}");
                    sb.Append($"   Perimeter={perim.ToString("F4", ic)}   Area={areaVal.ToString("F4", ic)}   GUID={Guid.NewGuid():D}");
                    sw.WriteLine(sb.ToString());
                }
                sw.WriteLine();

                if (settings.MeshSizeMm > 0)
                {
                    sw.WriteLine("TABLE:  \"FLOOR AUTO MESH OPTIONS\"");
                    foreach (var (id, _, _, _, _, _, _) in areas)
                        sw.WriteLine($"   UniqueName={id}   MeshType=Generalized   MaxMeshSize={settings.MeshSizeMm.ToString("0.###", ic)}");
                    sw.WriteLine();
                }

                if (openingPairs.Count > 0)
                {
                    var openingRows = new List<(string OpeningId, string ParentAreaId, List<string> PtNames)>();
                    int oIdx = 0;
                    foreach (var (parentSlabIdx, childSlabIdx) in openingPairs)
                    {
                        if (!slabIndexToAreaId.TryGetValue(parentSlabIdx, out string? parentId)) continue;
                        var pts = xSlabs[childSlabIdx];
                        var names = pts.Select(p => Pt(p.X, p.Y)).ToList();
                        openingRows.Add(($"O{++oIdx}", parentId, names));
                    }

                    if (openingRows.Count > 0)
                    {
                        sw.WriteLine("TABLE:  \"FLOOR OBJECT OPENINGS\"");
                        foreach (var (oid, parentId, ptNames) in openingRows)
                        {
                            var sb = new StringBuilder();
                            sb.Append($"   UniqueName={oid}   FloorObject={parentId}");
                            for (int j = 0; j < ptNames.Count; j++)
                                sb.Append($"   UniquePt{j + 1}={ptNames[j]}");
                            sw.WriteLine(sb.ToString());
                        }
                        sw.WriteLine();
                    }
                }

                sw.WriteLine("TABLE:  \"AREA ASSIGNMENTS - SECTION PROPERTIES\"");
                foreach (var (id, _, _, propName, _, _, _) in areas)
                    sw.WriteLine($"   UniqueName={id}   \"Section Property\"={propName}   \"Property Type\"=Slab");
                sw.WriteLine();

                if (columnPointNames.Count > 0)
                {
                    sw.WriteLine("TABLE:  \"JOINT ASSIGNMENTS - RESTRAINTS\"");
                    foreach (var pointName in columnPointNames)
                        sw.WriteLine($"   UniqueName={pointName}   U1=Yes   U2=Yes   U3=Yes   R1=No   R2=No   R3=No");
                    sw.WriteLine();
                }

                var areaLoads = new List<(string AreaId, string Pattern, double Value)>();
                foreach (var (id, _, _, _, _, _, color) in areas)
                {
                    if (colorSettings == null || !colorSettings.TryGetValue(color, out var colorCfg))
                        continue;

                    double sdlValue = colorCfg.SdlKPa * 0.001;
                    double liveValue = colorCfg.LiveKPa * 0.001;

                    if (sdlValue > 0) areaLoads.Add((id, "SDL", sdlValue));
                    if (liveValue > 0) areaLoads.Add((id, "LIVE", liveValue));
                }

                if (areaLoads.Count > 0)
                {
                    sw.WriteLine("TABLE:  \"AREA LOAD ASSIGNMENTS - UNIFORM\"");
                    foreach (var (areaId, pattern, value) in areaLoads)
                        sw.WriteLine($"   UniqueName={areaId}   LoadPat={pattern}   Dir=Gravity   Value={value.ToString("0.###", ic)}");
                    sw.WriteLine();
                }
            }

            if (lineSegs.Count > 0)
            {
                sw.WriteLine("TABLE:  \"NULL LINE OBJECT CONNECTIVITY\"");
                foreach (var (id, j1, j2, lenMm) in lineSegs)
                    sw.WriteLine($"   \"Unique Name\"={id}   UniquePtI={j1}   UniquePtJ={j2}   Length={lenMm.ToString("F4", ic)}   GUID={Guid.NewGuid():D}");
                sw.WriteLine();
            }

            if (settings.AutoGenerateStrips && xSlabs.Count > 0)
            {
                var strips = DesignStripGenerator.Generate(
                    xSlabs, settings.StripSpacingMm, settings.StripAAlongX);
                WriteDesignStrips(sw, strips, ic);
            }

            sw.WriteLine("END TABLE DATA");
        }

        public static void Export(
            string outputPath,
            IReadOnlyList<(ExtractedGeometry Geom, string StoryName, double ElevationMm)> stories,
            Dictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings = null,
            ExportSettings? settings = null)
        {
            if (stories.Count == 0) return;
            settings ??= new ExportSettings();

            double totalWeight = 0.0, sumX = 0.0, sumY = 0.0;
            foreach (var (geom, _, _) in stories)
            {
                foreach (var pts in geom.Slabs)
                {
                    double w = PolygonProcessor.PolygonAreaMm2(pts);
                    var (pcx, pcy) = PolygonProcessor.PolygonAreaCentroid(pts);
                    sumX += pcx * w;
                    sumY += pcy * w;
                    totalWeight += w;
                }
                foreach (var (x, y) in geom.Columns)
                {
                    sumX += x;
                    sumY += y;
                    totalWeight += 1.0;
                }
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

            List<(double X, double Y)> Ctr(List<(double X, double Y)> pts) =>
                pts.Select(p => (p.X - cx, p.Y - cy)).ToList();

            const double minSeg = PdfToSafeConstants.MinVertexDistanceMm;
            bool Ok(double x, double y) =>
                !double.IsNaN(x) && !double.IsInfinity(x) &&
                !double.IsNaN(y) && !double.IsInfinity(y);

            List<(double X, double Y)> FilterPts(List<(double X, double Y)> raw)
            {
                var result = new List<(double X, double Y)>();
                foreach (var p in raw)
                {
                    if (!Ok(p.X, p.Y)) continue;
                    if (result.Count == 0 || PolygonProcessor.Distance(result[^1], p) >= minSeg)
                        result.Add(p);
                }
                return result;
            }

            var ic = CultureInfo.InvariantCulture;

            string GradeFor((byte R, byte G, byte B) color)
            {
                if (colorSettings != null && colorSettings.TryGetValue(color, out var s) &&
                    !string.IsNullOrWhiteSpace(s.GradeCode))
                    return s.GradeCode;
                return PdfToSafeConstants.DefaultGradeCode;
            }

            var pointOrder = new List<(string Name, double X, double Y, double Z)>();
            var areas = new List<(string Id, List<string> PtNames, List<(double X, double Y)> Coords, string PropName, double ThicknessMm, string GradeCode, (byte R, byte G, byte B) Color)>();
            var lineSegs = new List<(string Id, string J1, string J2, double LenMm)>();
            var columnPointNames = new List<string>();

            var allSlabsForStrips = new List<List<(double X, double Y)>>();
            var openingRows = new List<(string OpeningId, string ParentAreaId, List<string> PtNames)>();
            int openingCounter = 0;
            for (int storyIndex = 0; storyIndex < stories.Count; storyIndex++)
            {
                var (geometry, _, elevationMm) = stories[storyIndex];
                var xSlabs = new List<List<(double X, double Y)>>();
                var xSlabColors = new List<(byte R, byte G, byte B)>();
                var xLines = new List<List<(double X, double Y)>>();
                var xColumns = new List<(double X, double Y)>();

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
                for (int i = 0; i < geometry.Columns.Count; i++)
                {
                    var (colX, colY) = geometry.Columns[i];
                    double px = colX - cx, py = colY - cy;
                    if (Ok(px, py))
                        xColumns.Add((px, py));
                }

                (xSlabs, xSlabColors) = PolygonProcessor.ProcessSlabs(xSlabs, xSlabColors);
                var openingPairs = PolygonProcessor.DetectOpenings(xSlabs);
                var childIndices = new HashSet<int>(openingPairs.Select(p => p.ChildIndex));
                var annotationThicknesses = ThicknessAnnotationParser.AssignToSlabs(
                    xSlabs,
                    geometry.TextAnnotations.Select(a => (a.Text, a.X - cx, a.Y - cy)).ToList());
                allSlabsForStrips.AddRange(xSlabs);

                var pointMap = new Dictionary<(long, long, long), string>();
                int ptCounter = 0;

                string Pt(double xMm, double yMm)
                {
                    var key = ((long)Math.Round(xMm * 10), (long)Math.Round(yMm * 10), (long)Math.Round(elevationMm * 10));
                    if (!pointMap.TryGetValue(key, out string? name))
                    {
                        name = $"s{storyIndex + 1}_J{++ptCounter}";
                        pointMap[key] = name;
                        pointOrder.Add((name, key.Item1 / 10.0, key.Item2 / 10.0, key.Item3 / 10.0));
                    }
                    return name;
                }

                (string PropName, double ThicknessMm, string GradeCode) SlabPropertyFor((byte R, byte G, byte B) color, int slabIdx)
                {
                    string grade = GradeFor(color);
                    double thickness = PdfToSafeConstants.DefaultThicknessMm;
                    if (slabIdx >= 0 && slabIdx < annotationThicknesses.Length && annotationThicknesses[slabIdx].HasValue)
                        thickness = annotationThicknesses[slabIdx]!.Value;
                    else if (colorSettings != null && colorSettings.TryGetValue(color, out var s) && s.ThicknessMm > 0)
                        thickness = s.ThicknessMm;
                    var tStr = thickness.ToString("0.###", ic).Replace('.', '_');
                    return ($"S{tStr}{grade}", thickness, grade);
                }

                int areaCounter = 0;
                var slabIndexToAreaId = new Dictionary<int, string>();
                for (int i = 0; i < xSlabs.Count; i++)
                {
                    if (childIndices.Contains(i)) continue;
                    var pts = xSlabs[i];
                    var color = xSlabColors[i];
                    var prop = SlabPropertyFor(color, i);
                    string areaId = $"s{storyIndex + 1}_A{++areaCounter}";
                    slabIndexToAreaId[i] = areaId;
                    areas.Add((areaId, pts.Select(p => Pt(p.X, p.Y)).ToList(), pts, prop.PropName, prop.ThicknessMm, prop.GradeCode, color));
                }

                int lineCounter = 0;
                foreach (var pts in xLines)
                {
                    for (int i = 0; i < pts.Count - 1; i++)
                    {
                        string j1 = Pt(pts[i].X, pts[i].Y);
                        string j2 = Pt(pts[i + 1].X, pts[i + 1].Y);
                        if (j1 != j2)
                            lineSegs.Add(($"s{storyIndex + 1}_L{++lineCounter}", j1, j2, PolygonProcessor.Distance(pts[i], pts[i + 1])));
                    }
                }

                foreach (var (px, py) in xColumns)
                    columnPointNames.Add(Pt(px, py));

                foreach (var (parentSlabIdx, childSlabIdx) in openingPairs)
                {
                    if (!slabIndexToAreaId.TryGetValue(parentSlabIdx, out string? parentId)) continue;
                    var pts = xSlabs[childSlabIdx];
                    var names = pts.Select(p => Pt(p.X, p.Y)).ToList();
                    openingRows.Add(($"s{storyIndex + 1}_O{++openingCounter}", parentId, names));
                }
            }

            using var sw = new StreamWriter(outputPath, false, Encoding.ASCII);
            sw.WriteLine("$ Generated by Kor Operations - Structural PDF Import");
            sw.WriteLine();
            sw.WriteLine("TABLE:  \"PROGRAM CONTROL\"");
            sw.WriteLine("   ProgramName=SAFE   Version=23.0.0   CurrUnits=\"N, mm, C\"   MergeTol=1   ModelDatum=0");
            sw.WriteLine();

            var usedGrades = areas.Select(a => a.GradeCode).Distinct().OrderBy(g => g).ToList();
            if (usedGrades.Count == 0) usedGrades.Add(PdfToSafeConstants.DefaultConcreteGrade);

            sw.WriteLine("TABLE:  \"MATERIAL PROPERTIES - GENERAL\"");
            foreach (var g in usedGrades)
                sw.WriteLine($"   Material={g}   Type=Concrete   SymType=Isotropic   Grade={g}   Color=Blue");
            sw.WriteLine();
            sw.WriteLine("TABLE:  \"MATERIAL PROPERTIES - BASIC MECHANICAL PROPERTIES\"");
            foreach (var g in usedGrades)
            {
                var (e, gMod, _) = StructuralMaterialDatabase.GetGrade(g);
                sw.WriteLine($"   Material={g}   DensityType=Mass   UnitWeight=2.355E-05   UnitMass=2.4E-09   E1={e}   G12={gMod}   U12=0.17   A1=1E-05");
            }
            sw.WriteLine();
            sw.WriteLine("TABLE:  \"MATERIAL PROPERTIES - CONCRETE DATA\"");
            foreach (var g in usedGrades)
            {
                var (_, _, fc) = StructuralMaterialDatabase.GetGrade(g);
                sw.WriteLine($"   Material={g}   Fc={fc}   LtWtConc=No   IsUserFr=No   SSCurveOpt=Simple   SSHysType=Kinematic");
            }
            sw.WriteLine();

            var rebarSizes = StructuralMaterialDatabase.GetRebarSizes(settings.DesignCode);
            if (rebarSizes.Count > 0)
            {
                sw.WriteLine("TABLE:  \"REINFORCING BAR SIZES\"");
                foreach (var r in rebarSizes)
                    sw.WriteLine($"   Name={r.Name}   Diameter={r.DiameterMm.ToString("0.###", ic)}   Area={r.AreaMm2.ToString("0.###", ic)}");
                sw.WriteLine();
            }

            var uniqueSlabProps = areas
                .Select(a => (a.PropName, a.ThicknessMm, Grade: a.GradeCode))
                .Distinct()
                .OrderBy(p => p.PropName)
                .ToList();

            bool hasSdlLoads = areas.Any(a =>
                colorSettings != null &&
                colorSettings.TryGetValue(a.Color, out var s) &&
                s.SdlKPa > 0);
            bool hasLiveLoads = areas.Any(a =>
                colorSettings != null &&
                colorSettings.TryGetValue(a.Color, out var s) &&
                s.LiveKPa > 0);

            sw.WriteLine("TABLE:  \"LOAD PATTERN DEFINITIONS\"");
            sw.WriteLine("   Name=DEAD   Type=Dead   SelfWtMult=1");
            if (hasSdlLoads) sw.WriteLine("   Name=SDL   Type=SuperDead   SelfWtMult=0");
            if (hasLiveLoads) sw.WriteLine("   Name=LIVE   Type=Live   SelfWtMult=0");
            if (settings.IncludePtLoads) sw.WriteLine("   Name=LBALC   Type=Other   SelfWtMult=0");
            sw.WriteLine();

            if (!string.IsNullOrEmpty(settings.LoadCombCode))
            {
                var combos = StructuralMaterialDatabase.BuildLoadCombinations(settings.LoadCombCode, hasSdlLoads, hasLiveLoads, settings.IncludePtLoads);
                if (combos.Count > 0)
                {
                    sw.WriteLine("TABLE:  \"LOAD COMBINATION DEFINITIONS\"");
                    foreach (var (name, _) in combos)
                        sw.WriteLine($"   Name={name}   Type=LinearAdd");
                    sw.WriteLine();

                    sw.WriteLine("TABLE:  \"LOAD COMBINATION CASES\"");
                    foreach (var (name, cases) in combos)
                        foreach (var (pat, sf) in cases)
                            sw.WriteLine($"   Name={name}   LoadPat={pat}   SF={sf.ToString("0.##", ic)}");
                    sw.WriteLine();
                }
            }

            sw.WriteLine("TABLE:  \"SLAB PROPERTY DEFINITIONS\"");
            foreach (var (propName, thicknessMm, grade) in uniqueSlabProps)
            {
                sw.WriteLine($"   Name={propName}   \"Modeling Type\"=Shell-Thick   \"Property Type\"=Slab   Material={grade}   \"Slab Thickness\"={thicknessMm.ToString("0.###", ic)} _");
                sw.WriteLine("        \"f11 Modifier\"=1   \"f22 Modifier\"=1   \"f12 Modifier\"=1 _");
                sw.WriteLine("        \"m11 Modifier\"=1   \"m22 Modifier\"=1   \"m12 Modifier\"=1 _");
                sw.WriteLine("        \"v13 Modifier\"=1   \"v23 Modifier\"=1 _");
                sw.WriteLine("        \"Mass Modifier\"=1   \"Weight Modifier\"=1   Orthotropic?=No");
            }
            sw.WriteLine();

            sw.WriteLine("TABLE:  \"POINT OBJECT CONNECTIVITY\"");
            foreach (var (name, xMm, yMm, zMm) in pointOrder)
                sw.WriteLine($"   UniqueName={name}   \"Is Auto Point\"=No   IsSpecial=No   X={xMm.ToString("F1", ic)}   Y={yMm.ToString("F1", ic)}   Z={zMm.ToString("0.###", ic)}");
            sw.WriteLine();

            if (areas.Count > 0)
            {
                sw.WriteLine("TABLE:  \"FLOOR OBJECT CONNECTIVITY\"");
                foreach (var (id, ptNames, coords, _, _, _, _) in areas)
                {
                    double perim = 0;
                    double area2 = 0;
                    for (int j = 0; j < coords.Count; j++)
                    {
                        var a = coords[j];
                        var b = coords[(j + 1) % coords.Count];
                        perim += Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                        area2 += a.X * b.Y - b.X * a.Y;
                    }
                    double areaVal = Math.Abs(area2) / 2.0;

                    var areaLine = new StringBuilder();
                    areaLine.Append($"   \"Unique Name\"={id}");
                    for (int j = 0; j < ptNames.Count; j++)
                        areaLine.Append($"   UniquePt{j + 1}={ptNames[j]}");
                    areaLine.Append($"   Perimeter={perim.ToString("F4", ic)}   Area={areaVal.ToString("F4", ic)}   GUID={Guid.NewGuid():D}");
                    sw.WriteLine(areaLine.ToString());
                }
                sw.WriteLine();

                if (settings.MeshSizeMm > 0)
                {
                    sw.WriteLine("TABLE:  \"FLOOR AUTO MESH OPTIONS\"");
                    foreach (var (id, _, _, _, _, _, _) in areas)
                        sw.WriteLine($"   UniqueName={id}   MeshType=Generalized   MaxMeshSize={settings.MeshSizeMm.ToString("0.###", ic)}");
                    sw.WriteLine();
                }

                if (openingRows.Count > 0)
                {
                    sw.WriteLine("TABLE:  \"FLOOR OBJECT OPENINGS\"");
                    foreach (var (oid, parentId, ptNames) in openingRows)
                    {
                        var sb = new StringBuilder();
                        sb.Append($"   UniqueName={oid}   FloorObject={parentId}");
                        for (int j = 0; j < ptNames.Count; j++)
                            sb.Append($"   UniquePt{j + 1}={ptNames[j]}");
                        sw.WriteLine(sb.ToString());
                    }
                    sw.WriteLine();
                }

                sw.WriteLine("TABLE:  \"AREA ASSIGNMENTS - SECTION PROPERTIES\"");
                foreach (var (id, _, _, propName, _, _, _) in areas)
                    sw.WriteLine($"   UniqueName={id}   \"Section Property\"={propName}   \"Property Type\"=Slab");
                sw.WriteLine();
            }

            if (columnPointNames.Count > 0)
            {
                sw.WriteLine("TABLE:  \"JOINT ASSIGNMENTS - RESTRAINTS\"");
                foreach (var pointName in columnPointNames)
                    sw.WriteLine($"   UniqueName={pointName}   U1=Yes   U2=Yes   U3=Yes   R1=No   R2=No   R3=No");
                sw.WriteLine();
            }

            var areaLoads = new List<(string AreaId, string Pattern, double Value)>();
            foreach (var (id, _, _, _, _, _, color) in areas)
            {
                if (colorSettings == null || !colorSettings.TryGetValue(color, out var colorCfg))
                    continue;

                double sdlValue = colorCfg.SdlKPa * 0.001;
                double liveValue = colorCfg.LiveKPa * 0.001;

                if (sdlValue > 0) areaLoads.Add((id, "SDL", sdlValue));
                if (liveValue > 0) areaLoads.Add((id, "LIVE", liveValue));
            }

            if (areaLoads.Count > 0)
            {
                sw.WriteLine("TABLE:  \"AREA LOAD ASSIGNMENTS - UNIFORM\"");
                foreach (var (areaId, pattern, value) in areaLoads)
                    sw.WriteLine($"   UniqueName={areaId}   LoadPat={pattern}   Dir=Gravity   Value={value.ToString("0.###", ic)}");
                sw.WriteLine();
            }

            if (lineSegs.Count > 0)
            {
                sw.WriteLine("TABLE:  \"NULL LINE OBJECT CONNECTIVITY\"");
                foreach (var (id, j1, j2, lenMm) in lineSegs)
                    sw.WriteLine($"   \"Unique Name\"={id}   UniquePtI={j1}   UniquePtJ={j2}   Length={lenMm.ToString("F4", ic)}   GUID={Guid.NewGuid():D}");
                sw.WriteLine();
            }

            if (settings.AutoGenerateStrips && allSlabsForStrips.Count > 0)
            {
                var strips = DesignStripGenerator.Generate(
                    allSlabsForStrips, settings.StripSpacingMm, settings.StripAAlongX);
                WriteDesignStrips(sw, strips, ic);
            }

            sw.WriteLine("END TABLE DATA");
        }

        // Targets SAFE v23 F2K format. Direction=A = strip runs along X-axis;
        // Direction=B = strip runs along Y-axis.
        private static void WriteDesignStrips(
            System.IO.StreamWriter sw,
            IReadOnlyList<DesignStrip> strips,
            System.Globalization.CultureInfo ic)
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
    }
}
