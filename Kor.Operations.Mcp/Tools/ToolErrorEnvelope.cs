#nullable enable
using System.Text.Json;

namespace Kor.Operations.Mcp.Tools;

/// <summary>
/// Standard JSON error shape returned by every MCP tool so the LLM sees a
/// consistent envelope and can decide whether to retry, change inputs, or
/// surface an infrastructure problem to the user. Audit Finding 9
/// (Batch 100). Pre-100 shape was the thin {"error":"..."} which gave
/// Claude no signal about recoverability or class.
///
/// Fields on the envelope:
///   tool         — exact registered tool name (e.g. "get_billed_pnl").
///   error        — human-readable message ($"{ExceptionTypeName}: {Message}"
///                  for caught exceptions, or the validation message for
///                  argument failures).
///   errorClass   — coarse classification: "Validation", "DataAccess",
///                  "Timeout", "Cancelled", "Unknown", or the exception
///                  type name when no specific bucket fits.
///   recoverable  — true if the model can plausibly retry / re-scope and
///                  succeed; false for infrastructure failures the LLM
///                  cannot fix by rewriting its inputs (broken DSN, failed
///                  SQL login, OLE DB metadata errors, etc.).
///   durationMs   — wall-time spent in the tool before failing. Mirrors the
///                  durationMs field on success payloads.
/// </summary>
public static class ToolErrorEnvelope
{
    public static string Build(string toolName, string error, string errorClass, bool recoverable, int durationMs)
    {
        return JsonSerializer.Serialize(new
        {
            tool = toolName,
            error,
            errorClass,
            recoverable,
            durationMs,
        });
    }

    public static string Validation(string toolName, string error, int durationMs)
        => Build(toolName, error, "Validation", recoverable: true, durationMs);

    public static string Cancelled(string toolName, int durationMs)
        => Build(toolName, "Query cancelled.", "Cancelled", recoverable: true, durationMs);

    public static string FromException(string toolName, Exception ex, int durationMs)
    {
        var error = $"{ex.GetType().Name}: {ex.Message}";
        var (errorClass, recoverable) = Classify(ex);
        return Build(toolName, error, errorClass, recoverable, durationMs);
    }

    private static (string ErrorClass, bool Recoverable) Classify(Exception ex)
    {
        return ex.GetType().Name switch
        {
            "OdbcException" or "SqlException" => ("DataAccess", false),
            "TimeoutException" => ("Timeout", true),
            "OperationCanceledException" or "TaskCanceledException" => ("Cancelled", true),
            "ArgumentException" or "ArgumentNullException" or "ArgumentOutOfRangeException" or "FormatException"
                => ("Validation", true),
            _ => (ex.GetType().Name, true),
        };
    }
}
