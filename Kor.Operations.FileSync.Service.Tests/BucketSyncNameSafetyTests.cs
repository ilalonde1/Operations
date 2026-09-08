#nullable enable
using Kor.Operations.FileSync.Service.Jobs.Watcher;
using Kor.Operations.Graph;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph.Models;
using Xunit;

namespace Kor.Operations.FileSync.Service.Tests;

// Differential harness for the SharePoint-name fix (2026-09-08).
//
// WHAT IT COMPARES: for the Stickfile bucket, across two consecutive syncs of
// the same unchanged folder, it asserts (a) an NTFS-legal / SharePoint-illegal
// name uploads under its sanitized name, (b) the second pass is a pure no-op --
// zero uploads, zero deletes, everything SkippedSame -- which is the property a
// naive "trim only at upload" would violate (it would re-upload and then delete
// the same file forever), and (c) two on-disk names that reduce to one
// SharePoint name never fight over one remote item or delete each other.
//
// WHAT IT DOES NOT COVER: it uses a fake IGraphFacade, so it does not exercise
// the real Graph client and cannot prove which names SharePoint actually
// rejects -- that boundary lives in SharePointNameTests + SharePoint's own
// contract. It does not test the chunked (>threshold) upload branch, the SSI/
// RFI/Photos buckets, or timestamp-skew edge cases. A SAME-CLASS FAULT IT WOULD
// NOT CATCH: a SharePoint-reserved name that Sanitize does not normalise (e.g.
// "CON.pdf", or a name containing '%') would still fail on the real service;
// the sanitizer's scope comment documents that as a deliberate boundary.
public sealed class BucketSyncNameSafetyTests
{
    private const string DriveId = "drive1";

    private static BucketSyncOp NewOp(IGraphFacade facade) =>
        new(facade, WatcherOptions.FromKnobs(new Dictionary<string, string?>()), DriveId, NullLogger.Instance);

    private static string MakeStickfileRoot(params string[] fileNames)
    {
        var project = "31056-01 (Test Project)";
        var root = Path.Combine(Path.GetTempPath(), "kor-bucketsync-" + Guid.NewGuid().ToString("N"), project, "05 Stickfile");
        Directory.CreateDirectory(root);
        foreach (var n in fileNames)
            File.WriteAllText(Path.Combine(root, n), "pdf-bytes-for-" + n.Trim());
        return root;
    }

    [Fact]
    public async Task Leading_space_file_uploads_trimmed_then_second_pass_is_a_no_op()
    {
        var root = MakeStickfileRoot(" leading.pdf", "clean.pdf");
        // Guard the premise: NTFS must have preserved the leading space.
        Assert.Contains(Directory.GetFiles(root).Select(Path.GetFileName), n => n!.StartsWith(" "));

        var fake = new FakeGraphFacade();
        var op = NewOp(fake);
        var bucket = SyncBucket.ByName("Stickfile")!;

        // Pass 1: remote empty -> both upload; the illegal name lands trimmed.
        var r1 = await op.RunAsync(bucket, root, isShadow: false, CancellationToken.None);
        Assert.Equal(0, r1.Failed);
        Assert.Equal(2, r1.Uploaded);
        Assert.Equal(0, r1.Deleted);
        Assert.Contains("leading.pdf", fake.StoreNames);
        Assert.DoesNotContain(" leading.pdf", fake.StoreNames);   // stored trimmed
        Assert.Contains("clean.pdf", fake.StoreNames);

        // Pass 2: same folder, nothing changed. The trimmed local name must
        // re-match the stored remote item: no re-upload, and crucially NO delete.
        var r2 = await op.RunAsync(bucket, root, isShadow: false, CancellationToken.None);
        Assert.Equal(0, r2.Failed);
        Assert.Equal(0, r2.Uploaded);
        Assert.Equal(0, r2.Deleted);
        Assert.Equal(2, r2.SkippedSame);
        Assert.Empty(fake.Deleted);
    }

    [Fact]
    public async Task Two_names_that_collapse_to_one_SharePoint_name_do_not_churn_or_delete()
    {
        var root = MakeStickfileRoot("dup.pdf", " dup.pdf");
        var fake = new FakeGraphFacade();
        var op = NewOp(fake);
        var bucket = SyncBucket.ByName("Stickfile")!;

        var r1 = await op.RunAsync(bucket, root, isShadow: false, CancellationToken.None);
        Assert.Equal(0, r1.Failed);
        Assert.Equal(1, r1.Uploaded);            // one wins, the other is skipped with a warning
        Assert.Single(fake.StoreNames);

        var r2 = await op.RunAsync(bucket, root, isShadow: false, CancellationToken.None);
        Assert.Equal(0, r2.Failed);
        Assert.Equal(0, r2.Uploaded);
        Assert.Equal(0, r2.Deleted);             // the surviving remote item is not pruned
        Assert.Empty(fake.Deleted);
    }

    // ---- Fake Graph drive: an in-memory folder keyed by item name. Only the
    // members BucketSyncOp calls do real work; the rest are not on this path.
    private sealed class FakeGraphFacade : IGraphFacade
    {
        private readonly Dictionary<string, DriveItem> _store = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Deleted { get; } = new();
        public IEnumerable<string> StoreNames => _store.Keys;

        public Task<string> EnsureFolderAsync(string folderRelativePath, CancellationToken ct) => Task.FromResult("folder1");

        public async IAsyncEnumerable<DriveItem> ListChildrenAsync(
            string driveId, string folderItemId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var item in _store.Values.ToList()) { yield return item; }
            await Task.CompletedTask;
        }

        public Task<DriveItem> UploadSimpleAsync(string driveId, string folderId, string fileName, string localFilePath, CancellationToken ct)
        {
            var fi = new FileInfo(localFilePath);
            var item = new DriveItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = fileName,
                Size = fi.Length,
                File = new FileObject(),
                LastModifiedDateTime = new DateTimeOffset(fi.LastWriteTimeUtc, TimeSpan.Zero),
            };
            _store[fileName] = item;   // conflictBehavior=replace, keyed by name
            return Task.FromResult(item);
        }

        public Task DeleteItemAsync(string driveId, string itemId, CancellationToken ct)
        {
            var hit = _store.FirstOrDefault(kv => kv.Value.Id == itemId);
            if (hit.Key is not null) { _store.Remove(hit.Key); Deleted.Add(hit.Key); }
            return Task.CompletedTask;
        }

        // Small test files stay under SimpleVsChunkedThresholdBytes, so the
        // chunked path is never taken here.
        public Task<GraphUploadResult> UploadToFolderAsync(string folderId, string fileName, string localFilePath, IProgress<(string file, long sent, long total)>? progress, int? chunkSizeBytes, CancellationToken ct)
            => throw new NotSupportedException("chunked upload not exercised by this test");

        // ---- Not on the BucketSyncOp sync path. ----
        public Task<string> ReserveTransmittalNumberAsync(string? projectNumber) => throw new NotSupportedException();
        public Task<GraphUploadResult> UploadWithMetadataAsync(string folderRelativePath, string fileName, string localFilePath, IProgress<(string file, long sent, long total)>? progress, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> UploadWithProgressAsync(string folderRelativePath, string fileName, string localFilePath, IProgress<(string file, long sent, long total)>? progress, CancellationToken ct) => throw new NotSupportedException();
        public Task<CreateLinksResult> CreateLinksAsync(string folderRelativePath, bool needExternal, CancellationToken ct) => throw new NotSupportedException();
        public Task SendMailAsync(object header, string coverSheetServerUrl, string? coverSheetLocalPath, bool attachCover, CancellationToken ct, string? senderUpn, IEnumerable<string>? toAndCcEmails) => throw new NotSupportedException();
        public Task SendSimpleMailAsync(string senderUpn, IEnumerable<string> toEmails, string subject, string htmlBody, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream?> TryGetUserPhotoAsync(string userPrincipalName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DriveItem> EnsureFolderPathAsync(string driveId, string relativePath, CancellationToken ct) => throw new NotSupportedException();
        public Task<GraphUploadResult> UploadToFolderAsync(string folderId, string fileName, string localFilePath, IProgress<(string file, long sent, long total)>? progress, CancellationToken ct) => throw new NotSupportedException();
        public Task<CreateLinksResult> CreateLinksForFolderAsync(string folderId, bool needExternal, CancellationToken ct) => throw new NotSupportedException();
        public IAsyncEnumerable<DriveItem> ListChildrenByPathAsync(string driveId, string folderRelativePath, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> DownloadAsync(string driveId, string itemId, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> DownloadByPathAsync(string driveId, string relativePath, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryDeleteItemAsync(string driveId, string itemId, CancellationToken ct) => throw new NotSupportedException();
        public Task<DriveItem> RenameItemAsync(string driveId, string itemId, string newName, CancellationToken ct) => throw new NotSupportedException();
        public Task<DriveItem> MoveItemAsync(string driveId, string itemId, string destinationFolderId, string? newName, CancellationToken ct) => throw new NotSupportedException();
        public Task<DriveItem?> TryGetItemByPathAsync(string driveId, string relativePath, CancellationToken ct) => throw new NotSupportedException();
        public IAsyncEnumerable<DriveItem> ListChildrenByPathIfExistsAsync(string driveId, string folderRelativePath, CancellationToken ct) => throw new NotSupportedException();
    }
}
