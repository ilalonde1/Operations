#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Kor.Operations.Services;

internal sealed class AppAiContextBuilder
{
    private readonly List<IAiContextProvider> _providers = new();

    internal void Register(IAiContextProvider provider)
    {
        _providers.RemoveAll(p => p.ProviderName == provider.ProviderName);
        _providers.Add(provider);
    }

    internal void Unregister(IAiContextProvider provider)
    {
        _providers.Remove(provider);
    }

    private static readonly string[] DefaultProviderNames =
    {
        "Historical Analytics",
        "Financials (Active Projects)",
        "PM Tools (Active Delivery)",
    };

    /// <summary>
    /// Builds the full AI context. <paramref name="expectedProviderNames"/> lets the
    /// caller control which providers appear in the "DATA NOT YET LOADED" advisory.
    /// When null, the default firm-wide provider list is used (backward compatible).
    /// Pass an empty array to suppress the advisory entirely.
    /// </summary>
    internal string BuildFullContext(
        string? localContext = null,
        IReadOnlyList<string>? expectedProviderNames = null)
    {
        var sb = new StringBuilder();

        var loaded = _providers.Where(p => p.HasData).ToList();
        foreach (var provider in loaded)
        {
            sb.AppendLine($"=== {provider.ProviderName.ToUpperInvariant()} ===");
            sb.AppendLine(provider.BuildContext());
            sb.AppendLine();
        }

        var expected = expectedProviderNames ?? DefaultProviderNames;
        if (expected.Count > 0)
        {
            var loadedNames = loaded.Select(p => p.ProviderName).ToHashSet();
            var missing = expected.Where(n => !loadedNames.Contains(n)).ToList();
            if (missing.Count > 0)
            {
                sb.AppendLine("=== DATA NOT YET LOADED ===");
                sb.AppendLine("The following data sources are available but haven't been opened yet this session.");
                sb.AppendLine("If the user asks about data you don't have, tell them which window to open.");
                foreach (var m in missing)
                    sb.AppendLine($"  - {m} (open the {m.Split('(')[0].Trim()} window to load)");
                sb.AppendLine();
            }
        }

        if (!string.IsNullOrWhiteSpace(localContext))
        {
            sb.AppendLine("=== CURRENTLY SELECTED ===");
            sb.AppendLine(localContext);
        }

        return sb.ToString();
    }
}
