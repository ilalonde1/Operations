#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe
{
    /// <summary>
    /// Full end-to-end virtual pipeline: real PDF → Extract → ReclassifyByColor
    /// → ExportOrchestrator.Run (against VirtualSafeDriver) → assert snapshot.
    ///
    /// These tests catch exactly the class of bugs that unit tests per stage
    /// missed (e.g. the orphan-slab-promotion bug, the SAFE LoadPattern
    /// already-exists collision, eItemType int-vs-enum coercion). They run
    /// in ~50ms with zero COM, zero SAFE install, zero publish cycle.
    /// </summary>
    public class VirtualPipelineTests
    {
        private readonly ITestOutputHelper _out;
        public VirtualPipelineTests(ITestOutputHelper output) => _out = output;

        // ─── Fixture helpers ──────────────────────────────────────────────

        private static string FixturePath(string name, bool mustExist = true)
        {
            string dir = Path.GetDirectoryName(typeof(VirtualPipelineTests).Assembly.Location)!;
            string path = Path.Combine(dir, "PdfToSafe", "Fixtures", name);
            if (!File.Exists(path))
            {
                // Try repo-relative fallback for IDE runners that don't copy output.
                string repoRelative = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "PdfToSafe", "Fixtures", name));
                if (File.Exists(repoRelative)) path = repoRelative;
            }
            if (mustExist) Assert.True(File.Exists(path), $"Fixture file not found: {path}");
            return path;
        }

        private static SafeApiExporter.ExportInput BuildInput(
            ExtractedGeometry reclassified,
            Dictionary<(byte R, byte G, byte B), SlabColorSettings>? colorSettings,
            HashSet<int>? excludedSlabs = null,
            HashSet<int>? excludedColumns = null,
            HashSet<int>? excludedLines = null,
            string grade = "C30",
            double thicknessMm = 250.0,
            bool isImperial = false)
        {
            var keptSlabs = new List<IReadOnlyList<(double X, double Y)>>();
            var keptSlabColors = new List<(byte R, byte G, byte B)>();
            for (int i = 0; i < reclassified.Slabs.Count; i++)
            {
                if (excludedSlabs?.Contains(i) == true) continue;
                keptSlabs.Add(reclassified.Slabs[i]);
                keptSlabColors.Add(i < reclassified.SlabColors.Count ? reclassified.SlabColors[i] : ((byte)0, (byte)0, (byte)0));
            }

            var keptColumns = new List<(double X, double Y)>();
            var keptColumnSz = new List<(double WidthMm, double DepthMm)>();
            for (int i = 0; i < reclassified.Columns.Count; i++)
            {
                if (excludedColumns?.Contains(i) == true) continue;
                keptColumns.Add(reclassified.Columns[i]);
                keptColumnSz.Add(i < reclassified.ColumnSizes.Count ? reclassified.ColumnSizes[i] : (400.0, 400.0));
            }

            var keptLines = new List<IReadOnlyList<(double X, double Y)>>();
            var keptHints = new List<(double WidthMm, double DepthMm)?>();
            for (int i = 0; i < reclassified.Lines.Count; i++)
            {
                if (excludedLines?.Contains(i) == true) continue;
                keptLines.Add(reclassified.Lines[i]);
                keptHints.Add(i < reclassified.LineSectionHints.Count ? reclassified.LineSectionHints[i] : null);
            }

            return new SafeApiExporter.ExportInput
            {
                Slabs              = keptSlabs,
                SlabColors         = keptSlabColors,
                Columns            = keptColumns,
                ColumnSizes        = keptColumnSz,
                Lines              = keptLines,
                LineSectionHints   = keptHints,
                ColorSettings      = colorSettings,
                DefaultGradeCode   = grade,
                DesignCode         = Kor.Operations.EngineeringTools.PdfToSafe.DesignCodeOption.CSA_A23_3_19,
                DefaultThicknessMm = thicknessMm,
                DefaultWallDepthMm = 1000.0,
                ColumnHeightMm     = 3000.0,
                DestFdbPath        = Path.Combine(Path.GetTempPath(), "virtual-test.fdb"),
                IsImperial         = isImperial,
            };
        }

        // ─── Regent floor: the canonical E2E test ─────────────────────────

        [Fact]
        public void RegentFloor_FullPipeline_ExtractsClassifiesAndExports()
        {
            // 1. Extract
            string pdfPath = FixturePath("regent_typ_floor.pdf");
            var geo = PdfGeometryExtractor.Extract(pdfPath, scaleDenominator: 100);

            Assert.True(geo.IsVectorPdf, "Regent PDF should be detected as vector");
            Assert.True(geo.Slabs.Count > 0 || geo.Lines.Count > 0, "Expected slabs or lines from extraction");

            _out.WriteLine($"Extracted: {geo.Slabs.Count} slabs, {geo.Columns.Count} cols, {geo.Lines.Count} lines");

            // 2. Reclassify — mirrors what the AI classifier does:
            //    burgundy (#800000) → Wall for slab-bucket shapes
            //    red (#F00000) → Slab
            var burgundy = ((byte)0x80, (byte)0x00, (byte)0x00);
            var red      = ((byte)0xF0, (byte)0x00, (byte)0x00);
            var colorSettings = new Dictionary<(byte R, byte G, byte B), SlabColorSettings>
            {
                [burgundy] = new SlabColorSettings { ElementType = "Wall", GradeCode = "C30", ThicknessMm = 250 },
                [red]      = new SlabColorSettings { ElementType = "Slab", GradeCode = "C30", ThicknessMm = 250, SdlKPa = 1.5, LiveKPa = 1.9 },
            };
            var reclassified = PdfGeometryExtractor.ReclassifyByColor(geo, colorSettings);

            _out.WriteLine($"Reclassified: {reclassified.Slabs.Count} slabs, {reclassified.Columns.Count} cols, {reclassified.Lines.Count} lines");

            // After reclassification:
            // - Wall-typed burgundy slabs → reduced to wall centerlines (Lines)
            // - Red lines → promoted to Slabs (closed outline + orphan balconies)
            // - Columns stay as columns
            Assert.True(reclassified.Slabs.Count >= 1, "At least the main slab outline should be present");
            Assert.Equal(reclassified.Lines.Count, reclassified.LineSectionHints.Count);
            Assert.Equal(reclassified.Columns.Count, reclassified.ColumnSizes.Count);

            // 3. Export against VirtualSafeDriver
            var driver = new VirtualSafeDriver();
            var input = BuildInput(reclassified, colorSettings);
            var result = ExportOrchestrator.Run(driver, input);

            _out.WriteLine($"Export result: {result.Message}");
            _out.WriteLine($"Virtual model snapshot:\n{driver.BuildSnapshot()}");

            Assert.True(result.Success, $"Export should succeed. Message: {result.Message}");

            // ── Structural invariants that MUST hold for a correct model ──

            // At least one slab (the closed red perimeter)
            Assert.True(result.SlabsExported >= 1, $"Expected ≥1 slab, got {result.SlabsExported}");
            Assert.True(driver.Areas.Count >= 1);

            // Columns: we saw 16 in every prior run
            Assert.True(result.ColumnsExported >= 14, $"Expected ≥14 columns, got {result.ColumnsExported}");

            // Every column has a 6-DOF restraint at its base
            Assert.Equal(result.ColumnsExported, driver.Restraints.Count);
            Assert.All(driver.Restraints, r => Assert.All(r.Dof6, d => Assert.True(d)));

            // Wall frames exist (burgundy→Wall shapes produced centerline frames)
            Assert.True(result.FramesExported >= 1, $"Expected wall frames, got {result.FramesExported}");

            // Loads: if slabs exist and color settings have SDL/LIVE > 0, loads applied
            if (result.SlabsExported > 0)
            {
                Assert.True(result.LoadsApplied >= 2, $"Expected SDL + LIVE loads on at least one slab, got {result.LoadsApplied}");
                // Verify unit conversion: kPa×1e-3 → N/mm²
                Assert.All(driver.AreaLoads, l =>
                    Assert.True(l.ValueNperMm2 > 0 && l.ValueNperMm2 < 0.01,
                        $"Load value {l.ValueNperMm2} N/mm² looks wrong (expected ~0.001–0.002 for 1–2 kPa)"));
            }

            // Material created with correct E (C30 → ~25742 MPa)
            var c30Mat = driver.Materials.FirstOrDefault(m => m.Name == "C30");
            Assert.NotNull(c30Mat);
            Assert.InRange(c30Mat!.E, 20000, 30000); // 4700·√30 ≈ 25742

            // Merge tolerance was set to 1mm
            Assert.Equal(1.0, driver.MergeTolMm);

            // Model was saved
            Assert.NotNull(driver.SavedPath);

            // No orphan column-top points disconnected from everything
            // (every column produces exactly 2 points — top + bottom)
            int columnPoints = result.ColumnsExported * 2;
            int slabPoints = driver.Areas.Sum(a => a.PointNames.Count);
            int wallPoints = result.FramesExported * 2;
            // Total points ≤ columnPoints + slabPoints + wallPoints
            // (with merge-overlap they'll be fewer, but never MORE)
            Assert.True(driver.Points.Count <= columnPoints + slabPoints + wallPoints,
                $"Unexpected extra points: {driver.Points.Count} vs expected ≤{columnPoints + slabPoints + wallPoints}");
        }

        // ─── Specific regression tests ────────────────────────────────────

        [Fact]
        public void RegentFloor_BalconyExtensions_AreSlabsNotBeams()
        {
            // The bug that produced "1 slab + 12 frames" instead of "5 slabs + 4 frames".
            // This test locks the fix: ≥3-point orphan slab-typed lines
            // get promoted to their own slab polygons.
            string pdfPath = FixturePath("regent_typ_floor.pdf");
            var geo = PdfGeometryExtractor.Extract(pdfPath, scaleDenominator: 100);

            var red = ((byte)0xF0, (byte)0x00, (byte)0x00);
            var colorSettings = new Dictionary<(byte R, byte G, byte B), SlabColorSettings>
            {
                [red] = new SlabColorSettings { ElementType = "Slab", GradeCode = "C30", ThicknessMm = 250 },
            };
            var reclassified = PdfGeometryExtractor.ReclassifyByColor(geo, colorSettings);

            // Count how many reclassified slabs came from the red-typed lines.
            // Must be ≥ 2 (main outline + at least one balcony).
            Assert.True(reclassified.Slabs.Count >= 2,
                $"Expected ≥2 slabs from red-typed lines (outline + balconies), got {reclassified.Slabs.Count}");
        }

        [Fact]
        public void RegentFloor_WallCenterlines_HaveSectionHints()
        {
            string pdfPath = FixturePath("regent_typ_floor.pdf");
            var geo = PdfGeometryExtractor.Extract(pdfPath, scaleDenominator: 100);

            var burgundy = ((byte)0x80, (byte)0x00, (byte)0x00);
            var colorSettings = new Dictionary<(byte R, byte G, byte B), SlabColorSettings>
            {
                [burgundy] = new SlabColorSettings { ElementType = "Wall" },
            };
            var reclassified = PdfGeometryExtractor.ReclassifyByColor(geo, colorSettings);

            // Wall-reduced lines have non-null section hints; other lines
            // (unmapped red) have null hints. Verify the wall-reduction
            // produced at least one hinted line AND that all lists stay parallel.
            Assert.True(reclassified.Lines.Count > 0, "Expected wall centerlines from burgundy shapes");
            Assert.Equal(reclassified.Lines.Count, reclassified.LineSectionHints.Count);
            int wallLines = reclassified.LineSectionHints.Count(h => h is not null);
            Assert.True(wallLines >= 1, $"Expected at least 1 wall centerline with section hint, got {wallLines}");
        }

        [Fact]
        public void RegentFloor_LoadPatternIdempotency()
        {
            // SAFE pre-creates DEAD + LIVE. Our orchestrator must not try
            // to re-add LIVE (would return error 1). The VirtualSafeDriver
            // models this: AddLoadPattern returns 1 if name already exists.
            string pdfPath = FixturePath("regent_typ_floor.pdf");
            var geo = PdfGeometryExtractor.Extract(pdfPath, scaleDenominator: 100);

            var red = ((byte)0xF0, (byte)0x00, (byte)0x00);
            var colorSettings = new Dictionary<(byte R, byte G, byte B), SlabColorSettings>
            {
                [red] = new SlabColorSettings { ElementType = "Slab", GradeCode = "C30", ThicknessMm = 250, SdlKPa = 1.5, LiveKPa = 1.9 },
            };
            var reclassified = PdfGeometryExtractor.ReclassifyByColor(geo, colorSettings);

            var driver = new VirtualSafeDriver();
            var input = BuildInput(reclassified, colorSettings);
            var result = ExportOrchestrator.Run(driver, input);

            Assert.True(result.Success, $"Should not fail on pre-existing LIVE pattern. Message: {result.Message}");
            // SDL should be added (not pre-existing), LIVE should be skipped (pre-existing).
            Assert.Contains(driver.LoadPatterns, p => p.Name == "SDL");
            // Only one LIVE entry (the pre-existing one, not doubled).
            Assert.Single(driver.LoadPatterns.Where(p =>
                string.Equals(p.Name, "LIVE", StringComparison.OrdinalIgnoreCase)));
        }

        [Fact]
        public void RegentFloor_ColumnRestraintsAreFullFixity()
        {
            string pdfPath = FixturePath("regent_typ_floor.pdf");
            var geo = PdfGeometryExtractor.Extract(pdfPath, scaleDenominator: 100);

            var burgundy = ((byte)0x80, (byte)0x00, (byte)0x00);
            var colorSettings = new Dictionary<(byte R, byte G, byte B), SlabColorSettings>
            {
                [burgundy] = new SlabColorSettings { ElementType = "Column" },
            };
            var reclassified = PdfGeometryExtractor.ReclassifyByColor(geo, colorSettings);

            var driver = new VirtualSafeDriver();
            var input = BuildInput(reclassified, colorSettings);
            var result = ExportOrchestrator.Run(driver, input);

            Assert.True(result.Success, result.Message);
            // Every column's base restraint has all 6 DOFs fixed.
            Assert.All(driver.Restraints, r =>
            {
                Assert.Equal(6, r.Dof6.Length);
                Assert.All(r.Dof6, d => Assert.True(d));
            });
        }

        [Fact]
        public void RegentFloor_UnitConversion_kPaToNperMm2()
        {
            string pdfPath = FixturePath("regent_typ_floor.pdf");
            var geo = PdfGeometryExtractor.Extract(pdfPath, scaleDenominator: 100);

            var red = ((byte)0xF0, (byte)0x00, (byte)0x00);
            var colorSettings = new Dictionary<(byte R, byte G, byte B), SlabColorSettings>
            {
                [red] = new SlabColorSettings { ElementType = "Slab", GradeCode = "C30", ThicknessMm = 250, SdlKPa = 1.5, LiveKPa = 1.9 },
            };
            var reclassified = PdfGeometryExtractor.ReclassifyByColor(geo, colorSettings);

            var driver = new VirtualSafeDriver();
            var input = BuildInput(reclassified, colorSettings);
            ExportOrchestrator.Run(driver, input);

            // SDL: 1.5 kPa × 1e-3 = 0.0015 N/mm²
            // LIVE: 1.9 kPa × 1e-3 = 0.0019 N/mm²
            var sdlLoads = driver.AreaLoads.Where(l => l.PatternName == "SDL").ToList();
            var liveLoads = driver.AreaLoads.Where(l => l.PatternName == "LIVE").ToList();
            Assert.NotEmpty(sdlLoads);
            Assert.NotEmpty(liveLoads);
            Assert.All(sdlLoads, l => Assert.Equal(0.0015, l.ValueNperMm2, 12));
            Assert.All(liveLoads, l => Assert.Equal(0.0019, l.ValueNperMm2, 12));
        }

        [Fact]
        public void RegentFloor_Imperial_CoordinatesConvertedToInches()
        {
            // The canonical imperial pipeline test: same Regent PDF, same
            // reclassification, but IsImperial=true. All coordinates should
            // be in inches (mm/25.4), E in ksi, loads in kip/in².
            string pdfPath = FixturePath("regent_typ_floor.pdf");
            var geo = PdfGeometryExtractor.Extract(pdfPath, scaleDenominator: 100);

            var burgundy = ((byte)0x80, (byte)0x00, (byte)0x00);
            var red      = ((byte)0xF0, (byte)0x00, (byte)0x00);
            var colorSettings = new Dictionary<(byte R, byte G, byte B), SlabColorSettings>
            {
                [burgundy] = new SlabColorSettings { ElementType = "Wall", GradeCode = "C30", ThicknessMm = 250 },
                [red]      = new SlabColorSettings { ElementType = "Slab", GradeCode = "C30", ThicknessMm = 250, SdlKPa = 1.5, LiveKPa = 1.9 },
            };
            var reclassified = PdfGeometryExtractor.ReclassifyByColor(geo, colorSettings);

            var driver = new VirtualSafeDriver();
            var input = BuildInput(reclassified, colorSettings, isImperial: true);

            var result = ExportOrchestrator.Run(driver, input);
            Assert.True(result.Success, result.Message);
            Assert.True(driver.IsImperial);

            // Spot-check: coordinates should be ~1/25.4 of the metric values.
            // Metric A1 first vertex is ~(11871, 28061). Imperial should be ~(467, 1105).
            var firstArea = driver.Areas.First();
            var firstPt = driver.Points.First(p => p.Name == firstArea.PointNames[0]);
            Assert.InRange(firstPt.X, 100, 2000); // inches, not mm
            Assert.InRange(firstPt.Y, 100, 2000);

            // E should be in ksi (CSA C30 E=24648 MPa → ~3574 ksi)
            var mat = driver.Materials.First();
            Assert.InRange(mat.E, 3000, 4000); // ksi range, not MPa

            // Slab thickness: 250mm → ~9.84 in
            var slab = driver.SlabProps.First();
            Assert.InRange(slab.ThicknessMm, 9.0, 11.0); // field name is still ThicknessMm but value is inches

            // Loads: 1.5 kPa → kip/in². 1.5 × (1e-3/6.895) ≈ 2.18e-4 kip/in²
            var sdlLoad = driver.AreaLoads.First(l => l.PatternName == "SDL");
            Assert.InRange(sdlLoad.ValueNperMm2, 1e-4, 5e-4); // kip/in² range

            // Column base Z: 3000mm → 118.1 in, so base is at ~-118 in
            var colBase = driver.Points.Where(p => p.Z < 0).First();
            Assert.InRange(colBase.Z, -130, -100); // inches, not -3000 mm

            // Wall/beam frame endpoints at z=0, in inch coordinate range
            var wallFrames = driver.Frames.Where(f => f.SectionName.StartsWith("B")).ToList();
            if (wallFrames.Count > 0)
            {
                var wPt = driver.Points.First(p => p.Name == wallFrames[0].StartPt);
                Assert.InRange(wPt.X, 100, 2000); // inches
                Assert.Equal(0.0, wPt.Z);
            }

            // Merge tolerance in inches (~0.039 in from 1mm)
            Assert.InRange(driver.MergeTolMm, 0.03, 0.05);

            // Restraints still present in imperial mode
            Assert.True(driver.Restraints.Count > 0);
            Assert.All(driver.Restraints, r => Assert.All(r.Dof6, d => Assert.True(d)));
        }

        [Fact]
        public void RegentFloor_CsaDesignCode_UsesCorrectEFormula()
        {
            // CRITICAL regression lock: prior to this fix, the OAPI path always
            // used E=4700√fc (ACI) regardless of design code. CSA A23.3 uses
            // E=4500√fc. For C30: ACI=25743 MPa, CSA=24648 MPa. A 4.4% stiffness
            // error that directly affects deflection and moment distribution.
            string pdfPath = FixturePath("regent_typ_floor.pdf");
            var geo = PdfGeometryExtractor.Extract(pdfPath, scaleDenominator: 100);

            var red = ((byte)0xF0, (byte)0x00, (byte)0x00);
            var colorSettings = new Dictionary<(byte R, byte G, byte B), SlabColorSettings>
            {
                [red] = new SlabColorSettings { ElementType = "Slab", GradeCode = "C30", ThicknessMm = 250 },
            };
            var reclassified = PdfGeometryExtractor.ReclassifyByColor(geo, colorSettings);

            // CSA path
            var csaDriver = new VirtualSafeDriver();
            var csaInput = BuildInput(reclassified, colorSettings);
            // csaInput already has DesignCode = CSA_A23_3_19
            ExportOrchestrator.Run(csaDriver, csaInput);

            var csaE = csaDriver.Materials.First(m => m.Name == "C30").E;
            double expectedCsaE = 4500.0 * Math.Sqrt(30.0);
            Assert.Equal(expectedCsaE, csaE, 1);

            // Verify it's NOT the ACI value
            double aciE = 4700.0 * Math.Sqrt(30.0);
            Assert.NotEqual(aciE, csaE);
        }

        [Fact]
        public void RegentFloor_NoConsecutiveDuplicateVerticesInSlabs()
        {
            string pdfPath = FixturePath("regent_typ_floor.pdf");
            var geo = PdfGeometryExtractor.Extract(pdfPath, scaleDenominator: 100);

            var burgundy = ((byte)0x80, (byte)0x00, (byte)0x00);
            var red      = ((byte)0xF0, (byte)0x00, (byte)0x00);
            var colorSettings = new Dictionary<(byte R, byte G, byte B), SlabColorSettings>
            {
                [burgundy] = new SlabColorSettings { ElementType = "Wall", GradeCode = "C30", ThicknessMm = 250 },
                [red]      = new SlabColorSettings { ElementType = "Slab", GradeCode = "C30", ThicknessMm = 250, SdlKPa = 1.5, LiveKPa = 1.9 },
            };
            var reclassified = PdfGeometryExtractor.ReclassifyByColor(geo, colorSettings);

            var driver = new VirtualSafeDriver();
            var input = BuildInput(reclassified, colorSettings);
            var result = ExportOrchestrator.Run(driver, input);
            Assert.True(result.Success, result.Message);

            // After dedup, no area should have consecutive identical vertices.
            foreach (var area in driver.Areas)
            {
                var pts = area.PointNames.Select(n => driver.Points.First(p => p.Name == n)).ToList();
                for (int i = 1; i < pts.Count; i++)
                {
                    double dx = Math.Abs(pts[i].X - pts[i - 1].X);
                    double dy = Math.Abs(pts[i].Y - pts[i - 1].Y);
                    Assert.True(dx > 0.01 || dy > 0.01,
                        $"Area {area.Name}: consecutive duplicate at vertex {i} ({pts[i].X:F1},{pts[i].Y:F1})");
                }
            }
        }

        [Fact]
        public void RegentFloor_ColumnSectionsRoundedToNearest5mm()
        {
            // Pre-fix, extraction noise produced C368x952 AND C368x953 as
            // separate sections. Post-fix, both round to C370x955 (or
            // similar) and merge into one section.
            string pdfPath = FixturePath("regent_typ_floor.pdf");
            var geo = PdfGeometryExtractor.Extract(pdfPath, scaleDenominator: 100);

            var burgundy = ((byte)0x80, (byte)0x00, (byte)0x00);
            var colorSettings = new Dictionary<(byte R, byte G, byte B), SlabColorSettings>
            {
                [burgundy] = new SlabColorSettings { ElementType = "Column" },
            };
            var reclassified = PdfGeometryExtractor.ReclassifyByColor(geo, colorSettings);

            var driver = new VirtualSafeDriver();
            var input = BuildInput(reclassified, colorSettings);
            ExportOrchestrator.Run(driver, input);

            // No two frame props should differ by < 5mm in any dimension.
            var props = driver.FrameProps.Select(p => (p.DepthMm, p.WidthMm)).ToList();
            for (int i = 0; i < props.Count; i++)
                for (int j = i + 1; j < props.Count; j++)
                {
                    double dd = Math.Abs(props[i].DepthMm - props[j].DepthMm);
                    double dw = Math.Abs(props[i].WidthMm - props[j].WidthMm);
                    // If names differ, at least one dimension should differ by > 5.
                    if (dd < 5 && dw < 5)
                    {
                        Assert.Fail($"Near-duplicate frame sections: " +
                            $"({props[i].WidthMm:F0}×{props[i].DepthMm:F0}) and " +
                            $"({props[j].WidthMm:F0}×{props[j].DepthMm:F0})");
                    }
                }
        }

        [Fact]
        public void RegentFloor_AllSlabsHaveBothLoads()
        {
            string pdfPath = FixturePath("regent_typ_floor.pdf");
            var geo = PdfGeometryExtractor.Extract(pdfPath, scaleDenominator: 100);

            var burgundy = ((byte)0x80, (byte)0x00, (byte)0x00);
            var red = ((byte)0xF0, (byte)0x00, (byte)0x00);
            var colorSettings = new Dictionary<(byte R, byte G, byte B), SlabColorSettings>
            {
                [burgundy] = new SlabColorSettings { ElementType = "Wall", GradeCode = "C30", ThicknessMm = 250 },
                [red] = new SlabColorSettings { ElementType = "Slab", GradeCode = "C30", ThicknessMm = 250, SdlKPa = 1.5, LiveKPa = 1.9 },
            };
            var reclassified = PdfGeometryExtractor.ReclassifyByColor(geo, colorSettings);

            var driver = new VirtualSafeDriver();
            var input = BuildInput(reclassified, colorSettings);
            var result = ExportOrchestrator.Run(driver, input);
            Assert.True(result.Success, result.Message);

            // EVERY exported slab must have BOTH SDL and LIVE loads.
            foreach (var area in driver.Areas)
            {
                var loads = driver.AreaLoads.Where(l => l.AreaName == area.Name).ToList();
                Assert.Contains(loads, l => l.PatternName == "SDL");
                Assert.Contains(loads, l => l.PatternName == "LIVE");
            }
            // Total loads = slabs × 2.
            Assert.Equal(driver.Areas.Count * 2, driver.AreaLoads.Count);
        }

        [Fact]
        public void RegentFloor_AllColumnBasesBelow_z0()
        {
            string pdfPath = FixturePath("regent_typ_floor.pdf");
            var geo = PdfGeometryExtractor.Extract(pdfPath, scaleDenominator: 100);

            var burgundy = ((byte)0x80, (byte)0x00, (byte)0x00);
            var colorSettings = new Dictionary<(byte R, byte G, byte B), SlabColorSettings>
            {
                [burgundy] = new SlabColorSettings { ElementType = "Column" },
            };
            var reclassified = PdfGeometryExtractor.ReclassifyByColor(geo, colorSettings);

            var driver = new VirtualSafeDriver();
            var input = BuildInput(reclassified, colorSettings);
            ExportOrchestrator.Run(driver, input);

            // Every restraint should be at z < 0 (column base, below slab).
            foreach (var r in driver.Restraints)
            {
                var pt = driver.Points.First(p => p.Name == r.PointName);
                Assert.True(pt.Z < 0, $"Restraint {r.PointName} at z={pt.Z} — expected below slab (z<0)");
            }
        }

        [Fact]
        public void RegentFloor_WallFramesAtSlabLevel()
        {
            string pdfPath = FixturePath("regent_typ_floor.pdf");
            var geo = PdfGeometryExtractor.Extract(pdfPath, scaleDenominator: 100);

            var burgundy = ((byte)0x80, (byte)0x00, (byte)0x00);
            var colorSettings = new Dictionary<(byte R, byte G, byte B), SlabColorSettings>
            {
                [burgundy] = new SlabColorSettings { ElementType = "Wall" },
            };
            var reclassified = PdfGeometryExtractor.ReclassifyByColor(geo, colorSettings);

            var driver = new VirtualSafeDriver();
            var input = BuildInput(reclassified, colorSettings);
            ExportOrchestrator.Run(driver, input);

            // Wall frames (section prefix "B") should be horizontal at z=0.
            // Column frames (prefix "C") go to z=-3000 and are checked elsewhere.
            var wallFrames = driver.Frames.Where(f => f.SectionName.StartsWith("B")).ToList();
            Assert.NotEmpty(wallFrames);
            foreach (var f in wallFrames)
            {
                var s = driver.Points.First(p => p.Name == f.StartPt);
                var e = driver.Points.First(p => p.Name == f.EndPt);
                Assert.Equal(0.0, s.Z);
                Assert.Equal(0.0, e.Z);
                // Frame must have nonzero length.
                double len = Math.Sqrt(Math.Pow(s.X - e.X, 2) + Math.Pow(s.Y - e.Y, 2));
                Assert.True(len > 10.0, $"Wall frame {f.Name} is degenerate: length {len:F1} mm");
            }
        }

        [Fact]
        public void RegentFloor_SnapshotIsStable()
        {
            // Golden-file test: run the full pipeline and compare the
            // VirtualSafeDriver snapshot against a committed baseline. Any
            // unintentional behaviour change shows up as a diff here.
            string pdfPath = FixturePath("regent_typ_floor.pdf");
            var geo = PdfGeometryExtractor.Extract(pdfPath, scaleDenominator: 100);

            var burgundy = ((byte)0x80, (byte)0x00, (byte)0x00);
            var red      = ((byte)0xF0, (byte)0x00, (byte)0x00);
            var colorSettings = new Dictionary<(byte R, byte G, byte B), SlabColorSettings>
            {
                [burgundy] = new SlabColorSettings { ElementType = "Wall", GradeCode = "C30", ThicknessMm = 250 },
                [red]      = new SlabColorSettings { ElementType = "Slab", GradeCode = "C30", ThicknessMm = 250, SdlKPa = 1.5, LiveKPa = 1.9 },
            };
            var reclassified = PdfGeometryExtractor.ReclassifyByColor(geo, colorSettings);

            var driver = new VirtualSafeDriver();
            var input = BuildInput(reclassified, colorSettings);
            var result = ExportOrchestrator.Run(driver, input);
            Assert.True(result.Success, result.Message);

            string snapshot = driver.BuildSnapshot();
            string goldenPath = FixturePath("regent_typ_floor.expected.txt", mustExist: false);

            if (!File.Exists(goldenPath))
            {
                // First run: create the baseline. Subsequent runs diff against it.
                File.WriteAllText(goldenPath, snapshot);
                _out.WriteLine("Golden file created (first run). Review and commit:");
                _out.WriteLine(goldenPath);
                _out.WriteLine(snapshot);
                // Don't fail — the golden file now exists for the next run.
                return;
            }

            string expected = File.ReadAllText(goldenPath);
            if (snapshot != expected)
            {
                _out.WriteLine("=== SNAPSHOT DIFF ===");
                _out.WriteLine("Expected (golden):");
                _out.WriteLine(expected);
                _out.WriteLine("Actual:");
                _out.WriteLine(snapshot);

                // Write the actual to a .actual.txt file next to the golden
                // for easy diffing.
                string actualPath = goldenPath.Replace(".expected.txt", ".actual.txt");
                File.WriteAllText(actualPath, snapshot);
                _out.WriteLine($"Actual written to: {actualPath}");
            }

            Assert.Equal(expected, snapshot);
        }
    }
}
