#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs.Watcher;

// Resolves which sync bucket a directory falls into and computes the
// "bucket root" -- i.e. the local folder that the matching SINGLE_SYNC
// helper would scan. Verbatim from watcher.ps1 Resolve-Target §64-86 and
// the surrounding "only act when the event is in the exact target root"
// guard §360-362.
internal sealed class SyncBucketRouter
{
    public sealed record Resolved(SyncBucket Bucket, string Root);

    public Resolved? Resolve(string anyDirUnderTarget)
    {
        if (string.IsNullOrWhiteSpace(anyDirUnderTarget))
            return null;

        var path = anyDirUnderTarget.Replace('/', '\\');
        var pathLower = path.ToLowerInvariant();

        foreach (var bucket in SyncBucket.All)
        {
            var needle = "\\" + bucket.LocalSubpath.ToLowerInvariant();
            var idx = pathLower.IndexOf(needle, StringComparison.Ordinal);
            if (idx < 0) continue;
            var rootEnd = idx + 1 + bucket.LocalSubpath.Length;
            if (rootEnd > path.Length) continue;
            var root = path.Substring(0, rootEnd);
            return new Resolved(bucket, root);
        }

        return null;
    }

    // Used by control-file-removed handling: pre-build all 4 (bucket, root)
    // pairs for the project so the dispatcher can fire them in turn.
    public IReadOnlyList<Resolved> ResolveAllForProject(string projectDir)
    {
        var list = new List<Resolved>(SyncBucket.All.Count);
        foreach (var bucket in SyncBucket.All)
            list.Add(new Resolved(bucket, Path.Combine(projectDir, bucket.LocalSubpath)));
        return list;
    }

    // True iff the candidate directory IS the bucket root (case-insensitive,
    // trailing-slash tolerant). Mirrors the PS1 §360-362 "only fire when the
    // event is in the exact target root, not a subfolder" rule.
    public static bool IsExactRoot(string candidateDir, string bucketRoot)
    {
        var a = candidateDir.TrimEnd('\\', '/');
        var b = bucketRoot.TrimEnd('\\', '/');
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
