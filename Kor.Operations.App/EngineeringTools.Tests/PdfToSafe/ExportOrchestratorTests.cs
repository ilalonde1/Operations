#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe
{
    /// <summary>
    /// End-to-end unit tests for <see cref="ExportOrchestrator"/>. We drive it
    /// against a hand-rolled <see cref="FakeSafeOapiDriver"/> that records
    /// every call and supports per-method failure injection, so we can
    /// assert on call sequence, argument values, unit math, idempotency,
    /// and error handling without ever touching SAFE.
    ///
    /// These tests replace the bulk of the "publish → click → read status →
    /// diagnose → fix" loop that used to be the only way to validate the
    /// OAPI export path.
    /// </summary>
    public class ExportOrchestratorTests
    {
        // ─── Call-sequence scaffolding ────────────────────────────────────

        private sealed record Call(string Method, IReadOnlyList<object?> Args);

        /// <summary>In-memory fake that records every OAPI call the orchestrator makes and lets tests inject per-method return codes / pre-existing state.</summary>
        private sealed class FakeSafeOapiDriver : ISafeOapiDriver
        {
            public List<Call> Calls { get; } = new();

            /// <summary>Return-code overrides keyed by method name. Default is 0 (success). Used for failure injection.</summary>
            public Dictionary<string, int> ReturnOverrides { get; } = new();

            /// <summary>Exception overrides keyed by method name — when set, the named method throws the given exception.</summary>
            public Dictionary<string, Exception> ThrowOverrides { get; } = new();

            /// <summary>Pre-populated load pattern names ListLoadPatternNames returns. Defaults to SAFE's "DEAD" + "LIVE" pre-populated blank-model behavior.</summary>
            public List<string> PreExistingLoadPatterns { get; } = new() { "DEAD", "LIVE" };

            public int DisposeCallCount { get; private set; }

            private int _nextPointId = 1;
            private int _nextAreaId = 1;
            private int _nextFrameId = 1;

            private int Record(string method, params object?[] args)
            {
                Calls.Add(new Call(method, args));
                if (ThrowOverrides.TryGetValue(method, out var ex)) throw ex;
                return ReturnOverrides.TryGetValue(method, out int ret) ? ret : 0;
            }

            public int Start()                                             => Record(nameof(Start));
            public int Unhide()                                            => Record(nameof(Unhide));
            public int InitializeNewModel(bool imperial)                   => Record(nameof(InitializeNewModel), imperial);
            public int NewBlank()                                          => Record(nameof(NewBlank));
            public int SetMergeTol(double mm)                              => Record(nameof(SetMergeTol), mm);
            public int SaveModel(string destFdbPath)                       => Record(nameof(SaveModel), destFdbPath);
            public int SetMaterial(string name, string notes)              => Record(nameof(SetMaterial), name, notes);
            public int SetMPIsotropic(string name, double e, double u, double a)
                                                                            => Record(nameof(SetMPIsotropic), name, e, u, a);
            public int SetSlabProp(string name, string matName, double thicknessMm)
                                                                            => Record(nameof(SetSlabProp), name, matName, thicknessMm);
            public int SetFrameRectangleProp(string name, string matName, double depthMm, double widthMm)
                                                                            => Record(nameof(SetFrameRectangleProp), name, matName, depthMm, widthMm);
            public int AddLoadPattern(string name, SafeLoadPatternType type)
                                                                            => Record(nameof(AddLoadPattern), name, type);

            public IReadOnlyList<string> ListLoadPatternNames()
            {
                Calls.Add(new Call(nameof(ListLoadPatternNames), Array.Empty<object?>()));
                return PreExistingLoadPatterns.ToArray();
            }

            public int AddPoint(double x, double y, double z, out string pointName)
            {
                int ret = Record(nameof(AddPoint), x, y, z);
                pointName = ret == 0 ? $"P{_nextPointId++}" : "";
                return ret;
            }

            public int AddArea(IReadOnlyList<string> pointNames, string propName, out string areaName)
            {
                int ret = Record(nameof(AddArea), pointNames.Count, propName);
                areaName = ret == 0 ? $"A{_nextAreaId++}" : "";
                return ret;
            }

            public int AddFrame(string startPointName, string endPointName, string sectionName, out string frameName)
            {
                int ret = Record(nameof(AddFrame), startPointName, endPointName, sectionName);
                frameName = ret == 0 ? $"F{_nextFrameId++}" : "";
                return ret;
            }

            public int SetAreaLoadUniform(string areaName, string loadPatternName, double loadNperMm2)
                                                                            => Record(nameof(SetAreaLoadUniform), areaName, loadPatternName, loadNperMm2);
            public int SetPointRestraint(string pointName, bool[] dof6)     => Record(nameof(SetPointRestraint), pointName, dof6);
            public int SetFrameInsertionPoint(string frameName, int cardinal)=> Record(nameof(SetFrameInsertionPoint), frameName, cardinal);
            public int SetAreaEdgeConstraint(string areaName, bool enabled) => Record(nameof(SetAreaEdgeConstraint), areaName, enabled);
            public int SetSlabModifiers(string propName, double membrane, double bending, double shear)
                                                                            => Record(nameof(SetSlabModifiers), propName, membrane, bending, shear);
            public int AddGridLines(IReadOnlyList<(string Label, bool IsAlongX, double Ordinate)> gridLines)
                                                                            => Record(nameof(AddGridLines), gridLines.Count);

            public void Dispose() => DisposeCallCount++;
        }

        // ─── Fixture helpers ──────────────────────────────────────────────

        private static SafeApiExporter.ExportInput MinimalInput(
            IReadOnlyList<IReadOnlyList<(double X, double Y)>>? slabs = null,
            IReadOnlyList<(byte R, byte G, byte B)>? slabColors = null,
            IReadOnlyDictionary<(byte R, byte G, byte B), SlabColorSettings>? colors = null,
            IReadOnlyList<(double X, double Y)>? columns = null,
            IReadOnlyList<(double WidthMm, double DepthMm)>? columnSizes = null,
            IReadOnlyList<IReadOnlyList<(double X, double Y)>>? lines = null,
            IReadOnlyList<(double WidthMm, double DepthMm)?>? lineHints = null,
            string grade = "C30",
            double thicknessMm = 250.0,
            bool isImperial = false)
            => new()
            {
                Slabs              = slabs       ?? new List<IReadOnlyList<(double, double)>>(),
                SlabColors         = slabColors  ?? new List<(byte, byte, byte)>(),
                Columns            = columns     ?? new List<(double, double)>(),
                ColumnSizes        = columnSizes ?? new List<(double, double)>(),
                Lines              = lines       ?? new List<IReadOnlyList<(double, double)>>(),
                LineSectionHints   = lineHints   ?? new List<(double, double)?>(),
                ColorSettings      = colors,
                DefaultGradeCode   = grade,
                DesignCode         = Kor.Operations.EngineeringTools.PdfToSafe.DesignCodeOption.CSA_A23_3_19,
                DefaultThicknessMm = thicknessMm,
                DefaultWallDepthMm = 1000.0,
                ColumnHeightMm     = 3000.0,
                DestFdbPath        = Path.Combine(Path.GetTempPath(), "kor-test.fdb"),
                IsImperial         = isImperial,
            };

        private static IReadOnlyList<(double X, double Y)> Rect(double x0, double y0, double x1, double y1)
            => new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };

        // ─── Tests ────────────────────────────────────────────────────────

        [Fact]
        public void EmptyInput_ReturnsFailedWithoutTouchingDriver()
        {
            var driver = new FakeSafeOapiDriver();
            var result = ExportOrchestrator.Run(driver, MinimalInput());

            Assert.False(result.Success);
            Assert.Contains("Nothing to export", result.Message);
            Assert.Empty(driver.Calls);
        }

        [Fact]
        public void SingleSlab_RunsMaterialSlabPropAndAreaInOrder()
        {
            var driver = new FakeSafeOapiDriver();
            var input = MinimalInput(
                slabs: new[] { Rect(0, 0, 6000, 6000) },
                slabColors: new[] { ((byte)255, (byte)0, (byte)0) });

            var result = ExportOrchestrator.Run(driver, input);

            Assert.True(result.Success, result.Message);
            Assert.Equal(1, result.SlabsExported);
            Assert.Equal(0, result.Skipped);

            // Method ordering — must set material before slab prop before adding the area.
            var methods = driver.Calls.Select(c => c.Method).ToList();
            int idxMat  = methods.IndexOf(nameof(FakeSafeOapiDriver.SetMaterial));
            int idxMp   = methods.IndexOf(nameof(FakeSafeOapiDriver.SetMPIsotropic));
            int idxProp = methods.IndexOf(nameof(FakeSafeOapiDriver.SetSlabProp));
            int idxArea = methods.IndexOf(nameof(FakeSafeOapiDriver.AddArea));
            Assert.True(idxMat  >= 0);
            Assert.True(idxMp   > idxMat);
            Assert.True(idxProp > idxMp);
            Assert.True(idxArea > idxProp);

            // AddPoint called once per vertex (4) before AddArea.
            int pointsAdded = driver.Calls.Count(c => c.Method == nameof(FakeSafeOapiDriver.AddPoint));
            Assert.Equal(4, pointsAdded);
        }

        [Fact]
        public void TwoSlabsSameGradeAndThickness_CreatesOneMaterialAndOneProp()
        {
            var driver = new FakeSafeOapiDriver();
            var input = MinimalInput(
                slabs: new[] { Rect(0, 0, 1000, 1000), Rect(2000, 0, 3000, 1000) },
                slabColors: new[] { ((byte)255, (byte)0, (byte)0), ((byte)255, (byte)0, (byte)0) });

            var result = ExportOrchestrator.Run(driver, input);

            Assert.True(result.Success, result.Message);
            Assert.Equal(2, result.SlabsExported);
            Assert.Equal(1, driver.Calls.Count(c => c.Method == nameof(FakeSafeOapiDriver.SetMaterial)));
            Assert.Equal(1, driver.Calls.Count(c => c.Method == nameof(FakeSafeOapiDriver.SetSlabProp)));
        }

        [Fact]
        public void MultipleGrades_CreatesOneMaterialPerGrade()
        {
            var driver = new FakeSafeOapiDriver();
            var redKey = ((byte)255, (byte)0, (byte)0);
            var blueKey = ((byte)0, (byte)0, (byte)255);
            var input = MinimalInput(
                slabs: new[] { Rect(0, 0, 1000, 1000), Rect(2000, 0, 3000, 1000) },
                slabColors: new[] { redKey, blueKey },
                colors: new Dictionary<(byte, byte, byte), SlabColorSettings>
                {
                    [redKey]  = new SlabColorSettings { GradeCode = "C30", ThicknessMm = 250 },
                    [blueKey] = new SlabColorSettings { GradeCode = "C40", ThicknessMm = 300 },
                });

            var result = ExportOrchestrator.Run(driver, input);

            Assert.True(result.Success, result.Message);
            var materialCalls = driver.Calls.Where(c => c.Method == nameof(FakeSafeOapiDriver.SetMaterial))
                                             .Select(c => (string)c.Args[0]!).ToList();
            Assert.Contains("C30", materialCalls);
            Assert.Contains("C40", materialCalls);
        }

        [Fact]
        public void SlabWithLoads_AddsSdlAndLiveAreaLoadsInDbUnits()
        {
            var driver = new FakeSafeOapiDriver();
            // Start with NO pre-existing patterns to force Add for both SDL and LIVE.
            driver.PreExistingLoadPatterns.Clear();
            var key = ((byte)255, (byte)0, (byte)0);
            var input = MinimalInput(
                slabs: new[] { Rect(0, 0, 1000, 1000) },
                slabColors: new[] { key },
                colors: new Dictionary<(byte, byte, byte), SlabColorSettings>
                {
                    [key] = new SlabColorSettings { GradeCode = "C30", ThicknessMm = 250, SdlKPa = 1.5, LiveKPa = 1.9 },
                });

            var result = ExportOrchestrator.Run(driver, input);

            Assert.True(result.Success, result.Message);
            Assert.Equal(2, result.LoadsApplied);

            // SDL + LIVE patterns created with their enum types.
            var patternAdds = driver.Calls.Where(c => c.Method == nameof(FakeSafeOapiDriver.AddLoadPattern)).ToList();
            Assert.Equal(2, patternAdds.Count);
            Assert.Contains(patternAdds, p => (string)p.Args[0]! == "SDL"  && (SafeLoadPatternType)p.Args[1]! == SafeLoadPatternType.SuperDead);
            Assert.Contains(patternAdds, p => (string)p.Args[0]! == "LIVE" && (SafeLoadPatternType)p.Args[1]! == SafeLoadPatternType.Live);

            // kPa → N/mm² conversion: 1.5 kPa == 0.0015 N/mm² ; 1.9 kPa == 0.0019.
            var loadCalls = driver.Calls.Where(c => c.Method == nameof(FakeSafeOapiDriver.SetAreaLoadUniform)).ToList();
            Assert.Equal(2, loadCalls.Count);
            Assert.Contains(loadCalls, c => (string)c.Args[1]! == "SDL"  && Math.Abs((double)c.Args[2]! - 0.0015) < 1e-12);
            Assert.Contains(loadCalls, c => (string)c.Args[1]! == "LIVE" && Math.Abs((double)c.Args[2]! - 0.0019) < 1e-12);
        }

        [Fact]
        public void PreExistingLivePattern_NotReAdded()
        {
            var driver = new FakeSafeOapiDriver(); // defaults include LIVE
            var key = ((byte)255, (byte)0, (byte)0);
            var input = MinimalInput(
                slabs: new[] { Rect(0, 0, 1000, 1000) },
                slabColors: new[] { key },
                colors: new Dictionary<(byte, byte, byte), SlabColorSettings>
                {
                    [key] = new SlabColorSettings { GradeCode = "C30", ThicknessMm = 250, SdlKPa = 1.5, LiveKPa = 1.9 },
                });

            var result = ExportOrchestrator.Run(driver, input);

            Assert.True(result.Success, result.Message);
            // LIVE is pre-existing → only SDL should be Added.
            var patternAdds = driver.Calls.Where(c => c.Method == nameof(FakeSafeOapiDriver.AddLoadPattern))
                                           .Select(c => (string)c.Args[0]!).ToList();
            Assert.Contains("SDL",  patternAdds);
            Assert.DoesNotContain("LIVE", patternAdds);
        }

        [Fact]
        public void Column_AddsFrameSectionFrameAndSixDofFixity()
        {
            var driver = new FakeSafeOapiDriver();
            var input = MinimalInput(
                columns: new[] { (1500.0, 1500.0) },
                columnSizes: new[] { (400.0, 400.0) });

            var result = ExportOrchestrator.Run(driver, input);

            Assert.True(result.Success, result.Message);
            Assert.Equal(1, result.ColumnsExported);

            // One rectangular section created.
            var sectionCalls = driver.Calls.Where(c => c.Method == nameof(FakeSafeOapiDriver.SetFrameRectangleProp)).ToList();
            Assert.Single(sectionCalls);
            Assert.Equal("C400x400", (string)sectionCalls[0].Args[0]!);

            // Exactly one AddFrame.
            Assert.Equal(1, driver.Calls.Count(c => c.Method == nameof(FakeSafeOapiDriver.AddFrame)));

            // SetPointRestraint called once, with 6 DOF all true.
            var restCalls = driver.Calls.Where(c => c.Method == nameof(FakeSafeOapiDriver.SetPointRestraint)).ToList();
            Assert.Single(restCalls);
            var dof = (bool[])restCalls[0].Args[1]!;
            Assert.Equal(6, dof.Length);
            Assert.All(dof, v => Assert.True(v));

            // Insertion point set to Bottom Center (cardinal 2) for column.
            var ipCalls = driver.Calls.Where(c => c.Method == nameof(FakeSafeOapiDriver.SetFrameInsertionPoint)).ToList();
            Assert.Single(ipCalls);
            Assert.Equal(2, (int)ipCalls[0].Args[1]!);
        }

        [Fact]
        public void WallPolylineWithHint_AddsPerSegmentFrames()
        {
            var driver = new FakeSafeOapiDriver();
            // 3-point polyline → 2 segments.
            var poly = new[] { (0.0, 0.0), (1000.0, 0.0), (1000.0, 1000.0) };
            var input = MinimalInput(
                lines: new IReadOnlyList<(double, double)>[] { poly },
                lineHints: new (double, double)?[] { (200.0, 1500.0) });

            var result = ExportOrchestrator.Run(driver, input);

            Assert.True(result.Success, result.Message);
            Assert.Equal(2, result.FramesExported);
            Assert.Equal(2, driver.Calls.Count(c => c.Method == nameof(FakeSafeOapiDriver.AddFrame)));
            // Section name derives from hint.
            Assert.Contains(driver.Calls, c => c.Method == nameof(FakeSafeOapiDriver.SetFrameRectangleProp)
                                              && (string)c.Args[0]! == "B200x1500");
        }

        [Fact]
        public void DegenerateSlabWithTwoPoints_IsSkippedNotFailed()
        {
            var driver = new FakeSafeOapiDriver();
            var twoPtSlab = new List<(double X, double Y)> { (0, 0), (1, 1) };
            var input = MinimalInput(
                slabs: new IReadOnlyList<(double, double)>[] { twoPtSlab, Rect(0, 0, 1000, 1000) },
                slabColors: new[] { ((byte)255, (byte)0, (byte)0), ((byte)255, (byte)0, (byte)0) });

            var result = ExportOrchestrator.Run(driver, input);

            Assert.True(result.Success, result.Message);
            Assert.Equal(1, result.SlabsExported);
            Assert.Equal(1, result.Skipped);
        }

        [Fact]
        public void AddAreaFails_SkippedAndContinues()
        {
            var driver = new FakeSafeOapiDriver { ReturnOverrides = { [nameof(FakeSafeOapiDriver.AddArea)] = 1 } };
            var input = MinimalInput(
                slabs: new[] { Rect(0, 0, 1000, 1000), Rect(2000, 0, 3000, 1000) },
                slabColors: new[] { ((byte)255, (byte)0, (byte)0), ((byte)255, (byte)0, (byte)0) });

            var result = ExportOrchestrator.Run(driver, input);

            Assert.True(result.Success, result.Message); // export itself still succeeds to Save
            Assert.Equal(0, result.SlabsExported);
            Assert.Equal(2, result.Skipped);
            // Both slabs' AddArea attempted (we didn't abort on first failure).
            Assert.Equal(2, driver.Calls.Count(c => c.Method == nameof(FakeSafeOapiDriver.AddArea)));
        }

        [Fact]
        public void SetMaterialFails_FailsExportWithSpecificMessage()
        {
            var driver = new FakeSafeOapiDriver { ReturnOverrides = { [nameof(FakeSafeOapiDriver.SetMaterial)] = 7 } };
            var input = MinimalInput(
                slabs: new[] { Rect(0, 0, 1000, 1000) },
                slabColors: new[] { ((byte)255, (byte)0, (byte)0) });

            var result = ExportOrchestrator.Run(driver, input);

            Assert.False(result.Success);
            Assert.Contains("SetMaterial", result.Message);
            Assert.Contains("returned 7", result.Message);
        }

        [Fact]
        public void SaveFails_FailsExportWithSaveMessage()
        {
            var driver = new FakeSafeOapiDriver { ReturnOverrides = { [nameof(FakeSafeOapiDriver.SaveModel)] = 2 } };
            var input = MinimalInput(
                slabs: new[] { Rect(0, 0, 1000, 1000) },
                slabColors: new[] { ((byte)255, (byte)0, (byte)0) });

            var result = ExportOrchestrator.Run(driver, input);

            Assert.False(result.Success);
            Assert.Contains("File.Save returned 2", result.Message);
            // Slab itself was added before Save failed.
            Assert.Equal(1, result.SlabsExported);
        }

        [Fact]
        public void StartThrowsInvalidCastException_FailsWithGuidMismatchGuidance()
        {
            var driver = new FakeSafeOapiDriver
            {
                ThrowOverrides = { [nameof(FakeSafeOapiDriver.Start)] = new InvalidCastException("QueryInterface failed") }
            };
            var input = MinimalInput(
                slabs: new[] { Rect(0, 0, 1000, 1000) },
                slabColors: new[] { ((byte)255, (byte)0, (byte)0) });

            var result = ExportOrchestrator.Run(driver, input);

            Assert.False(result.Success);
            Assert.Contains("cOAPI interface", result.Message);
            Assert.Contains("RegisterSAFE.exe", result.Message);
        }

        [Fact]
        public void ColumnSizesMismatch_UsesDefault400x400()
        {
            // Engineer-facing invariant: if the reclassifier handed us
            // fewer column sizes than columns, the orchestrator defaults to
            // 400x400 rather than crashing with IndexOutOfRange. This
            // scenario is easy to reproduce when ColumnTypeOverrides is set
            // to a type that skips column-size capture.
            var driver = new FakeSafeOapiDriver();
            var input = MinimalInput(
                columns: new[] { (100.0, 100.0), (200.0, 200.0) },
                columnSizes: new[] { (300.0, 400.0) }); // one short

            var result = ExportOrchestrator.Run(driver, input);
            Assert.True(result.Success, result.Message);
            Assert.Equal(2, result.ColumnsExported);
            // Section C400x400 was created for the column with no hint.
            Assert.Contains(driver.Calls, c => c.Method == nameof(FakeSafeOapiDriver.SetFrameRectangleProp)
                                              && (string)c.Args[0]! == "C400x400");
        }

        [Fact]
        public void ExcludedSlabNotRepresented_LoadPatternSkipped()
        {
            // When the click-handler excludes every slab with SDL/LIVE, the
            // orchestrator sees colorSettings WITHOUT those loads in it —
            // so no SDL/LIVE pattern should be created. Simulate that here
            // by constructing the input that the UI's filter produces.
            var driver = new FakeSafeOapiDriver();
            driver.PreExistingLoadPatterns.Clear();
            var key = ((byte)99, (byte)99, (byte)99);

            // Remaining kept slab has a color with zero loads — patterns not needed.
            var input = MinimalInput(
                slabs: new[] { Rect(0, 0, 1000, 1000) },
                slabColors: new[] { key },
                colors: new Dictionary<(byte, byte, byte), SlabColorSettings>
                {
                    [key] = new SlabColorSettings { GradeCode = "C30", ThicknessMm = 250, SdlKPa = 0, LiveKPa = 0 },
                });
            var result = ExportOrchestrator.Run(driver, input);

            Assert.True(result.Success, result.Message);
            Assert.DoesNotContain(driver.Calls, c => c.Method == nameof(FakeSafeOapiDriver.AddLoadPattern));
        }

        [Fact]
        public void NullColorSettings_UsesDefaultsConsistently()
        {
            var driver = new FakeSafeOapiDriver();
            var input = MinimalInput(
                slabs: new[] { Rect(0, 0, 1000, 1000), Rect(2000, 0, 3000, 1000) },
                slabColors: new[] { ((byte)1, (byte)1, (byte)1), ((byte)2, (byte)2, (byte)2) },
                colors: null);

            var result = ExportOrchestrator.Run(driver, input);
            Assert.True(result.Success, result.Message);
            // Both slabs default to grade "C30" and thickness 250 → single
            // material, single slab prop.
            Assert.Equal(1, driver.Calls.Count(c => c.Method == nameof(FakeSafeOapiDriver.SetMaterial)));
            Assert.Equal(1, driver.Calls.Count(c => c.Method == nameof(FakeSafeOapiDriver.SetSlabProp)));
        }

        [Fact]
        public void ManyVertexSlab_AddsOnePointPerVertex()
        {
            // 27-point slab like the Regent PDF's main outline: every vertex
            // must produce one AddPoint call before the single AddArea.
            var driver = new FakeSafeOapiDriver();
            var vertices = new List<(double, double)>();
            const int N = 27;
            for (int i = 0; i < N; i++)
            {
                double theta = 2 * Math.PI * i / N;
                vertices.Add((5000 + 3000 * Math.Cos(theta), 5000 + 3000 * Math.Sin(theta)));
            }
            var input = MinimalInput(
                slabs: new[] { vertices },
                slabColors: new[] { ((byte)255, (byte)0, (byte)0) });

            var result = ExportOrchestrator.Run(driver, input);
            Assert.True(result.Success, result.Message);
            Assert.Equal(N, driver.Calls.Count(c => c.Method == nameof(FakeSafeOapiDriver.AddPoint)));
            Assert.Equal(1, driver.Calls.Count(c => c.Method == nameof(FakeSafeOapiDriver.AddArea)));
        }

        [Fact]
        public void ColumnAndSlab_BothAddedEvenIfCoordsCoincide()
        {
            // A column at a slab corner: both objects still get added. SAFE's
            // merge-tolerance handles node coincidence on the driver side; the
            // orchestrator must NOT dedup geometry.
            var driver = new FakeSafeOapiDriver();
            var input = MinimalInput(
                slabs: new[] { Rect(0, 0, 6000, 6000) },
                slabColors: new[] { ((byte)255, (byte)0, (byte)0) },
                columns: new[] { (0.0, 0.0) }, // slab corner
                columnSizes: new[] { (400.0, 400.0) });

            var result = ExportOrchestrator.Run(driver, input);
            Assert.True(result.Success, result.Message);
            Assert.Equal(1, result.SlabsExported);
            Assert.Equal(1, result.ColumnsExported);
            // Slab added 4 corner points + column added 2 (top/bottom).
            Assert.Equal(6, driver.Calls.Count(c => c.Method == nameof(FakeSafeOapiDriver.AddPoint)));
        }

        [Fact]
        public void MultipleDistinctLoadValues_EachGetsSeparateSetAreaLoadCall()
        {
            // Two slabs each with different SDL/LIVE values — should produce
            // 4 SetAreaLoadUniform calls (SDL+LIVE × 2) with the correct
            // kPa×1e-3 conversion per slab.
            var driver = new FakeSafeOapiDriver();
            driver.PreExistingLoadPatterns.Clear();
            var k1 = ((byte)10, (byte)10, (byte)10);
            var k2 = ((byte)20, (byte)20, (byte)20);
            var input = MinimalInput(
                slabs: new[] { Rect(0, 0, 1000, 1000), Rect(2000, 0, 3000, 1000) },
                slabColors: new[] { k1, k2 },
                colors: new Dictionary<(byte, byte, byte), SlabColorSettings>
                {
                    [k1] = new SlabColorSettings { GradeCode = "C30", ThicknessMm = 250, SdlKPa = 1.0, LiveKPa = 2.0 },
                    [k2] = new SlabColorSettings { GradeCode = "C30", ThicknessMm = 250, SdlKPa = 3.0, LiveKPa = 4.0 },
                });

            var result = ExportOrchestrator.Run(driver, input);

            Assert.True(result.Success, result.Message);
            Assert.Equal(4, result.LoadsApplied);
            var loadValues = driver.Calls.Where(c => c.Method == nameof(FakeSafeOapiDriver.SetAreaLoadUniform))
                                          .Select(c => (double)c.Args[2]!)
                                          .OrderBy(v => v).ToList();
            Assert.Equal(new[] { 0.001, 0.002, 0.003, 0.004 }, loadValues);
        }

        [Fact]
        public void AddPointFailsMidSlab_SkipsSlabAndContinues()
        {
            // Inject a point failure on the 3rd AddPoint call of the first slab;
            // orchestrator must bail on THAT slab, count it as skipped, then
            // successfully process the second slab.
            var driver = new FakePointFailingDriver(failOnCallNumber: 3);
            var input = MinimalInput(
                slabs: new IReadOnlyList<(double, double)>[]
                {
                    Rect(0, 0, 1000, 1000),    // will fail on its 3rd point
                    Rect(2000, 0, 3000, 1000),
                },
                slabColors: new[] { ((byte)1, (byte)1, (byte)1), ((byte)1, (byte)1, (byte)1) });

            var result = ExportOrchestrator.Run(driver, input);
            Assert.True(result.Success, result.Message); // overall export still succeeds
            Assert.Equal(1, result.SlabsExported);
            Assert.Equal(1, result.Skipped);
        }

        /// <summary>Specialised fake that fails AddPoint on a configured Nth call to simulate SAFE returning an error partway through a polygon.</summary>
        private sealed class FakePointFailingDriver : ISafeOapiDriver
        {
            private int _pointCalls;
            private readonly int _failOnCall;
            public FakePointFailingDriver(int failOnCallNumber) { _failOnCall = failOnCallNumber; }

            public int Start()                                       => 0;
            public int Unhide()                                      => 0;
            public int InitializeNewModel(bool imperial)             => 0;
            public int NewBlank()                                    => 0;
            public int SetMergeTol(double mm)                        => 0;
            public int SaveModel(string destFdbPath)                 => 0;
            public int SetMaterial(string n, string notes)           => 0;
            public int SetMPIsotropic(string n, double e, double u, double a) => 0;
            public int SetSlabProp(string n, string mat, double t)   => 0;
            public int SetFrameRectangleProp(string n, string mat, double d, double w) => 0;
            public IReadOnlyList<string> ListLoadPatternNames()      => Array.Empty<string>();
            public int AddLoadPattern(string n, SafeLoadPatternType t) => 0;

            public int AddPoint(double x, double y, double z, out string pointName)
            {
                _pointCalls++;
                if (_pointCalls == _failOnCall) { pointName = ""; return 1; }
                pointName = $"P{_pointCalls}";
                return 0;
            }

            public int AddArea(IReadOnlyList<string> ptNames, string prop, out string name)
            { name = "A1"; return 0; }
            public int AddFrame(string a, string b, string sec, out string name)
            { name = "F1"; return 0; }
            public int SetAreaLoadUniform(string n, string p, double v) => 0;
            public int SetPointRestraint(string n, bool[] d)         => 0;
            public int SetFrameInsertionPoint(string n, int c)       => 0;
            public int SetAreaEdgeConstraint(string n, bool e)       => 0;
            public int SetSlabModifiers(string n, double m, double b, double s) => 0;
            public int AddGridLines(IReadOnlyList<(string, bool, double)> g) => 0;
            public void Dispose() { }
        }

        [Fact]
        public void Imperial_CoordinatesAndPropertiesConvertedExactly()
        {
            // Synthetic test with known values — verifies EXACT conversion
            // factors, not just ranges. No PDF fixture needed.
            var driver = new FakeSafeOapiDriver();
            var input = MinimalInput(
                slabs: new[] { Rect(0, 0, 2540, 2540) }, // 2540mm = 100 in exactly
                slabColors: new[] { ((byte)255, (byte)0, (byte)0) },
                columns: new[] { (1270.0, 1270.0) },     // 1270mm = 50 in
                columnSizes: new[] { (254.0, 508.0) },    // 254mm=10in, 508mm=20in
                grade: "C30",
                thicknessMm: 254.0,                       // 254mm = 10 in exactly
                isImperial: true);

            var result = ExportOrchestrator.Run(driver, input);
            Assert.True(result.Success, result.Message);

            // InitializeNewModel called with imperial=true
            var initCall = driver.Calls.First(c => c.Method == nameof(FakeSafeOapiDriver.InitializeNewModel));
            Assert.Equal(true, initCall.Args[0]);

            // SetMergeTol: 1mm = 1/25.4 in ≈ 0.03937
            var mergeTolCall = driver.Calls.First(c => c.Method == nameof(FakeSafeOapiDriver.SetMergeTol));
            Assert.Equal(1.0 / 25.4, (double)mergeTolCall.Args[0]!, 6);

            // E: CSA C30 = 4500*sqrt(30) MPa, converted to ksi (÷6.895)
            var eCall = driver.Calls.First(c => c.Method == nameof(FakeSafeOapiDriver.SetMPIsotropic));
            double expectedE = 4500.0 * Math.Sqrt(30.0) / 6.895;
            Assert.Equal(expectedE, (double)eCall.Args[1]!, 2);
            // Poisson still 0.2 (dimensionless)
            Assert.Equal(0.2, (double)eCall.Args[2]!);
            // Thermal alpha: 5.5e-6 /°F
            Assert.Equal(5.5e-6, (double)eCall.Args[3]!, 10);

            // Slab thickness: 254mm → 10 in
            var slabCall = driver.Calls.First(c => c.Method == nameof(FakeSafeOapiDriver.SetSlabProp));
            Assert.Equal(10.0, (double)slabCall.Args[2]!, 6);

            // Slab corner: (2540mm, 2540mm) → (100 in, 100 in)
            var pointCalls = driver.Calls.Where(c => c.Method == nameof(FakeSafeOapiDriver.AddPoint)).ToList();
            // Third point should be (100, 100, 0)
            Assert.Equal(100.0, (double)pointCalls[2].Args[0]!, 6);
            Assert.Equal(100.0, (double)pointCalls[2].Args[1]!, 6);

            // Column at (1270mm,1270mm) → (50 in, 50 in)
            // Column points: top at z=0, bottom at z=-118.11 (3000mm/25.4)
            var colTopCall = pointCalls.First(c => Math.Abs((double)c.Args[0]! - 50.0) < 0.1 && (double)c.Args[2]! == 0.0);
            Assert.Equal(50.0, (double)colTopCall.Args[0]!, 1);
            var colBotCall = pointCalls.First(c => Math.Abs((double)c.Args[0]! - 50.0) < 0.1 && (double)c.Args[2]! < 0);
            Assert.Equal(-3000.0 / 25.4, (double)colBotCall.Args[2]!, 2);

            // Frame section: 254mm→250mm(Round10)→9.84in depth; 508mm→510mm(Round10)→20.08in width
            var frameCall = driver.Calls.First(c => c.Method == nameof(FakeSafeOapiDriver.SetFrameRectangleProp));
            double expectedDepth = Math.Round(508.0 / 10.0) * 10.0 / 25.4; // 510/25.4 = 20.08
            double expectedWidth = Math.Round(254.0 / 10.0) * 10.0 / 25.4; // 250/25.4 = 9.84
            Assert.Equal(expectedDepth, (double)frameCall.Args[2]!, 2);
            Assert.Equal(expectedWidth, (double)frameCall.Args[3]!, 2);
        }

        [Fact]
        public void LifecycleSequence_FollowsSpecifiedOrder()
        {
            // Full lifecycle ordering audit — everyone freaks out in different
            // ways when this is wrong, so assert it explicitly.
            var driver = new FakeSafeOapiDriver();
            var input = MinimalInput(
                slabs: new[] { Rect(0, 0, 1000, 1000) },
                slabColors: new[] { ((byte)255, (byte)0, (byte)0) });

            var result = ExportOrchestrator.Run(driver, input);
            Assert.True(result.Success, result.Message);

            var methods = driver.Calls.Select(c => c.Method).ToList();
            int idx(string m) => methods.IndexOf(m);
            Assert.True(idx(nameof(FakeSafeOapiDriver.Start)) >= 0);
            Assert.True(idx(nameof(FakeSafeOapiDriver.InitializeNewModel)) > idx(nameof(FakeSafeOapiDriver.Start)));
            Assert.True(idx(nameof(FakeSafeOapiDriver.NewBlank))           > idx(nameof(FakeSafeOapiDriver.InitializeNewModel)));
            Assert.True(idx(nameof(FakeSafeOapiDriver.SetMergeTol))        > idx(nameof(FakeSafeOapiDriver.NewBlank)));
            Assert.True(idx(nameof(FakeSafeOapiDriver.SetMaterial))        > idx(nameof(FakeSafeOapiDriver.SetMergeTol)));
            Assert.True(idx(nameof(FakeSafeOapiDriver.SetSlabProp))        > idx(nameof(FakeSafeOapiDriver.SetMaterial)));
            Assert.True(idx(nameof(FakeSafeOapiDriver.AddPoint))           > idx(nameof(FakeSafeOapiDriver.SetSlabProp)));
            Assert.True(idx(nameof(FakeSafeOapiDriver.AddArea))            > idx(nameof(FakeSafeOapiDriver.AddPoint)));
            Assert.True(idx(nameof(FakeSafeOapiDriver.SaveModel))          > idx(nameof(FakeSafeOapiDriver.AddArea)));
        }
    }
}
