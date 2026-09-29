#nullable enable
using Kor.Operations.NetworkOps.Core.Health;

namespace Kor.Operations.NetworkOps.Core.Learning;

/// <param name="Subject">What the number is about -- a drive letter, a disk, a process, a mailbox file; "" when the PC itself.</param>
public sealed record MetricPoint(string Metric, string Subject, double Value);

// The numbers tracked over time for every PC -- the raw material of prediction. One sweep adds one
// point per series; the trend over those points is what warns before something breaks.
public static class Metrics
{
    public const string DiskFreeGb = "disk.free.gb";
    public const string DiskFreePct = "disk.free.pct";
    public const string SsdWearPct = "disk.wear.pct";
    public const string DiskTempC = "disk.temp.c";
    public const string DiskReadErrors = "disk.read.errors";
    public const string DiskWriteErrors = "disk.write.errors";
    public const string DiskPowerOnHours = "disk.poweron.hours";
    public const string GpuHangs14d = "gpu.hangs.14d";
    public const string DiskResets14d = "disk.resets.14d";
    public const string DiskBadBlocks14d = "disk.badblocks.14d";
    public const string Whea14d = "whea.14d";
    public const string Shutdowns14d = "shutdowns.14d";
    public const string ResourceExhaustion14d = "memory.exhaustion.14d";
    public const string UpdateFailures14d = "update.failures.14d";
    public const string AppHangs14d = "app.hangs.14d";
    public const string AppCrashes14d = "app.crashes.14d";
    public const string BootMedianMs = "boot.median.ms";
    public const string MailStoreGb = "mail.store.gb";
    public const string BatteryHealthPct = "battery.health.pct";
    public const string UptimeHours = "uptime.hours";

    public static IReadOnlyList<MetricPoint> Extract(HealthSnapshot s)
    {
        var m = new List<MetricPoint>();
        void Add(string metric, string subject, double? v) { if (v is { } x && !double.IsNaN(x)) m.Add(new(metric, subject, x)); }

        foreach (var v in s.Volumes.Where(v => v.SizeGB > 0))
        {
            Add(DiskFreeGb, v.Letter, v.FreeGB);
            Add(DiskFreePct, v.Letter, Math.Round(v.FreeGB / v.SizeGB * 100, 1));
        }
        foreach (var d in s.DiskReliability)
        {
            var subject = d.Name ?? d.Serial ?? "disk";
            Add(SsdWearPct, subject, d.WearPct);
            Add(DiskTempC, subject, d.TemperatureC);
            Add(DiskReadErrors, subject, d.ReadErrors);
            Add(DiskWriteErrors, subject, d.WriteErrors);
            Add(DiskPowerOnHours, subject, d.PowerOnHours);
        }
        if (s.Events14d is { } e)
        {
            Add(GpuHangs14d, "", e.GpuHang?.Count);
            Add(DiskResets14d, "", e.DiskResets?.Count);
            Add(DiskBadBlocks14d, "", e.DiskBadBlock?.Count);
            Add(Whea14d, "", e.Whea?.Count);
            Add(Shutdowns14d, "", e.UnexpectedShutdown?.Count);
            Add(ResourceExhaustion14d, "", e.ResourceExhaustion?.Count);
            Add(UpdateFailures14d, "", e.UpdateFailures?.Count);
            Add(AppHangs14d, "", e.AppHangs?.Count);
        }
        foreach (var c in s.AppCrashes14d) Add(AppCrashes14d, c.Process.ToLowerInvariant(), c.Count);
        Add(BootMedianMs, "", s.Boot?.MedianBootMs);
        foreach (var ms in s.MailStores) Add(MailStoreGb, $@"{ms.Profile}\{ms.Name}", ms.GB);
        Add(BatteryHealthPct, "", s.Battery?.HealthPct);
        Add(UptimeHours, "", s.Os?.UptimeHours);
        return m;
    }
}
