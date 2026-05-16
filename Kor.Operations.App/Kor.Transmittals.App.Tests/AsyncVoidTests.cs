#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Kor.Operations.App.Tests;

public sealed class AsyncVoidTests
{
    // Captures `async void` declarations along with the modifier prefix
    // (e.g., "private", "protected override", "public") and the parameter list.
    private static readonly Regex AsyncVoidRegex = new(
        @"(?<prefix>(?:(?:public|private|protected|internal|static|override|sealed|virtual|new|partial)\s+)*)async\s+void\s+(?<name>\w+)\s*\((?<params>[^)]*)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const string AnnotationToken = "// async-void OK";
    private const int AnnotationLookbackLines = 3;

    [Fact]
    public void No_unannotated_async_void_in_App()
    {
        var repoRoot = XamlStaticResourceOrderTests.GetRepoRoot();
        var appRoot = Path.Combine(repoRoot, "Kor.Operations.App");
        var offences = Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(UnusedPrivateMethodTests.IsAppCsPath)
            .SelectMany(FindAsyncVoid)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (offences.Count > 0)
        {
            Assert.Fail(
                $"Found {offences.Count} `async void` method(s) that are not event handlers, overrides, or annotated. "
                + $"Each needs a `{AnnotationToken}: <reason>` comment within {AnnotationLookbackLines} lines above, "
                + "or convert the method to `async Task`:\n"
                + string.Join(Environment.NewLine, offences));
        }
    }

    [Fact]
    public void Analyzer_flags_async_void_against_synthetic_class()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "KorAsyncVoidTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "AsyncVoidBad.cs.txt");
            var sourcePath = Path.Combine(tempDir, "AsyncVoidBad.cs");
            File.WriteAllText(sourcePath, File.ReadAllText(fixturePath));

            var offences = FindAsyncVoid(sourcePath);

            Assert.Equal(2, offences.Count);
            Assert.All(offences, offence => Assert.Contains("async void", offence, StringComparison.Ordinal));
            Assert.DoesNotContain(offences, x => x.Contains("EventHandler", StringComparison.Ordinal));
            Assert.DoesNotContain(offences, x => x.Contains("NullableEventHandler", StringComparison.Ordinal));
            Assert.DoesNotContain(offences, x => x.Contains("OverrideAsyncVoid", StringComparison.Ordinal));
            Assert.DoesNotContain(offences, x => x.Contains("AnnotatedAsyncVoid", StringComparison.Ordinal));
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                /* best-effort test cleanup */
            }
        }
    }

    public static IReadOnlyList<string> FindAsyncVoid(string sourceFile)
    {
        if (!UnusedPrivateMethodTests.IsAppCsPath(sourceFile) || !File.Exists(sourceFile))
        {
            return Array.Empty<string>();
        }

        var source = File.ReadAllText(sourceFile);
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var offences = new List<string>();

        foreach (Match match in AsyncVoidRegex.Matches(source))
        {
            var prefix = match.Groups["prefix"].Value;
            var name = match.Groups["name"].Value;
            var paramList = match.Groups["params"].Value;

            // Skip: override methods (base contract is void).
            if (prefix.Contains("override", StringComparison.Ordinal))
            {
                continue;
            }

            // Skip: WPF event-handler signature first param is `object sender` or `object? sender`.
            if (IsEventHandlerSignature(paramList))
            {
                continue;
            }

            var line = GetLineNumber(source, match.Index);

            // Skip: explicitly annotated.
            if (IsAnnotated(lines, line))
            {
                continue;
            }

            offences.Add(
                $"{Path.GetFileName(sourceFile)}({line}): async void {name}(...) is not an event handler / override; "
                + $"add `{AnnotationToken}: <reason>` or convert to async Task");
        }

        return offences;
    }

    private static bool IsEventHandlerSignature(string paramList)
    {
        var trimmed = paramList.TrimStart();
        return trimmed.StartsWith("object sender", StringComparison.Ordinal)
            || trimmed.StartsWith("object? sender", StringComparison.Ordinal);
    }

    private static bool IsAnnotated(string[] lines, int oneBasedLine)
    {
        var start = Math.Max(0, oneBasedLine - 1 - AnnotationLookbackLines);
        var end = Math.Min(lines.Length - 1, oneBasedLine - 1);
        for (var i = start; i <= end; i++)
        {
            if (lines[i].Contains(AnnotationToken, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int GetLineNumber(string source, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < source.Length; i++)
        {
            if (source[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}
