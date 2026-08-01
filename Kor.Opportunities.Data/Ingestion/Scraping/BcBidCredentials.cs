#nullable enable
namespace Kor.Opportunities.Data.Ingestion.Scraping;

/// <summary>BCeID Business credentials for BcBidScraper. Registered as a
/// singleton by the Worker, populated from machine env vars. Empty when not
/// configured - BcBidScraper falls back to anonymous scraping (which will
/// hit captcha and produce zero candidates).</summary>
public sealed class BcBidCredentials
{
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}
