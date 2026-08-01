#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Serilog;

namespace Kor.Operations.Services;

internal sealed class AppAiContextBuilder
{
    // Ref-counted by provider instance. The same singleton VM can be registered
    // by more than one open window (e.g. PmToolsViewModel is shared by
    // WorkloadMeetingWindow + PmCapacityWindow); it must survive in the prompt
    // until the LAST holder unregisters. Before ref-counting, Register deduped
    // by name but Unregister removed by reference, so closing the first window
    // stripped the provider out from under the still-open second one.
    private sealed class Registration
    {
        public IAiContextProvider Provider;
        public int Count;
        public Registration(IAiContextProvider provider) { Provider = provider; Count = 1; }
    }

    private readonly List<Registration> _providers = new();
    private readonly object _gate = new();

    internal void Register(IAiContextProvider provider)
    {
        lock (_gate)
        {
            var existing = _providers.FirstOrDefault(r => r.Provider.ProviderName == provider.ProviderName);
            if (existing is null)
            {
                _providers.Add(new Registration(provider));
            }
            else if (ReferenceEquals(existing.Provider, provider))
            {
                // Same instance held by another window — bump the ref-count.
                existing.Count++;
            }
            else
            {
                // Different instance, same name — a reopened window's fresh VM
                // supersedes the stale one (original by-name dedup contract).
                existing.Provider = provider;
                existing.Count = 1;
            }
        }
    }

    internal void Unregister(IAiContextProvider provider)
    {
        lock (_gate)
        {
            // Match by reference: only the instance that registered decrements,
            // and a stale instance already superseded by a same-named one is a
            // no-op (won't evict the live provider).
            var existing = _providers.FirstOrDefault(r => ReferenceEquals(r.Provider, provider));
            if (existing is null) return;
            if (--existing.Count <= 0)
                _providers.Remove(existing);
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
        lock (_gate) { snapshot = _providers.Select(r => r.Provider).ToArray(); }

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
