#nullable enable
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kor.AwardOllamaBackfill;

internal sealed class OllamaClient
{
    private readonly HttpClient _http;
    private readonly string _model;

    public OllamaClient(HttpClient http, string model)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _model = string.IsNullOrWhiteSpace(model) ? "qwen2.5:14b" : model;
    }

    public async Task<JsonNode?> GenerateJsonAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var requestBody = new
        {
            model = _model,
            prompt = userPrompt,
            system = systemPrompt,
            stream = false,
            format = "json",
            options = new
            {
                temperature = 0.2,
                num_predict = 2048,
                num_ctx = 8192
            }
        };

        Exception? lastError = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var resp = await _http.PostAsJsonAsync("/api/generate", requestBody, ct).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"Ollama HTTP {(int)resp.StatusCode}: {body}");
                }

                var outer = JsonNode.Parse(body);
                var inner = outer?["response"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(inner))
                {
                    throw new JsonException("Ollama response did not contain a JSON response string.");
                }

                return JsonNode.Parse(inner);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < 2)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException("Ollama generation failed after 2 attempts.", lastError);
    }
}
