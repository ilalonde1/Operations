#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    internal sealed class AiColorMappingResult
    {
        public List<(byte R, byte G, byte B)> SlabColors   { get; } = new();
        public List<(byte R, byte G, byte B)> BeamColors   { get; } = new();
        public List<(byte R, byte G, byte B)> ColumnColors { get; } = new();
        public string Summary { get; set; } = "";
    }

    /// <summary>
    /// Sends the rendered PDF page to Claude Vision and asks it to classify
    /// which detected colors correspond to structural element types.
    /// PdfPig still provides the precise geometry — this service only drives
    /// the color filter selection intelligently.
    /// </summary>
    internal sealed class PdfGeometryAnalysisService
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(90) };
        private readonly string _apiKey;

        public PdfGeometryAnalysisService(string apiKey) => _apiKey = apiKey;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

        /// <param name="bitmap">Rendered PDF page (already frozen BitmapSource).</param>
        /// <param name="detectedColors">Colors extracted by PdfPig — Claude receives these exact hex values.</param>
        /// <param name="userPrompt">null = auto mode; non-null = describe mode.</param>
        public async Task<AiColorMappingResult?> AnalyseColorsAsync(
            BitmapSource bitmap,
            IReadOnlyList<(byte R, byte G, byte B)> detectedColors,
            string? userPrompt,
            CancellationToken ct = default)
        {
            if (!IsConfigured || detectedColors.Count == 0) return null;

            // Build the colour list Claude will reason over — these are our
            // quantized hex values so Claude's answer snaps back exactly.
            var hexList = detectedColors.Select(c => $"#{c.R:X2}{c.G:X2}{c.B:X2}").ToList();
            var colorSummary = string.Join(", ", hexList);

            string promptText = string.IsNullOrWhiteSpace(userPrompt)
                ? "You are analysing a structural engineering floor plan PDF page. " +
                  $"The following distinct colors were detected in the drawing: {colorSummary}. " +
                  "Based on what you see in the image, classify each color as one of: " +
                  "'slab' (closed slab/floor plate outlines), 'beam' (linear beams, walls, or open elements), " +
                  "'column' (point column or support locations), or 'ignore' (dimensions, text, grid lines, hatching, background). " +
                  "Return ONLY a valid JSON object with no markdown, no explanation: " +
                  "{\"slabs\":[\"#RRGGBB\",...],\"beams\":[\"#RRGGBB\",...],\"columns\":[\"#RRGGBB\",...]}"
                : "You are analysing a structural engineering floor plan PDF page. " +
                  $"The engineer says: \"{userPrompt.Trim()}\". " +
                  $"The following distinct colors are present in the drawing: {colorSummary}. " +
                  "Based on the image and the engineer's description, classify each relevant color as " +
                  "'slab', 'beam', 'column', or 'ignore'. " +
                  "Return ONLY a valid JSON object with no markdown, no explanation: " +
                  "{\"slabs\":[\"#RRGGBB\",...],\"beams\":[\"#RRGGBB\",...],\"columns\":[\"#RRGGBB\",...]}";

            // Encode bitmap as base64 PNG
            string base64;
            using (var ms = new MemoryStream())
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bitmap));
                enc.Save(ms);
                base64 = Convert.ToBase64String(ms.ToArray());
            }

            var body = JsonSerializer.Serialize(new
            {
                model = "claude-sonnet-4-6",
                max_tokens = 512,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "image", source = new { type = "base64", media_type = "image/png", data = base64 } },
                            new { type = "text", text = promptText }
                        }
                    }
                }
            });

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            req.Headers.Add("x-api-key", _apiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            var respJson = await resp.Content.ReadAsStringAsync(ct);
            using var respDoc = JsonDocument.Parse(respJson);
            var text = respDoc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString() ?? "";

            return ParseResult(text, detectedColors);
        }

        private static AiColorMappingResult ParseResult(
            string text,
            IReadOnlyList<(byte R, byte G, byte B)> palette)
        {
            var result = new AiColorMappingResult();

            // Strip markdown fences Claude sometimes adds despite instructions
            var cleaned = Regex.Replace(
                text.Trim(), @"^```[a-z]*\n?|```$", "",
                RegexOptions.Multiline).Trim();

            try
            {
                using var doc = JsonDocument.Parse(cleaned);
                var root = doc.RootElement;

                void Populate(string key, List<(byte R, byte G, byte B)> list)
                {
                    if (!root.TryGetProperty(key, out var arr)) return;
                    foreach (var el in arr.EnumerateArray())
                    {
                        var hex = el.GetString();
                        if (string.IsNullOrEmpty(hex)) continue;
                        try
                        {
                            hex = hex.TrimStart('#');
                            if (hex.Length < 6) continue;
                            byte r = Convert.ToByte(hex[..2], 16);
                            byte g = Convert.ToByte(hex[2..4], 16);
                            byte b = Convert.ToByte(hex[4..6], 16);
                            // Snap to nearest palette entry so indices match exactly
                            var snapped = palette.MinBy(c =>
                                Math.Abs(c.R - r) + Math.Abs(c.G - g) + Math.Abs(c.B - b));
                            if (!list.Contains(snapped)) list.Add(snapped);
                        }
                        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("PdfGeometryAnalysisService: skipped color hex: " + ex.Message); }
                    }
                }

                Populate("slabs",   result.SlabColors);
                Populate("beams",   result.BeamColors);
                Populate("columns", result.ColumnColors);

                var total = result.SlabColors.Count + result.BeamColors.Count + result.ColumnColors.Count;
                result.Summary = total == 0
                    ? "Claude could not identify structural colors. Try 'Describe elements' mode."
                    : $"Claude identified {result.SlabColors.Count} slab color(s), " +
                      $"{result.BeamColors.Count} beam color(s), " +
                      $"{result.ColumnColors.Count} column color(s). " +
                      "Color filter updated — review the overlay and export.";
            }
            catch
            {
                result.Summary = "Could not parse Claude's response. Try again or use Describe mode.";
            }

            return result;
        }
    }
}
