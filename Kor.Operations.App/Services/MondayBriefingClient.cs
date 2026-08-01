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

internal sealed class MondayBriefingClient
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly McpServerOptions _mcp;
    private readonly string _authHeader;

    internal MondayBriefingClient(McpServerOptions mcp)
    {
        _mcp = mcp;
        _authHeader = _mcp.IsConfigured
            ? "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_mcp.Username}:{_mcp.Password}"))
            : "";
    }

    internal bool IsConfigured => _mcp.IsConfigured;

    internal async Task<IReadOnlyList<AlertDto>> GetActiveAlertsAsync(CancellationToken ct)
    {
        return await GetAsync<IReadOnlyList<AlertDto>>("/alerts/active", ct).ConfigureAwait(false)
               ?? Array.Empty<AlertDto>();
    }

    internal async Task<IReadOnlyList<BriefDto>> GetLatestBriefAsync(CancellationToken ct)
    {
        return await GetAsync<IReadOnlyList<BriefDto>>("/coo-brief/latest", ct).ConfigureAwait(false)
               ?? Array.Empty<BriefDto>();
    }

    internal async Task AcknowledgeAlertAsync(long id, CancellationToken ct)
    {
        var url = _mcp.ServiceUrl.TrimEnd('/') + $"/alerts/{id}/acknowledge";
        await SendNoContentAsync(url, ct).ConfigureAwait(false);
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
                Log.Warning("MCP briefing GET {Path} returned {Status}: {Body}", path, (int)response.StatusCode, json);
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
            Log.Warning(ex, "MCP briefing GET {Path} failed.", path);
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
