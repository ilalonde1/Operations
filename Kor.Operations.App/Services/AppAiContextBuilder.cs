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

    /// <summary>
    /// Concatenates the registered providers' UI-state snippets into a single
    /// string suitable for the gateway's localContext field. The gateway
    /// queries SQL for everything else, so this is intentionally lightweight:
    /// it should describe what the user is currently looking at, NOT dump
    /// firm-wide data.
    /// </summary>
    internal string BuildFullContext(string? localContext = null)
    {
        var sb = new StringBuilder();

        var loaded = _providers.Where(p => p.HasData).ToList();
        foreach (var provider in loaded)
        {
            sb.AppendLine($"=== {provider.ProviderName.ToUpperInvariant()} ===");
            sb.AppendLine(provider.BuildContext());
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(localContext))
        {
            sb.AppendLine("=== CURRENTLY SELECTED ===");
            sb.AppendLine(localContext);
        }

        return sb.ToString();
    }
}
