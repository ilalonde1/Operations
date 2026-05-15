#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Kor.Opportunities.Core.Ingestion.EmailAdapters;

public sealed class EmailFormatAdapterRegistry
{
    private readonly IReadOnlyList<IEmailFormatAdapter> _specificAdapters;
    private readonly IEmailFormatAdapter _genericFallback;

    public EmailFormatAdapterRegistry(IEnumerable<IEmailFormatAdapter> adapters, GenericEmailFormatAdapter genericFallback)
    {
        // Separate specific adapters from the generic fallback. The fallback is invoked
        // explicitly when no specific adapter matches, so we don't want it appearing in the
        // CanHandle dispatch list.
        _specificAdapters = adapters
            .Where(a => a is not GenericEmailFormatAdapter)
            .ToList();
        _genericFallback = genericFallback;
    }

    /// <summary>
    /// Returns the first specific adapter whose CanHandle matches, or the
    /// generic fallback. Never returns null.
    /// </summary>
    public IEmailFormatAdapter Resolve(string senderAddress)
    {
        foreach (var adapter in _specificAdapters)
        {
            if (adapter.CanHandle(senderAddress))
            {
                return adapter;
            }
        }

        return _genericFallback;
    }
}
