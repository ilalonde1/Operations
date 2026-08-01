#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Kor.Operations.App.Opportunities;
using Xunit;

namespace Kor.Operations.App.Tests;

public sealed class UnusedViewModelPropertyTests
{
    private static readonly string[] ViewModelSuffixes = ["ViewModel", "Vm"];

    private static readonly string[] AttributeSkipNameFragments =
    [
        "JsonProperty",
        "JsonPropertyName",
        "XmlElement",
        "DataMember",
        "AiContext",
        "Browsable"
    ];

    private static readonly string[] PropertyAttributeSkipNameFragments =
    [
        "JsonProperty",
        "JsonPropertyName",
        "XmlElement",
        "DataMember",
        "AiContext",
        "Browsable",
        "Obsolete"
    ];

    private static readonly Regex BindingRegex = new(
        @"\{Binding\s+(?:Path\s*=\s*)?(?<path>[A-Za-z_][\w.]*)(?:\s*[,}].*)?\}",
        RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex IdentifierRegex = new(
        @"\b[A-Za-z_]\w*\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void All_ViewModel_properties_have_a_consumer()
    {
        var repoRoot = XamlStaticResourceOrderTests.GetRepoRoot();
        var appRoot = Path.Combine(repoRoot, "Kor.Operations.App");
        var bindingPaths = CollectAllBindingPaths(appRoot);
        var allCsCorpus = CollectExternalReferenceCorpus(appRoot, excludeFile: "");
        var appAssembly = typeof(OpportunitiesWindow).Assembly;
        var offences = new List<string>();

        foreach (var vmType in appAssembly.GetTypes()
            .Where(t => t.IsPublic && ViewModelSuffixes.Any(s => t.Name.EndsWith(s, StringComparison.Ordinal)))
            .OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            var declaringFile = FindDeclaringSourceFile(appRoot, vmType.Name);
            if (declaringFile is null)
            {
                continue;
            }

            var externalTokens = BuildIdentifierCorpus(allCsCorpus, declaringFile);
            offences.AddRange(FindUnusedViewModelProperties(vmType, externalTokens, bindingPaths, declaringFile));
        }

        offences = offences
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (offences.Count > 0)
        {
            Assert.Fail(
                $"Found {offences.Count} ViewModel public instance propertie(s) with no detected consumer:\n"
                + string.Join(Environment.NewLine, offences));
        }
    }

    [Fact]
    public void Analyzer_flags_unused_property_against_synthetic_vm()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "KorUnusedVmPropertyTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var vmPath = Path.Combine(tempDir, "FakeUnusedVm.cs");
            var otherPath = Path.Combine(tempDir, "OtherFile.cs");

            File.WriteAllText(vmPath, """
                public sealed class FakeUnusedVm
                {
                    public string UsedProp { get; } = "";
                    public string UnusedProp { get; } = "";
                }
                """);
            File.WriteAllText(otherPath, """
                public sealed class OtherFile
                {
                    public string Read(FakeUnusedVm vm) => vm.UsedProp;
                }
                """);

            var corpus = CollectExternalReferenceCorpus(tempDir, vmPath);
            var tokens = BuildIdentifierCorpus(corpus, excludeFile: "");
            var offences = FindUnusedViewModelProperties(
                typeof(FakeUnusedVm),
                tokens,
                new HashSet<string>(StringComparer.Ordinal),
                vmPath);

            var offence = Assert.Single(offences);
            Assert.Contains("UnusedProp", offence, StringComparison.Ordinal);
            Assert.DoesNotContain(offences, x => x.Contains("UsedProp", StringComparison.Ordinal));
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

    public static IReadOnlyList<string> FindUnusedViewModelProperties(
        Type vmType,
        IReadOnlySet<string> externalReferenceCorpus,
        HashSet<string> bindingPathsFromAllXaml,
        string vmDeclaringSourcePath)
    {
        if (ShouldSkipViewModelType(vmType, vmDeclaringSourcePath))
        {
            return Array.Empty<string>();
        }

        var offences = new List<string>();
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        foreach (var property in vmType.GetProperties(Flags).OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (ShouldSkipProperty(vmType, property))
            {
                continue;
            }

            if (bindingPathsFromAllXaml.Contains(property.Name) || externalReferenceCorpus.Contains(property.Name))
            {
                continue;
            }

            offences.Add(
                $"{Path.GetFileName(vmDeclaringSourcePath)}: property '{property.Name}' has no XAML binding and no reference outside its declaring file");
        }

        return offences;
    }

    public static HashSet<string> CollectAllBindingPaths(string appRoot)
    {
        var bindingPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var xamlPath in Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(XamlStaticResourceOrderTests.IsAppXamlPath))
        {
            XDocument document;
            try
            {
                document = XDocument.Load(xamlPath, LoadOptions.SetLineInfo);
            }
            catch
            {
                continue;
            }

            foreach (var attribute in document.Descendants().Attributes())
            {
                foreach (Match match in BindingRegex.Matches(attribute.Value))
                {
                    AddBindingPath(bindingPaths, match.Groups["path"].Value);
                }
            }

            foreach (var bindingElement in document.Descendants()
                .Where(e => string.Equals(e.Name.LocalName, "Binding", StringComparison.OrdinalIgnoreCase)))
            {
                AddBindingPath(bindingPaths, bindingElement.Attribute("Path")?.Value);
            }
        }

        return bindingPaths;
    }

    public static IReadOnlyDictionary<string, string> CollectExternalReferenceCorpus(
        string appRoot,
        string excludeFile)
    {
        var normalizedExclude = string.IsNullOrWhiteSpace(excludeFile)
            ? ""
            : Path.GetFullPath(excludeFile);
        var corpus = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsAppCsPath))
        {
            var fullPath = Path.GetFullPath(path);
            if (!string.IsNullOrWhiteSpace(normalizedExclude)
                && string.Equals(fullPath, normalizedExclude, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            corpus[fullPath] = File.ReadAllText(fullPath);
        }

        return corpus;
    }

    private static bool ShouldSkipViewModelType(Type vmType, string vmDeclaringSourcePath)
    {
        if (ImplementsAiContextProvider(vmType)
            || vmType.IsSerializable
            || HasAttributeNameFragment(vmType.GetCustomAttributes(inherit: true), AttributeSkipNameFragments)
            || IsPartialClass(vmType, vmDeclaringSourcePath))
        {
            return true;
        }

        return false;
    }

    private static bool ImplementsAiContextProvider(Type vmType)
    {
        var aiContextProvider = vmType.Assembly.GetTypes()
            .FirstOrDefault(t => string.Equals(t.Name, "IAiContextProvider", StringComparison.Ordinal));
        if (aiContextProvider is not null && aiContextProvider.IsAssignableFrom(vmType))
        {
            return true;
        }

        return vmType.GetInterfaces()
            .Any(i => string.Equals(i.Name, "IAiContextProvider", StringComparison.Ordinal));
    }

    private static bool IsPartialClass(Type vmType, string vmDeclaringSourcePath)
    {
        var sourceRoot = FindSourceRoot(vmDeclaringSourcePath);
        var pattern = new Regex(
            $@"\bpartial\s+class\s+{Regex.Escape(vmType.Name)}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsAppCsPath))
        {
            if (pattern.IsMatch(File.ReadAllText(path)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldSkipProperty(Type vmType, PropertyInfo property)
    {
        if (property.Name.StartsWith("_", StringComparison.Ordinal)
            || property.Name.StartsWith("<", StringComparison.Ordinal)
            || property.GetIndexParameters().Length > 0
            || IsVirtualAbstractOrOverride(property)
            || IsInterfaceProperty(vmType, property.Name)
            || HasAttributeNameFragment(property.GetCustomAttributes(inherit: true), PropertyAttributeSkipNameFragments)
            || HasAttributeNameFragment(property.GetCustomAttributes(inherit: true), ["IndexerName"]))
        {
            return true;
        }

        return false;
    }

    private static bool IsVirtualAbstractOrOverride(PropertyInfo property)
    {
        var accessor = property.GetMethod ?? property.SetMethod;
        if (accessor is null)
        {
            return false;
        }

        return accessor.IsVirtual
            || accessor.IsAbstract
            || accessor.GetBaseDefinition().DeclaringType != accessor.DeclaringType;
    }

    private static bool IsInterfaceProperty(Type vmType, string propertyName)
    {
        return vmType.GetInterfaces().Any(i => i.GetProperty(propertyName) is not null);
    }

    private static bool HasAttributeNameFragment(IEnumerable<object> attributes, IReadOnlyList<string> fragments)
    {
        return attributes.Any(attribute =>
        {
            var name = attribute.GetType().Name;
            return fragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        });
    }

    private static void AddBindingPath(HashSet<string> bindingPaths, string? rawPath)
    {
        foreach (var segment in EnumerateBindingPathSegments(rawPath))
        {
            bindingPaths.Add(segment);
        }
    }

    private static string? ExtractTopLevelBindingIdentifier(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        var path = rawPath.Trim();
        if (path.Equals(".", StringComparison.Ordinal))
        {
            return null;
        }

        var stopIndex = path.IndexOfAny(['.', '[', '/']);
        return stopIndex > 0 ? path[..stopIndex] : path;
    }

    private static IEnumerable<string> EnumerateBindingPathSegments(string? rawPath)
    {
        // For dotted paths like "Cover.HasCoverPhoto" or
        // "DataContext.TwoMoAgoPeriodLabel" every segment is a real property
        // consumer — root and leaf both count. Walk all of them.
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            yield break;
        }

        var path = rawPath.Trim();
        if (path.Equals(".", StringComparison.Ordinal))
        {
            yield break;
        }

        foreach (var raw in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var stopIndex = raw.IndexOfAny(['[', '/']);
            var segment = stopIndex >= 0 ? raw[..stopIndex] : raw;
            if (!string.IsNullOrWhiteSpace(segment))
            {
                yield return segment;
            }
        }
    }

    private static IReadOnlySet<string> BuildIdentifierCorpus(
        IReadOnlyDictionary<string, string> corpus,
        string excludeFile)
    {
        var normalizedExclude = string.IsNullOrWhiteSpace(excludeFile)
            ? ""
            : Path.GetFullPath(excludeFile);
        var tokens = new HashSet<string>(StringComparer.Ordinal);

        foreach (var kv in corpus)
        {
            if (!string.IsNullOrWhiteSpace(normalizedExclude)
                && string.Equals(kv.Key, normalizedExclude, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (Match match in IdentifierRegex.Matches(kv.Value))
            {
                tokens.Add(match.Value);
            }
        }

        return tokens;
    }

    private static string? FindDeclaringSourceFile(string appRoot, string simpleName)
    {
        var matches = Directory.EnumerateFiles(appRoot, simpleName + ".cs", SearchOption.AllDirectories)
            .Where(IsAppCsPath)
            .Select(Path.GetFullPath)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    private static string FindSourceRoot(string sourcePath)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Kor.Operations.App.csproj")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? Directory.GetCurrentDirectory();
    }

    private static bool IsAppCsPath(string path)
    {
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var separator = Path.DirectorySeparatorChar;
        return !normalized.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeUnusedVm
    {
        public string UsedProp { get; } = "";

        public string UnusedProp { get; } = "";
    }
}
