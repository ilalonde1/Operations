#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Kor.Operations.App.Tests;

public sealed class SyncOverAsyncTests
{
    // Matches `.GetAwaiter().GetResult()` allowing whitespace/newlines between
    // tokens (handles fluent multi-line patterns).
    private static readonly Regex GetAwaiterGetResultRegex = new(
        @"\.GetAwaiter\s*\(\s*\)\s*\.\s*GetResult\s*\(\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const string AnnotationToken = "// sync-over-async OK";
    private const int AnnotationLookbackLines = 6;

    [Fact]
    public void No_unannotated_sync_over_async_in_App()
    {
        var repoRoot = XamlStaticResourceOrderTests.GetRepoRoot();
        var appRoot = Path.Combine(repoRoot, "Kor.Operations.App");
        var offences = Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(UnusedPrivateMethodTests.IsAppCsPath)
            .SelectMany(FindSyncOverAsync)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (offences.Count > 0)
        {
            Assert.Fail(
                $"Found {offences.Count} unannotated `.GetAwaiter().GetResult()` call(s) in production code. "
                + $"Each site needs a `{AnnotationToken}: <reason>` comment on the same line or within {AnnotationLookbackLines} lines above, "
                + "explaining why it's safe (off UI thread, sync-only interface contract, shutdown, etc.):\n"
                + string.Join(Environment.NewLine, offences));
        }
    }

    [Fact]
    public void Analyzer_flags_unannotated_sync_over_async_against_synthetic_class()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "KorSyncOverAsyncTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "SyncOverAsyncBad.cs.txt");
            var sourcePath = Path.Combine(tempDir, "SyncOverAsyncBad.cs");
            File.WriteAllText(sourcePath, File.ReadAllText(fixturePath));

            var offences = FindSyncOverAsync(sourcePath);

            Assert.Equal(2, offences.Count);
            Assert.All(offences, offence => Assert.Contains("GetAwaiter().GetResult()", offence, StringComparison.Ordinal));
            Assert.DoesNotContain(offences, x => x.Contains("AnnotatedSameLine", StringComparison.Ordinal));
            Assert.DoesNotContain(offences, x => x.Contains("AnnotatedPrecedingLine", StringComparison.Ordinal));
            Assert.DoesNotContain(offences, x => x.Contains("AnnotatedMultilineFluent", StringComparison.Ordinal));
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

    public static IReadOnlyList<string> FindSyncOverAsync(string sourceFile)
    {
        if (!UnusedPrivateMethodTests.IsAppCsPath(sourceFile) || !File.Exists(sourceFile))
        {
            return Array.Empty<string>();
        }

        var source = File.ReadAllText(sourceFile);
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var offences = new List<string>();

        foreach (Match match in GetAwaiterGetResultRegex.Matches(source))
        {
            var line = GetLineNumber(source, match.Index);
            if (IsAnnotated(lines, line))
            {
                continue;
            }

            offences.Add(
                $"{Path.GetFileName(sourceFile)}({line}): unannotated .GetAwaiter().GetResult()  add `{AnnotationToken}: <reason>` comment");
        }

        return offences;
    }

    private static bool IsAnnotated(string[] lines, int oneBasedLine)
    {
        // Check the line itself plus the AnnotationLookbackLines lines above it.
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
