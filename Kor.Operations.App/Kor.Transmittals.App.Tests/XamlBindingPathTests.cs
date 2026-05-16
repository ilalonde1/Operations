#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Kor.Operations.App.Opportunities;
using Xunit;

namespace Kor.Operations.App.Tests;

public sealed class XamlBindingPathTests
{
    private static readonly Regex BindingRegex = new(
        @"\{Binding\s+(?:Path\s*=\s*)?(?<path>[A-Za-z_][\w]*)(?:\s*[,}].*)?\}",
        RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TemplateBindingRegex = new(
        @"\{TemplateBinding\s+(?:Path\s*=\s*)?(?<path>[A-Za-z_][\w]*)(?:\s*[,}].*)?\}",
        RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DataContextAssignmentRegex = new(
        @"DataContext\s*=\s*(?<rhs>[^;]+);",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] TemplateAncestorNames =
    [
        "DataTemplate",
        "ItemTemplate",
        "ContentTemplate",
        "CellTemplate",
        "HierarchicalDataTemplate",
        "Style",
        "ControlTemplate",
        "Setter",
        // DataGrid/ListView column definitions live in <DataGrid.Columns> / <GridView.Columns>
        // and bind against the row item (ItemsSource type), not the page VM.
        "DataGridTextColumn",
        "DataGridCheckBoxColumn",
        "DataGridComboBoxColumn",
        "DataGridHyperlinkColumn",
        "DataGridTemplateColumn",
        "GridViewColumn"
    ];

    private static readonly string[] RowScopedPropertyElementSuffixes =
    [
        ".Columns",
        ".RowDetailsTemplate",
        ".CellTemplate",
        ".ElementStyle",
        ".EditingElementStyle"
    ];

    private static readonly string[] BindingSourceTokens =
    [
        "Source",
        "RelativeSource",
        "ElementName",
        "XPath"
    ];

    [Fact]
    public void All_App_xaml_bindings_resolve_against_root_data_context()
    {
        var asm = typeof(OpportunitiesWindow).Assembly;
        var repoRoot = XamlStaticResourceOrderTests.GetRepoRoot();
        var appRoot = Path.Combine(repoRoot, "Kor.Operations.App");
        var offences = Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(IsAppXamlPath)
            .SelectMany(path => FindMissingBindingProperties(path, asm))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (offences.Count > 0)
        {
            Assert.Fail(
                $"Found {offences.Count} XAML Binding path(s) that do not resolve against the root DataContext type:\n"
                + string.Join(Environment.NewLine, offences));
        }
    }

    [Fact]
    public void Analyzer_flags_missing_binding_against_synthetic_vm()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "KorXamlBindingTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var xamlPath = Path.Combine(tempDir, "SyntheticWindow.xaml");
            var codeBehindPath = xamlPath + ".cs";
            File.WriteAllText(xamlPath, """
                <Window
                    x:Class="X.Y"
                    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                    <StackPanel>
                        <TextBlock Text="{Binding ExistingProp}" />
                        <TextBlock Text="{Binding TypoProp}" />
                    </StackPanel>
                </Window>
                """);
            File.WriteAllText(codeBehindPath, """
                namespace X;
                public partial class Y
                {
                    public Y()
                    {
                        DataContext = new FakeVm();
                    }
                }
                """);

            var offences = FindMissingBindingProperties(xamlPath, typeof(FakeVm).Assembly);

            var offence = Assert.Single(offences);
            Assert.Contains("TypoProp", offence, StringComparison.Ordinal);
            Assert.DoesNotContain(offences, x => x.Contains("ExistingProp", StringComparison.Ordinal));
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

    public static IReadOnlyList<string> FindMissingBindingProperties(
        string xamlPath,
        Assembly appAssembly)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(xamlPath, LoadOptions.SetLineInfo);
        }
        catch
        {
            return Array.Empty<string>();
        }

        var root = document.Root;
        if (root is null || root.Attributes().All(a => !string.Equals(a.Name.LocalName, "Class", StringComparison.OrdinalIgnoreCase)))
        {
            return Array.Empty<string>();
        }

        var codeBehindPath = xamlPath + ".cs";
        if (!File.Exists(codeBehindPath))
        {
            return Array.Empty<string>();
        }

        var codeBehind = File.ReadAllText(codeBehindPath);
        var assignments = DataContextAssignmentRegex.Matches(codeBehind).Cast<Match>().ToList();
        if (assignments.Count != 1)
        {
            return Array.Empty<string>();
        }

        var rhs = assignments[0].Groups["rhs"].Value.Trim();
        var dataContextType = ResolveDataContextType(rhs, codeBehind, appAssembly);
        if (dataContextType is null)
        {
            return Array.Empty<string>();
        }

        var members = GetPublicInstanceMemberNames(dataContextType);
        var offences = new List<string>();

        foreach (var binding in EnumerateBindings(document))
        {
            if (ShouldSkipBinding(binding.Element, binding.RawText, binding.Path))
            {
                continue;
            }

            if (!members.Contains(binding.Path))
            {
                offences.Add(
                    $"{Path.GetFileName(xamlPath)}({binding.Line}): {{{binding.Kind} {binding.Path}}}  property '{binding.Path}' not found on {dataContextType.Name}");
            }
        }

        return offences;
    }

    private static Type? ResolveDataContextType(string rhs, string codeBehind, Assembly appAssembly)
    {
        if (rhs.Equals("this", StringComparison.Ordinal)
            || rhs.StartsWith("this.", StringComparison.Ordinal))
        {
            return null;
        }

        var newMatch = Regex.Match(rhs, @"^new\s+(?<vm>\w+)\b", RegexOptions.CultureInvariant);
        if (newMatch.Success)
        {
            return FindTypeBySimpleName(appAssembly, newMatch.Groups["vm"].Value);
        }

        if (!Regex.IsMatch(rhs, @"^_?[A-Za-z_]\w*$", RegexOptions.CultureInvariant))
        {
            return null;
        }

        var ident = rhs.Trim();
        var fieldIdentPattern = ident.StartsWith("_", StringComparison.Ordinal)
            ? Regex.Escape(ident)
            : "_?" + Regex.Escape(ident);
        var fieldMatch = Regex.Match(
            codeBehind,
            $@"(?:private|protected|internal)\s+(?:readonly\s+)?(?<type>\w+(?:<[^>]+>)?\??)\s+{fieldIdentPattern}\s*[;=]",
            RegexOptions.CultureInvariant);
        if (fieldMatch.Success)
        {
            return FindTypeBySimpleName(appAssembly, StripNullableSuffix(fieldMatch.Groups["type"].Value));
        }

        var bareIdent = ident.TrimStart('_');
        var parameterMatch = Regex.Match(
            codeBehind,
            $@"\(\s*[^)]*?\b(?<type>\w+(?:<[^>]+>)?\??)\s+{Regex.Escape(bareIdent)}\b",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);
        if (parameterMatch.Success)
        {
            return FindTypeBySimpleName(appAssembly, StripNullableSuffix(parameterMatch.Groups["type"].Value));
        }

        return null;
    }

    private static Type? FindTypeBySimpleName(Assembly assembly, string simpleName)
    {
        return assembly.GetTypes().FirstOrDefault(t => string.Equals(t.Name, simpleName, StringComparison.Ordinal));
    }

    private static string StripNullableSuffix(string value) =>
        value.EndsWith("?", StringComparison.Ordinal) ? value[..^1] : value;

    private static HashSet<string> GetPublicInstanceMemberNames(Type type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance;

        foreach (var property in type.GetProperties(Flags))
        {
            names.Add(property.Name);
        }

        foreach (var field in type.GetFields(Flags))
        {
            if (!field.Name.StartsWith("<", StringComparison.Ordinal))
            {
                names.Add(field.Name);
            }
        }

        foreach (var method in type.GetMethods(Flags))
        {
            if (!method.IsSpecialName)
            {
                names.Add(method.Name);
            }
        }

        return names;
    }

    private static IEnumerable<BindingReference> EnumerateBindings(XDocument document)
    {
        foreach (var attribute in document.Descendants().Attributes())
        {
            var line = attribute is IXmlLineInfo lineInfo && lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1;
            foreach (var binding in MatchBindingText(attribute.Parent!, attribute.Value, line))
            {
                yield return binding;
            }
        }

        foreach (var element in document.Descendants())
        {
            if (string.Equals(element.Name.LocalName, "Binding", StringComparison.OrdinalIgnoreCase))
            {
                var path = element.Attribute("Path")?.Value;
                var line = element is IXmlLineInfo lineInfo && lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    yield return new BindingReference("Binding", path.Trim(), line, element, "<Binding />");
                }
            }
        }
    }

    private static IEnumerable<BindingReference> MatchBindingText(XElement element, string text, int line)
    {
        foreach (Match match in BindingRegex.Matches(text))
        {
            if (match.Success)
            {
                yield return new BindingReference("Binding", match.Groups["path"].Value.Trim(), line, element, match.Value);
            }
        }

        foreach (Match match in TemplateBindingRegex.Matches(text))
        {
            if (match.Success)
            {
                yield return new BindingReference("TemplateBinding", match.Groups["path"].Value.Trim(), line, element, match.Value);
            }
        }
    }

    private static bool ShouldSkipBinding(XElement element, string rawText, string path)
    {
        if (IsInsideSkippedAncestor(element)
            || HasAncestorDataContext(element)
            || HasSourceToken(rawText)
            || HasSourceAttribute(element)
            || HasUnsupportedPathShape(path)
            || IsBindingPropertyElement(element)
            || IsInsideBindingWrapper(element))
        {
            return true;
        }

        return false;
    }

    private static bool IsInsideSkippedAncestor(XElement element)
    {
        foreach (var ancestor in element.AncestorsAndSelf())
        {
            var localName = ancestor.Name.LocalName;
            if (TemplateAncestorNames.Contains(localName, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(localName, "ItemsControl.ItemTemplate", StringComparison.OrdinalIgnoreCase)
                || string.Equals(localName, "DataGrid.RowDetailsTemplate", StringComparison.OrdinalIgnoreCase)
                || string.Equals(localName, "ListView.View", StringComparison.OrdinalIgnoreCase)
                || string.Equals(localName, "GridViewColumn.CellTemplate", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Property-element forms like <DataGrid.Columns>, <DataGridTextColumn.ElementStyle>,
            // <DataGridTemplateColumn.CellTemplate> are row-scoped or template-scoped.
            if (RowScopedPropertyElementSuffixes.Any(suffix =>
                    localName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAncestorDataContext(XElement element)
    {
        return element.Ancestors().Any(a => a.Attributes().Any(attr =>
            string.Equals(attr.Name.LocalName, "DataContext", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool HasSourceToken(string rawText)
    {
        return BindingSourceTokens.Any(token =>
            Regex.IsMatch(rawText, $@"\b{token}\s*=", RegexOptions.CultureInvariant));
    }

    private static bool HasSourceAttribute(XElement element)
    {
        return BindingSourceTokens.Any(token => element.Attributes().Any(attr =>
            string.Equals(attr.Name.LocalName, token, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool HasUnsupportedPathShape(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            || path.Equals(".", StringComparison.Ordinal)
            || path.Contains('.', StringComparison.Ordinal)
            || path.Contains('[', StringComparison.Ordinal)
            || path.Contains('/', StringComparison.Ordinal);
    }

    private static bool IsBindingPropertyElement(XElement element) =>
        element.Name.LocalName.StartsWith("Binding.", StringComparison.OrdinalIgnoreCase);

    private static bool IsInsideBindingWrapper(XElement element)
    {
        return element.AncestorsAndSelf().Any(a =>
            string.Equals(a.Name.LocalName, "MultiBinding", StringComparison.OrdinalIgnoreCase)
            || string.Equals(a.Name.LocalName, "PriorityBinding", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAppXamlPath(string path)
    {
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var separator = Path.DirectorySeparatorChar;
        return !normalized.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains($"{separator}Fixtures{separator}", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record BindingReference(string Kind, string Path, int Line, XElement Element, string RawText);

    private sealed class FakeVm
    {
        public string ExistingProp => "";
    }
}
