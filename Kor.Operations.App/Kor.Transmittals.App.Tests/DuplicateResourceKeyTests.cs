#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace Kor.Operations.App.Tests;

public sealed class DuplicateResourceKeyTests
{
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void No_duplicate_resource_keys_across_global_dictionaries()
    {
        var repoRoot = XamlStaticResourceOrderTests.GetRepoRoot();
        var appRoot = Path.Combine(repoRoot, "Kor.Operations.App");
        var globalFiles = BuildGlobalResourceDictionaryFileSet(appRoot);
        var declarations = CollectResourceKeyDeclarations(appRoot);
        var filteredKeys = declarations
            .GroupBy(d => d.Key, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Where(g => g.Any(d => globalFiles.Contains(d.FullPath)))
            .ToDictionary(
                g => g.Key,
                g => g
                    .Select(d => (d.DisplayPath, d.Line))
                    .ToList(),
                StringComparer.Ordinal);

        var offences = FindDuplicateResourceKeys(filteredKeys)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (offences.Count > 0)
        {
            Assert.Fail(
                $"Found {offences.Count} duplicate XAML resource key(s):\n"
                + string.Join(Environment.NewLine, offences));
        }
    }

    [Fact]
    public void Analyzer_flags_duplicate_key_against_synthetic_files()
    {
        var keysByName = new Dictionary<string, List<(string FilePath, int Line)>>(StringComparer.Ordinal)
        {
            ["DupKey.Test"] =
            [
                ("DuplicateResourceKey_FileA.xaml", 3),
                ("DuplicateResourceKey_FileB.xaml", 3)
            ]
        };

        var offences = FindDuplicateResourceKeys(keysByName);

        var offence = Assert.Single(offences);
        Assert.Contains("DupKey.Test", offence, StringComparison.Ordinal);
        Assert.Contains("DuplicateResourceKey_FileA.xaml", offence, StringComparison.Ordinal);
        Assert.Contains("DuplicateResourceKey_FileB.xaml", offence, StringComparison.Ordinal);
    }

    public static IReadOnlyList<string> FindDuplicateResourceKeys(
        IReadOnlyDictionary<string, List<(string FilePath, int Line)>> keysByName)
    {
        var offences = new List<string>();

        foreach (var kv in keysByName.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (kv.Value.Count <= 1)
            {
                continue;
            }

            var locations = kv.Value
                .OrderBy(v => v.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(v => v.Line)
                .Select(v => $"{NormalizeDisplayPath(v.FilePath)}({v.Line})");
            offences.Add(
                $"resource key '{kv.Key}' is declared {kv.Value.Count} times: {string.Join(", ", locations)}");
        }

        return offences;
    }

    private static IReadOnlyList<ResourceKeyDeclaration> CollectResourceKeyDeclarations(string appRoot)
    {
        var declarations = new List<ResourceKeyDeclaration>();
        if (!Directory.Exists(appRoot))
        {
            return declarations;
        }

        foreach (var xamlPath in Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(XamlStaticResourceOrderTests.IsAppXamlPath))
        {
            XDocument document;
            try
            {
                document = XDocument.Load(xamlPath, LoadOptions.SetLineInfo);
            }
            catch (XmlException ex)
            {
                Debug.WriteLine($"Skipping unparsable XAML file '{xamlPath}': {ex.Message}");
                continue;
            }

            var fullPath = Path.GetFullPath(xamlPath);
            var displayPath = Path.GetRelativePath(appRoot, fullPath);
            foreach (var attribute in document
                .Descendants()
                .Attributes()
                .Where(a => string.Equals(a.Name.LocalName, "Key", StringComparison.Ordinal)
                    && string.Equals(a.Name.NamespaceName, XamlNamespace, StringComparison.Ordinal)))
            {
                var key = attribute.Value.Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var line = attribute is IXmlLineInfo lineInfo && lineInfo.HasLineInfo()
                    ? lineInfo.LineNumber
                    : 1;
                declarations.Add(new ResourceKeyDeclaration(key, fullPath, displayPath, line));
            }
        }

        return declarations;
    }

    private static IReadOnlySet<string> BuildGlobalResourceDictionaryFileSet(string appProjectRoot)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appXamlPath = Path.Combine(appProjectRoot, "App.xaml");

        VisitResourceDictionary(appXamlPath, appProjectRoot, files);

        return files;
    }

    private static void VisitResourceDictionary(
        string xamlPath,
        string appProjectRoot,
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

            VisitResourceDictionary(resolved, appProjectRoot, visited);
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

    private static string NormalizeDisplayPath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string ToLocalPath(string value)
    {
        return value.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private sealed record ResourceKeyDeclaration(string Key, string FullPath, string DisplayPath, int Line);
}
