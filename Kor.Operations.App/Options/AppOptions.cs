#nullable enable
#pragma warning disable SA1649
namespace Kor.Operations.App.Options;

public sealed class GraphOptions
{
    public string TenantId { get; init; } = "";
    public string ClientId { get; init; } = "";
    public string DriveId { get; init; } = "";
    public string RedirectorBaseUrl { get; init; } = "";
}

public sealed class DatabaseOptions
{
    public string KorTransmittalsDb { get; init; } = "";
    public string KorTransmittals { get; init; } = "";
}

public sealed class StorageOptions
{
    public string ProjectsRoot { get; init; } = "";
    public string StandardDetailsFileStorageRootPath { get; init; } = "";
    public string BrochureSharedProposalsRootPath { get; init; } = "";
}

public sealed class UserOptions
{
    public string UserUpnOverride { get; init; } = "";
    public string DefaultFromEmail { get; init; } = "";
    public string DefaultFromDomain { get; init; } = "";
}

public sealed class CompensationOptions
{
    public double PoolRate { get; init; } = 0.10;
}

public sealed class OpportunitiesOptions
{
    /// <summary>SQL Server connection string for KorOpportunitiesDb.</summary>
    public string OpportunitiesDb { get; init; } = "";
}

public sealed class WatchlistSyncOptions
{
    /// <summary>Base URL of the deltek-webhook service, e.g. https://deltek-webhook.korstructural.com</summary>
    public string ServiceUrl { get; init; } = "";
    /// <summary>Basic-auth username (AppApi credential on the service).</summary>
    public string Username { get; init; } = "";
    /// <summary>Basic-auth password (AppApi credential on the service).</summary>
    public string Password { get; init; } = "";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ServiceUrl)
                             && !string.IsNullOrWhiteSpace(Username)
                             && !string.IsNullOrWhiteSpace(Password);
}
