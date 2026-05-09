#nullable enable
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Options;
using Serilog;

namespace Kor.Operations.Services;

internal sealed class CooCardClient
{
    // Outer timeout disabled — per-call CancellationTokens drive cancellation.
    // /coo-card/run-now does multi-second LLM synthesis; 30s would always lose.
    private static readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly McpServerOptions _mcp;
    private readonly string _authHeader;

    internal CooCardClient(McpServerOptions mcp)
    {
        _mcp = mcp;
        _authHeader = _mcp.IsConfigured
            ? "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_mcp.Username}:{_mcp.Password}"))
            : "";
    }

    internal bool IsConfigured => _mcp.IsConfigured;

    internal async Task<IReadOnlyList<CooCardItemDto>> GetLatestAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        return await GetAsync<IReadOnlyList<CooCardItemDto>>("/coo-card/latest", cts.Token).ConfigureAwait(false)
               ?? Array.Empty<CooCardItemDto>();
    }

    internal async Task AcknowledgeAsync(long id, CancellationToken ct)
    {
        var url = _mcp.ServiceUrl.TrimEnd('/') + $"/coo-card/{id}/acknowledge";
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        await SendNoContentAsync(url, cts.Token).ConfigureAwait(false);
    }

    internal async Task RunNowAsync(CancellationToken ct)
    {
        var url = _mcp.ServiceUrl.TrimEnd('/') + "/coo-card/run-now";
        // Server-side synthesis call to /ask is slow (multi-second LLM round-trip
        // plus brief + alerts read). Bump the wait window for run-now specifically
        // so the user can drive a fresh card from the UI without timing out.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(3));
        await SendNoContentAsync(url, cts.Token).ConfigureAwait(false);
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
    {
        if (!_mcp.IsConfigured)
        {
            return default;
        }

        var url = _mcp.ServiceUrl.TrimEnd('/') + path;
        try
        {
            using var request = BuildRequest(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("MCP COO Card GET {Path} returned {Status}: {Body}", path, (int)response.StatusCode, json);
                return default;
            }

            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "MCP COO Card GET {Path} failed.", path);
            return default;
        }
    }

    private async Task SendNoContentAsync(string url, CancellationToken ct)
    {
        using var request = BuildRequest(HttpMethod.Post, url);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"MCP returned HTTP {(int)response.StatusCode}: {body}");
        }
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Authorization", _authHeader);
        var upn = global::Kor.Operations.OperationsApp.SignedInUserUpn;
        if (!string.IsNullOrWhiteSpace(upn))
        {
            request.Headers.TryAddWithoutValidation("X-Kor-User-Upn", upn);
        }

        return request;
    }
}
