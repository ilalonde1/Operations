#nullable enable
using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Wpf;

namespace Kor.Operations;

public sealed class SignatureEditorService
{
    private readonly ILogger<SignatureEditorService> _logger;

    public SignatureEditorService(ILogger<SignatureEditorService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InitializeAsync(WebView2 editor, string? initialHtml)
    {
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var htmlPath = System.IO.Path.Combine(exeDir, "Assets", "SignatureEditor.html");

        if (!System.IO.File.Exists(htmlPath))
            return;

        await editor.EnsureCoreWebView2Async();
        editor.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
                !e.Uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
            }
        };

        editor.Source = new Uri(htmlPath);
        editor.NavigationCompleted += async (_, __) =>
        {
            try
            {
                if (editor.CoreWebView2 == null)
                    return;

                var html = initialHtml ?? string.Empty;
                var json = JsonSerializer.Serialize(html);
                var script = $"window.setSignatureHtml({json});";

                await editor.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch
            {
                // best effort; do not crash preferences if the editor fails
            }
        };
    }

    public async Task<string?> GetHtmlAsync(WebView2 editor)
    {
        if (editor?.CoreWebView2 == null)
            return null;

        var result = await editor.CoreWebView2.ExecuteScriptAsync(
            "window.getSignatureHtml && window.getSignatureHtml();");

        if (!string.IsNullOrWhiteSpace(result) &&
            result != "null" &&
            result != "undefined")
        {
            return JsonSerializer.Deserialize<string>(result);
        }

        return null;
    }

    public async Task SetHtmlAsync(WebView2 editor, string html)
    {
        if (editor?.CoreWebView2 == null)
            return;

        var json = JsonSerializer.Serialize(html ?? string.Empty);
        var script = $"window.setSignatureHtml({json});";

        await editor.CoreWebView2.ExecuteScriptAsync(script);
    }
}
