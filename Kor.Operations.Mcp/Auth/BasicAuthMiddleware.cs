using System.Text;
using Kor.Operations.Mcp.Options;
using Microsoft.Extensions.Options;

namespace Kor.Operations.Mcp.Auth;

/// <summary>
/// HTTP Basic auth gate that mirrors the WatchlistSync server-side pattern
/// from KOR's reverse-proxy stack. Validates the Authorization header
/// against the configured <see cref="McpOptions.Username"/> /
/// <see cref="McpOptions.Password"/>. /health is exempt so the deploy
/// runbook can `curl /health` without credentials.
/// </summary>
public sealed class BasicAuthMiddleware
{
    private const string AuthScheme = "Basic ";
    private readonly RequestDelegate _next;
    private readonly IOptions<McpOptions> _options;
    private readonly ILogger<BasicAuthMiddleware> _logger;

    public BasicAuthMiddleware(
        RequestDelegate next,
        IOptions<McpOptions> options,
        ILogger<BasicAuthMiddleware> logger)
    {
        _next = next;
        _options = options;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Health check is unauthenticated by design: used by deploy runbook
        // and external monitoring, where adding creds creates more risk than
        // it removes.
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        var opts = _options.Value;
        if (!opts.IsConfigured)
        {
            _logger.LogError("McpOptions not configured; rejecting all non-health requests until Username/Password/SqlConnectionString are set.");
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync("MCP service not configured.");
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith(AuthScheme, StringComparison.Ordinal))
        {
            Unauthorized(context);
            return;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[AuthScheme.Length..]));
        }
        catch (FormatException)
        {
            Unauthorized(context);
            return;
        }

        var sep = decoded.IndexOf(':');
        if (sep < 0)
        {
            Unauthorized(context);
            return;
        }

        var user = decoded[..sep];
        var pass = decoded[(sep + 1)..];

        // Constant-time comparison would be ideal here, but the threat model
        // is LAN-only behind the same reverse proxy as WatchlistSync; matching
        // that service's existing approach is the right call for v1.
        if (!string.Equals(user, opts.Username, StringComparison.Ordinal) ||
            !string.Equals(pass, opts.Password, StringComparison.Ordinal))
        {
            Unauthorized(context);
            return;
        }

        // Capture the calling user's UPN from the custom header. Honour-system
        // identity for v1; will be replaced by Windows Auth in a later phase.
        var upn = context.Request.Headers["X-Kor-User-Upn"].ToString();
        if (!string.IsNullOrWhiteSpace(upn))
        {
            context.Items["UserUpn"] = upn.Trim();
        }

        await _next(context);
    }

    private static void Unauthorized(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Basic realm=\"Kor.Operations.Mcp\"";
    }
}
