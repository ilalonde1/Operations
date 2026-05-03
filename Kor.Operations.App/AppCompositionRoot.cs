#nullable enable
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations
{
    internal static class AppCompositionRoot
    {
        internal static IServiceProvider BuildServiceProvider()
        {
            var services = new ServiceCollection();
            services
                .AddGraphServices()
                .AddFinancialsServices()
                .AddCompensationServices()
                .AddDataServices()
                .AddRenderingServices()
                .AddOpportunitiesServices()
                .AddAppServices();

            return services.BuildServiceProvider();
        }
    }
}
