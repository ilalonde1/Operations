using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kor.Opportunities.Knowledge.Claims;

/// <summary>The export and import document. Import provenance is computed from the
/// original file bytes, never trusted from JSON or reconstructed from serialization.</summary>
public sealed record ClaimFile
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public long SourceId { get; init; }
    public string Title { get; init; } = "";
    public string? Author { get; init; }
    public DateTimeOffset ExportedAtUtc { get; init; }
    public string? WrittenBy { get; init; }
    public IReadOnlyList<ClaimFileConcept> KnownConcepts { get; init; } = Array.Empty<ClaimFileConcept>();
    public IReadOnlyList<ClaimFilePassage> Passages { get; init; } = Array.Empty<ClaimFilePassage>();

    [JsonIgnore] public string? FileName { get; private init; }
    [JsonIgnore] public string? FileSha256 { get; private init; }
    // Import diagnostics accompany the tuple of committed counts without entering the file.
    [JsonIgnore] public IReadOnlyList<ClaimRejection> Rejections { get; internal set; } = Array.Empty<ClaimRejection>();

    public static ClaimFile Parse(ReadOnlySpan<byte> bytes, string fileName)
    {
        var sha = Convert.ToHexString(SHA256.HashData(bytes));
        // Editors may save UTF-8 with a BOM; hash it, but remove it for the JSON reader.
        if (bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) bytes = bytes[3..];
        var file = JsonSerializer.Deserialize<ClaimFile>(bytes, JsonOptions)
            ?? throw new InvalidDataException("A claims document is required.");
        return file with { FileName = Path.GetFileName(fileName), FileSha256 = sha };
    }
}

public sealed record ClaimFileConcept(string Slug, string Name);
public sealed record ClaimFilePassage
{
    [JsonRequired] public long Id { get; init; }
    [JsonRequired] public int Ordinal { get; init; }
    public string? Ref { get; init; }
    public string? Text { get; init; }
    // An explicit [] records that the writer examined a passage and found no claim.
    [JsonRequired] public IReadOnlyList<ClaimFileClaim> Claims { get; init; } = Array.Empty<ClaimFileClaim>();
}
public sealed record ClaimFileClaim
{
    public string Kind { get; init; } = "";
    public string Statement { get; init; } = "";
    public decimal? Confidence { get; init; }
    public IReadOnlyList<ClaimFileConcept> Concepts { get; init; } = Array.Empty<ClaimFileConcept>();
}
