#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Kor.Operations.App.Tests;

public sealed class ThrowExRewrapTests
{
    // Matches: catch (<TypeName> <varName>) { ... throw <varName>; ... }
    // Body is restricted to no nested braces so we don't bleed into siblings.
    private static readonly Regex CatchWithVarRegex = new(
        """
        catch
        \s*
        \(
        \s*
        (?<type>[\w.]+(?:\s*<[\w.,\s<>?]*>)?)
        \s+
        (?<var>\w+)
        \s*
        \)
        \s*
        (?:when\s*\([^)]*\))?
        \s*
        \{
        (?<body>[^{}]*)
        \}
        """,
        RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void No_throw_ex_rewrap_in_App()
    {
        var repoRoot = XamlStaticResourceOrderTests.GetRepoRoot();
        var appRoot = Path.Combine(repoRoot, "Kor.Operations.App");
        var offences = Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(UnusedPrivateMethodTests.IsAppCsPath)
            .SelectMany(FindThrowExRewraps)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (offences.Count > 0)
        {
            Assert.Fail(
                "Found " + offences.Count + " throw-ex rewrap(s) that destroy the stack trace; use plain `throw;` instead:\n"
                + string.Join(Environment.NewLine, offences));
        }
    }

    [Fact]
    public void Analyzer_flags_throw_ex_rewrap_against_synthetic_class()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "KorThrowExRewrapTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ThrowExRewrapBad.cs.txt");
            var sourcePath = Path.Combine(tempDir, "ThrowExRewrapBad.cs");
            File.WriteAllText(sourcePath, File.ReadAllText(fixturePath));

            var offences = FindThrowExRewraps(sourcePath);

            Assert.Equal(2, offences.Count);
            Assert.All(offences, offence => Assert.Contains("throw-ex rewrap", offence, StringComparison.Ordinal));
            Assert.DoesNotContain(offences, x => x.Contains("PlainRethrow", StringComparison.Ordinal));
            Assert.DoesNotContain(offences, x => x.Contains("ThrowNew", StringComparison.Ordinal));
            Assert.DoesNotContain(offences, x => x.Contains("ThrowInnerWrap", StringComparison.Ordinal));
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

    public static IReadOnlyList<string> FindThrowExRewraps(string sourceFile)
    {
        if (!UnusedPrivateMethodTests.IsAppCsPath(sourceFile) || !File.Exists(sourceFile))
        {
            return Array.Empty<string>();
        }

        var source = File.ReadAllText(sourceFile);
        var offences = new List<string>();

        foreach (Match match in CatchWithVarRegex.Matches(source))
        {
            var varName = match.Groups["var"].Value;
            var body = match.Groups["body"].Value;

            // Look for `throw <var>;` (with optional whitespace) inside the body.
            var throwRegex = new Regex(
                @"\bthrow\s+" + Regex.Escape(varName) + @"\s*;",
                RegexOptions.CultureInvariant);
            foreach (Match throwMatch in throwRegex.Matches(body))
            {
                var absoluteIndex = match.Groups["body"].Index + throwMatch.Index;
                var line = GetLineNumber(source, absoluteIndex);
                offences.Add(
                    $"{Path.GetFileName(sourceFile)}({line}): throw-ex rewrap  use `throw;` instead of `throw {varName};` to preserve stack trace");
            }
        }

        return offences;
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
