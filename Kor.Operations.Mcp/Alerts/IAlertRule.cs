namespace Kor.Operations.Mcp.Alerts;

public interface IAlertRule
{
    string RuleId { get; }
    AlertSection Section { get; }
    Task<IReadOnlyList<RichAlert>> RunAsync(CancellationToken ct);
}
