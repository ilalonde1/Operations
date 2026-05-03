#nullable enable
namespace Kor.Opportunities.Core.Ingestion;

/// <summary>
/// Stats from one IngestionService.IngestAsync call. Mirrored onto the
/// matching <see cref="Models.IngestionRun"/> row.
/// </summary>
public sealed record IngestionResult
{
    public int Inserted { get; init; }

    public int Duplicate { get; init; }

    /// <summary>Candidates the provider yielded but the service rejected
    /// before insert (e.g. missing required fields).</summary>
    public int Skipped { get; init; }

    /// <summary>Candidates that threw mid-insert despite passing pre-checks.</summary>
    public int Failed { get; init; }

    public bool Success { get; init; }

    public string? ErrorSummary { get; init; }
}
