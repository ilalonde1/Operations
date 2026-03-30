#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

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
            IReadOnlyList<(string Id, string J1, string J2, double LenMm, string? SecName)> lineSegs,
            CultureInfo ic)
        {
            var secToSegments = new Dictionary<string, (double W, double D, List<string> SegIds)>();
            foreach (var (id, _, _, _, secName) in lineSegs)
            {
                if (secName is null) continue;
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

        internal static void WriteBeamSections(
            StreamWriter sw,
            IReadOnlyList<(string Id, string J1, string J2, double LenMm, int LineIdx)> lineSegs,
            string?[] lineSecNames,
            CultureInfo ic)
        {
            var embedded = new List<(string Id, string J1, string J2, double LenMm, string? SecName)>(lineSegs.Count);
            foreach (var (id, j1, j2, lenMm, lineIdx) in lineSegs)
            {
                string? secName = lineIdx >= 0 && lineIdx < lineSecNames.Length ? lineSecNames[lineIdx] : null;
                embedded.Add((id, j1, j2, lenMm, secName));
            }
            WriteBeamSections(sw, embedded, ic);
        }

        internal static void WriteTables(
            StreamWriter sw,
            IReadOnlyList<F2kStoryData> stories,
            Dictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings,
            ExportSettings settings,
            IReadOnlyList<StructuralGridLine> gridLines,
            CultureInfo ic)
        {
            var allAreas = stories.SelectMany(s => s.Areas).ToList();
            var allDropAreas = stories.SelectMany(s => s.DropAreas).ToList();
            var allLineSegs = stories.SelectMany(s => s.LineSegs).ToList();
            var allColPtNames = stories.SelectMany(s => s.ColumnPointNames).ToList();
            var allColSections = stories.SelectMany(s => s.ColumnSections).ToList();
            var allOpeningRows = stories.SelectMany(s => s.OpeningRows).ToList();
            var allSlabsForStrips = stories.SelectMany(s => s.SlabsForStrips).ToList();
            var allPointOrder = stories.SelectMany(s => s.PointOrder).ToList();

            sw.WriteLine("$ Generated by Kor Operations - Structural PDF Import");
            sw.WriteLine();
            sw.WriteLine("TABLE:  \"PROGRAM CONTROL\"");
            sw.WriteLine("   ProgramName=SAFE   Version=23.0.0   CurrUnits=\"N, mm, C\"   MergeTol=1   ModelDatum=0");
            sw.WriteLine();

            WriteGridLines(sw, gridLines, ic);

            var usedGrades = allAreas.Select(a => a.GradeCode).Concat(allDropAreas.Select(a => a.GradeCode))
                .Distinct().OrderBy(g => g).ToList();
            if (usedGrades.Count == 0) usedGrades.Add(PdfToSafeConstants.DefaultConcreteGrade);

            sw.WriteLine("TABLE:  \"MATERIAL PROPERTIES - GENERAL\"");
            foreach (var g in usedGrades)
                sw.WriteLine($"   Material={g}   Type=Concrete   SymType=Isotropic   Grade={g}   Color=Blue");
            sw.WriteLine();
            sw.WriteLine("TABLE:  \"MATERIAL PROPERTIES - BASIC MECHANICAL PROPERTIES\"");
            foreach (var g in usedGrades)
            {
                var (e, gMod, _) = StructuralMaterialDatabase.GetGrade(g, settings.DesignCode);
                sw.WriteLine($"   Material={g}   DensityType=Mass   UnitWeight=2.4E-05   UnitMass=2.4E-09   E1={e}   G12={gMod}   U12=0.2   A1=1E-05");
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

            var uniqueSlabProps = allAreas
                .Select(a => (a.PropName, a.ThicknessMm, Grade: a.GradeCode))
                .Concat(allDropAreas.Select(a => (a.PropName, a.ThicknessMm, Grade: a.GradeCode)))
                .Distinct()
                .OrderBy(p => p.PropName)
                .ToList();

            sw.WriteLine("TABLE:  \"SLAB PROPERTY DEFINITIONS\"");
            foreach (var (propName, thicknessMm, grade) in uniqueSlabProps)
            {
                sw.WriteLine($"   Name={propName}   \"Modeling Type\"=Shell-Thick   \"Property Type\"=Slab   Material={grade}   \"Slab Thickness\"={thicknessMm.ToString("0.###", ic)} _");
                sw.WriteLine($"        \"f11 Modifier\"={settings.SlabMembraneModifier.ToString("0.###", ic)}   \"f22 Modifier\"={settings.SlabMembraneModifier.ToString("0.###", ic)}   \"f12 Modifier\"={settings.SlabMembraneModifier.ToString("0.###", ic)} _");
                sw.WriteLine($"        \"m11 Modifier\"={settings.SlabBendingModifier.ToString("0.###", ic)}   \"m22 Modifier\"={settings.SlabBendingModifier.ToString("0.###", ic)}   \"m12 Modifier\"={settings.SlabBendingModifier.ToString("0.###", ic)} _");
                sw.WriteLine($"        \"v13 Modifier\"={settings.SlabShearModifier.ToString("0.###", ic)}   \"v23 Modifier\"={settings.SlabShearModifier.ToString("0.###", ic)} _");
                sw.WriteLine("        \"Mass Modifier\"=1   \"Weight Modifier\"=1   Orthotropic?=No");
            }
            sw.WriteLine();

            bool hasSdlLoads = allAreas.Any(a =>
                colorSettings != null &&
                colorSettings.TryGetValue(a.Color, out var s) &&
                s.SdlKPa > 0);
            bool hasLiveLoads = allAreas.Any(a =>
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

            sw.WriteLine("TABLE:  \"POINT OBJECT CONNECTIVITY\"");
            foreach (var (name, xMm, yMm, zMm) in allPointOrder)
                sw.WriteLine($"   UniqueName={name}   \"Is Auto Point\"=No   IsSpecial=No   X={xMm.ToString("F1", ic)}   Y={yMm.ToString("F1", ic)}   Z={zMm.ToString("0.###", ic)}");
            sw.WriteLine();

            WriteColumnSections(sw, allColPtNames, allColSections, ic);

            if (allAreas.Count > 0)
            {
                sw.WriteLine("TABLE:  \"FLOOR OBJECT CONNECTIVITY\"");
                foreach (var (id, ptNames, coords, _, _, _, _) in allAreas)
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
                foreach (var (id, ptNames, coords, _, _, _) in allDropAreas)
                {
                    double perim = 0, area2 = 0;
                    for (int j = 0; j < coords.Count; j++)
                    {
                        var a = coords[j]; var b = coords[(j + 1) % coords.Count];
                        perim += Math.Sqrt((b.X-a.X)*(b.X-a.X)+(b.Y-a.Y)*(b.Y-a.Y));
                        area2 += a.X*b.Y - b.X*a.Y;
                    }
                    double areaVal = Math.Abs(area2) / 2.0;
                    var sb = new StringBuilder();
                    sb.Append($"   \"Unique Name\"={id}");
                    for (int j = 0; j < ptNames.Count; j++)
                        sb.Append($"   UniquePt{j+1}={ptNames[j]}");
                    sb.Append($"   Perimeter={perim.ToString("F4",ic)}   Area={areaVal.ToString("F4",ic)}   GUID={Guid.NewGuid():D}");
                    sw.WriteLine(sb.ToString());
                }
                sw.WriteLine();

                if (settings.MeshSizeMm > 0)
                {
                    sw.WriteLine("TABLE:  \"FLOOR AUTO MESH OPTIONS\"");
                    foreach (var (id, _, _, _, _, _, _) in allAreas)
                        sw.WriteLine($"   UniqueName={id}   MeshType=Generalized   MaxMeshSize={settings.MeshSizeMm.ToString("0.###", ic)}");
                    sw.WriteLine();
                }

                if (allOpeningRows.Count > 0)
                {
                    sw.WriteLine("TABLE:  \"FLOOR OBJECT OPENINGS\"");
                    foreach (var (oid, parentId, ptNames) in allOpeningRows)
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
                foreach (var (id, _, _, propName, _, _, _) in allAreas)
                    sw.WriteLine($"   UniqueName={id}   \"Section Property\"={propName}   \"Property Type\"=Slab");
                foreach (var (id, _, _, propName, _, _) in allDropAreas)
                    sw.WriteLine($"   UniqueName={id}   \"Section Property\"={propName}   \"Property Type\"=Slab");
                sw.WriteLine();

                if (allColPtNames.Count > 0)
                {
                    sw.WriteLine("TABLE:  \"JOINT ASSIGNMENTS - RESTRAINTS\"");
                    foreach (var pointName in allColPtNames)
                        sw.WriteLine($"   UniqueName={pointName}   U1=Yes   U2=Yes   U3=Yes   R1=No   R2=No   R3=No");
                    sw.WriteLine();
                }

                var areaLoads = new List<(string AreaId, string Pattern, double Value)>();
                foreach (var (id, _, _, _, _, _, color) in allAreas)
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

            if (allLineSegs.Count > 0)
            {
                sw.WriteLine("TABLE:  \"LINE OBJECT CONNECTIVITY\"");
                foreach (var (id, j1, j2, lenMm, _) in allLineSegs)
                    sw.WriteLine($"   \"Unique Name\"={id}   UniquePtI={j1}   UniquePtJ={j2}" +
                        $"   Length={lenMm.ToString("F4", ic)}   GUID={Guid.NewGuid():D}");
                sw.WriteLine();

                WriteBeamSections(sw, allLineSegs, ic);
            }

            if (settings.AutoGenerateStrips && allSlabsForStrips.Count > 0)
            {
                var strips = DesignStripGenerator.Generate(allSlabsForStrips,
                    settings.StripSpacingMm, settings.StripAAlongX);
                WriteDesignStrips(sw, strips, ic);
            }

            sw.WriteLine("END TABLE DATA");
        }
    }
}
