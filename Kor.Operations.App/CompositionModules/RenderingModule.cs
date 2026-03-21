#nullable enable
using Kor.Operations.Rendering;
using Kor.Operations.Rendering.Brochure;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations;

internal static class RenderingModule
{
    internal static IServiceCollection AddRenderingServices(this IServiceCollection services)
    {
        services.AddTransient(typeof(CoverSheetRenderer), _ => throw new NotSupportedException("CoverSheetRenderer is static and is not constructed through DI."));
        services.AddTransient<IBrochureRenderer, BrochureRenderer>();
        services.AddTransient<IBrochureDocxRenderer, BrochureDocxRenderer>();

        return services;
    }
}
