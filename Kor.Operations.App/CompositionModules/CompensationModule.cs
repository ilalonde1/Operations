#nullable enable
using Kor.Operations.App.Options;
using Kor.Operations.Compensation;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations;

internal static class CompensationModule
{
    internal static IServiceCollection AddCompensationServices(this IServiceCollection services)
    {
        services.AddSingleton<CompensationOptions>(_ => CompositionHelpers.GetCompensationOptions());
        services.AddTransient<CompensationService>();
        services.AddTransient<CompensationViewModel>();
        services.AddTransient<CompensationWindow>();
        return services;
    }
}
