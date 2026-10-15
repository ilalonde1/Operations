#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Options;

namespace Kor.Operations.Services;

public sealed record DirectorCitationDto(long ClaimId, long PassageId, string Statement, string Source);
public sealed record DirectorBriefDto(long Id, DateTime GeneratedAt, DateTime WeekOf, string Section, string Headline,
    string Body, string? Recommendation, int InputTokens, int OutputTokens, int ToolCalls,
    DateTime? AcknowledgedAt, string? AcknowledgedBy, string Citations)
{
    public IReadOnlyList<DirectorCitationDto> ReadCitations() =>
        JsonSerializer.Deserialize<DirectorCitationDto[]>(Citations, DirectorBriefClient.JsonOptions) ?? Array.Empty<DirectorCitationDto>();
}

public sealed class DirectorBriefClient
{
    private static readonly HttpClient SharedHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly McpServerOptions _mcp;
    private readonly HttpClient _http;
    public DirectorBriefClient(McpServerOptions mcp, HttpClient? http = null) { _mcp = mcp; _http = http ?? SharedHttp; }
    public bool IsConfigured => _mcp.IsConfigured;

    public async Task<IReadOnlyList<DirectorBriefDto>> GetLatestAsync(CancellationToken ct)
    {
        if (!IsConfigured) return Array.Empty<DirectorBriefDto>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = Request(HttpMethod.Get, "/director-brief/latest");
        using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<DirectorBriefDto[]>(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false), JsonOptions)
            ?? Array.Empty<DirectorBriefDto>();
    }
    public Task RunNowAsync(CancellationToken ct) => PostAsync("/director-brief/run-now", TimeSpan.FromMinutes(20), ct);
    public Task AcknowledgeAsync(long id, CancellationToken ct) => PostAsync($"/director-brief/{id}/acknowledge", TimeSpan.FromSeconds(30), ct);
    private async Task PostAsync(string path, TimeSpan duration, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("The brief service is not configured.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(duration);
        using var request = Request(HttpMethod.Post, path);
        using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, _mcp.ServiceUrl.TrimEnd('/') + path);
        request.Headers.TryAddWithoutValidation("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_mcp.Username}:{_mcp.Password}")));
        var actor = global::Kor.Operations.OperationsApp.SignedInUserUpn;
        if (!string.IsNullOrWhiteSpace(actor)) request.Headers.TryAddWithoutValidation("X-Kor-User-Upn", actor);
        return request;
    }
}
