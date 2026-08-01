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

internal sealed class CollectionsClient
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly McpServerOptions _mcp;
    private readonly string _authHeader;

    internal CollectionsClient(McpServerOptions mcp)
    {
        _mcp = mcp;
        _authHeader = _mcp.IsConfigured
            ? "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_mcp.Username}:{_mcp.Password}"))
            : "";
    }

    internal bool IsConfigured => _mcp.IsConfigured;

    internal async Task<IReadOnlyList<CollectionsCaseDto>> GetAllAsync(CancellationToken ct)
        => await GetAsync<IReadOnlyList<CollectionsCaseDto>>("/collections", ct).ConfigureAwait(false)
           ?? Array.Empty<CollectionsCaseDto>();

    internal async Task<IReadOnlyList<CollectionsCaseDto>> GetActiveAsync(CancellationToken ct)
        => await GetAsync<IReadOnlyList<CollectionsCaseDto>>("/collections/active", ct).ConfigureAwait(false)
           ?? Array.Empty<CollectionsCaseDto>();

    // Flat list of (WBS1, Invoice) pairs on every non-Resolved case. Used by
    // the Financials KPI layer to segregate Outstanding into "regular" vs
    // "in collections" without per-case lookups.
    internal async Task<IReadOnlyList<ActiveCaseInvoiceDto>> GetActiveCaseInvoicesAsync(CancellationToken ct)
        => await GetAsync<IReadOnlyList<ActiveCaseInvoiceDto>>("/collections/active-invoices", ct).ConfigureAwait(false)
           ?? Array.Empty<ActiveCaseInvoiceDto>();

    internal async Task<CollectionsCaseDetailDto?> GetDetailAsync(long id, CancellationToken ct)
        => await GetAsync<CollectionsCaseDetailDto>($"/collections/{id}", ct).ConfigureAwait(false);

    internal async Task<IReadOnlyList<ClientArInvoiceDto>> GetArByClientAsync(string clientId, CancellationToken ct)
        => await GetAsync<IReadOnlyList<ClientArInvoiceDto>>($"/collections/ar-by-client/{Uri.EscapeDataString(clientId)}", ct).ConfigureAwait(false)
           ?? Array.Empty<ClientArInvoiceDto>();

    internal async Task<long> OpenCaseAsync(OpenCollectionsCaseRequestDto req, CancellationToken ct)
    {
        if (!_mcp.IsConfigured)
        {
            throw new InvalidOperationException("MCP server is not configured.");
        }

        var url = _mcp.ServiceUrl.TrimEnd('/') + "/collections";
        using var request = BuildRequest(HttpMethod.Post, url);
        request.Content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"MCP POST /collections returned HTTP {(int)response.StatusCode}: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetInt64();
    }

    internal async Task UpdateCaseAsync(long id, UpdateCollectionsCaseRequestDto req, CancellationToken ct)
    {
        if (!_mcp.IsConfigured)
        {
            throw new InvalidOperationException("MCP server is not configured.");
        }

        var url = _mcp.ServiceUrl.TrimEnd('/') + $"/collections/{id}";
        using var request = BuildRequest(HttpMethod.Put, url);
        request.Content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"MCP PUT /collections/{id} returned HTTP {(int)response.StatusCode}: {body}");
        }
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
                Log.Warning("MCP collections GET {Path} returned {Status}: {Body}", path, (int)response.StatusCode, json);
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
            Log.Warning(ex, "MCP collections GET {Path} failed.", path);
            return default;
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
