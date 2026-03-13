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

namespace Kor.Operations
{
    internal sealed class AppStartupRouter
    {
        private readonly IServiceProvider _services;

        internal AppStartupRouter(IServiceProvider services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public Task<Window?> RouteAsync(string[] args, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var pickerMode = args.Any(a => string.Equals(a, "--file-picker", StringComparison.OrdinalIgnoreCase));
            var resultFile = args
                .Where(a => a.StartsWith("--picker-result=", StringComparison.OrdinalIgnoreCase))
                .Select(a => a.Substring("--picker-result=".Length).Trim('"'))
                .FirstOrDefault();

            if (pickerMode)
            {
                RunEmailPickerMode(resultFile);
                return Task.FromResult<Window?>(null);
            }

            var fileEmailsArg = args
                .FirstOrDefault(a => a.StartsWith("--file-emails=", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(fileEmailsArg))
            {
                var raw = fileEmailsArg.Substring("--file-emails=".Length).Trim('"');

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
                return Task.FromResult<Window?>(picker);
            }

            var quickTransferMode = args.Any(a =>
                a.StartsWith("--quick-transfer", StringComparison.OrdinalIgnoreCase));

            if (quickTransferMode)
            {
                var from = GetArgValue(args, "--from=");
                var to = GetArgValue(args, "--to=");
                var cc = GetArgValue(args, "--cc=");
                var subject = GetArgValue(args, "--subject=");

                var wnd = _services.GetRequiredService<QuickTransferWindow>();
                wnd.InitializeRequest(from, to, cc, subject);
                return Task.FromResult<Window?>(wnd);
            }

            var emailSearchMode = args.Any(a =>
                string.Equals(a, "--email-search", StringComparison.OrdinalIgnoreCase));

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
                catch
                {
                    // never break startup if something goes wrong here
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
                    try { File.Delete(effectiveResultFile); } catch { }
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
                Debug.WriteLine("RunEmailPickerMode error: " + ex);

                try
                {
                    var fallback = GetPickerResultFilePath(resultFile);
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
