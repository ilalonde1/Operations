using System.Security.Cryptography;
using System.Text;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// The job's drawings, on this disk instead of the far end of the projects share.
///
/// The slow half of this suite spends its time walking SMB: three buildings rebuilt from their real
/// plan sheets, every sheet read across the network, every run. Thirteen minutes, and because it
/// holds the build output lock, the next edit cannot compile until it finishes — so a change that
/// takes a minute to make waits a quarter of an hour to be judged, and the loop stops being a loop.
///
/// The drawings do not change while a test run is going, and they rarely change at all: they are
/// issued sheets. Mirroring them once per machine and reading locally afterwards costs one copy and
/// gives every run after it back.
///
/// The mirror is checked, not trusted. If the share has a different number of sheets, or any sheet
/// newer than the copy, the copy is made again — a stale drawing would be the worst possible thing
/// to hide behind a cache, because every number this suite guards is derived from one.
/// </summary>
internal static class DrawingCache
{
    private static readonly Dictionary<string, string> Mirrored = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    /// <summary>
    /// A local folder holding the same .dxf files, or the original path when the share is
    /// unreachable — the caller's own "is it there" check then fails as it always did.
    /// </summary>
    internal static string Local(string remoteFolder)
    {
        lock (Gate)
        {
            if (Mirrored.TryGetValue(remoteFolder, out string? had)) return had;
            if (!Directory.Exists(remoteFolder)) return remoteFolder;

            var sheets = Directory.GetFiles(remoteFolder, "*.dxf", SearchOption.TopDirectoryOnly);
            if (sheets.Length == 0) { Mirrored[remoteFolder] = remoteFolder; return remoteFolder; }

            string local = Path.Combine(
                Path.GetTempPath(), "kor-drawings", Fingerprint(remoteFolder));

            if (!Fresh(local, sheets))
            {
                if (Directory.Exists(local)) Directory.Delete(local, recursive: true);
                Directory.CreateDirectory(local);
                foreach (string sheet in sheets)
                    File.Copy(sheet, Path.Combine(local, Path.GetFileName(sheet)), overwrite: true);
            }

            Mirrored[remoteFolder] = local;
            return local;
        }
    }

    /// <summary>
    /// Same sheets, none of them newer than the copy. Anything else and the mirror is thrown away:
    /// a cache that can serve a superseded drawing is worse than no cache.
    /// </summary>
    private static bool Fresh(string local, IReadOnlyList<string> sheets)
    {
        if (!Directory.Exists(local)) return false;

        var copies = Directory.GetFiles(local, "*.dxf", SearchOption.TopDirectoryOnly);
        if (copies.Length != sheets.Count) return false;

        foreach (string sheet in sheets)
        {
            string copy = Path.Combine(local, Path.GetFileName(sheet));
            if (!File.Exists(copy)) return false;
            if (File.GetLastWriteTimeUtc(sheet) > File.GetLastWriteTimeUtc(copy)) return false;
        }

        return true;
    }

    /// <summary>A stable folder name for a share path, without the share path in it.</summary>
    private static string Fingerprint(string path)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
        return Convert.ToHexString(hash)[..16];
    }
}
