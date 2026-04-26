#nullable enable
#pragma warning disable SA1649
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.EmailCommon;
using Kor.Operations.App.Options;
using Kor.Operations.Data;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.App.Email;

internal sealed class EmailFilingResult
{
    public int FiledCount { get; init; }
    public int SkippedCount { get; init; }
    public string? DestinationFolder { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FiledPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FiledSourcePaths { get; init; } = Array.Empty<string>();
}

internal sealed class EmailFilingService
{
    private static readonly string DebugLogPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KorTransmittals",
            "Logs",
            "EmailFilePicker_MsgReaderDebug.txt");

    private static bool _encodingsRegistered;

    private readonly SqlEmailIndexStore? _emailIndexStore;
    private readonly StorageOptions _storageOptions;
    private readonly ILogger<EmailFilingService> _logger;

    public EmailFilingService(SqlEmailIndexStore? emailIndexStore, StorageOptions storageOptions, ILogger<EmailFilingService> logger)
    {
        _emailIndexStore = emailIndexStore;
        _storageOptions = storageOptions ?? throw new ArgumentNullException(nameof(storageOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<EmailFilingResult> FileEmailsAsync(
        IEnumerable<string> emailPaths,
        string destinationFolder,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(destinationFolder);

        var copied = 0;
        var skipped = 0;
        var errors = new List<string>();
        var filedPaths = new List<string>();
        var filedSourcePaths = new List<string>();
        var projectNumber = GetProjectNumber(destinationFolder);
        foreach (var src in emailPaths)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (!File.Exists(src))
                {
                    skipped++;
                    continue;
                }

                if (!(src.EndsWith(".msg", StringComparison.OrdinalIgnoreCase) ||
                      src.EndsWith(".eml", StringComparison.OrdinalIgnoreCase)))
                {
                    skipped++;
                    continue;
                }

                string fileName = Path.GetFileName(src);
                string destPath = Path.Combine(destinationFolder, fileName);
                destPath = EnsureUniquePath(destPath);

                File.Copy(src, destPath);
                filedPaths.Add(destPath);
                filedSourcePaths.Add(src);
                copied++;

                if (_emailIndexStore != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            EnsureCodePagesEncodingRegistered();

                            var parsed = EmailParser.Parse(destPath);

                            string subject = parsed.Subject ?? string.Empty;
                            string fromEmail = parsed.FromEmail ?? string.Empty;
                            DateTime? sentOnUtc = parsed.SentOnUtc;
                            int attachmentCount = parsed.AttachmentCount;
                            bool hasAttachments = parsed.HasAttachments;
                            string fromDisplay = parsed.FromDisplay ?? string.Empty;
                            string toList = parsed.ToList ?? string.Empty;
                            string ccList = parsed.CcList ?? string.Empty;
                            string bccList = parsed.BccList ?? string.Empty;
                            string bodyText = parsed.BodyText ?? string.Empty;
                            DateTime? receivedOn = parsed.ReceivedOnUtc;

                            await _emailIndexStore.InsertEmailAsync(
                                projectNumber,
                                destPath,
                                subject,
                                fromEmail,
                                sentOnUtc,
                                attachmentCount,
                                hasAttachments,
                                fromDisplay,
                                toList,
                                ccList,
                                bccList,
                                bodyText,
                                receivedOn);
                        }
                        catch (Exception ex)
                        {
                            DebugLog($"Indexing failed for {destPath}: {ex.GetType().Name}: {ex.Message}");
                            _logger.LogWarning(ex, "Email indexing failed for {Path}.", destPath);
                        }
                    });
                }
                else
                {
                    DebugLog("Email filed but index store is null; not indexed.");
                }
            }
            catch (Exception ex)
            {
                errors.Add(src + " -> " + ex.Message);
            }
        }

        if (copied > 0)
            WriteResultFile(projectNumber);

        return Task.FromResult(new EmailFilingResult
        {
            FiledCount = copied,
            SkippedCount = skipped,
            DestinationFolder = destinationFolder,
            Errors = errors,
            FiledPaths = filedPaths,
            FiledSourcePaths = filedSourcePaths
        });
    }

    public string EnsureUniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        string dir = Path.GetDirectoryName(path) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);

        int i = 1;
        string candidate;

        do
        {
            candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            i++;
        } while (File.Exists(candidate));

        return candidate;
    }

    private static void DebugLog(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DebugLogPath)!);

            File.AppendAllText(
                DebugLogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // ignore logging failures
        }
    }

    private static void EnsureCodePagesEncodingRegistered()
    {
        if (_encodingsRegistered)
            return;

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _encodingsRegistered = true;
            DebugLog("CodePagesEncodingProvider registered.");
        }
        catch (Exception ex)
        {
            DebugLog($"Encoding.RegisterProvider failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string GetProjectNumber(string destinationFolder)
    {
        try
        {
            var projectRoot = Directory.GetParent(destinationFolder)?.Parent?.Parent?.Name;
            if (!string.IsNullOrWhiteSpace(projectRoot) && projectRoot.Length >= 8)
                return projectRoot.Substring(0, 8);
        }
        catch
        {
        }

        return string.Empty;
    }

    private static void WriteResultFile(string projectNumber)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var korDir = Path.Combine(appData, "KOR");
            Directory.CreateDirectory(korDir);

            var resultPath = Path.Combine(korDir, "EmailFilePickerResult.txt");
            File.WriteAllText(resultPath, projectNumber ?? string.Empty);
        }
        catch
        {
            // best-effort only; if this fails, originals just will not be tagged
        }
    }
}
