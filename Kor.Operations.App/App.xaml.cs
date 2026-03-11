#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Configuration;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Kor.Operations.Services;
using Kor.Operations.Graph;
using Microsoft.Kiota.Abstractions.Authentication;
using Application = System.Windows.Application;

namespace Kor.Operations
{
    public partial class App : Application
    {
        private const string MutexName = "Kor.Transmittals.SingleInstance.Mutex";
        private const string PipeName = "Kor.Transmittals.NamedPipe";

        // Best-effort: the UPN used for delegated Graph auth. Useful for consistent header display name/avatar lookup.
        internal static string? SignedInUserUpn { get; private set; }

        private Mutex _mutex;
        private CancellationTokenSource _pipeCts;

        protected override void OnStartup(StartupEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOR_DB_USER", EnvironmentVariableTarget.Machine)) ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOR_DB_PASSWORD", EnvironmentVariableTarget.Machine)) ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOR_ODBC_USER", EnvironmentVariableTarget.Machine)) ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOR_ODBC_PASSWORD", EnvironmentVariableTarget.Machine)))
            {
                MessageBox.Show(
                    "This application is missing required system configuration and cannot start.\r\nPlease contact IT support.",
                    "Application Not Configured",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown();
                return;
            }

            SecretMigrationRunner.RunOnceAtStartup();
            EnvironmentSecretOverrides.Apply();

            // DataDirect's ODBC Hybrid driver uses an HTTP stack that respects proxy env vars.
            // If a dev shell/IDE sets these to a dead local proxy (common: 127.0.0.1:9),
            // Deltek ODBC calls fail with "Failed to connect() to host or proxy".
            ClearProcessProxyEnvVars();

            var args = e.Args ?? Array.Empty<string>();

            // -------------------------------------------------------------
            // 1) Special modes that should NOT participate in single-instance
            // -------------------------------------------------------------

            // a) Pure "pick a project" mode (used by the Outlook transmittal picker)
            bool pickerMode = args.Any(a =>
                string.Equals(a, "--file-picker", StringComparison.OrdinalIgnoreCase));

            string resultFile = null;
            foreach (var a in args)
            {
                const string prefix = "--picker-result=";
                if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    resultFile = a.Substring(prefix.Length).Trim('"');
                    break;
                }
            }

            if (pickerMode)
            {
                // Old behaviour – short-lived process that just returns a project number
                RunEmailPickerMode(resultFile);
                Shutdown();
                return;
            }

            // b) Email filing from Outlook (File Selected Emails / send-prompt)
            //    Always run in a short-lived, dedicated process – do NOT use the
            //    single-instance / pipe machinery, otherwise we end up on HomeWindow.
            string fileEmailsArg = args
                .FirstOrDefault(a => a.StartsWith("--file-emails=", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(fileEmailsArg))
            {
                string raw = fileEmailsArg.Substring("--file-emails=".Length).Trim('\"');

                var emailFiles = raw
                    .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p =>
                        File.Exists(p) &&
                        (p.EndsWith(".msg", StringComparison.OrdinalIgnoreCase) ||
                         p.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (emailFiles.Count == 0)
                {
                    // Nothing valid – just exit quietly
                    Shutdown();
                    return;
                }

                var picker = new EmailFilePickerWindow(emailFiles);
                MainWindow = picker;
                picker.Show();

                base.OnStartup(e);
                return; // this process lives only for the picker
            }

            // c) Quick Transfer from Outlook ribbon
            //    Also runs as a short-lived, dedicated process and does NOT join
            //    the single-instance / pipe world.
            bool quickTransferMode = args.Any(a =>
                a.StartsWith("--quick-transfer", StringComparison.OrdinalIgnoreCase));

            if (quickTransferMode)
            {
                // QuickTransfer uses Microsoft Graph for upload + email.
                EnsureGraphInitializedForDelegatedAuth();

                // Parse optional args: --from=, --to=, --cc=, --subject=
                string from = GetArgValue(args, "--from=");
                string to = GetArgValue(args, "--to=");
                string cc = GetArgValue(args, "--cc=");
                string subject = GetArgValue(args, "--subject=");

                var wnd = new QuickTransferWindow(from, to, cc, subject);
                MainWindow = wnd;
                wnd.Show();

                base.OnStartup(e);
                return; // this process lives only for Quick Transfer
            }

            // -------------------------------------------------------------
            // 2) Everything else uses the existing single-instance behaviour
            // -------------------------------------------------------------

            // single-instance guard
            _mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out bool isNew);
            if (!isNew)
            {
                // Forward args to the existing instance (transmittal file merges or commands) and quit
                if (args.Length > 0)
                {
                    try
                    {
                        using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                        client.Connect(2000);

                        using var writer = new StreamWriter(client, Encoding.UTF8, 1024, leaveOpen: false)
                        {
                            AutoFlush = true
                        };

                        writer.WriteLine(string.Join("|", args));
                    }
                    catch
                    {
                        // best-effort only; never crash on startup
                    }
                }

                Shutdown();
                return;
            }

            EnsureGraphInitializedForDelegatedAuth();

            // start pipe server (unchanged, plus email-search handling inside)
            _pipeCts = new CancellationTokenSource();
            _ = Task.Run(() => RunPipeServerAsync(_pipeCts.Token), _pipeCts.Token);

            // decide which window to show
            bool emailSearchMode = args.Any(a =>
                string.Equals(a, "--email-search", StringComparison.OrdinalIgnoreCase));

            // file args = transmittal PDFs, etc. (existing behaviour)
            List<string> fileArgs = args.Where(File.Exists).ToList();

            Window startupWindow;

            if (emailSearchMode)
            {
                // Outlook "Search Filed Emails" → go straight to EmailSearchWindow
                startupWindow = new EmailSearchWindow();
            }
            else if (fileArgs.Count > 0)
            {
                // Launched with files (double-click handler, watcher, etc.) → MainWindow
                var main = new MainWindow();
                try
                {
                    main.LoadInitialFiles(fileArgs);
                }
                catch
                {
                    // never break startup if something goes wrong here
                }
                startupWindow = main;
            }
            else
            {
                // Normal launch (Start menu, shortcut, Outlook Transmittal button, etc.) → Home dashboard
                startupWindow = new HomeWindow();
            }

            MainWindow = startupWindow;
            startupWindow.Show();

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { _pipeCts?.Cancel(); } catch { /* ignore */ }

            try
            {
                _mutex?.ReleaseMutex();
                _mutex?.Dispose();
            }
            catch
            {
                // ignore
            }

            base.OnExit(e);
        }

        private async Task RunPipeServerAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        transmissionMode: PipeTransmissionMode.Byte,
                        options: PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    string line;

#if NET8_0_OR_GREATER
                    line = await reader.ReadLineAsync(token).ConfigureAwait(false);
#elif NET6_0_OR_GREATER
                    line = await reader.ReadLineAsync().WaitAsync(token).ConfigureAwait(false);
#else
#pragma warning disable CA2016
                    line = await reader.ReadLineAsync().ConfigureAwait(false);
#pragma warning restore CA2016
#endif

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        // NEW: parse tokens once and detect commands + files
                        var tokens = line
                            .Split('|')
                            .Select(t => t.Trim())
                            .Where(t => !string.IsNullOrEmpty(t))
                            .ToList();

                        bool emailSearchCmd = tokens.Any(t =>
                            string.Equals(t, "--email-search", StringComparison.OrdinalIgnoreCase));

                        var files = tokens
                            .Where(File.Exists)
                            .ToList();

                        await Dispatcher.InvokeAsync(
                            () =>
                            {
                                // 1) Handle command: open / focus EmailSearchWindow
                                if (emailSearchCmd)
                                {
                                    var existing = Current.Windows
                                        .OfType<EmailSearchWindow>()
                                        .FirstOrDefault();

                                    if (existing != null)
                                    {
                                        if (existing.WindowState == WindowState.Minimized)
                                            existing.WindowState = WindowState.Normal;
                                        existing.Activate();
                                    }
                                    else
                                    {
                                        var win = new EmailSearchWindow();

                                        if (Current.MainWindow != null && Current.MainWindow.IsLoaded)
                                            win.Owner = Current.MainWindow;

                                        win.Show();
                                        win.Activate();
                                    }
                                }

                                // 2) Existing behaviour: merge file paths into MainWindow
                                if (Current.MainWindow is MainWindow mw && files.Count > 0)
                                {
                                    mw.MergeFiles(files);
                                    mw.Activate();
                                }
                            },
                            DispatcherPriority.Normal,
                            token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // expected on shutdown
                }
                catch
                {
                    // swallow and continue listening
                }
            }
        }

        // ---------------------------------------------------------------------
        // Helper for --file-picker mode (Outlook File Selected Emails)
        // ---------------------------------------------------------------------
        private void RunEmailPickerMode(string resultFile)
        {
            try
            {
                // Normalize to a definite path for the rest of this method
                string effectiveResultFile = resultFile;

                if (string.IsNullOrWhiteSpace(effectiveResultFile))
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    string korDir = Path.Combine(appData, "KOR");
                    Directory.CreateDirectory(korDir);
                    effectiveResultFile = Path.Combine(korDir, "EmailFilePickerResult.txt");
                }

                if (File.Exists(effectiveResultFile))
                {
                    try { File.Delete(effectiveResultFile); } catch { }
                }

                // Empty list for now – this mode is just "pick a project", no incoming files
                var picker = new EmailFilePickerWindow(Array.Empty<string>());
                bool? ok = picker.ShowDialog();

                string projectNo = null;

                if (ok == true)
                {
                    var t = picker.GetType();
                    var prop = t.GetProperty("SelectedProjectNo");
                    if (prop != null)
                    {
                        projectNo = prop.GetValue(picker) as string;
                    }
                }

                if (!string.IsNullOrWhiteSpace(projectNo))
                {
                    File.WriteAllText(effectiveResultFile, projectNo, Encoding.UTF8);
                }
                else
                {
                    // User cancelled or nothing selected – write empty file so caller knows it ran
                    File.WriteAllText(effectiveResultFile, string.Empty, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("RunEmailPickerMode error: " + ex);

                try
                {
                    string fallback = resultFile;
                    if (string.IsNullOrWhiteSpace(fallback))
                    {
                        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                        string korDir = Path.Combine(appData, "KOR");
                        Directory.CreateDirectory(korDir);
                        fallback = Path.Combine(korDir, "EmailFilePickerResult.txt");
                    }

                    File.WriteAllText(fallback, string.Empty, Encoding.UTF8);
                }
                catch
                {
                    // ignore
                }
            }
        }

        private static void ClearProcessProxyEnvVars()
        {
            // Upper and lower case because different libraries read different variants.
            string[] keys =
            {
                "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY",
                "http_proxy", "https_proxy", "all_proxy", "no_proxy"
            };

            foreach (var k in keys)
            {
                try
                {
                    Environment.SetEnvironmentVariable(k, null, EnvironmentVariableTarget.Process);
                }
                catch
                {
                    // best-effort only
                }
            }
        }

        // ---------------------------------------------------------------------
        // Simple arg parser for --foo=bar style args
        // ---------------------------------------------------------------------
        private static string GetArgValue(string[] args, string prefix)
        {
            foreach (var a in args)
            {
                if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var value = a.Substring(prefix.Length);
                    // strip surrounding quotes if present
                    if (value.Length >= 2 && value.StartsWith("\"") && value.EndsWith("\""))
                    {
                        value = value.Substring(1, value.Length - 2);
                    }
                    return value;
                }
            }
            return string.Empty;
        }

        private static void EnsureGraphInitializedForDelegatedAuth()
        {
            // Idempotent: GraphFacade.Initialize() is a no-op if already set.
            string tenantId = (ConfigurationManager.AppSettings["Graph.TenantId"] ?? string.Empty).Trim();
            string clientId = (ConfigurationManager.AppSettings["Graph.ClientId"] ?? string.Empty).Trim();
            string driveId = (ConfigurationManager.AppSettings["Graph.DriveId"] ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(tenantId) ||
                string.IsNullOrWhiteSpace(clientId) ||
                string.IsNullOrWhiteSpace(driveId))
            {
                throw new InvalidOperationException(
                    "Microsoft Graph configuration missing. App.config must define Graph.TenantId, Graph.ClientId, and Graph.DriveId.");
            }

            // Delegated least-privilege scopes for existing features:
            // - UploadWithProgressAsync + CreateLinksAsync -> files in a drive the user can access
            // - SendMailAsync -> send email as signed-in user
            // - Basic profile -> future /me use and account selection; low risk
            var scopes = new[]
            {
                "User.Read",
                "Mail.Send",
                "Files.ReadWrite.All"
            };

            var loginHint = ConfigurationManager.AppSettings["UserUpnOverride"];

            // MSAL init is async because of token cache wiring; block during startup to keep the rest synchronous.
            var provider = MsalGraphAuthenticationProvider
                .CreateAsync(tenantId, clientId, scopes, loginHintUpn: loginHint)
                .GetAwaiter()
                .GetResult();

            // Pre-warm auth so later Graph calls can stay silent (avoid interactive prompt from a background thread).
            provider.EnsureSignedInAsync(loginHintUpn: loginHint).GetAwaiter().GetResult();

            // Capture the signed-in UPN for other UI components (e.g., header name/photo lookups).
            SignedInUserUpn = provider.SignedInUpn ?? (string.IsNullOrWhiteSpace(loginHint) ? null : loginHint.Trim());

            GraphFacade.Initialize((IAuthenticationProvider)provider, driveId);
        }
    }
}
