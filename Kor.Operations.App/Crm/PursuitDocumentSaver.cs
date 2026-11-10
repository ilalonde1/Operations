#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.Crm;
using Serilog;

namespace Kor.Operations.App.Crm;

/// <summary>What a Save all did: written now, already in the folder with the same content, and those not saved, each named.</summary>
public sealed record PursuitDocumentSaveResult(int Saved, int AlreadyThere, IReadOnlyList<string> NotSaved);

/// <summary>
/// Copies a pursuit's stored files into a folder a person chose. A file appears under its name only once it is complete and matches
/// its stored fingerprint, as the opener requires; a different file of the same name is never overwritten; saving twice writes nothing
/// the second time, numbered copies included; and one file that cannot be saved is named and does not stop the others.
/// </summary>
public static class PursuitDocumentSaver
{
    private enum Outcome { Saved, AlreadyThere, NoContent }

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
            if (!file.HasContent || file.Sha256 is not { Length: 32 } sha)   // no fingerprint: nothing could say the copy is right, and the opener refuses it too
            {
                notSaved.Add(file.FileName);
                continue;
            }
            try
            {
                var outcome = await SaveOneAsync(store, file.Id, PursuitFileOpener.SafeFileName(file.FileName), sha, folder, ct).ConfigureAwait(false);
                if (outcome == Outcome.Saved) saved++;
                else if (outcome == Outcome.AlreadyThere) already++;
                else notSaved.Add(file.FileName);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Warning(ex, "Save all: file {Id} {Name} was not saved", file.Id, file.FileName);
                notSaved.Add(file.FileName);
            }
        }
        return new PursuitDocumentSaveResult(saved, already, notSaved);
    }

    private static async Task<Outcome> SaveOneAsync(IPursuitFileStore store, long id, string name, byte[] sha256, string folder, CancellationToken ct)
    {
        // Every name in the numbered sequence, so a copy saved as "RFP (2).pdf" last time is found this time.
        var path = Path.Combine(folder, name);
        for (var i = 2; File.Exists(path); i++)
        {
            if (await SameContentAsync(path, sha256, ct).ConfigureAwait(false)) return Outcome.AlreadyThere;
            path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}");
        }

        await using var source = await store.OpenContentAsync(id, ct).ConfigureAwait(false);
        if (source is null) return Outcome.NoContent;
        var partial = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            await using (var target = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await source.CopyToAsync(target, 81920, ct).ConfigureAwait(false);
            if (!await SameContentAsync(partial, sha256, ct).ConfigureAwait(false))
                throw new InvalidDataException("The stored file did not match its fingerprint.");
            File.Move(partial, path);   // no overwrite: a file that appeared since the walk throws, and is named as not saved
            return Outcome.Saved;
        }
        finally
        {
            File.Delete(partial);
        }
    }

    private static async Task<bool> SameContentAsync(string path, byte[] sha256, CancellationToken ct)
    {
        await using var existing = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, true);
        return CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(existing, ct).ConfigureAwait(false), sha256);
    }
}
