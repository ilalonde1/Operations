#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kor.Operations.App.Options;
using Kor.Operations.App.Services;
using Kor.Operations.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Application = System.Windows.Application;

namespace Kor.Operations
{
    public partial class OperationsApp : Application
    {
        internal static string? SignedInUserUpn { get; set; }

        private IServiceProvider? _services;
        private SingleInstanceGuard? _guard;
        private AppPipeServer? _pipeServer;

        internal IServiceProvider Services =>
            _services ?? throw new InvalidOperationException("The application service provider has not been initialized.");

        // Launch args can carry a file path, a project reference, or a kor:// deep link -- handy for diagnosing routing, but
        // not to write verbatim into a log or the crash file (audit #8). Keep the shape (count + each arg's scheme or kind)
        // and drop the payload: a URI -> "<scheme>://…", a path -> "<path>", anything long is truncated.
        private static string SafeArgs(string[]? args)
        {
            if (args is null || args.Length == 0) return "none";
            var parts = new System.Collections.Generic.List<string>(args.Length);
            foreach (var a in args)
            {
                if (string.IsNullOrEmpty(a)) { parts.Add("\"\""); continue; }
                int scheme = a.IndexOf("://", StringComparison.Ordinal);
                if (scheme > 0) parts.Add(a.Substring(0, scheme) + "://…");
                else if (a.Contains('\\') || a.Contains('/') || (a.Length > 1 && a[1] == ':')) parts.Add("<path>");
                else parts.Add(a.Length > 24 ? a.Substring(0, 24) + "…" : a);
            }
            return args.Length + ": [" + string.Join(", ", parts) + "]";
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            try
            {
                await OnStartupCoreAsync(e).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                try
                {
                    var crashLog = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "KorOperations", "startup-crash.txt");
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(crashLog)!);
                    System.IO.File.AppendAllText(crashLog,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} ARGS={SafeArgs(e.Args)}{Environment.NewLine}" +
                        $"{ex}{Environment.NewLine}{Environment.NewLine}");
                }
                catch (Exception) { /* last-resort crash log — nowhere to report if this fails */ }
                Shutdown();
            }
        }

        private async System.Threading.Tasks.Task OnStartupCoreAsync(StartupEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOR_DB_USER", EnvironmentVariableTarget.Machine)) ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOR_DB_PASSWORD", EnvironmentVariableTarget.Machine)) ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOR_ODBC_USER", EnvironmentVariableTarget.Machine)) ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOR_ODBC_PASSWORD", EnvironmentVariableTarget.Machine)))
            {
                MessageBox.Show("This application is missing required system configuration and cannot start.\r\nPlease contact IT support.", "Application — Application Not Configured", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            Log.Logger = CompositionHelpers.GetSerilogLogger();
            // One guaranteed line per launch: proves logging is alive this session, and dates the session.
            Log.ForContext<OperationsApp>().Information("App starting. version={Version} args={Args}",
                System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?",
                SafeArgs(e.Args));
            RegisterGlobalExceptionHandlers();
            SecretMigrationRunner.RunOnceAtStartup();
            EnvironmentSecretOverrides.Apply();
            QuestPDF.Settings.License =
                QuestPDF.Infrastructure.LicenseType.Community;
            // Register the kor:// scheme so report links (kor://mpi/<id>) open the
            // app from an exported PDF/email, not just inside the report preview.
            KorUriScheme.EnsureRegistered();
            _services = AppCompositionRoot.BuildServiceProvider();
            Kor.Operations.Services.AppServices.Initialize(_services);
            _services.GetRequiredService<AppAiContextBuilder>().Register(_services.GetRequiredService<FirmContextProvider>());
            Log.ForContext<OperationsApp>().Debug("Startup: services built; initializing Graph auth…");
            try
            {
                await AppAuthBootstrapper.EnsureGraphInitializedForDelegatedAuthAsync(
                    _services.GetRequiredService<GraphOptions>(),
                    _services.GetRequiredService<UserOptions>(),
                    _services.GetRequiredService<GraphAuthenticationState>()).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log.ForContext<OperationsApp>().Error(
                    ex,
                    "Startup Graph authentication initialization failed. {ErrorType}: {ErrorMessage}",
                    ex.GetType().Name,
                    ex.Message);

                MessageBox.Show(
                    "Sign-in failed during Microsoft Graph initialization. The application will now close.\r\n\r\nPlease try again or contact IT support if the problem persists.",
                    "Application — Sign-In Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown(-1);
                return;
            }
            ClearProcessProxyEnvVars();
            Log.ForContext<OperationsApp>().Debug("Startup: Graph auth initialized OK");

            var args = e.Args ?? Array.Empty<string>();
            _guard = new SingleInstanceGuard(args);
            Log.ForContext<OperationsApp>().Debug("Startup: single-instance firstInstance={First} participates={Participates} args={Args}",
                _guard.IsFirstInstance, _guard.ParticipatesInSingleInstance, SafeArgs(args));
            if (!_guard.IsFirstInstance)
            {
                Log.ForContext<OperationsApp>().Information("Another instance is already running; forwarding args and exiting");
                _guard.ForwardArgsAndExit(args);
                Shutdown();
                return;
            }

            if (_guard.ParticipatesInSingleInstance)
            {
                _pipeServer = new AppPipeServer();
                _pipeServer.Start(_services);
            }

            Log.ForContext<OperationsApp>().Debug("Startup: routing args={Args}…", SafeArgs(args));
            var startupWindow = await new AppStartupRouter(
                _services,
                _services.GetRequiredService<ILogger<AppStartupRouter>>()).RouteAsync(args, CancellationToken.None).ConfigureAwait(true);
            if (startupWindow == null)
            {
                Log.ForContext<OperationsApp>().Warning("Startup: the router returned NO window for args={Args} -- nothing to show, exiting", SafeArgs(args));
                Shutdown();
                return;
            }

            MainWindow = startupWindow;
            Log.ForContext<OperationsApp>().Information("Startup: showing {Window}", startupWindow.GetType().Name);
            startupWindow.Show();

            // kor:// deep link on cold launch (a report PDF/email link opened the
            // app) — open the target once the main window is up.
            var coldLink = KorUriScheme.FindLink(args);
            if (coldLink is not null)
            {
                _ = KorDeepLink.OpenAsync(coldLink);
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            UnregisterGlobalExceptionHandlers();

            try { _pipeServer?.StopAsync().GetAwaiter().GetResult(); } catch (Exception ex) { Log.ForContext<OperationsApp>().Warning(ex, "Pipe server stop failed. {ErrorType}: {ErrorMessage}", ex.GetType().Name, ex.Message); } // sync-over-async OK: app shutdown; UI message pump tearing down
            try { _guard?.Dispose(); } catch (Exception ex) { Log.ForContext<OperationsApp>().Warning(ex, "Single-instance guard dispose failed. {ErrorType}: {ErrorMessage}", ex.GetType().Name, ex.Message); }

            // Round 39c (T2.003): dispose the DI root so Singleton IDisposables
            // (WorkloadMeetingPanelViewModel cancels _disposeCts; Serilog provider
            // flushes; ODBC handles release; etc.) run their disposers. Without
            // this, those background CTSs keep running until the process exits,
            // and any in-flight notes flush silently aborts.
            try
            {
                (_services as IDisposable)?.Dispose();
            }
            catch (Exception ex)
            {
                Log.ForContext<OperationsApp>().Warning(ex, "DI service provider dispose failed. {ErrorType}: {ErrorMessage}", ex.GetType().Name, ex.Message);
            }

            Log.ForContext<OperationsApp>().Information("App exiting (code {Code})", e.ApplicationExitCode);
            Log.CloseAndFlush();   // never lose the tail of the log on exit
            base.OnExit(e);
        }

        private void RegisterGlobalExceptionHandlers()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        }

        private void UnregisterGlobalExceptionHandlers()
        {
            DispatcherUnhandledException -= OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
            AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
        }

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Log.ForContext<OperationsApp>().Error(
                e.Exception,
                "Unhandled UI dispatcher exception. {ErrorType}: {ErrorMessage}",
                e.Exception.GetType().FullName,
                e.Exception.Message);

            var decision = UnhandledExceptionPolicy.Decide(e.Exception);
            TryShowUnhandledExceptionMessage(decision);
            e.Handled = decision.CanContinue;
            if (!decision.CanContinue)
            {
                Shutdown(-1);
            }
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Log.ForContext<OperationsApp>().Error(
                e.Exception,
                "Unobserved task exception. {ErrorType}: {ErrorMessage}",
                e.Exception.GetType().FullName,
                e.Exception.Message);

            var decision = UnhandledExceptionPolicy.Decide(e.Exception);
            TryShowUnhandledExceptionMessage(decision);
            if (decision.CanContinue)
            {
                e.SetObserved();
            }
            else
            {
                Dispatcher.BeginInvoke((Action)(() => Shutdown(-1)));
            }
        }

        private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception
                ?? new InvalidOperationException("A non-Exception object reached the AppDomain unhandled exception handler.");

            Log.ForContext<OperationsApp>().Fatal(
                exception,
                "Unhandled AppDomain exception. Runtime terminating: {IsTerminating}",
                e.IsTerminating);

            var decision = UnhandledExceptionPolicy.Decide(exception);
            TryShowUnhandledExceptionMessage(e.IsTerminating ? decision with { CanContinue = false } : decision);
        }

        private void TryShowUnhandledExceptionMessage(UnhandledExceptionDecision decision)
        {
            try
            {
                void Show() => MessageBox.Show(
                    decision.UserMessage,
                    decision.CanContinue ? "KOR - Something went wrong" : "KOR - Application closing",
                    MessageBoxButton.OK,
                    decision.CanContinue ? MessageBoxImage.Warning : MessageBoxImage.Error);

                if (Dispatcher.CheckAccess())
                {
                    Show();
                }
                else
                {
                    Dispatcher.Invoke((Action)Show);
                }
            }
            catch (Exception ex)
            {
                Log.ForContext<OperationsApp>().Warning(
                    ex,
                    "Failed to show global exception message. {ErrorType}: {ErrorMessage}",
                    ex.GetType().FullName,
                    ex.Message);
            }
        }

        private static void ClearProcessProxyEnvVars()
        {
            string[] keys = { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "http_proxy", "https_proxy", "all_proxy", "no_proxy" };
            foreach (var k in keys)
            {
                try { Environment.SetEnvironmentVariable(k, null, EnvironmentVariableTarget.Process); } catch (Exception ex) { Log.ForContext<OperationsApp>().Warning(ex, "Failed clearing proxy env var {Key}. {ErrorType}: {ErrorMessage}", k, ex.GetType().Name, ex.Message); }
            }
        }
    }
}
