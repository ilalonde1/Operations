#nullable enable
namespace Kor.Operations.FileSync.Service.Jobs.Watcher;

// Args passed from WatcherHostedService to WatcherSyncRunner via JobDispatcher.
//
// The dispatcher has a `string? args` slot that lands in the JobRuns row;
// using a query-string-style format keeps the row directly readable in the
// Command Center while letting us round-trip strongly-typed inputs in code.
//
// Shapes:
//   op=sync&bucket=Photos&root=<UNC path>          -- one bucket sync pass
//   op=clean&projectDir=<UNC path>                 -- move SP project to _ToBeDeleted
//   op=init&projectDir=<UNC path>                  -- fire all 4 bucket syncs (control-file removal)
internal enum WatcherOp { Sync, Clean, Init }

internal sealed record WatcherArgs(WatcherOp Op, SyncBucket? Bucket, string? Root, string? ProjectDir)
{
    public static string EncodeSync(SyncBucket bucket, string root)
        => $"op=sync&bucket={Encode(bucket.Name)}&root={Encode(root)}";

    public static string EncodeClean(string projectDir)
        => $"op=clean&projectDir={Encode(projectDir)}";

    public static string EncodeInit(string projectDir)
        => $"op=init&projectDir={Encode(projectDir)}";

    public static WatcherArgs Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Watcher args required.", nameof(raw));

        var parts = raw.Split('&', StringSplitOptions.RemoveEmptyEntries);
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parts)
        {
            var eq = p.IndexOf('=');
            if (eq <= 0) continue;
            var key = p[..eq];
            var val = Decode(p[(eq + 1)..]);
            dict[key] = val;
        }

        if (!dict.TryGetValue("op", out var opStr))
            throw new ArgumentException("Missing 'op' in watcher args.", nameof(raw));

        var op = opStr.Trim().ToLowerInvariant() switch
        {
            "sync" => WatcherOp.Sync,
            "clean" => WatcherOp.Clean,
            "init" => WatcherOp.Init,
            _ => throw new ArgumentException($"Unknown op '{opStr}'.", nameof(raw)),
        };

        SyncBucket? bucket = null;
        if (dict.TryGetValue("bucket", out var bn))
            bucket = SyncBucket.ByName(bn) ?? throw new ArgumentException($"Unknown bucket '{bn}'.", nameof(raw));

        dict.TryGetValue("root", out var root);
        dict.TryGetValue("projectDir", out var projectDir);

        return new WatcherArgs(op, bucket, root, projectDir);
    }

    private static string Encode(string s) => Uri.EscapeDataString(s);

    private static string Decode(string s) => Uri.UnescapeDataString(s);
}
