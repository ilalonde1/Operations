#nullable enable
using Kor.Operations.App.Opportunities;
using Kor.Operations.App.Options;
using Kor.Opportunities.Data.Heartbeat;
using Kor.Opportunities.Data.Opportunities;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations;

/// <summary>
/// DI registrations for the BD opportunity-pipeline module.
/// Plan: <c>~/.claude/plans/kor-opportunities-architecture-plan.md</c>.
/// </summary>
internal static class OpportunitiesModule
{
    internal static IServiceCollection AddOpportunitiesServices(this IServiceCollection services)
    {
        var options = CompositionHelpers.GetOpportunitiesOptions();
        services.AddSingleton(options);

        // Stores take a plain connection string so Kor.Opportunities.Data stays
        // free of any host-specific Options class. The Worker registers its own
        // copy of these via Kor.Opportunities.Worker; the App registers them
        // here for the WPF feature window.
        services.AddSingleton<IHeartbeatStore>(_ => new SqlHeartbeatStore(options.OpportunitiesDb));
        services.AddSingleton<IOpportunityStore>(_ => new SqlOpportunityStore(options.OpportunitiesDb));

        // Phase 2B: WPF feature window. Both transient so a Close+reopen cycle
        // gets a fresh VM (no stale data from a previous session). Mirrors the
        // FileSync Command Center registration in AppModule.cs.
        services.AddTransient<OpportunitiesViewModel>();
        services.AddTransient<OpportunitiesWindow>();

        // TODO Phase 3: register IOpportunityScoringService + IScoringOptionsAccessor (singleton).
        return services;
    }
}
