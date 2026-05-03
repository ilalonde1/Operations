#nullable enable
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations;

/// <summary>
/// DI registrations for the BD opportunity-pipeline module.
/// Plan: <c>~/.claude/plans/kor-opportunities-architecture-plan.md</c>.
/// <para>
/// Phase 1 (foundation): registers <see cref="App.Options.OpportunitiesOptions"/> only,
/// so the connection string is reachable but no stores/services exist yet. Stores,
/// services, view models and the WPF window register here in subsequent phases.
/// </para>
/// </summary>
internal static class OpportunitiesModule
{
    internal static IServiceCollection AddOpportunitiesServices(this IServiceCollection services)
    {
        var options = CompositionHelpers.GetOpportunitiesOptions();
        services.AddSingleton(options);

        // TODO Phase 1B: register IOpportunityStore + sibling stores (singleton).
        // TODO Phase 1C: register OpportunitiesService (transient).
        // TODO Phase 2:  register OpportunitiesViewModel + OpportunitiesWindow (transient).
        // TODO Phase 3:  register IOpportunityScoringService + IScoringOptionsAccessor (singleton).
        return services;
    }
}
