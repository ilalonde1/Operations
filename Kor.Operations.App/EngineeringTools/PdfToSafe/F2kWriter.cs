#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// Emits an F2K file compatible with SAFE 22.7 (the version the firm runs).
    /// Table names, required sections, and combination format match the schema
    /// diffed from two real SAFE-exported reference files on 2026-04-14.
    /// </summary>
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
            // GRID DEFINITIONS - GENERAL defines the coordinate system;
            // individual grid lines are emitted in GRID LINES.
            sw.WriteLine("TABLE:  \"GRID DEFINITIONS - GENERAL\"");
            sw.WriteLine("   Name=GLOBAL   Type=Cartesian   Ux=0   Uy=0   Rz=0   \"Model Datum\"=0   \"Bubble Size\"=1500   Color=Gray8Dark");
            sw.WriteLine();

            if (gridLines.Count == 0) return;

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

        /// <summary>
        /// Columns are modelled as point-below restraints: a point with a SAFE
        /// "Column Below" section reference plus a fixed joint restraint. This
        /// is the legacy SAFE convention and still loads cleanly in 22.7.
        /// </summary>
        internal static void WriteColumnSections(
            StreamWriter sw,
            IReadOnlyList<string> columnPointNames,
            IReadOnlyList<(string SecName, double W, double D)> sections,
            CultureInfo ic)
        {
            if (columnPointNames.Count == 0) return;

            var uniqueSecs = sections
                .GroupBy(s => s.SecName)
                .Select(g => g.First())
                .OrderBy(s => s.SecName)
                .ToList();

            // Emit column sections as frame sections (SAFE 22+ vocabulary).
            WriteFrameSectionTables(sw, uniqueSecs, "Column", ic);

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
                string numericPart = secName;
                int numStart = 0;
                while (numStart < numericPart.Length && !char.IsDigit(numericPart[numStart]) && numericPart[numStart] != '-')
                    numStart++;
                if (numStart > 0) numericPart = numericPart.Substring(numStart);

                var parts = numericPart.Split('x');
                if (parts.Length != 2 ||
                    !double.TryParse(parts[0], out double w) ||
                    !double.TryParse(parts[1], out double d)) continue;
                if (!secToSegments.ContainsKey(secName))
                    secToSegments[secName] = (w, d, new List<string>());
                secToSegments[secName].SegIds.Add(id);
            }

            if (secToSegments.Count == 0) return;

            var uniqueSecs = secToSegments
                .OrderBy(kv => kv.Key)
                .Select(kv => (kv.Key, kv.Value.W, kv.Value.D))
                .ToList();

            WriteFrameSectionTables(sw, uniqueSecs, "Beam", ic);

            sw.WriteLine("TABLE:  \"FRAME ASSIGNMENTS - SECTION PROPERTIES\"");
            foreach (var (name, (_, _, segIds)) in secToSegments.OrderBy(kv => kv.Key))
                foreach (var segId in segIds)
                    sw.WriteLine($"   UniqueName={segId}   Shape=\"Concrete Rectangular\"   \"Auto Select List\"=N.A.   \"Section Property\"={name}");
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

        /// <summary>
        /// Emits both FRAME SECTION PROPERTY DEFINITIONS - SUMMARY (rich auto-
        /// computed properties SAFE expects) and - CONCRETE RECTANGULAR (the
        /// canonical column/beam section). The SUMMARY values are derived from
        /// width×depth using standard rectangular-section formulas.
        /// </summary>
        private static void WriteFrameSectionTables(
            StreamWriter sw,
            IReadOnlyList<(string Name, double W, double D)> sections,
            string sectionKind,
            CultureInfo ic)
        {
            if (sections.Count == 0) return;

            sw.WriteLine("TABLE:  \"FRAME SECTION PROPERTY DEFINITIONS - SUMMARY\"");
            foreach (var (name, w, d) in sections)
            {
                double area = w * d;
                double i33 = w * d * d * d / 12.0;       // about strong axis
                double i22 = d * w * w * w / 12.0;       // about weak axis
                double j = Math.Min(w, d) * Math.Min(w, d) * Math.Min(w, d) * Math.Max(w, d) / 3.0 * 0.333;
                double s33 = i33 / (d / 2.0);
                double s22 = i22 / (w / 2.0);
                double z33 = w * d * d / 4.0;
                double z22 = d * w * w / 4.0;
                double r33 = Math.Sqrt(i33 / area);
                double r22 = Math.Sqrt(i22 / area);
                double as2 = area * 5.0 / 6.0;

                sw.WriteLine(
                    $"   Name={name}   Material={PdfToSafeConstants.DefaultConcreteGrade}   Shape=\"Concrete Rectangular\"   Color=Blue" +
                    $"   Area={area.ToString("0.###", ic)}   J={j.ToString("0.###", ic)}   I33={i33.ToString("0.###", ic)}   I22={i22.ToString("0.###", ic)}   I23=0" +
                    $"   As2={as2.ToString("0.###", ic)}   As3={as2.ToString("0.###", ic)}" +
                    $"   S33Pos={s33.ToString("0.###", ic)}   S33Neg={s33.ToString("0.###", ic)}   S22Pos={s22.ToString("0.###", ic)}   S22Neg={s22.ToString("0.###", ic)}" +
                    $"   Z33={z33.ToString("0.###", ic)}   Z22={z22.ToString("0.###", ic)}   R33={r33.ToString("0.###", ic)}   R22={r22.ToString("0.###", ic)}" +
                    $"   \"Area Modifier\"=1   \"As2 Modifier\"=1   \"As3 Modifier\"=1   \"J Modifier\"=1   \"I33 Modifier\"=1   \"I22 Modifier\"=1   \"Mass Modifier\"=1   \"Weight Modifier\"=1");
            }
            sw.WriteLine();

            sw.WriteLine("TABLE:  \"FRAME SECTION PROPERTY DEFINITIONS - CONCRETE RECTANGULAR\"");
            foreach (var (name, w, d) in sections)
            {
                sw.WriteLine(
                    $"   Name={name}   Material={PdfToSafeConstants.DefaultConcreteGrade}   \"From File?\"=No   Depth={d.ToString("0.###", ic)}   Width={w.ToString("0.###", ic)}" +
                    $"   \"Rigid Zone?\"=No   \"Column Drop Panel?\"=No   \"Include Column Capital?\"=No   \"Notional Size Type\"=Auto   \"Notional Auto Factor\"=1" +
                    $"   \"Area Modifier\"=1   \"As2 Modifier\"=1   \"As3 Modifier\"=1   \"J Modifier\"=1   \"I22 Modifier\"=1   \"I33 Modifier\"=1   \"Mass Modifier\"=1   \"Weight Modifier\"=1   Color=Blue");
            }
            sw.WriteLine();
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
            sw.WriteLine("   ProgramName=SAFE   Version=22.7.0   ProgLevel=Standard   CurrUnits=\"N, mm, C\"   ConcFrmCode=\"CSA A23.3-19\"   ConcSlbCode=\"CSA A23.3-19\"   MergeTol=1");
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
                sw.WriteLine($"   Material={g}   DensityType=Mass   UnitWeight=2.4E-05   UnitMass=2.4E-09   E1={e.ToString("F2", ic)}   G12={gMod.ToString("F2", ic)}   U12=0.2   A1=1E-05");
            }
            sw.WriteLine();
            sw.WriteLine("TABLE:  \"MATERIAL PROPERTIES - CONCRETE DATA\"");
            foreach (var g in usedGrades)
            {
                var (_, _, fc) = StructuralMaterialDatabase.GetGrade(g);
                sw.WriteLine($"   Material={g}   Fc={fc.ToString("F2", ic)}   LtWtConc=No   IsUserFr=No   SSCurveOpt=Simple   SSHysType=Kinematic   SFc=0.00225   SCap=0.005   FinalSlope=-0.1   FAngle=0   DAngle=0");
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

            if (uniqueSlabProps.Count > 0)
            {
                sw.WriteLine("TABLE:  \"AREA SECTION PROPERTY DEFINITIONS - SUMMARY\"");
                foreach (var (propName, thicknessMm, grade) in uniqueSlabProps)
                    sw.WriteLine($"   Name={propName}   Type=Slab   \"Element Type\"=Shell-Thick   Material={grade}   \"Total Thickness\"={thicknessMm.ToString("0.###", ic)}");
                sw.WriteLine();

                sw.WriteLine("TABLE:  \"SLAB PROPERTY DEFINITIONS\"");
                foreach (var (propName, thicknessMm, grade) in uniqueSlabProps)
                {
                    sw.WriteLine($"   Name={propName}   \"Modeling Type\"=Shell-Thick   \"Property Type\"=Slab   Material={grade}   \"Slab Thickness\"={thicknessMm.ToString("0.###", ic)}   \"Notional Size Type\"=Auto   \"Notional Auto Factor\"=1 _");
                    sw.WriteLine($"        \"f11 Modifier\"={settings.SlabMembraneModifier.ToString("0.###", ic)}   \"f22 Modifier\"={settings.SlabMembraneModifier.ToString("0.###", ic)}   \"f12 Modifier\"={settings.SlabMembraneModifier.ToString("0.###", ic)} _");
                    sw.WriteLine($"        \"m11 Modifier\"={settings.SlabBendingModifier.ToString("0.###", ic)}   \"m22 Modifier\"={settings.SlabBendingModifier.ToString("0.###", ic)}   \"m12 Modifier\"={settings.SlabBendingModifier.ToString("0.###", ic)} _");
                    sw.WriteLine($"        \"v13 Modifier\"={settings.SlabShearModifier.ToString("0.###", ic)}   \"v23 Modifier\"={settings.SlabShearModifier.ToString("0.###", ic)} _");
                    sw.WriteLine("        \"Mass Modifier\"=1   \"Weight Modifier\"=1   Color=Blue   Orthotropic?=No");
                }
                sw.WriteLine();
            }

            bool hasSdlLoads = allAreas.Any(a =>
                colorSettings != null &&
                colorSettings.TryGetValue(a.Color, out var s) &&
                s.SdlKPa > 0);
            bool hasLiveLoads = allAreas.Any(a =>
                colorSettings != null &&
                colorSettings.TryGetValue(a.Color, out var s) &&
                s.LiveKPa > 0);

            var loadPatterns = new List<(string Name, string Type, double SelfWt)>();
            loadPatterns.Add(("DEAD", "Dead", 1));
            if (hasSdlLoads) loadPatterns.Add(("SDL", "Dead", 0));
            if (hasLiveLoads) loadPatterns.Add(("LIVE", "Live", 0));
            if (settings.IncludePtLoads) loadPatterns.Add(("LBALC", "Other", 0));

            sw.WriteLine("TABLE:  \"LOAD PATTERN DEFINITIONS\"");
            foreach (var (name, type, sw_) in loadPatterns)
                sw.WriteLine($"   Name={name}   \"Is Auto Load\"=No   Type={type}   \"Self Weight Multiplier\"={sw_.ToString("0.##", ic)}");
            sw.WriteLine();

            // SAFE 22 requires a MASS SOURCE DEFINITION + load cases (one per pattern)
            // or the pattern is never activated during import.
            sw.WriteLine("TABLE:  \"MASS SOURCE DEFINITION\"");
            sw.WriteLine("   Name=MsSrc1   \"Is Default\"=Yes   \"Include Lateral Mass?\"=Yes   \"Include Vertical Mass?\"=Yes   \"Lump Mass?\"=Yes   \"Source Self Mass?\"=Yes   \"Source Added Mass?\"=Yes   \"Source Load Patterns?\"=No");
            sw.WriteLine();

            sw.WriteLine("TABLE:  \"LOAD CASE DEFINITIONS - SUMMARY\"");
            foreach (var (name, _, _) in loadPatterns)
                sw.WriteLine($"   Name={name}   Type=\"Linear Static\"");
            sw.WriteLine();

            sw.WriteLine("TABLE:  \"LOAD CASE DEFINITIONS - LINEAR STATIC\"");
            foreach (var (name, _, _) in loadPatterns)
                sw.WriteLine($"   Name={name}   \"Exclude Group\"=None   \"Mass Source\"=MsSrc1   \"Initial Condition\"=Unstressed   \"Load Type\"=Load   \"Load Name\"={name}   \"Load SF\"=1   \"Design Type\"=\"Program Determined\"");
            sw.WriteLine();

            if (!string.IsNullOrEmpty(settings.LoadCombCode))
            {
                var combos = StructuralMaterialDatabase.BuildLoadCombinations(settings.LoadCombCode, hasSdlLoads, hasLiveLoads, settings.IncludePtLoads);
                if (combos.Count > 0)
                {
                    // SAFE 22 folds combination cases into the definition table
                    // via repeated rows rather than a separate table.
                    sw.WriteLine("TABLE:  \"LOAD COMBINATION DEFINITIONS\"");
                    foreach (var (name, cases) in combos)
                    {
                        bool first = true;
                        foreach (var (pat, sf) in cases)
                        {
                            if (first)
                            {
                                sw.WriteLine($"   Name={name}   Type=\"Linear Add\"   \"Is Auto\"=No   \"Load Name\"={pat}   SF={sf.ToString("0.##", ic)}");
                                first = false;
                            }
                            else
                            {
                                sw.WriteLine($"   Name={name}   \"Load Name\"={pat}   SF={sf.ToString("0.##", ic)}");
                            }
                        }
                    }
                    sw.WriteLine();
                }
            }

            sw.WriteLine("TABLE:  \"POINT OBJECT CONNECTIVITY\"");
            foreach (var (name, xMm, yMm, zMm) in allPointOrder)
                sw.WriteLine($"   UniqueName={name}   \"Is Auto Point\"=No   IsSpecial=No   X={xMm.ToString("F1", ic)}   Y={yMm.ToString("F1", ic)}   Z={zMm.ToString("F1", ic)}");
            sw.WriteLine();

            // Beam / wall-as-beam objects (SAFE 22 uses "BEAM OBJECT CONNECTIVITY").
            if (allLineSegs.Count > 0)
            {
                sw.WriteLine("TABLE:  \"BEAM OBJECT CONNECTIVITY\"");
                foreach (var (id, j1, j2, lenMm, _) in allLineSegs)
                    sw.WriteLine($"   \"Unique Name\"={id}   UniquePtI={j1}   UniquePtJ={j2}   Length={lenMm.ToString("0.###", ic)}");
                sw.WriteLine();

                WriteBeamSections(sw, allLineSegs, ic);
            }

            WriteColumnSections(sw, allColPtNames, allColSections, ic);

            // ── Floor objects (slabs) ──────────────────────────────────────
            if (allAreas.Count > 0)
            {
                sw.WriteLine("TABLE:  \"FLOOR OBJECT CONNECTIVITY\"");
                foreach (var (id, ptNames, coords, _, _, _, _) in allAreas)
                    WriteFloorObjectRows(sw, id, ptNames, coords, ic);
                foreach (var (id, ptNames, coords, _, _, _) in allDropAreas)
                    WriteFloorObjectRows(sw, id, ptNames, coords, ic);
                sw.WriteLine();

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

                sw.WriteLine("TABLE:  \"AREA ASSIGNMENTS - SUMMARY\"");
                foreach (var (id, _, _, propName, _, _, _) in allAreas)
                    sw.WriteLine($"   UniqueName={id}   \"Section Property\"={propName}   \"Property Type\"=Slab");
                foreach (var (id, _, _, propName, _, _) in allDropAreas)
                    sw.WriteLine($"   UniqueName={id}   \"Section Property\"={propName}   \"Property Type\"=Slab");
                sw.WriteLine();

                sw.WriteLine("TABLE:  \"AREA ASSIGNMENTS - SECTION PROPERTIES\"");
                foreach (var (id, _, _, propName, _, _, _) in allAreas)
                    sw.WriteLine($"   UniqueName={id}   \"Section Property\"={propName}   \"Property Type\"=Slab");
                foreach (var (id, _, _, propName, _, _) in allDropAreas)
                    sw.WriteLine($"   UniqueName={id}   \"Section Property\"={propName}   \"Property Type\"=Slab");
                sw.WriteLine();

                sw.WriteLine("TABLE:  \"AREA ASSIGNMENTS - INSERTION POINT\"");
                foreach (var (id, _, _, _, _, _, _) in allAreas)
                    sw.WriteLine($"   UniqueName={id}   \"Cardinal Point\"=Top");
                foreach (var (id, _, _, _, _, _) in allDropAreas)
                    sw.WriteLine($"   UniqueName={id}   \"Cardinal Point\"=Top");
                sw.WriteLine();

                if (settings.MeshSizeMm > 0)
                {
                    sw.WriteLine("TABLE:  \"AREA ASSIGNMENTS - FLOOR AUTO MESH OPTIONS\"");
                    foreach (var (id, _, _, _, _, _, _) in allAreas)
                        sw.WriteLine($"   UniqueName={id}   \"Mesh Option\"=Default   \"Add Restraints\"=No");
                    sw.WriteLine();
                }

                sw.WriteLine("TABLE:  \"AREA ASSIGNMENTS - AUTO EDGE CONSTRAINTS\"");
                foreach (var (id, _, _, _, _, _, _) in allAreas)
                    sw.WriteLine($"   UniqueName={id}   Constraint=Yes");
                sw.WriteLine();

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
                    sw.WriteLine("TABLE:  \"AREA LOADS ASSIGNMENTS - UNIFORM\"");
                    foreach (var (areaId, pattern, value) in areaLoads)
                        sw.WriteLine($"   UniqueName={areaId}   \"Load Pattern\"={pattern}   \"Coord System\"=GLOBAL   Direction=Gravity   Value={value.ToString("0.######", ic)}");
                    sw.WriteLine();
                }
            }

            // ── Column restraints (independent of slabs) ─────────────────
            if (allColPtNames.Count > 0)
            {
                sw.WriteLine("TABLE:  \"JOINT ASSIGNMENTS - RESTRAINTS\"");
                foreach (var pointName in allColPtNames)
                    sw.WriteLine($"   UniqueName={pointName}   UX=Yes   UY=Yes   UZ=Yes   RX=Yes   RY=Yes   RZ=Yes");
                sw.WriteLine();
            }

            if (settings.AutoGenerateStrips && allSlabsForStrips.Count > 0)
            {
                var strips = DesignStripGenerator.Generate(allSlabsForStrips,
                    settings.StripSpacingMm, settings.StripAAlongX);
                WriteDesignStrips(sw, strips, ic);
            }

            // Minimal analysis-options block so SAFE can run the imported model
            // without the user having to open every dialog and click OK.
            sw.WriteLine("TABLE:  \"ANALYSIS MODELING OPTIONS\"");
            sw.WriteLine("   \"Two Dimensional Only\"=No   \"Rigid Diaphragm At Top\"=No   \"Ignore Vertical Offsets\"=Yes");
            sw.WriteLine();
            sw.WriteLine("TABLE:  \"ANALYSIS OPTIONS - SAPFIRE OPTIONS\"");
            sw.WriteLine("   \"Solver Option\"=Advanced   \"Analysis Process\"=Auto   \"Number Analysis Threads\"=0   \"Max File Size\"=0");
            sw.WriteLine();
            sw.WriteLine("TABLE:  \"ANALYSIS OPTIONS - AUTOMATIC MESH SETTINGS FOR FLOORS\"");
            sw.WriteLine($"   \"Mesh Option\"=Rectangular   \"Use Localized Meshing\"=Yes   \"Merge Joints\"=Yes   \"Maximum Mesh Size\"={Math.Max(1, settings.MeshSizeMm).ToString("0.###", ic)}");
            sw.WriteLine();
            sw.WriteLine("TABLE:  \"ANALYSIS OPTIONS - AUTOMATIC RECTANGULAR MESH OPTIONS FOR WALLS\"");
            sw.WriteLine("   \"Maximum Mesh Size\"=1200");
            sw.WriteLine();
            sw.WriteLine("TABLE:  \"PREFERENCES - TOLERANCE\"");
            sw.WriteLine("   MergeTol=1   MaxInclVertCol=1   MaxInclHorzBmFlr=20");
            sw.WriteLine();

            sw.WriteLine("END TABLE DATA");
        }

        /// <summary>
        /// SAFE limits a FLOOR OBJECT CONNECTIVITY row to 4 point refs. Polygons
        /// with more vertices must be split across multiple rows with Order=1,
        /// 2, … — Perimeter/Area appear only on the first row.
        /// </summary>
        private static void WriteFloorObjectRows(
            StreamWriter sw,
            string id,
            IReadOnlyList<string> ptNames,
            IReadOnlyList<(double X, double Y)> coords,
            CultureInfo ic)
        {
            const int MaxPointsPerRow = 4;
            var (perimeter, area) = ComputePerimeterAndArea(coords);
            int total = ptNames.Count;
            int rowCount = (total + MaxPointsPerRow - 1) / MaxPointsPerRow;

            for (int row = 0; row < rowCount; row++)
            {
                var sb = new StringBuilder();
                sb.Append($"   \"Unique Name\"={id}");
                int startIdx = row * MaxPointsPerRow;
                int count = Math.Min(MaxPointsPerRow, total - startIdx);
                for (int j = 0; j < count; j++)
                    sb.Append($"   UniquePt{j + 1}={ptNames[startIdx + j]}");
                if (row == 0)
                {
                    sb.Append($"   Perimeter={perimeter.ToString("F1", ic)}   Area={area.ToString("F1", ic)}");
                    if (rowCount > 1) sb.Append("   Order=1");
                }
                else
                {
                    sb.Append($"   Order={row + 1}");
                }
                sw.WriteLine(sb.ToString());
            }
        }

        private static (double Perimeter, double Area) ComputePerimeterAndArea(
            IReadOnlyList<(double X, double Y)> coords)
        {
            if (coords.Count < 2)
                return (0, 0);

            double perimeter = 0;
            double area2 = 0;

            for (int i = 0; i < coords.Count; i++)
            {
                var (x1, y1) = coords[i];
                var (x2, y2) = coords[(i + 1) % coords.Count];
                double dx = x2 - x1;
                double dy = y2 - y1;
                perimeter += Math.Sqrt((dx * dx) + (dy * dy));
                area2 += (x1 * y2) - (x2 * y1);
            }

            return (perimeter, Math.Abs(0.5 * area2));
        }
    }
}
