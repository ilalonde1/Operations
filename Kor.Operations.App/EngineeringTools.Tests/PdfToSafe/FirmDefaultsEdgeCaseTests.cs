#nullable enable
using System;
using System.IO;
using System.Text.Json;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Tests.PdfToSafe
{
    /// <summary>
    /// xUnit collection marker for any test class that manipulates the real
    /// %AppData%\KorOperations\pdftosafe_defaults.json file. Members of this
    /// collection run serially so their backup-swap-restore dances don't
    /// race each other or the real file an engineer has on disk.
    /// </summary>
    [CollectionDefinition(nameof(FirmDefaultsFileCollection), DisableParallelization = true)]
    public class FirmDefaultsFileCollection { }

    /// <summary>
    /// Edge-case coverage for <see cref="FirmDefaults"/>'s Load/Save/ApplyTo.
    /// The happy-path roundtrip already exists in <c>FirmDefaultsTests</c>; these
    /// tests target the failure modes that matter to engineers whose machines
    /// have stale / partial / corrupt defaults files.
    /// </summary>
    [Collection(nameof(FirmDefaultsFileCollection))]
    public class FirmDefaultsEdgeCaseTests
    {
        /// <summary>
        /// Runs <paramref name="action"/> against a defaults file in a temp folder of this test's own.
        ///
        /// It used to move the REAL file aside and put it back — which meant every test in here
        /// operated on %APPDATA%\KorOperations, and one of them deleted that folder recursively to
        /// prove Save() recreates it. That folder holds the running application's log, so the test
        /// failed whenever the app was open and destroyed real data whenever it succeeded. Tests do
        /// not touch a live application's data directory.
        /// </summary>
        private static void WithIsolatedDefaultsFile(Action action)
        {
            string root = Path.Combine(Path.GetTempPath(), "KorOpsTests", Guid.NewGuid().ToString("N"));
            FirmDefaults.PathOverride = Path.Combine(root, "KorOperations", "pdftosafe_defaults.json");
            try { action(); }
            finally
            {
                FirmDefaults.PathOverride = null;
                try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { /* a temp folder */ }
            }
        }

        // ─── Load robustness ──────────────────────────────────────────────

        [Fact]
        public void Load_MalformedJson_ReturnsShippedDefaults()
        {
            WithIsolatedDefaultsFile(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FirmDefaults.DefaultPath)!);
                File.WriteAllText(FirmDefaults.DefaultPath, "{ this is not json");

                // Must not throw; must fall back to shipped defaults.
                var d = FirmDefaults.Load();
                Assert.Equal("C30", d.DefaultGradeCode);
                Assert.Equal(250.0, d.DefaultSlabThicknessMm);
            });
        }

        [Fact]
        public void Load_EmptyFile_ReturnsShippedDefaults()
        {
            WithIsolatedDefaultsFile(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FirmDefaults.DefaultPath)!);
                File.WriteAllText(FirmDefaults.DefaultPath, "");
                var d = FirmDefaults.Load();
                Assert.Equal("C30", d.DefaultGradeCode);
            });
        }

        [Fact]
        public void Load_JsonWithExtraFields_IgnoresThem()
        {
            WithIsolatedDefaultsFile(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FirmDefaults.DefaultPath)!);
                File.WriteAllText(FirmDefaults.DefaultPath,
                    """{"DefaultGradeCode":"C40","FutureField":"ignored","AnotherExtra":123}""");

                var d = FirmDefaults.Load();
                Assert.Equal("C40", d.DefaultGradeCode);
            });
        }

        [Fact]
        public void Load_PartialJson_MissingFieldsUseDefaults()
        {
            WithIsolatedDefaultsFile(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FirmDefaults.DefaultPath)!);
                // Only sets grade; everything else must keep shipped defaults.
                File.WriteAllText(FirmDefaults.DefaultPath, """{"DefaultGradeCode":"C35"}""");

                var d = FirmDefaults.Load();
                Assert.Equal("C35", d.DefaultGradeCode);
                Assert.Equal(250.0, d.DefaultSlabThicknessMm);
                Assert.Equal(1.5, d.DefaultSdlKPa);
            });
        }

        [Fact]
        public void Load_JsonWithWrongFieldTypes_FallsBackGracefully()
        {
            WithIsolatedDefaultsFile(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FirmDefaults.DefaultPath)!);
                // Number where string expected → System.Text.Json throws; Load
                // must catch and return defaults (not crash the app on startup).
                File.WriteAllText(FirmDefaults.DefaultPath, """{"DefaultGradeCode":42}""");

                var d = FirmDefaults.Load();
                Assert.Equal("C30", d.DefaultGradeCode);
            });
        }

        // ─── Save robustness ──────────────────────────────────────────────

        [Fact]
        public void Save_CreatesParentDirectoryIfMissing()
        {
            WithIsolatedDefaultsFile(() =>
            {
                // Force-delete the KorOperations parent folder as well so we
                // can verify Save recreates it rather than silently returning
                // false when it doesn't exist.
                string parent = Path.GetDirectoryName(FirmDefaults.DefaultPath)!;
                if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);

                var d = new FirmDefaults { DefaultGradeCode = "C45" };
                Assert.True(d.Save());
                Assert.True(File.Exists(FirmDefaults.DefaultPath));
            });
        }

        [Fact]
        public void Save_ProducesValidJson_WithExpectedFieldNames()
        {
            WithIsolatedDefaultsFile(() =>
            {
                var d = new FirmDefaults { DefaultGradeCode = "C42", DefaultSlabThicknessMm = 275 };
                Assert.True(d.Save());

                string content = File.ReadAllText(FirmDefaults.DefaultPath);
                using var doc = JsonDocument.Parse(content);
                Assert.True(doc.RootElement.TryGetProperty("DefaultGradeCode", out var g));
                Assert.Equal("C42", g.GetString());
                Assert.True(doc.RootElement.TryGetProperty("DefaultSlabThicknessMm", out var t));
                Assert.Equal(275.0, t.GetDouble());
            });
        }

        // ─── ApplyTo robustness ───────────────────────────────────────────

        [Fact]
        public void ApplyTo_UnknownDesignCode_LeavesTargetUntouched()
        {
            var d = new FirmDefaults { DefaultDesignCode = "NOT_A_CODE_EVER" };
            var es = new ExportSettings { DesignCode = DesignCodeOption.ACI_318_19 };
            d.ApplyTo(es);
            // Invalid code ⇒ target DesignCode preserved (not overwritten to None
            // or CSA just because the string didn't match).
            Assert.Equal(DesignCodeOption.ACI_318_19, es.DesignCode);
        }

        [Fact]
        public void ApplyTo_ZeroMeshSize_LeavesTargetMeshUntouched()
        {
            var d = new FirmDefaults { DefaultMeshSizeMm = 0 };
            var es = new ExportSettings { MeshSizeMm = 500 };
            d.ApplyTo(es);
            Assert.Equal(500, es.MeshSizeMm);
        }

        [Fact]
        public void ApplyTo_NullTarget_DoesNotThrow()
        {
            var d = new FirmDefaults();
            // Defensive coding: UI may hand in null during transient edit
            // state. Must be a no-op, not a crash.
            var ex = Record.Exception(() => d.ApplyTo(null!));
            Assert.Null(ex);
        }

        // ─── DefaultPath stability ────────────────────────────────────────

        [Fact]
        public void DefaultPath_ReferencesUserAppDataKorOperations()
        {
            // Lock the location so engineers don't find their defaults have
            // silently moved after an update.
            string path = FirmDefaults.DefaultPath;
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            Assert.StartsWith(appData, path);
            Assert.Contains("KorOperations", path);
            Assert.EndsWith(".json", path);
        }

        // ─── SafeExePath persistence (added for multi-version SAFE feature) ─

        [Fact]
        public void SafeExePath_RoundtripsThroughSave()
        {
            WithIsolatedDefaultsFile(() =>
            {
                var d = new FirmDefaults
                {
                    SafeExePath = @"C:\Program Files\Computers and Structures\SAFE 22\SAFE.exe",
                };
                Assert.True(d.Save());
                var reloaded = FirmDefaults.Load();
                Assert.Equal(d.SafeExePath, reloaded.SafeExePath);
            });
        }
    }
}
