#nullable enable
using System;
using Kor.Operations.App.Options;
using Kor.Operations.Core;
using Kor.Operations.Graph;
using Kor.Operations.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph;

namespace Kor.Operations;

internal static class GraphModule
{
    internal static IServiceCollection AddGraphServices(this IServiceCollection services)
    {
        var graphOptions = CompositionHelpers.GetGraphOptions();

        services.AddSingleton(graphOptions);
        services.AddSingleton<GraphAuthenticationState>();
        services.AddSingleton<ISecurityGroupAccess, SecurityGroupAccessAdapter>();
        services.AddSingleton<IAuthorizationService, AuthorizationService>();
        services.AddSingleton<IGraphFacade>(sp =>
        {
            var authState = sp.GetRequiredService<GraphAuthenticationState>();
            var authenticationProvider = authState.AuthenticationProvider
                ?? throw new InvalidOperationException("Graph authentication provider has not been initialized.");
            var options = sp.GetRequiredService<GraphOptions>();
            return new GraphFacade(new GraphServiceClient(authenticationProvider), options.DriveId);
        });
        services.AddTransient<IRecipientResolver, RecipientResolver>();

        return services;
    }
}
