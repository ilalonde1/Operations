#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace Kor.Operations.App.Tests;

public sealed class XamlStaticResourceOrderTests
{
    private static readonly Regex StaticResourceRegex = new(
        @"\{\s*StaticResource\s+(?:ResourceKey\s*=\s*)?([^,\}\s]+)[^}]*\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DynamicResourceRegex = new(
        @"\{\s*DynamicResource\s+(?:ResourceKey\s*=\s*)?([^,\}\s]+)[^}]*\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void All_App_xaml_files_have_resources_declared_before_use()
    {
        var repoRoot = GetRepoRoot();
        var appRoot = Path.Combine(repoRoot, "Kor.Operations.App");
        var offences = Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(IsAppXamlPath)
            .SelectMany(FindForwardStaticResourceReferences)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (offences.Count > 0)
        {
            Assert.Fail(
                $"Found {offences.Count} XAML StaticResource reference(s) declared after use:\n"
                + string.Join(Environment.NewLine, offences));
        }
    }

    [Fact]
    public void All_App_xaml_references_resolve_to_a_declared_key()
    {
        var repoRoot = GetRepoRoot();
        var appRoot = Path.Combine(repoRoot, "Kor.Operations.App");
        var globalKeys = BuildGlobalResourceKeys(appRoot);
        var offences = Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(IsAppXamlPath)
            .Where(path => !string.Equals(Path.GetFileName(path), "App.xaml", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => FindUnknownStaticResourceReferences(path, globalKeys))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (offences.Count > 0)
        {
            Assert.Fail(
                "Found " + offences.Count + " XAML references to keys that are not declared locally and are not in App.xaml's global resource set:\n"
                + string.Join(Environment.NewLine, offences));
        }
    }

    [Fact]
    public void Analyzer_flags_forward_ref_fixture()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ForwardRefBad.xaml");

        var offences = FindForwardStaticResourceReferences(fixturePath);

        var offence = Assert.Single(offences);
        Assert.Contains("ForwardBrush", offence, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyzer_flags_unknown_key_fixture()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "UnknownKeyBad.xaml");

        var offences = FindUnknownStaticResourceReferences(fixturePath, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var offence = Assert.Single(offences);
        Assert.Contains("ThisKeyDoesNotExistAnywhere", offence, StringComparison.Ordinal);
    }

    public static IReadOnlyList<string> FindForwardStaticResourceReferences(string xamlPath)
    {
        var document = LoadXaml(xamlPath);
        var declarations = CollectLocalKeyDeclarationLines(document);
        var offences = new List<string>();

        foreach (var reference in EnumerateResourceReferences(document, StaticResourceRegex, "StaticResource"))
        {
            var key = NormalizeResourceKey(reference.Key);
            if (ShouldSkipReferenceKey(key))
            {
                continue;
            }

            if (declarations.TryGetValue(key, out var declarationLine) && declarationLine > reference.Line)
            {
                offences.Add(
                    $"{Path.GetFileName(xamlPath)}({reference.Line}): {{StaticResource {reference.Key}}} references key '{key}' before it is declared on line {declarationLine}.");
            }
        }

        return offences;
    }

    public static IReadOnlyList<string> FindUnknownStaticResourceReferences(
        string xamlPath,
        IReadOnlySet<string> globalKeys)
    {
        var document = LoadXaml(xamlPath);
        var localKeys = CollectLocalKeys(document);
        var offences = new List<string>();

        AddUnknownResourceOffences(xamlPath, document, StaticResourceRegex, "StaticResource", localKeys, globalKeys, offences);
        AddUnknownResourceOffences(xamlPath, document, DynamicResourceRegex, "DynamicResource", localKeys, globalKeys, offences);

        return offences;
    }

    public static IReadOnlySet<string> BuildGlobalResourceKeys(string appProjectRoot)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appXamlPath = Path.Combine(appProjectRoot, "App.xaml");

        VisitResourceDictionary(appXamlPath, appProjectRoot, keys, visited);

        return keys;
    }

    private static void AddUnknownResourceOffences(
        string xamlPath,
        XDocument document,
        Regex regex,
        string resourceKind,
        IReadOnlySet<string> localKeys,
        IReadOnlySet<string> globalKeys,
        List<string> offences)
    {
        foreach (var reference in EnumerateResourceReferences(document, regex, resourceKind))
        {
            var key = NormalizeResourceKey(reference.Key);
            if (ShouldSkipReferenceKey(key))
            {
                continue;
            }

            if (localKeys.Contains(key) || globalKeys.Contains(key))
            {
                continue;
            }

            offences.Add(
                $"{Path.GetFileName(xamlPath)}({reference.Line}): {{{resourceKind} {reference.Key}}} references undeclared key '{key}' (not local, not in App.xaml or merged dictionaries).");
        }
    }

    private static void VisitResourceDictionary(
        string xamlPath,
        string appProjectRoot,
        HashSet<string> keys,
        HashSet<string> visited)
    {
        var fullPath = Path.GetFullPath(xamlPath);
        if (!visited.Add(fullPath) || !File.Exists(fullPath))
        {
            return;
        }

        XDocument document;
        try
        {
            document = XDocument.Load(fullPath, LoadOptions.SetLineInfo);
        }
        catch (XmlException ex)
        {
            Debug.WriteLine($"Skipping unparsable resource dictionary '{fullPath}': {ex.Message}");
            return;
        }

        foreach (var key in CollectLocalKeys(document))
        {
            keys.Add(key);
        }

        foreach (var source in document
            .Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "ResourceDictionary", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Attribute("Source")?.Value)
            .Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            var resolved = ResolveResourceDictionarySource(source!, fullPath, appProjectRoot);
            if (resolved is null)
            {
                continue;
            }

            VisitResourceDictionary(resolved, appProjectRoot, keys, visited);
        }
    }

    private static string? ResolveResourceDictionarySource(string source, string containingFile, string appProjectRoot)
    {
        var trimmed = source.Trim();
        const string PackPrefix = "pack://application:,,,";
        if (trimmed.StartsWith(PackPrefix, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[PackPrefix.Length..];
        }

        var componentIndex = trimmed.IndexOf(";component/", StringComparison.OrdinalIgnoreCase);
        if (componentIndex >= 0)
        {
            var assemblyName = trimmed[..componentIndex].TrimStart('/');
            var resourcePath = trimmed[(componentIndex + ";component/".Length)..];
            if (!string.Equals(assemblyName, "Kor.Operations.App", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine($"Skipping ResourceDictionary from unmapped assembly '{assemblyName}': {source}");
                return null;
            }

            return Path.GetFullPath(Path.Combine(appProjectRoot, ToLocalPath(resourcePath)));
        }

        if (trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            return Path.GetFullPath(Path.Combine(appProjectRoot, ToLocalPath(trimmed.TrimStart('/'))));
        }

        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(containingFile) ?? appProjectRoot, ToLocalPath(trimmed)));
    }

    private static IReadOnlyDictionary<string, int> CollectLocalKeyDeclarationLines(XDocument document)
    {
        var declarations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in EnumerateKeyAttributes(document))
        {
            var key = NormalizeResourceKey(attribute.Value);
            if (ShouldSkipReferenceKey(key))
            {
                continue;
            }

            var line = attribute is IXmlLineInfo lineInfo && lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1;
            if (!declarations.TryGetValue(key, out var existingLine) || line < existingLine)
            {
                declarations[key] = line;
            }
        }

        return declarations;
    }

    private static IReadOnlySet<string> CollectLocalKeys(XDocument document)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in EnumerateKeyAttributes(document))
        {
            var key = NormalizeResourceKey(attribute.Value);
            if (!ShouldSkipReferenceKey(key))
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    private static IEnumerable<XAttribute> EnumerateKeyAttributes(XDocument document)
    {
        return document
            .Descendants()
            .Attributes()
            .Where(a => string.Equals(a.Name.LocalName, "Key", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<ResourceReference> EnumerateResourceReferences(
        XDocument document,
        Regex regex,
        string resourceKind)
    {
        foreach (var attribute in document.Descendants().Attributes())
        {
            var line = attribute is IXmlLineInfo lineInfo && lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1;
            foreach (Match match in regex.Matches(attribute.Value))
            {
                if (match.Success)
                {
                    yield return new ResourceReference(resourceKind, Unquote(match.Groups[1].Value), line);
                }
            }
        }

        foreach (var element in document.Descendants())
        {
            if (element.HasElements)
            {
                continue;
            }

            var line = element is IXmlLineInfo lineInfo && lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1;
            foreach (Match match in regex.Matches(element.Value))
            {
                if (match.Success)
                {
                    yield return new ResourceReference(resourceKind, Unquote(match.Groups[1].Value), line);
                }
            }
        }
    }

    private static string NormalizeResourceKey(string key)
    {
        var trimmed = Unquote(key.Trim());
        if (trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var colonIndex = trimmed.IndexOf(':', StringComparison.Ordinal);
        return colonIndex > 0 ? trimmed[(colonIndex + 1)..] : trimmed;
    }

    private static bool ShouldSkipReferenceKey(string key) =>
        string.IsNullOrWhiteSpace(key) || key.StartsWith("{", StringComparison.Ordinal);

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2
            && ((trimmed[0] == '\'' && trimmed[^1] == '\'') || (trimmed[0] == '"' && trimmed[^1] == '"'))
            ? trimmed[1..^1]
            : trimmed;
    }

    private static XDocument LoadXaml(string xamlPath) =>
        XDocument.Load(xamlPath, LoadOptions.SetLineInfo);

    internal static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Kor.Operations.App", "Kor.Operations.App.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root containing Kor.Operations.App.sln.");
    }

    private static bool IsAppXamlPath(string path)
    {
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var separator = Path.DirectorySeparatorChar;
        return !normalized.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains($"{separator}Fixtures{separator}", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToLocalPath(string value) =>
        value.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

    private sealed record ResourceReference(string Kind, string Key, int Line);
}
