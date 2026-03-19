#nullable enable
using Kor.Operations.Core.Services;
using Kor.Operations.Rendering;
using Kor.Operations.Rendering.Brochure;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations;

internal static class RenderingModule
{
    internal static IServiceCollection AddRenderingServices(this IServiceCollection services)
    {
        var storageOptions = CompositionHelpers.GetStorageOptions();

        services.AddTransient(typeof(CoverSheetRenderer), _ => throw new NotSupportedException("CoverSheetRenderer is static and is not constructed through DI."));
        services.AddTransient<IBrochureRenderer, BrochureRenderer>();
        services.AddTransient<IBrochureProposalStore>(_ =>
        {
            var stores = new List<IBrochureProposalStore>
            {
                new BrochureProposalStore()
            };

            if (!string.IsNullOrWhiteSpace(storageOptions.BrochureSharedProposalsRootPath))
            {
                stores.Add(new BrochureProposalStore(storageOptions.BrochureSharedProposalsRootPath.Trim()));
            }

            return stores.Count == 1
                ? stores[0]
                : new CompositeBrochureProposalStore(stores);
        });

        return services;
    }
}
