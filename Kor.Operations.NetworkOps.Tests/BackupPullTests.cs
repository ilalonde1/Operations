#nullable enable
using Kor.Operations.NetworkOps.Core.Backups;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// What the nightly UniFi backup pull fetches, prunes, and when it declares the backups stopped.
//
// WHAT IT COVERS: new files are fetched once; a partial copy (size differs) is fetched again; files
// past retention are pruned and never fetched; only .unf files count; "stale" when nothing is held
// or the newest is past the threshold -- including the first night, before any backup exists.
// WHAT IT DOES NOT: the SFTP transfer, host-key pinning or writing to FS01 (proven by a live run-once).
// A SAME-CLASS FAULT IT WOULD NOT CATCH: a clock skew between the VM and APP01 would shift every
// WrittenUtc; a backup written "in the future" counts as fresh here.
public sealed class BackupPullTests
{
    private static readonly DateTime Now = new(2026, 9, 30, 11, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Keep = TimeSpan.FromDays(90), Stale = TimeSpan.FromHours(48);
    private static BackupFile F(string n, double daysAgo, long size = 25_000) => new(n, size, Now.AddDays(-daysAgo));

    [Fact]
    public void A_new_backup_is_fetched_and_one_already_held_is_not()
    {
        var p = BackupPull.Plan([F("a.unf", 1), F("b.unf", 2)], [F("b.unf", 2)], Now, Keep, Stale);
        Assert.Equal(["a.unf"], p.Download.Select(d => d.Name));
        Assert.False(p.Stale);
    }

    [Fact]
    public void A_partial_copy_is_fetched_again()
        => Assert.Single(BackupPull.Plan([F("a.unf", 1, 25_000)], [F("a.unf", 1, 4_096)], Now, Keep, Stale).Download);

    [Fact]
    public void Files_past_retention_are_pruned_and_never_fetched()
    {
        var p = BackupPull.Plan([F("old.unf", 120), F("new.unf", 1)], [F("older.unf", 100), F("kept.unf", 10)], Now, Keep, Stale);
        Assert.Equal(["new.unf"], p.Download.Select(d => d.Name));
        Assert.Equal(["older.unf"], p.Prune.Select(d => d.Name));
    }

    [Fact]
    public void Only_unf_files_are_backups()
        => Assert.Empty(BackupPull.Plan([F("meta.json", 1), F("notes.txt", 1)], [], Now, Keep, Stale).Download);

    [Fact]
    public void No_backup_at_all_is_stale_so_the_first_night_before_autobackup_is_on_says_so()
    {
        var p = BackupPull.Plan([], [], Now, Keep, Stale);
        Assert.True(p.Stale);
        Assert.Null(p.NewestUtc);
    }

    [Fact]
    public void Stale_once_the_newest_is_past_the_threshold()
    {
        Assert.False(BackupPull.Plan([], [F("a.unf", 1.9)], Now, Keep, Stale).Stale);
        Assert.True(BackupPull.Plan([], [F("a.unf", 2.1)], Now, Keep, Stale).Stale);
    }

    [Theory]
    [InlineData("SHA256:EqihW9l+4pvRH63kcz5ReUDliFQuuPDwAmhGseUKDT8", "EqihW9l+4pvRH63kcz5ReUDliFQuuPDwAmhGseUKDT8=")]
    [InlineData(" sha256:abc ", "abc")]
    public void Host_key_pins_compare_without_prefix_or_padding(string configured, string presented)
        => Assert.Equal(Service.Jobs.UniFiBackupJob.NormalisePin(configured), Service.Jobs.UniFiBackupJob.NormalisePin(presented));

    [Fact]
    public void A_different_host_key_never_matches()
        => Assert.NotEqual(Service.Jobs.UniFiBackupJob.NormalisePin("SHA256:EqihW9l+4pvRH63kcz5ReUDliFQuuPDwAmhGseUKDT8"),
                           Service.Jobs.UniFiBackupJob.NormalisePin("SHA256:EqihW9l+4pvRH63kcz5ReUDliFQuuPDwAmhGseUKDT9"));

    [Fact]
    public void A_fetch_that_lands_tonight_makes_it_fresh()
        => Assert.False(BackupPull.Plan([F("today.unf", 0.3)], [F("a.unf", 5)], Now, Keep, Stale).Stale);
}
