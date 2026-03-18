#nullable enable
using Kor.EmailSearch.Core;
using Kor.Operations.Data;
using Kor.Operations.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations;

internal static class DataModule
{
    internal static IServiceCollection AddDataServices(this IServiceCollection services)
    {
        var databaseOptions = CompositionHelpers.GetDatabaseOptions();

        services.AddSingleton(databaseOptions);
        services.AddTransient<IEmailMetadataExtractor, BasicEmailMetadataExtractor>();
        services.AddTransient(_ => new SqlFinancialPortfolioSnapshotStore(databaseOptions.KorTransmittalsDb));
        services.AddTransient<IEmailIndexWriter>(sp =>
            new EmailIndexWriter(CompositionHelpers.GetRequiredConnectionString(AppConfigKeys.ConnectionStrings.KorEmailIndex), sp.GetRequiredService<IEmailMetadataExtractor>()));
        services.AddTransient<IEmailSearchService>(_ => new EmailSearchService(CompositionHelpers.GetRequiredConnectionString(AppConfigKeys.ConnectionStrings.KorEmailIndex)));

        services.AddSingleton(sp => ActivatorUtilities.CreateInstance<SqlTransmittalsStore>(sp, databaseOptions.KorTransmittalsDb));
        services.AddSingleton<ITransmittalsStore>(sp => sp.GetRequiredService<SqlTransmittalsStore>());
        services.AddSingleton(sp => new SqlUserPreferencesStore(databaseOptions.KorTransmittalsDb));
        services.AddSingleton<IUserPreferencesStore>(sp => sp.GetRequiredService<SqlUserPreferencesStore>());
        services.AddSingleton(sp => ActivatorUtilities.CreateInstance<PreferencesRepository>(sp, databaseOptions.KorTransmittalsDb));
        services.AddSingleton(_ => new SqlEmailIndexStore(CompositionHelpers.GetRequiredConnectionString(AppConfigKeys.ConnectionStrings.KorEmailIndex)));
        services.AddSingleton(sp => ActivatorUtilities.CreateInstance<VantagepointRepository>(sp, sp.GetRequiredService<VpOdbcDsnFactory>()));

        return services;
    }
}
