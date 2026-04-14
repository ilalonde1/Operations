#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Kor.Operations.Services;
using Kor.Operations.Services.AiTools;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    /// <summary>
    /// AI bar wiring for the PDF-to-SAFE window. Registers the window as an
    /// <see cref="IAiContextProvider"/>, constructs the tool dispatcher, and
    /// initialises the shared <see cref="Controls.AiQueryPanel"/> in tool-use
    /// mode. Called from the window's Loaded event so the XAML-generated
    /// control field (<c>AiChatPanel</c>) is available.
    /// </summary>
    public partial class PdfToSafeWindow
    {
        private bool _aiBarInitialized;
        private AppAiService? _appAiService;

        private const string AiSystemPrompt =
            "You are an expert structural engineering CAD technician assisting KOR Structural " +
            "(Vancouver, BC). The user has loaded a Bluebeam-marked-up PDF into the KOR NewerForma " +
            "PDF-to-SAFE import tool and will converse with you in plain English to prepare the " +
            "model for export to CSI SAFE, ETABS, or AutoCAD.\n\n" +
            "You have tools that mutate the state of the tool directly: change per-colour or " +
            "per-element types, set slab properties (thickness, grade, SDL, live), toggle " +
            "inclusion, adjust export settings, and trigger the export itself. Prefer tool calls " +
            "over advisory prose — the user wants action. After tool calls, respond with one " +
            "crisp sentence describing what you did.\n\n" +
            "The current extraction state is provided in the CURRENT STATE block on every turn. " +
            "Shape indices in that state match the indices you pass to tools. Never invent " +
            "shapes, indices, or colours that aren't listed. If the user asks for something " +
            "impossible with the current data, explain why in one sentence.\n\n" +
            "Structural conventions:\n" +
            "  • Small roughly-square filled annotations = columns.\n" +
            "  • Elongated filled annotations = walls / beam line elements.\n" +
            "  • Large closed outlines = slab perimeters (set to Slab; chain assembly joins edges).\n" +
            "  • Core / elevator / stair enclosures = typically Opening on the floor slab.\n" +
            "  • Default concrete is C30 (Vancouver). CSA A23.3-19 with NBC combos is the default code.\n";

        private void InitializeAiBar()
        {
            if (_aiBarInitialized) return;
            _aiBarInitialized = true;

            try
            {
                _appAiService = AppServices.Get<AppAiService>();
                if (_appAiService is null || !_appAiService.IsConfigured)
                    return;

                var contextBuilder = AppServices.Get<AppAiContextBuilder>();
                contextBuilder.Register(this);

                AiChatPanel.InitializeWithTools(
                    _appAiService,
                    this,
                    PdfToSafeAiTools.All,
                    AiToolDispatchAsync,
                    AiSystemPrompt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialise PdfToSafe AI bar.");
            }
        }

        /// <summary>
        /// Routes a tool call from Claude to the matching handler on the UI thread.
        /// Returns a short human-readable string fed back to Claude as the tool result.
        /// Individual handlers are added in subsequent commits — unknown tools return a
        /// clear error so Claude can self-correct.
        /// </summary>
        private async Task<string> AiToolDispatchAsync(string toolName, JsonElement input, CancellationToken ct)
        {
            // Marshal to UI thread for any state mutation.
            var tcs = new TaskCompletionSource<string>();
            await Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    string result = toolName switch
                    {
                        // Handlers ship in Step 7+. Until wired, tools report a
                        // controlled error so Claude knows what's available.
                        _ => $"Tool '{toolName}' is not yet implemented in this build."
                    };
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetResult($"Tool '{toolName}' threw: {ex.Message}");
                }
            }, DispatcherPriority.Normal);
            return await tcs.Task;
        }
    }
}
