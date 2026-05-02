#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs.Watcher;

// Walks up from `directory` toward (but not past) `watchPath` and returns
// true if the named control file is present at any ancestor. Verbatim
// from watcher.ps1 ShouldIgnoreFolder §50-61. Used to gate every per-file
// sync so a parent's "NOT SYNCED..." flag suppresses subtree activity.
internal static class ControlFileGuard
{
    public static bool ShouldIgnore(string directory, string watchPath, string controlFileName)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;
        DirectoryInfo? curr;
        try { curr = new DirectoryInfo(directory); }
        catch { return false; }

        var rootLower = watchPath.TrimEnd('\\', '/').ToLowerInvariant();

        while (curr is not null)
        {
            var fullLower = curr.FullName.TrimEnd('\\', '/').ToLowerInvariant();
            if (fullLower == rootLower) break;

            var control = Path.Combine(curr.FullName, controlFileName);
            try
            {
                if (File.Exists(control))
                    return true;
            }
            catch
            {
                // best-effort: if the share blips, treat as not-ignored
            }

            curr = curr.Parent;
        }

        return false;
    }

    // True if a file is openable for ReadWrite/None (no other process is
    // writing it) AND its size is unchanged across `stabilitySleepMs`.
    // Mirrors Test-FileUnlockedStable §225-244 in watcher.ps1.
    public static bool IsUnlockedAndStable(string path, int stabilitySleepMs, CancellationToken ct = default)
    {
        if (!File.Exists(path)) return false;

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch
        {
            return false;
        }

        long len1;
        try { len1 = new FileInfo(path).Length; }
        catch { return false; }

        try { Task.Delay(stabilitySleepMs, ct).Wait(ct); }
        catch { return false; }

        if (!File.Exists(path)) return false;

        long len2;
        try { len2 = new FileInfo(path).Length; }
        catch { return false; }

        return len1 == len2;
    }
}
