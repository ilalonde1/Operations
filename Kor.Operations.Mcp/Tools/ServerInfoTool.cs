using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// First MCP tool: exists purely to prove the wire works end-to-end during
/// Phase 11a. Real business tools land in Phase 11b once the FinancialsService
/// and friends are extracted into a library Kor.Operations.Mcp can reference.
/// </summary>
[McpServerToolType]
public static class ServerInfoTool
{
    [McpServerTool]
    [Description("Health/connectivity check. Returns the server name, version, current UTC time, and echoes the optional input string. Use this to verify the MCP transport is reachable and you are authenticated.")]
    public static PingResponse Ping(
        [Description("Optional message to echo back. Useful for confirming the tool received your input correctly.")] string? input = null)
    {
        var version = typeof(ServerInfoTool).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";

        return new PingResponse(
            Service: "Kor.Operations.Mcp",
            Version: version,
            TimestampUtc: DateTimeOffset.UtcNow,
            Echo: input ?? "(no input)");
    }

    public sealed record PingResponse(
        string Service,
        string Version,
        DateTimeOffset TimestampUtc,
        string Echo);
}
