#nullable enable
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Crm;

namespace Kor.Operations.App.Crm;

public sealed class DbPursuitFileStorage(IPursuitFileStore store) : IPursuitFileStorage
{
    public bool IsConfigured => true;

    public async Task<PursuitFileUpload> StoreAsync(long engagementId, long? opportunityId, string pursuitLabel,
        string sourcePath, string uploadedBy, CancellationToken ct)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await StoreStreamAsync(engagementId, opportunityId, Path.GetFileName(sourcePath), source,
            source.Length, uploadedBy, ct).ConfigureAwait(false);
    }

    internal async Task<PursuitFileUpload> StoreStreamAsync(long engagementId, long? opportunityId, string fileName,
        Stream source, long declaredLength, string uploadedBy, CancellationToken ct)
    {
        SqlPursuitFileStore.ValidateLength(declaredLength); // before calling even an alternate store
        var saved = await store.AddAsync(engagementId, opportunityId, fileName, source, declaredLength,
            ContentTypeFor(fileName), uploadedBy, ct).ConfigureAwait(false);
        return new PursuitFileUpload(saved.FileName, string.Empty, saved.Sha256!, saved.SizeBytes!.Value, saved.ContentType, saved);
    }

    public void TryDelete(string? storedPath)
    {
        if (!string.IsNullOrWhiteSpace(storedPath)) LanPursuitFileStorage.TryDelete(storedPath);
    }

    internal static string? ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".doc" => "application/msword",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".mp4" => "video/mp4",
        ".txt" => "text/plain",
        _ => "application/octet-stream",
    };
}
