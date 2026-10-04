#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kor.Operations.NetworkOps.Core.Health;

// What Probes/health.ps1 reports about one workstation, as C# types. Field names match the
// probe's JSON exactly; a field the probe could not read arrives null and every rule treats
// null as "unknown", never as "healthy".
public sealed record HealthSnapshot
{
    public int ProbeVersion { get; init; }
    public DateTime CollectedAt { get; init; }
    public string Computer { get; init; } = "";
    public OsInfo? Os { get; init; }
    public PendingRebootInfo? PendingReboot { get; init; }
    public IReadOnlyList<VolumeInfo> Volumes { get; init; } = [];
    public IReadOnlyList<PhysicalDiskInfo> PhysicalDisks { get; init; } = [];
    public IReadOnlyList<MissingDiskInfo> MissingDisks { get; init; } = [];
    public IReadOnlyList<string> OrphanDriveLetters { get; init; } = [];
    public EventCounts? Events14d { get; init; }
    public IReadOnlyList<CrashCount> AppCrashes14d { get; init; } = [];
    public IReadOnlyList<StoreIndexFailures> OutlookIndex60d { get; init; } = [];
    public OfficeInfo? Office { get; init; }
    public UpdateInfo? Update { get; init; }
    public string? CoolingMode { get; init; }
    public IReadOnlyList<DisplayAdapterInfo> DisplayAdapters { get; init; } = [];
    public ResidueInfo? Residue { get; init; }
    public IReadOnlyList<RemoteToolInfo> RemoteTools { get; init; } = [];
    public IReadOnlyList<DataVolumeInfo> DataOutsideSystemDrive { get; init; } = [];
    public bool? WmiHealthy { get; init; }
    public IReadOnlyList<string> ProbeErrors { get; init; } = [];

    // ---- probe v2: early-warning signals and the inventory. Absent (null/empty) on v1 snapshots.
    public IReadOnlyList<DiskReliabilityInfo> DiskReliability { get; init; } = [];
    public InventoryInfo? Inventory { get; init; }
    public IReadOnlyList<MailStoreInfo> MailStores { get; init; } = [];
    public BootInfo? Boot { get; init; }
    public BatteryInfo? Battery { get; init; }
    /// <summary>v3: each memory module (slot, size, rated and actual speed).</summary>
    public IReadOnlyList<MemoryModuleInfo> Memory { get; init; } = [];
    /// <summary>v3: how many memory slots the board has.</summary>
    public int? MemorySlots { get; init; }
    /// <summary>v4: who is on the PC at probe time -- active, locked, remote, nobody.</summary>
    public SessionInfo? Session { get; init; }
    /// <summary>v6: whether a magic packet can wake it, and the wired MAC to send one to.</summary>
    public WakeInfo? Wake { get; init; }
    /// <summary>v7: keyboards and mice attached, and when the display switches itself off (0 = never).</summary>
    public ConsoleInfo? Console { get; init; }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>Reads one probe result -- a bare object, or the one-element array the payload publishes.</summary>
    public static HealthSnapshot Parse(string json)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(json) ?? throw new JsonException("empty health snapshot");
        // An empty array, or a scalar/array-of-scalar instead of an object, is a probe that returned junk-but-valid
        // JSON. Fail as a JsonException (the one type HealthSweeper catches per machine) rather than the
        // ArgumentOutOfRange a[0] / InvalidOperation AsObject() that would escape and abort the whole sweep.
        var node = root is System.Text.Json.Nodes.JsonArray a ? (a.Count > 0 ? a[0] : null) : root;
        var obj = node as System.Text.Json.Nodes.JsonObject ?? throw new JsonException("health snapshot is not an object");
        // PowerShell unrolls a one-item list to the bare item, so any list can arrive as a single
        // object (every C:-only machine's Volumes did, 2026-09-28). Wrap it back into a list.
        foreach (var name in ListProperties)
        {
            var key = obj.Select(kv => kv.Key).FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (key is not null && obj[key] is System.Text.Json.Nodes.JsonNode n && n is not System.Text.Json.Nodes.JsonArray)
            {
                obj[key] = null;
                obj[key] = new System.Text.Json.Nodes.JsonArray(n.DeepClone());
            }
        }
        // The same unrolling one level down: a PC with one tracked app sends Inventory.Apps as an object.
        if (obj.FirstOrDefault(kv => kv.Key.Equals(nameof(Inventory), StringComparison.OrdinalIgnoreCase)).Value is System.Text.Json.Nodes.JsonObject inv)
        {
            var appsKey = inv.Select(kv => kv.Key).FirstOrDefault(k => k.Equals("Apps", StringComparison.OrdinalIgnoreCase));
            if (appsKey is not null && inv[appsKey] is System.Text.Json.Nodes.JsonObject single)
            {
                inv[appsKey] = null;
                inv[appsKey] = new System.Text.Json.Nodes.JsonArray(single.DeepClone());
            }
        }
        var s = obj.Deserialize<HealthSnapshot>(Json) ?? throw new JsonException("empty health snapshot");
        // A probe block that failed on the machine can serialise as a list holding one null
        // (KOR-213, broken WMI, 2026-09-28). Unknown must read as empty, never crash a rule.
        return s with
        {
            Volumes = Clean(s.Volumes), PhysicalDisks = Clean(s.PhysicalDisks), MissingDisks = Clean(s.MissingDisks),
            OrphanDriveLetters = Clean(s.OrphanDriveLetters), AppCrashes14d = Clean(s.AppCrashes14d),
            OutlookIndex60d = Clean(s.OutlookIndex60d), DisplayAdapters = Clean(s.DisplayAdapters),
            RemoteTools = Clean(s.RemoteTools), DataOutsideSystemDrive = Clean(s.DataOutsideSystemDrive),
            ProbeErrors = Clean(s.ProbeErrors), DiskReliability = Clean(s.DiskReliability), MailStores = Clean(s.MailStores),
            Memory = Clean(s.Memory),
            Inventory =s.Inventory is null ? null : s.Inventory with { Apps = Clean(s.Inventory.Apps) },
        };
    }

    private static readonly string[] ListProperties =
    [
        nameof(Volumes), nameof(PhysicalDisks), nameof(MissingDisks), nameof(OrphanDriveLetters), nameof(AppCrashes14d),
        nameof(OutlookIndex60d), nameof(DisplayAdapters), nameof(RemoteTools), nameof(DataOutsideSystemDrive), nameof(ProbeErrors), nameof(Memory),
        nameof(DiskReliability), nameof(MailStores),
    ];

    internal static IReadOnlyList<T> Clean<T>(IReadOnlyList<T>? items) where T : class
        => items is null ? [] : items.Where(i => i is not null).ToList();
}

public sealed record OsInfo(string? Product, string? DisplayVersion, int Build, int Ubr, DateTime? LastBoot, int UptimeHours);
public sealed record PendingRebootInfo(bool ComponentServicing, bool WindowsUpdate, bool FileRename);
public sealed record VolumeInfo(string Letter, string? Label, double SizeGB, double FreeGB);
/// <param name="Letters">v10: the drive letters on it, comma-separated ("C", "D,E"); empty = none; null = probe before v10.</param>
/// <param name="System">v10: Windows boots from it; null = probe before v10.</param>
/// <param name="UnmountedGB">v11: GB of data partitions on it with no letter and no folder mount -- data Windows is not showing.</param>
public sealed record PhysicalDiskInfo(string? Name, string? Media, string? Bus, double SizeGB, string? Health, string? Operational,
    string? Letters = null, bool? System = null, double? UnmountedGB = null);
public sealed record MissingDiskInfo(string? Name, string? InstanceId);
public sealed record EventSummary(int Count, DateTime? Last);
public sealed record EventCounts(EventSummary? DiskBadBlock, EventSummary? DiskResets, EventSummary? NtfsCorruption,
    EventSummary? Whea, EventSummary? UnexpectedShutdown, EventSummary? GpuHang,
    EventSummary? ResourceExhaustion = null, EventSummary? UpdateFailures = null, EventSummary? AppHangs = null,
    EventSummary? GpuHangLogEntries = null,   // v3: GpuHang = distinct reports; this = the raw WER entries behind them
    int? GpuHangStaleReports = null,          // v8: reports created before the window, still being re-logged (not counted)
    IReadOnlyList<GpuReport>? GpuHangReports = null);   // v8: the reports behind the count, newest first

/// <summary>One WER LiveKernelEvent 141 report. Created is null when its folder is gone (it cannot be dated, so it is not counted).</summary>
public sealed record GpuReport(string ReportId, DateTime? Created, int Entries, bool Queued);

public sealed record DiskReliabilityInfo(string? Name, string? Serial, int? WearPct, int? TemperatureC, int? TemperatureMaxC,
    long? ReadErrors, long? ReadErrorsUncorrected, long? WriteErrors, long? PowerOnHours);

public sealed record AppVersion(string Name, string? Version);

public sealed record InventoryInfo(string? Manufacturer, string? Model, string? MachineType, string? Serial,
    string? BoardMaker, string? BoardProduct, string? BiosVersion, string? BiosDate, string? Cpu, int? Cores, int? RamGB,
    string? LoggedOnUser, string? OfficeBuild, string? OfficeChannel, IReadOnlyList<AppVersion>? Apps);

public sealed record MailStoreInfo(string Profile, string Name, double GB, DateTime? LastWrite);

public sealed record BootInfo(int LastBootMs, int MedianBootMs, int Samples);

public sealed record BatteryInfo(int DesignMWh, int FullChargeMWh, int HealthPct, int? CycleCount);
/// <param name="State">Active | Locked | RemoteOnly | Nobody.</param>
/// <param name="IdleSeconds">Since the console user last touched the keyboard or mouse (v5; only when the agent ran the probe).</param>
public sealed record SessionInfo(string? ConsoleUser, string State, string? LockedSince, string Summary, int? IdleSeconds = null);
public sealed record ConsoleInfo(int? DisplayOffAfterSeconds, int Keyboards, int Mice);
/// <param name="Pme">The NIC's "PME" (power-management event) setting as the driver shows it; null when the driver has none.</param>
public sealed record WakeNic(string Mac, string? Description, bool Up, bool MagicPacket, bool Armed, string? Pme);
/// <param name="FastStartup">HiberbootEnabled: 1 = on (blocks wake from shutdown on many NICs), 0 = off.</param>
/// <param name="LenovoWakeOnLan">The BIOS WakeOnLAN value on Lenovo (Disabled | Primary | Automatic | Boot Order | AC Only…); null elsewhere.</param>
public sealed record WakeInfo(int FastStartup, IReadOnlyList<WakeNic>? Nics, string? LenovoWakeOnLan)
{
    /// <summary>The wired NIC a magic packet goes to: the connected one, else the first.</summary>
    public WakeNic? Wired => (Nics ?? []).Where(n => n is not null).OrderByDescending(n => n.Up).FirstOrDefault();
}
public sealed record MemoryModuleInfo(string? Slot, string? Bank, int SizeGB, int RatedMTs, int ConfiguredMTs, string? Maker, string? Part);
public sealed record CrashCount(string Process, int Count, DateTime? Last);
public sealed record StoreIndexFailures(string Store, int Count, DateTime? Last);
public sealed record OfficeInfo(string? C2rMso, string? DownlevelMso, bool AccessEngine);
public sealed record UpdateInfo(bool MicrosoftUpdate, int? NoAutoUpdate, DateTime? LastInstall, string? LastInstallTitle, string? LastInstallBy);
// Width/Height = the resolution the card is DRIVING now (probe v12+; 0/null on an older probe or when it drives no display).
public sealed record DisplayAdapterInfo(string? Name, string? Driver, int? ErrorCode, int? Width = null, int? Height = null);
public sealed record ResidueInfo(int NewformaProfiles, IReadOnlyList<string>? NewformaInstalled);
public sealed record RemoteToolInfo(string Name, string? State, string? StartMode);
public sealed record DataVolumeInfo(string Letter, double UsedGB);
