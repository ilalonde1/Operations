#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Kor.Operations.App.Services
{
    public sealed class BrochureAnalysisService
    {
        private readonly string _apiKey;

        public BrochureAnalysisService(string apiKey)
        {
            _apiKey = apiKey ?? string.Empty;
        }

        public async Task<BrochureAnalysisResult> AnalyzeAsync(
            string pdfPath,
            IReadOnlyList<string> skinOptions,
            IReadOnlyList<string> layoutOptions,
            CancellationToken ct)
        {
            var imageBytes = await RenderFirstPageAsync(pdfPath);
            return await CallClaudeAsync(imageBytes, skinOptions, layoutOptions, ct);
        }

        private static async Task<byte[]> RenderFirstPageAsync(string pdfPath)
        {
            var file = await StorageFile.GetFileFromPathAsync(pdfPath);
            var doc = await PdfDocument.LoadFromFileAsync(file);
            using var page = doc.GetPage(0);

            var renderOptions = new PdfPageRenderOptions { DestinationWidth = 800 };
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, renderOptions);

            var size = (uint)stream.Size;
            stream.Seek(0);
            var reader = new DataReader(stream);
            await reader.LoadAsync(size);
            var bytes = new byte[size];
            reader.ReadBytes(bytes);
            return bytes;
        }

        private async Task<BrochureAnalysisResult> CallClaudeAsync(
            byte[] imageBytes,
            IReadOnlyList<string> skinOptions,
            IReadOnlyList<string> layoutOptions,
            CancellationToken ct)
        {
            var base64 = Convert.ToBase64String(imageBytes);
            var skinList = string.Join(", ", skinOptions);
            var layoutList = string.Join(", ", layoutOptions);

            var prompt =
                $"Analyze this brochure cover page for its visual design only  ignore all text content. " +
                $"Return ONLY a valid JSON object with exactly these fields: " +
                $"\"primaryColor\" (dominant dark/background color as hex, e.g. \"#435363\"), " +
                $"\"accentColor\" (dominant bright or highlight color as hex), " +
                $"\"skinDisplayName\" (choose the single closest match from: {skinList}), " +
                $"\"layoutDisplayName\" (choose the single closest match from: {layoutList}). " +
                $"No explanation, no markdown fences  just the raw JSON object.";

            var requestBody = new
            {
                model = "claude-sonnet-4-20250514",
                max_tokens = 256,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new
                            {
                                type = "image",
                                source = new
                                {
                                    type = "base64",
                                    media_type = "image/png",
                                    data = base64
                                }
                            },
                            new { type = "text", text = prompt }
                        }
                    }
                }
            };

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var json = JsonSerializer.Serialize(requestBody);
            using var response = await client.PostAsync(
                "https://api.anthropic.com/v1/messages",
                new StringContent(json, Encoding.UTF8, "application/json"),
                ct);

            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadAsStringAsync(ct);

            using var responseDoc = JsonDocument.Parse(responseJson);
            var text = responseDoc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString() ?? "{}";

            return JsonSerializer.Deserialize<BrochureAnalysisResult>(
                text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new BrochureAnalysisResult();
        }
    }
}
