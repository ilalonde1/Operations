#nullable enable
using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.Win32;

namespace Kor.Operations
{
    /// <summary>
    /// Registers the <c>kor://</c> URL scheme (per-user under HKCU, no admin) so
    /// report links like <c>kor://mpi/123</c> — which previously only worked
    /// inside the app's WebView2 report preview — open the app to that project
    /// from anywhere: an exported PDF, an email, a chat. Self-registers on every
    /// launch so the exe path stays current after an app update, and no-ops when
    /// already pointing at the right exe.
    /// </summary>
    internal static class KorUriScheme
    {
        public const string Scheme = "kor";
        private const string Prefix = "kor://";

        public static void EnsureRegistered()
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exe))
                {
                    exe = Process.GetCurrentProcess().MainModule?.FileName;
                }

                if (string.IsNullOrWhiteSpace(exe))
                {
                    return;
                }

                var desired = $"\"{exe}\" \"%1\"";
                var cmdPath = $@"Software\Classes\{Scheme}\shell\open\command";

                // Avoid registry churn — only write when missing or the exe moved.
                using (var existing = Registry.CurrentUser.OpenSubKey(cmdPath))
                {
                    if (existing?.GetValue(null) is string current
                        && string.Equals(current, desired, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }

                using var root = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}");
                root.SetValue(null, "URL:KOR Operations");
                root.SetValue("URL Protocol", string.Empty);
                using var cmd = Registry.CurrentUser.CreateSubKey(cmdPath);
                cmd.SetValue(null, desired);
            }
            catch
            {
                // Best-effort — deep links are a convenience, never block startup.
            }
        }

        /// <summary>The first <c>kor://</c> argument in a launch, or null.</summary>
        public static string? FindLink(string[]? args) =>
            args?.FirstOrDefault(a => a is not null && a.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase));
    }
}
