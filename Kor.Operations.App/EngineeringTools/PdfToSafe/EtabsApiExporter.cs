#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// ETABS OAPI exporter — same architecture as <see cref="SafeApiExporter"/>
    /// but targets CSI ETABS instead of CSI SAFE. The OAPI surface is identical
    /// (same interface names, same method signatures, different namespace prefix
    /// and ProgID). The <see cref="ExportOrchestrator"/> is reused as-is.
    /// </summary>
    internal static class EtabsApiExporter
    {
        private static readonly TimeSpan ExportTimeout = TimeSpan.FromMinutes(3);
        public const int DefaultPreferredVersion = 22;

        /// <summary>Known ETABS COM ProgIDs. Tried in order; first one that resolves wins.</summary>
        internal static readonly string[] CandidateProgIds =
        {
            "CSI.ETABS.API.ETABSObject",
        };

        public sealed record EtabsInstall(int Version, string ExePath, string FolderName);

        public sealed record CompatibilityReport(
            EtabsInstall Install,
            bool IsCompatible,
            IReadOnlyList<string> Issues);

        // ═══════════════════════════════════════════════════════════════════

        public static async Task<SafeApiExporter.ExportResult> ExportFullModelAsync(SafeApiExporter.ExportInput input)
        {
            string? chosenExe = ResolveEtabsExePath(input.SafeExePathOverride, out var discovery);
            if (chosenExe is null)
                return Failed("No usable ETABS install found. " + string.Join(" | ", discovery));

            var tcs = new TaskCompletionSource<SafeApiExporter.ExportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try { tcs.TrySetResult(RunExportOnStaThread(input, chosenExe)); }
                catch (Exception ex) { tcs.TrySetResult(Failed($"Thread exception: {ex.GetType().Name}: {ex.Message}")); }
            })
            { IsBackground = true, Name = "ETABS-OAPI-STA-Export" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            using var timeoutCts = new CancellationTokenSource();
            var timeoutTask = Task.Delay(ExportTimeout, timeoutCts.Token);
            var winner = await Task.WhenAny(tcs.Task, timeoutTask).ConfigureAwait(false);
            if (winner != tcs.Task)
                return Failed("Export timed out after 3 minutes — ETABS likely stuck on a dialog.");

            timeoutCts.Cancel();
            return await tcs.Task.ConfigureAwait(false);
        }

        private static SafeApiExporter.ExportResult RunExportOnStaThread(SafeApiExporter.ExportInput input, string chosenExe)
        {
            var diag = new List<string>
            {
                $"chosenExe={chosenExe}",
                $"overridePath={(string.IsNullOrWhiteSpace(input.SafeExePathOverride) ? "<none>" : input.SafeExePathOverride)}",
            };

            // Load ETABSv1.dll and verify all OAPI members exist.
            string etabsDllPath = Path.Combine(Path.GetDirectoryName(chosenExe)!, "ETABSv1.dll");
            diag.Add($"etabsDllPath={etabsDllPath}");
            if (!File.Exists(etabsDllPath))
                return Failed($"ETABSv1.dll missing at '{etabsDllPath}'. Diagnostics: {string.Join(" | ", diag)}");

            Assembly etabsAsm;
            try
            {
                etabsAsm = Assembly.LoadFrom(etabsDllPath);
                diag.Add($"loadedAssembly={etabsAsm.FullName}");
            }
            catch (Exception ex) { return Failed($"Assembly.LoadFrom failed: {ex.Message}. Diagnostics: {string.Join(" | ", diag)}"); }

            if (!SafeOapiTypes.TryLoad(etabsAsm, "ETABSv1", out var typesOrNull, out var compatIssues))
                return Failed($"ETABS compatibility check failed. Missing: {string.Join("; ", compatIssues)}. Diagnostics: {string.Join(" | ", diag)}");
            var types = typesOrNull!;

            // COM activation — auto-register if needed (one-time UAC prompt per machine).
            bool hasOverride = !string.IsNullOrWhiteSpace(input.SafeExePathOverride);
            diag.Add($"hasOverride={hasOverride}");
            if (!CsiComRegistration.EnsureRegistered(chosenExe, CandidateProgIds, hasOverride, out string? regError))
            {
                diag.Add("registration=" + CsiComRegistration.DescribeRegistration(CandidateProgIds));
                return Failed((regError ?? "ETABS COM registration failed.") + ". Diagnostics: " + string.Join(" | ", diag));
            }

            diag.Add("registration=" + CsiComRegistration.DescribeRegistration(CandidateProgIds));

            Type? etabsComType = null;
            string? resolvedProgId = null;
            foreach (string candidate in CandidateProgIds)
            {
                var t = Type.GetTypeFromProgID(candidate);
                if (t is not null) { etabsComType = t; resolvedProgId = candidate; break; }
            }
            if (etabsComType is null)
                return Failed($"No ETABS COM ProgID registered after auto-registration attempt. Diagnostics: {string.Join(" | ", diag)}");

            diag.Add($"resolvedProgId={resolvedProgId}");
            diag.Add($"comType={etabsComType.FullName}");

            object? etabs;
            var sw = Stopwatch.StartNew();
            try
            {
                etabs = Activator.CreateInstance(etabsComType);
                sw.Stop();
                diag.Add($"createInstanceMs={sw.ElapsedMilliseconds}");
            }
            catch (Exception ex)
            {
                sw.Stop();
                diag.Add($"createInstanceMs={sw.ElapsedMilliseconds}");
                diag.Add("registrationAfterFailure=" + CsiComRegistration.DescribeRegistration(CandidateProgIds));
                return Failed($"Activator.CreateInstance('{resolvedProgId}') failed: {ex.Message}. Diagnostics: {string.Join(" | ", diag)}");
            }
            if (etabs is null) return Failed("Activator.CreateInstance returned null. Diagnostics: " + string.Join(" | ", diag));

            // Wrap in a driver and delegate to the orchestrator.
            using var driver = new ReflectionCsiOapiDriver(types, etabs);
            return ExportOrchestrator.Run(driver, input);
        }

        // ═══════════════════════════════════════════════════════════════════
        //                    DISCOVERY + COMPATIBILITY
        // ═══════════════════════════════════════════════════════════════════

        private static string? ResolveEtabsExePath(string? overridePath, out List<string> log)
        {
            log = new List<string>();
            string? cleaned = overridePath?.Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                if (!File.Exists(cleaned))
                    log.Add($"override '{cleaned}' does not exist");
                else if (!File.Exists(Path.Combine(Path.GetDirectoryName(cleaned)!, "ETABSv1.dll")))
                    log.Add($"override '{cleaned}' has no ETABSv1.dll beside it");
                else
                    return cleaned;
            }

            var installs = EnumerateEtabsInstalls().ToList();
            if (installs.Count == 0) { log.Add("no ETABS installs found under Program Files\\Computers and Structures"); return null; }

            var preferred = installs.FirstOrDefault(i => i.Version == DefaultPreferredVersion);
            if (preferred is not null) return preferred.ExePath;
            log.Add($"preferred ETABS {DefaultPreferredVersion} not installed; falling back to newest");
            return installs[0].ExePath;
        }

        public static IEnumerable<EtabsInstall> EnumerateEtabsInstalls()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (string.IsNullOrEmpty(root)) return Array.Empty<EtabsInstall>();
            string csiRoot = Path.Combine(root, "Computers and Structures");
            if (!Directory.Exists(csiRoot)) return Array.Empty<EtabsInstall>();

            var found = new List<EtabsInstall>();
            foreach (string dir in Directory.EnumerateDirectories(csiRoot, "ETABS*"))
            {
                string candidate = Path.Combine(dir, "ETABS.exe");
                if (!File.Exists(candidate)) continue;
                string folder = Path.GetFileName(dir) ?? "";
                int major = ParseMajorVersion(folder);
                if (major < 17 || major >= 100) continue;
                found.Add(new EtabsInstall(major, candidate, folder));
            }
            found.Sort((a, b) => b.Version.CompareTo(a.Version));
            return found;
        }

        public static List<CompatibilityReport> CheckAllInstalledCompatibility()
        {
            var reports = new List<CompatibilityReport>();
            foreach (var install in EnumerateEtabsInstalls())
            {
                string dll = Path.Combine(Path.GetDirectoryName(install.ExePath)!, "ETABSv1.dll");
                if (!File.Exists(dll))
                {
                    reports.Add(new CompatibilityReport(install, false, new[] { "ETABSv1.dll missing" }));
                    continue;
                }
                var ctx = new AssemblyLoadContext($"ETABS-preflight-{install.Version}-{Guid.NewGuid():N}", isCollectible: true);
                try
                {
                    Assembly asm = ctx.LoadFromAssemblyPath(dll);
                    if (!SafeOapiTypes.TryLoad(asm, "ETABSv1", out _, out var issues))
                        reports.Add(new CompatibilityReport(install, false, issues));
                    else
                        reports.Add(new CompatibilityReport(install, true, Array.Empty<string>()));
                }
                catch (Exception ex) { reports.Add(new CompatibilityReport(install, false, new[] { ex.Message })); }
                finally { try { ctx.Unload(); } catch { /* best-effort: probe context may not be unloadable */ } }
            }
            return reports;
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

        private static SafeApiExporter.ExportResult Failed(string message)
            => new SafeApiExporter.ExportResult(false, message, null, 0, 0, 0, 0, 0);
    }
}
