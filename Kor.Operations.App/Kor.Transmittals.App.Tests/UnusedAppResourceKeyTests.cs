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

public sealed class UnusedAppResourceKeyTests
{
    private static readonly Regex StaticResourceRegex = new(
        @"\{\s*StaticResource\s+(?:ResourceKey\s*=\s*)?([^,\}\s]+)[^}]*\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DynamicResourceRegex = new(
        @"\{\s*DynamicResource\s+(?:ResourceKey\s*=\s*)?([^,\}\s]+)[^}]*\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CsResourceLookupRegex = new(
        """
        (?:
            (?:FindResource|TryFindResource)\s*\([^)]{0,50}?"(?<key>[A-Za-z0-9._-]+)"
            |
            this\s*\[[^\]]{0,50}?"(?<key>[A-Za-z0-9._-]+)"
        )
        """,
        RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void All_App_xaml_resource_keys_have_a_consumer()
    {
        var repoRoot = XamlStaticResourceOrderTests.GetRepoRoot();
        var appRoot = Path.Combine(repoRoot, "Kor.Operations.App");
        var declarations = CollectGlobalResourceKeyDeclarations(appRoot);
        var xamlReferences = CollectXamlResourceReferences(appRoot);
        var csStringLiteralReferences = CollectCsStringLiteralResourceReferences(appRoot);
        var offences = FindUnusedAppResourceKeys(declarations, xamlReferences, csStringLiteralReferences)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (offences.Count > 0)
        {
            Assert.Fail(
                $"Found {offences.Count} App.xaml resource key(s) with no detected consumer:\n"
                + string.Join(Environment.NewLine, offences));
        }
    }

    [Fact]
    public void Analyzer_flags_orphan_resource_key_against_synthetic_dictionary()
    {
        var declarations = new Dictionary<string, (string FilePath, int Line)>(StringComparer.OrdinalIgnoreCase)
        {
            ["AppBg.Used"] = ("UnusedAppResourceKeyBad.xaml", 4),
            ["AppBg.Orphan"] = ("UnusedAppResourceKeyBad.xaml", 5)
        };
        var xamlReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AppBg.Used"
        };

        var offences = FindUnusedAppResourceKeys(
            declarations,
            xamlReferences,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var offence = Assert.Single(offences);
        Assert.Contains("AppBg.Orphan", offence, StringComparison.Ordinal);
        Assert.DoesNotContain(offences, x => x.Contains("AppBg.Used", StringComparison.Ordinal));
    }

    public static IReadOnlyList<string> FindUnusedAppResourceKeys(
        IReadOnlyDictionary<string, (string FilePath, int Line)> declarations,
        IReadOnlySet<string> xamlReferences,
        IReadOnlySet<string> csStringLiteralReferences)
    {
        var offences = new List<string>();

        foreach (var declaration in declarations.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var key = NormalizeResourceKey(declaration.Key);
            if (key.StartsWith("{x:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (xamlReferences.Contains(key) || csStringLiteralReferences.Contains(key))
            {
                continue;
            }

            offences.Add(
                $"{Path.GetFileName(declaration.Value.FilePath)}({declaration.Value.Line}): resource key '{key}' is declared but never referenced anywhere");
        }

        return offences;
    }

    public static IReadOnlyDictionary<string, (string FilePath, int Line)> CollectGlobalResourceKeyDeclarations(
        string appProjectRoot)
    {
        var declarations = new Dictionary<string, (string FilePath, int Line)>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appXamlPath = Path.Combine(appProjectRoot, "App.xaml");

        VisitResourceDictionary(appXamlPath, appProjectRoot, declarations, visited);

        return declarations;
    }

    public static IReadOnlySet<string> CollectXamlResourceReferences(string appRoot)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(appRoot))
        {
            return references;
        }

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

            AddResourceReferences(text, StaticResourceRegex, references);
            AddResourceReferences(text, DynamicResourceRegex, references);
        }

        return references;
    }

    public static IReadOnlySet<string> CollectCsStringLiteralResourceReferences(string appRoot)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(appRoot))
        {
            return references;
        }

        foreach (var csPath in Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(UnusedPrivateMethodTests.IsAppCsPath))
        {
            string text;
            try
            {
                text = File.ReadAllText(csPath);
            }
            catch
            {
                continue;
            }

            foreach (Match match in CsResourceLookupRegex.Matches(text))
            {
                var key = NormalizeResourceKey(match.Groups["key"].Value);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    references.Add(key);
                }
            }
        }

        return references;
    }

    private static void VisitResourceDictionary(
        string xamlPath,
        string appProjectRoot,
        Dictionary<string, (string FilePath, int Line)> declarations,
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

        foreach (var attribute in document
            .Descendants()
            .Attributes()
            .Where(a => string.Equals(a.Name.LocalName, "Key", StringComparison.OrdinalIgnoreCase)))
        {
            var key = NormalizeResourceKey(attribute.Value);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var line = attribute is IXmlLineInfo lineInfo && lineInfo.HasLineInfo()
                ? lineInfo.LineNumber
                : 1;
            declarations.TryAdd(key, (fullPath, line));
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

            VisitResourceDictionary(resolved, appProjectRoot, declarations, visited);
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

    private static void AddResourceReferences(string text, Regex regex, HashSet<string> references)
    {
        foreach (Match match in regex.Matches(text))
        {
            var key = NormalizeResourceKey(Unquote(match.Groups[1].Value));
            if (!string.IsNullOrWhiteSpace(key))
            {
                references.Add(key);
            }
        }
    }

    private static string NormalizeResourceKey(string key)
    {
        var trimmed = Unquote(key.Trim());
        var colonIndex = trimmed.IndexOf(':', StringComparison.Ordinal);
        return colonIndex > 0 ? trimmed[(colonIndex + 1)..] : trimmed;
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2
            && ((trimmed[0] == '\'' && trimmed[^1] == '\'') || (trimmed[0] == '"' && trimmed[^1] == '"'))
            ? trimmed[1..^1]
            : trimmed;
    }

    private static string ToLocalPath(string value)
    {
        return value.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }
}
