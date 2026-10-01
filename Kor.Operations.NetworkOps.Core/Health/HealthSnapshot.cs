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

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>Reads one probe result -- a bare object, or the one-element array the payload publishes.</summary>
    public static HealthSnapshot Parse(string json)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(json) ?? throw new JsonException("empty health snapshot");
        var obj = (root is System.Text.Json.Nodes.JsonArray a ? a[0] : root)?.AsObject() ?? throw new JsonException("empty health snapshot");
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
public sealed record PhysicalDiskInfo(string? Name, string? Media, string? Bus, double SizeGB, string? Health, string? Operational);
public sealed record MissingDiskInfo(string? Name, string? InstanceId);
public sealed record EventSummary(int Count, DateTime? Last);
public sealed record EventCounts(EventSummary? DiskBadBlock, EventSummary? DiskResets, EventSummary? NtfsCorruption,
    EventSummary? Whea, EventSummary? UnexpectedShutdown, EventSummary? GpuHang,
    EventSummary? ResourceExhaustion = null, EventSummary? UpdateFailures = null, EventSummary? AppHangs = null,
    EventSummary? GpuHangLogEntries = null);   // v3: GpuHang = distinct reports; this = the raw WER entries behind them

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
public sealed record MemoryModuleInfo(string? Slot, string? Bank, int SizeGB, int RatedMTs, int ConfiguredMTs, string? Maker, string? Part);
public sealed record CrashCount(string Process, int Count, DateTime? Last);
public sealed record StoreIndexFailures(string Store, int Count, DateTime? Last);
public sealed record OfficeInfo(string? C2rMso, string? DownlevelMso, bool AccessEngine);
public sealed record UpdateInfo(bool MicrosoftUpdate, int? NoAutoUpdate, DateTime? LastInstall, string? LastInstallTitle, string? LastInstallBy);
public sealed record DisplayAdapterInfo(string? Name, string? Driver, int? ErrorCode);
public sealed record ResidueInfo(int NewformaProfiles, IReadOnlyList<string>? NewformaInstalled);
public sealed record RemoteToolInfo(string Name, string? State, string? StartMode);
public sealed record DataVolumeInfo(string Letter, double UsedGB);
