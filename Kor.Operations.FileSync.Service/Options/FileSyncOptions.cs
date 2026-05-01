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
}
