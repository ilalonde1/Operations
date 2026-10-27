namespace Kor.Operations.NetworkOps.Core.Actions;

/// <summary>
/// Machines a session may run a script on only when it names who approved that one action (Ian, 2026-10-23: "Dont touch
/// 1001 or 302n" -- his own PC, and the licensed Revit box the sessions hold by turns). ONE rule for every road a script
/// can take: POST /devices/{name}/run on APP01 (`netops run`, `netops copy`, the MCP tools all pass through
/// SessionApi.RunAsync) and `netops run --direct` from the PC. Until 2026-10-27 only the MCP tools checked it, so the
/// CLI's APP01 road and --direct both stepped around it.
/// </summary>
public static class ProtectedHosts
{
    public static readonly IReadOnlyList<string> Default = ["KOR-1001", "KOR-302N"];

    /// <summary>The machine's name without a rack description ("KOR-FS01 (file server)" -> "KOR-FS01").</summary>
    public static string Bare(string name)
    {
        var i = name.IndexOf(" (", StringComparison.Ordinal);
        return (i > 0 ? name[..i] : name).Trim();
    }

    public static bool Is(IEnumerable<string> list, string name) => list.Any(p => p.Equals(Bare(name), StringComparison.OrdinalIgnoreCase));

    /// <summary>Why a run on <paramref name="name"/> is refused, or null. Approval is "who, when" in the approver's words.</summary>
    public static string? Refusal(IEnumerable<string> list, string name, string? approvedBy)
        => Is(list, name) && string.IsNullOrWhiteSpace(approvedBy)
            ? $"{Bare(name)} is protected: name who approved this one action (--approved-by \"<who, when>\"), or leave it alone"
            : null;

    /// <summary>The purpose line as audited: the approval rides in it, so the action row says who allowed it.</summary>
    public static string? AuditPurpose(string? purpose, string? approvedBy)
        => string.IsNullOrWhiteSpace(approvedBy) ? purpose : $"{purpose?.Trim()} [approved by: {approvedBy.Trim()}]".Trim();
}
