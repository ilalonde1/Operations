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
    public string Catalog { get; init; } = "";
    public string PrLaborIdEng { get; init; } = "ENG";
    public string PrLaborIdDraft { get; init; } = "DRAFT";
    public double EngRate { get; set; } = 474;             // produces 58% eng share (calibrated Apr 2026)
    public double DraftRate { get; set; } = 655;           // produces 42% draft share (calibrated Apr 2026)
    public double TargetBillingRate { get; set; } = 185;   // portfolio median $/hr (calibrated Apr 2026)
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

public sealed class FinancialsOptions
{
    public string PnLGlFlipSign { get; init; } = "";
    public string FiscalYearStartMonth { get; init; } = "";
    public string PnLEngRate { get; init; } = "";
    public string PnLDraftRate { get; init; } = "";
    public string PnLOtherDirectRate { get; init; } = "";
    public string PnLOverheadRate { get; init; } = "";
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
