#nullable enable
using Kor.EmailSearch.Core;
using Kor.Operations.Core.Services;
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
        services.AddTransient(_ => new SqlFinancialPortfolioSnapshotStore(databaseOptions.KorTransmittalsDb));
        services.AddTransient<IEmailSearchService>(_ => new EmailSearchService(CompositionHelpers.GetRequiredConnectionString(AppConfigKeys.ConnectionStrings.KorEmailIndex)));

        services.AddSingleton(sp => ActivatorUtilities.CreateInstance<SqlTransmittalsStore>(sp, databaseOptions.KorTransmittalsDb));
        services.AddSingleton<ITransmittalsStore>(sp => sp.GetRequiredService<SqlTransmittalsStore>());
        services.AddSingleton(sp => new SqlUserPreferencesStore(databaseOptions.KorTransmittalsDb));
        services.AddSingleton<IUserPreferencesStore>(sp => sp.GetRequiredService<SqlUserPreferencesStore>());
        services.AddSingleton<SqlProposalStaffStore>(_ => new SqlProposalStaffStore(databaseOptions.KorTransmittalsDb));
        services.AddSingleton<IProposalStaffStore>(sp => sp.GetRequiredService<SqlProposalStaffStore>());
        services.AddSingleton<SqlProposalBlockLibraryStore>(_ => new SqlProposalBlockLibraryStore(databaseOptions.KorTransmittalsDb));
        services.AddSingleton<IProposalBlockLibraryStore>(sp => sp.GetRequiredService<SqlProposalBlockLibraryStore>());
        services.AddSingleton<SqlFeeProposalStore>(_ => new SqlFeeProposalStore(databaseOptions.KorTransmittalsDb));
        services.AddSingleton<IFeeProposalStore>(sp => sp.GetRequiredService<SqlFeeProposalStore>());
        services.AddSingleton<SqlBrochureProposalStore>(_ => new SqlBrochureProposalStore(databaseOptions.KorTransmittalsDb));
        services.AddSingleton<IBrochureProposalStore>(sp => sp.GetRequiredService<SqlBrochureProposalStore>());
        services.AddSingleton(sp => ActivatorUtilities.CreateInstance<PreferencesRepository>(sp, databaseOptions.KorTransmittalsDb));
        services.AddSingleton(_ => new SqlEmailIndexStore(CompositionHelpers.GetRequiredConnectionString(AppConfigKeys.ConnectionStrings.KorEmailIndex)));
        services.AddSingleton<IWorkloadMeetingStore>(_ => new SqlWorkloadMeetingStore(databaseOptions.KorTransmittalsDb));
        services.AddSingleton(sp => ActivatorUtilities.CreateInstance<VantagepointRepository>(sp, sp.GetRequiredService<VpOdbcDsnFactory>()));

        return services;
    }
}
