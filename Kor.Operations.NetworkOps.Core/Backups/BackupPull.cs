#nullable enable
namespace Kor.Operations.NetworkOps.Core.Backups;

/// <summary>One backup file: its name, size, and when it was written (UTC).</summary>
public sealed record BackupFile(string Name, long Size, DateTime WrittenUtc);

/// <param name="Download">Remote files not yet held locally (or held at a different size: a partial copy).</param>
/// <param name="Prune">Local files past the retention period.</param>
/// <param name="NewestUtc">The newest backup that will be held once the downloads land; null when there is none at all.</param>
/// <param name="Stale">No backup, or the newest one older than the stale threshold: the backups have stopped.</param>
public sealed record BackupPlan(IReadOnlyList<BackupFile> Download, IReadOnlyList<BackupFile> Prune, DateTime? NewestUtc, bool Stale);

// Pulling a device's backups to the file server, as a decision separate from the transfer: what to
// fetch, what to let go, and whether the backups have stopped. The failure that matters is silence --
// a backup job that quietly stopped months ago -- so "no new backup for two days" is itself a failure.
public static class BackupPull
{
    public static BackupPlan Plan(IEnumerable<BackupFile> remote, IEnumerable<BackupFile> local, DateTime nowUtc, TimeSpan keep, TimeSpan staleAfter)
    {
        var held = local.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var remoteList = remote.Where(r => r.Name.EndsWith(".unf", StringComparison.OrdinalIgnoreCase)).ToList();

        // Only files within the retention window are worth fetching: an old one would be pruned at once.
        var download = remoteList
            .Where(r => nowUtc - r.WrittenUtc <= keep)
            .Where(r => !held.TryGetValue(r.Name, out var l) || l.Size != r.Size)
            .OrderBy(r => r.WrittenUtc)
            .ToList();

        var prune = held.Values.Where(l => nowUtc - l.WrittenUtc > keep).OrderBy(l => l.WrittenUtc).ToList();

        var kept = held.Values.Where(l => nowUtc - l.WrittenUtc <= keep).Select(l => l.WrittenUtc)
            .Concat(download.Select(d => d.WrittenUtc)).ToList();
        DateTime? newest = kept.Count == 0 ? null : kept.Max();
        return new BackupPlan(download, prune, newest, newest is null || nowUtc - newest.Value > staleAfter);
    }
}
