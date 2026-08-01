#nullable enable
namespace Kor.Opportunities.Data.Ingestion.Scraping;

/// <summary>
/// MERX account login (DCC Organization subscription, purchased 2026-07-13) for
/// the live-detail extractor. Flows from KOR_OPPORTUNITIES_MERXUSERNAME /
/// MERXPASSWORD machine env vars on the host via Worker options — same pattern
/// as <see cref="BcBidCredentials"/>. Unconfigured = anonymous extraction
/// (description + contact only; documents and plan holders are login-walled).
/// </summary>
public sealed class MerxCredentials
{
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}
