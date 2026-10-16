#nullable enable
using System.Globalization;
using System.Text.Json;

namespace Kor.Opportunities.Data.Crm;

public sealed record PursuitRequirementInput(string Requirement, string SourceRef, string CheckKind, string? CheckParameter);
public sealed record PursuitRequirement(long Id, long EngagementId, int Ordinal, string Requirement,
    string SourceRef, string CheckKind, string? CheckParameter, string Status, string? StatusNote,
    string? CheckedBy, DateTimeOffset? CheckedAtUtc, string CreatedBy, DateTimeOffset CreatedAtUtc);

public interface IPursuitRequirementStore
{
    Task<IReadOnlyList<PursuitRequirement>> ListAsync(long engagementId, CancellationToken ct);
    Task AddAsync(long engagementId, IReadOnlyList<PursuitRequirementInput> items, string by, string? note, CancellationToken ct);
    Task UpdateStatusAsync(long engagementId, long id, string status, string? note, string by,
        bool automatic, CancellationToken ct);
    // Rechecks non-empty under a transaction lock; --replace is the only destructive import path.
    Task ImportAsync(long engagementId, IReadOnlyList<PursuitRequirementInput> items, string by, bool replace, CancellationToken ct);
}

public static class PursuitRequirementValidation
{
    public static IReadOnlyList<string> Kinds { get; } = Array.AsReadOnly(new[] { "Manual", "PhrasePresent", "PageLimit", "FileName" });
    public static IReadOnlyList<string> Statuses { get; } = Array.AsReadOnly(new[] { "Open", "Met", "NotMet", "NotApplicable" });

    public static PursuitRequirementInput[] Parse(string json)
    {
        var items = JsonSerializer.Deserialize<PursuitRequirementInput[]>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new ArgumentException("Checklist must be a JSON array.");
        Validate(items);
        return items;
    }

    public static void Validate(IReadOnlyList<PursuitRequirementInput> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i] ?? throw new ArgumentException($"Item {i + 1} is null.");
            Required(item.Requirement, 1000, $"Item {i + 1}: quote the requirement");
            Required(item.SourceRef, 200, $"Item {i + 1}: source reference");
            if (!Kinds.Contains(item.CheckKind)) throw new ArgumentException($"Item {i + 1}: unknown check kind.");
            if (item.CheckParameter?.Length > 400) throw new ArgumentException($"Item {i + 1}: parameter exceeds 400 characters.");
            if (item.CheckKind != "Manual") Required(item.CheckParameter, 400, $"Item {i + 1}: automatic check parameter");
            if (item.CheckKind == "PageLimit" && (!int.TryParse(item.CheckParameter, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var pages) || pages <= 0))
                throw new ArgumentException($"Item {i + 1}: page limit must be a positive whole number.");
        }
    }

    public static void Identity(long engagementId, string by)
    {
        if (engagementId <= 0) throw new ArgumentException("A pursuit is required.");
        Required(by, 150, "Checked/created by");
    }

    public static void Required(string? value, int max, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max)
            throw new ArgumentException($"{label} is required, at most {max} characters.");
    }
}

public static class PursuitChecklistImport
{
    public static async Task ImportAsync(IPursuitRequirementStore store, long engagementId, string json,
        string by, bool replace, CancellationToken ct)
    {
        var items = PursuitRequirementValidation.Parse(json); // all items before any store call
        PursuitRequirementValidation.Identity(engagementId, by);
        if (!replace && (await store.ListAsync(engagementId, ct).ConfigureAwait(false)).Count != 0)
            throw new InvalidOperationException("The checklist is not empty. Use --replace to replace it.");
        await store.ImportAsync(engagementId, items, by, replace, ct).ConfigureAwait(false);
    }
}
