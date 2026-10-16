#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Crm;

namespace Kor.Operations.App.Crm;

public interface IPursuitFileOpener
{
    Task OpenAsync(PursuitFile file, CancellationToken ct = default);
}

/// <summary>Small filesystem seam for cache gates; production uses streaming file IO.</summary>
internal interface IPursuitFileCache
{
    string TempRoot { get; }
    bool Exists(string path);
    Stream OpenRead(string path);
    Stream Create(string path);
    void CreateDirectory(string path);
    void Move(string source, string destination);
    void Delete(string path);
    void ShellOpen(string path);
}

public sealed class PursuitFileOpener : IPursuitFileOpener
{
    private readonly IPursuitFileStore _store;
    private readonly LanPursuitFileStorage _legacy;
    private readonly IPursuitFileCache _cache;
    private readonly SemaphoreSlim _openLock = new(1, 1);

    public PursuitFileOpener(IPursuitFileStore store, LanPursuitFileStorage legacy)
        : this(store, legacy, new FileCache()) { }

    internal PursuitFileOpener(IPursuitFileStore store, LanPursuitFileStorage legacy, IPursuitFileCache cache)
        => (_store, _legacy, _cache) = (store, legacy, cache);

    public async Task OpenAsync(PursuitFile file, CancellationToken ct = default)
    {
        if (!file.HasContent)
        {
            _cache.ShellOpen(_legacy.ResolveOpenPath(file.LocalPath));
            return;
        }
        if (file.Sha256 is not { Length: 32 }) throw new InvalidDataException("The attachment has no valid fingerprint.");
        await _openLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var folder = Path.Combine(_cache.TempRoot, "kor-pursuit-files", file.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var path = Path.Combine(folder, SafeFileName(file.FileName));
            if (!await HashMatchesAsync(path, file.Sha256, ct).ConfigureAwait(false))
            {
                _cache.CreateDirectory(folder);
                var partial = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".partial");
                try
                {
                    await using (var source = await _store.OpenContentAsync(file.Id, ct).ConfigureAwait(false)
                        ?? throw new FileNotFoundException("The attachment content could not be found."))
                    await using (var target = _cache.Create(partial))
                        await source.CopyToAsync(target, 81920, ct).ConfigureAwait(false);
                    if (!await HashMatchesAsync(partial, file.Sha256, ct).ConfigureAwait(false))
                        throw new InvalidDataException("The downloaded attachment did not match its fingerprint. Try opening it again.");
                    _cache.Move(partial, path);
                }
                finally { _cache.Delete(partial); }
            }
            ct.ThrowIfCancellationRequested();
            _cache.ShellOpen(path);
        }
        finally { _openLock.Release(); }
    }

    private async Task<bool> HashMatchesAsync(string path, byte[] expected, CancellationToken ct)
    {
        if (!_cache.Exists(path)) return false;
        await using var source = _cache.OpenRead(path);
        var actual = await SHA256.HashDataAsync(source, ct).ConfigureAwait(false);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    internal static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim().Trim('.');
        return string.IsNullOrWhiteSpace(name) ? "attachment" : name;
    }

    private sealed class FileCache : IPursuitFileCache
    {
        public string TempRoot => Path.GetTempPath();
        public bool Exists(string path) => File.Exists(path);
        public Stream OpenRead(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        public Stream Create(string path) => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public void Move(string source, string destination) => File.Move(source, destination, overwrite: true);
        public void Delete(string path) => File.Delete(path);
        public void ShellOpen(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
