#nullable enable
using System;
using System.Linq;
using System.Reflection;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe
{
    /// <summary>
    /// Reflection-based surface contract for <see cref="SafeApiExporter"/>.
    /// Locks the record shapes and method signatures the UI depends on:
    ///   - PdfToSafeWindow.xaml.cs constructs <c>ExportInput</c> by field name
    ///   - FirmDefaultsDialog calls <c>CheckAllInstalledCompatibility</c> and reads <c>CompatibilityReport</c>
    ///   - Both read <c>DefaultPreferredVersion</c>
    ///
    /// Any rename, removal, or type change in these surfaces will fail this
    /// test INSTEAD of breaking silently when the UI compiles.
    /// </summary>
    public class SafeApiExporterSurfaceTests
    {
        private static readonly Type _exporter = typeof(SafeApiExporter);
        private static readonly Type _exportInput  = typeof(SafeApiExporter.ExportInput);
        private static readonly Type _exportResult = typeof(SafeApiExporter.ExportResult);
        private static readonly Type _safeInstall  = typeof(SafeApiExporter.SafeInstall);
        private static readonly Type _compatReport = typeof(SafeApiExporter.CompatibilityReport);

        // ─── ExportInput shape ────────────────────────────────────────────

        [Theory]
        [InlineData("Slabs")]
        [InlineData("SlabColors")]
        [InlineData("Columns")]
        [InlineData("ColumnSizes")]
        [InlineData("Lines")]
        [InlineData("LineSectionHints")]
        [InlineData("ColorSettings")]
        [InlineData("DefaultGradeCode")]
        [InlineData("DefaultThicknessMm")]
        [InlineData("DefaultWallDepthMm")]
        [InlineData("ColumnHeightMm")]
        [InlineData("DestFdbPath")]
        [InlineData("SafeExePathOverride")]
        public void ExportInput_HasProperty(string name)
        {
            Assert.NotNull(_exportInput.GetProperty(name));
        }

        [Fact]
        public void ExportInput_AllRequiredPropsHaveInitSetter()
        {
            // init-only is what makes the record-style construction work from
            // the UI. If someone converts a property to a plain setter or
            // removes its init accessor, the object initializer on the UI
            // side would silently allow mutation — we want that to fail loudly.
            foreach (var pi in _exportInput.GetProperties())
            {
                var setter = pi.GetSetMethod(nonPublic: true);
                Assert.NotNull(setter);
                // init-only setters have the IsExternalInit modifier on the return type
                var modreqs = setter!.ReturnParameter.GetRequiredCustomModifiers();
                Assert.Contains(modreqs, m => m.Name == "IsExternalInit");
            }
        }

        // ─── ExportResult shape ───────────────────────────────────────────

        [Theory]
        [InlineData("Success")]
        [InlineData("Message")]
        [InlineData("SavedPath")]
        [InlineData("SlabsExported")]
        [InlineData("ColumnsExported")]
        [InlineData("FramesExported")]
        [InlineData("LoadsApplied")]
        [InlineData("Skipped")]
        public void ExportResult_HasProperty(string name)
        {
            Assert.NotNull(_exportResult.GetProperty(name));
        }

        // ─── SafeInstall + CompatibilityReport (used by Firm Defaults dialog) ─

        [Theory]
        [InlineData("Version")]
        [InlineData("ExePath")]
        [InlineData("FolderName")]
        public void SafeInstall_HasProperty(string name)
        {
            Assert.NotNull(_safeInstall.GetProperty(name));
        }

        [Theory]
        [InlineData("Install")]
        [InlineData("IsCompatible")]
        [InlineData("Issues")]
        public void CompatibilityReport_HasProperty(string name)
        {
            Assert.NotNull(_compatReport.GetProperty(name));
        }

        // ─── Static entry points ──────────────────────────────────────────

        [Fact]
        public void SafeApiExporter_HasExportFullModelAsync()
        {
            var mi = _exporter.GetMethod("ExportFullModelAsync",
                BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(mi);
            Assert.Single(mi!.GetParameters());
            Assert.Equal(typeof(SafeApiExporter.ExportInput), mi.GetParameters()[0].ParameterType);
        }

        [Fact]
        public void SafeApiExporter_HasCheckAllInstalledCompatibility()
        {
            var mi = _exporter.GetMethod("CheckAllInstalledCompatibility",
                BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(mi);
            Assert.Empty(mi!.GetParameters());
        }

        [Fact]
        public void SafeApiExporter_HasEnumerateSafeInstalls()
        {
            var mi = _exporter.GetMethod("EnumerateSafeInstalls",
                BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(mi);
            Assert.Empty(mi!.GetParameters());
        }

        [Fact]
        public void SafeApiExporter_DefaultPreferredVersion_IsTwentyTwo()
        {
            // This constant flows into the Firm Defaults dialog's "(default)"
            // marker. Changing it is a deliberate decision; the test locks the
            // current value so it can't drift accidentally.
            var field = _exporter.GetField("DefaultPreferredVersion",
                BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(field);
            Assert.Equal(22, (int)field!.GetRawConstantValue()!);
        }

        // ─── Discovery behaviour (no live SAFE required) ──────────────────

        [Fact]
        public void EnumerateSafeInstalls_ReturnsCollection_OnAnyMachine()
        {
            // Returns an enumerable regardless of whether SAFE is installed.
            // Must not throw. (Tests run on boxes with no SAFE — CI, dev-only.)
            var installs = SafeApiExporter.EnumerateSafeInstalls();
            Assert.NotNull(installs);
            _ = installs.ToList(); // force enumeration
        }

        [Fact]
        public void EnumerateSafeInstalls_ResultsAreSortedNewestFirst()
        {
            var installs = SafeApiExporter.EnumerateSafeInstalls().ToList();
            for (int i = 1; i < installs.Count; i++)
                Assert.True(installs[i - 1].Version >= installs[i].Version,
                    $"Install list is not newest-first at index {i}");
        }
    }
}
