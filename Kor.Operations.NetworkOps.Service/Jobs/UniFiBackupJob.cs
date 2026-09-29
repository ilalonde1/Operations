#nullable enable
using Kor.Operations.NetworkOps.Core.Backups;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Renci.SshNet;

namespace Kor.Operations.NetworkOps.Service.Jobs;

// Nightly: pull the UniFi controller's backups off KOR-UNIFI01 onto FS01, so the controller's config
// never lives only on the controller. KOR-UNIFI01 stages its auto-backups at 03:15 into a read-only,
// chrooted SFTP drop (user korbackup: key only, no shell, no write); this pulls them at 04:00 into
// \\KOR-FS01\Library\ADMIN\UNIFI\BACKUPS (inheritance broken: admins + this service only -- the files
// hold Wi-Fi keys and device credentials). The VM holds no Windows credential at all.
//
// ⚠ OFF since 2026-09-29 (UniFiBackupHost = ""): on UniFi OS Server with a UI account the system backups
// go to Ubiquiti's CLOUD (weekly), not to disk, so the drop never fills and this would alarm nightly for
// nothing. Local copies would need a no-MFA local controller admin whose password sits on APP01 -- Ian's
// call was no; KOR-UNIFI01 goes into Veeam instead. To turn this on: set UniFiBackupHost back to
// kor-unifi01.int.korstructural.com once something writes .unf files to the drop.
//
// The host key is PINNED: an SFTP server that is not KOR-UNIFI01 is refused before authentication.
// "No backup newer than 48 h" THROWS, so the dispatcher records the run as failed and alerts: a backup
// that silently stopped is the failure this job exists to catch.
internal sealed class UniFiBackupJob(IOptions<NetworkOpsOptions> options, ILogger<UniFiBackupJob> log) : INetworkOpsJob
{
    public const string JobName = "UniFiBackup";
    public string Name => JobName;

    public Task<string> RunAsync(CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.UniFiBackupHost))
            return Task.FromResult("not configured (UniFiBackupHost empty)");
        if (string.IsNullOrWhiteSpace(o.UniFiBackupKeyPath) || !File.Exists(o.UniFiBackupKeyPath))
            throw new InvalidOperationException($"UniFi backup key not found at '{o.UniFiBackupKeyPath}' (KOR_NETWORKOPS_UNIFIBACKUPKEYPATH)");
        if (string.IsNullOrWhiteSpace(o.UniFiBackupHostKeySha256) || string.IsNullOrWhiteSpace(o.UniFiBackupDestination))
            throw new InvalidOperationException("UniFiBackupHostKeySha256 and UniFiBackupDestination must both be set");

        var pin = NormalisePin(o.UniFiBackupHostKeySha256);
        var now = DateTime.UtcNow;
        var keep = TimeSpan.FromDays(o.UniFiBackupKeepDays);
        Directory.CreateDirectory(o.UniFiBackupDestination);

        using var key = new PrivateKeyFile(o.UniFiBackupKeyPath);
        using var sftp = new SftpClient(new ConnectionInfo(o.UniFiBackupHost, o.UniFiBackupUser, new PrivateKeyAuthenticationMethod(o.UniFiBackupUser, key))
        {
            Timeout = TimeSpan.FromSeconds(30),
        });
        string? seen = null;
        sftp.HostKeyReceived += (_, e) => { seen = NormalisePin(e.FingerPrintSHA256); e.CanTrust = seen == pin; };
        try { sftp.Connect(); }
        catch (Renci.SshNet.Common.SshConnectionException) when (seen is not null && seen != pin)
        {
            throw new InvalidOperationException($"{o.UniFiBackupHost} presented host key SHA256:{seen}, not the pinned SHA256:{pin}: refused, nothing read");
        }

        var remote = sftp.ListDirectory("/unifi")
            .Where(f => f.IsRegularFile)
            .Select(f => new BackupFile(f.Name, f.Length, f.LastWriteTimeUtc))
            .ToList();
        var local = new DirectoryInfo(o.UniFiBackupDestination).GetFiles("*.unf")
            .Select(f => new BackupFile(f.Name, f.Length, f.LastWriteTimeUtc))
            .ToList();
        var plan = BackupPull.Plan(remote, local, now, keep, TimeSpan.FromHours(o.UniFiBackupStaleHours));

        foreach (var f in plan.Download)
        {
            ct.ThrowIfCancellationRequested();
            var final = Path.Combine(o.UniFiBackupDestination, f.Name);
            var tmp = final + ".partial";
            using (var fs = File.Create(tmp)) sftp.DownloadFile("/unifi/" + f.Name, fs);
            if (new FileInfo(tmp).Length != f.Size) { File.Delete(tmp); throw new InvalidOperationException($"{f.Name}: downloaded {new FileInfo(tmp).Length} bytes, expected {f.Size}"); }
            File.Move(tmp, final, overwrite: true);
            File.SetLastWriteTimeUtc(final, f.WrittenUtc);   // retention and staleness go by when UniFi wrote it
        }
        foreach (var f in plan.Prune) File.Delete(Path.Combine(o.UniFiBackupDestination, f.Name));
        sftp.Disconnect();

        var summary = $"pulled {plan.Download.Count}, pruned {plan.Prune.Count}, holding {local.Count + plan.Download.Count - plan.Prune.Count}; newest " +
                      (plan.NewestUtc is { } n ? $"{n.ToLocalTime():yyyy-MM-dd HH:mm}" : "none");
        log.LogInformation("UniFi backup: {Summary}", summary);
        if (plan.Stale)
            throw new InvalidOperationException($"UniFi backups have STOPPED: {summary}. Check Auto Backup in the controller (Settings > Control Plane > Backups) and the kor-unifi-backup-stage timer on KOR-UNIFI01.");
        return Task.FromResult(summary);
    }

    /// <summary>"SHA256:abc=" / "abc" / "abc=" all compare equal: the prefix and base64 padding are presentation.</summary>
    internal static string NormalisePin(string s)
    {
        var v = s.Trim();
        if (v.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase)) v = v[7..];
        return v.TrimEnd('=');
    }
}
