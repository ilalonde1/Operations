#nullable enable
using System.IO;
using System.Security.Cryptography;
using Kor.Operations.App.Crm;
using Kor.Opportunities.Data.Crm;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// Covers byte/fingerprint/size preservation, pre-store size refusal, cache reuse and
/// invalidation, interrupted downloads and legacy opens. Uses an in-memory store and
/// cache; does not execute SQL or launch a real shell. A SQL permission or provider
/// streaming failure would escape these fakes and still needs the handoff's real proof.
/// </summary>
public sealed class PursuitFileStorageTests
{
    [Fact]
    public async Task StoreAsync_RoundTripsThreeMegabytes_AndReturnsTheCommittedRow()
    {
        var bytes = new byte[3 * 1024 * 1024];
        RandomNumberGenerator.Fill(bytes);
        using var source = new AttachmentFixtureFile(bytes);
        var store = new MemoryPursuitFileStore();
        var storage = new DbPursuitFileStorage(store);
        var upload = await storage.StoreAsync(12, 34, "Pursuit", source.Path, "actor", CancellationToken.None);
        Assert.True(storage.IsConfigured);
        var row = Assert.Single(store.Rows);
        Assert.Same(row, upload.File);
        Assert.Equal(bytes, store.Bytes[row.Id]);
        Assert.Equal(SHA256.HashData(bytes), upload.Sha256);
        Assert.Equal(row.Sha256, upload.Sha256);
        Assert.Equal(bytes.LongLength, upload.SizeBytes);
        Assert.Equal(row.SizeBytes, upload.SizeBytes);
        Assert.True(row.HasContent);
        Assert.Null(row.LocalPath);
        Assert.Equal("", upload.StoredPath);
        Assert.Equal(12, row.EngagementId);
        Assert.Equal(34L, row.OpportunityId);
        Assert.Equal("actor", row.UploadedBy);
        storage.TryDelete(upload.StoredPath);
        Assert.Single(store.Rows);
    }

    [Fact]
    public async Task OversizeStream_IsRefusedBeforeAnyStoreCall_AndNamesTheLimit()
    {
        var store = new MemoryPursuitFileStore();
        var storage = new DbPursuitFileStorage(store);
        using var stream = new UnreadableOversizeStream();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => storage.StoreStreamAsync(
            1, null, "large.mp4", stream, stream.Length, "actor", CancellationToken.None));
        Assert.Contains("250 MB", error.Message, StringComparison.Ordinal);
        Assert.Empty(store.Operations);
        Assert.Equal(0, stream.Reads);
        // The SQL store independently enforces the same cap before opening a connection.
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SqlPursuitFileStore("unused").AddAsync(
            1, null, "large.mp4", stream, stream.Length, null, "actor", CancellationToken.None));
    }

    [Fact]
    public async Task Opener_WritesOnce_ReusesHashMatch_AndReplacesAnAlteredCopy()
    {
        var store = new MemoryPursuitFileStore();
        using var content = new MemoryStream(new byte[] { 1, 2, 3 });
        var row = await store.AddAsync(1, 2, "proposal.pdf", content, 3, "application/pdf", "actor", CancellationToken.None);
        var cache = new MemoryPursuitFileCache();
        var opener = new PursuitFileOpener(store, new LanPursuitFileStorage(() => throw new InvalidOperationException("Not a legacy open")), cache);
        await opener.OpenAsync(row);
        await opener.OpenAsync(row);
        var path = System.IO.Path.Combine(cache.TempRoot, "kor-pursuit-files", row.Id.ToString(), "proposal.pdf");
        Assert.Equal(new[] { path, path }, cache.Opened);
        Assert.Equal(1, cache.Creates);
        Assert.Equal(1, store.Opens);
        Assert.Equal(store.Bytes[row.Id], cache.Files[path]);
        cache.Files[path] = new byte[] { 9 };
        await opener.OpenAsync(row);
        Assert.Equal(2, cache.Creates);
        Assert.Equal(2, store.Opens);
        Assert.Equal(store.Bytes[row.Id], cache.Files[path]);
        Assert.Single(cache.Files);
    }

    [Fact]
    public async Task Opener_RejectsCorruptOrInterruptedDownloads_AndCleansPartialFiles()
    {
        foreach (var interrupted in new[] { false, true })
        {
            var store = new MemoryPursuitFileStore();
            using var content = new MemoryStream(new byte[] { 1, 2, 3 });
            var row = await store.AddAsync(1, null, "proposal.pdf", content, 3, null, "actor", CancellationToken.None);
            if (interrupted) store.OpenOverride = () => new InterruptedStream();
            else store.Bytes[row.Id] = new byte[] { 8 };
            var cache = new MemoryPursuitFileCache();
            var opener = new PursuitFileOpener(store, new LanPursuitFileStorage(() => ""), cache);
            // A short stream surfaces as an IOException; a complete stream whose bytes do not
            // match the row's fingerprint is corrupt data, which is InvalidDataException.
            if (interrupted) await Assert.ThrowsAnyAsync<IOException>(() => opener.OpenAsync(row));
            else await Assert.ThrowsAsync<InvalidDataException>(() => opener.OpenAsync(row));
            Assert.Empty(cache.Opened);
            Assert.Empty(cache.Files);
        }
    }

    [Fact]
    public async Task Opener_LegacyPathDoesNotReadContent_AndDatabaseNameCannotEscapeItsFolder()
    {
        var cache = new MemoryPursuitFileCache();
        var store = new MemoryPursuitFileStore();
        var path = System.IO.Path.Combine(cache.TempRoot, "legacy.pdf");
        var row = new PursuitFile(5, 1, null, "legacy.pdf", path, null, null, null, DateTimeOffset.UtcNow, "actor");
        await new PursuitFileOpener(store, new LanPursuitFileStorage(() => "unused"), cache).OpenAsync(row);
        Assert.Equal(path, Assert.Single(cache.Opened));
        Assert.Equal(0, store.Opens);
        Assert.Equal(0, cache.Creates);
        Assert.Equal("proposal.pdf", PursuitFileOpener.SafeFileName("../proposal.pdf"));
    }

    private sealed class UnreadableOversizeStream : MemoryStream
    {
        public int Reads { get; private set; }
        public override long Length => SqlPursuitFileStore.MaxAttachmentBytes + 1;
        public override int Read(byte[] buffer, int offset, int count) { Reads++; throw new InvalidOperationException("Must not read an oversize stream."); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { Reads++; throw new InvalidOperationException("Must not read an oversize stream."); }
    }

    private sealed class InterruptedStream : MemoryStream
    {
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken ct)
            => Task.FromException(new IOException("Interrupted download"));
    }
}

internal sealed class AttachmentFixtureFile : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
    public AttachmentFixtureFile(byte[]? bytes = null) => File.WriteAllBytes(Path, bytes ?? new byte[] { 1, 2, 3 });
    public void Dispose() => File.Delete(Path);
}

internal sealed class MemoryPursuitFileStore : IPursuitFileStore
{
    public List<PursuitFile> Rows { get; } = new();
    public Dictionary<long, byte[]> Bytes { get; } = new();
    public List<string> Operations { get; } = new();
    public Func<Stream>? OpenOverride { get; set; }
    public string? ReturnedFileName { get; set; }
    public int Opens { get; private set; }
    public Task<PursuitFile> AddAsync(long engagementId, long? opportunityId, string fileName,
        Stream content, long declaredLength, string? contentType, string uploadedBy, CancellationToken ct)
    {
        Operations.Add("add");
        using var copy = new MemoryStream();
        ct.ThrowIfCancellationRequested();
        // Keep this small in-memory fake synchronous for the existing STA modal
        // dialog harness. Link-completion ordering is tested with a separate task gate.
        content.CopyTo(copy);
        Assert.Equal(declaredLength, copy.Length);
        var bytes = copy.ToArray(); // small fixtures only; production streams to SQL
        var row = new PursuitFile(100 + Rows.Count, engagementId, opportunityId, ReturnedFileName ?? fileName, null,
            SHA256.HashData(bytes), bytes.LongLength, contentType, DateTimeOffset.UtcNow, uploadedBy, true);
        Rows.Add(row);
        Bytes.Add(row.Id, bytes);
        return Task.FromResult(row);
    }
    public Task<PursuitFile> AddAsync(long engagementId, long? opportunityId, string fileName, string localPath,
        byte[]? sha256, long? sizeBytes, string? contentType, string uploadedBy, CancellationToken ct)
        => throw new InvalidOperationException("New attachments must not use the legacy overload.");
    public Task<IReadOnlyList<PursuitFile>> ListByEngagementAsync(long engagementId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<PursuitFile>>(Rows.Where(r => r.EngagementId == engagementId).ToArray());
    public Task<Stream?> OpenContentAsync(long id, CancellationToken ct)
    {
        Opens++;
        return Task.FromResult<Stream?>(OpenOverride is not null ? OpenOverride() : new MemoryStream(Bytes[id], writable: false));
    }
    public Task<string?> RemoveAsync(long id, CancellationToken ct)
    {
        Operations.Add("remove");
        Rows.RemoveAll(r => r.Id == id);
        Bytes.Remove(id);
        return Task.FromResult<string?>(null);
    }
}

internal sealed class MemoryPursuitFileCache : IPursuitFileCache
{
    public string TempRoot => System.IO.Path.GetTempPath();
    public Dictionary<string, byte[]> Files { get; } = new();
    public List<string> Opened { get; } = new();
    public int Creates { get; private set; }
    public bool Exists(string path) => Files.ContainsKey(path);
    public Stream OpenRead(string path) => new MemoryStream(Files[path], writable: false);
    public Stream Create(string path) { Creates++; return new CaptureStream(bytes => Files[path] = bytes); }
    public void CreateDirectory(string path) { }
    public void Move(string source, string destination) { Files[destination] = Files[source]; Files.Remove(source); }
    public void Delete(string path) => Files.Remove(path);
    public void ShellOpen(string path) => Opened.Add(path);
    private sealed class CaptureStream(Action<byte[]> save) : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing) save(ToArray());
            base.Dispose(disposing);
        }
    }
}
