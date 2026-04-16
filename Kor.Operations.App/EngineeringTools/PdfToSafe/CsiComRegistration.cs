#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// Handles auto-registration of CSI COM servers (SAFE, ETABS, SAP2000).
    /// Each CSI product ships a RegisterXXX.exe in its install folder that
    /// writes the COM ProgID to the Windows registry. Without it,
    /// <see cref="Type.GetTypeFromProgID"/> returns null and the OAPI export
    /// fails. This helper detects unregistered products and runs the
    /// registration tool with UAC elevation — the engineer gets a single
    /// "Allow this program to make changes?" prompt on first use per machine.
    /// </summary>
    internal static class CsiComRegistration
    {
        /// <summary>
        /// Ensures at least one of <paramref name="progIds"/> resolves via COM.
        /// If none resolves, looks for a Register*.exe beside <paramref name="exePath"/>
        /// and runs it elevated. Returns true if a ProgID resolves after the
        /// attempt (either it was already registered or registration succeeded).
        /// </summary>
        public static bool EnsureRegistered(string exePath, string[] progIds, out string? error)
        {
            error = null;

            // Already registered?
            if (progIds.Any(p => Type.GetTypeFromProgID(p) is not null))
                return true;

            // Find RegisterXXX.exe beside the product exe.
            string dir = Path.GetDirectoryName(exePath)!;
            string? registerExe = null;
            foreach (string candidate in Directory.EnumerateFiles(dir, "Register*.exe"))
            {
                string name = Path.GetFileName(candidate);
                // Match RegisterSAFE.exe, RegisterETABS.exe, RegisterSAP2000.exe, etc.
                if (name.StartsWith("Register", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("Unregister", StringComparison.OrdinalIgnoreCase))
                {
                    registerExe = candidate;
                    break;
                }
            }

            if (registerExe is null)
            {
                error = $"COM ProgID not registered and no Register*.exe found in '{dir}'. "
                      + "Run the CSI registration tool manually as Administrator.";
                return false;
            }

            // Run with UAC elevation. The "runas" verb triggers the system
            // UAC prompt; the engineer clicks Yes once per machine.
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = registerExe,
                    Verb = "runas",              // UAC elevation
                    UseShellExecute = true,      // required for Verb
                    WindowStyle = ProcessWindowStyle.Normal,
                };
                using var proc = Process.Start(psi);
                if (proc is null)
                {
                    error = $"Failed to launch '{registerExe}' — Process.Start returned null.";
                    return false;
                }
                // Wait up to 30 seconds for the registration tool to finish.
                proc.WaitForExit(30_000);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // ERROR_CANCELLED — user clicked "No" on the UAC prompt.
                error = "Registration cancelled. The COM server must be registered before export can work. "
                      + $"Run '{Path.GetFileName(registerExe)}' as Administrator from '{dir}'.";
                return false;
            }
            catch (Exception ex)
            {
                error = $"Failed to run '{registerExe}': {ex.Message}";
                return false;
            }

            // Verify registration succeeded.
            if (progIds.Any(p => Type.GetTypeFromProgID(p) is not null))
                return true;

            error = $"Registration tool ran but COM ProgID still not found. "
                  + $"Try running '{Path.GetFileName(registerExe)}' manually as Administrator.";
            return false;
        }
    }
}
