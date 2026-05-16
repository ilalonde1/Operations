#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Kor.Operations.App.Tests;

public sealed class LocaleSensitiveParseTests
{
    // Matches `<NumericType>.Parse(<args>)` with one level of nested parens.
    private static readonly Regex ParseCallRegex = new(
        @"\b(?<type>int|long|short|byte|double|decimal|float|Int16|Int32|Int64|Byte|Double|Decimal|Single|DateTime|TimeSpan)\.Parse\s*\((?<args>[^()]*(?:\([^()]*\)[^()]*)*)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const string AnnotationToken = "// locale OK";
    private const int AnnotationLookbackLines = 3;

    [Fact]
    public void No_locale_sensitive_parse_in_App()
    {
        var repoRoot = XamlStaticResourceOrderTests.GetRepoRoot();
        var appRoot = Path.Combine(repoRoot, "Kor.Operations.App");
        var offences = Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(UnusedPrivateMethodTests.IsAppCsPath)
            .SelectMany(FindLocaleSensitiveParse)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (offences.Count > 0)
        {
            Assert.Fail(
                $"Found {offences.Count} locale-sensitive .Parse() call(s) without CultureInfo. "
                + "Add `CultureInfo.InvariantCulture` argument (for stored / invariant data) or "
                + $"`{AnnotationToken}: <reason>` annotation (for user-input scenarios):\n"
                + string.Join(Environment.NewLine, offences));
        }
    }

    [Fact]
    public void Analyzer_flags_locale_sensitive_parse_against_synthetic_class()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "KorLocaleSensitiveParseTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "LocaleSensitiveParseBad.cs.txt");
            var sourcePath = Path.Combine(tempDir, "LocaleSensitiveParseBad.cs");
            File.WriteAllText(sourcePath, File.ReadAllText(fixturePath));

            var offences = FindLocaleSensitiveParse(sourcePath);

            Assert.Equal(2, offences.Count);
            Assert.All(offences, offence => Assert.Contains(".Parse", offence, StringComparison.Ordinal));
            Assert.DoesNotContain(offences, x => x.Contains("WithInvariantCulture", StringComparison.Ordinal));
            Assert.DoesNotContain(offences, x => x.Contains("WithCultureInfoArg", StringComparison.Ordinal));
            Assert.DoesNotContain(offences, x => x.Contains("AnnotatedSameLine", StringComparison.Ordinal));
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

    public static IReadOnlyList<string> FindLocaleSensitiveParse(string sourceFile)
    {
        if (!UnusedPrivateMethodTests.IsAppCsPath(sourceFile) || !File.Exists(sourceFile))
        {
            return Array.Empty<string>();
        }

        var source = File.ReadAllText(sourceFile);
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var offences = new List<string>();

        foreach (Match match in ParseCallRegex.Matches(source))
        {
            var args = match.Groups["args"].Value;
            if (ContainsCultureIndicator(args))
            {
                continue;
            }

            var line = GetLineNumber(source, match.Index);
            if (IsAnnotated(lines, line))
            {
                continue;
            }

            var type = match.Groups["type"].Value;
            offences.Add(
                $"{Path.GetFileName(sourceFile)}({line}): {type}.Parse(...) without CultureInfo  "
                + "add CultureInfo.InvariantCulture or `// locale OK: <reason>`");
        }

        return offences;
    }

    private static bool ContainsCultureIndicator(string args)
    {
        return args.Contains("Culture", StringComparison.OrdinalIgnoreCase)
            || args.Contains("Invariant", StringComparison.OrdinalIgnoreCase)
            || args.Contains("IFormatProvider", StringComparison.OrdinalIgnoreCase);
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
