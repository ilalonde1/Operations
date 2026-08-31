namespace Kor.Operations.EngineeringTools.Dxf;

/// <summary>
/// Drawings are read from local disk, always.
///
/// 139 sheets over the VPN is four minutes a run; the same sheets on this disk is thirty-seven
/// seconds. That was learned expensively on 2026-08-27 and then left depending on somebody
/// remembering to copy them first -- which is how it drifts back. It is a step now, not a
/// discipline, and it lives here rather than in the publish script so that every path that reads
/// drawings gets it: the publish, the CLI, and anything built on the service.
///
/// The mirror is never trusted, only used. A copy is rebuilt whenever the share holds a different
/// set -- a different count, a name the mirror does not have, a sheet newer than its copy, or a
/// sheet a different size. A cache that can serve a superseded drawing is worse than no cache, and
/// one that silently serves half the set is worse again.
/// </summary>
public static class DrawingMirror
{
    private static readonly Dictionary<string, string> Mirrored = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    /// <summary>Where the mirrors live. One folder per remote path, named from its hash.</summary>
    public static string Root { get; set; } = Path.Combine(Path.GetTempPath(), "kor-drawings");

    /// <summary>
    /// A local folder holding the same sheets, or the path itself when it is already local or
    /// unreachable -- the caller's own "is it there" check then fails exactly as it did before.
    /// </summary>
    public static string Folder(string remoteFolder, string pattern = "*.dxf")
    {
        if (string.IsNullOrWhiteSpace(remoteFolder)) return remoteFolder;
        if (!IsRemote(remoteFolder)) return remoteFolder;
        if (!Directory.Exists(remoteFolder)) return remoteFolder;

        lock (Gate)
        {
            if (Mirrored.TryGetValue(remoteFolder, out string? had)) return had;

            // Push the filter to the filesystem. EnumerateFiles(root, "*.*") followed by a Where()
            // enumerates every file on the volume; over SMB that is the difference between seconds
            // and never.
            var sheets = Directory.GetFiles(remoteFolder, pattern, SearchOption.TopDirectoryOnly);
            if (sheets.Length == 0) { Mirrored[remoteFolder] = remoteFolder; return remoteFolder; }

            string local = Path.Combine(Root, Key(remoteFolder));
            if (!Fresh(local, sheets, pattern))
            {
                if (Directory.Exists(local)) Directory.Delete(local, recursive: true);
                Directory.CreateDirectory(local);
                foreach (string sheet in sheets)
                    File.Copy(sheet, Path.Combine(local, Path.GetFileName(sheet)), overwrite: true);
            }

            // Verified, not assumed: a partial mirror publishes a partial building.
            int here = Directory.GetFiles(local, pattern, SearchOption.TopDirectoryOnly).Length;
            if (here != sheets.Length)
                throw new IOException(
                    $"Mirror of '{remoteFolder}' holds {here} of {sheets.Length} '{pattern}' files. " +
                    "Refusing to read a partial set.");

            Mirrored[remoteFolder] = local;
            return local;
        }
    }

    /// <summary>One file -- the stick-file PDF -- beside its folder's mirror.</summary>
    public static string SingleFile(string remoteFile)
    {
        if (string.IsNullOrWhiteSpace(remoteFile)) return remoteFile;
        if (!IsRemote(remoteFile)) return remoteFile;
        if (!File.Exists(remoteFile)) return remoteFile;

        lock (Gate)
        {
            if (Mirrored.TryGetValue(remoteFile, out string? had)) return had;

            string local = Path.Combine(Root, Key(Path.GetDirectoryName(remoteFile) ?? remoteFile));
            Directory.CreateDirectory(local);
            string copy = Path.Combine(local, Path.GetFileName(remoteFile));

            var source = new FileInfo(remoteFile);
            var have = new FileInfo(copy);
            if (!have.Exists || have.Length != source.Length || have.LastWriteTimeUtc != source.LastWriteTimeUtc)
                File.Copy(remoteFile, copy, overwrite: true);

            if (new FileInfo(copy).Length != source.Length)
                throw new IOException($"Mirror of '{remoteFile}' is a different size than the original.");

            Mirrored[remoteFile] = copy;
            return copy;
        }
    }

    /// <summary>A UNC path or a mapped network drive. Anything already on this machine is left alone.</summary>
    private static bool IsRemote(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return true;
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(path));
            return root is not null && new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Same sheets, same sizes, none of them newer than the copy.</summary>
    private static bool Fresh(string local, IReadOnlyList<string> sheets, string pattern)
    {
        if (!Directory.Exists(local)) return false;
        if (Directory.GetFiles(local, pattern, SearchOption.TopDirectoryOnly).Length != sheets.Count) return false;

        foreach (string sheet in sheets)
        {
            string copy = Path.Combine(local, Path.GetFileName(sheet));
            if (!File.Exists(copy)) return false;

            var source = new FileInfo(sheet);
            var have = new FileInfo(copy);
            if (have.Length != source.Length) return false;
            if (source.LastWriteTimeUtc > have.LastWriteTimeUtc) return false;
        }

        return true;
    }

    private static string Key(string path)
    {
        byte[] hash = System.Security.Cryptography.SHA1.HashData(
            System.Text.Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
        return Convert.ToHexString(hash)[..16];
    }
}
