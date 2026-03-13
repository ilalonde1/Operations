#nullable enable
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
using Kor.EmailSearch.Core;
using Kor.Operations.Core;
using Kor.Operations.Data;
using Kor.Operations.Financials;
using Kor.Operations.Rendering;
using Kor.Operations.Services;
using Kor.Operations.Graph;
using Microsoft.Extensions.DependencyInjection;
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

        private IServiceProvider? _services;
        private Mutex? _mutex;
        private CancellationTokenSource? _pipeCts;

        internal IServiceProvider Services =>
            _services ?? throw new InvalidOperationException("The application service provider has not been initialized.");

        protected override async void OnStartup(StartupEventArgs e)
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
            await EnsureGraphInitializedForDelegatedAuthAsync();
            _services = BuildServiceProvider();

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

            string? resultFile = null;
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
            string? fileEmailsArg = args
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

                var picker = _services.GetRequiredService<EmailFilePickerWindow>();
                picker.SetIncomingFiles(emailFiles);
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
                // Parse optional args: --from=, --to=, --cc=, --subject=
                string from = GetArgValue(args, "--from=");
                string to = GetArgValue(args, "--to=");
                string cc = GetArgValue(args, "--cc=");
                string subject = GetArgValue(args, "--subject=");

                var wnd = _services.GetRequiredService<QuickTransferWindow>();
                wnd.InitializeRequest(from, to, cc, subject);
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
                        // 8 s timeout: slow-start conditions on a cold-start instance may take several seconds before its pipe is ready to receive forwarded args.
                        client.Connect(8000);

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
                startupWindow = _services.GetRequiredService<EmailSearchWindow>();
            }
            else if (fileArgs.Count > 0)
            {
                // Launched with files (double-click handler, watcher, etc.) → MainWindow
                var main = _services.GetRequiredService<MainWindow>();
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
                startupWindow = _services.GetRequiredService<HomeWindow>();
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
                    string? line;

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
                                        var win = _services!.GetRequiredService<EmailSearchWindow>();

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
        private void RunEmailPickerMode(string? resultFile)
        {
            try
            {
                string effectiveResultFile = GetPickerResultFilePath(resultFile);

                if (File.Exists(effectiveResultFile))
                {
                    try { File.Delete(effectiveResultFile); } catch { }
                }

                // Empty list for now – this mode is just "pick a project", no incoming files
                var picker = _services!.GetRequiredService<EmailFilePickerWindow>();
                picker.SetIncomingFiles(Array.Empty<string>());
                bool? ok = picker.ShowDialog();

                string? projectNo = null;

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
                    string fallback = GetPickerResultFilePath(resultFile);
                    File.WriteAllText(fallback, string.Empty, Encoding.UTF8);
                }
                catch
                {
                    // ignore
                }
            }
        }

        private static string GetPickerResultFilePath(string? resultFile)
        {
            if (!string.IsNullOrWhiteSpace(resultFile))
                return resultFile;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string korDir = Path.Combine(appData, "KOR");
            Directory.CreateDirectory(korDir);
            return Path.Combine(korDir, "EmailFilePickerResult.txt");
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

        private IServiceProvider BuildServiceProvider()
        {
            var services = new ServiceCollection();
            var transmittalsConnectionString = GetRequiredConnectionString(AppConfigKeys.ConnectionStrings.KorTransmittalsDb);
            var emailIndexConnectionString = GetRequiredConnectionString(AppConfigKeys.ConnectionStrings.KorEmailIndex);
            var projectsRoot = GetRequiredAppSetting(AppConfigKeys.ProjectsRoot);
            var redirectorBaseUrl = ConfigurationManager.AppSettings[AppConfigKeys.RedirectorBaseUrl];
            var userUpn = ResolveUserUpn();

            services.AddSingleton<IServiceProvider>(sp => sp);
            services.AddSingleton<IGraphFacade>(_ => GraphFacade.Instance);
            services.AddTransient(typeof(CoverSheetRenderer), _ => throw new NotSupportedException("CoverSheetRenderer is static and is not constructed through DI."));
            services.AddTransient<IEmailMetadataExtractor, BasicEmailMetadataExtractor>();
            services.AddTransient<GlProfitLossService>();
            services.AddTransient<FinancialsService>();
            services.AddTransient(_ => new SqlFinancialPortfolioSnapshotStore(transmittalsConnectionString));
            services.AddTransient<ExecutiveSummaryDeltekLoader>();
            services.AddTransient<ExecutiveSummaryService>();
            services.AddTransient<EmailIndexWriter>(sp =>
                new EmailIndexWriter(emailIndexConnectionString, sp.GetRequiredService<IEmailMetadataExtractor>()));
            services.AddTransient(_ => new EmailSearchService(emailIndexConnectionString));

            services.AddSingleton<VpOdbcDsnFactory>(_ =>
            {
                var dsn = ConfigurationManager.AppSettings[AppConfigKeys.VpDsn] ?? "Deltek";
                var user = ConfigurationManager.AppSettings[AppConfigKeys.VpUser] ?? string.Empty;
                var pwd = ConfigurationManager.AppSettings[AppConfigKeys.VpPassword] ?? string.Empty;
                return new VpOdbcDsnFactory(dsn, user, pwd, () => new Dictionary<string, string>());
            });
            services.AddSingleton(sp => ActivatorUtilities.CreateInstance<SqlTransmittalsStore>(sp, transmittalsConnectionString));
            services.AddSingleton<ITransmittalsStore>(sp => sp.GetRequiredService<SqlTransmittalsStore>());
            services.AddSingleton(sp => new SqlUserPreferencesStore(transmittalsConnectionString));
            services.AddSingleton<IUserPreferencesStore>(sp => sp.GetRequiredService<SqlUserPreferencesStore>());
            services.AddSingleton(sp => ActivatorUtilities.CreateInstance<PreferencesRepository>(sp, transmittalsConnectionString));
            services.AddSingleton(_ => new SqlEmailIndexStore(emailIndexConnectionString));
            services.AddSingleton(sp => ActivatorUtilities.CreateInstance<VantagepointRepository>(sp, sp.GetRequiredService<VpOdbcDsnFactory>()));

            services.AddTransient<IUploadOrchestrator, UploadOrchestrator>();
            services.AddTransient<IRecipientResolver, RecipientResolver>();
            services.AddTransient<IProjectSearchService>(sp =>
                new ProjectSearchService(
                    sp.GetRequiredService<PreferencesRepository>(),
                    projectsRoot,
                    mustContainSubfolder: null));
            services.AddTransient<ITransmittalService>(sp =>
                new TransmittalService(
                    sp.GetRequiredService<IGraphFacade>(),
                    sp.GetRequiredService<IUploadOrchestrator>(),
                    sp.GetRequiredService<ITransmittalsStore>(),
                    transmittalsConnectionString,
                    redirectorBaseUrl,
                    typeof(MainWindow).Assembly.GetName().Version?.ToString()));
            services.AddTransient(sp =>
                new MainWindowWorkflowService(
                    userUpn,
                    sp.GetRequiredService<IUserPreferencesStore>(),
                    sp.GetRequiredService<IUploadOrchestrator>(),
                    sp.GetRequiredService<ITransmittalService>()));

            services.AddTransient<MainWindow>();
            services.AddTransient<HomeWindow>();
            services.AddTransient<DashboardWindow>();
            services.AddTransient<EmailSearchWindow>();
            services.AddTransient<EmailFilePickerWindow>();
            services.AddTransient<QuickTransferWindow>();
            services.AddTransient<PreferencesWindow>();
            services.AddTransient<TeamsPickerWindow>();
            services.AddTransient<InboundUploadRunner>();
            services.AddTransient<QuickTransferRunner>();

            return services.BuildServiceProvider();
        }

        private static string GetRequiredAppSetting(string key)
        {
            var value = (ConfigurationManager.AppSettings[key] ?? string.Empty).Trim();
            return !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new InvalidOperationException($"App.config appSetting '{key}' is missing or empty.");
        }

        private static string GetRequiredConnectionString(string key)
        {
            var value = ConfigurationManager.ConnectionStrings[key]?.ConnectionString;
            return !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new InvalidOperationException($"App.config connection string '{key}' is missing or empty.");
        }

        private static string ResolveUserUpn()
        {
            var overrideUpn = ConfigurationManager.AppSettings[AppConfigKeys.UserUpnOverride];
            return !string.IsNullOrWhiteSpace(overrideUpn)
                ? overrideUpn.Trim()
                : $"{Environment.UserName}@korstructural.com";
        }

        private static async Task EnsureGraphInitializedForDelegatedAuthAsync()
        {
            // Idempotent: GraphFacade.Initialize() is a no-op if already set.
            string tenantId = (ConfigurationManager.AppSettings[Kor.Operations.Services.AppConfigKeys.GraphTenantId] ?? string.Empty).Trim();
            string clientId = (ConfigurationManager.AppSettings[Kor.Operations.Services.AppConfigKeys.GraphClientId] ?? string.Empty).Trim();
            string driveId = (ConfigurationManager.AppSettings[Kor.Operations.Services.AppConfigKeys.GraphDriveId] ?? string.Empty).Trim();

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

            var loginHint = ConfigurationManager.AppSettings[Kor.Operations.Services.AppConfigKeys.UserUpnOverride];

            // MSAL init is async because of token cache wiring.
            var provider = await MsalGraphAuthenticationProvider
                .CreateAsync(tenantId, clientId, scopes, loginHintUpn: loginHint)
                .ConfigureAwait(true);

            // Pre-warm auth so later Graph calls can stay silent (avoid interactive prompt from a background thread).
            await provider.EnsureSignedInAsync(loginHintUpn: loginHint).ConfigureAwait(true);

            // Capture the signed-in UPN for other UI components (e.g., header name/photo lookups).
            SignedInUserUpn = provider.SignedInUpn ?? (string.IsNullOrWhiteSpace(loginHint) ? null : loginHint.Trim());

            GraphFacade.Initialize((IAuthenticationProvider)provider, driveId);
        }
    }
}

