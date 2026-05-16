#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Kor.Operations.App.Tests;

public sealed class UnusedPrivateMethodTests
{
    private static readonly Regex PrivateMethodRegex = new(
        """
        ^[ \t]*
        private\s+
        (?:(?:static|async|sealed|virtual|override|new|extern|unsafe|partial)\s+)*
        (?<ret>[\w<>?\s,\[\]\.]+?)
        \s+
        (?<name>[A-Za-z_]\w*)
        \s*
        (?:<[^>]+>)?   # optional generic method type-parameter list
        \s*
        \(             # method declarations REQUIRE an opening paren — distinguishes them
                       # from field declarations like `private Foo<T> _bar = …`
        """,
        RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex IdentifierRegex = new(
        @"\b[A-Za-z_]\w*\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex XamlEventHandlerRegex = new(
        @"^[A-Za-z_]\w*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AttributeSkipRegex = new(
        @"\[\s*(?:[A-Za-z_]\w*\.)*(?:Fact|Theory|TestCase|DataTestMethod|Conditional|DllImport|LibraryImport|Obsolete|GeneratedRegex|SuppressMessage|UnmanagedCallersOnly)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> ReservedMethodNames = new(StringComparer.Ordinal)
    {
        "Dispose",
        "Finalize",
        "ToString",
        "Equals",
        "GetHashCode",
        "Main"
    };

    [Fact]
    public void All_private_methods_have_a_caller()
    {
        var repoRoot = XamlStaticResourceOrderTests.GetRepoRoot();
        var appRoot = Path.Combine(repoRoot, "Kor.Operations.App");
        var xamlEventHandlerCorpus = CollectXamlEventHandlerCorpus(appRoot);
        var csFileCorpus = CollectCsFileCorpus(appRoot);
        var offences = new List<string>();

        foreach (var file in csFileCorpus.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var externalCorpus = BuildIdentifierCorpus(csFileCorpus, file);
            offences.AddRange(FindUnusedPrivateMethods(file, externalCorpus, xamlEventHandlerCorpus));
        }

        offences = offences
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (offences.Count > 0)
        {
            Assert.Fail(
                $"Found {offences.Count} private method(s) with no detected caller:\n"
                + string.Join(Environment.NewLine, offences));
        }
    }

    [Fact]
    public void Analyzer_flags_unused_private_method_against_synthetic_class()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "KorUnusedPrivateMethodTests", Guid.NewGuid().ToString("N"));
        var emptyXamlRoot = Path.Combine(tempDir, "EmptyXaml");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(emptyXamlRoot);
        try
        {
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "UnusedPrivateMethodBad.cs.txt");
            var sourcePath = Path.Combine(tempDir, "UnusedPrivateMethodBad.cs");
            var siblingPath = Path.Combine(tempDir, "SiblingFile.cs");

            File.WriteAllText(sourcePath, File.ReadAllText(fixturePath));
            File.WriteAllText(siblingPath, """
                public static class SiblingFile
                {
                    public const string Reference = nameof(UsedPrivateMethod);
                }
                """);

            var externalCorpus = CollectCsIdentifierCorpus(tempDir, sourcePath);
            var xamlEventHandlerCorpus = CollectXamlEventHandlerCorpus(emptyXamlRoot);
            var offences = FindUnusedPrivateMethods(sourcePath, externalCorpus, xamlEventHandlerCorpus);

            var offence = Assert.Single(offences);
            Assert.Contains("OrphanPrivateMethod", offence, StringComparison.Ordinal);
            Assert.DoesNotContain(offences, x => x.Contains("UsedPrivateMethod", StringComparison.Ordinal));
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

    public static IReadOnlyList<string> FindUnusedPrivateMethods(
        string sourceFile,
        IReadOnlySet<string> externalIdentifierCorpus,
        IReadOnlySet<string> xamlEventHandlerCorpus)
    {
        if (!IsAppCsPath(sourceFile) || !File.Exists(sourceFile))
        {
            return Array.Empty<string>();
        }

        var source = File.ReadAllText(sourceFile);
        var sourceLines = SplitLines(source);
        var topLevelClassName = GetTopLevelClassName(source);
        var localTokenCounts = CountIdentifierOccurrencesInText(source);
        var offences = new List<string>();

        foreach (Match match in PrivateMethodRegex.Matches(source))
        {
            var returnType = match.Groups["ret"].Value.Trim();
            var methodName = match.Groups["name"].Value.Trim();
            var line = GetLineNumber(source, match.Index);

            if (ShouldSkipMethod(methodName, returnType, topLevelClassName, sourceLines, line))
            {
                continue;
            }

            // Same-file count > 1 means there's at least one caller besides the
            // declaration itself. This is the common case — most private methods
            // are only invoked from within their declaring class.
            if (localTokenCounts.TryGetValue(methodName, out var localCount) && localCount > 1)
            {
                continue;
            }

            if (externalIdentifierCorpus.Contains(methodName) || xamlEventHandlerCorpus.Contains(methodName))
            {
                continue;
            }

            offences.Add(
                $"{Path.GetFileName(sourceFile)}({line}): private method '{methodName}' has no caller  not in any .cs and not wired from XAML");
        }

        return offences;
    }

    private static IReadOnlyDictionary<string, int> CountIdentifierOccurrencesInText(string source)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match match in IdentifierRegex.Matches(source))
        {
            var token = match.Value;
            counts[token] = counts.TryGetValue(token, out var current) ? current + 1 : 1;
        }

        return counts;
    }

    public static IReadOnlySet<string> CollectCsIdentifierCorpus(
        string appRoot,
        string excludeFile)
    {
        var corpus = CollectCsFileCorpus(appRoot);
        return BuildIdentifierCorpus(corpus, excludeFile);
    }

    public static IReadOnlySet<string> CollectXamlEventHandlerCorpus(string appRoot)
    {
        if (!Directory.Exists(appRoot))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var handlers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var xamlPath in Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(XamlStaticResourceOrderTests.IsAppXamlPath))
        {
            string text;
            try
            {
                text = File.ReadAllText(xamlPath);
            }
            catch
            {
                continue;
            }

            foreach (Match match in Regex.Matches(
                text,
                @"\s[\w:.-]+\s*=\s*""(?<value>[^""]*)""|\s[\w:.-]+\s*=\s*'(?<value>[^']*)'",
                RegexOptions.CultureInvariant))
            {
                var value = match.Groups["value"].Value.Trim();
                if (XamlEventHandlerRegex.IsMatch(value))
                {
                    handlers.Add(value);
                }
            }
        }

        return handlers;
    }

    private static IReadOnlyDictionary<string, string> CollectCsFileCorpus(string appRoot)
    {
        var corpus = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(appRoot))
        {
            return corpus;
        }

        foreach (var path in Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsAppCsPath))
        {
            var fullPath = Path.GetFullPath(path);
            corpus[fullPath] = File.ReadAllText(fullPath);
        }

        return corpus;
    }

    private static IReadOnlySet<string> BuildIdentifierCorpus(
        IReadOnlyDictionary<string, string> csFileCorpus,
        string excludeFile)
    {
        var normalizedExclude = string.IsNullOrWhiteSpace(excludeFile)
            ? ""
            : Path.GetFullPath(excludeFile);
        var identifiers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var kv in csFileCorpus)
        {
            if (!string.IsNullOrWhiteSpace(normalizedExclude)
                && string.Equals(kv.Key, normalizedExclude, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (Match match in IdentifierRegex.Matches(kv.Value))
            {
                identifiers.Add(match.Value);
            }
        }

        return identifiers;
    }

    private static bool ShouldSkipMethod(
        string methodName,
        string returnType,
        string? topLevelClassName,
        string[] sourceLines,
        int line)
    {
        if (string.Equals(methodName, "get", StringComparison.Ordinal)
            || string.Equals(methodName, "set", StringComparison.Ordinal)
            || ReservedMethodNames.Contains(methodName)
            || returnType.Contains("(", StringComparison.Ordinal)
            || (!string.IsNullOrWhiteSpace(topLevelClassName)
                && string.Equals(methodName, topLevelClassName, StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(returnType))
            || IsConventionEventHandler(methodName)
            || HasSkippedAttributeInPreviousLines(sourceLines, line))
        {
            return true;
        }

        return false;
    }

    private static bool IsConventionEventHandler(string methodName)
    {
        return methodName.StartsWith("On", StringComparison.Ordinal)
            && (methodName.EndsWith("Changed", StringComparison.Ordinal)
                || methodName.EndsWith("Changing", StringComparison.Ordinal)
                || methodName.EndsWith("Click", StringComparison.Ordinal));
    }

    private static bool HasSkippedAttributeInPreviousLines(string[] sourceLines, int line)
    {
        var declarationIndex = line - 1;
        var startIndex = Math.Max(0, declarationIndex - 3);
        for (var i = startIndex; i < declarationIndex; i++)
        {
            if (AttributeSkipRegex.IsMatch(sourceLines[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetTopLevelClassName(string source)
    {
        var match = Regex.Match(
            source,
            @"^\s*(?:public|internal|private|protected)?\s*(?:sealed|abstract|static|partial)?\s*class\s+(?<name>[A-Za-z_]\w*)\b",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["name"].Value : null;
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

    private static string[] SplitLines(string source)
    {
        return source.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }

    private static bool IsAppCsPath(string path)
    {
        var normalized = Path.GetFullPath(path)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var fileName = Path.GetFileName(normalized);
        var separator = Path.DirectorySeparatorChar;

        return !fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            && !fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
            && !fileName.EndsWith(".AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains($"{separator}Kor.Transmittals.App.Tests{separator}", StringComparison.OrdinalIgnoreCase);
    }
}
