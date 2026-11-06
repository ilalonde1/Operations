#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Crm;

namespace Kor.Operations.App.Crm;

/// <summary>What a Save all did: written now, already in the folder with the same content, and those with no stored content.</summary>
public sealed record PursuitDocumentSaveResult(int Saved, int AlreadyThere, IReadOnlyList<string> NotSaved);

/// <summary>
/// Copies a pursuit's stored files into a folder a person chose. A file only appears under its name once it is complete, a
/// different file of the same name is never overwritten, and saving twice writes nothing the second time.
/// </summary>
public static class PursuitDocumentSaver
{
    public static async Task<PursuitDocumentSaveResult> SaveAllAsync(IPursuitFileStore store, IReadOnlyList<PursuitFile> files, string folder,
        IProgress<string>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(files);
        Directory.CreateDirectory(folder);
        int saved = 0, already = 0;
        var notSaved = new List<string>();
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(file.FileName);
            if (!file.HasContent)
            {
                notSaved.Add(file.FileName);
                continue;
            }
            var name = PursuitFileOpener.SafeFileName(file.FileName);
            var path = Path.Combine(folder, name);
            if (file.Sha256 is { Length: 32 } && await SameContentAsync(path, file.Sha256, ct).ConfigureAwait(false))
            {
                already++;
                continue;
            }

            await using var source = await store.OpenContentAsync(file.Id, ct).ConfigureAwait(false);
            if (source is null)
            {
                notSaved.Add(file.FileName);
                continue;
            }
            var partial = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".partial");
            try
            {
                await using (var target = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                    await source.CopyToAsync(target, 81920, ct).ConfigureAwait(false);
                File.Move(partial, FreePath(folder, name));
                saved++;
            }
            finally
            {
                File.Delete(partial);
            }
        }
        return new PursuitDocumentSaveResult(saved, already, notSaved);
    }

    private static async Task<bool> SameContentAsync(string path, byte[] sha256, CancellationToken ct)
    {
        if (!File.Exists(path)) return false;
        await using var existing = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(existing, ct).ConfigureAwait(false), sha256);
    }

    private static string FreePath(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}");
        return path;
    }
}
