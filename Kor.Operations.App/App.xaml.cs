#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using Kor.Operations.App.Options;
using Kor.Operations.Services;
using Microsoft.Extensions.DependencyInjection;
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
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOR_DB_USER", EnvironmentVariableTarget.Machine)) ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOR_DB_PASSWORD", EnvironmentVariableTarget.Machine)) ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOR_ODBC_USER", EnvironmentVariableTarget.Machine)) ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KOR_ODBC_PASSWORD", EnvironmentVariableTarget.Machine)))
            {
                MessageBox.Show("This application is missing required system configuration and cannot start.\r\nPlease contact IT support.", "Application Not Configured", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            SecretMigrationRunner.RunOnceAtStartup();
            EnvironmentSecretOverrides.Apply();
            _services = AppCompositionRoot.BuildServiceProvider();
            await AppAuthBootstrapper.EnsureGraphInitializedForDelegatedAuthAsync(
                _services.GetRequiredService<GraphOptions>(),
                _services.GetRequiredService<UserOptions>()).ConfigureAwait(true);
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

            var startupWindow = await new AppStartupRouter(_services).RouteAsync(args, CancellationToken.None).ConfigureAwait(true);
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
            try { _pipeServer?.StopAsync().GetAwaiter().GetResult(); } catch (Exception ex) { Debug.WriteLine($"[App] Pipe server stop failed: {ex.GetType().Name}: {ex.Message}"); }
            try { _guard?.Dispose(); } catch (Exception ex) { Debug.WriteLine($"[App] Single-instance guard dispose failed: {ex.GetType().Name}: {ex.Message}"); }
            base.OnExit(e);
        }

        private static void ClearProcessProxyEnvVars()
        {
            string[] keys = { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "http_proxy", "https_proxy", "all_proxy", "no_proxy" };
            foreach (var k in keys)
            {
                try { Environment.SetEnvironmentVariable(k, null, EnvironmentVariableTarget.Process); } catch (Exception ex) { Debug.WriteLine($"[App] Failed clearing proxy env var '{k}': {ex.GetType().Name}: {ex.Message}"); }
            }
        }
    }
}
