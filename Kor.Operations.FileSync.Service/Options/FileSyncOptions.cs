#nullable enable
namespace Kor.Operations.FileSync.Service.Options;

internal sealed class FileSyncOptions
{
    public string TenantId { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string DriveId { get; set; } = string.Empty;

    public string SiteId { get; set; } = string.Empty;

    public string AlertFromAddress { get; set; } = "app-admin@korstructural.com";

    public string AlertRecipient { get; set; } = "ilalonde@korstructural.com";

    public JobMode Mode { get; set; } = JobMode.Shadow;

    public string KorTransmittalsDb { get; set; } = string.Empty;

    public int HeartbeatSeconds { get; set; } = 60;

    // KorMapSync credentials. Env vars on KOR-APP01, exactly like the Graph and
    // SQL settings above -- KOR_FILESYNC_DELTEKUSER / _DELTEKPASSWORD /
    // _MAPBOXTOKEN / _KORSYNCSECRET. Never in knobs, never in source control.
    public string DeltekUser { get; set; } = string.Empty;

    public string DeltekPassword { get; set; } = string.Empty;

    public string MapboxToken { get; set; } = string.Empty;

    public string KorSyncSecret { get; set; } = string.Empty;
}

