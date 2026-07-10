#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Kor.Operations
{
    internal sealed class AppPipeServer
    {
        private const string PipeName = "Kor.Transmittals.NamedPipe";
        private CancellationTokenSource? _pipeCts;
        private Task? _pipeTask;
        private IServiceProvider? _services;

        public void Start(IServiceProvider services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _pipeCts = new CancellationTokenSource();
            _pipeTask = Task.Run(() => RunPipeServerAsync(_pipeCts.Token), _pipeCts.Token);
        }

        public async Task StopAsync()
        {
            try { _pipeCts?.Cancel(); } catch (Exception ex) { Log.Warning(ex, "AppPipeServer: cancellation failed."); }

            if (_pipeTask != null)
            {
                try { await _pipeTask.ConfigureAwait(false); } catch (Exception ex) { Log.Warning(ex, "AppPipeServer: pipe task completion failed on shutdown."); }
            }

            _pipeCts?.Dispose();
            _pipeCts = null;
            _pipeTask = null;
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
                        var tokens = line
                            .Split('|')
                            .Select(t => t.Trim())
                            .Where(t => !string.IsNullOrEmpty(t))
                            .ToList();

                        var emailSearchCmd = tokens.Any(t =>
                            string.Equals(t, CliArgs.EmailSearch, StringComparison.OrdinalIgnoreCase));

                        var files = tokens
                            .Where(File.Exists)
                            .ToList();

                        var korLink = tokens.FirstOrDefault(t =>
                            t.StartsWith("kor://", StringComparison.OrdinalIgnoreCase));

                        await Application.Current.Dispatcher.InvokeAsync(
                            () =>
                            {
                                // kor:// deep link forwarded from a second instance
                                // (a report PDF/email link clicked while the app is
                                // already open): surface the app and open the target.
                                if (korLink != null)
                                {
                                    if (Application.Current.MainWindow is { } main)
                                    {
                                        if (main.WindowState == WindowState.Minimized)
                                            main.WindowState = WindowState.Normal;
                                        main.Activate();
                                    }

                                    _ = KorDeepLink.OpenAsync(korLink);
                                }

                                if (emailSearchCmd)
                                {
                                    var existing = Application.Current.Windows
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

                                        if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsLoaded)
                                            win.Owner = Application.Current.MainWindow;

                                        win.Show();
                                        win.Activate();
                                    }
                                }

                                if (Application.Current.MainWindow is MainWindow mw && files.Count > 0)
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
                catch (Exception ex)
                {
                    Log.Error(ex, "AppPipeServer: pipe server loop failed.");
                }
            }
        }
    }
}
