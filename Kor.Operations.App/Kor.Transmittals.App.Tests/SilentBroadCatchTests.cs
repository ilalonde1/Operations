#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Kor.Operations.App.Tests;

public sealed class SilentBroadCatchTests
{
    // Matches `catch ([System.]Exception <var>) [when (...)] { <body without nested braces> }`.
    private static readonly Regex BroadCatchRegex = new(
        """
        catch
        \s*
        \(
        \s*
        (?:System\.)?Exception
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

    private static readonly string[] LoggingTokens =
    {
        "_log", "_logger", "Log.", "Serilog", "Logger.",
    };

    private const string AnnotationToken = "// silent-catch OK";
    private const int AnnotationLookbackLines = 3;

    [Fact]
    public void No_silent_broad_catch_in_App()
    {
        var repoRoot = XamlStaticResourceOrderTests.GetRepoRoot();
        var appRoot = Path.Combine(repoRoot, "Kor.Operations.App");
        var offences = Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(UnusedPrivateMethodTests.IsAppCsPath)
            .SelectMany(FindSilentBroadCatch)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (offences.Count > 0)
        {
            Assert.Fail(
                $"Found {offences.Count} silent `catch (Exception <var>)` block(s) that neither log, rethrow, nor reference the exception. "
                + $"Add logging/rethrow or `{AnnotationToken}: <reason>`:\n"
                + string.Join(Environment.NewLine, offences));
        }
    }

    [Fact]
    public void Analyzer_flags_silent_broad_catch_against_synthetic_class()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "KorSilentBroadCatchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "SilentBroadCatchBad.cs.txt");
            var sourcePath = Path.Combine(tempDir, "SilentBroadCatchBad.cs");
            File.WriteAllText(sourcePath, File.ReadAllText(fixturePath));

            var offences = FindSilentBroadCatch(sourcePath);

            Assert.Equal(2, offences.Count);
            Assert.All(offences, offence => Assert.Contains("silent", offence, StringComparison.Ordinal));
            Assert.DoesNotContain(offences, x => x.Contains("LoggedCatch", StringComparison.Ordinal));
            Assert.DoesNotContain(offences, x => x.Contains("RethrowCatch", StringComparison.Ordinal));
            Assert.DoesNotContain(offences, x => x.Contains("UsesExCatch", StringComparison.Ordinal));
            Assert.DoesNotContain(offences, x => x.Contains("AnnotatedCatch", StringComparison.Ordinal));
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

    public static IReadOnlyList<string> FindSilentBroadCatch(string sourceFile)
    {
        if (!UnusedPrivateMethodTests.IsAppCsPath(sourceFile) || !File.Exists(sourceFile))
        {
            return Array.Empty<string>();
        }

        var source = File.ReadAllText(sourceFile);
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var offences = new List<string>();

        foreach (Match match in BroadCatchRegex.Matches(source))
        {
            var varName = match.Groups["var"].Value;
            var body = match.Groups["body"].Value;

            if (BodyReferencesVar(body, varName))
            {
                continue;
            }

            if (BodyContainsToken(body, "throw"))
            {
                continue;
            }

            if (LoggingTokens.Any(token => body.Contains(token, StringComparison.Ordinal)))
            {
                continue;
            }

            var line = GetLineNumber(source, match.Index);
            if (IsAnnotated(lines, line))
            {
                continue;
            }

            offences.Add(
                $"{Path.GetFileName(sourceFile)}({line}): silent catch (Exception {varName})  body neither logs, rethrows, nor references the exception");
        }

        return offences;
    }

    private static bool BodyReferencesVar(string body, string varName)
    {
        var regex = new Regex($@"\b{Regex.Escape(varName)}\b", RegexOptions.CultureInvariant);
        return regex.IsMatch(body);
    }

    private static bool BodyContainsToken(string body, string token)
    {
        var regex = new Regex($@"\b{Regex.Escape(token)}\b", RegexOptions.CultureInvariant);
        return regex.IsMatch(body);
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
