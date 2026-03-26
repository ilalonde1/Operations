#nullable enable
namespace Kor.Operations.Graph;

/// <summary>
/// Holds the sharing links returned by
/// <see cref="IGraphFacade.CreateLinksAsync"/>.
/// </summary>
public sealed class CreateLinksResult
{
    /// <summary>Internal (organization-only) sharing link.</summary>
    public string InternalLink { get; init; } = string.Empty;

    /// <summary>External (anyone with link) sharing link.
    /// Null if not requested.</summary>
    public string? ExternalLink { get; init; }
}
