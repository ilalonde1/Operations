namespace Kor.Operations.Mcp.Options;

/// <summary>
/// Strongly-typed view of the "Mcp" config section. Bound at startup so
/// missing/malformed config fails fast rather than at first request.
/// </summary>
public sealed class McpOptions
{
    /// <summary>Service-account username clients send via HTTP Basic.</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>Service-account password (paired with <see cref="Username"/>).</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// SQL Server connection string used for both (a) the audit/COO Card stores and
    /// (b) the AI's data queries — the same instance hosts the writable local DBs and
    /// the read-only DELTEK_VP linked server, so one connection serves both.
    /// </summary>
    public string SqlConnectionString { get; init; } = string.Empty;

    /// <summary>Anthropic API key used by the /ask endpoint's LLM loop.</summary>
    public string AnthropicApiKey { get; init; } = string.Empty;

    /// <summary>Anthropic model ID. Defaults to Sonnet 4.6 — fast enough for chat, capable enough for analysis.</summary>
    public string AnthropicModel { get; init; } = "claude-sonnet-4-6";

    /// <summary>Hard timeout (seconds) on each AI-issued SQL query. Caps run-away cost on the DB.</summary>
    public int SqlQueryTimeoutSeconds { get; init; } = 30;

    /// <summary>Maximum rows returned to the LLM per query. Keeps the tool result inside the model's context budget.</summary>
    public int SqlQueryRowCap { get; init; } = 1000;

    /// <summary>Optional employee IDs hidden from employee-performance AI output; empty is fine for normal deployments.</summary>
    public IReadOnlyCollection<string> EmployeeSummaryExcludedIds { get; init; } = Array.Empty<string>();

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password) &&
        !string.IsNullOrWhiteSpace(SqlConnectionString);

    /// <summary>True if the AI features (/ask + LLM loop) can run. Distinct from the basic auth/audit gate.</summary>
    public bool AiIsConfigured =>
        IsConfigured && !string.IsNullOrWhiteSpace(AnthropicApiKey);
}
