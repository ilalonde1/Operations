#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;

namespace Kor.Opportunities.Core.Ingestion;

/// <summary>
/// Ambient warning channel from providers/scrapers back to the ingestion run
/// row. A provider that degrades but survives (e.g. Socrata dropping its
/// server-side $where filter after an HTTP 400) has no return channel for
/// "this run is worse than it looks" — IProvider.FetchAsync returns only
/// candidates, and IngestionRuns.ErrorSummary was exception-only. Flowing an
/// AsyncLocal list through the fetch keeps Success=true honest while making
/// the degradation queryable ("DEGRADED: ..." in ErrorSummary) instead of
/// log-only.
///
/// Scoped per async flow: <see cref="BeginRun"/> before the provider call,
/// <see cref="Drain"/> after. Concurrent ingestion runs each see their own
/// list. <see cref="AddWarning"/> outside a run (e.g. an awards scraper whose
/// service does not call BeginRun) is a safe no-op.
/// </summary>
public static class IngestionRunDiagnostics
{
    private static readonly AsyncLocal<List<string>?> Current = new();

    public static void BeginRun() => Current.Value = new List<string>();

    public static void AddWarning(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var list = Current.Value;
        if (list is null)
        {
            return;
        }

        lock (list)
        {
            list.Add(message.Trim());
        }
    }

    public static IReadOnlyList<string> Drain()
    {
        var list = Current.Value;
        Current.Value = null;
        if (list is null)
        {
            return Array.Empty<string>();
        }

        lock (list)
        {
            return list.ToArray();
        }
    }
}
