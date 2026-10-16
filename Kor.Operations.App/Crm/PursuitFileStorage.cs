#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Crm;

namespace Kor.Operations.App.Crm;

/// <summary>The committed attachment and its fingerprint. Database rows have no stored path.</summary>
public sealed record PursuitFileUpload(
    string FileName, string StoredPath, byte[] Sha256, long SizeBytes, string? ContentType, PursuitFile File);

public interface IPursuitFileStorage
{
    bool IsConfigured { get; }
    Task<PursuitFileUpload> StoreAsync(long engagementId, long? opportunityId, string pursuitLabel,
        string sourcePath, string uploadedBy, CancellationToken ct);
    void TryDelete(string? storedPath);
}

/// <summary>Legacy path resolution only. New attachments never write to the share.</summary>
public sealed class LanPursuitFileStorage(Func<string> legacyRoot)
{
    public string ResolveOpenPath(string? localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath)) throw new FileNotFoundException("This attachment has no stored content or legacy path.");
        if (Path.IsPathFullyQualified(localPath)) return localPath;
        var root = legacyRoot();
        if (string.IsNullOrWhiteSpace(root)) throw new FileNotFoundException("The legacy attachment folder is unavailable.");
        return Path.GetFullPath(Path.Combine(root, localPath));
    }

    internal static void TryDelete(string? storedPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(storedPath) && File.Exists(storedPath)) File.Delete(storedPath);
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Could not remove legacy pursuit attachment {Path}.", storedPath); }
    }
}
