#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Serilog;

namespace Kor.Operations.Services;

internal sealed class AppAiContextBuilder
{
    private readonly List<IAiContextProvider> _providers = new();
    private readonly object _gate = new();

    internal void Register(IAiContextProvider provider)
    {
        lock (_gate)
        {
            _providers.RemoveAll(p => p.ProviderName == provider.ProviderName);
            _providers.Add(provider);
        }
    }

    internal void Unregister(IAiContextProvider provider)
    {
        lock (_gate)
        {
            _providers.Remove(provider);
        }
    }

    /// <summary>
    /// Concatenates every registered provider's <see cref="IAiContextProvider.BuildContext"/>
    /// output into the prompt suffix sent to Claude. Providers with HasData=false
    /// are skipped; a provider that throws during BuildContext is logged + skipped
    /// so one bad VM cannot blank out the AI bar firm-wide.
    /// </summary>
    /// <remarks>
    /// Restored in Batch 60 (commit pending) after Phase 11e left the AI bar
    /// blind on the Financials window — the builder existed but
    /// <see cref="AppAiService"/> discarded it, and FinancialsViewModel's
    /// BuildLocalContext returned "". See Kor.Operations.Mcp.md for the
    /// architecture decision (push context vs pull via tools).
    /// </remarks>
    internal string BuildFullContext(string? localContext = null)
    {
        var sb = new StringBuilder();

        IAiContextProvider[] snapshot;
        lock (_gate) { snapshot = _providers.ToArray(); }

        foreach (var provider in snapshot)
        {
            bool hasData;
            try { hasData = provider.HasData; }
            catch (Exception ex)
            {
                Log.Warning(ex, "AppAiContextBuilder: provider {Provider} threw on HasData; skipping.", provider.ProviderName);
                continue;
            }
            if (!hasData) continue;

            string body;
            try { body = provider.BuildContext() ?? ""; }
            catch (Exception ex)
            {
                Log.Warning(ex, "AppAiContextBuilder: provider {Provider} threw in BuildContext; skipping.", provider.ProviderName);
                continue;
            }

            sb.AppendLine($"=== {provider.ProviderName.ToUpperInvariant()} ===");
            sb.AppendLine(body);
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
