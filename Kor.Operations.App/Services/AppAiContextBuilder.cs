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
        if (!_providers.Contains(provider))
            _providers.Add(provider);
    }

    internal void Unregister(IAiContextProvider provider)
    {
        _providers.Remove(provider);
    }

    internal string BuildFullContext(string? localContext = null)
    {
        var sb = new StringBuilder();

        foreach (var provider in _providers.Where(p => p.HasData))
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
