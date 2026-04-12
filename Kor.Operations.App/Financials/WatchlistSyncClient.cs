#nullable enable
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Options;

namespace Kor.Operations.Financials;

/// <summary>
/// HTTP client for the deltek-webhook service's /api/watchlist-change endpoints.
/// Used by Kor.Operations.App to enqueue watchlist toggles that the service relays
/// to Deltek Vantagepoint's REST API on our behalf.
/// </summary>
public sealed class WatchlistSyncClient
{
    private readonly WatchlistSyncOptions _options;
    private readonly HttpClient _http;
    private readonly string _authHeader;

    public WatchlistSyncClient(WatchlistSyncOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
            BaseAddress = string.IsNullOrWhiteSpace(options.ServiceUrl) ? null : new Uri(options.ServiceUrl.TrimEnd('/') + "/")
        };
        _authHeader = "Basic " + Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
    }

    public bool IsConfigured => _options.IsConfigured;

    /// <summary>
    /// Enqueues a watchlist state change request. The worker on the service side applies
    /// it to Deltek shortly after. Returns the queue row id used for polling status.
    /// </summary>
    public async Task<EnqueueResponse> EnqueueAsync(string wbs1, bool desiredOn, string requestedBy, CancellationToken ct)
    {
        if (!_options.IsConfigured)
            throw new InvalidOperationException("Watchlist sync service is not configured in App.config.");
        if (string.IsNullOrWhiteSpace(wbs1))
            throw new ArgumentException("wbs1 is required.", nameof(wbs1));

        var body = JsonSerializer.Serialize(new
        {
            wbs1,
            desiredState = desiredOn ? "Y" : "N",
            requestedBy = string.IsNullOrWhiteSpace(requestedBy) ? Environment.UserName : requestedBy
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/watchlist-change")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(_authHeader);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Watchlist sync enqueue failed: {(int)response.StatusCode} {response.ReasonPhrase} — {Truncate(responseBody, 300)}");
        }

        return ParseEnqueueResponse(responseBody);
    }

    /// <summary>Polls the status of a previously-enqueued change.</summary>
    public async Task<StatusResponse> GetStatusAsync(int id, CancellationToken ct)
    {
        if (!_options.IsConfigured)
            throw new InvalidOperationException("Watchlist sync service is not configured in App.config.");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/watchlist-change/{id}");
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(_authHeader);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Watchlist sync status check failed: {(int)response.StatusCode} {response.ReasonPhrase} — {Truncate(responseBody, 300)}");
        }

        return ParseStatusResponse(responseBody);
    }

    /// <summary>
    /// Enqueues a change and polls until it reaches a terminal state (Applied or Error) or the timeout elapses.
    /// Returns the final observed status.
    /// </summary>
    public async Task<StatusResponse> EnqueueAndWaitAsync(
        string wbs1, bool desiredOn, string requestedBy,
        TimeSpan timeout, TimeSpan pollInterval, CancellationToken ct)
    {
        var enqueued = await EnqueueAsync(wbs1, desiredOn, requestedBy, ct).ConfigureAwait(false);

        var deadline = DateTime.UtcNow + timeout;
        var last = new StatusResponse(enqueued.Id, enqueued.Status, null, null);

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(pollInterval, ct).ConfigureAwait(false);
            last = await GetStatusAsync(enqueued.Id, ct).ConfigureAwait(false);
            if (last.Status == "Applied" || last.Status == "Error")
            {
                return last;
            }
        }

        return last; // still Pending at timeout
    }

    private static EnqueueResponse ParseEnqueueResponse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return new EnqueueResponse(
            Id: root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt32() : 0,
            Status: root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? "" : "");
    }

    private static StatusResponse ParseStatusResponse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return new StatusResponse(
            Id: root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt32() : 0,
            Status: root.TryGetProperty("status", out var statusEl) ? statusEl.GetString() ?? "" : "",
            AppliedUtc: root.TryGetProperty("appliedUtc", out var appliedEl) && appliedEl.ValueKind == JsonValueKind.String
                ? (DateTime.TryParse(appliedEl.GetString(), out var dt) ? dt : (DateTime?)null)
                : null,
            ErrorMessage: root.TryGetProperty("errorMessage", out var errEl) && errEl.ValueKind == JsonValueKind.String
                ? errEl.GetString()
                : null);
    }

    private static string Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) ? "" : (s.Length > max ? s.Substring(0, max) : s);

    public sealed record EnqueueResponse(int Id, string Status);
    public sealed record StatusResponse(int Id, string Status, DateTime? AppliedUtc, string? ErrorMessage);
}
