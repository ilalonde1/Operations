#nullable enable
using System.Text.RegularExpressions;

namespace Kor.Operations.Mcp.Ai;

/// <summary>
/// Startup gate: refuses to start the MCP service if the auto-discovered tool
/// inventory and the system prompt disagree. Drift detection without tests.
/// Run from Program.cs after DI builds the container.
/// </summary>
public static partial class PromptToolParityValidator
{
    private static readonly Regex BacktickToolRef =
        new(@"`(get_[a-z_]+|query_kor_data)`", RegexOptions.Compiled);

    private static readonly Regex CatalogBulletToolRef =
        new(@"^\s*-\s+(get_[a-z_]+|query_kor_data)\b(?::|\s|$)", RegexOptions.Compiled | RegexOptions.Multiline);

    public static void Validate(IReadOnlyCollection<string> registeredTools, string systemPrompt)
    {
        var errors = new List<string>();

        // 1. Every registered tool must be cited at least once in the system prompt.
        foreach (var tool in registeredTools)
        {
            if (!systemPrompt.Contains($"`{tool}`", StringComparison.Ordinal))
                errors.Add($"Tool '{tool}' is registered but not referenced in the system prompt as `{tool}`.");
        }

        // 2. Every `get_*` / `query_kor_data` reference in the prompt must be a registered tool.
        // Scan both explicit backticked references and tool-catalog bullets
        // ("- get_x ..." or "- get_x: ..."). The latter catches prompt/tool
        // drift in the human-readable catalog even when the tool name is not
        // backticked.
        var referenced = BacktickToolRef.Matches(systemPrompt).Cast<Match>()
            .Concat(CatalogBulletToolRef.Matches(systemPrompt).Cast<Match>())
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var name in referenced)
        {
            if (!registeredTools.Contains(name, StringComparer.Ordinal))
                errors.Add($"System prompt references `{name}` but no such tool is registered.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "MCP tool / system-prompt parity check failed at startup. Fix one of these or the service will not run:\n  - "
                + string.Join("\n  - ", errors));
        }
    }
}
