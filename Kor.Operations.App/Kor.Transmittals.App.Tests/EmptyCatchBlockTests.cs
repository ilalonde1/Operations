#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Kor.Operations.App.Tests;

public sealed class EmptyCatchBlockTests
{
    private static readonly Regex CatchBlockRegex = new(
        """
        catch
        \s*
        (?:\([^)]*\))?
        \s*
        (?:when\s*\([^)]*\))?
        \s*
        \{
        (?<body>[^{}]*?)
        \}
        """,
        RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LineCommentRegex = new(
        @"//.*?$",
        RegexOptions.Multiline | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BlockCommentRegex = new(
        @"/\*.*?\*/",
        RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CancellationTypeFilterRegex = new(
        @"^catch\s*\(\s*(?:[\w.]+\.)?(?:Operation|Task)CanceledException\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void No_empty_catch_blocks_in_App()
    {
        var repoRoot = XamlStaticResourceOrderTests.GetRepoRoot();
        var appRoot = Path.Combine(repoRoot, "Kor.Operations.App");
        var offences = Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(UnusedPrivateMethodTests.IsAppCsPath)
            .SelectMany(FindEmptyCatchBlocks)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (offences.Count > 0)
        {
            Assert.Fail(
                $"Found {offences.Count} empty catch block(s):\n"
                + string.Join(Environment.NewLine, offences));
        }
    }

    [Fact]
    public void Analyzer_flags_empty_catch_blocks_against_synthetic_class()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "KorEmptyCatchBlockTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "EmptyCatchBad.cs.txt");
            var sourcePath = Path.Combine(tempDir, "EmptyCatchBad.cs");
            File.WriteAllText(sourcePath, File.ReadAllText(fixturePath));

            var offences = FindEmptyCatchBlocks(sourcePath);

            Assert.Equal(2, offences.Count);
            Assert.All(offences, offence => Assert.Contains("empty catch block", offence, StringComparison.Ordinal));
            Assert.DoesNotContain(offences, x => x.Contains("RealCatch", StringComparison.Ordinal));
            Assert.DoesNotContain(offences, x => x.Contains("CommentedCatch", StringComparison.Ordinal));
            Assert.DoesNotContain(offences, x => x.Contains("OperationCancelledTypedCatch", StringComparison.Ordinal));
            Assert.DoesNotContain(offences, x => x.Contains("TaskCancelledTypedCatch", StringComparison.Ordinal));
            Assert.DoesNotContain(offences, x => x.Contains("OperationCancelledTypedCatchNamed", StringComparison.Ordinal));
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    public static IReadOnlyList<string> FindEmptyCatchBlocks(string sourceFile)
    {
        if (!UnusedPrivateMethodTests.IsAppCsPath(sourceFile) || !File.Exists(sourceFile))
        {
            return Array.Empty<string>();
        }

        var source = File.ReadAllText(sourceFile);
        var offences = new List<string>();

        foreach (Match match in CatchBlockRegex.Matches(source))
        {
            var body = match.Groups["body"].Value;
            if (!IsEmptyCatchBody(body))
            {
                continue;
            }

            if (CancellationTypeFilterRegex.IsMatch(match.Value))
            {
                continue;
            }

            var line = GetLineNumber(source, match.Index);
            offences.Add(
                $"{Path.GetFileName(sourceFile)}({line}): empty catch block  swallowed exception, add a log/rethrow or a /*  */ explanation");
        }

        return offences;
    }

    private static bool IsEmptyCatchBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return true;
        }

        if (LineCommentRegex.IsMatch(body) || BlockCommentRegex.IsMatch(body))
        {
            return false;
        }

        var withoutComments = BlockCommentRegex.Replace(LineCommentRegex.Replace(body, ""), "");
        return string.IsNullOrWhiteSpace(withoutComments);
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
