#nullable enable
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe
{
    public class Sap2000ApiExporterTests
    {
        private static string? LocateSapDll()
        {
            string? dir = Path.GetDirectoryName(typeof(Sap2000ApiExporterTests).Assembly.Location);
            foreach (var c in new[]
            {
                Path.Combine(dir ?? ".", "SAP2000v1.dll"),
                Path.GetFullPath(Path.Combine(dir ?? ".", "..", "..", "..", "..", "..", "lib", "SAP2000", "SAP2000v1.dll")),
                Path.GetFullPath(Path.Combine(dir ?? ".", "..", "..", "..", "..", "lib", "SAP2000", "SAP2000v1.dll")),
            })
                if (File.Exists(c)) return c;
            return null;
        }

        [Fact]
        public void Sap2000V1Dll_TryLoad_ReturnsTrue()
        {
            string? dllPath = LocateSapDll();
            if (dllPath is null) return;

            var ctx = new AssemblyLoadContext($"sap-contract-{Guid.NewGuid():N}", isCollectible: true);
            try
            {
                Assembly asm = ctx.LoadFromAssemblyPath(dllPath);
                bool ok = SafeOapiTypes.TryLoad(asm, "SAP2000v1", out var types, out var issues);

                Assert.True(ok, "TryLoad should succeed for SAP2000v1.dll. Issues: " + string.Join("; ", issues));
                Assert.NotNull(types);
                Assert.Empty(issues);
            }
            finally { ctx.Unload(); }
        }

        [Fact]
        public void Sap2000V1Dll_DetectsApplicationStart3()
        {
            string? dllPath = LocateSapDll();
            if (dllPath is null) return;

            var ctx = new AssemblyLoadContext($"sap-start-{Guid.NewGuid():N}", isCollectible: true);
            try
            {
                Assembly asm = ctx.LoadFromAssemblyPath(dllPath);
                SafeOapiTypes.TryLoad(asm, "SAP2000v1", out var types, out _);
                Assert.NotNull(types);
                Assert.Equal(3, types!.ApplicationStartArity);
            }
            finally { ctx.Unload(); }
        }

        [Fact]
        public void Sap2000V1Dll_DetectsSetShellInsteadOfSetSlab()
        {
            string? dllPath = LocateSapDll();
            if (dllPath is null) return;

            var ctx = new AssemblyLoadContext($"sap-shell-{Guid.NewGuid():N}", isCollectible: true);
            try
            {
                Assembly asm = ctx.LoadFromAssemblyPath(dllPath);
                SafeOapiTypes.TryLoad(asm, "SAP2000v1", out var types, out _);
                Assert.NotNull(types);
                Assert.Equal("SetShell", types!.SlabMethodName);
                Assert.Equal(9, types.SlabMethodArity);
            }
            finally { ctx.Unload(); }
        }

        [Fact]
        public void CandidateProgIds_ContainsSapProgId()
        {
            Assert.Contains("CSI.SAP2000.API.SapObject", Sap2000ApiExporter.CandidateProgIds);
        }

        [Fact]
        public void Sap2000Export_UsesSharedOrchestrator_AgainstVirtualDriver()
        {
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
                DestFdbPath = Path.Combine(Path.GetTempPath(), "sap-test.sdb"),
                IsImperial = false,
            };

            var result = ExportOrchestrator.Run(driver, input);
            Assert.True(result.Success, result.Message);
            Assert.Equal(1, result.SlabsExported);
            Assert.Equal(1, result.ColumnsExported);
        }
    }
}
