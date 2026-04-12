#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kor.Operations
{
    internal sealed class AppStartupRouter
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<AppStartupRouter> _logger;

        internal AppStartupRouter(IServiceProvider services, ILogger<AppStartupRouter> logger)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<Window?> RouteAsync(string[] args, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var pickerMode = args.Any(a => string.Equals(a, CliArgs.FilePicker, StringComparison.OrdinalIgnoreCase));
            var resultFile = args
                .Where(a => a.StartsWith(CliArgs.PickerResult, StringComparison.OrdinalIgnoreCase))
                .Select(a => a.Substring(CliArgs.PickerResult.Length).Trim('"'))
                .FirstOrDefault();

            if (pickerMode)
            {
                RunEmailPickerMode(resultFile);
                return Task.FromResult<Window?>(null);
            }

            var fileEmailsArg = args
                .FirstOrDefault(a => a.StartsWith(CliArgs.FileEmails, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(fileEmailsArg))
            {
                var raw = fileEmailsArg.Substring(CliArgs.FileEmails.Length).Trim('"');

                var emailFiles = raw
                    .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p =>
                        File.Exists(p) &&
                        (p.EndsWith(".msg", StringComparison.OrdinalIgnoreCase) ||
                         p.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (emailFiles.Count == 0)
                    return Task.FromResult<Window?>(null);

                var picker = _services.GetRequiredService<EmailFilePickerWindow>();
                picker.SetIncomingFiles(emailFiles);

                var resultFilePath = GetPickerResultFilePath(null);
                try { if (File.Exists(resultFilePath)) File.Delete(resultFilePath); } catch (Exception ex) { _logger.LogWarning(ex, "Could not clear old result file before --file-emails session."); }

                picker.Closed += (_, _) =>
                {
                    try
                    {
                        if (picker.FiledSuccessfully && !string.IsNullOrWhiteSpace(picker.SelectedProjectNo))
                            File.WriteAllText(resultFilePath, picker.SelectedProjectNo, Encoding.UTF8);
                        else
                            File.WriteAllText(resultFilePath, string.Empty, Encoding.UTF8);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to write email picker result file after --file-emails session.");
                        try { File.WriteAllText(resultFilePath, string.Empty, Encoding.UTF8); } catch (Exception) { /* last-resort fallback — primary write already logged above */ }
                    }
                };

                return Task.FromResult<Window?>(picker);
            }

            var quickTransferMode = args.Any(a =>
                a.StartsWith(CliArgs.QuickTransfer, StringComparison.OrdinalIgnoreCase));

            if (quickTransferMode)
            {
                var from = GetArgValue(args, CliArgs.From);
                var to = GetArgValue(args, CliArgs.To);
                var cc = GetArgValue(args, CliArgs.Cc);
                var subject = GetArgValue(args, CliArgs.Subject);

                var wnd = _services.GetRequiredService<QuickTransferWindow>();
                wnd.InitializeRequest(from, to, cc, subject);
                return Task.FromResult<Window?>(wnd);
            }

            var emailSearchMode = args.Any(a =>
                string.Equals(a, CliArgs.EmailSearch, StringComparison.OrdinalIgnoreCase));

            var fileArgs = args.Where(File.Exists).ToList();

            if (emailSearchMode)
                return Task.FromResult<Window?>(_services.GetRequiredService<EmailSearchWindow>());

            if (fileArgs.Count > 0)
            {
                var main = _services.GetRequiredService<MainWindow>();
                try
                {
                    main.LoadInitialFiles(fileArgs);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "LoadInitialFiles failed for startup file args; continuing to open main window.");
                }
                return Task.FromResult<Window?>(main);
            }

            return Task.FromResult<Window?>(_services.GetRequiredService<HomeWindow>());
        }

        private void RunEmailPickerMode(string? resultFile)
        {
            try
            {
                var effectiveResultFile = GetPickerResultFilePath(resultFile);

                if (File.Exists(effectiveResultFile))
                {
                    try { File.Delete(effectiveResultFile); } catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete the existing email picker result file at {ResultFilePath}.", effectiveResultFile);
                        // Safe to continue: result file cleanup is best-effort before rewriting the picker output.
                    }
                }

                var picker = _services.GetRequiredService<EmailFilePickerWindow>();
                picker.SetIncomingFiles(Array.Empty<string>());
                var ok = picker.ShowDialog();

                string? projectNo = null;

                if (ok == true)
                    projectNo = picker.SelectedProjectNo;

                if (!string.IsNullOrWhiteSpace(projectNo))
                    File.WriteAllText(effectiveResultFile, projectNo, Encoding.UTF8);
                else
                    File.WriteAllText(effectiveResultFile, string.Empty, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RunEmailPickerMode failed for {ResultFile}.", resultFile);

                try
                {
                    var fallback = GetPickerResultFilePath(resultFile);
                    File.WriteAllText(fallback, string.Empty, Encoding.UTF8);
                }
                catch (Exception writeEx)
                {
                    _logger.LogError(writeEx, "Failed to write the fallback empty email picker result file for {RequestedResultFile}.", resultFile);
                    // Safe to stop here: this is a best-effort fallback after startup routing has already failed.
                }
            }
        }

        private static string GetPickerResultFilePath(string? resultFile)
        {
            if (!string.IsNullOrWhiteSpace(resultFile))
                return resultFile;

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var korDir = Path.Combine(appData, "KOR");
            Directory.CreateDirectory(korDir);
            return Path.Combine(korDir, "EmailFilePickerResult.txt");
        }

        private static string GetArgValue(string[] args, string prefix)
        {
            foreach (var a in args)
            {
                if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var value = a.Substring(prefix.Length);
                    if (value.Length >= 2 && value.StartsWith("\"") && value.EndsWith("\""))
                        value = value.Substring(1, value.Length - 2);
                    return value;
                }
            }

            return string.Empty;
        }
    }
}
