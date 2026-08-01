#nullable enable
using System;
using System.Collections.Generic;
using Kor.Operations.App.Options;
using Kor.Operations.Data;
using Kor.Operations.Financials;
using Kor.Operations.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations;

internal static class FinancialsModule
{
    internal static IServiceCollection AddFinancialsServices(this IServiceCollection services)
    {
        var deltekOdbcOptions = CompositionHelpers.GetDeltekOdbcOptions();
        var financialsOptions = CompositionHelpers.GetFinancialsOptions();

        services.AddSingleton(deltekOdbcOptions);
        services.AddSingleton(financialsOptions);
        services.AddSingleton<VpOdbcDsnFactory>(_ =>
        {
            var dsn = string.IsNullOrWhiteSpace(deltekOdbcOptions.Dsn) ? "Deltek" : deltekOdbcOptions.Dsn;
            return new VpOdbcDsnFactory(dsn, deltekOdbcOptions.User, deltekOdbcOptions.Password, () => new Dictionary<string, string>());
        });
        services.AddTransient<GlProfitLossService>();
        services.AddTransient<BilledFinancialsService>();
        services.AddTransient(sp => new FinancialsService(
            sp.GetRequiredService<DeltekOdbcOptions>(),
            sp.GetRequiredService<FinancialsOptions>(),
            // Same delegate used by ExecutiveSummaryService — provider is
            // registered below, both consumers reuse the closure.
            sp.GetService<ActiveCollectionsInvoiceProvider>()));
        services.AddTransient(sp => new ExecutiveSummaryDeltekLoader(sp.GetRequiredService<DeltekOdbcOptions>(), sp.GetRequiredService<FinancialsOptions>()));
        // Phase 12-5a: bridge ExecutiveSummaryService → CollectionsClient via
        // a delegate so the public service stays decoupled from the internal
        // client type. When MCP isn't configured, GetActiveCaseInvoicesAsync
        // returns an empty list and the tile degrades silently.
        services.AddTransient<ActiveCollectionsInvoiceProvider>(sp => async ct =>
        {
            var client = sp.GetService<CollectionsClient>();
            if (client is null || !client.IsConfigured)
            {
                return null;
            }

            var rows = await client.GetActiveCaseInvoicesAsync(ct).ConfigureAwait(false);
            return new HashSet<(string Wbs1, string Invoice)>(
                rows.Select(r => ((r.wbS1 ?? string.Empty).Trim(), (r.invoiceNumber ?? string.Empty).Trim())));
        });
        services.AddTransient<ExecutiveSummaryService>();
        services.AddTransient<ExecutiveSummaryViewModel>();
        services.AddTransient<PartnerFinancialsViewModel>();
        services.AddTransient<FinancialsViewModel>();

        return services;
    }
}
