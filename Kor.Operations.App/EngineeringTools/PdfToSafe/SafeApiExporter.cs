#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// SAFE OAPI exporter — public facade. The actual work is split into:
    ///   - <see cref="ISafeOapiDriver"/> — contract for talking to SAFE
    ///   - <see cref="RealSafeOapiDriver"/> — reflection + COM implementation
    ///   - <see cref="ExportOrchestrator"/> — pure orchestration logic
    ///   - <see cref="SafeOapiTypes"/> — interop type/method resolution
    ///
    /// This class only handles install discovery, compatibility reporting,
    /// STA threading, timeout, and lifecycle. Everything that touches a
    /// live SAFE instance goes through the driver abstraction so it can be
    /// swapped for a fake in unit tests.
    /// </summary>
    internal static class SafeApiExporter
    {
        private static readonly TimeSpan ExportTimeout = TimeSpan.FromMinutes(3);

        /// <summary>Default SAFE major version picked when no override is set and the user's preferred version is installed.</summary>
        public const int DefaultPreferredVersion = 22;

        /// <summary>Known SAFE COM ProgIDs. Tried in order; first that resolves wins.</summary>
        internal static readonly string[] CandidateProgIds =
        {
            "CSI.SAFE.API.ETABSObject",
            "CSI.SAFE.API.SafeObject",
        };

        public sealed record ExportResult(
            bool Success,
            string Message,
            string? SavedPath,
            int SlabsExported,
            int ColumnsExported,
            int FramesExported,
            int LoadsApplied,
            int Skipped,
            int OpeningsCreated = 0);

        public sealed class ExportInput
        {
            public required IReadOnlyList<IReadOnlyList<(double X, double Y)>> Slabs { get; init; }
            public required IReadOnlyList<(byte R, byte G, byte B)> SlabColors { get; init; }
            public required IReadOnlyList<(double X, double Y)> Columns { get; init; }
            public required IReadOnlyList<(double WidthMm, double DepthMm)> ColumnSizes { get; init; }
            public required IReadOnlyList<IReadOnlyList<(double X, double Y)>> Lines { get; init; }
            public required IReadOnlyList<(double WidthMm, double DepthMm)?> LineSectionHints { get; init; }
            public IReadOnlyDictionary<(byte R, byte G, byte B), SlabColorSettings>? ColorSettings { get; init; }

            /// <summary>Per-slab thickness from text annotations (e.g., "S-250" label near the slab). Parallel to Slabs. Null entry = no annotation → fall back to color settings.</summary>
            public IReadOnlyList<double?>? AnnotatedSlabThicknesses { get; init; }
            /// <summary>Per-line section from text annotations (e.g., "B300x600"). Parallel to Lines. Null entry = fall back to hint or default.</summary>
            public IReadOnlyList<(double WidthMm, double DepthMm)?>? AnnotatedLineSections { get; init; }
            /// <summary>Per-column section from text annotations (e.g., "C500x500"). Parallel to Columns. Null entry = fall back to bbox.</summary>
            public IReadOnlyList<(double WidthMm, double DepthMm)?>? AnnotatedColumnSections { get; init; }
            /// <summary>Drop panel candidate polygons detected during PDF extraction. Each polygon is sized relative to a parent slab + nearby column.</summary>
            public IReadOnlyList<IReadOnlyList<(double X, double Y)>>? DropPanelCandidates { get; init; }
            /// <summary>Multiplier applied to parent slab thickness for drop panels (default 1.5 = 50% thicker).</summary>
            public double DropPanelThicknessMultiplier { get; init; } = 1.5;
            /// <summary>Slab stiffness modifiers (CSA A23.3 cracked = 0.25, uncracked = 1.0). Applied to every slab property via SetModifiers.</summary>
            public double SlabMembraneModifier { get; init; } = 1.0;
            public double SlabBendingModifier { get; init; } = 1.0;
            public double SlabShearModifier { get; init; } = 1.0;
            public required string DefaultGradeCode { get; init; }
            public required DesignCodeOption DesignCode { get; init; }
            public required double DefaultThicknessMm { get; init; }
            public required double DefaultWallDepthMm { get; init; }
            public required double ColumnHeightMm { get; init; }
            public required string DestFdbPath { get; init; }
            /// <summary>When true, SAFE model is created in kip-in-F units; all coordinates/properties converted from the internal metric representation at the driver boundary.</summary>
            public bool IsImperial { get; init; }
            public string? SafeExePathOverride { get; init; }
            /// <summary>
            /// When true, the orchestrator runs <see cref="WallOpeningDetector"/>
            /// against (Lines, LineSectionHints, Slabs) after slab/wall emission
            /// and, for each detected opening, creates an area on the parent
            /// slab and flags it as an opening via cAreaObj.SetOpening. Mirrors
            /// the AUTO_GEN openings in the F2K path.
            /// </summary>
            public bool AutoGenerateOpeningsFromWalls { get; init; } = true;
        }

        public sealed record SafeInstall(int Version, string ExePath, string FolderName);

        public sealed record CompatibilityReport(
            SafeInstall Install,
            bool IsCompatible,
            IReadOnlyList<string> Issues);

        // ═══════════════════════════════════════════════════════════════════
        //                          EXPORT ENTRY POINT
        // ═══════════════════════════════════════════════════════════════════

        public static async Task<ExportResult> ExportFullModelAsync(ExportInput input)
        {
            string? chosenExe = ResolveSafeExePath(input.SafeExePathOverride, out var discovery);
            if (chosenExe is null)
                return Failed("No usable SAFE install found. " + string.Join(" | ", discovery));

            var tcs = new TaskCompletionSource<ExportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try { tcs.TrySetResult(RunExportOnStaThread(input, chosenExe)); }
                catch (Exception ex) { tcs.TrySetResult(Failed($"Thread exception: {ex.GetType().Name}: {ex.Message}")); }
            })
            { IsBackground = true, Name = "SAFE-OAPI-STA-Export" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            using var timeoutCts = new CancellationTokenSource();
            var timeoutTask = Task.Delay(ExportTimeout, timeoutCts.Token);
            var winner = await Task.WhenAny(tcs.Task, timeoutTask).ConfigureAwait(false);
            if (winner != tcs.Task)
                return Failed(
                    $"Export timed out after {ExportTimeout.TotalMinutes:0} minutes — SAFE likely stuck on a license dialog. Dismiss any dialogs and retry.");

            timeoutCts.Cancel();
            return await tcs.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Runs on the STA thread: construct the real driver (which loads
        /// SAFEv1.dll, preflights, and activates the COM server), delegate
        /// to the orchestrator, and dispose the driver regardless of outcome.
        /// </summary>
        private static ExportResult RunExportOnStaThread(ExportInput input, string chosenExe)
        {
            string safeDllPath = Path.Combine(Path.GetDirectoryName(chosenExe)!, "SAFEv1.dll");
            if (!File.Exists(safeDllPath))
                return Failed($"SAFEv1.dll missing at '{safeDllPath}'.");

            Assembly safeAsm;
            try { safeAsm = Assembly.LoadFrom(safeDllPath); }
            catch (Exception ex) { return Failed($"Assembly.LoadFrom failed: {ex.Message}"); }

            if (!SafeOapiTypes.TryLoad(safeAsm, "SAFEv1", out var types, out var compatIssues))
                return Failed($"SAFE compatibility check failed. Missing: {string.Join("; ", compatIssues)}");

            // COM activation — auto-register if needed (one-time UAC prompt per machine).
            bool hasOverride = !string.IsNullOrWhiteSpace(input.SafeExePathOverride);
            if (!CsiComRegistration.EnsureRegistered(chosenExe, CandidateProgIds, hasOverride, out string? regError))
                return Failed(regError ?? "SAFE COM registration failed.");

            Type? safeComType = null;
            string? resolvedProgId = null;
            foreach (string candidate in CandidateProgIds)
            {
                var t = Type.GetTypeFromProgID(candidate);
                if (t is not null) { safeComType = t; resolvedProgId = candidate; break; }
            }
            if (safeComType is null)
                return Failed($"No SAFE COM ProgID registered after auto-registration attempt.");

            object? safe;
            try { safe = Activator.CreateInstance(safeComType); }
            catch (Exception ex) { return Failed($"Activator.CreateInstance('{resolvedProgId}') failed: {ex.Message}"); }
            if (safe is null) return Failed("Activator.CreateInstance returned null.");

            using var driver = new ReflectionCsiOapiDriver(types!, safe);
            return ExportOrchestrator.Run(driver, input);
        }

        // ═══════════════════════════════════════════════════════════════════
        //                       DISCOVERY + COMPATIBILITY
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Picks a SAFE install. Every candidate must have SAFE.exe AND
        /// SAFEv1.dll in the same folder. Order of preference:
        ///   1. User override (Firm Defaults)
        ///   2. Preferred version (<see cref="DefaultPreferredVersion"/>)
        ///   3. Newest installed version
        /// </summary>
        private static string? ResolveSafeExePath(string? overridePath, out List<string> log)
        {
            log = new List<string>();
            string? cleaned = overridePath?.Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                if (!File.Exists(cleaned))
                    log.Add($"override '{cleaned}' does not exist");
                else if (!File.Exists(Path.Combine(Path.GetDirectoryName(cleaned)!, "SAFEv1.dll")))
                    log.Add($"override '{cleaned}' has no SAFEv1.dll beside it");
                else
                    return cleaned;
            }

            var installs = EnumerateSafeInstalls().ToList();
            if (installs.Count == 0) { log.Add("no SAFE installs found under Program Files\\Computers and Structures"); return null; }

            var preferred = installs.FirstOrDefault(i => i.Version == DefaultPreferredVersion);
            if (preferred is not null) return preferred.ExePath;
            log.Add($"preferred SAFE {DefaultPreferredVersion} not installed; falling back to newest");
            return installs[0].ExePath; // newest-first
        }

        /// <summary>
        /// Enumerates every SAFE install under 64-bit Program Files\Computers
        /// and Structures\SAFE*, newest-first by folder-name version. Skips
        /// Program Files (x86) — SAFE 2016 (the one 32-bit install we know of)
        /// cannot be COM-activated in-process from a 64-bit host. Folders with
        /// a parsed version that looks like a year (≥100) are also excluded.
        /// </summary>
        public static IEnumerable<SafeInstall> EnumerateSafeInstalls()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (string.IsNullOrEmpty(root)) return Array.Empty<SafeInstall>();
            string csiRoot = Path.Combine(root, "Computers and Structures");
            if (!Directory.Exists(csiRoot)) return Array.Empty<SafeInstall>();

            var found = new List<SafeInstall>();
            foreach (string dir in Directory.EnumerateDirectories(csiRoot, "SAFE*"))
            {
                string candidate = Path.Combine(dir, "SAFE.exe");
                if (!File.Exists(candidate)) continue;
                string folder = Path.GetFileName(dir) ?? "";
                int major = ParseMajorVersion(folder);
                if (major < 20 || major >= 100) continue;
                found.Add(new SafeInstall(major, candidate, folder));
            }
            found.Sort((a, b) => b.Version.CompareTo(a.Version));
            return found;
        }

        private static int ParseMajorVersion(string folderName)
        {
            var digits = new System.Text.StringBuilder();
            foreach (char c in folderName)
            {
                if (char.IsDigit(c)) digits.Append(c);
                else if (digits.Length > 0) break;
            }
            return (digits.Length > 0 && int.TryParse(digits.ToString(), out int v)) ? v : 0;
        }

        /// <summary>
        /// Runs the preflight compatibility check against every 64-bit SAFE
        /// install. Each DLL is loaded in its own collectible
        /// AssemblyLoadContext so identical assembly identities don't shadow
        /// one another, and so we can unload after probing.
        /// </summary>
        public static List<CompatibilityReport> CheckAllInstalledCompatibility()
        {
            var reports = new List<CompatibilityReport>();
            foreach (var install in EnumerateSafeInstalls())
                reports.Add(CheckCompatibility(install));
            return reports;
        }

        public static CompatibilityReport CheckCompatibility(SafeInstall install)
        {
            string dll = Path.Combine(Path.GetDirectoryName(install.ExePath)!, "SAFEv1.dll");
            if (!File.Exists(dll))
                return new CompatibilityReport(install, false, new[] { $"SAFEv1.dll missing next to {install.ExePath}" });

            var ctx = new AssemblyLoadContext($"SAFE-preflight-{install.Version}-{Guid.NewGuid():N}", isCollectible: true);
            try
            {
                Assembly asm;
                try { asm = ctx.LoadFromAssemblyPath(dll); }
                catch (Exception ex) { return new CompatibilityReport(install, false, new[] { $"Assembly load failed: {ex.Message}" }); }

                if (!SafeOapiTypes.TryLoad(asm, out _, out var issues))
                    return new CompatibilityReport(install, false, issues);
                return new CompatibilityReport(install, true, Array.Empty<string>());
            }
            finally
            {
                try { ctx.Unload(); } catch { /* best-effort */ }
            }
        }

        private static ExportResult Failed(string message)
            => new ExportResult(false, message, null, 0, 0, 0, 0, 0);
    }
}
