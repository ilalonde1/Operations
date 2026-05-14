#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Kor.Operations.Tests.Services;

/// <summary>
/// Locks the rule "every window that hosts an &lt;AiQueryPanel&gt; must wire
/// an <c>IAiContextProvider</c> so AI sees the on-screen scope". Static
/// gate added after Batch 101 discovered StaffUtilizationWindow shipped
/// with the panel embedded but no context provider — Claude received only
/// firmwide context and silently answered staff-utilization questions
/// without seeing the rows the user was looking at.
///
/// Detection rule: for each *.xaml referencing AiQueryPanel under
/// Kor.Operations.App, at least one .cs file in the same directory (or a
/// nested AiContext/AiWiring partial) must contain either
/// <c>AppAiContextBuilder...Register(</c> (push-context wiring) OR
/// <c>: IAiContextProvider</c> (window/VM implements the interface). Both
/// patterns are accepted because Crm/Financials use VM registration while
/// PdfToSafe implements the interface on the window directly.
/// </summary>
public sealed class AiPanelContextProviderTests
{
    [Fact]
    public void EveryAiQueryPanelHost_HasRegisteredContextProvider()
    {
        var appRoot = ResolveAppRoot();

        // Find every XAML that references the AiQueryPanel control. The tag uses
        // an xmlns alias (controls:, ctrl:, etc.) so we match on ":AiQueryPanel"
        // — matches all alias variants and won't false-match the control's own
        // source file (AiQueryPanel.xaml has class="AiQueryPanel" not ":AiQueryPanel").
        var xamlHosts = Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(p => File.ReadAllText(p).Contains(":AiQueryPanel", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(xamlHosts);

        var gaps = new List<string>();
        foreach (var xamlPath in xamlHosts)
        {
            var dir = Path.GetDirectoryName(xamlPath)!;
            var stem = Path.GetFileNameWithoutExtension(xamlPath); // e.g. "StaffUtilizationWindow"

            // Only count .cs files that belong to THIS class — its primary
            // code-behind (<stem>.xaml.cs) and any partial-class siblings
            // (<stem>.AiWiring.cs, <stem>.AiContext.cs, …). Scanning the
            // whole directory false-passes when an unrelated neighbouring
            // window has the wiring (PMTools/ — Historical/Pm are wired
            // but StaffUtil is not).
            var classCs = Directory.EnumerateFiles(dir, $"{stem}.*.cs", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(dir, $"{stem}.cs", SearchOption.TopDirectoryOnly))
                .Distinct()
                .ToList();

            bool hasWiring = classCs.Any(cs =>
            {
                var text = File.ReadAllText(cs);
                // Either pattern is acceptable:
                //   1. Push-context: file mentions AppAiContextBuilder AND calls
                //      Register(...) — kept as two independent substring checks
                //      because real code stores the builder in a local first
                //      (`var contextBuilder = AppServices.Get<AppAiContextBuilder>();
                //      contextBuilder.Register(_vm);`).
                //   2. Interface impl: window or VM declares `: IAiContextProvider`.
                bool hasPushWiring =
                    text.Contains("AppAiContextBuilder", StringComparison.Ordinal)
                    && text.Contains(".Register(", StringComparison.Ordinal);
                bool implementsInterface =
                    text.Contains(": IAiContextProvider", StringComparison.Ordinal);
                return hasPushWiring || implementsInterface;
            });

            if (!hasWiring)
                gaps.Add(Path.GetRelativePath(appRoot, xamlPath));
        }

        Assert.True(gaps.Count == 0,
            "Window(s) host AiQueryPanel but never register an IAiContextProvider — AI will answer " +
            "without on-screen context. Fix by registering a provider (Crm/Financials pattern) or " +
            "implementing IAiContextProvider on the window class (PdfToSafe pattern):\n  - " +
            string.Join("\n  - ", gaps));
    }

    private static string ResolveAppRoot()
    {
        // Walk up from the test binary's BaseDirectory until we find the
        // Kor.Operations.App project directory.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Kor.Operations.App.csproj")))
            dir = dir.Parent;

        if (dir == null)
            throw new InvalidOperationException(
                $"Could not locate Kor.Operations.App.csproj from BaseDirectory '{AppContext.BaseDirectory}'.");

        return dir.FullName;
    }
}
