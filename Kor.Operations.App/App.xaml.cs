#nullable enable
using System;
using System.Threading;
using System.Windows;
using Kor.Operations.App.Options;
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
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} ARGS=[{string.Join(" ", e.Args ?? Array.Empty<string>())}]{Environment.NewLine}" +
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
                MessageBox.Show("This application is missing required system configuration and cannot start.\r\nPlease contact IT support.", "Application Not Configured", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            Log.Logger = CompositionHelpers.GetSerilogLogger();
            SecretMigrationRunner.RunOnceAtStartup();
            EnvironmentSecretOverrides.Apply();
            QuestPDF.Settings.License =
                QuestPDF.Infrastructure.LicenseType.Community;
            _services = AppCompositionRoot.BuildServiceProvider();
            Kor.Operations.Services.AppServices.Initialize(_services);
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
                    "Sign-In Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown(-1);
                return;
            }
            ClearProcessProxyEnvVars();

            var args = e.Args ?? Array.Empty<string>();
            _guard = new SingleInstanceGuard(args);
            if (!_guard.IsFirstInstance)
            {
                _guard.ForwardArgsAndExit(args);
                Shutdown();
                return;
            }

            if (_guard.ParticipatesInSingleInstance)
            {
                _pipeServer = new AppPipeServer();
                _pipeServer.Start(_services);
            }

            var startupWindow = await new AppStartupRouter(
                _services,
                _services.GetRequiredService<ILogger<AppStartupRouter>>()).RouteAsync(args, CancellationToken.None).ConfigureAwait(true);
            if (startupWindow == null)
            {
                Shutdown();
                return;
            }

            MainWindow = startupWindow;
            startupWindow.Show();
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { _pipeServer?.StopAsync().GetAwaiter().GetResult(); } catch (Exception ex) { Log.ForContext<OperationsApp>().Warning(ex, "Pipe server stop failed. {ErrorType}: {ErrorMessage}", ex.GetType().Name, ex.Message); }
            try { _guard?.Dispose(); } catch (Exception ex) { Log.ForContext<OperationsApp>().Warning(ex, "Single-instance guard dispose failed. {ErrorType}: {ErrorMessage}", ex.GetType().Name, ex.Message); }
            base.OnExit(e);
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
