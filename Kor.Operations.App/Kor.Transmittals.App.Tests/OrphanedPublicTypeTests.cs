#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Kor.Operations.App.Tests;

/// <summary>
/// Catches whole types that no production code references any more.
///
/// The existing detectors cover private types, private methods, ViewModel properties,
/// commands and resource keys — all member- or visibility-scoped. None of them notice a
/// PUBLIC type whose last call site was deleted, which is how the CfoMetrics subsystem
/// sat dead for three and a half months while the suite stayed green: its classes were
/// public, still compiled, still had unit tests, and were even maintained through a
/// TotalFee refactor — but nothing in the app had rendered them since 2026-04-14.
///
/// A type counts as ALIVE if its name appears in any production .cs file other than the
/// one declaring it, or anywhere in XAML. References from the test project alone do NOT
/// count — tests keeping a dead subsystem compiling is precisely the failure mode here.
///
/// This is a heuristic over identifiers, not a compiler-accurate reference graph, so it
/// is deliberately conservative: anything reachable by reflection, serialization, DI or
/// XAML is skipped rather than risk a false delete. Treat every hit as a CANDIDATE to
/// verify by hand, never as an automatic deletion.
/// </summary>
public sealed class OrphanedPublicTypeTests
{
    private static readonly Regex PublicTypeRegex = new(
        """
        ^[ \t]*
        public\s+
        (?<modifiers>(?:(?:static|sealed|abstract|partial|new|unsafe|readonly|ref|file)\s+)*)
        (?<kind>record\s+struct|record\s+class|record|class|struct|enum|interface)
        \s+
        (?<name>[A-Za-z_]\w*)
        """,
        RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Attributes that imply something outside the compiler reaches this type —
    /// serializers, DI containers, the MCP tool registry, analyzer suppressions.
    /// </summary>
    private static readonly Regex AttributeSkipRegex = new(
        @"\[\s*(?:[A-Za-z_]\w*\.)*(?:Serializable|JsonObject|JsonConverter|DataContract|XmlRoot|Conditional|Obsolete|SuppressMessage|McpServerToolType|McpServerTool|AttributeUsage)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Base types and interfaces that mean WPF, DI or the markup engine constructs this
    /// type by name at runtime, where no C# call site will ever exist.
    /// </summary>
    private static readonly string[] RuntimeResolvedBaseTypes =
    [
        "IValueConverter",
        "IMultiValueConverter",
        "MarkupExtension",
        "IAiContextProvider",
        "Window",
        "UserControl",
        "Page",
        "Application",
        "ResourceDictionary",
        "Attribute",
        "Exception",
        "IHostedService",
        "BackgroundService"
    ];

    private static readonly HashSet<string> ReservedTypeNames = new(StringComparer.Ordinal)
    {
        "App",
        "MainWindow",
        "Program",
        "Settings"
    };

    [Fact]
    public void All_public_types_have_a_production_consumer()
    {
        var repoRoot = XamlStaticResourceOrderTests.GetRepoRoot();
        var appRoot = Path.Combine(repoRoot, "Kor.Operations.App");

        // IsAppCsPath already excludes the test project, so this corpus is production-only.
        var productionFiles = UnusedPrivateMethodTests.CollectCsFileCorpus(appRoot);
        var xamlCorpus = UnusedPrivateTypeTests.CollectXamlIdentifierCorpus(appRoot);
        var testCorpus = CollectTestIdentifierCorpus(repoRoot);

        var offences = new List<string>();
        foreach (var file in productionFiles.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var productionCorpus = UnusedPrivateMethodTests.BuildIdentifierCorpus(productionFiles, file);
            offences.AddRange(FindOrphanedPublicTypes(file, productionCorpus, xamlCorpus, testCorpus));
        }

        offences = offences.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

        if (offences.Count > 0)
        {
            Assert.Fail(
                $"Found {offences.Count} public type(s) with no production consumer:\n"
                + string.Join(Environment.NewLine, offences)
                + "\n\nEach is a CANDIDATE, not a confirmed deletion. Verify by hand against .cs, .xaml,"
                + "\nDI registration, config and SQL before removing. If a type is genuinely reached at"
                + "\nruntime in a way identifier matching cannot see, add it to the skip rules with a"
                + "\ncomment saying how it is reached.");
        }
    }

    [Fact]
    public void Analyzer_flags_orphaned_public_type_against_synthetic_class()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "OrphanedPublicTypeBad.cs.txt");
        var tempDir = Path.Combine(Path.GetTempPath(), "KorOrphanedPublicTypeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "OrphanedPublicTypeBad.cs");
            File.WriteAllText(sourcePath, File.ReadAllText(fixturePath));

            // ProductionlyUsedType is named by other production code; TestOnlyType only by tests;
            // TotallyOrphanedType by nobody. Only the last two should be reported.
            var productionCorpus = new HashSet<string>(StringComparer.Ordinal) { "ProductionlyUsedType" };
            var xamlCorpus = new HashSet<string>(StringComparer.Ordinal) { "XamlBoundConverter" };
            var testCorpus = new HashSet<string>(StringComparer.Ordinal) { "TestOnlyType" };

            var offences = FindOrphanedPublicTypes(sourcePath, productionCorpus, xamlCorpus, testCorpus);

            Assert.Equal(2, offences.Count);
            Assert.Contains(offences, o => o.Contains("TotallyOrphanedType", StringComparison.Ordinal));
            Assert.Contains(offences, o => o.Contains("TestOnlyType", StringComparison.Ordinal)
                                        && o.Contains("only the test project", StringComparison.Ordinal));
            Assert.DoesNotContain(offences, o => o.Contains("ProductionlyUsedType", StringComparison.Ordinal));
            Assert.DoesNotContain(offences, o => o.Contains("XamlBoundConverter", StringComparison.Ordinal));
            Assert.DoesNotContain(offences, o => o.Contains("SerializedDto", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    public static IReadOnlyList<string> FindOrphanedPublicTypes(
        string sourceFile,
        IReadOnlySet<string> productionCorpus,
        IReadOnlySet<string> xamlCorpus,
        IReadOnlySet<string> testCorpus)
    {
        if (!UnusedPrivateMethodTests.IsAppCsPath(sourceFile) || !File.Exists(sourceFile))
        {
            return Array.Empty<string>();
        }

        // IsAppCsPath only excludes the main test project. Several other test projects live
        // under the app folder (EngineeringTools, PdfToSafe and friends), and their fixtures
        // are not production code either.
        if (IsTestSource(sourceFile))
        {
            return Array.Empty<string>();
        }

        var source = File.ReadAllText(sourceFile);
        var lines = SplitLines(source);
        var offences = new List<string>();

        foreach (Match match in PublicTypeRegex.Matches(source))
        {
            var typeName = match.Groups["name"].Value.Trim();
            var modifiers = match.Groups["modifiers"].Value;
            var line = GetLineNumber(source, match.Index);

            // `partial` is the WPF code-behind marker — the other half is generated from XAML.
            if (modifiers.Contains("partial", StringComparison.Ordinal))
            {
                continue;
            }

            if (ReservedTypeNames.Contains(typeName))
            {
                continue;
            }

            if (HasSkippedAttributeAbove(lines, line))
            {
                continue;
            }

            if (DerivesFromRuntimeResolvedType(lines, line))
            {
                continue;
            }

            if (productionCorpus.Contains(typeName) || xamlCorpus.Contains(typeName))
            {
                continue;
            }

            // A public row/DTO type declared at the foot of a ViewModel and consumed by that
            // same ViewModel is legitimate, so local use counts as alive. But the declaration
            // itself and every constructor repeat the type name, so those must be discounted
            // first — otherwise a dead class with a constructor looks used (CfoMetricRegistry
            // is exactly that shape: declaration + ctor = two occurrences, zero real uses).
            if (CountRealLocalUses(source, typeName) > 0)
            {
                continue;
            }

            var reason = testCorpus.Contains(typeName)
                ? "referenced by only the test project, never by production code"
                : "no reference anywhere outside its declaring file";

            offences.Add($"{Path.GetFileName(sourceFile)}({line}): public type '{typeName}' — {reason}");
        }

        return offences;
    }

    /// <summary>
    /// Identifiers appearing anywhere in the test project. Used to tell a type kept alive
    /// solely by its own unit tests from one that nothing references at all — both are
    /// orphans, but the first is the more deceptive kind.
    /// </summary>
    public static IReadOnlySet<string> CollectTestIdentifierCorpus(string repoRoot)
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        var testRoot = Path.Combine(repoRoot, "Kor.Operations.App", "Kor.Transmittals.App.Tests");
        if (!Directory.Exists(testRoot))
        {
            return identifiers;
        }

        var identifierRegex = new Regex(@"\b[A-Za-z_]\w*\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        foreach (var path in Directory.EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories))
        {
            var normalized = Path.GetFullPath(path);
            var separator = Path.DirectorySeparatorChar;
            if (normalized.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (Match m in identifierRegex.Matches(File.ReadAllText(normalized)))
            {
                identifiers.Add(m.Value);
            }
        }

        return identifiers;
    }

    /// <summary>
    /// True for any source file belonging to a test project or test fixture, wherever it sits.
    /// </summary>
    private static bool IsTestSource(string path)
    {
        var normalized = Path.GetFullPath(path).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var separator = Path.DirectorySeparatorChar;
        var fileName = Path.GetFileName(normalized);

        return fileName.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"{separator}Tests{separator}", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(".Tests", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"{separator}Fixtures{separator}", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Occurrences of <paramref name="typeName"/> in its own file that represent actual USE,
    /// discounting the type declaration and any constructor declarations — both of which
    /// repeat the name without consuming the type.
    /// </summary>
    private static int CountRealLocalUses(string source, string typeName)
    {
        var escaped = Regex.Escape(typeName);

        var declarationRegex = new Regex(
            $@"^[ \t]*(?:public|internal|private|protected)\s+(?:[\w]+\s+)*(?:record\s+struct|record\s+class|record|class|struct|enum|interface)\s+{escaped}\b",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        var constructorRegex = new Regex(
            $@"^[ \t]*(?:public|internal|private|protected)\s+(?:static\s+)?{escaped}\s*\(",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        var stripped = declarationRegex.Replace(source, string.Empty);
        stripped = constructorRegex.Replace(stripped, string.Empty);

        return Regex.Matches(stripped, $@"\b{escaped}\b", RegexOptions.CultureInvariant).Count;
    }

    private static bool DerivesFromRuntimeResolvedType(string[] lines, int line)
    {
        // Look at the declaration line and the two after it — base lists often wrap.
        var start = Math.Max(0, line - 1);
        var end = Math.Min(lines.Length, line + 2);
        for (var i = start; i < end; i++)
        {
            foreach (var baseType in RuntimeResolvedBaseTypes)
            {
                if (Regex.IsMatch(lines[i], $@"[:,]\s*(?:[A-Za-z_]\w*\.)*{Regex.Escape(baseType)}\b"))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasSkippedAttributeAbove(string[] lines, int line)
    {
        var declarationIndex = line - 1;
        var startIndex = Math.Max(0, declarationIndex - 3);
        for (var i = startIndex; i < declarationIndex && i < lines.Length; i++)
        {
            if (AttributeSkipRegex.IsMatch(lines[i]))
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

    private static string[] SplitLines(string source)
    {
        return source.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }
}
