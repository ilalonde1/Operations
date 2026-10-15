using System.Text.Json.Serialization;

namespace Kor.Opportunities.Knowledge.Claims;

public sealed record RelationFile
{
    public string WrittenBy { get; init; } = "";
    public IReadOnlyList<ClaimRelation> Relations { get; init; } = Array.Empty<ClaimRelation>();
    [JsonIgnore] public IReadOnlyList<RelationRejection> Rejections { get; internal set; } = Array.Empty<RelationRejection>();
}
public sealed record ClaimRelation(long From, long To, string Kind, decimal? Confidence, string? Note = null);
public sealed record RelationRejection(int Number, string Reason);
public sealed record RelationValidationResult(IReadOnlyList<ClaimRelation> Accepted, IReadOnlyList<RelationRejection> Rejected, int Skipped);
