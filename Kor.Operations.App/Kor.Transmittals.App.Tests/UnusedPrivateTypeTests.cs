#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Kor.Operations.App.Tests;

public sealed class UnusedPrivateTypeTests
{
    private static readonly Regex PrivateTypeRegex = new(
        """
        ^[ \t]*
        private\s+
        (?:(?:static|sealed|abstract|partial|new|unsafe|readonly|ref|file)\s+)*
        (?<kind>record\s+struct|record\s+class|record|class|struct|enum|interface)
        \s+
        (?<name>[A-Za-z_]\w*)
        (?:\s*<[^>]+>)?
        \s*
        (?:[:({]|$|\r|\n)
        """,
        RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex XamlIdentifierRegex = new(
        @"^[A-Za-z_]\w*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AttributeSkipRegex = new(
        @"\[\s*(?:[A-Za-z_]\w*\.)*(?:Serializable|JsonObject|DataContract|XmlRoot|Conditional|Obsolete|SuppressMessage)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> ReservedTypeNames = new(StringComparer.Ordinal)
    {
        "List",
        "Dictionary",
        "HashSet",
        "Stack",
        "Queue",
        "ICommand"
    };

    [Fact]
    public void All_private_nested_types_have_a_reference()
    {
        var repoRoot = XamlStaticResourceOrderTests.GetRepoRoot();
        var appRoot = Path.Combine(repoRoot, "Kor.Operations.App");
        var xamlIdentifierCorpus = CollectXamlIdentifierCorpus(appRoot);
        var csFileCorpus = UnusedPrivateMethodTests.CollectCsFileCorpus(appRoot);
        var offences = new List<string>();

        foreach (var file in csFileCorpus.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var externalCorpus = UnusedPrivateMethodTests.BuildIdentifierCorpus(csFileCorpus, file);
            offences.AddRange(FindUnusedPrivateTypes(file, externalCorpus, xamlIdentifierCorpus));
        }

        offences = offences
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (offences.Count > 0)
        {
            Assert.Fail(
                $"Found {offences.Count} private nested type(s) with no detected reference:\n"
                + string.Join(Environment.NewLine, offences));
        }
    }

    [Fact]
    public void Analyzer_flags_unused_private_type_against_synthetic_class()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "KorUnusedPrivateTypeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "UnusedPrivateTypeBad.cs.txt");
            var sourcePath = Path.Combine(tempDir, "UnusedPrivateTypeBad.cs");
            File.WriteAllText(sourcePath, File.ReadAllText(fixturePath));

            var fixtureText = File.ReadAllText(sourcePath);
            var externalCorpus = UnusedPrivateMethodTests.CountIdentifierOccurrencesInText(fixtureText)
                .Where(kv => kv.Value > 1)
                .Select(kv => kv.Key)
                .ToHashSet(StringComparer.Ordinal);
            var offences = FindUnusedPrivateTypes(
                sourcePath,
                externalCorpus,
                new HashSet<string>(StringComparer.Ordinal));

            var offence = Assert.Single(offences);
            Assert.Contains("OrphanHelper", offence, StringComparison.Ordinal);
            Assert.DoesNotContain(offences, x => x.Contains("UsedHelper", StringComparison.Ordinal));
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

    public static IReadOnlyList<string> FindUnusedPrivateTypes(
        string sourceFile,
        IReadOnlySet<string> externalIdentifierCorpus,
        IReadOnlySet<string> xamlIdentifierCorpus)
    {
        if (!UnusedPrivateMethodTests.IsAppCsPath(sourceFile) || !File.Exists(sourceFile))
        {
            return Array.Empty<string>();
        }

        var source = File.ReadAllText(sourceFile);
        var sourceLines = SplitLines(source);
        var localTokenCounts = UnusedPrivateMethodTests.CountIdentifierOccurrencesInText(source);
        var offences = new List<string>();

        foreach (Match match in PrivateTypeRegex.Matches(source))
        {
            var typeName = match.Groups["name"].Value.Trim();
            var line = GetLineNumber(source, match.Index);

            if (ShouldSkipType(typeName, sourceLines, line))
            {
                continue;
            }

            if (localTokenCounts.TryGetValue(typeName, out var localCount) && localCount > 1)
            {
                continue;
            }

            if (externalIdentifierCorpus.Contains(typeName) || xamlIdentifierCorpus.Contains(typeName))
            {
                continue;
            }

            offences.Add(
                $"{Path.GetFileName(sourceFile)}({line}): private nested type '{typeName}' has no reference  declared but never instantiated or named anywhere");
        }

        return offences;
    }

    public static IReadOnlySet<string> CollectXamlIdentifierCorpus(string appRoot)
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        if (!Directory.Exists(appRoot))
        {
            return identifiers;
        }

        foreach (var xamlPath in Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(XamlStaticResourceOrderTests.IsAppXamlPath))
        {
            XDocument document;
            try
            {
                document = XDocument.Load(xamlPath, LoadOptions.None);
            }
            catch
            {
                continue;
            }

            foreach (var element in document.Descendants())
            {
                AddXamlIdentifier(identifiers, element.Name.LocalName);
                foreach (var attribute in element.Attributes())
                {
                    AddXamlIdentifier(identifiers, attribute.Value);
                }
            }
        }

        return identifiers;
    }

    private static bool ShouldSkipType(string typeName, string[] sourceLines, int line)
    {
        return ReservedTypeNames.Contains(typeName)
            || HasSkippedAttributeInPreviousLines(sourceLines, line);
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

    private static void AddXamlIdentifier(HashSet<string> identifiers, string rawValue)
    {
        var value = rawValue.Trim();
        if (XamlIdentifierRegex.IsMatch(value))
        {
            identifiers.Add(value);
        }
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
}
