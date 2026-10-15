using System.Security.Cryptography;
using System.Text.Json;
using Kor.Opportunities.Data.Crm;

namespace Kor.Opportunities.Knowledge.Brief;

/// <summary>JSON codec and import boundary for the shared Data file contract. Hashes always
/// describe the original bytes, including any UTF-8 BOM, not a reserialization.</summary>
public static class DirectorBriefFiles
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static DirectorBriefFile Parse(ReadOnlySpan<byte> bytes) => Read<DirectorBriefFile>(bytes);
    public static DirectorBriefExportFile ParseExport(ReadOnlySpan<byte> bytes) => Read<DirectorBriefExportFile>(bytes);

    private static T Read<T>(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) bytes = bytes[3..];
        return JsonSerializer.Deserialize<T>(bytes, JsonOptions) ?? throw new InvalidDataException("A brief document is required.");
    }

    public static async Task<(int SectionsWritten, IReadOnlyList<string> Rejected)> ImportAsync(
        byte[] exportBytes, byte[] sectionBytes, string fileName, string by, bool force,
        IDirectorBriefStore briefs, CancellationToken ct = default)
    {
        DirectorBriefExportFile export;
        DirectorBriefFile file;
        try { export = ParseExport(exportBytes); file = Parse(sectionBytes); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        { return (0, new[] { "Invalid brief JSON: " + ex.Message }); }
        var errors = DirectorBriefValidation.Validate(file, export).ToList();
        if (string.IsNullOrWhiteSpace(by) || by.Length > 100) errors.Add("Imported by must contain 1–100 characters.");
        if (errors.Count != 0) return (0, errors);
        // The store skips an existing hash first, then validates retirement and snapshots
        // citations under the same transaction as the writes. Import needs no vector search.
        return await briefs.ImportAsync(file, Convert.ToHexString(SHA256.HashData(exportBytes)), Path.GetFileName(fileName),
            Convert.ToHexString(SHA256.HashData(sectionBytes)), by, force, ct);
    }
}
