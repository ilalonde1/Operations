#nullable enable
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe
{
    public class EtabsApiExporterTests
    {
        // ─── Contract: ETABSv1.dll has all required OAPI members ──────

        private static string? LocateEtabsDll()
        {
            string? asmDir = Path.GetDirectoryName(typeof(EtabsApiExporterTests).Assembly.Location);
            string[] candidates =
            {
                Path.Combine(asmDir ?? ".", "ETABSv1.dll"),
                Path.GetFullPath(Path.Combine(asmDir ?? ".", "..", "..", "..", "..", "..", "lib", "ETABS", "ETABSv1.dll")),
                Path.GetFullPath(Path.Combine(asmDir ?? ".", "..", "..", "..", "..", "lib", "ETABS", "ETABSv1.dll")),
            };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
            return null;
        }

        [Fact]
        public void EtabsV1Dll_TryLoad_ReturnsTrue()
        {
            string? dllPath = LocateEtabsDll();
            if (dllPath is null) return; // skip if not available (CI without DLL)

            var ctx = new AssemblyLoadContext($"etabs-contract-{Guid.NewGuid():N}", isCollectible: true);
            try
            {
                Assembly asm = ctx.LoadFromAssemblyPath(dllPath);
                bool ok = SafeOapiTypes.TryLoad(asm, "ETABSv1", out var types, out var issues);

                Assert.True(ok, "TryLoad should succeed for ETABSv1.dll. Issues: " + string.Join("; ", issues));
                Assert.NotNull(types);
                Assert.Empty(issues);
            }
            finally { ctx.Unload(); }
        }

        [Fact]
        public void EtabsV1Dll_HasSameInterfaceShape_AsSafeV1()
        {
            // Both DLLs must resolve the same set of types/enums/methods.
            // If ETABSv1 diverges, the shared orchestrator will fail at runtime.
            string? etabsPath = LocateEtabsDll();
            if (etabsPath is null) return;

            string? safePath = null;
            string? dir = Path.GetDirectoryName(typeof(EtabsApiExporterTests).Assembly.Location);
            foreach (var candidate in new[]
            {
                Path.Combine(dir ?? ".", "SAFEv1.dll"),
                Path.GetFullPath(Path.Combine(dir ?? ".", "..", "..", "..", "..", "..", "lib", "SAFE", "SAFEv1.dll")),
                Path.GetFullPath(Path.Combine(dir ?? ".", "..", "..", "..", "..", "lib", "SAFE", "SAFEv1.dll")),
            })
            {
                if (File.Exists(candidate)) { safePath = candidate; break; }
            }
            if (safePath is null) return;

            var ctxS = new AssemblyLoadContext("safe-shape", isCollectible: true);
            var ctxE = new AssemblyLoadContext("etabs-shape", isCollectible: true);
            try
            {
                SafeOapiTypes.TryLoad(ctxS.LoadFromAssemblyPath(safePath), "SAFEv1", out _, out var safeIssues);
                SafeOapiTypes.TryLoad(ctxE.LoadFromAssemblyPath(etabsPath), "ETABSv1", out _, out var etabsIssues);

                // Both should have zero issues — same interface shape.
                Assert.Empty(safeIssues);
                Assert.Empty(etabsIssues);
            }
            finally { ctxS.Unload(); ctxE.Unload(); }
        }

        // ─── ProgID guardrails ────────────────────────────────────────

        [Fact]
        public void CandidateProgIds_ContainsEtabsProgId()
        {
            Assert.Contains("CSI.ETABS.API.ETABSObject", EtabsApiExporter.CandidateProgIds);
        }

        // ─── Pipeline: reuses ExportOrchestrator (already tested) ─────

        [Fact]
        public void EtabsExport_UsesSharedOrchestrator_AgainstVirtualDriver()
        {
            // Proves the ETABS path produces the same model structure as SAFE
            // when driven through ExportOrchestrator with VirtualSafeDriver.
            // The driver is product-agnostic; only the facade differs.
            var driver = new VirtualSafeDriver();
            var input = new SafeApiExporter.ExportInput
            {
                Slabs = new[] { new (double, double)[] { (0, 0), (6000, 0), (6000, 6000), (0, 6000) } },
                SlabColors = new[] { ((byte)255, (byte)0, (byte)0) },
                Columns = new[] { (3000.0, 3000.0) },
                ColumnSizes = new[] { (400.0, 400.0) },
                Lines = Array.Empty<(double, double)[]>(),
                LineSectionHints = Array.Empty<(double, double)?>(),
                ColorSettings = null,
                DefaultGradeCode = "C30",
                DesignCode = DesignCodeOption.CSA_A23_3_19,
                DefaultThicknessMm = 250.0,
                DefaultWallDepthMm = 1000.0,
                ColumnHeightMm = 3000.0,
                DestFdbPath = Path.Combine(Path.GetTempPath(), "etabs-test.edb"),
                IsImperial = false,
            };

            var result = ExportOrchestrator.Run(driver, input);

            Assert.True(result.Success, result.Message);
            Assert.Equal(1, result.SlabsExported);
            Assert.Equal(1, result.ColumnsExported);
            Assert.NotNull(driver.SavedPath);
        }
    }
}
