using System.Diagnostics;

namespace Kor.Operations.Mcp.Audit;

/// <summary>
/// Wraps every authenticated request, captures duration and final status,
/// and writes one row to Mcp.AuditLog. /health is skipped to avoid noise
/// from monitoring polls. The actual write fires-and-forgets so the audit
/// path never blocks the response.
/// </summary>
public sealed class AuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AuditLogger _audit;

    public AuditMiddleware(RequestDelegate next, AuditLogger audit)
    {
        _next = next;
        _audit = audit;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        // /ask is audited by AskService itself, which has the final answer
        // text + token counts (Batch 92a). Skipping here avoids the empty
        // duplicate row this middleware would otherwise write.
        if (context.Request.Path.StartsWithSegments("/ask"))
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        string? errorMessage = null;
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            errorMessage = ex.GetType().Name + ": " + ex.Message;
            throw;
        }
        finally
        {
            sw.Stop();

            var status = errorMessage != null
                ? "Error"
                : context.Response.StatusCode is >= 200 and < 300
                    ? "Ok"
                    : "Error";

            var entry = new AuditEntry(
                UserUpn:      context.Items["UserUpn"] as string,
                ClientApp:    context.Request.Headers["X-Kor-Client-App"].ToString() is { Length: > 0 } app ? app : null,
                ToolName:     context.Request.Path.Value,
                InputJson:    null, // tool-input capture lands in Step 3 with the MCP wire layer
                ResultStatus: status,
                DurationMs:   (int)sw.ElapsedMilliseconds,
                ErrorMessage: errorMessage);

            // Fire-and-forget so the response doesn't wait on SQL.
            _ = _audit.WriteAsync(entry, CancellationToken.None);
        }
    }
}
