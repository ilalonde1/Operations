#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Kor.Operations.App.Opportunities;
using Xunit;

namespace Kor.Operations.App.Tests;

public sealed class UnboundCommandPropertyTests
{
    private static readonly string[] ViewModelSuffixes = ["ViewModel", "Vm"];

    private static readonly string[] PropertyAttributeSkipNameFragments =
    [
        "Browsable",
        "Obsolete",
        "EditorBrowsable"
    ];

    private static readonly object PartialClassCacheLock = new();

    private static readonly Dictionary<string, IReadOnlyDictionary<string, int>> PartialClassCountsByRoot =
        new(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void All_command_properties_have_an_xaml_binding()
    {
        var repoRoot = XamlStaticResourceOrderTests.GetRepoRoot();
        var appRoot = Path.Combine(repoRoot, "Kor.Operations.App");
        var bindingPaths = UnusedViewModelPropertyTests.CollectAllBindingPaths(appRoot);
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

            offences.AddRange(FindUnboundCommandProperties(vmType, bindingPaths, declaringFile));
        }

        offences = offences
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (offences.Count > 0)
        {
            Assert.Fail(
                $"Found {offences.Count} ICommand propertie(s) with no XAML binding:\n"
                + string.Join(Environment.NewLine, offences));
        }
    }

    [Fact]
    public void Analyzer_flags_unbound_command_against_synthetic_vm()
    {
        var bindingPaths = new HashSet<string>(StringComparer.Ordinal)
        {
            "BoundCmd"
        };

        var offences = FindUnboundCommandProperties(typeof(FakeCmdVm), bindingPaths, "");

        var offence = Assert.Single(offences);
        Assert.Contains("UnboundCmd", offence, StringComparison.Ordinal);
        Assert.DoesNotContain(offences, x => x.Contains("BoundCmd", StringComparison.Ordinal));
    }

    public static IReadOnlyList<string> FindUnboundCommandProperties(
        Type vmType,
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
            if (!IsCommandProperty(property) || ShouldSkipCommandProperty(vmType, property))
            {
                continue;
            }

            if (!bindingPathsFromAllXaml.Contains(property.Name))
            {
                offences.Add(
                    $"{Path.GetFileName(vmDeclaringSourcePath)}: ICommand property '{property.Name}' is invoked only from code-behind  no XAML binding found");
            }
        }

        return offences;
    }

    private static bool ShouldSkipViewModelType(Type vmType, string vmDeclaringSourcePath)
    {
        // Unlike the unused-property analyzer, we deliberately do NOT skip
        // IAiContextProvider implementations or partial classes here. The AI
        // context builder never invokes commands — it only reads data
        // properties — so the FP risk is zero. Partial classes can still hold
        // legitimate Command properties whose only invokers are code-behind.
        _ = vmDeclaringSourcePath;
        return vmType.IsInterface
            || vmType.IsAbstract
            || vmType.IsDefined(typeof(SerializableAttribute), inherit: false);
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
        if (sourceRoot is null)
        {
            return false;
        }

        var partialCounts = GetPartialClassCounts(sourceRoot);
        return partialCounts.TryGetValue(vmType.Name, out var count) && count > 0;
    }

    private static IReadOnlyDictionary<string, int> GetPartialClassCounts(string sourceRoot)
    {
        lock (PartialClassCacheLock)
        {
            if (PartialClassCountsByRoot.TryGetValue(sourceRoot, out var cached))
            {
                return cached;
            }

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var pattern = new Regex(
                @"\bpartial\s+class\s+(?<name>[A-Za-z_]\w*)\b",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

            foreach (var path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(IsAppCsPath))
            {
                foreach (Match match in pattern.Matches(File.ReadAllText(path)))
                {
                    var name = match.Groups["name"].Value;
                    counts[name] = counts.TryGetValue(name, out var current) ? current + 1 : 1;
                }
            }

            PartialClassCountsByRoot[sourceRoot] = counts;
            return counts;
        }
    }

    private static bool IsCommandProperty(PropertyInfo property)
    {
        return typeof(ICommand).IsAssignableFrom(property.PropertyType)
            || property.Name.EndsWith("Command", StringComparison.Ordinal);
    }

    private static bool ShouldSkipCommandProperty(Type vmType, PropertyInfo property)
    {
        return IsVirtualAbstractOrOverride(property)
            || IsInterfaceProperty(vmType, property.Name)
            || HasAttributeNameFragment(property.GetCustomAttributes(inherit: true), PropertyAttributeSkipNameFragments);
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

    private static string? FindDeclaringSourceFile(string appRoot, string simpleName)
    {
        var matches = Directory.EnumerateFiles(appRoot, simpleName + ".cs", SearchOption.AllDirectories)
            .Where(IsAppCsPath)
            .Select(Path.GetFullPath)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    private static string? FindSourceRoot(string vmDeclaringSourcePath)
    {
        if (string.IsNullOrWhiteSpace(vmDeclaringSourcePath) || !File.Exists(vmDeclaringSourcePath))
        {
            return null;
        }

        var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(vmDeclaringSourcePath)) ?? Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Kor.Operations.App.csproj")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static bool IsAppCsPath(string path)
    {
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var separator = Path.DirectorySeparatorChar;
        return !normalized.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeCmdVm
    {
        public ICommand BoundCmd { get; } = new NoOpCommand();

        public ICommand UnboundCmd { get; } = new NoOpCommand();
    }

    private sealed class NoOpCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
        }
    }
}
