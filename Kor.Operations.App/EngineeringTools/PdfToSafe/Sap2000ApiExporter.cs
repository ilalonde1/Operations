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
    /// SAP2000 OAPI exporter — same architecture as <see cref="SafeApiExporter"/>
    /// and <see cref="EtabsApiExporter"/>. Key API differences from SAFE/ETABS:
    ///   - ApplicationStart(3) takes (eUnits, visible, fileName) instead of ()
    ///   - SetShell(9) instead of SetSlab(8) for area section properties
    /// Both are handled by <see cref="ReflectionCsiOapiDriver"/> via the
    /// flexible <see cref="SafeOapiTypes"/> detection.
    /// </summary>
    internal static class Sap2000ApiExporter
    {
        private static readonly TimeSpan ExportTimeout = TimeSpan.FromMinutes(3);
        public const int DefaultPreferredVersion = 24;

        internal static readonly string[] CandidateProgIds =
        {
            "CSI.SAP2000.API.SapObject",
        };

        public sealed record Sap2000Install(int Version, string ExePath, string FolderName);

        public static async Task<SafeApiExporter.ExportResult> ExportFullModelAsync(SafeApiExporter.ExportInput input)
        {
            string? chosenExe = ResolveSapExePath(input.SafeExePathOverride, out var discovery);
            if (chosenExe is null)
                return Failed("No usable SAP2000 install found. " + string.Join(" | ", discovery));

            var tcs = new TaskCompletionSource<SafeApiExporter.ExportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try { tcs.TrySetResult(RunExportOnStaThread(input, chosenExe)); }
                catch (Exception ex) { tcs.TrySetResult(Failed($"Thread exception: {ex.GetType().Name}: {ex.Message}")); }
            })
            { IsBackground = true, Name = "SAP2000-OAPI-STA-Export" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            using var timeoutCts = new CancellationTokenSource();
            var timeoutTask = Task.Delay(ExportTimeout, timeoutCts.Token);
            var winner = await Task.WhenAny(tcs.Task, timeoutTask).ConfigureAwait(false);
            if (winner != tcs.Task)
                return Failed("Export timed out after 3 minutes.");

            timeoutCts.Cancel();
            return await tcs.Task.ConfigureAwait(false);
        }

        private static SafeApiExporter.ExportResult RunExportOnStaThread(SafeApiExporter.ExportInput input, string chosenExe)
        {
            string sapDllPath = Path.Combine(Path.GetDirectoryName(chosenExe)!, "SAP2000v1.dll");
            if (!File.Exists(sapDllPath))
                return Failed($"SAP2000v1.dll missing at '{sapDllPath}'.");

            Assembly sapAsm;
            try { sapAsm = Assembly.LoadFrom(sapDllPath); }
            catch (Exception ex) { return Failed($"Assembly.LoadFrom failed: {ex.Message}"); }

            if (!SafeOapiTypes.TryLoad(sapAsm, "SAP2000v1", out var types, out var issues))
                return Failed($"SAP2000 compatibility check failed. Missing: {string.Join("; ", issues)}");

            // COM activation — auto-register if needed (one-time UAC prompt per machine).
            if (!CsiComRegistration.EnsureRegistered(chosenExe, CandidateProgIds, out string? regError))
                return Failed(regError ?? "SAP2000 COM registration failed.");

            Type? sapComType = null;
            string? resolvedProgId = null;
            foreach (string candidate in CandidateProgIds)
            {
                var t = Type.GetTypeFromProgID(candidate);
                if (t is not null) { sapComType = t; resolvedProgId = candidate; break; }
            }
            if (sapComType is null)
                return Failed($"No SAP2000 COM ProgID registered after auto-registration attempt.");

            object? sap;
            try { sap = Activator.CreateInstance(sapComType); }
            catch (Exception ex) { return Failed($"Activator.CreateInstance('{resolvedProgId}') failed: {ex.Message}"); }
            if (sap is null) return Failed("Activator.CreateInstance returned null.");

            using var driver = new ReflectionCsiOapiDriver(types!, sap);
            return ExportOrchestrator.Run(driver, input);
        }

        private static string? ResolveSapExePath(string? overridePath, out List<string> log)
        {
            log = new List<string>();
            string? cleaned = overridePath?.Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                if (!File.Exists(cleaned)) log.Add($"override '{cleaned}' does not exist");
                else if (!File.Exists(Path.Combine(Path.GetDirectoryName(cleaned)!, "SAP2000v1.dll")))
                    log.Add($"override '{cleaned}' has no SAP2000v1.dll beside it");
                else return cleaned;
            }

            var installs = EnumerateSapInstalls().ToList();
            if (installs.Count == 0) { log.Add("no SAP2000 installs found"); return null; }

            var preferred = installs.FirstOrDefault(i => i.Version == DefaultPreferredVersion);
            if (preferred is not null) return preferred.ExePath;
            return installs[0].ExePath;
        }

        public static IEnumerable<Sap2000Install> EnumerateSapInstalls()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (string.IsNullOrEmpty(root)) return Array.Empty<Sap2000Install>();
            string csiRoot = Path.Combine(root, "Computers and Structures");
            if (!Directory.Exists(csiRoot)) return Array.Empty<Sap2000Install>();

            var found = new List<Sap2000Install>();
            foreach (string dir in Directory.EnumerateDirectories(csiRoot, "SAP*"))
            {
                string candidate = Path.Combine(dir, "SAP2000.exe");
                if (!File.Exists(candidate)) continue;
                string folder = Path.GetFileName(dir) ?? "";
                int major = ParseMajorVersion(folder);
                if (major < 17 || major >= 100) continue;
                found.Add(new Sap2000Install(major, candidate, folder));
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

        private static SafeApiExporter.ExportResult Failed(string message)
            => new SafeApiExporter.ExportResult(false, message, null, 0, 0, 0, 0, 0);
    }
}
