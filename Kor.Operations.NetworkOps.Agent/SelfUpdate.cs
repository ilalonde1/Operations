using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations.NetworkOps.Agent;

/// <summary>
/// The agent replaces itself when APP01 ships a newer one, so a PC that push-install cannot reach still keeps up.
///
/// WHY THIS EXISTS: RemoteAgentInstall pushes over the admin share after an SMB probe, which can never reach a machine
/// working off-LAN through a VPN -- the office cannot open a connection back down a client tunnel. On 2026-10-13 the
/// fleet went to 1.2.3 and exactly one PC was left behind for that reason, every batch, with no way to fix it from
/// APP01. Pull closes that hole; push stays for the first install, which still needs an administrator.
///
/// TRUST: this adds none. The package comes over the SAME pinned TLS connection the agent already uses, and that
/// connection already hands this process arbitrary PowerShell to run as SYSTEM. Anyone who could substitute a package
/// here could already own the machine through a job. The pin (ServerCertSha256) is the anchor, redirects are refused,
/// and a package whose version is not strictly NEWER is ignored, so a replayed older build cannot roll an agent back.
///
/// WHAT IT CHECKS: the server's shipped version parses and is greater than this one; the downloaded zip contains the
/// agent exe; that exe's file version equals what the server advertised. WHAT IT DOES NOT: verify an Authenticode
/// signature -- the agent is not signed today. If it is ever signed, verify it here and refuse otherwise; the pin
/// alone is sound but a signature would survive a compromised listener.
///
/// HOW THE SWAP WORKS: a running service cannot overwrite its own exe, so this writes a detached .cmd that waits for
/// the service to stop, keeps the current build in data\previous, copies the new files in, starts the service, and
/// puts the old build back if it will not start. The agent then asks to stop, and the updater takes it from there.
/// data\ is never copied over: the key lives there.
/// </summary>
internal static class SelfUpdate
{
    /// <summary>One attempt per agent process: a package that fails must not become a restart loop.</summary>
    private static int _tried;

    public static bool Wanted(string? shipped, string running) =>
        Version.TryParse(Trim(shipped), out var s) && Version.TryParse(Trim(running), out var r) && s > r;

    private static string? Trim(string? v) => v?.Split('+')[0].Trim();

    /// <summary>True when the swap has been handed to the updater and the agent should stop.</summary>
    public static async Task<bool> TryAsync(HttpClient http, AgentSettings s, AgentLog log, string shipped, CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _tried, 1) == 1) return false;
        try
        {
            log.Info($"self-update: APP01 ships {shipped}, this is {AgentSettings.Version}; downloading");

            var staging = Path.Combine(s.DataDir, "update", DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + shipped);
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);

            var zipPath = staging + ".zip";
            using (var response = await http.GetAsync("agent/v1/package", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                using var src = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var dst = File.Create(zipPath);
                await src.CopyToAsync(dst, 81920, ct).ConfigureAwait(false);
            }
            ZipFile.ExtractToDirectory(zipPath, staging);
            File.Delete(zipPath);

            var exe = Path.Combine(staging, ExeName);
            if (!File.Exists(exe)) { log.Warn($"self-update: the package has no {ExeName}; nothing done"); return false; }

            // The server said one thing; the file must say the same. A package folder mid-deploy would differ.
            var got = Trim(FileVersionInfo.GetVersionInfo(exe).ProductVersion);
            if (!string.Equals(got, Trim(shipped), StringComparison.OrdinalIgnoreCase))
            {
                log.Warn($"self-update: the package is {got} but the server advertised {shipped}; refusing");
                return false;
            }

            var cmdPath = Path.Combine(s.DataDir, "update", "apply.cmd");
            File.WriteAllText(cmdPath, UpdaterScript(staging, Path.Combine(s.DataDir, "previous")), new UTF8Encoding(false));

            // Detached and outside this service's job: it has to outlive the stop it is about to perform.
            Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + cmdPath + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                // A SYSTEM process must not take its working directory from a user-writable place (the TEMP
                // folder): cmd.exe and the script could resolve a planted binary/DLL from there. The script
                // uses only absolute paths, so an admin-only CWD changes nothing but the attack surface.
                WorkingDirectory = Environment.SystemDirectory,
            });
            log.Info($"self-update: {shipped} staged at {staging}; the updater is running and will restart the service");
            return true;
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            log.Warn("self-update failed, staying on " + AgentSettings.Version + ": " + ex.Message);
            return false;
        }
    }

    private const string ExeName = "Kor.Operations.NetworkOps.Agent.exe";
    private const string ServiceName = "KorNetworkOpsAgent";
    private const string InstallDir = @"C:\Program Files\KorOperations\Agent";

    private static string UpdaterScript(string staging, string previous) =>
        "@echo off\r\n" +
        "setlocal\r\n" +
        "set LOG=\"" + Path.Combine(InstallDir, "data", "update.log") + "\"\r\n" +
        "echo [%DATE% %TIME%] updating from " + staging + ">> %LOG%\r\n" +
        // Give the service time to come down on its own; it stops itself right after launching this.
        "net stop " + ServiceName + " >> %LOG% 2>&1\r\n" +
        "timeout /t 5 /nobreak >nul\r\n" +
        // Keep the build that works. data\ is excluded: the key and the logs live there.
        "if exist \"" + previous + "\" rd /s /q \"" + previous + "\"\r\n" +
        "mkdir \"" + previous + "\"\r\n" +
        "robocopy \"" + InstallDir + "\" \"" + previous + "\" /E /XD \"" + Path.Combine(InstallDir, "data") + "\" /NFL /NDL /NJH /NJS /NP >> %LOG% 2>&1\r\n" +
        "robocopy \"" + staging + "\" \"" + InstallDir + "\" /E /NFL /NDL /NJH /NJS /NP >> %LOG% 2>&1\r\n" +
        "net start " + ServiceName + " >> %LOG% 2>&1\r\n" +
        "sc query " + ServiceName + " | find \"RUNNING\" >nul\r\n" +
        "if errorlevel 1 (\r\n" +
        "  echo [%DATE% %TIME%] it did not start: putting the previous build back>> %LOG%\r\n" +
        "  robocopy \"" + previous + "\" \"" + InstallDir + "\" /E /NFL /NDL /NJH /NJS /NP >> %LOG% 2>&1\r\n" +
        "  net start " + ServiceName + " >> %LOG% 2>&1\r\n" +
        ") else (\r\n" +
        "  echo [%DATE% %TIME%] updated and running>> %LOG%\r\n" +
        "  rd /s /q \"" + staging + "\"\r\n" +
        ")\r\n";
}
