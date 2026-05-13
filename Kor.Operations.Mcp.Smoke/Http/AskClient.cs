#nullable enable
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Kor.Operations.Mcp.Smoke.Config;

namespace Kor.Operations.Mcp.Smoke.Http;

internal sealed class AskClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http = new();
    private readonly SmokeConfig _config;

    public AskClient(SmokeConfig config)
    {
        _config = config;
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Mcp.Username}:{config.Mcp.Password}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        _http.DefaultRequestHeaders.Add("X-Kor-User-Upn", "smoke-harness@kor");
        _http.DefaultRequestHeaders.Add("X-Kor-Client-App", "smoke-test");
        _http.Timeout = TimeSpan.FromMinutes(3);
    }

    public async Task<AskCallResult> AskAsync(string question, string? currentlyViewing, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var bodyQuestion = string.IsNullOrWhiteSpace(currentlyViewing)
            ? question
            : question + "\n\n[CURRENTLY VIEWING]\n" + currentlyViewing;
        using var content = new StringContent(
            JsonSerializer.Serialize(new { question = bodyQuestion }),
            Encoding.UTF8,
            "application/json");
        using var response = await _http.PostAsync(_config.Mcp.Endpoint, content, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var finished = DateTime.UtcNow;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"/ask returned HTTP {(int)response.StatusCode}: {text}");

        var dto = JsonSerializer.Deserialize<AskResponseDto>(text, JsonOptions)
            ?? throw new InvalidOperationException("/ask returned an empty response body.");
        return new AskCallResult(
            dto.Answer ?? "",
            dto.DurationMs,
            dto.ToolCallsExecuted,
            started,
            finished);
    }

    private sealed class AskResponseDto
    {
        public string? Answer { get; init; }
        public int DurationMs { get; init; }
        public int ToolCallsExecuted { get; init; }
    }
}

internal sealed record AskCallResult(
    string Answer,
    int DurationMs,
    int ToolCallsExecuted,
    DateTime StartedAtUtc,
    DateTime FinishedAtUtc);
