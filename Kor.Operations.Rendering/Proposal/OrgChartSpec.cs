#nullable enable
using System.Collections.ObjectModel;
using System.Text.Json;

namespace Kor.Operations.Rendering.Proposal;

public sealed class OrgChartSpec
{
    public string Title { get; set; } = "KOR Structural - Project Organization and Supervision";
    public string Subtitle { get; set; } = "";
    public OrgChartContext Context { get; set; } = new();
    public ObservableCollection<OrgChartPerson> Chain { get; set; } = new();
    public ObservableCollection<OrgChartGroup> Groups { get; set; } = new();
    public string Note { get; set; } = "";
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    public string ToJson() => JsonSerializer.Serialize(this, Options);
    public static OrgChartSpec FromJson(string json) => JsonSerializer.Deserialize<OrgChartSpec>(json, Options)
        ?? throw new ArgumentException("The org chart specification is empty.");
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title)) throw new ArgumentException("Enter an org chart title.");
        if (Context is null || Chain is null || Groups is null || Context.Lines is null)
            throw new ArgumentException("Context, chain and groups must be present.");
        if (Groups.Count > 12 || Chain.Count > 20 || Groups.Any(g => g is null || g.People is null || g.People.Count > 30))
            throw new ArgumentException("Use at most 12 groups, 20 leaders and 30 people per group.");
        if (!Chain.Concat(Groups.SelectMany(g => g.People)).Any()) throw new ArgumentException("Add at least one person.");
        if (Chain.Concat(Groups.SelectMany(g => g.People)).Any(p => p is null || string.IsNullOrWhiteSpace(p.Name)))
            throw new ArgumentException("Every person needs a name.");
    }
}
public sealed class OrgChartContext
{
    public string Name { get; set; } = "";
    public string[] Lines { get; set; } = Array.Empty<string>();
}
public sealed class OrgChartPerson
{
    public string Name { get; set; } = "";
    public string Cred { get; set; } = "";
    public string Title { get; set; } = "";
    public string Role { get; set; } = "";
}
public sealed class OrgChartGroup
{
    public string Name { get; set; } = "Project team";
    public ObservableCollection<OrgChartPerson> People { get; set; } = new();
}
