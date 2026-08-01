#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Kor.Operations.App.Crm;

/// <summary>Where a copied attachment landed on the share, plus its fingerprint.</summary>
public sealed record PursuitFileUpload(
    string FileName, string StoredPath, byte[] Sha256, long SizeBytes, string? ContentType);

/// <summary>
/// Copies pursuit attachments to the LAN share and removes them (Ian,
/// 2026-07-08). The DB index rows live in <c>IPursuitFileStore</c>; this owns
/// the bytes. LAN-only by design — no SharePoint/Graph.
/// </summary>
public interface IPursuitFileStorage
{
    /// <summary>True when a share root is configured; the UI hides the feature otherwise.</summary>
    bool IsConfigured { get; }

    /// <summary>Copies <paramref name="sourcePath"/> into this pursuit's folder on
    /// the share (dedup-renamed on collision), streaming the bytes.</summary>
    Task<PursuitFileUpload> StoreAsync(long engagementId, string pursuitLabel, string sourcePath, CancellationToken ct);

    /// <summary>Best-effort delete of a stored file. Never throws.</summary>
    void TryDelete(string storedPath);
}

public sealed class LanPursuitFileStorage : IPursuitFileStorage
{
    private readonly string _root;

    public LanPursuitFileStorage(string root) => _root = root?.Trim() ?? string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_root);

    public async Task<PursuitFileUpload> StoreAsync(long engagementId, string pursuitLabel, string sourcePath, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "Pursuit files aren't set up — the 'BD.PursuitFilesRoot' share isn't configured.");
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("That file no longer exists.", sourcePath);
        }

        // Take ONLY the leaf name of the source and scrub it — a dropped path
        // can never escape the pursuit folder (path-traversal guard).
        var safeName = SanitizeFileName(Path.GetFileName(sourcePath));
        var folder = Path.Combine(_root, PursuitFolder(engagementId, pursuitLabel));
        Directory.CreateDirectory(folder);

        // Copy with a small retry (review fix 2026-07-08): another user
        // attaching the same name to the same pursuit can win the
        // File.Exists→Copy race — overwrite:false throws rather than clobber
        // their bytes, so recompute the unique name and try again.
        var target = string.Empty;
        for (var attempt = 0; ; attempt++)
        {
            target = UniquePath(folder, safeName);
            try
            {
                // File.Copy streams under the hood — safe for large videos.
                await Task.Run(() => File.Copy(sourcePath, target, overwrite: false), ct).ConfigureAwait(false);
                break;
            }
            catch (IOException) when (attempt < 5 && File.Exists(target))
            {
                // A racer created this name between the check and the copy; loop.
            }
        }

        var info = new FileInfo(target);
        // Hash the SOURCE (usually local), not the share copy — halves network
        // I/O for a big video and avoids reading the whole file back off the LAN.
        var sha = await ComputeSha256Async(sourcePath, ct).ConfigureAwait(false);

        return new PursuitFileUpload(
            FileName: Path.GetFileName(target),
            StoredPath: target,
            Sha256: sha,
            SizeBytes: info.Length,
            ContentType: GuessContentType(target));
    }

    public void TryDelete(string storedPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(storedPath) && File.Exists(storedPath))
            {
                File.Delete(storedPath);
            }
        }
        catch
        {
            // The DB row is already gone; a stranded file on the share is a
            // minor cleanup, never worth surfacing an error to the user.
        }
    }

    private string PursuitFolder(long engagementId, string? label)
    {
        var slug = SanitizeFileName(label ?? string.Empty);
        slug = slug.Length > 60 ? slug[..60].TrimEnd() : slug;
        return string.IsNullOrWhiteSpace(slug) ? $"pursuit-{engagementId}" : $"{engagementId} - {slug}";
    }

    private static string SanitizeFileName(string name)
    {
        name = name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        // Also strip any lingering separators the invalid-char set may miss on
        // some platforms, and collapse leading dots ("..", ".").
        name = name.Replace('/', '_').Replace('\\', '_').TrimStart('.', ' ');
        return string.IsNullOrWhiteSpace(name) ? "file" : name;
    }

    private static string UniquePath(string folder, string fileName)
    {
        var candidate = Path.Combine(folder, fileName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var n = 2; n < 10000; n++)
        {
            candidate = Path.Combine(folder, $"{stem} ({n}){ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Astronomically unlikely; fall back to a stamp to guarantee uniqueness.
        return Path.Combine(folder, $"{stem} ({Guid.NewGuid():N}){ext}");
    }

    private static async Task<byte[]> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1 << 20, useAsync: true);
        using var sha = SHA256.Create();
        return await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
    }

    private static string GuessContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => "PDF",
        ".doc" or ".docx" => "Word document",
        ".xls" or ".xlsx" => "Excel workbook",
        ".ppt" or ".pptx" => "PowerPoint",
        ".msg" or ".eml" => "Email",
        ".mp4" or ".mov" or ".m4v" or ".avi" or ".mkv" or ".wmv" => "Video",
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".heic" or ".tif" or ".tiff" => "Image",
        ".zip" or ".7z" or ".rar" => "Archive",
        ".txt" or ".rtf" => "Text",
        ".dwg" or ".dxf" => "CAD drawing",
        _ => null!,
    };
}
