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

    /// <summary>SQL Server connection string for the audit log + COO Card stores.</summary>
    public string SqlConnectionString { get; init; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password) &&
        !string.IsNullOrWhiteSpace(SqlConnectionString);
}
