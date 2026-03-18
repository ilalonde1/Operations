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

public sealed class DeltekOdbcOptions
{
    public string Dsn { get; init; } = "";
    public string OdbcDsn { get; init; } = "";
    public string User { get; init; } = "";
    public string Password { get; init; } = "";
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
}

public sealed class UserOptions
{
    public string UserUpnOverride { get; init; } = "";
    public string DefaultFromEmail { get; init; } = "";
    public string DefaultFromDomain { get; init; } = "";
}

public sealed class FinancialsOptions
{
    public string PnLGlFlipSign { get; init; } = "";
    public string FiscalYearStartMonth { get; init; } = "";
    public string PnLEngRate { get; init; } = "";
    public string PnLDraftRate { get; init; } = "";
    public string PnLOtherDirectRate { get; init; } = "";
    public string PnLOverheadRate { get; init; } = "";
}
