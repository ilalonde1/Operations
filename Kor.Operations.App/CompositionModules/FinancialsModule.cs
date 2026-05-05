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
        services.AddTransient<FinancialsService>();
        services.AddTransient<ProfitLossReportService>();
        services.AddTransient<ExecutiveSummaryDeltekLoader>();
        services.AddTransient<ExecutiveSummaryService>();
        services.AddTransient<ExecutiveSummaryViewModel>();
        services.AddTransient<BillingManagerReportViewModel>();
        services.AddTransient<FinancialsViewModel>();

        return services;
    }
}
