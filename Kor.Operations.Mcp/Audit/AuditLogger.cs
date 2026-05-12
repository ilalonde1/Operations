using System.Collections.Concurrent;
using Kor.Operations.Mcp.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Kor.Operations.Mcp.Audit;

/// <summary>
/// Writes one row per authenticated request to Mcp.AuditLog. Failures
/// to write are logged but never thrown: audit-log outages must not
/// break the request path itself.
/// </summary>
public sealed class AuditLogger
{
    // Noise-suppression cache only: audit writes still run best-effort on every call.
    private static readonly ConcurrentDictionary<string, DateTime> _lastLoggedAt = new();

    private readonly IOptions<McpOptions> _options;
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(IOptions<McpOptions> options, ILogger<AuditLogger> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task WriteAsync(AuditEntry entry, CancellationToken ct)
    {
        var opts = _options.Value;
        if (!opts.IsConfigured) return;

        const string sql = @"
INSERT INTO Mcp.AuditLog
    (OccurredAt, UserUpn, ClientApp, ToolName, InputJson, ResultStatus, DurationMs, ErrorMessage)
VALUES
    (SYSUTCDATETIME(), @UserUpn, @ClientApp, @ToolName, @InputJson, @ResultStatus, @DurationMs, @ErrorMessage);";

        try
        {
            await using var conn = new SqlConnection(opts.SqlConnectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@UserUpn",      (object?)entry.UserUpn       ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ClientApp",    (object?)entry.ClientApp     ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ToolName",     (object?)entry.ToolName      ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@InputJson",    (object?)entry.InputJson     ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ResultStatus", entry.ResultStatus);
            cmd.Parameters.AddWithValue("@DurationMs",   entry.DurationMs);
            cmd.Parameters.AddWithValue("@ErrorMessage", (object?)entry.ErrorMessage  ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var now = DateTime.UtcNow;
            if (_lastLoggedAt.Count > 64)
            {
                foreach (var stale in _lastLoggedAt.ToArray().Where(kvp => now - kvp.Value > TimeSpan.FromMinutes(5)))
                {
                    _lastLoggedAt.TryRemove(stale.Key, out _);
                }
            }

            var key = ex.Message ?? string.Empty;
            var shouldLog = !_lastLoggedAt.TryGetValue(key, out var last)
                || now - last > TimeSpan.FromSeconds(60);
            if (shouldLog)
            {
                _lastLoggedAt[key] = now;
                _logger.LogWarning(
                    ex,
                    "Failed to write audit row for {ToolName}; request itself "
                    + "succeeded. (Identical errors within the next 60s will be "
                    + "suppressed.)",
                    entry.ToolName);
            }
        }
    }
}

public sealed record AuditEntry(
    string? UserUpn,
    string? ClientApp,
    string? ToolName,
    string? InputJson,
    string ResultStatus,
    int DurationMs,
    string? ErrorMessage);
