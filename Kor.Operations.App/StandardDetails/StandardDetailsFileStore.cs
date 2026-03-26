#nullable enable
#pragma warning disable SA1649
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Kor.Operations.StandardDetails;

internal sealed record StandardDetailsPreparedFile(
    string FolderPath,
    string StoragePath,
    string OriginalFileName,
    string FileExtension,
    string ContentType,
    long ContentLengthBytes,
    string Sha256Hash);

internal sealed record StandardDetailsOpenFileResult(bool FileMissing, string? Note);

internal sealed class StandardDetailsFileStore
{
    private const int FileStoragePathMax = 1024;
    private readonly string _storageRoot;

    internal StandardDetailsFileStore(string storageRoot)
    {
        _storageRoot = storageRoot ?? throw new ArgumentNullException(nameof(storageRoot));
    }

    internal static string NormalizeStorageRoot(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return @"\\Kor-fs01\Drafting";

        var value = configured.Trim().Trim('"');
        if (value.StartsWith(@"\\\\", StringComparison.Ordinal))
            value = @"\\" + value[4..].Replace(@"\\", @"\");

        return value;
    }

    internal async Task<StandardDetailsPreparedFile> PrepareVersionFileAsync(long documentId, string documentTitle, int nextVersion, string sourcePath, string ext)
    {
        var folder = Path.Combine(_storageRoot, "Document Details", $"{ToSafeFolderSegment(documentTitle)} (ID {documentId})", $"v{nextVersion}");
        Directory.CreateDirectory(folder);

        var storagePath = Path.Combine(folder, $"{Guid.NewGuid():N}{ext}");
        File.Copy(sourcePath, storagePath, false);

        if (storagePath.Length > FileStoragePathMax)
            throw new InvalidOperationException($"Generated storage path exceeds {FileStoragePathMax} characters. Shorten the record title.");

        string sha256Hash;
        await using (var fs = File.OpenRead(storagePath))
            sha256Hash = Convert.ToHexString(await SHA256.HashDataAsync(fs));

        var fi = new FileInfo(storagePath);
        return new StandardDetailsPreparedFile(folder, storagePath, Path.GetFileName(sourcePath), ext, GetContentType(ext), fi.Length, sha256Hash);
    }

    internal void CleanupPreparedVersionFile(StandardDetailsPreparedFile? file)
    {
        if (file == null)
            return;

        try
        {
            if (File.Exists(file.StoragePath))
                File.Delete(file.StoragePath);
            if (Directory.Exists(file.FolderPath) && !Directory.EnumerateFileSystemEntries(file.FolderPath).Any())
                Directory.Delete(file.FolderPath, false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Upload cleanup failed for '{file.StoragePath}': {ex.Message}");
        }
    }

    internal void DeleteFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not delete file '{path}': {ex.Message}");
            }
        }
    }

    internal StandardDetailsOpenFileResult OpenVersionFile(string storagePath, byte status, string fallbackStatusText)
    {
        if (!File.Exists(storagePath))
            return new StandardDetailsOpenFileResult(true, null);

        var launchPath = storagePath;
        var note = string.Empty;
        if (status != 4)
            StatusWatermarkRenderer.TryPrepareOpenCopy(storagePath, GetStatusWatermarkText(status, fallbackStatusText), out launchPath, out note);

        Process.Start(new ProcessStartInfo { FileName = launchPath, UseShellExecute = true });
        return new StandardDetailsOpenFileResult(false, string.IsNullOrWhiteSpace(note) ? null : note);
    }

    internal static string ToSafeFolderSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Untitled";

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "Untitled" : cleaned;
    }

    private static string GetContentType(string ext)
        => ext == ".pdf" ? "application/pdf"
        : ext == ".dwg" ? "application/acad"
        : "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private static string GetStatusWatermarkText(byte status, string fallback)
        => status switch
        {
            0 => "DRAFT",
            1 => "SUBMITTED",
            2 => "APPROVED - NOT PUBLISHED",
            3 => "REJECTED",
            _ => string.IsNullOrWhiteSpace(fallback) ? "WORKING COPY" : fallback.ToUpperInvariant()
        };
}
